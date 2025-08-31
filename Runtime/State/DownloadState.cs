namespace QHotUpdateSystem.State
{
    /// <summary>
    /// 下载流程整体阶段（可用于 UI 大状态展示）
    /// </summary>
    public enum DownloadState
    {
        Idle = 0,
        Preparing = 1,
        Running = 2,
        Paused = 3,
        Canceling = 4,
        Completed = 5,
        Error = 6
    }
}