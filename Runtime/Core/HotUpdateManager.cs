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

namespace QHotUpdateSystem
{
    /// <summary>
    /// 热更新管理器
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

        // 批次2: 可调的过期清理策略（简单常量，后续可放到配置中）
        private const double TempPartMaxAgeHours = 24; // 超过 24 小时未完成的临时文件清理

        private HotUpdateManager() { }

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

        public async Task Initialize(Core.HotUpdateInitOptions opt)
        {
            if (_initialized) return;
            _initialized = true;

            HotUpdateLogger.EnableDebug = opt.EnableDebugLog;
            _context = new Core.HotUpdateContext(opt);
            _storage = new LocalStorage(_context.PlatformAdapter);

            // 批次2: 先清理过期临时文件，避免旧数据影响续传判定
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
                {
                    HotUpdateLogger.Error("Version signature verify failed!");
                }
                else
                {
                    HotUpdateLogger.Info("Version signature verified.");
                }
            }

            HotUpdateEvents.InvokeRemoteVersion(_context.RemoteVersion);
            PrepareModuleStates();
            _downloadManager = new DownloadManager(_context, _storage);
            _downloadManager.VersionVerifier = _signatureVerifier;

            if (NeedCoreUpdate(out var remoteCore, out var localCore))
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
            if (!NeedCoreUpdate(out var remoteCore, out var localCore))
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
            await _downloadManager.DownloadModuleAsync("Core", DownloadPriority.Critical);

            bool success = await tcs.Task;
            _coreReady = success;
            HotUpdateEvents.InvokeCoreReady();
            onComplete?.Invoke();
        }

        public async Task UpdateModules(IEnumerable<string> modules, DownloadPriority priority = DownloadPriority.Normal)
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
                    {
                        state.Status = (lm.aggregateHash == rm.aggregateHash)
                            ? ModuleStatus.Installed
                            : ModuleStatus.Partial;
                    }
                    else
                        state.Status = ModuleStatus.NotInstalled;
                    _context.ModuleStates[rm.name] = state;
                }
            }
            else
            {
                if (_context.LocalVersion.modules != null)
                    foreach (var lm in _context.LocalVersion.modules)
                        _context.ModuleStates[lm.name] = new Core.ModuleRuntimeState
                        {
                            ModuleName = lm.name,
                            Status = ModuleStatus.Installed
                        };
            }
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
                if (m.name == name) return m;
            return null;
        }
    }
}
