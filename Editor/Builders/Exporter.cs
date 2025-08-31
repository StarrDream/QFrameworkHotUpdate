using System.IO;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Editor.Utils;
using QHotUpdateSystem.Editor.Security;
using QHotUpdateSystem.Editor.Config;
using UnityEngine;

namespace QHotUpdateSystem.Editor.Builders
{
    public static class Exporter
    {
        public static void ExportVersion(string outputRoot, string platform, VersionInfo info, bool pretty, HotUpdateConfigAsset cfg)
        {
            string versionDir = Path.Combine(outputRoot, "Versions");
            EditorPathUtility.EnsureDir(versionDir);
            string file = Path.Combine(versionDir, $"version_{platform.ToLower()}.json");

            // 1. 签名统一用紧凑模式（canonical）
            info.sign = "";
            string canonicalJson = EditorJsonUtility.ToJson(info, false); // 强制 false
            if (cfg.enableSignature && !string.IsNullOrEmpty(cfg.hmacSecret))
            {
                string sign = HmacVersionSigner.Sign(canonicalJson, cfg.hmacSecret);
                info.sign = sign;
            }

            // 2. 输出文件格式仍可根据 pretty 决定（对可读性友好）
            string finalJson = EditorJsonUtility.ToJson(info, pretty);
            File.WriteAllText(file, finalJson);
            Debug.Log("[QHotUpdate] Version exported with canonical signature: " + file);
        }
    }
}