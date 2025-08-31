using System;

namespace QHotUpdateSystem.Version
{
    [Serializable]
    public class ModuleInfo
    {
        public string name;
        public bool mandatory;
        public string aggregateHash;
        public long sizeBytes;
        public long compressedSizeBytes;
        public int fileCount;
        public FileEntry[] files;
    }
}