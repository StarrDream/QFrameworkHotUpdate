using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    [CustomEditor(typeof(AssetBundleNameScriptable))]
    public class AssetBundleNameTableEditor : UnityEditor.Editor
    {
        private AssetBundleNameScriptable table;
        private Vector2 scrollPosition;
        private string assetBundlesPath;
        private string newBundleName = "";
        private ReorderableList reorderableList;

        private void OnEnable()
        {
            table = (AssetBundleNameScriptable)target;
            assetBundlesPath = Path.Combine(Application.streamingAssetsPath, "AssetBundles", "Windows");

            // 创建可重新排序的列表
            CreateReorderableList();
        }

        private void CreateReorderableList()
        {
            reorderableList = new ReorderableList(serializedObject,
                serializedObject.FindProperty("assetBundleNames"),
                true, true, false, true);

            // 设置列表头部
            reorderableList.drawHeaderCallback = (Rect rect) => { EditorGUI.LabelField(rect, "📦 AssetBundle列表 (可拖拽排序)"); };

            // 设置列表元素绘制
            reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = reorderableList.serializedProperty.GetArrayElementAtIndex(index);
                rect.y += 2;

                // 序号标签
                Rect indexRect = new Rect(rect.x, rect.y, 30, EditorGUIUtility.singleLineHeight);
                EditorGUI.LabelField(indexRect, $"{index + 1}.");

                // 名称输入框
                Rect nameRect = new Rect(rect.x + 35, rect.y, rect.width - 120, EditorGUIUtility.singleLineHeight);
                string oldValue = element.stringValue;
                string newValue = EditorGUI.TextField(nameRect, oldValue);

                // 检查重复名称
                if (newValue != oldValue)
                {
                    if (!IsNameDuplicate(newValue, index))
                    {
                        element.stringValue = newValue;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("重复名称", $"AssetBundle名称 '{newValue}' 已存在！", "确定");
                    }
                }

                // 上移按钮
                Rect upRect = new Rect(rect.x + rect.width - 80, rect.y, 25, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(upRect, "↑") && index > 0)
                {
                    reorderableList.serializedProperty.MoveArrayElement(index, index - 1);
                }

                // 下移按钮
                Rect downRect = new Rect(rect.x + rect.width - 50, rect.y, 25, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(downRect, "↓") && index < reorderableList.serializedProperty.arraySize - 1)
                {
                    reorderableList.serializedProperty.MoveArrayElement(index, index + 1);
                }
            };

            // 设置删除回调
            reorderableList.onRemoveCallback = (ReorderableList list) =>
            {
                if (EditorUtility.DisplayDialog("确认删除",
                        $"确定要删除 '{list.serializedProperty.GetArrayElementAtIndex(list.index).stringValue}' 吗？",
                        "确定", "取消"))
                {
                    ReorderableList.defaultBehaviours.DoRemoveButton(list);
                }
            };

            // 设置元素高度
            reorderableList.elementHeight = EditorGUIUtility.singleLineHeight + 4;
        }

        private bool IsNameDuplicate(string name, int currentIndex)
        {
            for (int i = 0; i < table.assetBundleNames.Count; i++)
            {
                if (i != currentIndex && table.assetBundleNames[i] == name)
                {
                    return true;
                }
            }

            return false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(10);

            // 标题
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.fontSize = 16;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            EditorGUILayout.LabelField("AssetBundle资源名称排序(下载顺序) 管理器", titleStyle);

            EditorGUILayout.Space(10);

            // 路径显示
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("AssetBundle路径:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(assetBundlesPath, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 操作按钮区域
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("操作面板", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            // 自动读取按钮
            if (GUILayout.Button("🔄 自动读取AssetBundle", GUILayout.Height(30)))
            {
                AutoLoadAssetBundles();
            }

            // 清空按钮
            if (GUILayout.Button("🗑️ 清空列表", GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog("确认清空", "确定要清空所有AssetBundle名称吗？", "确定", "取消"))
                {
                    ClearAllAssetBundles();
                }
            }

            EditorGUILayout.EndHorizontal();

            // 手动添加
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("手动添加:", GUILayout.Width(70));
            newBundleName = EditorGUILayout.TextField(newBundleName);
            if (GUILayout.Button("添加", GUILayout.Width(50)))
            {
                if (!string.IsNullOrEmpty(newBundleName))
                {
                    AddAssetBundle(newBundleName);
                    newBundleName = "";
                    GUI.FocusControl(null); // 清除焦点
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 统计信息
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"📊 统计信息: 共 {table.Count} 个AssetBundle", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 排序操作按钮
            if (table.Count > 1)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("🔄 排序操作", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("按名称升序排列"))
                {
                    SortAssetBundles(true);
                }

                if (GUILayout.Button("按名称降序排列"))
                {
                    SortAssetBundles(false);
                }

                if (GUILayout.Button("反转顺序"))
                {
                    ReverseAssetBundles();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(10);
            }

            // AssetBundle列表 - 使用可重新排序的列表
            if (table.Count > 0)
            {
                EditorGUILayout.BeginVertical("box");
                reorderableList.DoLayoutList();
                EditorGUILayout.EndVertical();

                // 操作提示
                EditorGUILayout.HelpBox("💡 操作提示：\n" +
                                        "• 拖拽左侧的≡图标可以重新排序\n" +
                                        "• 点击↑↓按钮可以上下移动元素\n" +
                                        "• 点击-按钮可以删除元素",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("暂无AssetBundle信息，点击'自动读取AssetBundle'按钮来加载资源。", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();

            // 保存提示
            if (GUI.changed)
            {
                EditorUtility.SetDirty(table);
            }
        }

        /// <summary>
        /// 排序AssetBundle列表
        /// </summary>
        private void SortAssetBundles(bool ascending)
        {
            if (ascending)
            {
                table.assetBundleNames.Sort();
            }
            else
            {
                table.assetBundleNames.Sort();
                table.assetBundleNames.Reverse();
            }

            EditorUtility.SetDirty(table);
            serializedObject.Update();
            Debug.Log($"[ObjectScriptTable] 已按名称{(ascending ? "升序" : "降序")}排列");
        }

        /// <summary>
        /// 反转AssetBundle列表顺序
        /// </summary>
        private void ReverseAssetBundles()
        {
            table.assetBundleNames.Reverse();
            EditorUtility.SetDirty(table);
            serializedObject.Update();
            Debug.Log("[ObjectScriptTable] 已反转列表顺序");
        }

        /// <summary>
        /// 自动读取AssetBundle文件
        /// </summary>
        private void AutoLoadAssetBundles()
        {
            if (!Directory.Exists(assetBundlesPath))
            {
                EditorUtility.DisplayDialog("路径不存在",
                    $"AssetBundle路径不存在:\n{assetBundlesPath}\n\n请确保已构建AssetBundle到StreamingAssets目录。",
                    "确定");
                return;
            }

            try
            {
                string[] files = Directory.GetFiles(assetBundlesPath, "*", SearchOption.TopDirectoryOnly);
                List<string> bundleNames = new List<string>();

                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    string extension = Path.GetExtension(file);

                    // 过滤掉.manifest文件和其他不需要的文件
                    if (string.IsNullOrEmpty(extension) &&
                        !fileName.Contains(".") &&
                        fileName != "Windows" &&
                        fileName != "AssetBundles")
                    {
                        bundleNames.Add(fileName);
                    }
                }

                if (bundleNames.Count == 0)
                {
                    EditorUtility.DisplayDialog("未找到AssetBundle",
                        "在指定路径下未找到有效的AssetBundle文件。",
                        "确定");
                    return;
                }

                int addedCount = 0;
                foreach (string bundleName in bundleNames)
                {
                    if (!table.ContainsBundleName(bundleName))
                    {
                        table.AddAssetBundle(bundleName);
                        addedCount++;
                    }
                }

                EditorUtility.SetDirty(table);
                serializedObject.Update();

                string message = $"扫描完成！\n找到 {bundleNames.Count} 个AssetBundle文件\n新添加 {addedCount} 个";
                EditorUtility.DisplayDialog("读取完成", message, "确定");

                Debug.Log($"[ObjectScriptTable] 自动读取完成，共找到 {bundleNames.Count} 个AssetBundle，新添加 {addedCount} 个");
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("读取失败", $"读取AssetBundle时发生错误:\n{e.Message}", "确定");
                Debug.LogError($"[ObjectScriptTable] 读取AssetBundle失败: {e.Message}");
            }
        }

        /// <summary>
        /// 添加AssetBundle
        /// </summary>
        private void AddAssetBundle(string bundleName)
        {
            if (table.ContainsBundleName(bundleName))
            {
                EditorUtility.DisplayDialog("重复添加", $"AssetBundle '{bundleName}' 已存在！", "确定");
                return;
            }

            table.AddAssetBundle(bundleName);
            EditorUtility.SetDirty(table);
            serializedObject.Update();
            Debug.Log($"[ObjectScriptTable] 已添加AssetBundle: {bundleName}");
        }

        /// <summary>
        /// 清空所有AssetBundle
        /// </summary>
        private void ClearAllAssetBundles()
        {
            table.ClearAll();
            EditorUtility.SetDirty(table);
            serializedObject.Update();
            Debug.Log("[ObjectScriptTable] 已清空所有AssetBundle名称");
        }
    }
}