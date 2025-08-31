using System;
using System.IO;
using QHotUpdateSystem.Security;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Logging;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 断点续传元数据：
    /// - remoteUrl: 下载来源（简单校验避免来源变化继续使用旧 temp）
    /// - hash / algo: 目标文件最终校验信息
    /// - size: 目标压缩或原文件实际总大小（根据 FileEntry.compressed 决定用 cSize 或 size）
    /// - compressed: 是否压缩
    /// - timestamp: 记录时间（可用于过期策略）
    /// </summary>
    [Serializable]
    public class DownloadResumeMeta
    {
        public string remoteUrl;
        public string hash;
        public string algo;
        public long size;
        public bool compressed;
        public long timestamp;

        public static bool TryLoad(string path, out DownloadResumeMeta meta)
        {
            meta = null;
            if (!File.Exists(path)) return false;
            try
            {
                var json = File.ReadAllText(path);
                meta = UnityEngine.JsonUtility.FromJson<DownloadResumeMeta>(json);
                return meta != null;
            }
            catch (Exception e)
            {
                HotUpdateLogger.Warn("Load resume meta failed: " + e.Message);
                return false;
            }
        }

        public void Save(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = UnityEngine.JsonUtility.ToJson(this);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                HotUpdateLogger.Warn("Save resume meta failed: " + e.Message);
            }
        }

        public static DownloadResumeMeta Create(FileEntry file, string remoteUrl, string hashAlgo)
        {
            return new DownloadResumeMeta
            {
                remoteUrl = remoteUrl,
                hash = file.hash,
                algo = hashAlgo,
                size = file.compressed ? file.cSize : file.size,
                compressed = file.compressed,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
    }
}
