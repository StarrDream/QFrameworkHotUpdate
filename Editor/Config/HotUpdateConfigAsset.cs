using UnityEngine;
using QHotUpdateSystem.Core;

namespace QHotUpdateSystem.Editor.Config
{
  public class HotUpdateConfigAsset : ScriptableObject
  {
      public string baseUrl = "https://cdn.example.com/HotUpdate/";
      public string outputRoot = "HotUpdateOutput";
      public string initialPackageOutput = "Assets/StreamingAssets/HotUpdate";
      public string hashAlgo = "md5";
      public string version = "1.0.0";
      public bool prettyJson = true;
      public bool cleanObsolete = true;

      [Header("签名 (HMAC-SHA256)")]
      public bool enableSignature = false;
      public string hmacSecret = "";

      public ModuleConfig[] modules;
      
      [Header("压缩设置")]
      public CompressionAlgorithm compressionAlgorithm = CompressionAlgorithm.None;

      /// <summary>
      /// 获取压缩算法字符串
      /// </summary>
      public string GetCompressionAlgoString()
      {
          switch (compressionAlgorithm)
          {
              case CompressionAlgorithm.Zip: return "zip";
              case CompressionAlgorithm.GZip: return "gzip";
              case CompressionAlgorithm.LZ4: return "lz4";
              default: return "";
          }
      }

      /// <summary>
      /// 是否启用了压缩
      /// </summary>
      public bool IsCompressionEnabled => compressionAlgorithm != CompressionAlgorithm.None;
  }

#if UNITY_EDITOR
  public static class HotUpdateConfigCreator
  {
      // priority 0 放在 Create 子菜单靠前位置
      [UnityEditor.MenuItem("Assets/Create/QHotUpdate/Config Asset", false, 0)]
      public static void Create()
      {
          var asset = ScriptableObject.CreateInstance<HotUpdateConfigAsset>();
          string path = UnityEditor.EditorUtility.SaveFilePanelInProject(
              "Create HotUpdate Config",
              "HotUpdateConfigAsset",
              "asset",
              ""
          );
          if (!string.IsNullOrEmpty(path))
          {
              UnityEditor.AssetDatabase.CreateAsset(asset, path);
              UnityEditor.AssetDatabase.SaveAssets();
              UnityEditor.Selection.activeObject = asset;
          }
      }
  }
#endif
}