using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle下载管理器
    ///     功能：管理多个下载任务，处理依赖关系，协调下载顺序
    ///     设计原则：管理者模式，负责创建和管理下载器，下载完成后自动销毁下载器
    /// </summary>
    public class AssetBundleDownloadManager : MonoBehaviour
    {
        [Header("下载配置")] [SerializeField] private int maxConcurrentDownloads = 3; // 最大并发下载数
        [SerializeField] private float timeoutSeconds = 30f; // 下载超时时间

        // 下载状态管理
        private readonly List<AssetBundleDownloader> activeDownloaders = new();
        private readonly HashSet<string> completedBundles = new();
        private int completedBundlesCount;
        private AssetBundleDependencyManager dependencyManager;
        private long downloadedBytes;
        private readonly Queue<AssetBundleInfo> downloadQueue = new();
        private readonly HashSet<string> failedBundles = new();
        public Action<List<string>, List<string>> OnAllDownloadsCompleted; // 全部完成 (成功列表, 失败列表)
        public Action<string> OnSingleBundleCompleted; // 单个Bundle完成
        public Action<string, float> OnSingleBundleProgress; // 单个Bundle进度

        // 事件回调
        public Action<string> OnSingleBundleStarted; // 单个Bundle开始下载
        public Action<float> OnTotalProgress; // 总进度

        // 统计信息
        private int totalBundlesToDownload;
        private long totalBytesToDownload;

        // 核心组件
        private AssetBundleVersionManager versionManager;

        private void OnDestroy()
        {
            CancelAllDownloads();
        }

        /// <summary>
        ///     初始化下载管理器
        /// </summary>
        public IEnumerator Initialize()
        {
            Debug.Log("[DownloadManager] 初始化下载管理器");

            // 初始化版本管理器
            versionManager = new AssetBundleVersionManager();
            yield return versionManager.Initialize();

            // 初始化依赖管理器
            dependencyManager = new AssetBundleDependencyManager();
            if (versionManager.RemoteManifest != null)
            {
                dependencyManager.BuildDependencyGraph(versionManager.RemoteManifest.assetBundles);

                // 检查循环依赖
                var circularDeps = dependencyManager.DetectCircularDependencies();
                if (circularDeps.Count > 0)
                {
                    Debug.LogError($"[DownloadManager] 检测到循环依赖，无法继续: {string.Join(", ", circularDeps)}");
                    yield break;
                }
            }

            Debug.Log("[DownloadManager] 下载管理器初始化完成");
        }

        /// <summary>
        ///     下载指定的AB包（包括依赖）
        /// </summary>
        /// <param name="bundleNames">要下载的AB包名称列表</param>
        /// <param name="forceDownload">是否强制下载</param>
        public void DownloadBundles(List<string> bundleNames, bool forceDownload = false)
        {
            if (!versionManager.IsInitialized)
            {
                Debug.LogError("[DownloadManager] 版本管理器未初始化");
                return;
            }

            if (IsDownloading())
            {
                Debug.LogWarning("[DownloadManager] 已有下载任务在进行中");
                return;
            }

            StartCoroutine(DownloadBundlesCoroutine(bundleNames, forceDownload));
        }

        /// <summary>
        ///     下载单个AB包（包括依赖）
        /// </summary>
        /// <param name="bundleName">AB包名称</param>
        /// <param name="forceDownload">是否强制下载</param>
        public void DownloadBundle(string bundleName, bool forceDownload = false)
        {
            DownloadBundles(new List<string> { bundleName }, forceDownload);
        }

        /// <summary>
        ///     下载协程
        /// </summary>
        private IEnumerator DownloadBundlesCoroutine(List<string> bundleNames, bool forceDownload)
        {
            Debug.Log($"[DownloadManager] 开始下载任务: {string.Join(", ", bundleNames)}");

            // 重置状态
            ResetDownloadState();

            // 获取需要下载的Bundle信息
            var bundlesToDownload = GetBundlesToDownload(bundleNames, forceDownload);

            if (bundlesToDownload.Count == 0)
            {
                Debug.Log("[DownloadManager] 没有需要下载的Bundle");
                OnAllDownloadsCompleted?.Invoke(new List<string>(), new List<string>());
                yield break;
            }

            // 计算下载顺序
            var downloadOrder = dependencyManager.GetDownloadOrder(bundlesToDownload.Select(b => b.bundleName).ToList());

            // 按下载顺序重新排列Bundle信息
            var orderedBundles = new List<AssetBundleInfo>();
            foreach (var bundleName in downloadOrder)
            {
                var bundleInfo = bundlesToDownload.FirstOrDefault(b => b.bundleName == bundleName);
                if (bundleInfo != null) orderedBundles.Add(bundleInfo);
            }

            // 初始化统计信息
            InitializeDownloadStats(orderedBundles);

            // 将Bundle添加到下载队列
            foreach (var bundle in orderedBundles) downloadQueue.Enqueue(bundle);

            Debug.Log($"[DownloadManager] 准备下载 {orderedBundles.Count} 个Bundle，总大小: {AssetBundleUtility.FormatFileSize(totalBytesToDownload)}");

            // 开始下载处理循环
            yield return StartDownloadLoop();

            // 下载配置文件并刷新ResKit
            yield return DownloadConfigFile();
            AssetBundleUtility.RefreshResKitConfig();

            // 完成回调
            var successList = completedBundles.ToList();
            var failureList = failedBundles.ToList();
            OnAllDownloadsCompleted?.Invoke(successList, failureList);

            Debug.Log($"[DownloadManager] 下载任务完成 - 成功: {successList.Count}, 失败: {failureList.Count}");
        }

        /// <summary>
        ///     获取需要下载的Bundle列表
        /// </summary>
        private List<AssetBundleInfo> GetBundlesToDownload(List<string> bundleNames, bool forceDownload)
        {
            var result = new List<AssetBundleInfo>();

            foreach (var bundleName in bundleNames)
                // 检查Bundle是否需要更新
                if (forceDownload || versionManager.CheckBundleNeedUpdate(bundleName))
                {
                    var bundleInfo = versionManager.RemoteManifest.assetBundles.FirstOrDefault(b => b.bundleName == bundleName);
                    if (bundleInfo != null && bundleInfo.enableUpdate)
                    {
                        result.Add(bundleInfo);

                        // 添加依赖
                        foreach (var dependency in bundleInfo.allDependencies)
                            if (forceDownload || versionManager.CheckBundleNeedUpdate(dependency))
                            {
                                var depInfo = versionManager.RemoteManifest.assetBundles.FirstOrDefault(b => b.bundleName == dependency);
                                if (depInfo != null && depInfo.enableUpdate && !result.Any(b => b.bundleName == dependency)) result.Add(depInfo);
                            }
                    }
                }

            return result;
        }

        /// <summary>
        ///     初始化下载统计信息
        /// </summary>
        private void InitializeDownloadStats(List<AssetBundleInfo> bundles)
        {
            totalBundlesToDownload = bundles.Count;
            completedBundlesCount = 0;
            totalBytesToDownload = bundles.Sum(b => b.size);
            downloadedBytes = 0;
        }

        /// <summary>
        ///     开始下载循环
        /// </summary>
        private IEnumerator StartDownloadLoop()
        {
            while (downloadQueue.Count > 0 || activeDownloaders.Count > 0)
            {
                // 启动新的下载任务（在并发限制内）
                while (downloadQueue.Count > 0 && activeDownloaders.Count < maxConcurrentDownloads)
                {
                    var bundleInfo = downloadQueue.Dequeue();
                    StartSingleDownload(bundleInfo);
                }

                // 清理已完成的下载器
                CleanupCompletedDownloaders();

                // 更新总进度
                UpdateTotalProgress();

                yield return null;
            }
        }

        /// <summary>
        ///     启动单个下载任务
        /// </summary>
        private void StartSingleDownload(AssetBundleInfo bundleInfo)
        {
            var downloader = new AssetBundleDownloader(bundleInfo, this);

            // 注册事件
            downloader.OnDownloadStarted += OnDownloaderStarted;
            downloader.OnProgressChanged += OnDownloaderProgress;
            downloader.OnDownloadCompleted += OnDownloaderCompleted;

            activeDownloaders.Add(downloader);
            downloader.StartDownload();

            Debug.Log($"[DownloadManager] 启动下载: {bundleInfo.bundleName} (活跃下载器: {activeDownloaders.Count})");
        }

        /// <summary>
        ///     下载器开始回调
        /// </summary>
        private void OnDownloaderStarted(AssetBundleDownloader downloader)
        {
            OnSingleBundleStarted?.Invoke(downloader.BundleName);
        }

        /// <summary>
        ///     下载器进度回调
        /// </summary>
        private void OnDownloaderProgress(AssetBundleDownloader downloader, float progress)
        {
            OnSingleBundleProgress?.Invoke(downloader.BundleName, progress);
        }

        /// <summary>
        ///     下载器完成回调
        /// </summary>
        private void OnDownloaderCompleted(AssetBundleDownloader downloader, DownloadResult result)
        {
            if (result.Success)
            {
                completedBundles.Add(result.BundleName);
                completedBundlesCount++;
                downloadedBytes += downloader.BundleInfo.size;

                // 更新本地版本清单
                versionManager.UpdateLocalManifest(downloader.BundleInfo);

                OnSingleBundleCompleted?.Invoke(result.BundleName);
                Debug.Log($"[DownloadManager] Bundle下载成功: {result.BundleName}");
            }
            else
            {
                failedBundles.Add(result.BundleName);
                Debug.LogError($"[DownloadManager] Bundle下载失败: {result.BundleName} - {result.ErrorMessage}");
            }
        }

        /// <summary>
        ///     清理已完成的下载器
        /// </summary>
        private void CleanupCompletedDownloaders()
        {
            for (var i = activeDownloaders.Count - 1; i >= 0; i--)
                if (activeDownloaders[i].IsCompleted)
                    activeDownloaders.RemoveAt(i);
        }

        /// <summary>
        ///     更新总进度
        /// </summary>
        private void UpdateTotalProgress()
        {
            if (totalBundlesToDownload == 0) return;

            // 计算基于完成数量的进度
            var countProgress = (float)completedBundlesCount / totalBundlesToDownload;

            // 计算基于字节数的进度
            var currentDownloadedBytes = downloadedBytes;
            foreach (var downloader in activeDownloaders) currentDownloadedBytes += downloader.DownloadedBytes;

            var bytesProgress = totalBytesToDownload > 0 ? (float)currentDownloadedBytes / totalBytesToDownload : 0f;

            // 使用字节进度，更精确
            var totalProgress = Mathf.Clamp01(bytesProgress);
            OnTotalProgress?.Invoke(totalProgress);
        }

        /// <summary>
        ///     下载配置文件
        /// </summary>
        private IEnumerator DownloadConfigFile()
        {
            Debug.Log("[DownloadManager] 开始下载配置文件");

            var configUrl = AssetBundleConfig.GetServerUrl(AssetBundleConfig.ConfigFileName);
            var configPath = Path.Combine(Application.streamingAssetsPath, AssetBundleConfig.ConfigFileName);

            AssetBundleUtility.EnsureDirectoryExists(configPath);

            using (var request = UnityWebRequest.Get(configUrl))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllBytes(configPath, request.downloadHandler.data);
                    Debug.Log($"[DownloadManager] 配置文件下载成功: {configPath}");

                    // 也复制到AssetBundles目录
                    var bundleConfigPath = AssetBundleConfig.GetLocalBundlePath(AssetBundleConfig.ConfigFileName);
                    File.WriteAllBytes(bundleConfigPath, request.downloadHandler.data);
                }
                else
                {
                    Debug.LogError($"[DownloadManager] 配置文件下载失败: {request.error}");
                }
            }
        }

        /// <summary>
        ///     重置下载状态
        /// </summary>
        private void ResetDownloadState()
        {
            // 取消所有活跃的下载
            foreach (var downloader in activeDownloaders) downloader.CancelDownload();

            activeDownloaders.Clear();
            downloadQueue.Clear();
            completedBundles.Clear();
            failedBundles.Clear();

            totalBundlesToDownload = 0;
            completedBundlesCount = 0;
            totalBytesToDownload = 0;
            downloadedBytes = 0;
        }

        /// <summary>
        ///     取消所有下载
        /// </summary>
        public void CancelAllDownloads()
        {
            Debug.Log("[DownloadManager] 取消所有下载任务");
            ResetDownloadState();
        }

        /// <summary>
        ///     检查是否正在下载
        /// </summary>
        public bool IsDownloading()
        {
            return activeDownloaders.Count > 0 || downloadQueue.Count > 0;
        }

        /// <summary>
        ///     获取下载统计信息
        /// </summary>
        public (int total, int completed, int failed, int active) GetDownloadStats()
        {
            return (totalBundlesToDownload, completedBundlesCount, failedBundles.Count, activeDownloaders.Count);
        }
    }
}