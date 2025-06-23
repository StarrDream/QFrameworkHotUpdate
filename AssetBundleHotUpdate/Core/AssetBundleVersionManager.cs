using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle版本管理器
    ///     功能：管理本地和远程版本清单，提供版本比较功能
    /// </summary>
    public class AssetBundleVersionManager
    {
        // 事件回调
        public Action<bool> OnLocalManifestLoaded; // 本地清单加载完成
        public Action<bool> OnRemoteManifestLoaded; // 远程清单加载完成

        // 属性
        public AssetBundleManifest LocalManifest { get; private set; }

        public AssetBundleManifest RemoteManifest { get; private set; }

        public bool IsInitialized => LocalManifest != null && RemoteManifest != null;

        /// <summary>
        ///     初始化版本管理器
        /// </summary>
        public IEnumerator Initialize()
        {
            Debug.Log("[VersionManager] 开始初始化版本管理器");

            // 加载本地版本清单
            yield return LoadLocalManifest();

            // 下载远程版本清单
            yield return LoadRemoteManifest();

            Debug.Log("[VersionManager] 版本管理器初始化完成");
        }

        /// <summary>
        ///     加载本地版本清单
        /// </summary>
        private IEnumerator LoadLocalManifest()
        {
            var localPath = AssetBundleConfig.GetLocalManifestPath();

            if (File.Exists(localPath))
            {
                try
                {
                    var json = File.ReadAllText(localPath);
                    LocalManifest = JsonConvert.DeserializeObject<AssetBundleManifest>(json);
                    Debug.Log($"[VersionManager] 本地版本清单加载成功，包含 {LocalManifest.assetBundles.Count} 个AB包");
                    OnLocalManifestLoaded?.Invoke(true);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VersionManager] 加载本地版本清单失败: {e.Message}");
                    LocalManifest = new AssetBundleManifest();
                    OnLocalManifestLoaded?.Invoke(false);
                }
            }
            else
            {
                Debug.Log("[VersionManager] 本地版本清单不存在，创建空清单");
                LocalManifest = new AssetBundleManifest();
                OnLocalManifestLoaded?.Invoke(true);
            }

            yield return null;
        }

        /// <summary>
        ///     加载远程版本清单
        /// </summary>
        private IEnumerator LoadRemoteManifest()
        {
            var remoteUrl = AssetBundleConfig.GetServerUrl(AssetBundleConfig.ManifestFileName);

            using (var request = UnityWebRequest.Get(remoteUrl))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var json = request.downloadHandler.text;
                        RemoteManifest = JsonConvert.DeserializeObject<AssetBundleManifest>(json);
                        Debug.Log($"[VersionManager] 远程版本清单加载成功，包含 {RemoteManifest.assetBundles.Count} 个AB包");
                        OnRemoteManifestLoaded?.Invoke(true);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[VersionManager] 解析远程版本清单失败: {e.Message}");
                        OnRemoteManifestLoaded?.Invoke(false);
                    }
                }
                else
                {
                    Debug.LogError($"[VersionManager] 下载远程版本清单失败: {request.error}");
                    OnRemoteManifestLoaded?.Invoke(false);
                }
            }
        }

        /// <summary>
        ///     检查AB包是否需要更新
        /// </summary>
        /// <param name="bundleName">AB包名称</param>
        /// <returns>是否需要更新</returns>
        public bool CheckBundleNeedUpdate(string bundleName)
        {
            if (RemoteManifest == null)
            {
                Debug.LogWarning("[VersionManager] 远程版本清单未加载");
                return false;
            }

            var remoteBundleInfo = RemoteManifest.assetBundles.FirstOrDefault(b => b.bundleName == bundleName);
            if (remoteBundleInfo == null || !remoteBundleInfo.enableUpdate) return false;

            var localBundlePath = AssetBundleConfig.GetLocalBundlePath(bundleName);

            // 检查本地是否存在该Bundle
            if (!File.Exists(localBundlePath)) return true;

            // 检查版本信息
            var localBundleInfo = LocalManifest.assetBundles.FirstOrDefault(b => b.bundleName == bundleName);
            if (localBundleInfo == null) return true;

            if (localBundleInfo.hash != remoteBundleInfo.hash || localBundleInfo.version != remoteBundleInfo.version) return true;

            // 验证本地文件完整性
            var localFileHash = AssetBundleUtility.CalculateFileHash(localBundlePath);
            if (localFileHash != remoteBundleInfo.hash)
            {
                Debug.LogWarning($"[VersionManager] 文件完整性验证失败: {bundleName}");
                return true;
            }

            return false;
        }

        /// <summary>
        ///     获取需要更新的AB包列表
        /// </summary>
        /// <param name="bundleNames">要检查的AB包名称列表</param>
        /// <returns>需要更新的AB包信息列表</returns>
        public List<AssetBundleInfo> GetBundlesToUpdate(List<string> bundleNames)
        {
            var bundlesToUpdate = new List<AssetBundleInfo>();

            if (RemoteManifest == null)
            {
                Debug.LogWarning("[VersionManager] 远程版本清单未加载");
                return bundlesToUpdate;
            }

            foreach (var bundleName in bundleNames)
                if (CheckBundleNeedUpdate(bundleName))
                {
                    var bundleInfo = RemoteManifest.assetBundles.FirstOrDefault(b => b.bundleName == bundleName);
                    if (bundleInfo != null) bundlesToUpdate.Add(bundleInfo);
                }

            return bundlesToUpdate;
        }

        /// <summary>
        ///     更新本地版本清单
        /// </summary>
        /// <param name="bundleInfo">更新的AB包信息</param>
        public void UpdateLocalManifest(AssetBundleInfo bundleInfo)
        {
            if (LocalManifest == null) LocalManifest = new AssetBundleManifest();

            // 移除旧的版本信息
            LocalManifest.assetBundles.RemoveAll(b => b.bundleName == bundleInfo.bundleName);

            // 添加新的版本信息
            LocalManifest.assetBundles.Add(bundleInfo);

            // 保存到文件
            SaveLocalManifest();
        }

        /// <summary>
        ///     保存本地版本清单
        /// </summary>
        private void SaveLocalManifest()
        {
            try
            {
                var localPath = AssetBundleConfig.GetLocalManifestPath();
                var json = JsonConvert.SerializeObject(LocalManifest, Formatting.Indented);
                File.WriteAllText(localPath, json);
                Debug.Log("[VersionManager] 本地版本清单已保存");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VersionManager] 保存本地版本清单失败: {e.Message}");
            }
        }

        /// <summary>
        ///     获取AB包信息
        /// </summary>
        /// <param name="bundleName">AB包名称</param>
        /// <returns>(本地版本, 远程版本, 需要更新)</returns>
        public (string localVersion, string remoteVersion, bool needUpdate) GetBundleVersionInfo(string bundleName)
        {
            var localVersion = "未知";
            var remoteVersion = "未知";
            var needUpdate = false;

            if (LocalManifest != null)
            {
                var localInfo = LocalManifest.assetBundles.FirstOrDefault(b => b.bundleName == bundleName);
                if (localInfo != null) localVersion = localInfo.version;
            }

            if (RemoteManifest != null)
            {
                var remoteInfo = RemoteManifest.assetBundles.FirstOrDefault(b => b.bundleName == bundleName);
                if (remoteInfo != null)
                {
                    remoteVersion = remoteInfo.version;
                    needUpdate = CheckBundleNeedUpdate(bundleName);
                }
            }

            return (localVersion, remoteVersion, needUpdate);
        }
    }
}