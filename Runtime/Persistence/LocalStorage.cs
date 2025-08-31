using System;
using System.IO;
using System.Text;
using QHotUpdateSystem.Platform;
using QHotUpdateSystem.Security;

namespace QHotUpdateSystem.Persistence
{
    /// <summary>
    /// 本地路径管理
    /// 说明：
    /// - 若升级过程中已有旧 .part，不再自动迁移；旧任务将从 0 重新下载（一次性影响可接受）。
    /// - 如需兼容可在未来加入“扫描旧命名”逻辑尝试匹配。
    /// </summary>
    public class LocalStorage
    {
        private readonly IPlatformAdapter adapter;

        public LocalStorage(IPlatformAdapter adapter)
        {
            this.adapter = adapter;
            EnsureBaseDirs();
        }

        public string VersionFile => adapter.GetLocalVersionFilePath();
        public string AssetDir => adapter.GetLocalAssetDir();
        public string AssetDirFullPath => Path.GetFullPath(AssetDir);
        public string TempDir => adapter.GetTempDir();

        public string GetAssetPath(string fileName) => Path.Combine(AssetDir, fileName);

        [Obsolete("未使用的旧接口保留以防外部代码引用")]
        public string GetTempFile(string fileName) => Path.Combine(TempDir, fileName + ".part");

        public string GetTempFile(string moduleName, string fileName, string hash)
        {
            if (string.IsNullOrEmpty(moduleName)) moduleName = "M";
            if (string.IsNullOrEmpty(fileName)) fileName = "F";
            if (string.IsNullOrEmpty(hash)) hash = "na";

            // 计算模块与文件名的 MD5 前缀（避免直接使用原始长文件名）
            string modPrefix = HashUtility.Compute(moduleName, "md5");
            string filePrefix = HashUtility.Compute(fileName, "md5");

            modPrefix = SafeHead(modPrefix, 8);
            filePrefix = SafeHead(filePrefix, 8);

            // 取文件自身 hash（理论上由外部版本文件给出：md5/sha1 等）
            string mainHashPart = hash.Length > 32 ? hash.Substring(0, 32) : hash;

            string tempPureName = $"{modPrefix}_{filePrefix}_{mainHashPart}.part";
            // 控制最大长度（极端情况下）
            if (tempPureName.Length > 96)
                tempPureName = tempPureName.Substring(0, 96);

            return Path.Combine(TempDir, tempPureName);
        }

        private string SafeHead(string s, int len)
        {
            if (string.IsNullOrEmpty(s)) return "x";
            if (s.Length <= len) return s;
            return s.Substring(0, len);
        }

        public void EnsureBaseDirs()
        {
            EnsureDir(adapter.GetPersistentRoot());
            EnsureDir(adapter.GetLocalAssetDir());
            EnsureDir(adapter.GetTempDir());
        }

        private void EnsureDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public string GetResumeMetaPathByTemp(string tempFilePath) => tempFilePath + ".meta";
        public string GetResumeMetaPath(string module, string fileName, string hash)
        {
            var temp = GetTempFile(module, fileName, hash);
            return GetResumeMetaPathByTemp(temp);
        }

        /// <summary>
        /// 清理过期临时文件
        /// </summary>
        public int CleanExpiredTemps(double maxAgeHours)
        {
            if (maxAgeHours <= 0) return 0;
            if (!Directory.Exists(TempDir)) return 0;
            int removed = 0;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var part in Directory.GetFiles(TempDir, "*.part", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var metaPath = part + ".meta";
                    long ts = 0;
                    if (File.Exists(metaPath))
                    {
                        if (Download.DownloadResumeMeta.TryLoad(metaPath, out var meta) && meta != null)
                            ts = meta.timestamp;
                    }
                    if (ts == 0)
                    {
                        // 使用文件的“过去时间秒数”估算
                        var diff = (long)(DateTime.UtcNow - File.GetLastWriteTimeUtc(part)).TotalSeconds;
                        if (diff > maxAgeHours * 3600)
                        {
                            File.Delete(part);
                            if (File.Exists(metaPath)) File.Delete(metaPath);
                            removed++;
                        }
                        continue;
                    }
                    if (now - ts > maxAgeHours * 3600)
                    {
                        File.Delete(part);
                        if (File.Exists(metaPath)) File.Delete(metaPath);
                        removed++;
                    }
                }
                catch { /* 忽略单条错误 */ }
            }
            return removed;
        }
    }
}
