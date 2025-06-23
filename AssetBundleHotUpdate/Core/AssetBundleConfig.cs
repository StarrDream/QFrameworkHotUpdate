using System.IO;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle配置管理
    ///     功能：统一管理服务器地址、本地路径等配置信息
    /// </summary>
    public static class AssetBundleConfig
    {
        /// <summary>
        ///     服务器根URL
        /// </summary>
        public const string ServerUrl = "http://你的地址:你的端口/改成你的目录/";

        /// <summary>
        ///     本地AssetBundle存储目录
        /// </summary>
        public const string LocalBundleDirectory = "AssetBundles/Windows";

        /// <summary>
        ///     版本清单文件名
        /// </summary>
        public const string ManifestFileName = "AssetBundleManifest.json";

        /// <summary>
        ///     ResKit配置文件名
        /// </summary>
        public const string ConfigFileName = "asset_bundle_config.bin";

        /// <summary>
        ///     获取完整的服务器URL
        /// </summary>
        public static string GetServerUrl(string fileName = "")
        {
            return ServerUrl.TrimEnd('/') + (string.IsNullOrEmpty(fileName) ? "" : "/" + fileName);
        }

        /// <summary>
        ///     获取本地Bundle完整路径
        /// </summary>
        public static string GetLocalBundlePath(string bundleName)
        {
            var targetDir = Path.Combine(Application.streamingAssetsPath, LocalBundleDirectory);
            Directory.CreateDirectory(targetDir);
            return Path.Combine(targetDir, bundleName);
        }

        /// <summary>
        ///     获取版本清单本地路径
        /// </summary>
        public static string GetLocalManifestPath()
        {
            var dir = Path.Combine(Application.streamingAssetsPath, "AssetBundles");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, ManifestFileName);
        }
    }
}