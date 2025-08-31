using QHotUpdateSystem.Core;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Security;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 单文件下载任务（Batch4：新增排队时间戳 & Aging 用原始优先级字段）
    /// </summary>
    public class DownloadTask
    {
        public string Module;
        public FileEntry File;
        public string RemoteUrl;
        public string TempPath;
        public string FinalPath;
        public string ResumeMetaPath;

        public long TotalBytes;
        public long ExistingBytes;
        public long DownloadedBytes;
        public DownloadPriority Priority;          // 当前有效优先级（可能被 aging 调整）
        public DownloadPriority OriginalPriority;  // 记录初始值，用于诊断
        public bool SupportResume;

        public int RetryCount;
        public DownloadTaskState State;
        public bool IsCompressed => File.compressed;
        public string LastError;
        public DownloadErrorCode ErrorCode = DownloadErrorCode.None;

        public int IntegrityRetryCount;
        public System.Action<DownloadTask> OnCompleted;

        // 增量哈希（Batch3）
        internal IncrementalHashWrapper IncrementalHash;
        internal string IncrementalHashHex;

        // 排队时间（秒，基于 DownloadManager 内部 Stopwatch）供老化策略使用
        internal double EnqueueTimeSec;

        public override string ToString()
        {
            return $"{Module}:{File?.name} state={State} retry={RetryCount} integrityRetry={IntegrityRetryCount} priority={Priority} err={LastError}";
        }
    }
}