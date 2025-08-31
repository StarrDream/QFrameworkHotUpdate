using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QHotUpdateSystem.Logging;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.EventsSystem;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.Persistence;
using QHotUpdateSystem.Download;
using QHotUpdateSystem.Security;
using QHotUpdateSystem.State;
using System.IO;
using UnityEngine;
using System.Linq;
using QHotUpdateSystem.BundleEvents;
using QHotUpdateSystem.Events;

namespace QHotUpdateSystem
{
    /// <summary>
    /// 热更新管理器（含 Bundle 依赖补全会话）
    /// </summary>
    public class HotUpdateManager
    {
        private static HotUpdateManager _instance;
        public static HotUpdateManager Instance => _instance ?? (_instance = new HotUpdateManager());

        private HotUpdateContext _context;
        private LocalStorage _storage;
        private DownloadManager _downloadManager;
        private bool _initialized;
        private bool _coreReady;

        private IVersionSignatureVerifier _signatureVerifier;
        private bool _enableSignatureVerify;

        private const double TempPartMaxAgeHours = 24;

        private HotUpdateManager()
        {
        }

        public bool IsInitialized => _initialized;
        public bool IsCoreReady => _coreReady;
        public DownloadState DownloadState => _downloadManager?.Controller.StateMachine.Current ?? DownloadState.Idle;

        public void ConfigureSignatureVerifier(IVersionSignatureVerifier verifier, bool enable)
        {
            _signatureVerifier = verifier;
            _enableSignatureVerify = enable;
            if (_downloadManager != null)
                _downloadManager.VersionVerifier = verifier;
        }

        // ================================
        // 会话管理结构
        // ================================
        private class BundleDownloadSession
        {
            public Guid Id;
            public HashSet<string> RootBundles;
            public HashSet<string> ClosureBundles;
            public HashSet<string> Modules;
            public long TotalBytes;
            public long DownloadedBytes;
            public bool Completed;
            public bool Failed;
            public string FailedModule;
            public TaskCompletionSource<bool> Tcs;
            public string Key;
        }

        private readonly Dictionary<Guid, BundleDownloadSession> _bundleSessions = new Dictionary<Guid, BundleDownloadSession>();
        private readonly Dictionary<string, BundleDownloadSession> _bundleSessionByKey = new Dictionary<string, BundleDownloadSession>(StringComparer.OrdinalIgnoreCase);
        private readonly object _bundleSessionLock = new object();

        private readonly Dictionary<string, Task<Guid?>> _bundleEnsureTasks = new Dictionary<string, Task<Guid?>>(StringComparer.OrdinalIgnoreCase);

        public async Task Initialize(Core.HotUpdateInitOptions opt)
        {
            if (_initialized) return;
            _initialized = true;

            HotUpdateLogger.EnableDebug = opt.EnableDebugLog;
            _context = new Core.HotUpdateContext(opt);
            _storage = new LocalStorage(_context.PlatformAdapter);

            int cleaned = _storage.CleanExpiredTemps(TempPartMaxAgeHours);
            if (cleaned > 0)
                HotUpdateLogger.Info($"Clean expired temp parts: {cleaned}");

            _context.LocalVersion = VersionLoader.LoadLocal(_context.PlatformAdapter.GetLocalVersionFilePath(), _context.JsonSerializer)
                                    ?? new VersionInfo
                                    {
                                        version = "0",
                                        platform = _context.PlatformAdapter.GetPlatformName(),
                                        timestamp = Utility.TimeUtility.UnixTimeSeconds(),
                                        modules = new ModuleInfo[0]
                                    };

            var remoteUrl = _context.PlatformAdapter.GetRemoteVersionFileUrl(opt.BaseUrl);
            _context.RemoteVersion = await VersionLoader.LoadRemote(remoteUrl, _context.JsonSerializer);
            if (_context.RemoteVersion != null && _enableSignatureVerify && _signatureVerifier != null)
            {
                var rawSign = _context.RemoteVersion.sign;
                var cacheSign = _context.RemoteVersion.sign;
                _context.RemoteVersion.sign = "";
                var serialized = _context.JsonSerializer.Serialize(_context.RemoteVersion, false);
                _context.RemoteVersion.sign = cacheSign;

                if (!_signatureVerifier.Verify(serialized, rawSign))
                    HotUpdateLogger.Error("Version signature verify failed!");
                else
                    HotUpdateLogger.Info("Version signature verified.");
            }

            HotUpdateEvents.InvokeRemoteVersion(_context.RemoteVersion);
            PrepareModuleStates();
            PrepareBundleMaps();
            _downloadManager = new DownloadManager(_context, _storage);
            _downloadManager.VersionVerifier = _signatureVerifier;

            // 订阅模块事件，用于会话进度
            HotUpdateEvents.OnModuleProgress += OnModuleProgressForSessions;
            HotUpdateEvents.OnModuleStatusChanged += OnModuleStatusChangedForSessions;

            if (NeedCoreUpdate(out _, out _))
            {
                HotUpdateLogger.Info("Core module needs update, auto starting...");
                await StartCoreUpdate();
            }
            else
            {
                _coreReady = true;
                HotUpdateEvents.InvokeCoreReady();
            }
        }

        public async Task StartCoreUpdate(Action onComplete = null)
        {
            if (!NeedCoreUpdate(out _, out _))
            {
                _coreReady = true;
                HotUpdateEvents.InvokeCoreReady();
                onComplete?.Invoke();
                return;
            }

            var tcs = new TaskCompletionSource<bool>();

            void Handler(string module, ModuleStatus status)
            {
                if (module == "Core" && (status == ModuleStatus.Updated || status == ModuleStatus.Failed))
                {
                    HotUpdateEvents.OnModuleStatusChanged -= Handler;
                    tcs.TrySetResult(status == ModuleStatus.Updated);
                }
            }

            HotUpdateEvents.OnModuleStatusChanged += Handler;
            await _downloadManager.DownloadModuleAsync("Core", Core.DownloadPriority.Critical);

            bool success = await tcs.Task;
            _coreReady = success;
            HotUpdateEvents.InvokeCoreReady();
            onComplete?.Invoke();
        }

        public async Task UpdateModules(IEnumerable<string> modules, Core.DownloadPriority priority = Core.DownloadPriority.Normal)
        {
            foreach (var m in modules)
            {
                if (!_context.ModuleStates.ContainsKey(m))
                {
                    HotUpdateLogger.Warn("Module not found: " + m);
                    continue;
                }

                await _downloadManager.DownloadModuleAsync(m, priority);
            }
        }

        public void PauseModule(string module) => _downloadManager?.PauseModule(module);
        public void ResumeModule(string module) => _downloadManager?.ResumeModule(module);
        public void CancelModule(string module) => _downloadManager?.CancelModule(module);
        public void CancelAll() => _downloadManager?.CancelAll();

        public ModuleStatus GetModuleStatus(string module)
        {
            if (_context != null && _context.ModuleStates.TryGetValue(module, out var st))
                return st.Status;
            return ModuleStatus.NotInstalled;
        }

        public IEnumerable<string> GetInstalledModules()
        {
            foreach (var kv in _context.ModuleStates)
                if (kv.Value.Status == ModuleStatus.Installed || kv.Value.Status == ModuleStatus.Updated)
                    yield return kv.Key;
        }

        /// <summary>
        /// 获取更新信息（包含大小统计）
        /// </summary>
        public UpdateInfo GetUpdateInfo()
        {
            if (!_initialized || _context.RemoteVersion == null)
                return new UpdateInfo { IsUpdateAvailable = false };

            var updateInfo = new UpdateInfo();
            var modulesToUpdate = new List<ModuleUpdateInfo>();
            long totalSize = 0;

            foreach (var remoteModule in _context.RemoteVersion.modules)
            {
                var localModule = FindModule(_context.LocalVersion, remoteModule.name);

                if (localModule == null || localModule.aggregateHash != remoteModule.aggregateHash)
                {
                    var changedFiles = VersionComparer.GetChangedFiles(remoteModule, localModule);
                    long moduleSize = 0;

                    foreach (var file in changedFiles)
                    {
                        long fileSize = file.compressed && file.cSize > 0 ? file.cSize : file.size;
                        moduleSize += fileSize;
                    }

                    modulesToUpdate.Add(new ModuleUpdateInfo
                    {
                        ModuleName = remoteModule.name,
                        IsMandatory = remoteModule.mandatory,
                        UpdateSize = moduleSize,
                        FileCount = changedFiles.Count,
                        TotalFileCount = remoteModule.fileCount
                    });

                    totalSize += moduleSize;
                }
            }

            updateInfo.IsUpdateAvailable = modulesToUpdate.Count > 0;
            updateInfo.TotalUpdateSize = totalSize;
            updateInfo.ModulesToUpdate = modulesToUpdate.ToArray();
            updateInfo.RemoteVersion = _context.RemoteVersion.version;
            updateInfo.LocalVersion = _context.LocalVersion?.version ?? "0";

            return updateInfo;
        }

        /// <summary>
        /// 获取指定模块的更新大小
        /// </summary>
        public long GetModuleUpdateSize(string moduleName)
        {
            if (!_initialized || _context.RemoteVersion == null)
                return 0;

            var remoteModule = FindModule(_context.RemoteVersion, moduleName);
            var localModule = FindModule(_context.LocalVersion, moduleName);

            if (remoteModule == null || (localModule != null && localModule.aggregateHash == remoteModule.aggregateHash))
                return 0;

            var changedFiles = VersionComparer.GetChangedFiles(remoteModule, localModule);
            long totalSize = 0;

            foreach (var file in changedFiles)
            {
                long fileSize = file.compressed && file.cSize > 0 ? file.cSize : file.size;
                totalSize += fileSize;
            }

            return totalSize;
        }

        public async Task<long> GetBundleUpdateSize(string[] bundleNames)
        {
            var manager = HotUpdateManager.Instance;
            if (!manager.IsInitialized) return 0;

            // 解析Bundle依赖
            HashSet<string> closure = manager._context.BundleResolver?.GetClosure(bundleNames)
                                      ?? new HashSet<string>(bundleNames, StringComparer.OrdinalIgnoreCase);

            // 获取相关模块
            var modules = new HashSet<string>();
            foreach (var bundle in closure)
            {
                if (manager._context.BundleToModule.TryGetValue(bundle, out var module))
                    modules.Add(module);
            }

            // 计算模块更新大小
            long totalSize = 0;
            foreach (var module in modules)
            {
                totalSize += manager.GetModuleUpdateSize(module);
            }

            return totalSize;
        }

        /// <summary>
        /// 旧接口：无会话事件
        /// </summary>
        public async Task EnsureBundlesDownloaded(IEnumerable<string> bundleNames, Core.DownloadPriority priority = Core.DownloadPriority.Normal)
        {
            if (!_initialized) throw new InvalidOperationException("HotUpdate not initialized");
            if (bundleNames == null) return;

            var inputList = new List<string>(bundleNames);
            if (inputList.Count == 0) return;

            HashSet<string> closure = (_context.BundleResolver != null)
                ? _context.BundleResolver.GetClosure(inputList)
                : new HashSet<string>(inputList, StringComparer.OrdinalIgnoreCase);

            var needModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in closure)
            {
                if (_context.BundleToModule.TryGetValue(b, out var mod))
                    needModules.Add(mod);
                else
                    HotUpdateLogger.Warn("Bundle not mapped to module: " + b);
            }

            var toDownload = new List<string>();
            foreach (var m in needModules)
            {
                var st = GetModuleStatus(m);
                if (st == ModuleStatus.Installed || st == ModuleStatus.Updated)
                    continue;
                toDownload.Add(m);
            }

            if (toDownload.Count > 0)
                await UpdateModules(toDownload, priority);
        }

        /// <summary>
        /// 新接口：带会话与事件。返回 SessionId；若无需下载返回 null。
        /// </summary>
        public async Task<Guid?> EnsureBundlesDownloadedSessionAsync(IEnumerable<string> bundleNames,
            Core.DownloadPriority priority = Core.DownloadPriority.Normal)
        {
            if (!_initialized) throw new InvalidOperationException("HotUpdate not initialized");
            if (bundleNames == null) return null;
            var roots = bundleNames.Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (roots.Count == 0) return null;

            roots.Sort(StringComparer.OrdinalIgnoreCase);
            var key = string.Join("|", roots);

            // ★ 修复：不能在 lock 内 await；这里只取引用
            Task<Guid?> existingTask = null;
            lock (_bundleSessionLock)
            {
                _bundleEnsureTasks.TryGetValue(key, out existingTask);
            }

            if (existingTask != null)
                return await existingTask;

            var newTask = InternalEnsureBundlesDownloadedSessionAsync(roots, key, priority);
            lock (_bundleSessionLock)
            {
                _bundleEnsureTasks[key] = newTask;
            }

            try
            {
                return await newTask;
            }
            finally
            {
                lock (_bundleSessionLock)
                {
                    _bundleEnsureTasks.Remove(key);
                }
            }
        }

        private async Task<Guid?> InternalEnsureBundlesDownloadedSessionAsync(List<string> roots, string key,
            Core.DownloadPriority priority)
        {
            HashSet<string> closure = (_context.BundleResolver != null)
                ? _context.BundleResolver.GetClosure(roots)
                : new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);

            var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in closure)
            {
                if (_context.BundleToModule.TryGetValue(b, out var mod))
                    modules.Add(mod);
            }

            if (modules.Count == 0)
            {
                foreach (var r in roots)
                    if (_context.BundleToModule.TryGetValue(r, out var m))
                        modules.Add(m);
            }

            var needDownload = new List<string>();
            bool allReady = true;
            foreach (var m in modules)
            {
                var st = GetModuleStatus(m);
                if (st == ModuleStatus.Installed || st == ModuleStatus.Updated)
                    continue;
                if (st == ModuleStatus.Updating || st == ModuleStatus.Partial || st == ModuleStatus.NotInstalled)
                {
                    needDownload.Add(m);
                    allReady = false;
                }
            }

            if (allReady)
                return null;

            BundleDownloadSession session = new BundleDownloadSession
            {
                Id = Guid.NewGuid(),
                RootBundles = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase),
                ClosureBundles = new HashSet<string>(closure, StringComparer.OrdinalIgnoreCase),
                Modules = new HashSet<string>(modules, StringComparer.OrdinalIgnoreCase),
                TotalBytes = 0,
                DownloadedBytes = 0,
                Key = key,
                Tcs = new TaskCompletionSource<bool>()
            };
            lock (_bundleSessionLock)
            {
                _bundleSessions[session.Id] = session;
                _bundleSessionByKey[key] = session;
            }

            session.TotalBytes = EstimateTotalBytes(session.Modules);

            BundleDownloadEvents.InvokeStart(new BundleDownloadStartInfo
            {
                SessionId = session.Id,
                RootBundles = session.RootBundles.ToArray(),
                ClosureBundles = session.ClosureBundles.ToArray(),
                Modules = session.Modules.ToArray(),
                TotalBytes = session.TotalBytes
            });

            foreach (var m in needDownload)
            {
                var stNow = GetModuleStatus(m);
                if (stNow == ModuleStatus.Installed || stNow == ModuleStatus.Updated)
                    continue;
                await _downloadManager.DownloadModuleAsync(m, priority);
            }

            RecalculateSessionProgress(session.Id);

            bool success = await session.Tcs.Task;
            return session.Id;
        }

        private long EstimateTotalBytes(IEnumerable<string> modules)
        {
            long total = 0;
            if (_context.RemoteVersion?.modules == null) return 0;
            foreach (var m in modules)
            {
                var info = FindModule(_context.RemoteVersion, m);
                if (info != null)
                    total += (info.compressedSizeBytes > 0 ? info.compressedSizeBytes : info.sizeBytes);
            }

            return total;
        }

        // 模块事件驱动会话更新
        private void OnModuleProgressForSessions(string module, ModuleProgressInfo info)
        {
            List<Guid> affected = null;
            lock (_bundleSessionLock)
            {
                foreach (var kv in _bundleSessions)
                {
                    if (!kv.Value.Completed && !kv.Value.Failed && kv.Value.Modules.Contains(module))
                    {
                        affected ??= new List<Guid>();
                        affected.Add(kv.Key);
                    }
                }
            }

            if (affected == null) return;
            foreach (var sid in affected)
                RecalculateSessionProgress(sid);
        }

        private void OnModuleStatusChangedForSessions(string module, ModuleStatus status)
        {
            List<Guid> affected = null;
            lock (_bundleSessionLock)
            {
                foreach (var kv in _bundleSessions)
                {
                    if (!kv.Value.Completed && !kv.Value.Failed && kv.Value.Modules.Contains(module))
                    {
                        affected ??= new List<Guid>();
                        affected.Add(kv.Key);
                    }
                }
            }

            if (affected == null) return;

            foreach (var sid in affected)
            {
                if (status == ModuleStatus.Failed)
                    FailSession(sid, module, $"模块 {module} 下载失败");
                else
                {
                    if (RecalculateSessionProgress(sid))
                        TryCompleteSessionIfAllModulesReady(sid);
                }
            }
        }

        private bool RecalculateSessionProgress(Guid sessionId)
        {
            BundleDownloadSession session;
            lock (_bundleSessionLock)
            {
                if (!_bundleSessions.TryGetValue(sessionId, out session)) return false;
                if (session.Completed || session.Failed) return false;
            }

            long total = 0;
            long downloaded = 0;

            foreach (var m in session.Modules)
            {
                if (!_context.ModuleStates.TryGetValue(m, out var st)) continue;

                if (st.Status == ModuleStatus.Updating || st.Status == ModuleStatus.Partial)
                {
                    long moduleTotal = st.TotalBytes;
                    long moduleDownloaded = st.DownloadedBytes;
                    if (moduleTotal <= 0)
                    {
                        var remote = FindModule(_context.RemoteVersion, m);
                        if (remote != null)
                            moduleTotal = (remote.compressedSizeBytes > 0 ? remote.compressedSizeBytes : remote.sizeBytes);
                    }

                    total += moduleTotal;
                    downloaded += Math.Min(moduleDownloaded, moduleTotal);
                }
                else if (st.Status == ModuleStatus.Installed || st.Status == ModuleStatus.Updated)
                {
                    var remote = FindModule(_context.RemoteVersion, m);
                    long moduleTotal = 0;
                    if (remote != null)
                        moduleTotal = (remote.compressedSizeBytes > 0 ? remote.compressedSizeBytes : remote.sizeBytes);
                    total += moduleTotal;
                    downloaded += moduleTotal;
                }
                else if (st.Status == ModuleStatus.Failed)
                {
                    FailSession(sessionId, m, $"模块 {m} 标记失败");
                    return false;
                }
                else
                {
                    var remote = FindModule(_context.RemoteVersion, m);
                    if (remote != null)
                        total += (remote.compressedSizeBytes > 0 ? remote.compressedSizeBytes : remote.sizeBytes);
                }
            }

            if (downloaded > total && total > 0) downloaded = total;

            session.TotalBytes = total;
            session.DownloadedBytes = downloaded;

            BundleDownloadEvents.InvokeProgress(new BundleDownloadProgressInfo
            {
                SessionId = session.Id,
                DownloadedBytes = session.DownloadedBytes,
                TotalBytes = session.TotalBytes
            });

            return true;
        }

        private void TryCompleteSessionIfAllModulesReady(Guid sessionId)
        {
            BundleDownloadSession session;
            lock (_bundleSessionLock)
            {
                if (!_bundleSessions.TryGetValue(sessionId, out session)) return;
                if (session.Completed || session.Failed) return;
            }

            foreach (var m in session.Modules)
            {
                var st = GetModuleStatus(m);
                if (st != ModuleStatus.Installed && st != ModuleStatus.Updated)
                    return;
            }

            CompleteSession(sessionId);
        }

        private void CompleteSession(Guid sessionId)
        {
            BundleDownloadSession session;
            lock (_bundleSessionLock)
            {
                if (!_bundleSessions.TryGetValue(sessionId, out session)) return;
                if (session.Completed || session.Failed) return;
                session.Completed = true;
                _bundleSessionByKey.Remove(session.Key);
            }

            session.Tcs.TrySetResult(true);

            BundleDownloadEvents.InvokeProgress(new BundleDownloadProgressInfo
            {
                SessionId = session.Id,
                DownloadedBytes = session.DownloadedBytes,
                TotalBytes = session.TotalBytes
            });

            BundleDownloadEvents.InvokeCompleted(new BundleDownloadResultInfo
            {
                SessionId = session.Id,
                Success = true,
                Modules = session.Modules.ToArray(),
                FailedModule = null,
                Message = "OK"
            });
        }

        private void FailSession(Guid sessionId, string failedModule, string message)
        {
            BundleDownloadSession session;
            lock (_bundleSessionLock)
            {
                if (!_bundleSessions.TryGetValue(sessionId, out session)) return;
                if (session.Completed || session.Failed) return;
                session.Failed = true;
                session.FailedModule = failedModule;
                _bundleSessionByKey.Remove(session.Key);
            }

            session.Tcs.TrySetResult(false);

            BundleDownloadEvents.InvokeFailed(new BundleDownloadResultInfo
            {
                SessionId = session.Id,
                Success = false,
                Modules = session.Modules.ToArray(),
                FailedModule = failedModule,
                Message = message
            });
        }

        // ========== Bundle 便捷接口 ==========
        public bool IsBundleReady(string bundleName)
        {
            if (!_initialized || string.IsNullOrEmpty(bundleName)) return false;
            var path = GetBundlePath(bundleName);
            if (!File.Exists(path)) return false;

            if (_context.BundleToModule.TryGetValue(bundleName, out var module))
            {
                var st = GetModuleStatus(module);
                return st == ModuleStatus.Installed || st == ModuleStatus.Updated;
            }

            return false;
        }

        public async Task<AssetBundle> LoadBundleAsync(string bundleName)
        {
            if (string.IsNullOrEmpty(bundleName))
                throw new ArgumentException("bundleName 不能为空");
            if (!_initialized)
                throw new InvalidOperationException("HotUpdateManager 未初始化");

            var path = GetBundlePath(bundleName);
            if (!File.Exists(path))
                throw new InvalidOperationException("尝试加载的 Bundle 文件不存在: " + bundleName);

            var req = AssetBundle.LoadFromFileAsync(path);
            while (!req.isDone)
                await Task.Yield();

            if (req.assetBundle == null)
                throw new Exception("AssetBundle 加载失败: " + bundleName);

            return req.assetBundle;
        }

        private string GetBundlePath(string bundleName) => _storage.GetAssetPath(bundleName);

        // ========== 初始化辅助 ==========
        private void PrepareModuleStates()
        {
            _context.ModuleStates.Clear();
            var localMap = new Dictionary<string, ModuleInfo>();
            if (_context.LocalVersion.modules != null)
                foreach (var m in _context.LocalVersion.modules)
                    localMap[m.name] = m;

            if (_context.RemoteVersion?.modules != null)
            {
                foreach (var rm in _context.RemoteVersion.modules)
                {
                    var state = new Core.ModuleRuntimeState { ModuleName = rm.name };
                    if (localMap.TryGetValue(rm.name, out var lm))
                        state.Status = (lm.aggregateHash == rm.aggregateHash) ? ModuleStatus.Installed : ModuleStatus.Partial;
                    else
                        state.Status = ModuleStatus.NotInstalled;
                    _context.ModuleStates[rm.name] = state;
                }
            }
            else
            {
                if (_context.LocalVersion.modules != null)
                    foreach (var lm in _context.LocalVersion.modules)
                        _context.ModuleStates[lm.name] = new Core.ModuleRuntimeState { ModuleName = lm.name, Status = ModuleStatus.Installed };
            }
        }

        private void PrepareBundleMaps()
        {
            _context.BundleToModule.Clear();
            if (_context.RemoteVersion?.modules != null)
            {
                foreach (var m in _context.RemoteVersion.modules)
                {
                    if (m.files == null) continue;
                    foreach (var f in m.files)
                    {
                        if (string.IsNullOrEmpty(f.name)) continue;
                        _context.BundleToModule[f.name] = m.name;
                    }
                }
            }

            _context.BundleResolver = new Dependency.BundleDependencyResolver(_context.RemoteVersion?.bundleDeps);
        }

        private bool NeedCoreUpdate(out ModuleInfo remoteCore, out ModuleInfo localCore)
        {
            remoteCore = FindModule(_context.RemoteVersion, "Core");
            localCore = FindModule(_context.LocalVersion, "Core");
            if (remoteCore == null) return false;
            if (localCore == null) return true;
            return remoteCore.aggregateHash != localCore.aggregateHash;
        }

        private ModuleInfo FindModule(VersionInfo ver, string name)
        {
            if (ver?.modules == null) return null;
            foreach (var m in ver.modules)
                if (m.name == name)
                    return m;
            return null;
        }
    }
} // 更新信息数据结构

public class UpdateInfo
{
    public bool IsUpdateAvailable;
    public long TotalUpdateSize;
    public string RemoteVersion;
    public string LocalVersion;
    public ModuleUpdateInfo[] ModulesToUpdate;
}

public class ModuleUpdateInfo
{
    public string ModuleName;
    public bool IsMandatory;
    public long UpdateSize;
    public int FileCount;
    public int TotalFileCount;
}