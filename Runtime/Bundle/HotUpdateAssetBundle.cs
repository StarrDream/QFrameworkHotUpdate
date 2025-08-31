// ========================================
// 文件名: HotUpdateAssetBundle.cs
// 路径: Plugins/QHotUpdateSystem/Runtime/Bundle/HotUpdateAssetBundle.cs
// 批次2修改:
//   ★ 新增重载：支持 IProgress<float> 直接获取会话聚合进度
//   ★ 若无需下载(本地已存在)且传入 progress != null 则立即回调 1.0
// ========================================

using System;
using System.Threading.Tasks;
using UnityEngine;
using QHotUpdateSystem.BundleEvents;

namespace QHotUpdateSystem
{
    public static class HotUpdateAssetBundle
    {
        /// <summary>
        /// 最小实现（Batch1 保留）：不关心会话事件。
        /// </summary>
        public static async Task<AssetBundle> LoadAsync(
            string bundleName,
            bool autoDownload = true,
            Core.DownloadPriority priority = Core.DownloadPriority.High)
        {
            return await LoadAsync(bundleName, autoDownload, priority, progress: null);
        }

        /// <summary>
        /// 扩展：带进度回调的加载
        ///  - 若需要自动下载，会创建/复用 Bundle 会话并聚合进度
        ///  - progress.Report 在主线程回调（事件已在主线程派发）
        /// </summary>
        public static async Task<AssetBundle> LoadAsync(
            string bundleName,
            bool autoDownload,
            Core.DownloadPriority priority,
            IProgress<float> progress)
        {
            if (string.IsNullOrEmpty(bundleName))
                throw new ArgumentException("bundleName 不能为空");

            var mgr = HotUpdateManager.Instance;
            if (!mgr.IsInitialized)
                throw new InvalidOperationException("HotUpdateManager 未初始化");

            // 已存在直接加载
            if (mgr.IsBundleReady(bundleName))
            {
                progress?.Report(1f);
                return await mgr.LoadBundleAsync(bundleName);
            }

            if (!autoDownload)
                throw new InvalidOperationException($"本地缺失 Bundle: {bundleName}, 且 autoDownload = false");

            Guid? sessionId = await mgr.EnsureBundlesDownloadedSessionAsync(new[] { bundleName }, priority);

            // 如果需要跟踪进度，但 sessionId 为 null（表示无需下载）直接完成
            if (sessionId == null)
            {
                progress?.Report(1f);
            }
            else if (progress != null)
            {
                Guid sid = sessionId.Value;
                void ProgressHandler(BundleDownloadProgressInfo pi)
                {
                    if (pi.SessionId == sid)
                        progress.Report(pi.Progress);
                }
                void CompletedHandler(BundleDownloadResultInfo ri)
                {
                    if (ri.SessionId == sid)
                        progress.Report(1f);
                }
                void FailedHandler(BundleDownloadResultInfo ri)
                {
                    if (ri.SessionId == sid)
                        progress.Report(0f);
                }

                // 订阅
                BundleDownloadEvents.OnProgress += ProgressHandler;
                BundleDownloadEvents.OnCompleted += CompletedHandler;
                BundleDownloadEvents.OnFailed += FailedHandler;

                // 等待模块下载结束（会话级 Task 已在 EnsureBundlesDownloadedSessionAsync 内部管理）
                // 这里不单独等待事件，只等待实际下载完成后再解除订阅
                // => 因为下方的存在性检查和最终加载已经表征成功
                try
                {
                    // 等待目标 bundle 出现在本地（简易轮询，避免过度修改 DownloadManager）
                    // 若之前 Ensure 已经完成则立即跳过
                    int spin = 0;
                    while (!mgr.IsBundleReady(bundleName) && spin < 600) // 最长 ~60s（每100ms一次）
                    {
                        await Task.Delay(100);
                        spin++;
                    }
                }
                finally
                {
                    // 解绑（防泄露）
                    BundleDownloadEvents.OnProgress -= ProgressHandler;
                    BundleDownloadEvents.OnCompleted -= CompletedHandler;
                    BundleDownloadEvents.OnFailed -= FailedHandler;
                }
            }
            else
            {
                // 无进度回调，仅等待文件就绪（与上方一致）
                int spin = 0;
                while (!mgr.IsBundleReady(bundleName) && spin < 600)
                {
                    await Task.Delay(100);
                    spin++;
                }
            }

            if (!mgr.IsBundleReady(bundleName))
                throw new InvalidOperationException($"Bundle 下载后仍不存在或超时: {bundleName}");

            return await mgr.LoadBundleAsync(bundleName);
        }
    }
}
