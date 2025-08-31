using UnityEditor;
using UnityEngine;
using QHotUpdateSystem.Editor.Config;

namespace QHotUpdateSystem.Editor.Windows.Sections
{
    public class ModuleListSection
    {
        Vector2 _scroll;

        public void OnGUI(HotUpdateConfigAsset cfg)
        {
            GUILayout.Label("模块列表", EditorStyles.boldLabel);
            if (cfg.modules == null) cfg.modules = new ModuleConfig[0];

            if (GUILayout.Button("添加模块"))
            {
                ArrayUtility.Add(ref cfg.modules, new ModuleConfig
                {
                    moduleName = "NewModule",
                    entries = new ResourceEntry[0]
                });
            }

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(180));
            for (int i = 0; i < cfg.modules.Length; i++)
            {
                var m = cfg.modules[i];
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.BeginHorizontal();
                m.moduleName = EditorGUILayout.TextField("Name", m.moduleName);
                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    ArrayUtility.RemoveAt(ref cfg.modules, i);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    break;
                }
                GUILayout.EndHorizontal();
                m.mandatory = EditorGUILayout.Toggle("Mandatory", m.mandatory);
                m.defaultCompress = EditorGUILayout.Toggle("Default Compress", m.defaultCompress);

                // Tags
                int tagCount = m.tags == null ? 0 : m.tags.Length;
                int newTagCount = Mathf.Max(0, EditorGUILayout.IntField("Tag Count", tagCount));
                if (newTagCount != tagCount)
                {
                    System.Array.Resize(ref m.tags, newTagCount);
                }
                for (int t = 0; t < newTagCount; t++)
                {
                    m.tags[t] = EditorGUILayout.TextField($"Tag {t}", m.tags[t]);
                }

                GUILayout.Label("资源条目", EditorStyles.miniBoldLabel);
                if (m.entries == null) m.entries = new ResourceEntry[0];
                if (GUILayout.Button("添加项"))
                {
                    ArrayUtility.Add(ref m.entries, new ResourceEntry());
                }

                for (int e = 0; e < m.entries.Length; e++)
                {
                    var entry = m.entries[e];
                    GUILayout.BeginVertical(EditorStyles.textArea);
                    entry.path = EditorGUILayout.TextField("Path", entry.path);
                    entry.includeSubDir = EditorGUILayout.Toggle("Include SubDir", entry.includeSubDir);
                    entry.searchPattern = EditorGUILayout.TextField("Search Pattern", entry.searchPattern);
                    entry.compress = EditorGUILayout.Toggle("Compress", entry.compress);
                    entry.explicitName = EditorGUILayout.TextField("Explicit Name", entry.explicitName);
                    if (GUILayout.Button("删除", GUILayout.Width(50)))
                    {
                        ArrayUtility.RemoveAt(ref m.entries, e);
                        GUILayout.EndVertical();
                        break;
                    }
                    GUILayout.EndVertical();
                }

                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }
    }
}
