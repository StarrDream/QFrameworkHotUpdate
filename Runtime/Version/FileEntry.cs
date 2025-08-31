using System;

namespace QHotUpdateSystem.Version
{
    [Serializable]
    public class FileEntry
    {
        public string name;
        public string hash;
        public long size;
        public bool compressed;
        public long cSize;
        public string algo;
        public string crc;
        public string[] tags;
    }
}