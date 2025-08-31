using System.IO;

namespace QHotUpdateSystem.Persistence
{
    public static class FileIOUtility
    {
        public static bool Exists(string path) => File.Exists(path);
        public static long GetSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
        public static void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
        public static void SafeMove(string src, string dst)
        {
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(src, dst);
        }
    }
}