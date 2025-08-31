namespace QHotUpdateSystem.Core
{
    /// <summary>
    /// 单个下载任务生命周期状态
    /// </summary>
    public enum DownloadTaskState
    {
        Queued = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Canceled = 4
    }
}