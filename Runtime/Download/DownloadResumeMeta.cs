using System;
using System.IO;
using QHotUpdateSystem.Security;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Logging;

namespace QHotUpdateSystem.Download
{
    [Serializable]
    public class DownloadResumeMeta
    {
        public string remoteUrl;
        public string hash;
        public string algo;
        public long size;
        public bool compressed;
        public long timestamp;
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

        /// <summary>
        /// 原子写入：写到临时文件再替换，避免崩溃/断电导致截断损坏。
        /// </summary>
        public void Save(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = UnityEngine.JsonUtility.ToJson(this);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path))
                {
                    try
                    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                        File.Replace(tmp, path, null);
#else
                        File.Delete(path);
                        File.Move(tmp, path);
#endif
                    }
                    catch
                    {
                        // 回退策略
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
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

        public void UpdateRemoteMeta(string newEtag, string newLM)
        {
            if (!string.IsNullOrEmpty(newEtag)) etag = newEtag;
            if (!string.IsNullOrEmpty(newLM)) lastModified = newLM;
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }
}