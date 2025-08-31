using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.State;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 下载控制器：统一暂停 / 取消，管理活动 token
    /// </summary>
    public class DownloadController
    {
        private readonly HashSet<string> _pausedModules = new HashSet<string>();
        private readonly HashSet<string> _canceledModules = new HashSet<string>();
        private CancellationTokenSource _globalCts = new CancellationTokenSource();

        public DownloadStateMachine StateMachine { get; } = new DownloadStateMachine();

        public CancellationToken GlobalToken => _globalCts.Token;

        public bool IsModulePaused(string module) => _pausedModules.Contains(module);
        public bool IsModuleCanceled(string module) => _canceledModules.Contains(module);

        public void PauseModule(string module)
        {
            if (_pausedModules.Add(module))
            {
                // 不需要取消 token，暂停通过任务轮询外部状态处理
            }
        }

        public void ResumeModule(string module)
        {
            _pausedModules.Remove(module);
        }

        public void CancelModule(string module)
        {
            if (_canceledModules.Add(module))
            {
                // 取消策略：标记后由下载循环检测 -> Abort
            }
        }

        public void CancelAll()
        {
            _canceledModules.Clear();
            _pausedModules.Clear();
            _globalCts.Cancel();
            _globalCts.Dispose();
            _globalCts = new CancellationTokenSource();
        }

        public async Task WaitIfPaused(string module, CancellationToken token)
        {
            while (_pausedModules.Contains(module) && !token.IsCancellationRequested)
            {
                await Task.Delay(150, token);
            }
        }

        public bool ShouldAbort(string module, CancellationToken token)
        {
            return token.IsCancellationRequested || _canceledModules.Contains(module);
        }

        public void ClearModuleFlags(string module)
        {
            _pausedModules.Remove(module);
            _canceledModules.Remove(module);
        }
    }
}
