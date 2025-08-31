using UnityEditor;
using UnityEngine;
using QHotUpdateSystem.Editor.Config;
using System.IO;
using QHotUpdateSystem.Editor.Builders;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Editor.Utils;

namespace QHotUpdateSystem.Editor.Windows.Sections
{
    /// <summary>
    /// 构建预览：展示最新构建的模块文件数与大小
    /// </summary>
    public class ModuleFilesSection
    {
        Vector2 _scroll;
        VersionInfo _lastVersion;

        public void SetVersion(VersionInfo v) => _lastVersion = v;

        public void OnGUI(HotUpdateConfigAsset cfg)
        {
            GUILayout.Label("构建结果预览", EditorStyles.boldLabel);
            if (_lastVersion == null)
            {
                EditorGUILayout.HelpBox("尚未构建版本。", MessageType.Info);
                return;
            }
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(150));
            foreach (var m in _lastVersion.modules)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label($"{m.name}  Files:{m.fileCount}  Size:{m.sizeBytes}  CompSize:{m.compressedSizeBytes}");
                foreach (var f in m.files)
                {
                    GUILayout.Label($" - {f.name} {(f.compressed ? $"[{f.algo} cSize={f.cSize}]" : "")}");
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("打开输出目录"))
            {
                var root = EditorPathUtility.MakeAbsolute(cfg.outputRoot);
                if (Directory.Exists(root))
                    EditorUtility.RevealInFinder(root);
            }
        }
    }
}