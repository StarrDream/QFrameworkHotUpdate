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
    public class DownloadManager
    {
        private readonly HotUpdateContext _context;
        private readonly LocalStorage _storage;
        private readonly DownloadController _controller;

        private readonly PriorityQueue<DownloadTask> _queue = new PriorityQueue<DownloadTask>();
        private readonly List<DownloadTask> _running = new List<DownloadTask>();
        private readonly List<DownloadTask> _completed = new List<DownloadTask>();

        private readonly SpeedMeter _globalSpeed = new SpeedMeter();
        private long _globalTotalBytes;
        private long _globalDownloadedBytes;
        private bool _loopActive;

        // 新增：防重复 / 合并
        private readonly HashSet<string> _activeTempFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // tempPath -> 主任务
        private readonly Dictionary<string, DownloadTask> _pendingByTemp = new Dictionary<string, DownloadTask>(StringComparer.OrdinalIgnoreCase);
        // 主任务 -> 附属（合并）任务列表
        private readonly Dictionary<DownloadTask, List<DownloadTask>> _mergedFollowers = new Dictionary<DownloadTask, List<DownloadTask>>();

        public IVersionSignatureVerifier VersionVerifier; // 远端版本加载后的可选签名校验

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
            state.TotalFiles = changed.Count;
            state.TotalBytes = changed.Sum(f => f.compressed ? f.cSize : f.size);
            _globalTotalBytes += state.TotalBytes;
            HotUpdateEvents.InvokeModuleStatus(moduleName, ModuleStatus.Updating);

            foreach (var f in changed)
            {
                // (批次3会提供的新重载) 唯一 tempPath：module + file + hash
                string temp = _storage.GetTempFile(moduleName, f.name, f.hash);  // 若未应用批次3, 临时改回 _storage.GetTempFile(f.name)
                string final = _storage.GetAssetPath(f.name);
                string remoteUrl = _context.PlatformAdapter.GetRemoteAssetFileUrl(_context.Options.BaseUrl, f.name);

                var task = new DownloadTask
                {
                    Module = moduleName,
                    File = f,
                    RemoteUrl = remoteUrl,
                    TempPath = temp,
                    FinalPath = final,
                    TotalBytes = f.compressed ? f.cSize : f.size,
                    Priority = priority
                };
                EnqueueTask(task);
            }

            if (!_loopActive)
                _ = ProcessLoop(); // fire & forget

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
            // 去重逻辑：tempPath 作为唯一键（含 module/hash 后碰撞概率极低）
            if (_pendingByTemp.TryGetValue(task.TempPath, out var existingPrimary))
            {
                HotUpdateLogger.Info($"Merge duplicate task into primary: {task.File.name} temp={task.TempPath}");
                // 将该任务标记为 follower
                if (!_mergedFollowers.TryGetValue(existingPrimary, out var list))
                {
                    list = new List<DownloadTask>();
                    _mergedFollowers[existingPrimary] = list;
                }
                list.Add(task);
                task.State = DownloadTaskState.Queued; // 逻辑上不单独入队
                return;
            }

            _pendingByTemp[task.TempPath] = task;
            _queue.Enqueue((int)task.Priority, task);
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

                    // 暂停判定（当前实现保留原逻辑）
                    bool allPaused = _queue.Count == 0 && _running.Count == 0
                        || (_running.Count == 0 && _queue.Count > 0 && AreAllQueuedModulesPaused());
                    if (allPaused)
                    {
                        await Task.Delay(200);
                        continue;
                    }

                    // 启动新任务
                    while (_running.Count < _context.Options.MaxConcurrent && _queue.Count > 0)
                    {
                        var next = _queue.Dequeue();
                        if (_controller.IsModuleCanceled(next.Module))
                        {
                            next.State = DownloadTaskState.Canceled;
                            ExtendedDownloadEvents.InvokeModuleCanceled(next.Module);
                            MarkPrimaryAndFollowersFailedOrCanceled(next, canceled: true);
                            continue;
                        }
                        if (_controller.IsModulePaused(next.Module))
                        {
                            // 降权后重新排队
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

        private bool AreAllQueuedModulesPaused() => false; // 维持原实现

        #endregion

        #region Task Execution

        private async Task RunTaskAsync(DownloadTask task)
        {
            task.State = DownloadTaskState.Running;

            // 占用 tempPath，避免并发
            if (!_activeTempFiles.Add(task.TempPath))
            {
                // 理论上不应发生（因为 temp 去重），若发生说明竞态
                HotUpdateLogger.Warn("Temp already active (race) -> skipping: " + task.TempPath);
                task.State = DownloadTaskState.Canceled;
                FinishTask(task);
                return;
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
                            var moduleState = _context.ModuleStates[task.Module];
                            moduleState.DownloadedBytes += delta;
                            _globalDownloadedBytes += delta;
                            _globalSpeed.AddSample(delta);
                            EmitProgress(task, moduleState);
                            return true;
                        },
                        ShouldAbort = () => _controller.ShouldAbort(task.Module, _controller.GlobalToken),
                        OnPauseWait = () => _controller.WaitIfPaused(task.Module, _controller.GlobalToken)
                    });

                if (ok && await FinalizeFile(task))
                {
                    task.State = DownloadTaskState.Completed;
                    var moduleState = _context.ModuleStates[task.Module];
                    moduleState.CompletedFiles++;
                    EmitProgress(task, moduleState);
                    PropagateFollowersSuccess(task);
                    break;
                }

                task.State = DownloadTaskState.Failed;

                if (_controller.IsModuleCanceled(task.Module))
                {
                    task.State = DownloadTaskState.Canceled;
                    MarkPrimaryAndFollowersFailedOrCanceled(task, canceled: true);
                    break;
                }

                if (task.RetryCount < maxRetry)
                {
                    await RetryPolicy.DelayForRetry(task.RetryCount);
                }
                else
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
            _mergedFollowers.Remove(task); // followers 已在 success/failure 时处理
        }

        private async Task<bool> FinalizeFile(DownloadTask task)
        {
            try
            {
                // 若目标已经存在且 hash 正确 -> 直接视为成功（可能是并发合并后的“另一条”先完成）
                if (File.Exists(task.FinalPath))
                {
                    var existingBytes = File.ReadAllBytes(task.FinalPath);
                    var existingHash = HashUtility.Compute(existingBytes, _context.Options.HashAlgo);
                    if (string.Equals(existingHash, task.File.hash, StringComparison.OrdinalIgnoreCase))
                    {
                        // 清理自己的 temp（如果有）
                        if (File.Exists(task.TempPath))
                            SafeDelete(task.TempPath);
                        return true;
                    }
                    else
                    {
                        // 不同 hash -> 需要覆盖（继续走后面逻辑）
                        SafeDelete(task.FinalPath);
                    }
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

                // Hash 校验
                byte[] bytes = File.ReadAllBytes(task.FinalPath);
                var hash = HashUtility.Compute(bytes, _context.Options.HashAlgo);
                if (!string.Equals(hash, task.File.hash, StringComparison.OrdinalIgnoreCase))
                {
                    task.LastError = $"Hash mismatch expect={task.File.hash} got={hash}";
                    SafeDelete(task.FinalPath);
                    if (task.IsCompressed && File.Exists(task.TempPath))
                        SafeDelete(task.TempPath);
                    return false;
                }

                if (task.IsCompressed && File.Exists(task.TempPath))
                    SafeDelete(task.TempPath);

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

        #region Followers (Merged duplicated tasks)

        private void PropagateFollowersSuccess(DownloadTask primary)
        {
            if (!_mergedFollowers.TryGetValue(primary, out var list)) return;
            foreach (var follower in list)
            {
                follower.State = DownloadTaskState.Completed;
                follower.DownloadedBytes = primary.DownloadedBytes;
                follower.TotalBytes = primary.TotalBytes;
                follower.LastError = null;
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
            }
            _mergedFollowers.Remove(primary);
        }

        private void MarkPrimaryAndFollowersFailedOrCanceled(DownloadTask primary, bool canceled)
        {
            if (_mergedFollowers.TryGetValue(primary, out var list))
            {
                foreach (var follower in list)
                    follower.State = canceled ? DownloadTaskState.Canceled : DownloadTaskState.Failed;
                _mergedFollowers.Remove(primary);
            }
        }

        #endregion

        #region Progress / Completion

        private void EmitProgress(DownloadTask task, ModuleRuntimeState moduleState)
        {
            // 仅对“主任务”输出（followers 合并）
            if (_mergedFollowers.ContainsKey(task) || _mergedFollowers.Values.Any(l => l.Contains(task)))
            {
                // 如果想让 follower 也有进度事件，可在这里再发一遍
            }

            var fInfo = new FileProgressInfo
            {
                Module = task.Module,
                FileName = task.File.name,
                Downloaded = task.DownloadedBytes,
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
            // 可在此扩展：清理被取消任务的临时文件等
        }

        #endregion
    }
}
