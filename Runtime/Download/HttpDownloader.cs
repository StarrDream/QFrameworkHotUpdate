using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using QHotUpdateSystem.Logging;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 支持取消与暂停（通过外部轮询）的单文件下载
    /// 新增：临时文件打开 Sharing Violation 重试；更详细日志。
    /// </summary>
    public static class HttpDownloader
    {
        public class DownloadOptions
        {
            public int TimeoutSec;
            public Func<long, bool> OnDelta;          // 每获得新增字节（delta）回调；返回 false 中止
            public Func<bool> ShouldAbort;            // 外部中止检查
            public Func<Task> OnPauseWait;            // 暂停等待
        }

        public static async Task<bool> DownloadFile(DownloadTask task, string url, string tempPath, DownloadOptions opt)
        {
            long existing = ResumeMetadata.GetPartialSize(tempPath);
            task.DownloadedBytes = existing;

            FileStream fs = null;
            UnityWebRequest req = null;
            try
            {
                fs = OpenWithRetry(tempPath); // 关键：共享冲突重试
                req = UnityWebRequest.Get(url);
                if (existing > 0)
                    req.SetRequestHeader("Range", $"bytes={existing}-");
                req.timeout = opt.TimeoutSec;
                var op = req.SendWebRequest();

                long lastReported = existing;

                while (!op.isDone)
                {
                    // 中止检测
                    if (opt.ShouldAbort?.Invoke() == true)
                    {
                        req.Abort();
                        task.LastError = "Aborted";
                        return false;
                    }

                    // 暂停
                    if (opt.OnPauseWait != null)
                        await opt.OnPauseWait();

                    // 进度增量
                    long downloaded = (long)req.downloadedBytes + existing;
                    if (downloaded > lastReported)
                    {
                        long delta = downloaded - lastReported;
                        lastReported = downloaded;
                        if (opt.OnDelta != null && !opt.OnDelta(delta))
                        {
                            req.Abort();
                            task.LastError = "Aborted by OnDelta";
                            return false;
                        }
                    }
                    await Task.Yield();
                }

#if UNITY_2020_3_OR_NEWER
                if (req.result != UnityWebRequest.Result.Success)
#else
                if (req.isNetworkError || req.isHttpError)
#endif
                {
                    task.LastError = req.error;
                    return false;
                }

                // 处理最终数据块
                bool rangeNotSupported = existing > 0 && req.responseCode == 200;
                byte[] data = req.downloadHandler.data;
                if (rangeNotSupported)
                {
                    // 服务器不支持 Range：需重写整个文件
                    fs.Dispose();
                    fs = null;
                    try
                    {
                        // 先删旧的，再写新的
                        if (File.Exists(tempPath))
                            File.Delete(tempPath);
                        File.WriteAllBytes(tempPath, data);
                    }
                    catch (IOException ioex)
                    {
                        HotUpdateLogger.Warn($"Rewrite temp failed (rangeNotSupported). {ioex.Message}\nTemp={tempPath}");
                        throw;
                    }
                    // 由于之前 existing 已统计，这里全量作为 delta 通知
                    opt.OnDelta?.Invoke(data.LongLength - existing);
                    task.DownloadedBytes = data.LongLength;
                }
                else
                {
                    // 正常追加
                    fs.Write(data, 0, data.Length);
                    task.DownloadedBytes = existing + data.LongLength;
                }

                return true;
            }
            catch (Exception e)
            {
                task.LastError = e.Message;
                HotUpdateLogger.Warn($"Download exception: {e.Message}\nTemp={tempPath}\nUrl={url}");
                return false;
            }
            finally
            {
                fs?.Close();
                req?.Dispose();
            }
        }

        #region Sharing Violation Retry Helpers

        // 打开临时文件（Append）并对 sharing violation 做有限次重试
        private static FileStream OpenWithRetry(string tempPath, int maxAttempts = 5, int initialDelayMs = 50)
        {
            int delay = initialDelayMs;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return new FileStream(
                        tempPath,
                        FileMode.Append,
                        FileAccess.Write,
                        // 放宽共享策略，避免其他只读扫描进程导致失败
                        FileShare.ReadWrite | FileShare.Delete
                    );
                }
                catch (IOException ioex) when (IsSharingViolation(ioex) && attempt < maxAttempts)
                {
                    // 仅针对共享冲突退避
                    System.Threading.Thread.Sleep(delay);
                    delay *= 2;
                }
            }

            // 最后一次若仍失败则让异常抛出
            return new FileStream(
                tempPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete
            );
        }

        private static bool IsSharingViolation(IOException ex)
        {
            // Windows: 0x20 (32) SHARING_VIOLATION, 0x21 (33) LOCK_VIOLATION
            const int ERROR_SHARING_VIOLATION = 0x20;
            const int ERROR_LOCK_VIOLATION = 0x21;
            int hr = System.Runtime.InteropServices.Marshal.GetHRForException(ex) & 0xFFFF;
            return hr == ERROR_SHARING_VIOLATION || hr == ERROR_LOCK_VIOLATION;
        }

        #endregion
    }
}
