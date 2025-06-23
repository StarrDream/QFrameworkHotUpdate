using System;
using System.Collections;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle下载器
    ///     功能：负责单个AB包的下载，包括进度监控、完整性验证等
    ///     设计原则：单一职责，每个下载器只负责一个下载任务，完成后自动销毁
    /// </summary>
    public class AssetBundleDownloader
    {
        private readonly MonoBehaviour coroutineRunner;
        private UnityWebRequest currentRequest;
        public Action<AssetBundleDownloader, DownloadResult> OnDownloadCompleted;

        // 事件回调
        public Action<AssetBundleDownloader> OnDownloadStarted;
        public Action<AssetBundleDownloader, float> OnProgressChanged;

        /// <summary>
        ///     构造函数
        /// </summary>
        /// <param name="bundleInfo">要下载的AB包信息</param>
        /// <param name="runner">协程运行器</param>
        public AssetBundleDownloader(AssetBundleInfo bundleInfo, MonoBehaviour runner)
        {
            this.BundleInfo = bundleInfo;
            coroutineRunner = runner;
            TotalBytes = bundleInfo.size;
        }

        // 下载状态
        public bool IsDownloading { get; private set; }
        public bool IsCompleted { get; private set; }
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }

        // 进度信息
        public float Progress { get; private set; }
        public long DownloadedBytes { get; private set; }
        public long TotalBytes { get; }

        // 属性
        public string BundleName => BundleInfo.bundleName;
        public AssetBundleInfo BundleInfo { get; }

        /// <summary>
        ///     开始下载
        /// </summary>
        public void StartDownload()
        {
            if (IsDownloading || IsCompleted)
            {
                Debug.LogWarning($"[Downloader] 下载器状态异常: {BundleInfo.bundleName}");
                return;
            }

            coroutineRunner.StartCoroutine(DownloadCoroutine());
        }

        /// <summary>
        ///     取消下载
        /// </summary>
        public void CancelDownload()
        {
            if (currentRequest != null && !currentRequest.isDone)
            {
                currentRequest.Abort();
                Debug.Log($"[Downloader] 取消下载: {BundleInfo.bundleName}");
            }

            Cleanup();
        }

        /// <summary>
        ///     下载协程
        /// </summary>
        private IEnumerator DownloadCoroutine()
        {
            IsDownloading = true;
            OnDownloadStarted?.Invoke(this);

            var url = AssetBundleConfig.GetServerUrl(BundleInfo.bundleName);
            var savePath = AssetBundleConfig.GetLocalBundlePath(BundleInfo.bundleName);

            Debug.Log($"[Downloader] 开始下载: {BundleInfo.bundleName}");
            Debug.Log($"[Downloader] URL: {url}");
            Debug.Log($"[Downloader] 保存路径: {savePath}");

            // 确保目录存在
            AssetBundleUtility.EnsureDirectoryExists(savePath);

            using (currentRequest = UnityWebRequest.Get(url))
            {
                var operation = currentRequest.SendWebRequest();

                // 监控下载进度
                while (!operation.isDone)
                {
                    UpdateProgress();
                    yield return null;
                }

                // 最后更新一次进度
                UpdateProgress();

                // 处理下载结果
                yield return ProcessDownloadResult(savePath);
            }

            IsDownloading = false;
            IsCompleted = true;

            // 清理资源
            Cleanup();
        }

        /// <summary>
        ///     更新下载进度
        /// </summary>
        private void UpdateProgress()
        {
            if (currentRequest != null)
            {
                Progress = currentRequest.downloadProgress;
                DownloadedBytes = (long)(Progress * TotalBytes);
                OnProgressChanged?.Invoke(this, Progress);
            }
        }

        /// <summary>
        ///     处理下载结果
        /// </summary>
        private IEnumerator ProcessDownloadResult(string savePath)
        {
            if (currentRequest.result == UnityWebRequest.Result.Success)
            {
                // try
                // {
                // 保存文件
                File.WriteAllBytes(savePath, currentRequest.downloadHandler.data);
                Debug.Log($"[Downloader] 文件保存成功: {savePath}");

                // 验证文件完整性
                yield return ValidateDownloadedFile(savePath);
                // }
                // catch (System.Exception e)
                // {
                //     HandleDownloadError($"保存文件失败: {e.Message}");
                // }
            }
            else
            {
                HandleDownloadError($"下载失败: {currentRequest.error}");
            }
        }

        /// <summary>
        ///     验证下载的文件
        /// </summary>
        private IEnumerator ValidateDownloadedFile(string filePath)
        {
            Debug.Log($"[Downloader] 开始验证文件: {BundleInfo.bundleName}");

            // 在后台线程计算哈希值（避免阻塞主线程）
            var validationCompleted = false;
            var validationResult = false;
            var actualHash = "";

            var validationThread = new Thread(() =>
            {
                try
                {
                    actualHash = AssetBundleUtility.CalculateFileHash(filePath);
                    validationResult = actualHash.Equals(BundleInfo.hash, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Downloader] 文件验证异常: {e.Message}");
                    validationResult = false;
                }
                finally
                {
                    validationCompleted = true;
                }
            });

            validationThread.Start();

            // 等待验证完成
            while (!validationCompleted) yield return null;

            if (validationResult)
            {
                Debug.Log($"[Downloader] 文件验证成功: {BundleInfo.bundleName}");
                HandleDownloadSuccess();
            }
            else
            {
                Debug.LogError($"[Downloader] 文件验证失败: {BundleInfo.bundleName}");
                Debug.LogError($"[Downloader] 期望哈希: {BundleInfo.hash}");
                Debug.LogError($"[Downloader] 实际哈希: {actualHash}");

                // 删除损坏的文件
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Downloader] 删除损坏文件失败: {e.Message}");
                }

                HandleDownloadError("文件完整性验证失败");
            }
        }

        /// <summary>
        ///     处理下载成功
        /// </summary>
        private void HandleDownloadSuccess()
        {
            IsSuccess = true;
            var result = DownloadResult.CreateSuccess(BundleInfo.bundleName);
            OnDownloadCompleted?.Invoke(this, result);

            Debug.Log($"[Downloader] 下载完成: {BundleInfo.bundleName} ({AssetBundleUtility.FormatFileSize(BundleInfo.size)})");
        }

        /// <summary>
        ///     处理下载错误
        /// </summary>
        private void HandleDownloadError(string error)
        {
            IsSuccess = false;
            ErrorMessage = error;
            var result = DownloadResult.CreateFailure(BundleInfo.bundleName, error);
            OnDownloadCompleted?.Invoke(this, result);

            Debug.LogError($"[Downloader] 下载失败: {BundleInfo.bundleName} - {error}");
        }

        /// <summary>
        ///     清理资源
        /// </summary>
        private void Cleanup()
        {
            currentRequest?.Dispose();
            currentRequest = null;

            // 清空事件回调，避免内存泄漏
            OnDownloadStarted = null;
            OnProgressChanged = null;
            OnDownloadCompleted = null;
        }

        /// <summary>
        ///     获取下载进度信息
        /// </summary>
        public DownloadProgress GetProgressInfo()
        {
            return new DownloadProgress
            {
                BundleName = BundleInfo.bundleName,
                Progress = Progress,
                DownloadedBytes = DownloadedBytes,
                TotalBytes = TotalBytes
            };
        }
    }
}