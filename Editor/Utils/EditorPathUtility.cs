using System.IO;
using UnityEditor;
using UnityEngine;

namespace QHotUpdateSystem.Editor.Utils
{
    public static class EditorPathUtility
    {
        public static string GetProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath).Replace("\\", "/");
        }

        public static string MakeAbsolute(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (Path.IsPathRooted(path)) return Normalize(path);
            // 视为相对工程根
            return Normalize(Path.Combine(GetProjectRoot(), path));
        }

        public static string Normalize(string p)
        {
            return p.Replace("\\", "/");
        }

        public static void EnsureDir(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public static string RelativeTo(string fullPath, string baseDir)
        {
            fullPath = Normalize(fullPath);
            baseDir = Normalize(baseDir);
            if (!fullPath.StartsWith(baseDir)) return fullPath;
            var rel = fullPath.Substring(baseDir.Length).TrimStart('/');
            return rel;
        }
    }
}