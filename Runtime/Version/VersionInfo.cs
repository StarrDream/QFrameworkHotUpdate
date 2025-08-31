using System;

namespace QHotUpdateSystem.Version
{
    [Serializable]
    public class VersionInfo
    {
        public string version;
        public long timestamp;
        public string platform;
        public ModuleInfo[] modules;
        public string sign;

        // ★ 新增：Bundle 依赖拓扑（可为空；旧版本无此字段时保持兼容）
        public BundleDependencyNode[] bundleDeps;
    }
}