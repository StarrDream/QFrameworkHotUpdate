using QHotUpdateSystem.Core;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Security;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 单文件下载任务
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
        public DownloadPriority Priority;
        public DownloadPriority OriginalPriority;
        public bool SupportResume;

        public int RetryCount;
        public DownloadTaskState State;
        public bool IsCompressed => File.compressed;
        public string LastError;
        public DownloadErrorCode ErrorCode = DownloadErrorCode.None;

        public int IntegrityRetryCount;
        public System.Action<DownloadTask> OnCompleted;

        internal IncrementalHashWrapper IncrementalHash;
        internal string IncrementalHashHex;
        internal double EnqueueTimeSec;

        // 批次1新增
        public bool IsAlias;

        // 批次2新增：用于与简化的 HEAD / GET 响应比对
        public string ETag;
        public string LastModified;

        // 标记是否已为该任务写入或更新过 meta（避免重复写）
        internal bool ResumeMetaInitialized;

        public override string ToString()
        {
            return $"{Module}:{File?.name} state={State} retry={RetryCount} priority={Priority} alias={IsAlias} etag={ETag}";
        }
    }
}