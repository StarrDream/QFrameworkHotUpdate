using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using QHotUpdateSystem.Logging;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 基础 HTTP 下载器（支持 Range 断点续传 + 流式写入）
    /// </summary>
    public static class HttpDownloader
    {
        public class DownloadOptions
        {
            public int TimeoutSec = 30;
            public Func<long, bool> OnDelta;              // 传入本次新增字节
            public Func<bool> ShouldAbort;               // 外部取消 / 中断
            public Func<Task> OnPauseWait;               // 暂停等待
            public bool SupportResume = true;
            public long ExistingBytes = 0;               // 已有 .part 长度
            public long ExpectedTotal = -1;              // 期望总长度（来自 meta 或 FileEntry）
            public string RemoteUrl;                     // 冗余记录
        }

        public class HeadResult
        {
            public bool Ok;
            public long ContentLength;
            public bool AcceptRanges;
        }

        public static async Task<HeadResult> HeadAsync(string url, int timeoutSec)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "HEAD";
            req.Timeout = timeoutSec * 1000;
            try
            {
                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                {
                    var len = resp.ContentLength;
                    bool ar = string.Equals(resp.Headers["Accept-Ranges"], "bytes", StringComparison.OrdinalIgnoreCase);
                    return new HeadResult { Ok = true, ContentLength = len, AcceptRanges = ar };
                }
            }
            catch
            {
                return new HeadResult { Ok = false };
            }
        }

        /// <summary>
        /// 下载（可续传）：如果 ExistingBytes > 0 尝试 Range，失败则回退全量重下。
        /// </summary>
        public static async Task<bool> DownloadFile(DownloadTask task, string url, string tempPath, DownloadOptions opt)
        {
            // 简化：若存在 ExistingBytes 但 ExpectedTotal <= ExistingBytes => 认为已满（交给 Finalize 校验）
            if (opt.ExistingBytes > 0 && opt.ExpectedTotal > 0 && opt.ExistingBytes >= opt.ExpectedTotal)
                return true;

            bool attemptResume = opt.SupportResume && opt.ExistingBytes > 0;

            // 多次尝试：1) (可选) 续传 2) 全量
            for (int phase = 0; phase < (attemptResume ? 2 : 1); phase++)
            {
                bool useRange = attemptResume && phase == 0;
                long startOffset = useRange ? opt.ExistingBytes : 0;

                if (opt.ShouldAbort?.Invoke() == true)
                    return false;

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = opt.TimeoutSec * 1000;
                req.ReadWriteTimeout = opt.TimeoutSec * 1000;

                if (useRange)
                {
                    req.AddRange(startOffset);
                }

                HttpWebResponse resp = null;
                try
                {
                    resp = (HttpWebResponse)await req.GetResponseAsync();
                }
                catch (WebException wex)
                {
                    HotUpdateLogger.Warn($"Http get failed (phase={phase} range={useRange}): {wex.Message}");
                    resp = wex.Response as HttpWebResponse;
                    if (resp == null)
                    {
                        continue;
                    }
                }

                bool isPartial = resp.StatusCode == HttpStatusCode.PartialContent;
                if (useRange && !isPartial)
                {
                    // 服务器不支持 Range -> 回退到全量
                    HotUpdateLogger.Info($"Server did not return 206 for resume, fallback full download. code={resp.StatusCode}");
                    attemptResume = false; // 不再尝试第二轮
                    SafeClose(resp);
                    continue;
                }

                // 确定预期总长度
                long remoteAppendLen;
                if (isPartial)
                {
                    // Content-Range: bytes start-end/total
                    var cr = resp.Headers["Content-Range"];
                    long total = opt.ExpectedTotal;
                    if (!string.IsNullOrEmpty(cr))
                    {
                        // 简易解析
                        // bytes 12345-99999/100000
                        int slash = cr.LastIndexOf('/');
                        if (slash > 0 && long.TryParse(cr.Substring(slash + 1), out var tot))
                            total = tot;
                    }
                    if (total > 0 && opt.ExpectedTotal <= 0)
                        opt.ExpectedTotal = total;

                    remoteAppendLen = resp.ContentLength;
                }
                else
                {
                    // 全量
                    remoteAppendLen = resp.ContentLength;
                    if (opt.ExpectedTotal <= 0 && resp.ContentLength > 0)
                        opt.ExpectedTotal = resp.ContentLength;
                    // 如果存在旧的 temp 但不能续传，删除旧文件
                    if (!useRange && opt.ExistingBytes > 0)
                    {
                        SafeDelete(tempPath);
                        opt.ExistingBytes = 0;
                    }
                }

                // 打开文件流
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));
                using (var fs = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    if (useRange)
                        fs.Seek(startOffset, SeekOrigin.Begin);
                    else
                        fs.SetLength(0);

                    const int BUF = 64 * 1024;
                    byte[] buffer = new byte[BUF];
                    int read;
                    using (var rs = resp.GetResponseStream())
                    {
                        while (true)
                        {
                            if (opt.ShouldAbort?.Invoke() == true)
                                return false;

                            if (opt.OnPauseWait != null)
                                await opt.OnPauseWait();

                            read = await rs.ReadAsync(buffer, 0, buffer.Length);
                            if (read <= 0) break;

                            fs.Write(buffer, 0, read);
                            fs.Flush(false);

                            opt.OnDelta?.Invoke(read);
                        }
                    }
                }

                SafeClose(resp);

                // 下载阶段完成
                return true;
            }

            return false;
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                HotUpdateLogger.Warn("Delete temp failed: " + path + " err=" + e.Message);
            }
        }

        private static void SafeClose(HttpWebResponse resp)
        {
            try { resp?.Close(); } catch { }
        }
    }
}
