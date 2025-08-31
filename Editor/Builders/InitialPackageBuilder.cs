using System.IO;
using UnityEditor;
using QHotUpdateSystem.Editor.Utils;
using QHotUpdateSystem.Version;

namespace QHotUpdateSystem.Editor.Builders
{
    /// <summary>
    /// 生成初始包：将构建出的 Version + AssetBundles 拷贝到 StreamingAssets 或自定义目录
    /// </summary>
    public static class InitialPackageBuilder
    {
        public static void BuildInitial(string buildOutput, string destDir, string platform)
        {
            if (string.IsNullOrEmpty(destDir)) return;
            var versionSrc = Path.Combine(buildOutput, "Versions", $"version_{platform.ToLower()}.json");
            var assetSrc = Path.Combine(buildOutput, "AssetBundles", platform);
            if (!File.Exists(versionSrc) || !Directory.Exists(assetSrc))
            {
                UnityEngine.Debug.LogWarning("InitialPackageBuilder: 输出目录缺失，先执行版本构建。");
                return;
            }

            // 拷贝
            var versionDstDir = Path.Combine(destDir, "Versions");
            var assetDstDir = Path.Combine(destDir, "AssetBundles", platform);
            EditorPathUtility.EnsureDir(versionDstDir);
            EditorPathUtility.EnsureDir(assetDstDir);

            File.Copy(versionSrc, Path.Combine(versionDstDir, Path.GetFileName(versionSrc)), true);

            foreach (var f in Directory.GetFiles(assetSrc, "*", SearchOption.AllDirectories))
            {
                var rel = f.Substring(assetSrc.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                var dst = Path.Combine(assetDstDir, rel);
                var dstdir = Path.GetDirectoryName(dst);
                if (!Directory.Exists(dstdir)) Directory.CreateDirectory(dstdir);
                File.Copy(f, dst, true);
            }

            AssetDatabase.Refresh();
        }
    }
}