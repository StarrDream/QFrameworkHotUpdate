using System;
using UnityEngine;

namespace QHotUpdateSystem.Editor.Config
{
    /// <summary>
    /// 资源条目：可直接指定文件 / 目录（目录会自动展开所有文件）
    /// </summary>
    [Serializable]
    public class ResourceEntry
    {
        public string path;         // 绝对或相对工程路径（Assets/... 或磁盘）
        public bool includeSubDir = true;
        public string searchPattern = "*.*"; // 用于目录展开
        public bool compress;       // 是否压缩（覆盖模块默认）
        public string explicitName; // 可指定输出文件名（为空则使用原文件名）
    }
}