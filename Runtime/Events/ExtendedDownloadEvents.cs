using System;

namespace QHotUpdateSystem.EventsSystem
{
    /// <summary>
    /// 扩展下载控制事件（可选订阅）
    /// </summary>
    public static class ExtendedDownloadEvents
    {
        public static event Action<string> OnModulePaused;
        public static event Action<string> OnModuleResumed;
        public static event Action<string> OnModuleCanceled;
        public static event Action OnAllCanceled;

        internal static void InvokeModulePaused(string m) => OnModulePaused?.Invoke(m);
        internal static void InvokeModuleResumed(string m) => OnModuleResumed?.Invoke(m);
        internal static void InvokeModuleCanceled(string m) => OnModuleCanceled?.Invoke(m);
        internal static void InvokeAllCanceled() => OnAllCanceled?.Invoke();
    }
}