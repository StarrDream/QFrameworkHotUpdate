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
    }
}