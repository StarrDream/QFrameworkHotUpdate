using System;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Events;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.Download;
using QHotUpdateSystem.Diagnostics;

namespace QHotUpdateSystem.EventsSystem
{
    /// <summary>
    /// 事件中心（Batch4：新增文件级错误与诊断事件）
    /// 说明：
    /// - 新增 OnFileError 与 OnDiagnostics，不影响旧订阅。
    /// - 仍通过 MainThreadDispatcher 保证主线程派发。
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

        // Batch4 新增
        public static event Action<string, string, DownloadErrorCode, string> OnFileError;
        public static event Action<DownloadDiagnosticsSnapshot> OnDiagnostics;

        private static void Dispatch(Action a)
        {
            if (a == null) return;
            MainThreadDispatcher.Enqueue(a);
        }

        private static void Dispatch<T>(Action<T> a, T arg)
        {
            if (a == null) return;
            MainThreadDispatcher.Enqueue(() => a(arg));
        }

        private static void Dispatch<T1, T2>(Action<T1, T2> a, T1 a1, T2 a2)
        {
            if (a == null) return;
            MainThreadDispatcher.Enqueue(() => a(a1, a2));
        }

        private static void Dispatch<T1, T2, T3, T4>(Action<T1, T2, T3, T4> a, T1 a1, T2 a2, T3 a3, T4 a4)
        {
            if (a == null) return;
            MainThreadDispatcher.Enqueue(() => a(a1, a2, a3, a4));
        }

        internal static void InvokeRemoteVersion(VersionInfo v) => Dispatch(OnRemoteVersionReceived, v);
        internal static void InvokeModuleStatus(string module, ModuleStatus status) => Dispatch(OnModuleStatusChanged, module, status);
        internal static void InvokeFileProgress(string module, FileProgressInfo info) => Dispatch(OnFileProgress, module, info);
        internal static void InvokeModuleProgress(string module, ModuleProgressInfo info) => Dispatch(OnModuleProgress, module, info);
        internal static void InvokeGlobalProgress(GlobalProgressInfo info) => Dispatch(OnGlobalProgress, info);
        internal static void InvokeError(string module, string msg) => Dispatch(OnError, module, msg);
        internal static void InvokeAllTasksCompleted() => Dispatch(OnAllTasksCompleted);
        internal static void InvokeCoreReady() => Dispatch(OnCoreReady);

        // Batch4 新增调用方法
        internal static void InvokeFileError(string module, string file, DownloadErrorCode code, string msg)
            => Dispatch(OnFileError, module, file, code, msg);

        internal static void InvokeDiagnostics(DownloadDiagnosticsSnapshot snap)
            => Dispatch(OnDiagnostics, snap);
    }
}