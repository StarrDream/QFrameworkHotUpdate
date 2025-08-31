using System;
using System.Collections.Generic;
using UnityEngine;
using QHotUpdateSystem;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.EventsSystem;
using QHotUpdateSystem.Events;
using QHotUpdateSystem.Security;
using QHotUpdateSystem.Version;

namespace QHotUpdateSystem
{
    /// <summary>
    /// 热更新系统API工作流完整示例
    /// </summary>
    public class HotUpdateAPIExample : MonoBehaviour
    {
        [Header("配置")] public string baseUrl = "https://your-cdn.com/HotUpdate/";
        public string hmacSecret = "your-secret-key"; // 用于版本签名验证
        public bool enableSignatureVerify = true;

        [Header("UI引用")] public UnityEngine.UI.Button checkUpdateButton;
        public UnityEngine.UI.Button startUpdateButton;
        public UnityEngine.UI.Button pauseButton;
        public UnityEngine.UI.Button resumeButton;
        public UnityEngine.UI.Button cancelButton;
        public UnityEngine.UI.Slider progressSlider;
        public UnityEngine.UI.Text statusText;
        public UnityEngine.UI.Text speedText;
        public UnityEngine.UI.Text moduleStatusText;

        private HotUpdateManager _hotUpdateManager;
        private bool _isUpdating = false;
        private List<string> _pendingModules = new List<string>();

        async void Start()
        {
            // 1. 初始化热更新系统
            await InitializeHotUpdateSystem();

            // 2. 设置UI事件
            SetupUI();

            // 3. 订阅热更新事件
            SubscribeToEvents();

            // 4. 检查Core模块状态
            CheckCoreModuleStatus();
        }

        /// <summary>
        /// 初始化热更新系统
        /// </summary>
        private async System.Threading.Tasks.Task InitializeHotUpdateSystem()
        {
            try
            {
                _hotUpdateManager = HotUpdateManager.Instance;

                // 配置初始化选项
                var options = new HotUpdateInitOptions
                {
                    BaseUrl = baseUrl,
                    MaxConcurrent = 4, // 最大并发下载数
                    MaxRetry = 3, // 最大重试次数
                    TimeoutSeconds = 30, // 超时时间
                    EnableDebugLog = true, // 启用调试日志
                    HashAlgo = "md5" // Hash算法
                };

                // 配置签名验证（可选）
                if (enableSignatureVerify && !string.IsNullOrEmpty(hmacSecret))
                {
                    var verifier = new HmacVersionSignatureVerifier(hmacSecret);
                    _hotUpdateManager.ConfigureSignatureVerifier(verifier, false);
                }

                // 初始化系统
                await _hotUpdateManager.Initialize(options);

                UpdateStatusText("热更新系统初始化完成");
                Debug.Log("热更新系统初始化成功");
            }
            catch (Exception ex)
            {
                UpdateStatusText($"初始化失败: {ex.Message}");
                Debug.LogError($"热更新系统初始化失败: {ex}");
            }
        }

        /// <summary>
        /// 设置UI事件
        /// </summary>
        private void SetupUI()
        {
            if (checkUpdateButton) checkUpdateButton.onClick.AddListener(OnCheckUpdateClicked);
            if (startUpdateButton) startUpdateButton.onClick.AddListener(OnStartUpdateClicked);
            if (pauseButton) pauseButton.onClick.AddListener(OnPauseClicked);
            if (resumeButton) resumeButton.onClick.AddListener(OnResumeClicked);
            if (cancelButton) cancelButton.onClick.AddListener(OnCancelClicked);

            // 初始UI状态
            UpdateUIState();
        }

        /// <summary>
        /// 订阅热更新事件
        /// </summary>
        private void SubscribeToEvents()
        {
            // 远程版本接收事件
            HotUpdateEvents.OnRemoteVersionReceived += OnRemoteVersionReceived;

            // 模块状态变化事件
            HotUpdateEvents.OnModuleStatusChanged += OnModuleStatusChanged;

            // 文件下载进度事件
            HotUpdateEvents.OnFileProgress += OnFileProgress;

            // 模块下载进度事件
            HotUpdateEvents.OnModuleProgress += OnModuleProgress;

            // 全局下载进度事件
            HotUpdateEvents.OnGlobalProgress += OnGlobalProgress;

            // 错误事件
            HotUpdateEvents.OnError += OnError;

            // 所有任务完成事件
            HotUpdateEvents.OnAllTasksCompleted += OnAllTasksCompleted;

            // Core模块准备就绪事件
            HotUpdateEvents.OnCoreReady += OnCoreReady;

            // 扩展事件（暂停/恢复/取消）
            ExtendedDownloadEvents.OnModulePaused += OnModulePaused;
            ExtendedDownloadEvents.OnModuleResumed += OnModuleResumed;
            ExtendedDownloadEvents.OnModuleCanceled += OnModuleCanceled;
        }

        #region UI事件处理

        private void OnCheckUpdateClicked()
        {
            if (!_hotUpdateManager.IsInitialized)
            {
                UpdateStatusText("系统未初始化");
                return;
            }

            CheckForUpdates();
        }

        private async void OnStartUpdateClicked()
        {
            if (_pendingModules.Count == 0)
            {
                UpdateStatusText("没有需要更新的模块");
                return;
            }

            _isUpdating = true;
            UpdateUIState();

            try
            {
                // 开始更新模块（可以指定优先级）
                await _hotUpdateManager.UpdateModules(_pendingModules, DownloadPriority.High);
                UpdateStatusText("开始下载更新...");
            }
            catch (Exception ex)
            {
                UpdateStatusText($"更新失败: {ex.Message}");
                _isUpdating = false;
                UpdateUIState();
            }
        }

        private void OnPauseClicked()
        {
            foreach (var module in _pendingModules)
            {
                _hotUpdateManager.PauseModule(module);
            }

            UpdateStatusText("已暂停下载");
        }

        private void OnResumeClicked()
        {
            foreach (var module in _pendingModules)
            {
                _hotUpdateManager.ResumeModule(module);
            }

            UpdateStatusText("已恢复下载");
        }

        private void OnCancelClicked()
        {
            _hotUpdateManager.CancelAll();
            _isUpdating = false;
            _pendingModules.Clear();
            UpdateUIState();
            UpdateStatusText("已取消所有下载");
        }

        #endregion

        #region 热更新事件回调

        private void OnRemoteVersionReceived(VersionInfo versionInfo)
        {
            Debug.Log($"接收到远程版本: {versionInfo.version}, 平台: {versionInfo.platform}");
            UpdateStatusText($"远程版本: {versionInfo.version}");
        }

        private void OnModuleStatusChanged(string module, ModuleStatus status)
        {
            Debug.Log($"模块 {module} 状态变更: {status}");
            UpdateModuleStatusText($"{module}: {status}");

            // 根据状态更新UI
            switch (status)
            {
                case ModuleStatus.Updated:
                    _pendingModules.Remove(module);
                    if (_pendingModules.Count == 0)
                    {
                        _isUpdating = false;
                        UpdateUIState();
                    }

                    break;
                case ModuleStatus.Failed:
                    UpdateStatusText($"模块 {module} 更新失败");
                    break;
            }
        }

        private void OnFileProgress(string module, FileProgressInfo info)
        {
            // 可以显示具体文件的下载进度
            Debug.Log($"文件 {info.FileName} 下载进度: {info.Progress:P2}");
        }

        private void OnModuleProgress(string module, ModuleProgressInfo info)
        {
            Debug.Log($"模块 {module} 进度: {info.Progress:P2}, 速度: {info.Speed / 1024:F1} KB/s");
        }

        private void OnGlobalProgress(GlobalProgressInfo info)
        {
            // 更新全局进度UI
            if (progressSlider)
                progressSlider.value = info.Progress;

            if (speedText)
                speedText.text = $"速度: {info.Speed / 1024:F1} KB/s";

            UpdateStatusText($"总进度: {info.Progress:P2} ({info.DownloadedBytes}/{info.TotalBytes})");
        }

        private void OnError(string module, string message)
        {
            Debug.LogError($"模块 {module} 错误: {message}");
            UpdateStatusText($"错误: {message}");
        }

        private void OnAllTasksCompleted()
        {
            Debug.Log("所有下载任务完成");
            UpdateStatusText("所有更新完成!");
            _isUpdating = false;
            _pendingModules.Clear();
            UpdateUIState();
        }

        private void OnCoreReady()
        {
            Debug.Log("Core模块准备就绪");
            UpdateStatusText("Core模块已就绪，可以使用应用程序");
        }

        private void OnModulePaused(string module)
        {
            Debug.Log($"模块 {module} 已暂停");
        }

        private void OnModuleResumed(string module)
        {
            Debug.Log($"模块 {module} 已恢复");
        }

        private void OnModuleCanceled(string module)
        {
            Debug.Log($"模块 {module} 已取消");
        }

        #endregion

        #region 业务逻辑

        /// <summary>
        /// 检查更新
        /// </summary>
        private void CheckForUpdates()
        {
            _pendingModules.Clear();

            // 检查已安装的模块
            var installedModules = new List<string>(_hotUpdateManager.GetInstalledModules());
            Debug.Log($"已安装模块: {string.Join(", ", installedModules)}");

            // 这里可以根据具体业务逻辑决定需要更新哪些模块
            // 示例：检查所有非Core模块的状态
            string[] allModules = { "UI", "Audio", "GameContent", "Localization" };

            foreach (var module in allModules)
            {
                var status = _hotUpdateManager.GetModuleStatus(module);
                Debug.Log($"模块 {module} 状态: {status}");

                switch (status)
                {
                    case ModuleStatus.NotInstalled:
                    case ModuleStatus.Partial:
                        _pendingModules.Add(module);
                        break;
                }
            }

            if (_pendingModules.Count > 0)
            {
                UpdateStatusText($"发现 {_pendingModules.Count} 个模块需要更新");
                Debug.Log($"需要更新的模块: {string.Join(", ", _pendingModules)}");
            }
            else
            {
                UpdateStatusText("所有模块都是最新版本");
            }

            UpdateUIState();
        }

        /// <summary>
        /// 检查Core模块状态
        /// </summary>
        private void CheckCoreModuleStatus()
        {
            if (!_hotUpdateManager.IsCoreReady)
            {
                UpdateStatusText("Core模块需要更新，正在自动下载...");
                // HotUpdateManager会自动处理Core模块的更新
            }
            else
            {
                UpdateStatusText("Core模块已就绪");
            }
        }

        /// <summary>
        /// 更新UI状态
        /// </summary>
        private void UpdateUIState()
        {
            bool canCheck = _hotUpdateManager?.IsInitialized == true && !_isUpdating;
            bool canStart = _pendingModules.Count > 0 && !_isUpdating;
            bool canControl = _isUpdating;

            if (checkUpdateButton) checkUpdateButton.interactable = canCheck;
            if (startUpdateButton) startUpdateButton.interactable = canStart;
            if (pauseButton) pauseButton.interactable = canControl;
            if (resumeButton) resumeButton.interactable = canControl;
            if (cancelButton) cancelButton.interactable = canControl;
        }

        /// <summary>
        /// 更新状态文本
        /// </summary>
        private void UpdateStatusText(string text)
        {
            if (statusText)
                statusText.text = text;

            Debug.Log($"[HotUpdate] {text}");
        }

        /// <summary>
        /// 更新模块状态文本
        /// </summary>
        private void UpdateModuleStatusText(string text)
        {
            if (moduleStatusText)
                moduleStatusText.text += text + "\n";
        }

        #endregion

        #region 高级用法示例

        /// <summary>
        /// 按优先级更新模块示例
        /// </summary>
        public async void UpdateModulesByPriority()
        {
            // 高优先级：核心游戏内容
            await _hotUpdateManager.UpdateModules(new[] { "GameContent" }, DownloadPriority.Critical);

            // 中优先级：UI和音频
            await _hotUpdateManager.UpdateModules(new[] { "UI", "Audio" }, DownloadPriority.High);

            // 低优先级：本地化文件
            await _hotUpdateManager.UpdateModules(new[] { "Localization" }, DownloadPriority.Normal);
        }

        /// <summary>
        /// 单独控制模块示例
        /// </summary>
        public void ControlSpecificModule(string moduleName)
        {
            // 暂停特定模块
            _hotUpdateManager.PauseModule(moduleName);

            // 恢复特定模块
            _hotUpdateManager.ResumeModule(moduleName);

            // 取消特定模块
            _hotUpdateManager.CancelModule(moduleName);

            // 获取模块状态
            var status = _hotUpdateManager.GetModuleStatus(moduleName);
            Debug.Log($"模块 {moduleName} 当前状态: {status}");
        }

        #endregion

        void OnDestroy()
        {
            // 取消订阅事件
            HotUpdateEvents.OnRemoteVersionReceived -= OnRemoteVersionReceived;

            // 模块状态变化事件
            HotUpdateEvents.OnModuleStatusChanged -= OnModuleStatusChanged;

            // 文件下载进度事件
            HotUpdateEvents.OnFileProgress -= OnFileProgress;

            // 模块下载进度事件
            HotUpdateEvents.OnModuleProgress -= OnModuleProgress;

            // 全局下载进度事件
            HotUpdateEvents.OnGlobalProgress -= OnGlobalProgress;

            // 错误事件
            HotUpdateEvents.OnError -= OnError;

            // 所有任务完成事件
            HotUpdateEvents.OnAllTasksCompleted -= OnAllTasksCompleted;

            // Core模块准备就绪事件
            HotUpdateEvents.OnCoreReady -= OnCoreReady;

            // 扩展事件（暂停/恢复/取消）
            ExtendedDownloadEvents.OnModulePaused -= OnModulePaused;
            ExtendedDownloadEvents.OnModuleResumed -= OnModuleResumed;
            ExtendedDownloadEvents.OnModuleCanceled -= OnModuleCanceled;
        }
    }
}