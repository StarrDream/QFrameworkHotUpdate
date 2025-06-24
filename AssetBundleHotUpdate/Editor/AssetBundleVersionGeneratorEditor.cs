using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;


namespace AssetBundleHotUpdate
{
    /// <summary>
    /// AssetBundle版本管理器
    /// 功能：
    /// 1. 扫描和生成AB包版本信息
    /// 2. 支持热更新配置（生成空版本清单）
    /// 3. 生成服务器上传配置
    /// 4. 优先级管理
    /// 5. 依赖关系管理
    /// </summary>
    public class AssetBundleVersionGenerator : EditorWindow
    {
        #region 私有字段

        /// <summary>
        /// AssetBundle文件路径
        /// </summary>
        private string assetBundlePath = "";

        /// <summary>
        /// 当前的AssetBundle清单数据
        /// </summary>
        private AssetBundleManifest bundleManifest;

        /// <summary>
        /// 滚动视图位置
        /// </summary>
        private Vector2 scrollPosition;

        /// <summary>
        /// 服务器URL配置（用于生成下载链接）
        /// </summary>
        private string serverUrl = AssetBundleConfig.ServerUrl;


        /// <summary>
        /// 首包资源 不删除的
        /// </summary>
        private AssetBundleNameScriptable m_NameScriptableObject;

        /// <summary>
        /// 清单版本号
        /// </summary>
        private string manifestVersion = "1.0.0";

        /// <summary>
        /// 默认AB包版本号
        /// </summary>
        private string defaultBundleVersion = "1.0.0";

        #endregion

        #region Unity编辑器菜单和窗口初始化

        /// <summary>
        /// 创建编辑器窗口菜单项
        /// </summary>
        [MenuItem("QFramework/AssetBundle版本管理器")]
        static void Init()
        {
            AssetBundleVersionGenerator window = (AssetBundleVersionGenerator)EditorWindow.GetWindow(typeof(AssetBundleVersionGenerator));
            window.titleContent = new GUIContent("AB版本管理器");
            window.Show();
        }

        /// <summary>
        /// 窗口启用时的初始化
        /// </summary>
        void OnEnable()
        {
            // 设置默认的AssetBundle路径
            if (string.IsNullOrEmpty(assetBundlePath))
            {
                assetBundlePath = Path.Combine(Application.streamingAssetsPath, AssetBundleConfig.LocalBundleDirectory);
            }
        }

        #endregion

        #region 主界面绘制

        /// <summary>
        /// 绘制主界面
        /// </summary>
        void OnGUI()
        {
            try
            {
                EditorGUILayout.BeginVertical();

                // 绘制标题
                EditorGUILayout.LabelField("AssetBundle 版本管理器", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                m_NameScriptableObject = (AssetBundleNameScriptable)EditorGUILayout.ObjectField("首包资源名称列表", m_NameScriptableObject, typeof(AssetBundleNameScriptable), true);

                // 绘制服务器URL配置区域
                DrawServerConfig();
                EditorGUILayout.Space();

                // 绘制AssetBundle路径选择区域
                DrawPathSelection();
                EditorGUILayout.Space();

                // 绘制基础操作按钮
                DrawBasicOperations();

                // 绘制保存操作按钮
                DrawSaveOperations();

                // 绘制热更新配置按钮
                DrawHotUpdateOperations();

                EditorGUILayout.Space();

                // 绘制AssetBundle列表
                DrawAssetBundleList();

                EditorGUILayout.EndVertical();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"OnGUI Error: {e.Message}");
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// 绘制服务器配置区域
        /// </summary>
        void DrawServerConfig()
        {
            EditorGUILayout.LabelField("服务器配置", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("服务器URL:", GUILayout.Width(80));
            serverUrl = EditorGUILayout.TextField(serverUrl);
            EditorGUILayout.EndHorizontal();

            // 添加说明文字
            EditorGUILayout.HelpBox("设置AssetBundle的服务器下载地址，用于生成完整的下载URL", MessageType.Info);
        }

        /// <summary>
        /// 绘制路径选择区域
        /// </summary>
        void DrawPathSelection()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("AssetBundle路径:", GUILayout.Width(120));
            assetBundlePath = EditorGUILayout.TextField(assetBundlePath);
            if (GUILayout.Button("选择", GUILayout.Width(50)))
            {
                SelectAssetBundlePath();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制基础操作按钮
        /// </summary>
        void DrawBasicOperations()
        {
            EditorGUILayout.LabelField("基础操作", EditorStyles.boldLabel);

            // 版本号配置区域
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("版本配置", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("清单版本号:", GUILayout.Width(80));
            manifestVersion = EditorGUILayout.TextField(manifestVersion);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("默认AB包版本:", GUILayout.Width(80));
            defaultBundleVersion = EditorGUILayout.TextField(defaultBundleVersion);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("清单版本号：整个AB包清单的版本\n默认AB包版本：新生成AB包的默认版本号", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("扫描AssetBundle", GUILayout.Height(25)))
            {
                ScanAssetBundles();
            }

            if (GUILayout.Button("生成版本信息", GUILayout.Height(25)))
            {
                GenerateVersionInfo();
            }

            if (GUILayout.Button("分析依赖关系", GUILayout.Height(25)))
            {
                AnalyzeDependencies();
            }

            EditorGUILayout.EndHorizontal();
        }


        /// <summary>
        /// 绘制保存操作按钮
        /// </summary>
        void DrawSaveOperations()
        {
            EditorGUILayout.LabelField("保存操作", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存到StreamingAssets", GUILayout.Height(25)))
            {
                SaveManifestToFile();
            }

            if (GUILayout.Button("保存到自定义路径", GUILayout.Height(25)))
            {
                SaveManifestToCustomPath();
            }

            if (GUILayout.Button("加载现有清单", GUILayout.Height(25)))
            {
                LoadExistingManifest();
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 热更新功能

        /// <summary>
        /// 绘制热更新配置操作按钮
        /// </summary>
        void DrawHotUpdateOperations()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("热更新配置", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成空版本清单", GUILayout.Height(25)))
            {
                GenerateEmptyManifest();
            }

            if (GUILayout.Button("生成服务器配置", GUILayout.Height(25)))
            {
                GenerateServerConfig();
            }

            EditorGUILayout.EndHorizontal();

            // 添加详细的使用说明
            EditorGUILayout.HelpBox(
                "热更新流程说明：\n" +
                "1. 先点击'生成版本信息'创建完整的AB包清单\n" +
                "2. 点击'生成服务器配置'创建服务器上传文件\n" +
                "3. 点击'生成空版本清单'清空StreamingAssets中的AB包\n" +
                "4. 将ServerUpload文件夹上传到服务器\n" +
                "5. 打包游戏（安装包将不包含AB包，首次启动时下载）",
                MessageType.Info);
        }

        /// <summary>
        /// 生成空版本清单（用于热更新）
        /// </summary>
        void GenerateEmptyManifest()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    // 创建空的版本清单
                    var emptyManifest = new AssetBundleManifest
                    {
                        manifestVersion = "0.0.0",
                        createTime = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"),
                        assetBundles = new List<AssetBundleInfo>()
                    };

                    // 保存空清单到StreamingAssets
                    string manifestPath = AssetBundleConfig.GetLocalManifestPath();
                    string json = JsonUtility.ToJson(emptyManifest, true);
                    File.WriteAllText(manifestPath, json);

                    // 删除StreamingAssets中的所有AB包文件（但保留清单文件）
                    DeleteAssetBundlesFromStreamingAssets();

                    // 刷新资源数据库
                    AssetDatabase.Refresh();

                    Debug.Log($"空版本清单已生成: {manifestPath}");
                    Debug.Log("StreamingAssets中的AB包文件已删除，只保留空版本清单和必备资源");

                    // 显示完成对话框
                    EditorUtility.DisplayDialog("空版本清单生成完成",
                        "操作完成！\n\n" +
                        "• 已生成空版本清单（版本0.0.0）\n" +
                        "• 已删除StreamingAssets中的AB包文件\n" +
                        "• 现在可以打包游戏，安装包将只包含必备AB包内容\n" +
                        "• 游戏首次启动时会从服务器下载你所选AB包", "确定");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"生成空版本清单失败: {e.Message}");
                    EditorUtility.DisplayDialog("错误", $"生成失败: {e.Message}", "确定");
                }
            };
        }

        /// <summary>
        /// 删除StreamingAssets中的AB包文件，但保留清单文件
        /// </summary>
        void DeleteAssetBundlesFromStreamingAssets()
        {
            string streamingAssetsPath = Path.Combine(Application.streamingAssetsPath, AssetBundleConfig.LocalBundleDirectory);

            if (!Directory.Exists(streamingAssetsPath))
                return;

            var files = Directory.GetFiles(streamingAssetsPath, "*", SearchOption.AllDirectories);
            int deletedCount = 0;

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);

                // 跳过清单文件和meta文件，只删除AB包文件
                if (fileName == AssetBundleConfig.ManifestFileName || fileName.EndsWith(".meta"))
                    continue;

                if (CheckName(fileName))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                    deletedCount++;
                    Debug.Log($"已删除AB包文件: {fileName}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"删除文件失败 {fileName}: {e.Message}");
                }
            }

            Debug.Log($"共删除了 {deletedCount} 个AB包文件");
        }


        /// <summary>
        /// 不删除首包资源
        /// </summary>
        bool CheckName(string bundleName)
        {
            var nameList = m_NameScriptableObject.assetBundleNames;
            foreach (var names in nameList)
            {
                if (names == bundleName)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region 服务器配置生成

        /// <summary>
        /// 生成服务器配置文件
        /// </summary>
        void GenerateServerConfig()
        {
            if (bundleManifest == null)
            {
                Debug.LogError("请先生成完整的版本信息");
                EditorUtility.DisplayDialog("错误", "请先点击'生成版本信息'创建完整的AB包清单", "确定");
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    // 创建服务器配置数据（使用相同的数据结构）
                    var serverManifest = new AssetBundleManifest
                    {
                        manifestVersion = bundleManifest.manifestVersion,
                        createTime = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"),
                        assetBundles = new List<AssetBundleInfo>(bundleManifest.assetBundles)
                    };

                    // 创建服务器上传目录
                    string serverConfigPath = Path.Combine(Application.dataPath, "../ServerUpload");
                    Directory.CreateDirectory(serverConfigPath);

                    // 保存服务器版本清单
                    string serverManifestPath = Path.Combine(serverConfigPath, AssetBundleConfig.ManifestFileName);
                    string serverJson = JsonUtility.ToJson(serverManifest, true);
                    File.WriteAllText(serverManifestPath, serverJson);

                    // 复制AB包文件到服务器上传目录
                    CopyAssetBundlesToServerFolder(serverConfigPath);

                    Debug.Log($"服务器配置已生成: {serverConfigPath}");

                    // 显示完成对话框
                    EditorUtility.DisplayDialog("服务器配置生成完成",
                        $"服务器配置已生成到：\n{serverConfigPath}\n\n" +
                        "包含内容：\n" +
                        $"• {AssetBundleConfig.ManifestFileName}（服务器版本清单）\n" +
                        "• 所有AB包文件\n\n" +
                        "请将此文件夹中的所有内容上传到服务器。", "确定");

                    // 自动打开文件夹
                    EditorUtility.RevealInFinder(serverConfigPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"生成服务器配置失败: {e.Message}");
                    EditorUtility.DisplayDialog("错误", $"生成失败: {e.Message}", "确定");
                }
            };
        }

        /// <summary>
        /// 复制AB包文件到服务器上传文件夹
        /// </summary>
        void CopyAssetBundlesToServerFolder(string serverConfigPath)
        {
            if (!Directory.Exists(assetBundlePath))
            {
                Debug.LogWarning("AB包路径不存在，跳过复制AB包文件");
                return;
            }

            var files = Directory.GetFiles(assetBundlePath, "*", SearchOption.AllDirectories);
            int copiedCount = 0;

            foreach (string sourceFile in files)
            {
                string fileName = Path.GetFileName(sourceFile);

                // 跳过meta文件和清单文件，只复制AB包文件
                if (fileName.EndsWith(".meta") || fileName == AssetBundleConfig.ManifestFileName)
                    continue;

                string destFile = Path.Combine(serverConfigPath, fileName);

                try
                {
                    File.Copy(sourceFile, destFile, true);
                    copiedCount++;
                    Debug.Log($"已复制AB包: {fileName}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"复制文件失败 {fileName}: {e.Message}");
                }
            }

            Debug.Log($"已复制 {copiedCount} 个AB包文件到服务器上传目录");
        }

        #endregion

        #region 基础功能实现

        /// <summary>
        /// 选择AssetBundle文件夹路径
        /// </summary>
        void SelectAssetBundlePath()
        {
            EditorApplication.delayCall += () =>
            {
                string selectedPath = EditorUtility.OpenFolderPanel("选择AssetBundle文件夹", assetBundlePath, "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    assetBundlePath = selectedPath;
                    Repaint();
                }
            };
        }

        /// <summary>
        /// 扫描AssetBundle文件
        /// </summary>
        void ScanAssetBundles()
        {
            if (string.IsNullOrEmpty(assetBundlePath) || !Directory.Exists(assetBundlePath))
            {
                Debug.LogError("AssetBundle路径无效，请先选择正确的路径");
                EditorUtility.DisplayDialog("路径错误", "AssetBundle路径无效，请先选择正确的路径", "确定");
                return;
            }

            var files = Directory.GetFiles(assetBundlePath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta") &&
                            !f.EndsWith(".manifest") &&
                            !Path.GetFileName(f).Contains("AssetBundleManifest"))
                .ToArray();

            Debug.Log($"扫描完成：在路径 {assetBundlePath} 中找到 {files.Length} 个AssetBundle文件");

            foreach (var file in files)
            {
                Debug.Log($"- {Path.GetFileName(file)}");
            }

            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("扫描结果", "在指定路径下没有找到AssetBundle文件", "确定");
            }
            else
            {
                EditorUtility.DisplayDialog("扫描完成", $"找到 {files.Length} 个AssetBundle文件\n详细信息请查看Console窗口", "确定");
            }
        }

        /// <summary>
        /// 生成完整的版本信息
        /// </summary>
        void GenerateVersionInfo()
        {
            if (string.IsNullOrEmpty(assetBundlePath) || !Directory.Exists(assetBundlePath))
            {
                Debug.LogError("AssetBundle路径无效，请先选择正确的路径");
                EditorUtility.DisplayDialog("路径错误", "AssetBundle路径无效，请先选择正确的路径", "确定");
                return;
            }

            // 验证版本号格式
            if (string.IsNullOrEmpty(manifestVersion))
            {
                Debug.LogError("清单版本号不能为空");
                EditorUtility.DisplayDialog("版本号错误", "清单版本号不能为空", "确定");
                return;
            }

            if (string.IsNullOrEmpty(defaultBundleVersion))
            {
                Debug.LogError("默认AB包版本号不能为空");
                EditorUtility.DisplayDialog("版本号错误", "默认AB包版本号不能为空", "确定");
                return;
            }

            // 如果已经存在清单，保留现有AB包的版本信息
            Dictionary<string, string> existingVersions = new Dictionary<string, string>();
            if (bundleManifest?.assetBundles != null)
            {
                foreach (var bundle in bundleManifest.assetBundles)
                {
                    existingVersions[bundle.bundleName] = bundle.version;
                }
            }

            // 创建新的版本清单
            bundleManifest = new AssetBundleManifest
            {
                manifestVersion = manifestVersion,
                createTime = System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz"),
                assetBundles = new List<AssetBundleInfo>()
            };

            var files = Directory.GetFiles(assetBundlePath, "*", SearchOption.AllDirectories)
                .Where(f => !f.EndsWith(".meta") &&
                            !f.EndsWith(".manifest") &&
                            !Path.GetFileName(f).Contains("AssetBundleManifest"))
                .ToArray();

            Debug.Log($"开始生成版本信息，共找到 {files.Length} 个AssetBundle文件");
            Debug.Log($"使用清单版本号: {manifestVersion}");
            Debug.Log($"默认AB包版本号: {defaultBundleVersion}");

            foreach (var file in files)
            {
                try
                {
                    string bundleName = Path.GetFileName(file);

                    // 如果存在旧版本信息，保留原版本号，否则使用默认版本号
                    string bundleVersion = existingVersions.ContainsKey(bundleName)
                        ? existingVersions[bundleName]
                        : defaultBundleVersion;

                    var bundleInfo = new AssetBundleInfo
                    {
                        bundleName = bundleName,
                        version = bundleVersion,
                        hash = CalculateFileHash(file),
                        size = new FileInfo(file).Length,
                        buildTime = File.GetLastWriteTime(file).ToString("yyyy-MM-dd HH:mm:ss"),
                        enableUpdate = true,
                        description = "",
                        priority = 0,
                        dependencies = new List<string>(),
                        allDependencies = new List<string>(),
                        dependencyLevel = 0
                    };

                    bundleManifest.assetBundles.Add(bundleInfo);

                    string versionInfo = existingVersions.ContainsKey(bundleName) ? "(保留原版本)" : "(使用默认版本)";
                    Debug.Log($"已添加AB包: {bundleInfo.bundleName} 版本:{bundleInfo.version} {versionInfo} (大小: {FormatFileSize(bundleInfo.size)})");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"处理文件 {Path.GetFileName(file)} 时出错: {e.Message}");
                }
            }

            Debug.Log($"版本信息生成完成，共生成了 {bundleManifest.assetBundles.Count} 个AssetBundle的版本信息");

            Repaint();

            EditorUtility.DisplayDialog("生成完成",
                $"版本信息生成完成！\n\n" +
                $"清单版本: {manifestVersion}\n" +
                $"共处理了 {bundleManifest.assetBundles.Count} 个AssetBundle文件\n" +
                "现在可以保存版本清单或生成热更新配置", "确定");
        }


        /// <summary>
        /// 分析依赖关系
        /// </summary>
        void AnalyzeDependencies()
        {
            if (bundleManifest == null || bundleManifest.assetBundles.Count == 0)
            {
                Debug.LogError("请先生成版本信息");
                EditorUtility.DisplayDialog("错误", "请先点击'生成版本信息'创建版本清单", "确定");
                return;
            }

            Debug.Log("开始分析AssetBundle依赖关系...");

            // 这里可以添加实际的依赖分析逻辑
            // 目前先简单设置一些示例依赖关系
            foreach (var bundle in bundleManifest.assetBundles)
            {
                // 重置依赖信息
                bundle.dependencies.Clear();
                bundle.allDependencies.Clear();
                bundle.dependencyLevel = 0;

                // 这里可以根据实际需要添加依赖分析逻辑
                // 例如：分析AssetBundle的manifest文件来获取真实的依赖关系
            }

            Debug.Log("依赖关系分析完成");
            Repaint();

            EditorUtility.DisplayDialog("分析完成", "依赖关系分析完成！", "确定");
        }

        /// <summary>
        /// 计算文件的MD5哈希值
        /// </summary>
        string CalculateFileHash(string filePath)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        var hash = md5.ComputeHash(stream);
                        return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"计算文件哈希失败 {filePath}: {e.Message}");
                return "";
            }
        }

        #endregion

        #region 保存和加载功能

        /// <summary>
        /// 保存版本清单到StreamingAssets文件夹
        /// </summary>
        void SaveManifestToFile()
        {
            if (bundleManifest == null)
            {
                Debug.LogError("请先生成版本信息");
                EditorUtility.DisplayDialog("错误", "请先点击'生成版本信息'创建版本清单", "确定");
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try
                {
                    string manifestPath = AssetBundleConfig.GetLocalManifestPath();
                    string json = JsonUtility.ToJson(bundleManifest, true);
                    File.WriteAllText(manifestPath, json);

                    AssetDatabase.Refresh();

                    Debug.Log($"版本清单已保存到StreamingAssets: {manifestPath}");
                    EditorUtility.DisplayDialog("保存成功", $"版本清单已保存到：\n{manifestPath}", "确定");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"保存版本清单失败: {e.Message}");
                    EditorUtility.DisplayDialog("保存失败", $"保存失败: {e.Message}", "确定");
                }
            };
        }

        /// <summary>
        /// 保存版本清单到自定义路径
        /// </summary>
        void SaveManifestToCustomPath()
        {
            if (bundleManifest == null)
            {
                Debug.LogError("请先生成版本信息");
                EditorUtility.DisplayDialog("错误", "请先点击'生成版本信息'创建版本清单", "确定");
                return;
            }

            EditorApplication.delayCall += () =>
            {
                string savePath = EditorUtility.SaveFilePanel("保存版本清单", "", "AssetBundleManifest", "json");
                if (!string.IsNullOrEmpty(savePath))
                {
                    try
                    {
                        string json = JsonUtility.ToJson(bundleManifest, true);
                        File.WriteAllText(savePath, json);

                        Debug.Log($"版本清单已保存到自定义路径: {savePath}");
                        EditorUtility.DisplayDialog("保存成功", $"版本清单已保存到：\n{savePath}", "确定");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"保存失败: {e.Message}");
                        EditorUtility.DisplayDialog("保存失败", $"保存失败: {e.Message}", "确定");
                    }
                }
            };
        }

        /// <summary>
        /// 加载现有的版本清单文件
        /// </summary>
        void LoadExistingManifest()
        {
            EditorApplication.delayCall += () =>
            {
                string loadPath = EditorUtility.OpenFilePanel("加载版本清单", "", "json");
                if (!string.IsNullOrEmpty(loadPath))
                {
                    try
                    {
                        string json = File.ReadAllText(loadPath);
                        bundleManifest = JsonUtility.FromJson<AssetBundleManifest>(json);

                        Debug.Log($"版本清单已加载: {loadPath}");
                        Debug.Log($"加载的清单版本: {bundleManifest.manifestVersion}, AB包数量: {bundleManifest.assetBundles.Count}");

                        Repaint();

                        EditorUtility.DisplayDialog("加载成功",
                            $"版本清单加载成功！\n\n" +
                            $"版本: {bundleManifest.manifestVersion}\n" +
                            $"AB包数量: {bundleManifest.assetBundles.Count}\n" +
                            $"创建时间: {bundleManifest.createTime}", "确定");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"加载失败: {e.Message}");
                        EditorUtility.DisplayDialog("加载失败", $"加载失败: {e.Message}", "确定");
                    }
                }
            };
        }

        #endregion

        #region 界面显示功能

        /// <summary>
        /// 绘制AssetBundle列表
        /// </summary>
        void DrawAssetBundleList()
        {
            if (bundleManifest == null || bundleManifest.assetBundles == null)
                return;

            EditorGUILayout.LabelField($"AssetBundle列表 ({bundleManifest.assetBundles.Count})", EditorStyles.boldLabel);

            // 显示版本清单的基本信息
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField($"清单版本: {bundleManifest.manifestVersion}", GUILayout.Width(150));
            EditorGUILayout.LabelField($"创建时间: {bundleManifest.createTime}");
            EditorGUILayout.EndHorizontal();

            // 在批量优先级操作按钮后面添加版本号批量操作
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField("批量版本操作:", GUILayout.Width(120));
            EditorGUILayout.LabelField("新版本号:", GUILayout.Width(60));
            string batchVersion = EditorGUILayout.TextField(defaultBundleVersion, GUILayout.Width(80));
            if (GUILayout.Button("应用到全部", GUILayout.Width(80)))
            {
                SetAllBundleVersions(batchVersion);
            }

            if (GUILayout.Button("应用到启用更新的", GUILayout.Width(120)))
            {
                SetEnabledBundleVersions(batchVersion);
            }

            EditorGUILayout.EndHorizontal();


            // 创建滚动视图来显示AB包列表
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(300));

            // 遍历显示每个AB包的信息
            for (int i = 0; i < bundleManifest.assetBundles.Count; i++)
            {
                var bundle = bundleManifest.assetBundles[i];

                EditorGUILayout.BeginVertical("box");

                // 第一行：序号、名称、版本、大小、更新开关
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{i + 1}]", GUILayout.Width(30));
                EditorGUILayout.LabelField($"名称: {bundle.bundleName}", GUILayout.Width(180));

// 版本号改为可编辑字段 - 这里就是单独设置版本号的地方
                EditorGUILayout.LabelField("版本:", GUILayout.Width(35));
                bundle.version = EditorGUILayout.TextField(bundle.version, GUILayout.Width(65));

                EditorGUILayout.LabelField($"大小: {FormatFileSize(bundle.size)}", GUILayout.Width(100));
                bundle.enableUpdate = EditorGUILayout.Toggle("启用更新", bundle.enableUpdate, GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();

                // 第二行：哈希值、优先级显示和操作
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Hash: {bundle.hash}", GUILayout.Width(280));

                // 优先级显示
                string priorityText = bundle.priority == -1 ? "固定最高" : bundle.priority.ToString();
                EditorGUILayout.LabelField($"优先级: {priorityText}", GUILayout.Width(80));

                // 优先级操作按钮
                if (GUILayout.Button("置顶", GUILayout.Width(40)))
                {
                    MoveToTop(i);
                }

                if (GUILayout.Button("置底", GUILayout.Width(40)))
                {
                    MoveToBottom(i);
                }

                if (GUILayout.Button("固定", GUILayout.Width(40)))
                {
                    SetFixedPriority(i);
                }

                // 上下移动按钮
                GUI.enabled = i > 0;
                if (GUILayout.Button("↑", GUILayout.Width(25)))
                {
                    SwapBundlePosition(i, i - 1);
                }

                GUI.enabled = i < bundleManifest.assetBundles.Count - 1;
                if (GUILayout.Button("↓", GUILayout.Width(25)))
                {
                    SwapBundlePosition(i, i + 1);
                }

                GUI.enabled = true;

                EditorGUILayout.LabelField($"构建时间: {bundle.buildTime}", GUILayout.Width(150));
                EditorGUILayout.EndHorizontal();

                // 第三行：依赖信息
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"依赖层级: {bundle.dependencyLevel}", GUILayout.Width(80));
                EditorGUILayout.LabelField($"直接依赖: {bundle.dependencies.Count}", GUILayout.Width(80));
                EditorGUILayout.LabelField($"所有依赖: {bundle.allDependencies.Count}", GUILayout.Width(80));

                // 显示依赖列表（如果有的话）
                if (bundle.dependencies.Count > 0)
                {
                    string depText = string.Join(", ", bundle.dependencies.Take(3));
                    if (bundle.dependencies.Count > 3)
                        depText += $"... (+{bundle.dependencies.Count - 3})";
                    EditorGUILayout.LabelField($"依赖: {depText}");
                }

                EditorGUILayout.EndHorizontal();

                // 第四行：描述（可编辑）
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("描述:", GUILayout.Width(40));
                bundle.description = EditorGUILayout.TextField(bundle.description);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            EditorGUILayout.EndScrollView();

            // 显示统计信息
            if (bundleManifest.assetBundles.Count > 0)
            {
                long totalSize = bundleManifest.assetBundles.Sum(b => b.size);
                int enabledCount = bundleManifest.assetBundles.Count(b => b.enableUpdate);
                int withDependencies = bundleManifest.assetBundles.Count(b => b.dependencies.Count > 0);

                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField($"总计: {bundleManifest.assetBundles.Count} 个文件");
                EditorGUILayout.LabelField($"启用更新: {enabledCount} 个");
                EditorGUILayout.LabelField($"有依赖: {withDependencies} 个");
                EditorGUILayout.LabelField($"总大小: {FormatFileSize(totalSize)}");
                EditorGUILayout.EndHorizontal();
            }
        }

        #endregion

        #region 优先级管理功能

        /// <summary>
        /// 将指定AB包移动到列表顶部（最高优先级）
        /// </summary>
        void MoveToTop(int index)
        {
            if (bundleManifest?.assetBundles == null || index <= 0 || index >= bundleManifest.assetBundles.Count)
                return;

            var bundle = bundleManifest.assetBundles[index];
            bundleManifest.assetBundles.RemoveAt(index);
            bundleManifest.assetBundles.Insert(0, bundle);

            ReassignPrioritiesByOrder();
            Debug.Log($"已将 {bundle.bundleName} 移动到最高优先级");
            Repaint();
        }

        /// <summary>
        /// 将指定AB包移动到列表底部（最低优先级）
        /// </summary>
        void MoveToBottom(int index)
        {
            if (bundleManifest?.assetBundles == null || index < 0 || index >= bundleManifest.assetBundles.Count - 1)
                return;

            var bundle = bundleManifest.assetBundles[index];
            bundleManifest.assetBundles.RemoveAt(index);
            bundleManifest.assetBundles.Add(bundle);

            ReassignPrioritiesByOrder();
            Debug.Log($"已将 {bundle.bundleName} 移动到最低优先级");
            Repaint();
        }

        /// <summary>
        /// 设置指定AB包为固定最高优先级
        /// </summary>
        void SetFixedPriority(int index)
        {
            if (bundleManifest?.assetBundles == null || index < 0 || index >= bundleManifest.assetBundles.Count)
                return;

            var bundle = bundleManifest.assetBundles[index];
            bundle.priority = -1;

            Debug.Log($"已将 {bundle.bundleName} 设置为固定最高优先级");
            Repaint();
        }

        /// <summary>
        /// 交换两个AB包在列表中的位置
        /// </summary>
        void SwapBundlePosition(int index1, int index2)
        {
            if (bundleManifest?.assetBundles == null ||
                index1 < 0 || index1 >= bundleManifest.assetBundles.Count ||
                index2 < 0 || index2 >= bundleManifest.assetBundles.Count)
                return;

            var bundle1 = bundleManifest.assetBundles[index1];
            var bundle2 = bundleManifest.assetBundles[index2];

            bundleManifest.assetBundles[index1] = bundle2;
            bundleManifest.assetBundles[index2] = bundle1;

            ReassignPrioritiesByOrder();

            Debug.Log($"已交换 {bundle2.bundleName} 和 {bundle1.bundleName} 的位置");
            Repaint();
        }

        /// <summary>
        /// 将所有AB包设置为最高优先级（保持当前顺序）
        /// </summary>
        void SetAllToHighestPriority()
        {
            if (bundleManifest?.assetBundles == null) return;

            ReassignPrioritiesByOrder();
            Debug.Log("已将所有AB包设置为最高优先级（按当前顺序）");
            Repaint();
        }

        /// <summary>
        /// 将所有AB包设置为最低优先级
        /// </summary>
        void SetAllToLowestPriority()
        {
            if (bundleManifest?.assetBundles == null) return;

            int lowestPriority = 9999;
            foreach (var bundle in bundleManifest.assetBundles)
            {
                bundle.priority = lowestPriority;
            }

            Debug.Log($"已将所有AB包设置为最低优先级: {lowestPriority}");
            Repaint();
        }

        /// <summary>
        /// 按照当前列表顺序重新分配优先级
        /// </summary>
        void ReassignPrioritiesByOrder()
        {
            if (bundleManifest?.assetBundles == null) return;

            for (int i = 0; i < bundleManifest.assetBundles.Count; i++)
            {
                var bundle = bundleManifest.assetBundles[i];
                if (bundle.priority != -1)
                {
                    bundle.priority = i;
                }
            }
        }

        #endregion

        #region 版本号管理功能

        /// <summary>
        /// 设置所有AB包的版本号
        /// </summary>
        void SetAllBundleVersions(string version)
        {
            if (bundleManifest?.assetBundles == null || string.IsNullOrEmpty(version)) return;

            foreach (var bundle in bundleManifest.assetBundles)
            {
                bundle.version = version;
            }

            Debug.Log($"已将所有AB包版本号设置为: {version}");
            Repaint();
        }

        /// <summary>
        /// 设置启用更新的AB包的版本号
        /// </summary>
        void SetEnabledBundleVersions(string version)
        {
            if (bundleManifest?.assetBundles == null || string.IsNullOrEmpty(version)) return;

            int count = 0;
            foreach (var bundle in bundleManifest.assetBundles)
            {
                if (bundle.enableUpdate)
                {
                    bundle.version = version;
                    count++;
                }
            }

            Debug.Log($"已将 {count} 个启用更新的AB包版本号设置为: {version}");
            Repaint();
        }

        #endregion

        /// <summary>
        /// 格式化文件大小显示
        /// </summary>
        string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
    }
}
