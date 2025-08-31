using System.Text;
using System.Collections.Generic;
using QHotUpdateSystem.Version;

namespace QHotUpdateSystem.Utility
{
    /// <summary>
    /// 计算模块聚合 Hash（长度前缀安全格式）
    /// </summary>
    public static class AggregateHashUtility
    {
        public static string ComputeModuleAggregate(List<FileEntry> files, string hashAlgo, System.Func<string, string> hashFunc)
        {
            files.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var sb = new StringBuilder();
            foreach (var f in files)
            {
                // 结构: <名称长度>#<名称><hash>
                // 示例: 10#myfile.txtd41d8cd...
                var name = f.name ?? "";
                sb.Append(name.Length).Append('#').Append(name).Append(f.hash);
            }

            return hashFunc(sb.ToString());
        }
    }
}