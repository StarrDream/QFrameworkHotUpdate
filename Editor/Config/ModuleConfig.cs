using System;
using UnityEngine;

namespace QHotUpdateSystem.Editor.Config
{
    /// <summary>
    /// 模块配置
    /// </summary>
    [Serializable]
    public class ModuleConfig
    {
        public string moduleName;
        public bool mandatory = false;
        public bool defaultCompress = false;
        public ResourceEntry[] entries;
        public string[] tags; // 模块级公共标签（合并到每个 FileEntry）
    }
}