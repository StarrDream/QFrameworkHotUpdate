using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Persistence;
using QHotUpdateSystem.Logging;
using QHotUpdateSystem.EventsSystem;
using QHotUpdateSystem.Events;
using QHotUpdateSystem.Security;
using QHotUpdateSystem.Compression;
using QHotUpdateSystem.State;
using QHotUpdateSystem.Diagnostics;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 下载管理器
    /// ★ 本批次改动：
    ///   1. 修复：模块文件重复（相同 name+hash）导致 alias 计数失衡，引起模块无法完成的问题 —— 现在预先去重，TotalFiles/TotalBytes 按唯一文件统计。
    ///   2. 其它逻辑保持不变（后续批次再做性能和 HEAD 优化）。
    /// </summary>
    public class DownloadManager
    {
        private const int CompletedListMax = 2000; // _completed 列表最大保留条目数（防止无限增长）
        private const int QueuedSnapshotMax = 4000; // 仅用于诊断的 queued 快照上限（防止诊断巨大内存）

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

        private readonly object _sync = new object();
        private static readonly object _versionWriteLock = new object();

        // Progress 节流
        private readonly Stopwatch _progressWatch = Stopwatch.StartNew();
        private double _lastGlobalEmitSec;
        private readonly Dictionary<string, double> _lastModuleEmitSec = new Dictionary<string, double>();
        private double _lastFileEmitSec;
        private const double ProgressEmitIntervalSec = 0.06;

        // Aging & 诊断计时
        private readonly Stopwatch _agingWatch = Stopwatch.StartNew();
        private double _lastDiagnosticsEmitSec;
        private const double DiagnosticsIntervalSec = 3.0;

        // Lazy Aging 参数
        private const double AgingWaitThresholdSec = 15.0;
        private const double AgingStepSec = 10.0;
        private const int AgingPriorityStep = 5;

        // 调度信号
        private readonly SemaphoreSlim _loopSignal = new SemaphoreSlim(0, int.MaxValue);
        private const int LoopWaitTimeoutMs = 500;

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

            // 1) 过滤不安全文件
            var safeFilesRaw = changed.Where(f => FileNameValidator.IsSafeRelativeName(f.name)).ToList();

            // 2) ★ 去重：按 (name + hash) 唯一，避免同文件重复导致 alias 不计数阻塞完成判定
            var unique = new List<FileEntry>();
            var dedupSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in safeFilesRaw)
            {
                string key = f.name + "|" + f.hash;
                if (dedupSet.Add(key))
                    unique.Add(f);
            }

            // 3) 统计（只按唯一文件计数）
            state.TotalFiles = unique.Count;
            state.TotalBytes = unique.Sum(f => f.compressed ? f.cSize : f.size);

            lock (_sync)
            {
                _globalTotalBytes += state.TotalBytes;
            }

            HotUpdateEvents.InvokeModuleStatus(moduleName, ModuleStatus.Updating);

            foreach (var f in unique)
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
                    catch
                    {
                        existing = 0;
                    }
                }

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
                    lock (_sync)
                    {
                        state.DownloadedBytes += existing;
                        _globalDownloadedBytes += existing;
                    }
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
                    OriginalPriority = priority,
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
            SignalLoop();
        }

        public void ResumeModule(string module)
        {
            _controller.ResumeModule(module);
            ExtendedDownloadEvents.InvokeModuleResumed(module);
            if (_controller.StateMachine.Current == DownloadState.Paused)
                _controller.StateMachine.TryTransition(DownloadState.Running);
            SignalLoop();
            if (!_loopActive)
                _ = ProcessLoop();
        }

        public void CancelModule(string module)
        {
            _controller.CancelModule(module);
            ExtendedDownloadEvents.InvokeModuleCanceled(module);
            _controller.StateMachine.TryTransition(DownloadState.Canceling);
            SignalLoop();
        }

        public void CancelAll()
        {
            _controller.CancelAll();
            ExtendedDownloadEvents.InvokeAllCanceled();
            _controller.StateMachine.TryTransition(DownloadState.Canceling);
            SignalLoop();
        }

        #endregion

        #region Queue / Loop

        private bool CanMerge(DownloadTask existingPrimary, DownloadTask newTask)
        {
            if (existingPrimary == null || newTask == null) return false;
            return existingPrimary.Module == newTask.Module
                   && existingPrimary.File.name == newTask.File.name
                   && existingPrimary.File.hash == newTask.File.hash;
        }

        // 在任务失败最终化后扣减剩余未下载的字节数
        private void AdjustGlobalTotalForFailedTask(DownloadTask task)
        {
            if (task == null || task.IsAlias) return; // alias 不重复统计全局
            long done = task.ExistingBytes + task.DownloadedBytes;
            if (done < 0) done = 0;
            long remain = task.TotalBytes - done;
            if (remain <= 0) return;

            lock (_sync)
            {
                _globalTotalBytes -= remain;
                if (_globalTotalBytes < _globalDownloadedBytes) // 防御：保持 total >= downloaded
                    _globalTotalBytes = _globalDownloadedBytes;
            }
        }

        private void EnqueueTask(DownloadTask task)
        {
            lock (_sync)
            {
                if (_pendingByTemp.TryGetValue(task.TempPath, out var existingPrimary) && CanMerge(existingPrimary, task))
                {
                    HotUpdateLogger.Info($"Merge duplicate task (alias): {task.File.name} temp={task.TempPath}");
                    if (!_mergedFollowers.TryGetValue(existingPrimary, out var list))
                    {
                        list = new List<DownloadTask>();
                        _mergedFollowers[existingPrimary] = list;
                    }

                    task.IsAlias = true;
                    list.Add(task);
                    task.State = DownloadTaskState.Queued;
                }
                else
                {
                    task.EnqueueTimeSec = _agingWatch.Elapsed.TotalSeconds;
                    _pendingByTemp[task.TempPath] = task;
                    _queue.Enqueue((int)task.Priority, task);
                    if (_queuedTasks.Count < QueuedSnapshotMax) // 限制快照尺寸
                        _queuedTasks.Add(task);
                }
            }

            SignalLoop();
        }

        private void SignalLoop()
        {
            try
            {
                _loopSignal.Release();
            }
            catch
            {
            }
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
                    if (_controller.StateMachine.Current == DownloadState.Canceling ||
                        _controller.GlobalCancelRequested)
                    {
                        CleanupCancelFlags();
                        _controller.StateMachine.TryTransition(DownloadState.Idle);
                        break;
                    }

                    MaybeEmitDiagnosticsSnapshot();

                    DownloadTask toStart = null;
                    bool idle;
                    lock (_sync)
                    {
                        idle = _queue.Count == 0 && _running.Count == 0;
                        if (!idle)
                        {
                            bool allPaused = AreAllQueuedModulesPausedLocked() && _running.Count == 0;
                            if (!allPaused && _running.Count < _context.Options.MaxConcurrent && _queue.Count > 0)
                            {
                                var next = _queue.Dequeue();
                                if (_queuedTasks.Contains(next))
                                    _queuedTasks.Remove(next);

                                if (ApplyAgingPriority(next))
                                {
                                    _queue.Enqueue((int)next.Priority, next);
                                    if (_queuedTasks.Count < QueuedSnapshotMax)
                                        _queuedTasks.Add(next);
                                }
                                else if (_controller.IsModuleCanceled(next.Module))
                                {
                                    next.State = DownloadTaskState.Canceled;
                                    MarkPrimaryAndFollowersFailedOrCanceled(next, true);
                                    FinishTask(next);
                                }
                                else if (_controller.IsModulePaused(next.Module))
                                {
                                    _queue.Enqueue((int)next.Priority, next);
                                    if (_queuedTasks.Count < QueuedSnapshotMax)
                                        _queuedTasks.Add(next);
                                }
                                else
                                {
                                    _running.Add(next);
                                    toStart = next;
                                }
                            }
                        }
                    }

                    if (toStart != null)
                        _ = RunTaskAsync(toStart);

                    if (idle)
                        break;

                    await _loopSignal.WaitAsync(LoopWaitTimeoutMs);
                }
            }
            finally
            {
                bool finished;
                lock (_sync)
                {
                    finished = _queue.Count == 0 && _running.Count == 0;
                }

                if (finished)
                {
                    _controller.StateMachine.TryTransition(DownloadState.Completed);
                    ForceEmitGlobalProgress();
                    EmitFinalDiagnosticsSnapshot();
                    HotUpdateEvents.InvokeAllTasksCompleted();
                }

                _loopActive = false;

                // ★ 新增：在一次循环终止（完成或取消）后重置全局取消 token
                _controller.RenewGlobalToken();
            }
        }

        private bool ApplyAgingPriority(DownloadTask t)
        {
            if (t == null || t.State != DownloadTaskState.Queued) return false;
            var now = _agingWatch.Elapsed.TotalSeconds;
            var wait = now - t.EnqueueTimeSec;
            if (wait < AgingWaitThresholdSec) return false;

            int steps = (int)((wait - AgingWaitThresholdSec) / AgingStepSec) + 1;
            int old = (int)t.Priority;
            int target = old + steps * AgingPriorityStep;
            if (target > (int)DownloadPriority.Critical)
                target = (int)DownloadPriority.Critical;
            if (target != old)
            {
                t.Priority = (DownloadPriority)target;
                StructuredLogger.Log(StructuredLogger.Level.Info, "LazyAgingUpgrade",
                    new { file = t.File?.name, old, @new = target, wait });
                return true;
            }

            return false;
        }

        private bool AreAllQueuedModulesPausedLocked()
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
            lock (_sync)
            {
                if (!_activeTempFiles.Add(task.TempPath))
                {
                    HotUpdateLogger.Warn("Temp already active (race) -> skipping: " + task.TempPath);
                    task.State = DownloadTaskState.Canceled;
                    FinishTask(task);
                    SignalLoop();
                    return;
                }
            }

            var head = await HttpDownloader.HeadAsync(task.RemoteUrl, _context.Options.TimeoutSeconds);
            if (head.Ok)
            {
                if (!head.AcceptRanges)
                {
                    task.SupportResume = false;
                    if (task.ExistingBytes > 0)
                    {
                        TryDelete(task.TempPath);
                        TryDelete(task.ResumeMetaPath);
                        task.ExistingBytes = 0;
                    }
                }

                task.ETag = head.ETag;
                task.LastModified = head.LastModified;
                // 续传校验逻辑保持不变……
            }

            if (!task.IsCompressed && task.ExistingBytes == 0)
                task.IncrementalHash = IncrementalHashWrapper.Create(_context.Options.HashAlgo);

            if (task.SupportResume && task.ExistingBytes > 0 && !File.Exists(task.ResumeMetaPath))
            {
                var meta = DownloadResumeMeta.Create(task.File, task.RemoteUrl, _context.Options.HashAlgo, task.ETag, task.LastModified);
                meta.Save(task.ResumeMetaPath);
            }

            int maxRetry = _context.Options.MaxRetry;
            int maxIntegrityRetry = Math.Max(1, Math.Min(2, maxRetry));
            bool finished = false;

            for (; task.RetryCount <= maxRetry && !finished; task.RetryCount++)
            {
                if (_controller.IsModuleCanceled(task.Module))
                {
                    task.State = DownloadTaskState.Canceled;
                    task.ErrorCode = DownloadErrorCode.Canceled;
                    MarkPrimaryAndFollowersFailedOrCanceled(task, true);
                    HotUpdateEvents.InvokeFileError(task.Module, task.File.name, task.ErrorCode, "Canceled");
                    break;
                }

                long attemptDelta = 0;
                var moduleState = _context.ModuleStates[task.Module];

                var netResult = await HttpDownloader.DownloadFile(
                    task,
                    task.RemoteUrl,
                    task.TempPath,
                    new HttpDownloader.DownloadOptions
                    {
                        TimeoutSec = _context.Options.TimeoutSeconds,
                        OnDelta = delta =>
                        {
                            attemptDelta += delta;
                            lock (_sync)
                            {
                                task.DownloadedBytes += delta;
                                moduleState.DownloadedBytes += delta;
                                _globalDownloadedBytes += delta;
                            }

                            _globalSpeed.AddSample(delta);
                            EmitProgressThrottled(task, moduleState, force: false);
                            return true;
                        },
                        OnChunk = (buf, ofs, cnt) => task.IncrementalHash?.Append(buf, ofs, cnt),
                        ShouldAbort = () => _controller.ShouldAbort(task.Module, _controller.GlobalToken),
                        OnPauseWait = () => _controller.WaitIfPaused(task.Module, _controller.GlobalToken),
                        SupportResume = task.SupportResume,
                        ExistingBytes = task.ExistingBytes,
                        ExpectedTotal = task.TotalBytes,
                        RemoteUrl = task.RemoteUrl,
                        OnResponseMeta = (etag, lm) =>
                        {
                            if (!string.IsNullOrEmpty(etag)) task.ETag = etag;
                            if (!string.IsNullOrEmpty(lm)) task.LastModified = lm;
                            if (task.SupportResume && !task.ResumeMetaInitialized)
                            {
                                var meta = DownloadResumeMeta.Create(task.File, task.RemoteUrl, _context.Options.HashAlgo, task.ETag, task.LastModified);
                                meta.Save(task.ResumeMetaPath);
                                task.ResumeMetaInitialized = true;
                            }
                        }
                    });

                if (netResult.Aborted)
                {
                    task.State = DownloadTaskState.Canceled;
                    task.ErrorCode = DownloadErrorCode.Canceled;
                    MarkPrimaryAndFollowersFailedOrCanceled(task, true);
                    HotUpdateEvents.InvokeFileError(task.Module, task.File.name, task.ErrorCode, "Aborted");
                    break;
                }

                if (!netResult.Succeeded)
                {
                    task.State = DownloadTaskState.Failed;
                    task.ErrorCode = netResult.ErrorCode == DownloadErrorCode.None ? DownloadErrorCode.Network : netResult.ErrorCode;
                    task.LastError = netResult.ErrorMessage;
                    bool retryable = task.RetryCount < maxRetry;

                    StructuredLogger.Log(StructuredLogger.Level.Warn, "Download network fail",
                        new { task.Module, File = task.File.name, task.RetryCount, task.ErrorCode, task.LastError, retryable });

                    if (retryable)
                    {
                        EmitProgressThrottled(task, moduleState, force: true);
                        await RetryPolicy.DelayForRetry(task.RetryCount);
                        continue;
                    }

                    if (!task.IsAlias)
                    {
                        lock (_sync)
                        {
                            moduleState.FailedFiles++;
                        }

                        AdjustGlobalTotalForFailedTask(task); // ★ 扣减剩余字节
                    }

                    moduleState.LastError = task.LastError;
                    EmitProgressThrottled(task, moduleState, force: true);
                    HotUpdateEvents.InvokeError(task.Module, $"File {task.File.name} failed (network): {task.LastError}");
                    HotUpdateEvents.InvokeFileError(task.Module, task.File.name, task.ErrorCode, task.LastError);
                    PropagateFollowersFailure(task);
                    break;
                }

                var fin = await FinalizeFile(task, attemptDelta, moduleState);
                if (fin.Success)
                {
                    task.State = DownloadTaskState.Completed;
                    task.ErrorCode = DownloadErrorCode.None;
                    if (!task.IsAlias)
                    {
                        lock (_sync)
                        {
                            moduleState.CompletedFiles++;
                        }
                    }

                    EmitProgressThrottled(task, moduleState, force: true);
                    PropagateFollowersSuccess(task);
                    finished = true;
                    break;
                }
                else
                {
                    task.State = DownloadTaskState.Failed;
                    task.ErrorCode = fin.ErrorCode;
                    task.LastError = fin.ErrorMessage;

                    StructuredLogger.Log(StructuredLogger.Level.Warn, "Finalize fail",
                        new { task.Module, File = task.File.name, task.ErrorCode, task.LastError, attemptDelta, task.RetryCount, task.IntegrityRetryCount });

                    if (fin.TempInvalidated && attemptDelta > 0)
                    {
                        lock (_sync)
                        {
                            var mState = moduleState;
                            task.DownloadedBytes -= attemptDelta;
                            mState.DownloadedBytes -= attemptDelta;
                            _globalDownloadedBytes -= attemptDelta;
                            if (task.DownloadedBytes < 0) task.DownloadedBytes = 0;
                            if (mState.DownloadedBytes < 0) mState.DownloadedBytes = 0;
                        }
                    }

                    if (fin.ErrorCode == DownloadErrorCode.IntegrityMismatch && fin.TempInvalidated && task.ExistingBytes > 0)
                    {
                        long rollback = task.ExistingBytes;
                        lock (_sync)
                        {
                            moduleState.DownloadedBytes -= rollback;
                            _globalDownloadedBytes -= rollback;
                            if (moduleState.DownloadedBytes < 0) moduleState.DownloadedBytes = 0;
                            if (_globalDownloadedBytes < 0) _globalDownloadedBytes = 0;
                        }

                        task.ExistingBytes = 0;
                    }

                    if ((fin.ErrorCode == DownloadErrorCode.IntegrityMismatch || fin.ErrorCode == DownloadErrorCode.DecompressFail))
                    {
                        task.IntegrityRetryCount++;
                        bool retryable = task.IntegrityRetryCount <= maxIntegrityRetry && task.RetryCount < maxRetry;
                        if (retryable)
                        {
                            EmitProgressThrottled(task, moduleState, force: true);
                            await RetryPolicy.DelayForRetry(task.RetryCount);
                            continue;
                        }
                    }

                    if (!task.IsAlias)
                    {
                        lock (_sync)
                        {
                            moduleState.FailedFiles++;
                        }

                        AdjustGlobalTotalForFailedTask(task); // ★ 扣减剩余
                    }

                    moduleState.LastError = task.LastError;
                    EmitProgressThrottled(task, moduleState, force: true);
                    HotUpdateEvents.InvokeError(task.Module, $"File {task.File.name} failed ({task.ErrorCode}): {task.LastError}");
                    HotUpdateEvents.InvokeFileError(task.Module, task.File.name, task.ErrorCode, task.LastError);
                    PropagateFollowersFailure(task);
                    break;
                }
            }

            FinishTask(task);
            CheckModuleComplete(task.Module);
            SignalLoop();
        }

        private void FinishTask(DownloadTask task)
        {
            lock (_sync)
            {
                _running.Remove(task);
                _completed.Add(task);
                // 控制 _completed 列表大小，避免无上限增长
                if (_completed.Count > CompletedListMax)
                    _completed.RemoveRange(0, _completed.Count - CompletedListMax);

                _activeTempFiles.Remove(task.TempPath);
                _pendingByTemp.Remove(task.TempPath);
                _mergedFollowers.Remove(task);
            }
        }

        private async Task<FinalizeResult> FinalizeFile(DownloadTask task, long attemptDelta, ModuleRuntimeState moduleState)
        {
            try
            {
                if (!FileNameValidator.IsSafeRelativeName(task.File.name))
                    return FinalizeResult.Fail(DownloadErrorCode.UnsafePath, "Unsafe file name", true);
                if (!FileNameValidator.IsPathWithinRoot(_storage.AssetDirFullPath, task.FinalPath))
                    return FinalizeResult.Fail(DownloadErrorCode.UnsafePath, "Target path outside asset root", true);

                var tempInfo = new FileInfo(task.TempPath);
                if (!tempInfo.Exists)
                    return FinalizeResult.Fail(DownloadErrorCode.IO, "Temp missing", true);
                if (tempInfo.Length != task.TotalBytes)
                    return FinalizeResult.Fail(DownloadErrorCode.IO, $"Length mismatch expect={task.TotalBytes} got={tempInfo.Length}", true);

                if (File.Exists(task.FinalPath))
                {
                    using (var existStream = File.OpenRead(task.FinalPath))
                    {
                        var existingHash = HashUtility.ComputeStream(existStream, _context.Options.HashAlgo);
                        if (string.Equals(existingHash, task.File.hash, StringComparison.OrdinalIgnoreCase))
                        {
                            SafeDelete(task.TempPath);
                            TryDelete(task.ResumeMetaPath);
                            task.OnCompleted?.Invoke(task);
                            return FinalizeResult.Ok();
                        }
                    }

                    SafeDelete(task.FinalPath);
                }

                string computedHash = null;

                if (!task.IsCompressed)
                {
                    if (task.IncrementalHash != null && task.ExistingBytes == 0)
                    {
                        computedHash = task.IncrementalHash.FinalHex();
                    }
                    else
                    {
                        using (var fs = File.OpenRead(task.TempPath))
                            computedHash = HashUtility.ComputeStream(fs, _context.Options.HashAlgo);
                    }

                    if (!string.Equals(computedHash, task.File.hash, StringComparison.OrdinalIgnoreCase))
                    {
                        SafeDelete(task.TempPath);
                        TryDelete(task.ResumeMetaPath);
                        return FinalizeResult.Fail(DownloadErrorCode.IntegrityMismatch,
                            $"Hash mismatch expect={task.File.hash} got={computedHash}", true);
                    }

                    if (File.Exists(task.FinalPath))
                        SafeDelete(task.FinalPath);
                    File.Move(task.TempPath, task.FinalPath);
                    TryDelete(task.ResumeMetaPath);
                    task.OnCompleted?.Invoke(task);
                    return FinalizeResult.Ok();
                }
                else
                {
                    string finalTemp = task.FinalPath + ".dl_tmp";
                    if (File.Exists(finalTemp)) SafeDelete(finalTemp);

                    var comp = CompressorRegistry.Get(task.File.algo);
                    if (comp == null)
                        return FinalizeResult.Fail(DownloadErrorCode.DecompressFail, "No compressor: " + task.File.algo, true);

                    if (!comp.Decompress(task.TempPath, finalTemp, out var derr))
                        return FinalizeResult.Fail(DownloadErrorCode.DecompressFail, "Decompress fail: " + derr, false);

                    using (var fs = File.OpenRead(finalTemp))
                        computedHash = HashUtility.ComputeStream(fs, _context.Options.HashAlgo);

                    if (!string.Equals(computedHash, task.File.hash, StringComparison.OrdinalIgnoreCase))
                    {
                        SafeDelete(finalTemp);
                        SafeDelete(task.TempPath);
                        TryDelete(task.ResumeMetaPath);
                        return FinalizeResult.Fail(DownloadErrorCode.IntegrityMismatch,
                            $"Hash mismatch expect={task.File.hash} got={computedHash}", true);
                    }

                    if (File.Exists(task.FinalPath))
                        SafeDelete(task.FinalPath);
                    File.Move(finalTemp, task.FinalPath);
                    SafeDelete(task.TempPath);
                    TryDelete(task.ResumeMetaPath);
                    task.OnCompleted?.Invoke(task);
                    return FinalizeResult.Ok();
                }
            }
            catch (Exception e)
            {
                return FinalizeResult.Fail(DownloadErrorCode.Unknown, e.Message, false);
            }
            finally
            {
                try
                {
                    task.IncrementalHash?.Dispose();
                }
                catch
                {
                }

                task.IncrementalHash = null;
                await Task.Yield();
            }
        }

        private void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
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
            List<DownloadTask> followers = null;
            lock (_sync)
            {
                if (!_mergedFollowers.TryGetValue(primary, out followers))
                    return;
            }

            foreach (var follower in followers)
            {
                follower.State = DownloadTaskState.Completed;
                follower.DownloadedBytes = primary.DownloadedBytes;
                follower.TotalBytes = primary.TotalBytes;
                follower.LastError = null;
                follower.ErrorCode = DownloadErrorCode.None;
            }

            lock (_sync)
            {
                _mergedFollowers.Remove(primary);
            }
        }

        private void PropagateFollowersFailure(DownloadTask primary)
        {
            List<DownloadTask> followers = null;
            lock (_sync)
            {
                if (!_mergedFollowers.TryGetValue(primary, out followers))
                    return;
            }

            foreach (var follower in followers)
            {
                follower.State = DownloadTaskState.Failed;
                follower.LastError = primary.LastError;
                follower.ErrorCode = primary.ErrorCode;
            }

            lock (_sync)
            {
                _mergedFollowers.Remove(primary);
            }
        }

        private void MarkPrimaryAndFollowersFailedOrCanceled(DownloadTask primary, bool canceled)
        {
            List<DownloadTask> followers = null;
            lock (_sync)
            {
                if (_mergedFollowers.TryGetValue(primary, out followers))
                {
                    foreach (var follower in followers)
                    {
                        follower.State = canceled ? DownloadTaskState.Canceled : DownloadTaskState.Failed;
                        follower.ErrorCode = canceled ? DownloadErrorCode.Canceled : DownloadErrorCode.Unknown;
                    }

                    _mergedFollowers.Remove(primary);
                }
            }
        }

        #endregion

        #region Progress Throttling

        private void EmitProgressThrottled(DownloadTask task, ModuleRuntimeState moduleState, bool force)
        {
            FileProgressInfo? fInfo = null;
            ModuleProgressInfo? mInfo = null;
            GlobalProgressInfo? gInfo = null;

            var nowSec = _progressWatch.Elapsed.TotalSeconds;
            bool emitFile = false, emitModule = false, emitGlobal = false;

            long moduleDownloaded = 0, moduleTotal = 0;
            int moduleCompletedFiles = 0, moduleTotalFiles = 0;
            long gDown = 0, gTot = 0;

            lock (_sync)
            {
                _lastModuleEmitSec.TryGetValue(task.Module, out var lastModuleTs);
                emitFile = force || (nowSec - _lastFileEmitSec >= ProgressEmitIntervalSec);
                emitModule = force || (nowSec - lastModuleTs >= ProgressEmitIntervalSec);
                emitGlobal = force || (nowSec - _lastGlobalEmitSec >= ProgressEmitIntervalSec);

                if (!(emitFile || emitModule || emitGlobal))
                    return;

                if (emitFile)
                {
                    _lastFileEmitSec = nowSec;
                    fInfo = new FileProgressInfo
                    {
                        Module = task.Module,
                        FileName = task.File.name,
                        Downloaded = task.ExistingBytes + task.DownloadedBytes,
                        Total = task.TotalBytes,
                        Speed = _globalSpeed.GetSpeed()
                    };
                }

                if (emitModule)
                {
                    _lastModuleEmitSec[task.Module] = nowSec;
                    moduleDownloaded = moduleState.DownloadedBytes;
                    moduleTotal = moduleState.TotalBytes;
                    moduleCompletedFiles = moduleState.CompletedFiles;
                    moduleTotalFiles = moduleState.TotalFiles;
                    mInfo = new ModuleProgressInfo
                    {
                        Module = task.Module,
                        DownloadedBytes = moduleDownloaded,
                        TotalBytes = moduleTotal,
                        CompletedFiles = moduleCompletedFiles,
                        TotalFiles = moduleTotalFiles,
                        Speed = _globalSpeed.GetSpeed()
                    };
                }

                if (emitGlobal)
                {
                    _lastGlobalEmitSec = nowSec;
                    gDown = _globalDownloadedBytes;
                    gTot = _globalTotalBytes;
                    gInfo = new GlobalProgressInfo
                    {
                        DownloadedBytes = gDown,
                        TotalBytes = gTot,
                        Speed = _globalSpeed.GetSpeed()
                    };
                }
            }

            if (fInfo.HasValue) HotUpdateEvents.InvokeFileProgress(task.Module, fInfo.Value);
            if (mInfo.HasValue) HotUpdateEvents.InvokeModuleProgress(task.Module, mInfo.Value);
            if (gInfo.HasValue) HotUpdateEvents.InvokeGlobalProgress(gInfo.Value);
        }

        private void ForceEmitGlobalProgress()
        {
            long gDown, gTot;
            lock (_sync)
            {
                gDown = _globalDownloadedBytes;
                gTot = _globalTotalBytes;
            }

            var gInfo = new GlobalProgressInfo
            {
                DownloadedBytes = gDown,
                TotalBytes = gTot,
                Speed = _globalSpeed.GetSpeed()
            };
            HotUpdateEvents.InvokeGlobalProgress(gInfo);
        }

        #endregion

        #region Diagnostics

        private void MaybeEmitDiagnosticsSnapshot()
        {
            var now = _agingWatch.Elapsed.TotalSeconds;
            if (now - _lastDiagnosticsEmitSec < DiagnosticsIntervalSec) return;
            _lastDiagnosticsEmitSec = now;
            EmitDiagnosticsSnapshot();
        }

        private void EmitFinalDiagnosticsSnapshot() => EmitDiagnosticsSnapshot();

        private void EmitDiagnosticsSnapshot()
        {
            DownloadDiagnosticsSnapshot snap;
            lock (_sync)
            {
                snap = new DownloadDiagnosticsSnapshot
                {
                    GlobalDownloadedBytes = _globalDownloadedBytes,
                    GlobalTotalBytes = _globalTotalBytes,
                    GlobalSpeed = _globalSpeed.GetSpeed(),
                    QueuedCount = _queue.Count,
                    RunningCount = _running.Count,
                    CompletedCount = _completed.Count,
                    Modules = _context.ModuleStates.Select(kv => new DownloadDiagnosticsSnapshot.ModuleSummary
                    {
                        Name = kv.Key,
                        CompletedFiles = kv.Value.CompletedFiles,
                        FailedFiles = kv.Value.FailedFiles,
                        TotalFiles = kv.Value.TotalFiles,
                        DownloadedBytes = kv.Value.DownloadedBytes,
                        TotalBytes = kv.Value.TotalBytes,
                        Status = kv.Value.Status.ToString()
                    }).ToArray(),
                    ActiveTasks = _running
                        .Concat(_queuedTasks)
                        .Select(t =>
                        {
                            double wait = t.State == DownloadTaskState.Queued
                                ? (_agingWatch.Elapsed.TotalSeconds - t.EnqueueTimeSec)
                                : 0;
                            return new DownloadDiagnosticsSnapshot.TaskSummary
                            {
                                Module = t.Module,
                                FileName = t.File.name,
                                Downloaded = t.ExistingBytes + t.DownloadedBytes,
                                Total = t.TotalBytes,
                                State = t.State.ToString(),
                                Priority = (int)t.Priority,
                                OriginalPriority = (int)t.OriginalPriority,
                                WaitSeconds = Math.Round(wait, 2),
                                RetryCount = t.RetryCount,
                                IntegrityRetry = t.IntegrityRetryCount,
                                ErrorCode = t.ErrorCode.ToString()
                            };
                        }).ToArray()
                };
            }

            HotUpdateEvents.InvokeDiagnostics(snap);
        }

        #endregion

        #region Completion / Version

        private void CheckModuleComplete(string module)
        {
            if (!_context.ModuleStates.TryGetValue(module, out var mState)) return;

            bool done;
            bool failed;
            lock (_sync)
            {
                done = (mState.CompletedFiles + mState.FailedFiles) == mState.TotalFiles;
                failed = mState.FailedFiles > 0;
            }

            if (!done) return;

            if (!failed)
            {
                var remoteModule = FindModule(_context.RemoteVersion, module);
                lock (_versionWriteLock)
                {
                    VersionWriter.UpsertModule(_context.LocalVersion, remoteModule);
                    VersionLoader.SaveLocal(_context.PlatformAdapter.GetLocalVersionFilePath(), _context.LocalVersion, _context.JsonSerializer);
                }

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

        private void MarkModuleInstalled(ModuleInfo remoteModule)
        {
            lock (_versionWriteLock)
            {
                VersionWriter.UpsertModule(_context.LocalVersion, remoteModule);
                VersionLoader.SaveLocal(_context.PlatformAdapter.GetLocalVersionFilePath(), _context.LocalVersion, _context.JsonSerializer);
            }

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
                if (m.name == name)
                    return m;
            return null;
        }

        private void CleanupCancelFlags()
        {
            // 保留 .part，供后续续传
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        #endregion
    }
}