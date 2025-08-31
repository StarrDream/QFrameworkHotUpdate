using UnityEditor;

namespace QHotUpdateSystem.Editor.Utils
{
    /// <summary>
    /// 平台辅助：决定 version_{platform}.json 的平台名应与运行期 PlatformName 匹配
    /// </summary>
    public static class PlatformBuildUtility
    {
        public static string GetCurrentPlatformName()
        {
#if UNITY_STANDALONE_WIN
            return "Windows";
#elif UNITY_ANDROID
            return "Android";
#elif UNITY_IOS
            return "iOS";
#elif UNITY_STANDALONE_OSX
            return "OSX";
#else
            return "Unknown";
#endif
        }
    }
}