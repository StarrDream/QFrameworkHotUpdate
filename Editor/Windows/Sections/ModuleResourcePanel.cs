using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using QHotUpdateSystem.Editor.Config;

namespace QHotUpdateSystem.Editor.Windows.Sections
{
    /// <summary>
    /// 模块右侧资源配置面板（折叠 + 批量操作 + 拖拽 + 搜索 + 批量属性）
    /// </summary>
    public class ModuleResourcePanel
    {
        private Vector2 _scroll;
        private string _search = "";
        private readonly HashSet<int> _selected = new HashSet<int>();
        private readonly Dictionary<int, bool> _foldouts = new Dictionary<int, bool>();
        private int _lastEntryCount = -1;

        public void OnGUI(HotUpdateConfigAsset cfg, ModuleConfig module)
        {
            if (module == null)
            {
                EditorGUILayout.HelpBox("请选择左侧的模块以编辑资源。", MessageType.Info);
                return;
            }

            if (module.entries == null) module.entries = new ResourceEntry[0];

            // 新增条目默认展开
            if (_lastEntryCount != module.entries.Length)
            {
                _lastEntryCount = module.entries.Length;
                for (int i = 0; i < module.entries.Length; i++)
                    if (!_foldouts.ContainsKey(i))
                        _foldouts[i] = true;
            }

            DrawHeader(cfg, module);
            DrawToolbar(cfg, module);
            DrawList(cfg, module);
            DrawDragArea(cfg, module);
        }

        #region Header

        void DrawHeader(HotUpdateConfigAsset cfg, ModuleConfig module)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"模块：{module.moduleName}", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            module.moduleName = EditorGUILayout.TextField("名称", module.moduleName);
            module.mandatory = EditorGUILayout.Toggle("Mandatory", module.mandatory);
            module.defaultCompress = EditorGUILayout.Toggle("Default Compress", module.defaultCompress);

            int tagCount = module.tags == null ? 0 : module.tags.Length;
            int newTagCount = Mathf.Max(0, EditorGUILayout.IntField("Tag 数量", tagCount));
            if (newTagCount != tagCount)
            {
                System.Array.Resize(ref module.tags, newTagCount);
            }

            for (int i = 0; i < newTagCount; i++)
            {
                module.tags[i] = EditorGUILayout.TextField($"Tag {i}", module.tags[i]);
            }

            if (EditorGUI.EndChangeCheck())
                MarkDirty(cfg);
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Toolbar

        void DrawToolbar(HotUpdateConfigAsset cfg, ModuleConfig module)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 搜索框
            _search = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField, GUILayout.Width(180));
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? EditorStyles.toolbarButton, GUILayout.Width(20)))
            {
                if (!string.IsNullOrEmpty(_search))
                {
                    _search = "";
                    GUI.FocusControl(null);
                }
            }

            GUILayout.Space(4);
            if (GUILayout.Button("添加条目", EditorStyles.toolbarButton))
            {
                ArrayUtility.Add(ref module.entries, new ResourceEntry
                {
                    path = "",
                    includeSubDir = true,
                    searchPattern = "*.*",
                    compress = module.defaultCompress
                });
                _foldouts[module.entries.Length - 1] = true;
                MarkDirty(cfg);
            }

            if (GUILayout.Button("删除选中", EditorStyles.toolbarButton))
            {
                DeleteSelected(module);
                MarkDirty(cfg);
            }

            if (GUILayout.Button("全部展开", EditorStyles.toolbarButton))
            {
                foreach (var k in new List<int>(_foldouts.Keys)) _foldouts[k] = true;
            }

            if (GUILayout.Button("全部折叠", EditorStyles.toolbarButton))
            {
                foreach (var k in new List<int>(_foldouts.Keys)) _foldouts[k] = false;
            }

            GUILayout.Space(6);
            if (GUILayout.Button("批量:Compress=Yes", EditorStyles.toolbarButton))
            {
                ApplyBulkCompress(module, true);
                MarkDirty(cfg);
            }

            if (GUILayout.Button("批量:Compress=No", EditorStyles.toolbarButton))
            {
                ApplyBulkCompress(module, false);
                MarkDirty(cfg);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label($"总数:{module.entries.Length} 选中:{_selected.Count}", EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }

        #endregion

        #region List

        void DrawList(HotUpdateConfigAsset cfg, ModuleConfig module)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < module.entries.Length; i++)
            {
                if (!_foldouts.ContainsKey(i)) _foldouts[i] = true;
                var entry = module.entries[i];

                if (!PassSearch(entry)) continue;

                GUILayout.BeginVertical(_selected.Contains(i) ? EditorStyles.helpBox : EditorStyles.textArea);
                GUILayout.BeginHorizontal();

                // 1. 选中框
                bool sel = _selected.Contains(i);
                bool newSel = GUILayout.Toggle(sel, GUIContent.none, GUILayout.Width(16));
                if (newSel != sel)
                {
                    if (newSel) _selected.Add(i);
                    else _selected.Remove(i);
                }

                // 2. 折叠箭头 (使用 EditorGUI.Foldout，手动 Rect 控制宽度)
                Rect foldRect = GUILayoutUtility.GetRect(14, EditorGUIUtility.singleLineHeight, GUILayout.Width(14));
                _foldouts[i] = EditorGUI.Foldout(foldRect, _foldouts[i], GUIContent.none, true);

                // 3. 摘要
                string label = BuildSummaryLabel(i, entry);
                GUILayout.Label(label, EditorStyles.label);

                GUILayout.FlexibleSpace();

                // 右键菜单按钮
                if (GUILayout.Button("…", GUILayout.Width(22)))
                {
                    ShowEntryContextMenu(i, module, cfg);
                }

                // 删除按钮
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    RemoveEntry(module, i);
                    MarkDirty(cfg);
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    break;
                }

                GUILayout.EndHorizontal();

                if (_foldouts[i])
                {
                    EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    entry.path = EditorGUILayout.TextField("Path", entry.path);
                    entry.includeSubDir = EditorGUILayout.Toggle("Include SubDir", entry.includeSubDir);
                    entry.searchPattern = EditorGUILayout.TextField("Search Pattern", entry.searchPattern);
                    entry.compress = EditorGUILayout.Toggle("Compress", entry.compress);
                    entry.explicitName = EditorGUILayout.TextField("Explicit Name", entry.explicitName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        module.entries[i] = entry;
                        MarkDirty(cfg);
                    }

                    DrawPathStatus(entry);
                    EditorGUI.indentLevel--;
                }

                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
        }

        string BuildSummaryLabel(int index, ResourceEntry entry)
        {
            bool isDir = IsDirectoryEntry(entry);
            string label = $"{index:000} {(isDir ? "[Dir]" : "[File]")} {ShortName(entry.path)}";
            if (entry.compress) label += " (C)";
            if (!string.IsNullOrEmpty(entry.explicitName)) label += $" -> {entry.explicitName}";
            return label;
        }

        #endregion

        #region Drag & Context

        void DrawDragArea(HotUpdateConfigAsset cfg, ModuleConfig module)
        {
            var rect = GUILayoutUtility.GetRect(0, 48, GUILayout.ExpandWidth(true));
            GUI.Box(rect, "拖拽 文件 / 目录 到此添加 (支持多选)", EditorStyles.helpBox);
            var evt = Event.current;
            if (rect.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (var p in DragAndDrop.paths)
                        {
                            AddEntry(module, p, module.defaultCompress);
                        }

                        MarkDirty(cfg);
                        evt.Use();
                    }
                }
            }
        }

        void ShowEntryContextMenu(int index, ModuleConfig module, HotUpdateConfigAsset cfg)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("复制路径"), false, () => { EditorGUIUtility.systemCopyBuffer = module.entries[index].path ?? ""; });
            menu.AddItem(new GUIContent("切换 Compress"), false, () =>
            {
                module.entries[index].compress = !module.entries[index].compress;
                MarkDirty(cfg);
            });
            menu.AddItem(new GUIContent("删除"), false, () =>
            {
                RemoveEntry(module, index);
                MarkDirty(cfg);
            });
            menu.ShowAsContext();
        }

        #endregion

        #region Helpers (Entries Ops)

        bool PassSearch(ResourceEntry e)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            var name = Path.GetFileName(e.path ?? "").ToLower();
            return name.Contains(_search.ToLower());
        }

        bool IsDirectoryEntry(ResourceEntry e)
        {
            if (string.IsNullOrEmpty(e.path)) return false;
            if (Directory.Exists(e.path)) return true;
            if (Path.IsPathRooted(e.path) && Directory.Exists(e.path)) return true;
            return false;
        }

        void DrawPathStatus(ResourceEntry e)
        {
            if (string.IsNullOrEmpty(e.path))
            {
                EditorGUILayout.HelpBox("未填写路径。", MessageType.Info);
                return;
            }

            bool isDir = IsDirectoryEntry(e);
            if (isDir)
            {
                var pattern = string.IsNullOrEmpty(e.searchPattern) ? "*.*" : e.searchPattern;
                try
                {
                    int count = 0;
                    if (Directory.Exists(e.path))
                        count = Directory.GetFiles(e.path, pattern, e.includeSubDir ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Length;
                    EditorGUILayout.HelpBox($"目录条目：匹配文件数 = {count}", MessageType.None);
                }
                catch
                {
                    EditorGUILayout.HelpBox("目录读取失败。", MessageType.Warning);
                }
            }
            else
            {
                if (File.Exists(e.path))
                {
                    long size = new FileInfo(e.path).Length;
                    EditorGUILayout.HelpBox($"文件存在，大小 {size} bytes", MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox("文件不存在！", MessageType.Warning);
                }
            }
        }

        void AddEntry(ModuleConfig module, string path, bool compressDefault)
        {
            ArrayUtility.Add(ref module.entries, new ResourceEntry
            {
                path = path,
                includeSubDir = true,
                searchPattern = "*.*",
                compress = compressDefault
            });
            _foldouts[module.entries.Length - 1] = true;
        }

        void RemoveEntry(ModuleConfig module, int index)
        {
            if (index < 0 || index >= module.entries.Length) return;
            ArrayUtility.RemoveAt(ref module.entries, index);
            _selected.Remove(index);

            // 重新映射 foldout
            var newFold = new Dictionary<int, bool>();
            for (int i = 0; i < module.entries.Length; i++)
            {
                // 原索引 >= index 的向后偏移
                int oldIndex = i >= index ? i + 1 : i;
                if (_foldouts.TryGetValue(oldIndex, out var fo))
                    newFold[i] = fo;
                else
                    newFold[i] = true;
            }

            _foldouts.Clear();
            foreach (var kv in newFold) _foldouts[kv.Key] = kv.Value;
        }

        void DeleteSelected(ModuleConfig module)
        {
            if (_selected.Count == 0) return;
            var arr = new List<int>(_selected);
            arr.Sort();
            for (int i = arr.Count - 1; i >= 0; i--)
                RemoveEntry(module, arr[i]);
            _selected.Clear();
        }

        void ApplyBulkCompress(ModuleConfig module, bool value)
        {
            foreach (var idx in _selected)
            {
                if (idx >= 0 && idx < module.entries.Length)
                {
                    var e = module.entries[idx];
                    e.compress = value;
                    module.entries[idx] = e;
                }
            }
        }

        string ShortName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(empty)";
            const int max = 60;
            if (path.Length <= max) return path;
            return "..." + path.Substring(path.Length - max);
        }

        void MarkDirty(HotUpdateConfigAsset cfg)
        {
            if (cfg != null)
                EditorUtility.SetDirty(cfg);
        }

        #endregion
    }
}