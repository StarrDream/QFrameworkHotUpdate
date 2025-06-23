using System.Collections.Generic;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    public class GameLauncherExample : MonoBehaviour
    {
        private AssetBundleUpdateController updateController;

        private void Start()
        {
            // 创建更新控制器
            var controllerObj = new GameObject("UpdateController");
            updateController = controllerObj.AddComponent<AssetBundleUpdateController>();

            // 注册事件
            updateController.OnInitializeCompleted += OnInitCompleted;
            updateController.OnAllDownloadsCompleted += OnUpdateCompleted;

            // 初始化
            updateController.Initialize();
        }

        private void OnInitCompleted(bool success)
        {
            if (success)
            {
                // 更新必备资源包
                var essentialBundles = new List<string>
                {
                    "ui_bundle",
                    "audio_bundle",
                    "config_bundle"
                };

                updateController.UpdateEssentialBundles(essentialBundles);
            }
        }

        private void OnUpdateCompleted(List<string> success, List<string> failed)
        {
            if (failed.Count == 0)
                Debug.Log("必备资源更新完成，可以启动游戏");
            // 启动游戏逻辑...
            else
                Debug.LogError($"部分资源更新失败: {string.Join(", ", failed)}");
        }
    }
}