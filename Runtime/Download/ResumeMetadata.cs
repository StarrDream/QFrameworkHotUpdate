using System.IO;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 断点续传辅助（当前实现仅依赖 .part 文件长度）
    /// 后续可扩展写 JSON 存 ETag 等
    /// </summary>
    public static class ResumeMetadata
    {
        public static long GetPartialSize(string tempPath)
        {
            if (File.Exists(tempPath))
            {
                var fi = new FileInfo(tempPath);
                return fi.Length;
            }
            return 0;
        }
    }
}