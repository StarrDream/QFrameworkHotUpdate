using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace QHotUpdateSystem.Editor.Builders
{
    /// <summary>
    /// 清理输出目录中多余文件（不在当前新版本中）
    /// </summary>
    public static class LocalCleaner
    {
        public static void CleanObsolete(string assetOutputDir, HashSet<string> validRelativeFiles)
        {
            if (!Directory.Exists(assetOutputDir)) return;
            foreach (var f in Directory.GetFiles(assetOutputDir, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(assetOutputDir.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                if (!validRelativeFiles.Contains(rel))
                {
                    File.Delete(f);
                    Debug.Log("[QHotUpdate] Clean obsolete: " + rel);
                }
            }
        }
    }
}