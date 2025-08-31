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

        /// <summary> 绝对根目录（GetFullPath）用于安全边界校验 </summary>
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
        } // 续传 meta: 与 temp 文件同目录，以 "<temp>.meta" 或独立策略

        public string GetResumeMetaPathByTemp(string tempFilePath) => tempFilePath + ".meta";

        // 如需要按模块+文件生成 meta 可额外封装
        public string GetResumeMetaPath(string module, string fileName, string hash)
        {
            var temp = GetTempFile(module, fileName, hash);
            return GetResumeMetaPathByTemp(temp);
        }
    }
}