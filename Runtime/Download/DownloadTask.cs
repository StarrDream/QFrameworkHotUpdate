using System;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.Version;

namespace QHotUpdateSystem.Download
{
    public class DownloadTask
    {
        public string Module;
        public FileEntry File;
        public string RemoteUrl;
        public string TempPath;
        public string FinalPath;
        public DownloadTaskState State = DownloadTaskState.Queued;
        public DownloadPriority Priority = DownloadPriority.Normal;

        public long TotalBytes;
        public long DownloadedBytes;
        public int RetryCount;
        public string LastError;

        public bool IsCompressed => File.compressed;
        public bool Canceled;

        public Action<DownloadTask> OnProgress;
        public Action<DownloadTask> OnCompleted;
        public Action<DownloadTask> OnFailed;

        public override string ToString() => $"{Module}/{File?.name} ({DownloadedBytes}/{TotalBytes}) State={State} P={Priority}";
    }
}