using System;
using System.IO;
using System.Security.Cryptography;
using QFramework;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle工具类
    ///     功能：提供通用的工具方法，如文件哈希计算、ResKit刷新等
    /// </summary>
    public static class AssetBundleUtility
    {
        /// <summary>
        ///     计算文件MD5哈希值
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>MD5哈希值（小写，无分隔符）</returns>
        public static string CalculateFileHash(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Utility] 计算文件哈希失败 {filePath}: {e.Message}");
                return "";
            }
        }

        /// <summary>
        ///     刷新ResKit配置
        /// </summary>
        public static void RefreshResKitConfig()
        {
            try
            {
                var configPath = Path.Combine(Application.streamingAssetsPath, AssetBundleConfig.ConfigFileName);
                if (File.Exists(configPath))
                {
                    Debug.Log($"[Utility] 找到配置文件: {configPath}");
                    ResMgr.Init();
                    Debug.Log("[Utility] ResKit配置已刷新");
                }
                else
                {
                    Debug.LogWarning($"[Utility] 配置文件不存在: {configPath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Utility] 刷新ResKit配置失败: {e.Message}");
            }
        }

        /// <summary>
        ///     格式化文件大小
        /// </summary>
        /// <param name="bytes">字节数</param>
        /// <returns>格式化后的大小字符串</returns>
        public static string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

        /// <summary>
        ///     确保目录存在
        /// </summary>
        /// <param name="filePath">文件路径</param>
        public static void EnsureDirectoryExists(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        }

        /// <summary>
        ///     验证文件完整性
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="expectedHash">期望的哈希值</param>
        /// <returns>是否验证通过</returns>
        public static bool ValidateFileIntegrity(string filePath, string expectedHash)
        {
            if (!File.Exists(filePath)) return false;

            var actualHash = CalculateFileHash(filePath);
            return actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}