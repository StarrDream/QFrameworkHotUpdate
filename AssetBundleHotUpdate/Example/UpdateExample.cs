using System;
using System.Collections.Generic;
using System.Reflection;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle更新系统使用示例
    ///     功能：演示如何使用更新系统进行必备资源和指定资源的更新
    /// </summary>
    public class UpdateExample : MonoBehaviour
    {
        [Header("UI组件")] [SerializeField] private Button initButton;
        [SerializeField] private Button updateEssentialButton;
        [SerializeField] private Button updateSpecificButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Text statusText;
        [SerializeField] private Text logText;

        [Header("首次启动检查-更新必备资源名称列表")] public AssetBundleNameScriptable essentialBundlesScriptable;


        [Header("游戏中-指定更新资源名称列表")] public AssetBundleNameScriptable specificBundlesScriptable;
        private string logContent = "";

        private AssetBundleUpdateController updateController;

        private void Start()
        {
            InitializeUI();
            SetupUpdateController();
        }

        private void OnDestroy()
        {
            // 清理事件
            if (updateController != null)
            {
                updateController.OnInitializeCompleted -= OnInitializeCompleted;
                updateController.OnBundleDownloadStarted -= OnBundleDownloadStarted;
                updateController.OnBundleDownloadProgress -= OnBundleDownloadProgress;
                updateController.OnBundleDownloadCompleted -= OnBundleDownloadCompleted;
                updateController.OnTotalDownloadProgress -= OnTotalDownloadProgress;
                updateController.OnAllDownloadsCompleted -= OnAllDownloadsCompleted;
            }
        }

        /// <summary>
        ///     初始化UI
        /// </summary>
        private void InitializeUI()
        {
            // 按钮事件
            initButton.onClick.AddListener(OnInitButtonClicked);
            updateEssentialButton.onClick.AddListener(OnUpdateEssentialButtonClicked);
            updateSpecificButton.onClick.AddListener(OnUpdateSpecificButtonClicked);
            cancelButton.onClick.AddListener(OnCancelButtonClicked);

            // 初始状态
            updateEssentialButton.interactable = false;
            updateSpecificButton.interactable = false;
            cancelButton.interactable = false;
            progressSlider.value = 0;
            statusText.text = "等待初始化...";

            UpdateLog("系统启动，等待初始化");
        }

        /// <summary>
        ///     设置更新控制器
        /// </summary>
        private void SetupUpdateController()
        {
            // 创建更新控制器
            var controllerObj = new GameObject("AssetBundleUpdateController");
            updateController = controllerObj.AddComponent<AssetBundleUpdateController>();

            // 注册事件
            updateController.OnInitializeCompleted += OnInitializeCompleted;
            updateController.OnBundleDownloadStarted += OnBundleDownloadStarted;
            updateController.OnBundleDownloadProgress += OnBundleDownloadProgress;
            updateController.OnBundleDownloadCompleted += OnBundleDownloadCompleted;
            updateController.OnTotalDownloadProgress += OnTotalDownloadProgress;
            updateController.OnAllDownloadsCompleted += OnAllDownloadsCompleted;
        }

        #region 按钮事件处理

        /// <summary>
        ///     初始化按钮点击
        /// </summary>
        private void OnInitButtonClicked()
        {
            UpdateLog("开始初始化更新系统...");
            statusText.text = "正在初始化...";
            initButton.interactable = false;

            updateController.Initialize();
        }

        /// <summary>
        ///     更新必备资源按钮点击
        /// </summary>
        private void OnUpdateEssentialButtonClicked()
        {
            UpdateLog($"开始更新必备资源: {string.Join(", ", essentialBundlesScriptable.assetBundleNames)}");
            statusText.text = "正在更新必备资源...";
            SetDownloadButtonsState(false);

            updateController.UpdateEssentialBundles(essentialBundlesScriptable.assetBundleNames);
        }

        /// <summary>
        ///     更新指定资源按钮点击
        /// </summary>
        private void OnUpdateSpecificButtonClicked()
        {
            UpdateLog($"开始更新指定资源: {string.Join(", ", specificBundlesScriptable.assetBundleNames)}");
            statusText.text = "正在更新指定资源...";
            SetDownloadButtonsState(false);

            updateController.UpdateBundles(specificBundlesScriptable.assetBundleNames);
        }

        /// <summary>
        ///     取消按钮点击
        /// </summary>
        private void OnCancelButtonClicked()
        {
            UpdateLog("取消所有下载任务");
            statusText.text = "已取消下载";

            updateController.CancelAllDownloads();
            SetDownloadButtonsState(true);
            progressSlider.value = 0;
        }

        #endregion

        #region 更新控制器事件处理

        /// <summary>
        ///     初始化完成
        /// </summary>
        private void OnInitializeCompleted(bool success)
        {
            if (success)
            {
                UpdateLog("更新系统初始化成功");
                statusText.text = "初始化完成，可以开始更新";
                updateEssentialButton.interactable = true;
                updateSpecificButton.interactable = true;
            }
            else
            {
                UpdateLog("更新系统初始化失败");
                statusText.text = "初始化失败";
                initButton.interactable = true;
            }
        }

        /// <summary>
        ///     Bundle开始下载
        /// </summary>
        private void OnBundleDownloadStarted(string bundleName)
        {
            UpdateLog($"开始下载: {bundleName}");
            cancelButton.interactable = true;
        }

        /// <summary>
        ///     Bundle下载进度
        /// </summary>
        private void OnBundleDownloadProgress(string bundleName, float progress)
        {
            // 这里可以显示单个Bundle的下载进度
            // 为了简化示例，我们只在日志中记录
            if (progress >= 1.0f) UpdateLog($"{bundleName} 下载进度: 100%");
        }

        /// <summary>
        ///     Bundle下载完成
        /// </summary>
        private void OnBundleDownloadCompleted(string bundleName)
        {
            UpdateLog($"下载完成: {bundleName}");
        }

        /// <summary>
        ///     总下载进度
        /// </summary>
        private void OnTotalDownloadProgress(float progress)
        {
            progressSlider.value = progress;
            statusText.text = $"下载进度: {progress:P}";
        }

        /// <summary>
        ///     所有下载完成
        /// </summary>
        private void OnAllDownloadsCompleted(List<string> successList, List<string> failureList)
        {
            cancelButton.interactable = false;
            SetDownloadButtonsState(true);
            progressSlider.value = 1.0f;

            if (failureList.Count == 0)
            {
                UpdateLog($"所有资源更新完成！成功: {successList.Count}");
                statusText.text = "更新完成";
            }
            else
            {
                UpdateLog($"更新完成，但有失败项 - 成功: {successList.Count}, 失败: {failureList.Count}");
                UpdateLog($"失败的Bundle: {string.Join(", ", failureList)}");
                statusText.text = "更新完成（部分失败）";
            }

            // 显示统计信息
            var stats = updateController.GetDownloadStats();
            UpdateLog($"下载统计 - 总计: {stats.total}, 完成: {stats.completed}, 失败: {stats.failed}");


            // 资源加载完后 加载程序集
            var dll = ResLoader.Allocate().LoadSync<TextAsset>("HotUpdate.dll");
            Assembly.Load(dll.bytes);
        }

        #endregion

        #region 辅助方法

        /// <summary>
        ///     设置下载按钮状态
        /// </summary>
        private void SetDownloadButtonsState(bool enabled)
        {
            updateEssentialButton.interactable = enabled;
            updateSpecificButton.interactable = enabled;
        }

        /// <summary>
        ///     更新日志显示
        /// </summary>
        private void UpdateLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            logContent += $"[{timestamp}] {message}\n";

            // 限制日志长度
            if (logContent.Length > 2000)
            {
                var firstNewLine = logContent.IndexOf('\n', 500);
                if (firstNewLine > 0) logContent = logContent.Substring(firstNewLine + 1);
            }

            if (logText != null) logText.text = logContent;

            Debug.Log($"[UpdateExample] {message}");
        }

        #endregion
    }
}