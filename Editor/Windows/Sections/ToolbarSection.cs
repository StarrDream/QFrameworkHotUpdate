using UnityEditor;
using UnityEngine;
using QHotUpdateSystem.Editor.Config;
using QHotUpdateSystem.Editor.Builders;

namespace QHotUpdateSystem.Editor.Windows.Sections
{
    public class ToolbarSection
    {
        public System.Action<VersionBuilder.BuildResult> OnBuildDone;

        public void OnGUI(HotUpdateConfigAsset cfg)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("构建版本", EditorStyles.toolbarButton))
            {
                if (cfg.modules == null || cfg.modules.Length == 0)
                {
                    EditorUtility.DisplayDialog("提示", "没有配置模块。", "OK");
                }
                else
                {
                    var res = VersionBuilder.Build(cfg);
                    OnBuildDone?.Invoke(res);
                    EditorUtility.DisplayDialog("完成", "构建完成。", "OK");
                }
            }
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
            {
                UnityEditor.AssetDatabase.Refresh();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
    }
}