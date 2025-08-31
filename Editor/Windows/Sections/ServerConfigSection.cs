using UnityEditor;
using UnityEngine;
using QHotUpdateSystem.Editor.Config;
using QHotUpdateSystem.Core;

namespace QHotUpdateSystem.Editor.Windows.Sections
{
    public class ServerConfigSection
    {
        public void OnGUI(HotUpdateConfigAsset cfg)
        {
            GUILayout.Label("服务器与版本配置", EditorStyles.boldLabel);
            cfg.baseUrl = EditorGUILayout.TextField("Base Url", cfg.baseUrl);
            cfg.outputRoot = EditorGUILayout.TextField("Output Root", cfg.outputRoot);
            cfg.initialPackageOutput = EditorGUILayout.TextField("Initial Package", cfg.initialPackageOutput);
            cfg.version = EditorGUILayout.TextField("Version", cfg.version);
            cfg.hashAlgo = EditorGUILayout.TextField("Hash Algo", cfg.hashAlgo);
            cfg.prettyJson = EditorGUILayout.Toggle("Pretty Json", cfg.prettyJson);
            cfg.cleanObsolete = EditorGUILayout.Toggle("Clean Obsolete", cfg.cleanObsolete);

            GUILayout.Space(4);
            GUILayout.Label("压缩设置", EditorStyles.miniBoldLabel);
            cfg.compressionAlgorithm = (CompressionAlgorithm)EditorGUILayout.EnumPopup("压缩算法", cfg.compressionAlgorithm);
          
            // 显示当前压缩设置的说明
            switch (cfg.compressionAlgorithm)
            {
                case CompressionAlgorithm.None:
                    EditorGUILayout.HelpBox("不使用压缩", MessageType.Info);
                    break;
                case CompressionAlgorithm.Zip:
                    EditorGUILayout.HelpBox("使用 ZIP 压缩（标准格式，兼容性好）", MessageType.Info);
                    break;
                case CompressionAlgorithm.GZip:
                    EditorGUILayout.HelpBox("使用 GZip 压缩（压缩率较高）", MessageType.Info);
                    break;
                case CompressionAlgorithm.LZ4:
                    EditorGUILayout.HelpBox("使用 LZ4 压缩（速度快，压缩率适中）", MessageType.Info);
                    break;
            }
        }
    }
}