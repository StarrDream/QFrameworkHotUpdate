using System.Collections.Generic;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    public class GameResourceManagerExample : MonoBehaviour
    {
        private AssetBundleUpdateController updateController;

        private void Start()
        {
            // 获取已初始化的更新控制器
            updateController = FindObjectOfType<AssetBundleUpdateController>();

            if (updateController == null)
            {
                Debug.LogError("未找到AssetBundleUpdateController，请确保已初始化");
                return;
            }

            // 注册事件
            updateController.OnAllDownloadsCompleted += OnResourceUpdateCompleted;
        }

        /// <summary>
        ///     进入关卡时更新关卡资源
        /// </summary>
        public void UpdateLevelResources(int levelId)
        {
            var levelBundle = $"level{levelId}_bundle";

            Debug.Log($"开始更新关卡{levelId}资源");
            updateController.UpdateBundle(levelBundle);
        }

        /// <summary>
        ///     更新角色资源包
        /// </summary>
        public void UpdateCharacterResources(List<string> characterIds)
        {
            var characterBundles = new List<string>();
            foreach (var characterId in characterIds) characterBundles.Add($"character_{characterId}_bundle");

            Debug.Log($"开始更新角色资源: {string.Join(", ", characterIds)}");
            updateController.UpdateBundles(characterBundles);
        }

        /// <summary>
        ///     强制更新所有UI资源
        /// </summary>
        public void ForceUpdateUIResources()
        {
            var uiBundles = new List<string>
            {
                "ui_main_bundle",
                "ui_battle_bundle",
                "ui_shop_bundle"
            };

            Debug.Log("强制更新UI资源");
            updateController.UpdateBundles(uiBundles, true);
        }

        /// <summary>
        ///     资源更新完成回调
        /// </summary>
        private void OnResourceUpdateCompleted(List<string> success, List<string> failed)
        {
            if (success.Count > 0)
            {
                Debug.Log($"资源更新成功: {string.Join(", ", success)}");

                // 刷新ResKit以使用新资源
                AssetBundleUtility.RefreshResKitConfig();

                // 通知其他系统资源已更新
                NotifyResourceUpdated(success);
            }

            if (failed.Count > 0)
            {
                Debug.LogError($"资源更新失败: {string.Join(", ", failed)}");

                // 处理更新失败的情况
                HandleUpdateFailure(failed);
            }
        }

        /// <summary>
        ///     通知资源更新完成
        /// </summary>
        private void NotifyResourceUpdated(List<string> updatedBundles)
        {
            // 发送事件通知其他系统
            foreach (var bundleName in updatedBundles)
                if (bundleName.StartsWith("level"))
                {
                    // 通知关卡系统
                    // EventManager.Instance.Trigger("LevelResourceUpdated", bundleName);
                }
                else if (bundleName.StartsWith("character"))
                {
                    // 通知角色系统
                    // EventManager.Instance.Trigger("CharacterResourceUpdated", bundleName);
                }
                else if (bundleName.StartsWith("ui"))
                {
                    // 通知UI系统
                    // EventManager.Instance.Trigger("UIResourceUpdated", bundleName);
                }
        }

        /// <summary>
        ///     处理更新失败
        /// </summary>
        private void HandleUpdateFailure(List<string> failedBundles)
        {
            // 显示错误提示
            // UIManager.Instance.ShowMessageBox(
            //     "资源更新失败", 
            //     $"以下资源更新失败，可能影响游戏体验:\n{string.Join("\n", failedBundles)}\n\n是否重试？",
            //     "重试", 
            //     "取消",
            //     () => RetryUpdateBundles(failedBundles),
            //     null
            // );
        }

        /// <summary>
        ///     重试更新失败的资源
        /// </summary>
        private void RetryUpdateBundles(List<string> bundleNames)
        {
            Debug.Log($"重试更新失败的资源: {string.Join(", ", bundleNames)}");
            updateController.UpdateBundles(bundleNames, true);
        }
    }
}