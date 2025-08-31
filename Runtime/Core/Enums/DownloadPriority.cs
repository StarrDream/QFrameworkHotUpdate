namespace QHotUpdateSystem.Core
{
    /// <summary>
    /// 下载优先级（数值越大优先级越高）
    /// </summary>
    public enum DownloadPriority
    {
        Low = 0,
        Normal = 10,
        High = 20,
        Critical = 30
    }
}