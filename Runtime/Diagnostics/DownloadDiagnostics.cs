using System.Collections.Generic;
using QHotUpdateSystem.Download;

namespace QHotUpdateSystem.Diagnostics
{
    /// <summary>
    /// 下载诊断快照（轻量结构，用于事件广播 & 调试面板）
    /// </summary>
    public struct DownloadDiagnosticsSnapshot
    {
        public long GlobalDownloadedBytes;
        public long GlobalTotalBytes;
        public float GlobalSpeed;
        public int QueuedCount;
        public int RunningCount;
        public int CompletedCount;

        public ModuleSummary[] Modules;
        public TaskSummary[] ActiveTasks;

        public struct ModuleSummary
        {
            public string Name;
            public int CompletedFiles;
            public int FailedFiles;
            public int TotalFiles;
            public long DownloadedBytes;
            public long TotalBytes;
            public string Status;
        }

        public struct TaskSummary
        {
            public string Module;
            public string FileName;
            public long Downloaded;
            public long Total;
            public string State;
            public int Priority;
            public int OriginalPriority;
            public double WaitSeconds;
            public int RetryCount;
            public int IntegrityRetry;
            public string ErrorCode;
        }
    }
}