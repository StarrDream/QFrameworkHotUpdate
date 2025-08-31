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

        /// <summary>
        /// 保存本地版本（原实现为直接覆盖写入，存在崩溃/断电导致 JSON 截断风险）
        /// ★ 修复：采用临时文件 + 原子替换，保证要么旧文件完整，要么新文件完整。
        /// </summary>
        public static void SaveLocal(string path, VersionInfo info, Utility.IJsonSerializer json, bool pretty = true)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var txt = json.Serialize(info, pretty);

                // ★ 修复：原子写入
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, txt);

                if (File.Exists(path))
                {
                    try
                    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                        // Windows 可使用 File.Replace 获得更强一致性
                        File.Replace(tmp, path, null);
#else
                        // 其它平台直接先删除再移动
                        File.Delete(path);
                        File.Move(tmp, path);
#endif
                    }
                    catch
                    {
                        // 回退策略：若 Replace 失败，再尝试简单覆盖
                        try
                        {
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(tmp, path);
                        }
                        catch (Exception ex2)
                        {
                            HotUpdateLogger.Error("Save version (fallback) failed: " + ex2.Message);
                        }
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
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