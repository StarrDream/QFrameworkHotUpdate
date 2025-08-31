using System;
using System.IO;
using System.Text;
using QHotUpdateSystem.Platform;

namespace QHotUpdateSystem.Persistence
{
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
        public string TempDir => adapter.GetTempDir();

        public string GetAssetPath(string fileName) => Path.Combine(AssetDir, fileName);

        /// <summary>
        /// 旧版接口（仍保留），仅依据文件名生成 temp（可能产生跨模块冲突）
        /// </summary>
        public string GetTempFile(string fileName) => Path.Combine(TempDir, fileName + ".part");

        /// <summary>
        /// 新接口：基于 (moduleName + fileName + hash) 生成几乎唯一的临时文件名，显著降低并发重复 / 覆盖。
        /// hash 可为空（将使用 "na" 占位）。
        /// </summary>
        public string GetTempFile(string moduleName, string fileName, string hash)
        {
            if (string.IsNullOrEmpty(moduleName)) moduleName = "M";
            if (string.IsNullOrEmpty(fileName)) fileName = "F";
            if (string.IsNullOrEmpty(hash)) hash = "na";

            // 清理潜在非法字符（Windows 下）
            moduleName = SanitizeForFile(moduleName);
            fileName = SanitizeForFile(fileName);
            hash = SanitizeForFile(hash);

            // 限制 hash 展示长度（避免路径过长）
            if (hash.Length > 16) hash = hash.Substring(0, 16);

            // fileName 可能带扩展，拆开再组装
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName); // 包含 '.'

            // 构建唯一临时文件名： <module>__<name>__<hash>.part  (保留原扩展信息在 name 中不再追加 ext，避免 temp 过长且无需真实扩展)
            // 这样不会影响最终落地文件名（最终落地仍用 fileName）
            string combinedBase = $"{name}";
            if (combinedBase.Length > 60) // 避免超长
                combinedBase = combinedBase.Substring(0, 60);

            string tempPureName = $"{moduleName}__{combinedBase}__{hash}.part";
            // 若过长再截断
            if (tempPureName.Length > 120)
                tempPureName = tempPureName.Substring(0, 120);

            return Path.Combine(TempDir, tempPureName);
        }

        /// <summary>
        /// 简单过滤文件名不合法字符
        /// </summary>
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
                    if (c == invalid[i]) { bad = true; break; }
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
    }
}
