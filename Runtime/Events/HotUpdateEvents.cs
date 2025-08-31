using System;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Events;
using QHotUpdateSystem.Core;

namespace QHotUpdateSystem.EventsSystem
{
    /// <summary>
    /// 热更新事件集中管理
    /// </summary>
    public static class HotUpdateEvents
    {
        public static event Action<VersionInfo> OnRemoteVersionReceived;
        public static event Action<string, ModuleStatus> OnModuleStatusChanged;
        public static event Action<string, FileProgressInfo> OnFileProgress;
        public static event Action<string, ModuleProgressInfo> OnModuleProgress;
        public static event Action<GlobalProgressInfo> OnGlobalProgress;
        public static event Action<string, string> OnError;
        public static event Action OnAllTasksCompleted;
        public static event Action OnCoreReady;

        internal static void InvokeRemoteVersion(VersionInfo v) => OnRemoteVersionReceived?.Invoke(v);
        internal static void InvokeModuleStatus(string module, ModuleStatus status) => OnModuleStatusChanged?.Invoke(module, status);
        internal static void InvokeFileProgress(string module, FileProgressInfo info) => OnFileProgress?.Invoke(module, info);
        internal static void InvokeModuleProgress(string module, ModuleProgressInfo info) => OnModuleProgress?.Invoke(module, info);
        internal static void InvokeGlobalProgress(GlobalProgressInfo info) => OnGlobalProgress?.Invoke(info);
        internal static void InvokeError(string module, string msg) => OnError?.Invoke(module, msg);
        internal static void InvokeAllTasksCompleted() => OnAllTasksCompleted?.Invoke();
        internal static void InvokeCoreReady() => OnCoreReady?.Invoke();
    }
}