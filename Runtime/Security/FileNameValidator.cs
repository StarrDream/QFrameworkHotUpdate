using System.IO;
using System.Text;
using System;

namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 文件名 / 路径安全校验与清洗工具
    /// </summary>
    public static class FileNameValidator
    {
        // 允许的基本字符集（可根据需要扩展）: 字母数字 _ . - 空格
        static readonly char[] AllowedExtra = new[] { '_', '.', '-', ' ' };

        /// <summary>
        /// 是否安全的相对文件名（不包含路径分隔或上级引用）
        /// </summary>
        public static bool IsSafeRelativeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Contains("/") || name.Contains("\\"))
                return false;
            if (name.Contains(".."))
                return false;
            if (name.Length > 180) return false;
            return true;
        }

        /// <summary>
        /// 清洗潜在不安全字符；不保证唯一性
        /// </summary>
        public static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "f";
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || Array.IndexOf(AllowedExtra, c) >= 0)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            var cleaned = sb.ToString();
            cleaned = cleaned.Replace("/", "_").Replace("\\", "_");
            if (cleaned.Contains("..")) cleaned = cleaned.Replace("..", "_");
            if (cleaned.Length == 0) cleaned = "f";
            return cleaned;
        }

        /// <summary>
        /// 确保最终落地路径仍在根目录内（防目录逃逸）
        /// </summary>
        public static bool IsPathWithinRoot(string rootDir, string fullPath)
        {
            try
            {
                var root = Path.GetFullPath(rootDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var target = Path.GetFullPath(fullPath);
                return target.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
