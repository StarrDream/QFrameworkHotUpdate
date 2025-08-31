using System.Collections.Generic;

namespace QHotUpdateSystem.Version
{
    public static class VersionComparer
    {
        public static List<FileEntry> GetChangedFiles(ModuleInfo remoteModule, ModuleInfo localModule)
        {
            var changed = new List<FileEntry>();
            if (remoteModule == null) return changed;
            if (localModule == null)
            {
                changed.AddRange(remoteModule.files);
                return changed;
            }

            var localMap = new Dictionary<string, FileEntry>();
            foreach (var f in localModule.files)
                localMap[f.name] = f;

            foreach (var rf in remoteModule.files)
            {
                if (!localMap.TryGetValue(rf.name, out var lf))
                {
                    changed.Add(rf);
                    continue;
                }
                if (lf.hash != rf.hash) changed.Add(rf);
            }
            return changed;
        }
    }
}