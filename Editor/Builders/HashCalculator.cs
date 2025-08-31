using System.IO;
using QHotUpdateSystem.Security;

namespace QHotUpdateSystem.Editor.Builders
{
    /// <summary>
    /// 计算文件 Hash / Size
    /// </summary>
    public static class HashCalculator
    {
        public static (string hash, long size) Calc(string filePath, string algo)
        {
            var bytes = File.ReadAllBytes(filePath);
            var hash = HashUtility.Compute(bytes, algo);
            return (hash, bytes.LongLength);
        }
    }
}