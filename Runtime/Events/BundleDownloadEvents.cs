// ========================================
// 文件名: BundleDownloadEvents.cs
// 路径: Plugins/QHotUpdateSystem/Runtime/Events/BundleDownloadEvents.cs
// 说明:
//   Bundle 级补全下载会话事件（基于模块下载聚合）
//   - Start: 会话创建时（可能无需实际下载，仅用于统一语义）
//   - Progress: 按模块进度聚合（DownloadedBytes / TotalBytes）
//   - Completed: 所有关联模块成功（或无需下载）
//   - Failed: 任意关联模块失败
//   事件派发使用 MainThreadDispatcher（与 HotUpdateEvents 一致）
// ========================================

using System;
using QHotUpdateSystem.EventsSystem;

namespace QHotUpdateSystem.BundleEvents
{
    public struct BundleDownloadStartInfo
    {
        public Guid SessionId;
        public string[] RootBundles;
        public string[] ClosureBundles;
        public string[] Modules;
        public long TotalBytes; // 初始估算（可能为 0，后续 Progress 会刷新）
    }

    public struct BundleDownloadProgressInfo
    {
        public Guid SessionId;
        public long DownloadedBytes;
        public long TotalBytes;
        public float Progress => TotalBytes > 0 ? (float)DownloadedBytes / TotalBytes : 0f;
    }

    public struct BundleDownloadResultInfo
    {
        public Guid SessionId;
        public bool Success;
        public string[] Modules;
        public string FailedModule; // 失败时记录
        public string Message;
    }

    public static class BundleDownloadEvents
    {
        public static event Action<BundleDownloadStartInfo> OnStart;
        public static event Action<BundleDownloadProgressInfo> OnProgress;
        public static event Action<BundleDownloadResultInfo> OnCompleted;
        public static event Action<BundleDownloadResultInfo> OnFailed;

        internal static void InvokeStart(BundleDownloadStartInfo info) =>
            MainThreadDispatcher.Enqueue(() => OnStart?.Invoke(info));

        internal static void InvokeProgress(BundleDownloadProgressInfo info) =>
            MainThreadDispatcher.Enqueue(() => OnProgress?.Invoke(info));

        internal static void InvokeCompleted(BundleDownloadResultInfo info) =>
            MainThreadDispatcher.Enqueue(() => OnCompleted?.Invoke(info));

        internal static void InvokeFailed(BundleDownloadResultInfo info) =>
            MainThreadDispatcher.Enqueue(() => OnFailed?.Invoke(info));
    }
}
