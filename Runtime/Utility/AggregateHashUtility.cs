using System.Text;
using System.Collections.Generic;
using QHotUpdateSystem.Version;

namespace QHotUpdateSystem.Utility
{
    /// <summary>
    /// 计算模块聚合 Hash
    /// </summary>
    public static class AggregateHashUtility
    {
        public static string ComputeModuleAggregate(List<FileEntry> files, string hashAlgo, System.Func<string, string> hashFunc)
        {
            files.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var sb = new StringBuilder();
            foreach (var f in files)
                sb.Append(f.name).Append('|').Append(f.hash).Append(';');
            return hashFunc(sb.ToString());
        }
    }
}