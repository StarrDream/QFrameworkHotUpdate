using System;
using System.Collections.Generic;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle数据结构定义
    ///     功能：定义系统中使用的所有数据结构
    /// </summary>
    /// <summary>
    ///     AssetBundle版本信息
    /// </summary>
    [Serializable]
    public class AssetBundleInfo
    {
        public string bundleName; // AB包名称
        public string version = "1.0.0"; // 版本号
        public string hash; // 文件哈希值
        public long size; // 文件大小（字节）
        public string buildTime; // 构建时间
        public bool enableUpdate = true; // 是否启用更新
        public string description = ""; // 描述信息
        public int priority; // 下载优先级
        public List<string> dependencies = new(); // 直接依赖
        public List<string> allDependencies = new(); // 所有依赖（包括间接）
        public int dependencyLevel; // 依赖层级
    }

    /// <summary>
    ///     AssetBundle版本清单
    /// </summary>
    [Serializable]
    public class AssetBundleManifest
    {
        public string manifestVersion = "1.0.0"; // 清单版本
        public string createTime; // 创建时间
        public List<AssetBundleInfo> assetBundles = new();
    }

    /// <summary>
    ///     下载结果
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string BundleName { get; set; }

        public static DownloadResult CreateSuccess(string bundleName)
        {
            return new DownloadResult { Success = true, BundleName = bundleName };
        }

        public static DownloadResult CreateFailure(string bundleName, string error)
        {
            return new DownloadResult { Success = false, BundleName = bundleName, ErrorMessage = error };
        }
    }

    /// <summary>
    ///     下载进度信息
    /// </summary>
    public class DownloadProgress
    {
        public string BundleName { get; set; }
        public float Progress { get; set; } // 0-1
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }

        public string GetProgressText()
        {
            return $"{BundleName}: {Progress:P} ({FormatBytes(DownloadedBytes)}/{FormatBytes(TotalBytes)})";
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
    }
}