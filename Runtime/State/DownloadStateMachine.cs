using System;

namespace QHotUpdateSystem.State
{
    /// <summary>
    /// 简单状态机：只有顺序与合法跳转校验
    /// </summary>
    public class DownloadStateMachine
    {
        public DownloadState Current { get; private set; } = DownloadState.Idle;
        public event Action<DownloadState, DownloadState> OnStateChanged;

        bool Allow(DownloadState from, DownloadState to)
        {
            if (from == to) return false;
            switch (from)
            {
                case DownloadState.Idle: return to == DownloadState.Preparing;
                case DownloadState.Preparing: return to == DownloadState.Running || to == DownloadState.Error;
                case DownloadState.Running: return to == DownloadState.Paused || to == DownloadState.Canceling || to == DownloadState.Completed || to == DownloadState.Error;
                case DownloadState.Paused: return to == DownloadState.Running || to == DownloadState.Canceling || to == DownloadState.Error;
                case DownloadState.Canceling: return to == DownloadState.Idle || to == DownloadState.Error;
                case DownloadState.Completed: return to == DownloadState.Idle;
                case DownloadState.Error: return to == DownloadState.Idle;
            }
            return false;
        }

        public bool TryTransition(DownloadState to)
        {
            if (!Allow(Current, to)) return false;
            var old = Current;
            Current = to;
            OnStateChanged?.Invoke(old, to);
            return true;
        }

        public void Reset() => Current = DownloadState.Idle;
    }
}