using System;
using System.IO;
using System.Text;
using QHotUpdateSystem.Platform;

namespace QHotUpdateSystem.Persistence
{
    /// <summary>
    /// 本地存储路径管理
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
        public string GetTempFile(string fileName) => Path.Combine(TempDir, fileName + ".part");

        public string GetTempFile(string moduleName, string fileName, string hash)
        {
            if (string.IsNullOrEmpty(moduleName)) moduleName = "M";
            if (string.IsNullOrEmpty(fileName)) fileName = "F";
            if (string.IsNullOrEmpty(hash)) hash = "na";

            moduleName = SanitizeForFile(moduleName);
            fileName = SanitizeForFile(fileName);
            hash = SanitizeForFile(hash);

            if (hash.Length > 16) hash = hash.Substring(0, 16);

            string name = Path.GetFileNameWithoutExtension(fileName);
            string combinedBase = $"{name}";
            if (combinedBase.Length > 60)
                combinedBase = combinedBase.Substring(0, 60);

            string tempPureName = $"{moduleName}__{combinedBase}__{hash}.part";
            if (tempPureName.Length > 120)
                tempPureName = tempPureName.Substring(0, 120);

            return Path.Combine(TempDir, tempPureName);
        }

        private string SanitizeForFile(string s)
        {
            if (string.IsNullOrEmpty(s)) return "_";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                bool bad = false;
                for (int i = 0; i < invalid.Length; i++)
                {
                    if (c == invalid[i])
                    {
                        bad = true;
                        break;
                    }
                }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString();
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
        /// 清理过期临时文件（.part 及其 .meta）。基于 meta 中 timestamp；若无 meta 则按文件最后写入时间。
        /// maxAgeHours: 过期小时数，例如 24
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
                        ts = (long)(DateTime.UtcNow - File.GetLastWriteTimeUtc(part)).TotalSeconds;
                        // 若没有 meta，用“文件距现在的秒差”粗略估计，再与阈值比较
                        if (ts < 0) ts = 0;
                        // 转换方式不同：这里让 ts 表示文件过去时间秒；统一下面比较逻辑
                        if (ts > maxAgeHours * 3600)
                        {
                            File.Delete(part);
                            if (File.Exists(metaPath)) File.Delete(metaPath);
                            removed++;
                        }
                        continue;
                    }
                    // ts 为写入时刻的 unix 秒
                    if (now - ts > maxAgeHours * 3600)
                    {
                        File.Delete(part);
                        if (File.Exists(metaPath)) File.Delete(metaPath);
                        removed++;
                    }
                }
                catch { /* 忽略单个删除失败 */ }
            }
            return removed;
        }
    }
}
