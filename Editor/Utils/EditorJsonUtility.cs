using UnityEngine;

namespace QHotUpdateSystem.Editor.Utils
{
    /// <summary>
    /// 编辑器 JSON 简易封装（优先 Unity JsonUtility）
    /// </summary>
    public static class EditorJsonUtility
    {
        public static string ToJson(object obj, bool pretty) => JsonUtility.ToJson(obj, pretty);
        public static T FromJson<T>(string json) => JsonUtility.FromJson<T>(json);
    }
}