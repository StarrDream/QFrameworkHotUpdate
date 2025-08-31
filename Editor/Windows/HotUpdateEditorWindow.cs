using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using QHotUpdateSystem.Editor.Config;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Editor.Windows.Sections;
using QHotUpdateSystem.Editor.Builders;
using QHotUpdateSystem.Editor.Utils;

namespace QHotUpdateSystem.Editor.Windows
{
    /// <summary>
    /// HotUpdate 主窗口（稳定版 + 强制 Core 模块）
    /// 顶部使用纯手动 Rect（Toolbar + Tabs），避免 GetLastRect 在 Layout 阶段为 0 的问题。
    /// Tabs: Config / Modules / Preview / Logs
    /// Modules 页：左-模块列表 / 右-资源面板（手动分割）
    /// 规则：始终存在名为 "Core" 的模块，位于顶部，不可删除、不可改名、不可移动。
    /// </summary>
    public class HotUpdateEditorWindow : EditorWindow
    {
        #region Constants
        const float HEIGHT_TOOLBAR = 20f;
        const float HEIGHT_TABS = 22f;
        const float TOP_TOTAL_HEIGHT = HEIGHT_TOOLBAR + HEIGHT_TABS;
        const float SPLITTER_WIDTH = 4f;
        const float LEFT_MIN_WIDTH = 160f;
        const float RIGHT_MIN_PADDING = 220f;
        const string CORE_MODULE_NAME = "Core";
        #endregion

        #region Pref Keys
        const string PREF_TAB_INDEX = "QHot_TabIndex";
        const string PREF_LEFT_WIDTH = "QHot_LeftWidth";
        const string PREF_SELECTED_MODULE = "QHot_SelectedModule";
        #endregion

        #region Data
        HotUpdateConfigAsset _config;
        VersionInfo _lastVersion;
        ModuleResourcePanel _resourcePanel = new ModuleResourcePanel();
        ServerConfigSection _serverSection = new ServerConfigSection();
        #endregion

        #region UI State
        int _tabIndex;
        int _selectedModuleIndex = -1;
        float _leftWidth = 240f;
        bool _resizing;

        Vector2 _configScroll;
        Vector2 _previewScroll;
        Vector2 _logsScroll;
        Vector2 _moduleScroll;

        readonly List<string> _logBuffer = new List<string>();

        static readonly string[] TAB_NAMES = { "Config", "Modules", "Preview", "Logs" };
        #endregion

        #region Menu
        [MenuItem("Tools/QHotUpdate/HotUpdate Window")]
        public static void Open()
        {
            var win = GetWindow<HotUpdateEditorWindow>("QHotUpdate");
            win.minSize = new Vector2(640, 360);
            win.Show();
        }
        #endregion

        #region Unity Lifecycle
        void OnEnable()
        {
            _tabIndex = EditorPrefs.GetInt(PREF_TAB_INDEX, 0);
            _leftWidth = EditorPrefs.GetFloat(PREF_LEFT_WIDTH, 240f);
            _selectedModuleIndex = EditorPrefs.GetInt(PREF_SELECTED_MODULE, -1);
        }

        void OnDisable()
        {
            SavePrefs();
        }

        void SavePrefs()
        {
            EditorPrefs.SetInt(PREF_TAB_INDEX, _tabIndex);
            EditorPrefs.SetFloat(PREF_LEFT_WIDTH, _leftWidth);
            EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
        }
        #endregion

        #region OnGUI
        void OnGUI()
        {
            DrawTopArea();
            Rect contentRect = new Rect(0, TOP_TOTAL_HEIGHT, position.width, position.height - TOP_TOTAL_HEIGHT);

            switch (_tabIndex)
            {
                case 0: DrawConfigTab(contentRect); break;
                case 1: DrawModulesTab(contentRect); break;
                case 2: DrawPreviewTab(contentRect); break;
                case 3: DrawLogsTab(contentRect); break;
            }

            if (Event.current.type == EventType.MouseUp)
                SavePrefs();
        }
        #endregion

        #region Top (Manual)
        void DrawTopArea()
        {
            // Toolbar
            Rect toolbarRect = new Rect(0, 0, position.width, HEIGHT_TOOLBAR);
            GUILayout.BeginArea(toolbarRect, EditorStyles.toolbar);
            GUILayout.BeginHorizontal();

            // 配置选择
            EditorGUI.BeginChangeCheck();
            _config = (HotUpdateConfigAsset)EditorGUILayout.ObjectField(_config, typeof(HotUpdateConfigAsset), false, GUILayout.Width(240));
            if (EditorGUI.EndChangeCheck())
            {
                EnsureSelectedModuleValid();
                EnsureCoreModuleExistsAndOrdered();
            }

            GUI.enabled = _config != null;
            if (GUILayout.Button("Build", EditorStyles.toolbarButton, GUILayout.Width(50)))
                TriggerBuild();
            if (GUILayout.Button("Output", EditorStyles.toolbarButton, GUILayout.Width(60)))
                OpenOutputDir();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
                AssetDatabase.Refresh();
            GUI.enabled = true;

            GUILayout.Space(6);
            if (_lastVersion != null)
                GUILayout.Label($"v{_lastVersion.version} ({_lastVersion.platform})", EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Repaint", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Repaint();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // Tabs
            Rect tabsRect = new Rect(0, HEIGHT_TOOLBAR, position.width, HEIGHT_TABS);
            GUILayout.BeginArea(tabsRect);
            int newIndex = GUILayout.Toolbar(_tabIndex, TAB_NAMES, GUILayout.Height(HEIGHT_TABS - 2));
            if (newIndex != _tabIndex)
            {
                _tabIndex = newIndex;
                EditorPrefs.SetInt(PREF_TAB_INDEX, _tabIndex);
            }
            GUILayout.EndArea();
        }
        #endregion

        #region Tab: Config
        void DrawConfigTab(Rect rect)
        {
            GUILayout.BeginArea(rect);
            if (_config == null)
            {
                GUILayout.Space(8);
                EditorGUILayout.HelpBox("请先在顶部工具栏指定 HotUpdateConfigAsset。", MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            _configScroll = GUILayout.BeginScrollView(_configScroll);
            GUILayout.Label("配置文件基础设置", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _config.outputRoot = EditorGUILayout.TextField("输出根目录", _config.outputRoot);
            if (EditorGUI.EndChangeCheck())
                EditorUtility.SetDirty(_config);

            GUILayout.Space(8);
            GUILayout.Label("服务器配置", EditorStyles.boldLabel);
            _serverSection.OnGUI(_config);

            GUILayout.Space(8);
            GUILayout.Label("构建动作", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            GUI.enabled = _config.modules != null && _config.modules.Length > 0;
            if (GUILayout.Button("执行构建", GUILayout.Width(120)))
                TriggerBuild();
            GUI.enabled = true;

            if (GUILayout.Button("打开输出目录", GUILayout.Width(120)))
                OpenOutputDir();
            if (GUILayout.Button("刷新资产数据库", GUILayout.Width(140)))
                AssetDatabase.Refresh();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (_lastVersion != null)
            {
                GUILayout.Space(6);
                EditorGUILayout.HelpBox($"上次构建: v{_lastVersion.version}  模块:{_lastVersion.modules.Length}", MessageType.None);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        #endregion

        #region Tab: Modules
        void DrawModulesTab(Rect rect)
        {
            if (_config == null)
            {
                GUILayout.BeginArea(rect);
                EditorGUILayout.HelpBox("请先指定 Config Asset。", MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            EnsureCoreModuleExistsAndOrdered();
            EnsureSelectedModuleValid(); // 二次校验

            if (rect.height < 40) return;

            _leftWidth = Mathf.Clamp(_leftWidth,
                LEFT_MIN_WIDTH,
                Mathf.Max(LEFT_MIN_WIDTH, rect.width - RIGHT_MIN_PADDING));

            Rect leftRect = new Rect(rect.x, rect.y, _leftWidth, rect.height);
            Rect splitterRect = new Rect(leftRect.xMax, rect.y, SPLITTER_WIDTH, rect.height);
            Rect rightRect = new Rect(splitterRect.xMax, rect.y, rect.width - _leftWidth - SPLITTER_WIDTH, rect.height);

            EditorGUI.DrawRect(leftRect, new Color(0.18f, 0.18f, 0.18f));
            EditorGUI.DrawRect(rightRect, new Color(0.21f, 0.21f, 0.21f));

            GUILayout.BeginArea(leftRect);
            DrawModuleList();
            GUILayout.EndArea();

            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);
            Handles.color = new Color(0, 0, 0, 0.35f);
            Handles.DrawLine(new Vector2(splitterRect.x + splitterRect.width * 0.5f, splitterRect.y),
                             new Vector2(splitterRect.x + splitterRect.width * 0.5f, splitterRect.yMax));
            Handles.color = Color.white;

            GUILayout.BeginArea(rightRect);
            var module = GetSelectedModule();
            _resourcePanel.OnGUI(_config, module);
            GUILayout.EndArea();

            HandleSplitDrag(splitterRect);
        }
        #endregion

        #region Tab: Preview
        void DrawPreviewTab(Rect rect)
        {
            GUILayout.BeginArea(rect);
            if (_lastVersion == null)
            {
                GUILayout.Space(8);
                EditorGUILayout.HelpBox("尚无构建结果。请先 Build。", MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            _previewScroll = GUILayout.BeginScrollView(_previewScroll);
            foreach (var m in _lastVersion.modules)
            {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(m.name, EditorStyles.boldLabel);
                GUILayout.Label($"Files: {m.fileCount}  Size: {m.sizeBytes}  Compressed: {m.compressedSizeBytes}");
                if (m.files != null)
                {
                    foreach (var f in m.files)
                        GUILayout.Label($" - {f.name} {(f.compressed ? $"[c:{f.algo} {f.cSize}]" : "")}");
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        #endregion

        #region Tab: Logs
        void DrawLogsTab(Rect rect)
        {
            GUILayout.BeginArea(rect);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("清空日志", GUILayout.Width(80)))
                _logBuffer.Clear();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            _logsScroll = GUILayout.BeginScrollView(_logsScroll);
            foreach (var line in _logBuffer)
                GUILayout.Label(line, EditorStyles.miniLabel);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
        #endregion

        #region Modules: List
        void DrawModuleList()
        {
            GUILayout.Label("模块列表", EditorStyles.boldLabel);

            if (_config.modules == null)
                _config.modules = new ModuleConfig[0];

            _moduleScroll = GUILayout.BeginScrollView(_moduleScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _config.modules.Length; i++)
            {
                var m = _config.modules[i];
                bool isCore = string.Equals(m.moduleName, CORE_MODULE_NAME);
                bool selected = i == _selectedModuleIndex;
                var bg = selected ? EditorStyles.helpBox : EditorStyles.textArea;

                GUILayout.BeginVertical(bg);
                GUILayout.BeginHorizontal();

                // 选择勾
                if (GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(16)))
                {
                    if (_selectedModuleIndex != i)
                    {
                        _selectedModuleIndex = i;
                        EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
                    }
                }

                // 名称：Core 不可编辑
                if (isCore)
                {
                    GUILayout.Label(CORE_MODULE_NAME, GUILayout.MinWidth(60));
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    string newName = EditorGUILayout.TextField(m.moduleName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        // 防止用户改成 "Core" 导致冲突：如果用户手动改为 Core，则会被后续 EnsureCoreModuleExistsAndOrdered 冲突；这里直接阻止
                        if (newName == CORE_MODULE_NAME)
                        {
                            EditorUtility.DisplayDialog("名称无效", $"名称 {CORE_MODULE_NAME} 保留不可使用。", "OK");
                        }
                        else
                        {
                            m.moduleName = newName;
                            EditorUtility.SetDirty(_config);
                        }
                    }
                }

                // 上移
                bool canMoveUp = !isCore && i > 0 && !IsCoreIndex(i - 1);
                if (GUILayout.Button("▲", GUILayout.Width(22)))
                {
                    if (canMoveUp)
                    {
                        SwapModules(i, i - 1);
                        _selectedModuleIndex = i - 1;
                        EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        break;
                    }
                }

                // 下移（不能把 Core 下移 / 不能把普通模块下移到 Core 之上，因为 Core 永远 index 0 已锁定）
                bool canMoveDown = !isCore && i < _config.modules.Length - 1;
                if (GUILayout.Button("▼", GUILayout.Width(22)))
                {
                    if (canMoveDown)
                    {
                        // 移动后还需要保证 Core 在 0，Core 本身不会被移动，此处只要不跟 Core 交换即可
                        if (!IsCoreIndex(i + 1))
                        {
                            SwapModules(i, i + 1);
                            _selectedModuleIndex = i + 1;
                            EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            break;
                        }
                    }
                }

                // 删除按钮：Core 不显示
                if (!isCore)
                {
                    if (GUILayout.Button("X", GUILayout.Width(22)))
                    {
                        if (EditorUtility.DisplayDialog("确认删除", $"删除模块 {m.moduleName} ?", "删除", "取消"))
                        {
                            ArrayUtility.RemoveAt(ref _config.modules, i);
                            if (_config.modules.Length == 0)
                                _selectedModuleIndex = -1;
                            else
                                _selectedModuleIndex = Mathf.Clamp(_selectedModuleIndex, 0, _config.modules.Length - 1);
                            EditorUtility.SetDirty(_config);
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            break;
                        }
                    }
                }
                else
                {
                    GUILayout.Space(22 + 22 + 22); // 占位（保持行高度一致：▲ ▼ X 3个按钮宽度）
                }

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("添加模块"))
            {
                EnsureCoreModuleExistsAndOrdered(); // 再确认一次
                string newName = GenerateUniqueModuleName("NewModule");
                ArrayUtility.Add(ref _config.modules, new ModuleConfig
                {
                    moduleName = newName,
                    entries = new ResourceEntry[0],
                    defaultCompress = false
                });
                if (_selectedModuleIndex == -1)
                {
                    _selectedModuleIndex = _config.modules.Length - 1;
                    EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
                }
                EditorUtility.SetDirty(_config);
            }
            GUILayout.EndHorizontal();
        }
        #endregion

        #region Core Module Enforcement
        void EnsureCoreModuleExistsAndOrdered()
        {
            if (_config == null) return;
            if (_config.modules == null || _config.modules.Length == 0)
            {
                _config.modules = new ModuleConfig[]
                {
                    new ModuleConfig
                    {
                        moduleName = CORE_MODULE_NAME,
                        entries = new ResourceEntry[0],
                        defaultCompress = false
                    }
                };
                EditorUtility.SetDirty(_config);
                _selectedModuleIndex = 0;
                EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
                return;
            }

            // 查找 Core
            int coreIndex = -1;
            for (int i = 0; i < _config.modules.Length; i++)
            {
                if (_config.modules[i] != null &&
                    _config.modules[i].moduleName == CORE_MODULE_NAME)
                {
                    coreIndex = i;
                    break;
                }
            }

            // 不存在则插入顶部
            if (coreIndex == -1)
            {
                var list = new List<ModuleConfig>(_config.modules);
                list.Insert(0, new ModuleConfig
                {
                    moduleName = CORE_MODULE_NAME,
                    entries = new ResourceEntry[0],
                    defaultCompress = false
                });
                _config.modules = list.ToArray();
                EditorUtility.SetDirty(_config);
                // 如果之前选择的是 0 之后的 index，需要整体后移
                if (_selectedModuleIndex != -1)
                    _selectedModuleIndex += 1;
                _selectedModuleIndex = Mathf.Clamp(_selectedModuleIndex, 0, _config.modules.Length - 1);
                EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
                coreIndex = 0;
            }

            // 保证 Core 在 0
            if (coreIndex != 0)
            {
                var core = _config.modules[coreIndex];
                for (int i = coreIndex; i > 0; i--)
                    _config.modules[i] = _config.modules[i - 1];
                _config.modules[0] = core;
                EditorUtility.SetDirty(_config);

                if (_selectedModuleIndex == 0)
                {
                    // 已选 0 不变
                }
                else if (_selectedModuleIndex <= coreIndex)
                {
                    // 被整体后移
                    _selectedModuleIndex = Mathf.Clamp(_selectedModuleIndex + 1, 0, _config.modules.Length - 1);
                }
                EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
            }
        }

        bool IsCoreIndex(int i)
        {
            if (_config == null || _config.modules == null) return false;
            if (i < 0 || i >= _config.modules.Length) return false;
            return _config.modules[i] != null && _config.modules[i].moduleName == CORE_MODULE_NAME;
        }

        string GenerateUniqueModuleName(string baseName)
        {
            if (_config == null || _config.modules == null) return baseName;
            HashSet<string> names = new HashSet<string>();
            foreach (var m in _config.modules)
            {
                if (m != null && !string.IsNullOrEmpty(m.moduleName))
                    names.Add(m.moduleName);
            }
            if (!names.Contains(baseName) && baseName != CORE_MODULE_NAME)
                return baseName;

            int idx = 1;
            while (true)
            {
                string candidate = baseName + idx;
                if (candidate == CORE_MODULE_NAME)
                {
                    idx++;
                    continue;
                }
                if (!names.Contains(candidate))
                    return candidate;
                idx++;
            }
        }
        #endregion

        #region Split Drag
        void HandleSplitDrag(Rect splitterRect)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && splitterRect.Contains(e.mousePosition))
            {
                _resizing = true;
                e.Use();
            }
            if (_resizing && e.type == EventType.MouseDrag)
            {
                _leftWidth = Mathf.Clamp(e.mousePosition.x,
                    LEFT_MIN_WIDTH,
                    Mathf.Max(LEFT_MIN_WIDTH, position.width - RIGHT_MIN_PADDING));
                EditorPrefs.SetFloat(PREF_LEFT_WIDTH, _leftWidth);
                Repaint();
                e.Use();
            }
            if (_resizing && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                _resizing = false;
                e.Use();
            }
        }
        #endregion

        #region Build / Helpers
        void TriggerBuild()
        {
            if (_config == null)
            {
                EditorUtility.DisplayDialog("提示", "未指定配置文件。", "OK");
                return;
            }
            EnsureCoreModuleExistsAndOrdered();
            if (_config.modules == null || _config.modules.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "没有配置模块。", "OK");
                return;
            }

            var result = VersionBuilder.Build(_config);
            _lastVersion = result.versionInfo;
            Log($"Build 完成: v{_lastVersion.version} platform={_lastVersion.platform}");
            EditorUtility.DisplayDialog("完成", "构建完成。", "OK");
        }

        void OpenOutputDir()
        {
            if (_config == null) return;
            var path = EditorPathUtility.MakeAbsolute(_config.outputRoot);
            if (Directory.Exists(path))
                EditorUtility.RevealInFinder(path);
            else
                EditorUtility.DisplayDialog("提示", "输出目录不存在，请先构建。", "OK");
        }

        void EnsureSelectedModuleValid()
        {
            if (_config == null || _config.modules == null || _config.modules.Length == 0)
                _selectedModuleIndex = -1;
            else if (_selectedModuleIndex < 0 || _selectedModuleIndex >= _config.modules.Length)
                _selectedModuleIndex = 0;

            EditorPrefs.SetInt(PREF_SELECTED_MODULE, _selectedModuleIndex);
        }

        ModuleConfig GetSelectedModule()
        {
            if (_config == null || _config.modules == null) return null;
            if (_selectedModuleIndex < 0 || _selectedModuleIndex >= _config.modules.Length) return null;
            return _config.modules[_selectedModuleIndex];
        }

        void SwapModules(int a, int b)
        {
            if (a == b) return;
            if (IsCoreIndex(a) || IsCoreIndex(b)) return; // Core 不参与交换
            var tmp = _config.modules[a];
            _config.modules[a] = _config.modules[b];
            _config.modules[b] = tmp;
            EditorUtility.SetDirty(_config);
        }

        void Log(string msg)
        {
            _logBuffer.Add(msg);
            if (_logBuffer.Count > 500)
                _logBuffer.RemoveAt(0);
        }
        #endregion
    }
}
