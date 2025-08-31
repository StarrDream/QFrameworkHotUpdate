using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using QHotUpdateSystem.Logging;

namespace QHotUpdateSystem.Version
{
    public static class VersionLoader
    {
        public static VersionInfo LoadLocal(string path, Utility.IJsonSerializer json)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var txt = File.ReadAllText(path);
                return json.Deserialize<VersionInfo>(txt);
            }
            catch (Exception e)
            {
                HotUpdateLogger.Warn("Parse local version failed: " + e.Message);
                return null;
            }
        }

        public static void SaveLocal(string path, VersionInfo info, Utility.IJsonSerializer json, bool pretty = true)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var txt = json.Serialize(info, pretty);
                File.WriteAllText(path, txt);
            }
            catch (Exception e)
            {
                HotUpdateLogger.Error("Save version failed: " + e.Message);
            }
        }

        public static async Task<VersionInfo> LoadRemote(string url, Utility.IJsonSerializer json)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield();
#if UNITY_2020_3_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                {
                    HotUpdateLogger.Warn($"Remote version request failed: \n {url} \n" + req.error);
                    return null;
                }

                try
                {
                    return json.Deserialize<VersionInfo>(req.downloadHandler.text);
                }
                catch (Exception e)
                {
                    HotUpdateLogger.Error($"Parse remote version failed:  \n {url} \n" + e.Message);
                    return null;
                }
            }
        }
    }
}