namespace QHotUpdateSystem.Core
{
    /// <summary>
    /// 初始化参数（由外部业务在启动时传入）
    /// </summary>
    public class HotUpdateInitOptions
    {
        public string BaseUrl;
        public int MaxConcurrent = 4;
        public int MaxRetry = 3;
        public int TimeoutSeconds = 30;
        public bool EnableDebugLog = true;

        public Platform.IPlatformAdapter PlatformAdapter;
        public Utility.IJsonSerializer JsonSerializer;

        /// <summary>Hash 计算方式（"md5" / "sha1"）</summary>
        public string HashAlgo = "md5";
    }
}