using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle更新控制器
    ///     功能：提供简单的API接口，封装整个热更新系统的复杂性
    ///     设计原则：外观模式，为复杂的子系统提供统一的简单接口
    /// </summary>
    public class AssetBundleUpdateController : MonoBehaviour
    {
        [Header("更新配置")] [SerializeField] private bool autoInitializeOnStart = true;
        [SerializeField] private bool showDebugLog = true;

        // 核心组件
        private AssetBundleDownloadManager downloadManager;
        public Action<List<string>, List<string>> OnAllDownloadsCompleted; // 所有下载完成
        public Action<string> OnBundleDownloadCompleted; // Bundle下载完成
        public Action<string, float> OnBundleDownloadProgress; // Bundle下载进度
        public Action<string> OnBundleDownloadStarted; // Bundle开始下载

        // 事件回调
        public Action<bool> OnInitializeCompleted; // 初始化完成
        public Action<float> OnTotalDownloadProgress; // 总下载进度

        // 初始化状态
        public bool IsInitialized { get; private set; }
        public bool IsInitializing { get; private set; }

        private void Start()
        {
            if (autoInitializeOnStart) Initialize();
        }

        private void OnDestroy()
        {
            // 清理事件回调
            OnInitializeCompleted = null;
            OnBundleDownloadStarted = null;
            OnBundleDownloadProgress = null;
            OnBundleDownloadCompleted = null;
            OnTotalDownloadProgress = null;
            OnAllDownloadsCompleted = null;
        }

        /// <summary>
        ///     初始化更新系统
        /// </summary>
        public void Initialize()
        {
            if (IsInitialized || IsInitializing)
            {
                LogDebug("更新系统已初始化或正在初始化中");
                return;
            }

            StartCoroutine(InitializeCoroutine());
        }

        /// <summary>
        ///     初始化协程
        /// </summary>
        private IEnumerator InitializeCoroutine()
        {
            IsInitializing = true;
            LogDebug("开始初始化AssetBundle更新系统");

            // 创建下载管理器
            downloadManager = gameObject.AddComponent<AssetBundleDownloadManager>();

            // 注册事件
            RegisterDownloadManagerEvents();

            // 初始化下载管理器
            yield return downloadManager.Initialize();

            IsInitializing = false;
            IsInitialized = true;

            LogDebug("AssetBundle更新系统初始化完成");
            OnInitializeCompleted?.Invoke(true);
        }

        /// <summary>
        ///     注册下载管理器事件
        /// </summary>
        private void RegisterDownloadManagerEvents()
        {
            downloadManager.OnSingleBundleStarted += bundleName =>
            {
                LogDebug($"开始下载: {bundleName}");
                OnBundleDownloadStarted?.Invoke(bundleName);
            };

            downloadManager.OnSingleBundleProgress += (bundleName, progress) => { OnBundleDownloadProgress?.Invoke(bundleName, progress); };

            downloadManager.OnSingleBundleCompleted += bundleName =>
            {
                LogDebug($"下载完成: {bundleName}");
                OnBundleDownloadCompleted?.Invoke(bundleName);
            };

            downloadManager.OnTotalProgress += progress => { OnTotalDownloadProgress?.Invoke(progress); };

            downloadManager.OnAllDownloadsCompleted += (successList, failureList) =>
            {
                LogDebug($"所有下载完成 - 成功: {successList.Count}, 失败: {failureList.Count}");
                OnAllDownloadsCompleted?.Invoke(successList, failureList);
            };
        }

        /// <summary>
        ///     更新必备资源包（游戏启动时调用）
        /// </summary>
        /// <param name="essentialBundles">必备资源包列表</param>
        public void UpdateEssentialBundles(List<string> essentialBundles)
        {
            if (!CheckInitialized()) return;

            LogDebug($"开始更新必备资源包: {string.Join(", ", essentialBundles)}");
            downloadManager.DownloadBundles(essentialBundles);
        }

        /// <summary>
        ///     更新指定资源包（游戏运行时调用）
        /// </summary>
        /// <param name="bundleName">资源包名称</param>
        /// <param name="forceUpdate">是否强制更新</param>
        public void UpdateBundle(string bundleName, bool forceUpdate = false)
        {
            if (!CheckInitialized()) return;

            LogDebug($"开始更新资源包: {bundleName} (强制更新: {forceUpdate})");
            downloadManager.DownloadBundle(bundleName, forceUpdate);
        }

        /// <summary>
        ///     更新多个资源包
        /// </summary>
        /// <param name="bundleNames">资源包名称列表</param>
        /// <param name="forceUpdate">是否强制更新</param>
        public void UpdateBundles(List<string> bundleNames, bool forceUpdate = false)
        {
            if (!CheckInitialized()) return;

            LogDebug($"开始更新资源包: {string.Join(", ", bundleNames)} (强制更新: {forceUpdate})");
            downloadManager.DownloadBundles(bundleNames, forceUpdate);
        }

        /// <summary>
        ///     取消所有下载
        /// </summary>
        public void CancelAllDownloads()
        {
            if (!CheckInitialized()) return;

            LogDebug("取消所有下载任务");
            downloadManager.CancelAllDownloads();
        }

        /// <summary>
        ///     检查是否正在下载
        /// </summary>
        public bool IsDownloading()
        {
            if (!IsInitialized) return false;
            return downloadManager.IsDownloading();
        }

        /// <summary>
        ///     获取下载统计信息
        /// </summary>
        public (int total, int completed, int failed, int active) GetDownloadStats()
        {
            if (!IsInitialized) return (0, 0, 0, 0);
            return downloadManager.GetDownloadStats();
        }

        /// <summary>
        ///     检查初始化状态
        /// </summary>
        private bool CheckInitialized()
        {
            if (!IsInitialized)
            {
                Debug.LogError("[UpdateController] 更新系统未初始化，请先调用Initialize()");
                return false;
            }

            return true;
        }

        /// <summary>
        ///     调试日志
        /// </summary>
        private void LogDebug(string message)
        {
            if (showDebugLog) Debug.Log($"[UpdateController] {message}");
        }
    }
}