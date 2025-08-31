using System.IO;

namespace QHotUpdateSystem.Platform
{
    public static class PathUtility
    {
        public static void EnsureDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        public static string Combine(params string[] parts) => Path.Combine(parts);
    }
}