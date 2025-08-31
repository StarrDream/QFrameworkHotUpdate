namespace QHotUpdateSystem.Events
{
    public struct FileProgressInfo
    {
        public string Module;
        public string FileName;
        public long Downloaded;
        public long Total;
        public float Progress => Total > 0 ? (float)Downloaded / Total : 0f;
        public float Speed;
    }

    public struct ModuleProgressInfo
    {
        public string Module;
        public long DownloadedBytes;
        public long TotalBytes;
        public int CompletedFiles;
        public int TotalFiles;
        public float Speed;
        public float Progress => TotalBytes > 0 ? (float)DownloadedBytes / TotalBytes : 0f;
    }

    public struct GlobalProgressInfo
    {
        public long DownloadedBytes;
        public long TotalBytes;
        public float Speed;
        public float Progress => TotalBytes > 0 ? (float)DownloadedBytes / TotalBytes : 0f;
    }
}