using System.Collections.Generic;
using System.IO;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Utility;
using QHotUpdateSystem.Security;

namespace QHotUpdateSystem.Editor.Builders
{
    /// <summary>
    /// 聚合模块：收集 FileEntry -> 计算 aggregateHash / size统计
    /// </summary>
    public static class ModuleAggregator
    {
        public static ModuleInfo BuildModule(string moduleName, bool mandatory, List<FileEntry> files, string hashAlgo)
        {
            var aggregate = AggregateHashUtility.ComputeModuleAggregate(files, hashAlgo,
                s => HashUtility.Compute(s, hashAlgo));

            long size = 0;
            long cSize = 0;
            foreach (var f in files)
            {
                size += f.size;
                if (f.compressed) cSize += f.cSize;
            }

            return new ModuleInfo
            {
                name = moduleName,
                mandatory = mandatory,
                aggregateHash = aggregate,
                sizeBytes = size,
                compressedSizeBytes = cSize,
                fileCount = files.Count,
                files = files.ToArray()
            };
        }
    }
}