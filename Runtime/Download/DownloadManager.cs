using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Persistence;
using QHotUpdateSystem.Logging;
using QHotUpdateSystem.EventsSystem;
using QHotUpdateSystem.Events;
using QHotUpdateSystem.Security;
using QHotUpdateSystem.Compression;
using QHotUpdateSystem.Download;
using QHotUpdateSystem.State;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 下载管理器（加入断点续传逻辑）
    /// </summary>
    public class DownloadManager
    {
        private readonly HotUpdateContext _context;
        private readonly LocalStorage _storage;
        private readonly DownloadController _controller;

        private readonly PriorityQueue<DownloadTask> _queue = new PriorityQueue<DownloadTask>();
        private readonly List<DownloadTask> _running = new List<DownloadTask>();
        private readonly List<DownloadTask> _completed = new List<DownloadTask>();
        private readonly List<DownloadTask> _queuedTasks = new List<DownloadTask>();

        private readonly SpeedMeter _globalSpeed = new SpeedMeter();
        private long _globalTotalBytes;
        private long _globalDownloadedBytes;
        private bool _loopActive;

        private readonly HashSet<string> _activeTempFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DownloadTask> _pendingByTemp = new Dictionary<string, DownloadTask>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DownloadTask, List<DownloadTask>> _mergedFollowers = new Dictionary<DownloadTask, List<DownloadTask>>();

        public IVersionSignatureVerifier VersionVerifier;
        public bool IsBusy => _loopActive;
        public DownloadController Controller => _controller;

        public DownloadManager(HotUpdateContext ctx, LocalStorage storage, DownloadController controller = null)
        {
            _context = ctx;
            _storage = storage;
            _controller = controller ?? new DownloadController();
        }

        #region Public API

        public async Task DownloadModuleAsync(string moduleName, DownloadPriority priority = DownloadPriority.Normal)
        {
            var remoteModule = FindModule(_context.RemoteVersion, moduleName);
            if (remoteModule == null)
            {
                HotUpdateLogger.Warn("Remote module missing: " + moduleName);
                HotUpdateEvents.InvokeModuleStatus(moduleName, ModuleStatus.Failed);
                return;
            }
            var localModule = FindModule(_context.LocalVersion, moduleName);
            var changed = VersionComparer.GetChangedFiles(remoteModule, localModule);

            if (changed.Count == 0)
            {
                MarkModuleInstalled(remoteModule);
                return;
            }

            var state = _context.ModuleStates[moduleName];
            state.Status = ModuleStatus.Updating;
            state.ResetProgress();

            var safeFiles = changed.Where(f => FileNameValidator.IsSafeRelativeName(f.name)).ToList();
            state.TotalFiles = safeFiles.Count;
            state.TotalBytes = safeFiles.Sum(f => f.compressed ? f.cSize : f.size);

            // 全局总字节：加入本模块（后续再减去 resume 已有部分）
            _globalTotalBytes += state.TotalBytes;
            HotUpdateEvents.InvokeModuleStatus(moduleName, ModuleStatus.Updating);

            foreach (var f in safeFiles)
            {
                string temp = _storage.GetTempFile(moduleName, f.name, f.hash);
                string final = _storage.GetAssetPath(f.name);
                string remoteUrl = _context.PlatformAdapter.GetRemoteAssetFileUrl(_context.Options.BaseUrl, f.name);

                long expected = f.compressed ? f.cSize : f.size;
                long existing = 0;

                if (File.Exists(temp))
                {
                    try
                    {
                        var fi = new FileInfo(temp);
                        existing = fi.Length;
                        if (existing < 0 || existing > expected)
                        {
                            existing = 0;
                            TryDelete(temp);
                        }
                    }
                    catch { existing = 0; }
                }

                // Meta 校验：若不匹配 hash/algo/size 则丢弃
                var metaPath = _storage.GetResumeMetaPath(moduleName, f.name, f.hash);
                if (existing > 0)
                {
                    if (DownloadResumeMeta.TryLoad(metaPath, out var meta))
                    {
                        bool metaOk = meta.hash == f.hash
                                      && meta.size == expected
                                      && string.Equals(meta.algo, _context.Options.HashAlgo, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(meta.remoteUrl, remoteUrl, StringComparison.OrdinalIgnoreCase);
                        if (!metaOk)
                        {
                            existing = 0;
                            TryDelete(temp);
                            TryDelete(metaPath);
                        }
                    }
                    else
                    {
                        existing = 0;
                        TryDelete(temp);
                    }
                }

                if (existing > 0)
                {
                    // 统计逻辑：全局 & 模块下载进度预先加
                    state.DownloadedBytes += existing;
                    _globalDownloadedBytes += existing;
                }

                var task = new DownloadTask
                {
                    Module = moduleName,
                    File = f,
                    RemoteUrl = remoteUrl,
                    TempPath = temp,
                    FinalPath = final,
                    TotalBytes = expected,
                    ExistingBytes = existing,
                    Priority = priority,
                    SupportResume = true,
                    ResumeMetaPath = metaPath
                };

                EnqueueTask(task);
            }

            if (!_loopActive)
                _ = ProcessLoop();

            await Task.Yield();
        }

        public void PauseModule(string module)
        {
            _controller.PauseModule(module);
            ExtendedDownloadEvents.InvokeModulePaused(module);
            _controller.StateMachine.TryTransition(DownloadState.Paused);
        }

        public void ResumeModule(string module)
        {
            _controller.ResumeModule(module);
            ExtendedDownloadEvents.InvokeModuleResumed(module);
            if (_controller.StateMachine.Current == DownloadState.Paused)
                _controller.StateMachine.TryTransition(DownloadState.Running);
            if (!_loopActive && (_queuedTasks.Count > 0 || _running.Count > 0))
                _ = ProcessLoop();
        }

        public void CancelModule(string module)
        {
            _controller.CancelModule(module);
            ExtendedDownloadEvents.InvokeModuleCanceled(module);
            _controller.StateMachine.TryTransition(DownloadState.Canceling);
        }

        public void CancelAll()
        {
            _controller.CancelAll();
            ExtendedDownloadEvents.InvokeAllCanceled();
            _controller.StateMachine.TryTransition(DownloadState.Canceling);
        }

        #endregion

        #region Queue / Loop

        private void EnqueueTask(DownloadTask task)
        {
            if (_pendingByTemp.TryGetValue(task.TempPath, out var existingPrimary))
            {
                HotUpdateLogger.Info($"Merge duplicate task into primary: {task.File.name} temp={task.TempPath}");
                if (!_mergedFollowers.TryGetValue(existingPrimary, out var list))
                {
                    list = new List<DownloadTask>();
                    _mergedFollowers[existingPrimary] = list;
                }
                list.Add(task);
                task.State = DownloadTaskState.Queued;
                return;
            }

            _pendingByTemp[task.TempPath] = task;
            _queue.Enqueue((int)task.Priority, task);
            _queuedTasks.Add(task);
        }

        private async Task ProcessLoop()
        {
            if (_loopActive) return;
            _loopActive = true;
            _controller.StateMachine.TryTransition(DownloadState.Preparing);
            _controller.StateMachine.TryTransition(DownloadState.Running);

            try
            {
                while (true)
                {
                    if (_controller.StateMachine.Current == DownloadState.Canceling)
                    {
                        CleanupCancelFlags();
                        _controller.StateMachine.TryTransition(DownloadState.Idle);
                        break;
                    }

                    bool idle = _queue.Count == 0 && _running.Count == 0;
                    if (idle) break;

                    bool allPaused = AreAllQueuedModulesPaused() && _running.Count == 0;
                    if (allPaused)
                    {
                        await Task.Delay(200);
                        continue;
                    }

                    while (_running.Count < _context.Options.MaxConcurrent && _queue.Count > 0)
                    {
                        var next = _queue.Dequeue();
                        _queuedTasks.Remove(next);
                        if (_controller.IsModuleCanceled(next.Module))
                        {
                            next.State = DownloadTaskState.Canceled;
                            ExtendedDownloadEvents.InvokeModuleCanceled(next.Module);
                            MarkPrimaryAndFollowersFailedOrCanceled(next, canceled: true);
                            continue;
                        }
                        if (_controller.IsModulePaused(next.Module))
                        {
                            next.Priority = (DownloadPriority)Math.Max((int)next.Priority - 1, (int)DownloadPriority.Low);
                            EnqueueTask(next);
                            break;
                        }

                        _running.Add(next);
                        _ = RunTaskAsync(next);
                    }

                    await Task.Delay(50);
                }
            }
            finally
            {
                if (_queue.Count == 0 && _running.Count == 0)
                {
                    _controller.StateMachine.TryTransition(DownloadState.Completed);
                    HotUpdateEvents.InvokeAllTasksCompleted();
                }
                _loopActive = false;
            }
        }

        private bool AreAllQueuedModulesPaused()
        {
            if (_queuedTasks.Count == 0) return true;
            foreach (var t in _queuedTasks)
                if (!_controller.IsModulePaused(t.Module))
                    return false;
            return true;
        }

        #endregion

        #region Task Execution

        private async Task RunTaskAsync(DownloadTask task)
        {
            task.State = DownloadTaskState.Running;

            if (!_activeTempFiles.Add(task.TempPath))
            {
                HotUpdateLogger.Warn("Temp already active (race) -> skipping: " + task.TempPath);
                task.State = DownloadTaskState.Canceled;
                FinishTask(task);
                return;
            }

            // 若已有部分且 meta 不存在，生成 meta
            if (task.SupportResume && task.ExistingBytes > 0 && !File.Exists(task.ResumeMetaPath))
            {
                var meta = DownloadResumeMeta.Create(task.File, task.RemoteUrl, _context.Options.HashAlgo);
                meta.Save(task.ResumeMetaPath);
            }

            int maxRetry = _context.Options.MaxRetry;
            for (; task.RetryCount <= maxRetry; task.RetryCount++)
            {
                if (_controller.IsModuleCanceled(task.Module))
                {
                    task.State = DownloadTaskState.Canceled;
                    MarkPrimaryAndFollowersFailedOrCanceled(task, canceled: true);
                    break;
                }

                long sessionStartExisting = task.ExistingBytes;
                long sessionIncr = 0;

                bool ok = await HttpDownloader.DownloadFile(
                    task,
                    task.RemoteUrl,
                    task.TempPath,
                    new HttpDownloader.DownloadOptions
                    {
                        TimeoutSec = _context.Options.TimeoutSeconds,
                        OnDelta = delta =>
                        {
                            task.DownloadedBytes += delta;
                            sessionIncr += delta;
                            var moduleState = _context.ModuleStates[task.Module];
                            moduleState.DownloadedBytes += delta;
                            _globalDownloadedBytes += delta;
                            _globalSpeed.AddSample(delta);
                            EmitProgress(task, moduleState);
                            return true;
                        },
                        ShouldAbort = () => _controller.ShouldAbort(task.Module, _controller.GlobalToken),
                        OnPauseWait = () => _controller.WaitIfPaused(task.Module, _controller.GlobalToken),
                        SupportResume = task.SupportResume,
                        ExistingBytes = task.ExistingBytes,
                        ExpectedTotal = task.TotalBytes,
                        RemoteUrl = task.RemoteUrl
                    });

                if (ok)
                {
                    task.ExistingBytes += sessionIncr; // 已写入部分变成新的 existing
                    // 每轮成功后立即写 meta（如果支持）
                    if (task.SupportResume)
                    {
                        var meta = DownloadResumeMeta.Create(task.File, task.RemoteUrl, _context.Options.HashAlgo);
                        meta.Save(task.ResumeMetaPath);
                    }

                    if (task.ExistingBytes >= task.TotalBytes)
                    {
                        if (await FinalizeFile(task))
                        {
                            task.State = DownloadTaskState.Completed;
                            var moduleState = _context.ModuleStates[task.Module];
                            moduleState.CompletedFiles++;
                            EmitProgress(task, moduleState);
                            PropagateFollowersSuccess(task);
                            break;
                        }
                        else
                        {
                            task.State = DownloadTaskState.Failed;
                        }
                    }
                }
                else
                {
                    task.State = DownloadTaskState.Failed;
                }

                if (_controller.IsModuleCanceled(task.Module))
                {
                    task.State = DownloadTaskState.Canceled;
                    MarkPrimaryAndFollowersFailedOrCanceled(task, canceled: true);
                    break;
                }

                if (task.State == DownloadTaskState.Failed && task.RetryCount < maxRetry)
                {
                    await RetryPolicy.DelayForRetry(task.RetryCount);
                }
                else if (task.State == DownloadTaskState.Failed)
                {
                    var moduleState = _context.ModuleStates[task.Module];
                    moduleState.FailedFiles++;
                    moduleState.LastError = task.LastError;
                    HotUpdateEvents.InvokeError(task.Module, $"File {task.File.name} failed: {task.LastError}");
                    PropagateFollowersFailure(task);
                }
            }

            FinishTask(task);
            CheckModuleComplete(task.Module);
        }

        private void FinishTask(DownloadTask task)
        {
            _running.Remove(task);
            _completed.Add(task);
            _activeTempFiles.Remove(task.TempPath);
            _pendingByTemp.Remove(task.TempPath);
            _mergedFollowers.Remove(task);
        }

        private async Task<bool> FinalizeFile(DownloadTask task)
        {
            try
            {
                if (!FileNameValidator.IsSafeRelativeName(task.File.name))
                {
                    task.LastError = "Unsafe file name (rejected)";
                    return false;
                }

                if (!FileNameValidator.IsPathWithinRoot(_storage.AssetDirFullPath, task.FinalPath))
                {
                    task.LastError = "Target path outside asset root";
                    return false;
                }

                // 如果最终文件已存在且 hash 已匹配，可直接成功
                if (File.Exists(task.FinalPath))
                {
                    using (var existStream = File.OpenRead(task.FinalPath))
                    {
                        var existingHash = HashUtility.ComputeStream(existStream, _context.Options.HashAlgo);
                        if (string.Equals(existingHash, task.File.hash, StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(task.TempPath))
                                SafeDelete(task.TempPath);
                            TryDelete(task.ResumeMetaPath);
                            return true;
                        }
                    }
                    SafeDelete(task.FinalPath);
                }

                // 如果临时文件大小与期望不符则失败（可能被服务器截断）
                var tempLen = new FileInfo(task.TempPath).Length;
                if (tempLen != task.TotalBytes)
                {
                    task.LastError = $"Temp length mismatch expect={task.TotalBytes} got={tempLen}";
                    return false;
                }

                if (task.IsCompressed)
                {
                    var comp = CompressorRegistry.Get(task.File.algo);
                    if (comp == null)
                    {
                        task.LastError = "No compressor: " + task.File.algo;
                        return false;
                    }
                    if (!comp.Decompress(task.TempPath, task.FinalPath, out var derr))
                    {
                        task.LastError = "Decompress fail: " + derr;
                        return false;
                    }
                }
                else
                {
                    if (File.Exists(task.FinalPath))
                        SafeDelete(task.FinalPath);
                    File.Move(task.TempPath, task.FinalPath);
                }

                using (var fs = File.OpenRead(task.FinalPath))
                {
                    var hash = HashUtility.ComputeStream(fs, _context.Options.HashAlgo);
                    if (!string.Equals(hash, task.File.hash, StringComparison.OrdinalIgnoreCase))
                    {
                        task.LastError = $"Hash mismatch expect={task.File.hash} got={hash}";
                        SafeDelete(task.FinalPath);
                        if (task.IsCompressed && File.Exists(task.TempPath))
                            SafeDelete(task.TempPath);
                        return false;
                    }
                }

                if (File.Exists(task.TempPath))
                    SafeDelete(task.TempPath);
                TryDelete(task.ResumeMetaPath);

                task.OnCompleted?.Invoke(task);
                return true;
            }
            catch (Exception e)
            {
                task.LastError = e.Message;
                return false;
            }
            finally
            {
                await Task.Yield();
            }
        }

        private void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                HotUpdateLogger.Warn("SafeDelete failed: " + path + " err=" + e.Message);
            }
        }

        #endregion

        #region Followers

        private void PropagateFollowersSuccess(DownloadTask primary)
        {
            if (!_mergedFollowers.TryGetValue(primary, out var list)) return;
            foreach (var follower in list)
            {
                follower.State = DownloadTaskState.Completed;
                follower.DownloadedBytes = primary.DownloadedBytes;
                follower.TotalBytes = primary.TotalBytes;
                follower.LastError = null;

                if (_context.ModuleStates.TryGetValue(follower.Module, out var mState))
                {
                    mState.CompletedFiles++;
                    // follower 的 ExistingBytes 和 primary 一样，不重复统计
                }
            }
            _mergedFollowers.Remove(primary);
        }

        private void PropagateFollowersFailure(DownloadTask primary)
        {
            if (!_mergedFollowers.TryGetValue(primary, out var list)) return;
            foreach (var follower in list)
            {
                follower.State = DownloadTaskState.Failed;
                follower.LastError = primary.LastError;
                if (_context.ModuleStates.TryGetValue(follower.Module, out var mState))
                {
                    mState.FailedFiles++;
                    mState.LastError = primary.LastError;
                }
            }
            _mergedFollowers.Remove(primary);
        }

        private void MarkPrimaryAndFollowersFailedOrCanceled(DownloadTask primary, bool canceled)
        {
            if (_mergedFollowers.TryGetValue(primary, out var list))
            {
                foreach (var follower in list)
                {
                    follower.State = canceled ? DownloadTaskState.Canceled : DownloadTaskState.Failed;
                    if (_context.ModuleStates.TryGetValue(follower.Module, out var mState))
                    {
                        if (!canceled) mState.FailedFiles++;
                    }
                }
                _mergedFollowers.Remove(primary);
            }
        }

        #endregion

        #region Progress / Completion (保持原逻辑)

        private void EmitProgress(DownloadTask task, ModuleRuntimeState moduleState)
        {
            var fInfo = new FileProgressInfo
            {
                Module = task.Module,
                FileName = task.File.name,
                Downloaded = task.ExistingBytes + task.DownloadedBytes,
                Total = task.TotalBytes,
                Speed = _globalSpeed.GetSpeed()
            };
            HotUpdateEvents.InvokeFileProgress(task.Module, fInfo);

            var mInfo = new ModuleProgressInfo
            {
                Module = task.Module,
                DownloadedBytes = moduleState.DownloadedBytes,
                TotalBytes = moduleState.TotalBytes,
                CompletedFiles = moduleState.CompletedFiles,
                TotalFiles = moduleState.TotalFiles,
                Speed = _globalSpeed.GetSpeed()
            };
            HotUpdateEvents.InvokeModuleProgress(task.Module, mInfo);

            var gInfo = new GlobalProgressInfo
            {
                DownloadedBytes = _globalDownloadedBytes,
                TotalBytes = _globalTotalBytes,
                Speed = _globalSpeed.GetSpeed()
            };
            HotUpdateEvents.InvokeGlobalProgress(gInfo);
        }

        private void CheckModuleComplete(string module)
        {
            if (!_context.ModuleStates.TryGetValue(module, out var mState)) return;
            if (mState.CompletedFiles + mState.FailedFiles == mState.TotalFiles)
            {
                if (mState.FailedFiles == 0)
                {
                    var remoteModule = FindModule(_context.RemoteVersion, module);
                    VersionWriter.UpsertModule(_context.LocalVersion, remoteModule);
                    VersionLoader.SaveLocal(_context.PlatformAdapter.GetLocalVersionFilePath(), _context.LocalVersion, _context.JsonSerializer);
                    mState.Status = ModuleStatus.Updated;
                    HotUpdateEvents.InvokeModuleStatus(module, ModuleStatus.Updated);
                }
                else
                {
                    mState.Status = ModuleStatus.Failed;
                    HotUpdateEvents.InvokeModuleStatus(module, ModuleStatus.Failed);
                }
                _controller.ClearModuleFlags(module);
            }
        }

        private void MarkModuleInstalled(ModuleInfo remoteModule)
        {
            VersionWriter.UpsertModule(_context.LocalVersion, remoteModule);
            VersionLoader.SaveLocal(_context.PlatformAdapter.GetLocalVersionFilePath(), _context.LocalVersion, _context.JsonSerializer);
            var state = _context.ModuleStates[remoteModule.name];
            state.Status = ModuleStatus.Installed;
            HotUpdateEvents.InvokeModuleStatus(remoteModule.name, ModuleStatus.Installed);
        }

        #endregion

        #region Helpers

        private ModuleInfo FindModule(VersionInfo v, string name)
        {
            if (v?.modules == null) return null;
            foreach (var m in v.modules)
                if (m.name == name) return m;
            return null;
        }

        private void CleanupCancelFlags()
        {
            // TODO: 可在此处清理未完成的 .part (策略: 保留用于 resume)
        }

        private void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        #endregion
    }
}
