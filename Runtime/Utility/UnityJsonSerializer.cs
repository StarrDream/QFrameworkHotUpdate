using UnityEngine;

namespace QHotUpdateSystem.Utility
{
    /// <summary>
    /// Unity 内置 JsonUtility 实现
    /// </summary>
    public class UnityJsonSerializer : IJsonSerializer
    {
        public string Serialize(object obj, bool pretty = false)
        {
            return JsonUtility.ToJson(obj, pretty);
        }

        public T Deserialize<T>(string json)
        {
            return JsonUtility.FromJson<T>(json);
        }
    }
}