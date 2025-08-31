using UnityEngine;

namespace QHotUpdateSystem.Logging
{
    public static class HotUpdateLogger
    {
        public static bool EnableDebug = true;

        public static void Info(string msg)
        {
            if (EnableDebug) Debug.Log("[QHotUpdate] " + msg);
        }

        public static void Warn(string msg)
        {
            Debug.LogWarning("[QHotUpdate] " + msg);
        }

        public static void Error(string msg)
        {
            Debug.LogError("[QHotUpdate] " + msg);
        }
    }
}