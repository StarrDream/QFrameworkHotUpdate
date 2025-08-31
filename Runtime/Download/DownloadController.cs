using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.State;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 下载控制器
    /// </summary>
    public class DownloadController
    {
        private readonly HashSet<string> _pausedModules = new HashSet<string>();
        private readonly HashSet<string> _canceledModules = new HashSet<string>();
        private readonly Dictionary<string, TaskCompletionSource<bool>> _pauseSignals = new Dictionary<string, TaskCompletionSource<bool>>();
        private readonly object _lock = new object();

        private CancellationTokenSource _globalCts = new CancellationTokenSource();

        public DownloadStateMachine StateMachine { get; } = new DownloadStateMachine();
        public CancellationToken GlobalToken => _globalCts.Token;

        public bool IsModulePaused(string module)
        {
            lock (_lock) return _pausedModules.Contains(module);
        }
        public bool IsModuleCanceled(string module)
        {
            lock (_lock) return _canceledModules.Contains(module);
        }

        public void PauseModule(string module)
        {
            lock (_lock)
            {
                if (_pausedModules.Add(module))
                {
                    if (!_pauseSignals.ContainsKey(module))
                        _pauseSignals[module] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        public void ResumeModule(string module)
        {
            TaskCompletionSource<bool> tcs = null;
            lock (_lock)
            {
                if (_pausedModules.Remove(module))
                {
                    if (_pauseSignals.TryGetValue(module, out tcs))
                        _pauseSignals.Remove(module);
                }
            }
            tcs?.TrySetResult(true); // 唤醒等待
        }

        public void CancelModule(string module)
        {
            TaskCompletionSource<bool> tcs = null;
            lock (_lock)
            {
                if (_canceledModules.Add(module))
                {
                    if (_pauseSignals.TryGetValue(module, out tcs))
                        _pauseSignals.Remove(module);
                }
            }
            tcs?.TrySetResult(false); // 取消也唤醒等待
        }

        public void CancelAll()
        {
            Dictionary<string, TaskCompletionSource<bool>> signals;
            lock (_lock)
            {
                _canceledModules.Clear();
                _pausedModules.Clear();
                signals = new Dictionary<string, TaskCompletionSource<bool>>(_pauseSignals);
                _pauseSignals.Clear();
                _globalCts.Cancel();
                _globalCts.Dispose();
                _globalCts = new CancellationTokenSource();
            }
            foreach (var kv in signals)
                kv.Value.TrySetResult(false);
        }

        /// <summary>
        /// 暂停等待：若模块被暂停，则阻塞直到恢复或取消。
        /// </summary>
        public async Task WaitIfPaused(string module, CancellationToken token)
        {
            while (true)
            {
                TaskCompletionSource<bool> tcs = null;
                lock (_lock)
                {
                    if (!_pausedModules.Contains(module) || token.IsCancellationRequested) return;
                    if (_pauseSignals.TryGetValue(module, out tcs) == false)
                    {
                        tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _pauseSignals[module] = tcs;
                    }
                }
                try
                {
                    await Task.WhenAny(tcs.Task, Task.Delay(-1, token));
                    return;
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        public bool ShouldAbort(string module, CancellationToken token)
        {
            lock (_lock)
                return token.IsCancellationRequested || _canceledModules.Contains(module);
        }

        public void ClearModuleFlags(string module)
        {
            TaskCompletionSource<bool> tcs = null;
            lock (_lock)
            {
                _pausedModules.Remove(module);
                _canceledModules.Remove(module);
                if (_pauseSignals.TryGetValue(module, out tcs))
                    _pauseSignals.Remove(module);
            }
            tcs?.TrySetResult(true);
        }
    }
}
