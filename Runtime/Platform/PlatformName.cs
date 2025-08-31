namespace QHotUpdateSystem.Platform
{
    public static class PlatformName
    {
#if UNITY_STANDALONE_WIN
        public const string Current = "Windows";
#elif UNITY_ANDROID
        public const string Current = "Android";
#elif UNITY_IOS
        public const string Current = "iOS";
#elif UNITY_STANDALONE_OSX
        public const string Current = "OSX";
#else
        public const string Current = "Unknown";
#endif
    }
}