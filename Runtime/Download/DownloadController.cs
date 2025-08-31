using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QHotUpdateSystem.State;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 下载控制器（暂停/恢复/取消）
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

            tcs?.TrySetResult(true);
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

            tcs?.TrySetResult(false);
        }

        /// <summary>
        /// 取消全部任务
        /// ★ 修复：旧的 CancellationTokenSource 现在会在安全点异步 Dispose，避免长生命周期泄漏。
        /// </summary>
        public void CancelAll()
        {
            Dictionary<string, TaskCompletionSource<bool>> signals;
            CancellationTokenSource oldCts;
            lock (_lock)
            {
                _canceledModules.Clear();
                _pausedModules.Clear();
                signals = new Dictionary<string, TaskCompletionSource<bool>>(_pauseSignals);
                _pauseSignals.Clear();

                oldCts = _globalCts;
                try
                {
                    oldCts.Cancel();
                }
                catch
                {
                }

                _globalCts = new CancellationTokenSource(); // 新 token
            }

            foreach (var kv in signals)
                kv.Value.TrySetResult(false);

            // ★ 异步延迟释放，避免与仍在使用旧 token 的注册产生竞态
            if (oldCts != null)
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        // 给仍在使用的代码一个极短窗口（这里简单 Sleep 50ms，可选）
                        System.Threading.Thread.Sleep(50);
                        oldCts.Dispose();
                    }
                    catch
                    {
                    }
                });
            }
        }

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