using System;
using System.IO;
using QHotUpdateSystem.Security;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Logging;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 断点续传元数据
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

        // 批次2新增
        public string etag;
        public string lastModified;

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

        public static DownloadResumeMeta Create(FileEntry file, string remoteUrl, string hashAlgo,
            string etag = null, string lastModified = null)
        {
            return new DownloadResumeMeta
            {
                remoteUrl = remoteUrl,
                hash = file.hash,
                algo = hashAlgo,
                size = file.compressed ? file.cSize : file.size,
                compressed = file.compressed,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                etag = etag,
                lastModified = lastModified
            };
        }

        /// <summary>
        /// 更新 ETag / Last-Modified（例如旧 meta 没有这些字段时补齐）
        /// </summary>
        public void UpdateRemoteMeta(string newEtag, string newLM)
        {
            if (!string.IsNullOrEmpty(newEtag)) etag = newEtag;
            if (!string.IsNullOrEmpty(newLM)) lastModified = newLM;
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}
