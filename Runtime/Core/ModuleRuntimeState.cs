namespace QHotUpdateSystem.Core
{
    /// <summary>
    /// 模块运行期状态（进度与速率）
    /// </summary>
    public class ModuleRuntimeState
    {
        public string ModuleName;
        public ModuleStatus Status = ModuleStatus.NotInstalled;

        public long TotalBytes;
        public long DownloadedBytes;
        public int TotalFiles;
        public int CompletedFiles;
        public int FailedFiles;

        public string LastError;
        public float CurrentSpeed;

        public void ResetProgress()
        {
            DownloadedBytes = 0;
            CompletedFiles = 0;
            FailedFiles = 0;
            CurrentSpeed = 0;
            LastError = null;
        }
    }
}