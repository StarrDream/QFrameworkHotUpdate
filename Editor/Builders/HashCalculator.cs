using System.IO;
using QHotUpdateSystem.Security;

namespace QHotUpdateSystem.Editor.Builders
{
    /// <summary>
    /// 计算文件 Hash / Size （修复：不再一次性读取全文件，改为流式）
    /// </summary>
    public static class HashCalculator
    {
        public static (string hash, long size) Calc(string filePath, string algo)
        {
            using (var fs = File.OpenRead(filePath))
            {
                long size = fs.Length;
                string hash = HashUtility.ComputeStream(fs, algo);
                return (hash, size);
            }
        }
    }
}