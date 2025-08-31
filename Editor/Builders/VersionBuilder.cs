using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using QHotUpdateSystem.Editor.Config;
using QHotUpdateSystem.Editor.Utils;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Core;
using QHotUpdateSystem.Security;

namespace QHotUpdateSystem.Editor.Builders
{
    /// <summary>
    /// 版本构建器（本次改动：生成 Bundle 依赖拓扑）
    /// </summary>
    public static class VersionBuilder
    {
        public class BuildResult
        {
            public VersionInfo versionInfo;
            public List<string> outputFiles = new List<string>();
        }

        public static BuildResult Build(HotUpdateConfigAsset cfg)
        {
            var platform = PlatformBuildUtility.GetCurrentPlatformName();
            var outputRoot = EditorPathUtility.MakeAbsolute(cfg.outputRoot);
            var assetPlatformDir = Path.Combine(outputRoot, "AssetBundles", platform);
            EditorPathUtility.EnsureDir(assetPlatformDir);

            var modules = new List<ModuleInfo>();
            var validFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ★ 新增：记录原始文件名 -> 最终输出文件名（用于依赖图统一）
            var originalToFinal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (cfg.modules != null)
            {
                foreach (var moduleCfg in cfg.modules)
                {
                    var fileEntries = new List<FileEntry>();
                    ExpandModuleEntries(cfg, moduleCfg, assetPlatformDir, fileEntries, validFiles, originalToFinal);
                    var moduleInfo = ModuleAggregator.BuildModule(moduleCfg.moduleName, moduleCfg.mandatory, fileEntries, cfg.hashAlgo);
                    modules.Add(moduleInfo);
                }
            }

            var ver = new VersionInfo
            {
                version = cfg.version,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                platform = platform,
                modules = modules.ToArray(),
                sign = "",
                bundleDeps = null // 先置空，后面按需生成
            };

            // ★ 生成依赖拓扑
            if (cfg.enableBundleDependency)
            {
                TryBuildBundleDependencyGraph(cfg, ver, originalToFinal);
            }

            if (cfg.cleanObsolete)
                LocalCleaner.CleanObsolete(assetPlatformDir, validFiles);

            Exporter.ExportVersion(outputRoot, platform, ver, cfg.prettyJson, cfg);
            InitialPackageBuilder.BuildInitial(outputRoot, EditorPathUtility.MakeAbsolute(cfg.initialPackageOutput), platform);

            AssetDatabase.Refresh();

            return new BuildResult
            {
                versionInfo = ver,
                outputFiles = new List<string>(validFiles)
            };
        }

        private static void ExpandModuleEntries(
            HotUpdateConfigAsset rootCfg,
            ModuleConfig moduleCfg,
            string assetPlatformDir,
            List<FileEntry> outFileEntries,
            HashSet<string> validFiles,
            Dictionary<string, string> originalToFinal // ★ 新增参数
        )
        {
            if (moduleCfg.entries == null) return;
            foreach (var entry in moduleCfg.entries)
            {
                if (string.IsNullOrEmpty(entry.path)) continue;
                var abs = ResolveToAbsolute(entry.path);
                if (File.Exists(abs))
                {
                    ProcessSingleFile(rootCfg, moduleCfg, entry, abs, assetPlatformDir, outFileEntries, validFiles, originalToFinal);
                }
                else if (Directory.Exists(abs))
                {
                    var files = Directory.GetFiles(abs, entry.searchPattern ?? "*.*",
                        entry.includeSubDir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                    foreach (var f in files)
                        ProcessSingleFile(rootCfg, moduleCfg, entry, f, assetPlatformDir, outFileEntries, validFiles, originalToFinal);
                }
                else
                {
                    Debug.LogWarning("[QHotUpdate] Path not found: " + entry.path);
                }
            }
        }

        private static void ProcessSingleFile(
            HotUpdateConfigAsset rootCfg,
            ModuleConfig moduleCfg,
            ResourceEntry entry,
            string absFile,
            string assetPlatformDir,
            List<FileEntry> outFileEntries,
            HashSet<string> validFiles,
            Dictionary<string, string> originalToFinal
        )
        {
            string originalName = string.IsNullOrEmpty(entry.explicitName)
                ? Path.GetFileName(absFile)
                : entry.explicitName;

            // 清洗与唯一化
            originalName = FileNameValidator.Sanitize(originalName);
            string finalName = EnsureUniqueFileName(originalName, out var renamed, validFiles);

            if (renamed)
                Debug.LogWarning($"[QHotUpdate] Duplicate output filename: {originalName} -> {finalName}");

            string targetFile = Path.Combine(assetPlatformDir, finalName);
            File.Copy(absFile, targetFile, true);

            var (hash, size) = Builders.HashCalculator.Calc(targetFile, rootCfg.hashAlgo);

            bool compress = entry.compress || moduleCfg.defaultCompress;
            string algo = "";
            long compressedSize = 0;

            if (compress && rootCfg.IsCompressionEnabled)
            {
                algo = rootCfg.GetCompressionAlgoString();
                var dstCompressed = targetFile + "." + algo;
                if (CompressionProcessor.Compress(targetFile, dstCompressed, algo, out compressedSize, out string err))
                {
                    File.Delete(targetFile);
                    File.Move(dstCompressed, targetFile);
                }
                else
                {
                    Debug.LogWarning($"[QHotUpdate] 压缩失败({algo}) 使用原文件: {finalName} Err={err}");
                    compress = false;
                    algo = "";
                }
            }
            else
            {
                compress = false;
            }

            var fe = new FileEntry
            {
                name = finalName,
                hash = hash,
                size = size,
                compressed = compress,
                cSize = compress ? compressedSize : 0,
                algo = compress ? algo : "",
                crc = "",
                tags = moduleCfg.tags
            };

            outFileEntries.Add(fe);
            validFiles.Add(finalName);

            // ★ 记录原始 => 最终，用于依赖图映射（注意只记录文件名，不含路径）
            var rawFileNameOnly = Path.GetFileName(absFile);
            if (!string.IsNullOrEmpty(rawFileNameOnly))
            {
                // 若重复记录，以最终名覆盖即可（保持简单；依赖图只需方向：原名->最终）
                originalToFinal[rawFileNameOnly] = finalName;
            }
        }

        private static string EnsureUniqueFileName(string candidate, out bool renamed, HashSet<string> existing)
        {
            renamed = false;
            if (!existing.Contains(candidate))
                return candidate;
            renamed = true;
            string name = Path.GetFileNameWithoutExtension(candidate);
            string ext = Path.GetExtension(candidate);
            int idx = 1;
            string final;
            do
            {
                final = FileNameValidator.Sanitize($"{name}_{idx}{ext}");
                idx++;
            } while (existing.Contains(final));
            return final;
        }

        private static string ResolveToAbsolute(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            return EditorPathUtility.MakeAbsolute(path);
        }

        /// <summary>
        /// 生成 bundle 依赖拓扑：读取 Unity 的主 manifest
        /// </summary>
        private static void TryBuildBundleDependencyGraph(HotUpdateConfigAsset cfg, VersionInfo ver, Dictionary<string, string> originalToFinal)
        {
            try
            {
                // 可能的两种来源：磁盘 AssetBundle 或 资源
                string manifestDirAbs = EditorPathUtility.MakeAbsolute(cfg.unityManifestPath);
                string mainManifestPath = Path.Combine(manifestDirAbs, cfg.unityManifestName);
                AssetBundle mainAb = null;
                AssetBundleManifest unityManifest = null;

                if (File.Exists(mainManifestPath))
                {
                    mainAb = AssetBundle.LoadFromFile(mainManifestPath);
                    if (mainAb != null)
                        unityManifest = mainAb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                }
                else if (cfg.unityManifestPath.StartsWith("Assets/"))
                {
                    // 若 manifest 被导入到工程
                    unityManifest = AssetDatabase.LoadAssetAtPath<AssetBundleManifest>(cfg.unityManifestPath);
                }

                if (unityManifest == null)
                {
                    Debug.LogWarning("[QHotUpdate] 未找到 Unity AssetBundleManifest，跳过依赖拓扑。");
                    if (mainAb != null) mainAb.Unload(true);
                    return;
                }

                var all = unityManifest.GetAllAssetBundles();
                var list = new List<BundleDependencyNode>(all.Length);

                foreach (var b in all)
                {
                    var deps = unityManifest.GetAllDependencies(b) ?? Array.Empty<string>();
                    string finalName = MapFinal(b, originalToFinal);
                    // 过滤：若 finalName 在版本文件中不存在，也写入但会在运行期被忽略
                    var mappedDeps = new List<string>(deps.Length);
                    foreach (var d in deps)
                        mappedDeps.Add(MapFinal(d, originalToFinal));
                    list.Add(new BundleDependencyNode
                    {
                        name = finalName,
                        deps = mappedDeps.ToArray()
                    });
                }

                ver.bundleDeps = list.ToArray();

                if (mainAb != null) mainAb.Unload(true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QHotUpdate] 生成 Bundle 依赖拓扑失败: " + e.Message);
            }
        }

        private static string MapFinal(string original, Dictionary<string, string> originalToFinal)
        {
            if (string.IsNullOrEmpty(original)) return original;
            var fileOnly = Path.GetFileName(original);
            if (string.IsNullOrEmpty(fileOnly)) return original;
            return originalToFinal.TryGetValue(fileOnly, out var final) ? final : fileOnly;
        }
    }
}
