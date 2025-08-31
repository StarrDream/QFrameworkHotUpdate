using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using QHotUpdateSystem.Editor.Config;
using QHotUpdateSystem.Editor.Utils;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Core;

namespace QHotUpdateSystem.Editor.Builders
{
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
            var globalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // 新增：跨模块去重

            if (cfg.modules != null)
            {
                foreach (var moduleCfg in cfg.modules)
                {
                    var fileEntries = new List<FileEntry>();
                    ExpandModuleEntries(cfg, moduleCfg, assetPlatformDir, fileEntries, validFiles, globalNames);
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
                sign = "" // 构建后由 Exporter 填
            };

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
            HashSet<string> globalNames)
        {
            if (moduleCfg.entries == null) return;
            foreach (var entry in moduleCfg.entries)
            {
                if (string.IsNullOrEmpty(entry.path)) continue;
                var abs = ResolveToAbsolute(entry.path);
                if (File.Exists(abs))
                {
                    ProcessSingleFile(rootCfg, moduleCfg, entry, abs, assetPlatformDir, outFileEntries, validFiles, globalNames);
                }
                else if (Directory.Exists(abs))
                {
                    var files = Directory.GetFiles(abs, entry.searchPattern ?? "*.*",
                        entry.includeSubDir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                    foreach (var f in files)
                        ProcessSingleFile(rootCfg, moduleCfg, entry, f, assetPlatformDir, outFileEntries, validFiles, globalNames);
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
            HashSet<string> globalNames)
        {
            string originalName = string.IsNullOrEmpty(entry.explicitName)
                ? Path.GetFileName(absFile)
                : entry.explicitName;

            string finalName = EnsureUniqueFileName(originalName, globalNames);

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
        }

        private static string EnsureUniqueFileName(string candidate, HashSet<string> globalNames)
        {
            if (globalNames.Add(candidate))
                return candidate;

            string name = Path.GetFileNameWithoutExtension(candidate);
            string ext = Path.GetExtension(candidate);
            int index = 1;
            string newName;
            do
            {
                newName = $"{name}_{index}{ext}";
                index++;
            } while (!globalNames.Add(newName));

            Debug.LogWarning($"[QHotUpdate] Duplicate output filename detected: {candidate} -> renamed to {newName}");
            return newName;
        }

        private static string ResolveToAbsolute(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            return EditorPathUtility.MakeAbsolute(path);
        }
    }
}