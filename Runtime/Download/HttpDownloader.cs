using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using QHotUpdateSystem.Logging;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// HTTP 下载器
    /// </summary>
    public static class HttpDownloader
    {
        public class DownloadOptions
        {
            public int TimeoutSec = 30;
            public Func<long, bool> OnDelta;
            public Action<byte[], int, int> OnChunk;
            public Func<bool> ShouldAbort;
            public Func<Task> OnPauseWait;
            public bool SupportResume = true;
            public long ExistingBytes = 0;
            public long ExpectedTotal = -1;
            public string RemoteUrl;

            /// <summary>
            /// 新增：获取响应头中的 ETag / Last-Modified（仅在真正发起 GET 时回调一次每次尝试）
            /// </summary>
            public Action<string, string> OnResponseMeta;
        }

        public class HeadResult
        {
            public bool Ok;
            public long ContentLength;
            public bool AcceptRanges;
            public string ETag;          // 批次2新增
            public string LastModified;  // 批次2新增
        }

        public struct HttpDownloadResult
        {
            public bool Succeeded;
            public bool Aborted;
            public string ErrorMessage;
            public DownloadErrorCode ErrorCode;

            public static HttpDownloadResult Ok() => new HttpDownloadResult { Succeeded = true };
            public static HttpDownloadResult Abort() => new HttpDownloadResult { Aborted = true, ErrorCode = DownloadErrorCode.Canceled };
            public static HttpDownloadResult Fail(DownloadErrorCode code, string msg) =>
                new HttpDownloadResult { Succeeded = false, ErrorCode = code, ErrorMessage = msg };
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
                    return new HeadResult
                    {
                        Ok = true,
                        ContentLength = len,
                        AcceptRanges = ar,
                        ETag = resp.Headers["ETag"],
                        LastModified = resp.Headers["Last-Modified"]
                    };
                }
            }
            catch
            {
                return new HeadResult { Ok = false };
            }
        }

        public static async Task<HttpDownloadResult> DownloadFile(DownloadTask task, string url, string tempPath, DownloadOptions opt)
        {
            if (opt.ExistingBytes > 0 && opt.ExpectedTotal > 0 && opt.ExistingBytes >= opt.ExpectedTotal)
                return HttpDownloadResult.Ok();

            bool attemptResume = opt.SupportResume && opt.ExistingBytes > 0;

            for (int phase = 0; phase < (attemptResume ? 2 : 1); phase++)
            {
                bool useRange = attemptResume && phase == 0;
                long startOffset = useRange ? opt.ExistingBytes : 0;

                if (opt.ShouldAbort?.Invoke() == true)
                    return HttpDownloadResult.Abort();

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = opt.TimeoutSec * 1000;
                req.ReadWriteTimeout = opt.TimeoutSec * 1000;
                if (useRange) req.AddRange(startOffset);

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
                        continue;
                }

                bool isPartial = resp.StatusCode == HttpStatusCode.PartialContent;
                if (useRange && !isPartial)
                {
                    attemptResume = false;
                    SafeClose(resp);
                    continue;
                }

                if (!isPartial && !useRange && opt.ExistingBytes > 0)
                {
                    SafeDelete(tempPath);
                    opt.ExistingBytes = 0;
                }

                if (resp.ContentLength >= 0 && opt.ExpectedTotal <= 0)
                    opt.ExpectedTotal = useRange && isPartial ? opt.ExistingBytes + resp.ContentLength : resp.ContentLength;

                // 回调响应头元信息
                opt.OnResponseMeta?.Invoke(resp.Headers["ETag"], resp.Headers["Last-Modified"]);

                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(tempPath));
                using (var fs = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                {
                    if (useRange)
                        fs.Seek(startOffset, SeekOrigin.Begin);
                    else
                        fs.SetLength(0);

                    const int BUF = 64 * 1024;
                    byte[] buffer = new byte[BUF];
                    using (var rs = resp.GetResponseStream())
                    {
                        while (true)
                        {
                            if (opt.ShouldAbort?.Invoke() == true)
                                return HttpDownloadResult.Abort();

                            if (opt.OnPauseWait != null)
                                await opt.OnPauseWait();

                            int read;
                            try
                            {
                                read = await rs.ReadAsync(buffer, 0, buffer.Length);
                            }
                            catch (Exception ioEx)
                            {
                                SafeClose(resp);
                                return HttpDownloadResult.Fail(DownloadErrorCode.Network, ioEx.Message);
                            }
                            if (read <= 0) break;

                            fs.Write(buffer, 0, read);

                            opt.OnChunk?.Invoke(buffer, 0, read);
                            if (opt.OnDelta != null && !opt.OnDelta(read))
                                return HttpDownloadResult.Abort();
                        }
                    }
                }

                SafeClose(resp);
                return HttpDownloadResult.Ok();
            }

            return HttpDownloadResult.Fail(DownloadErrorCode.Network, "HTTP download failed after attempts");
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception e) { HotUpdateLogger.Warn("Delete temp failed: " + e.Message); }
        }

        private static void SafeClose(HttpWebResponse resp)
        {
            try { resp?.Close(); } catch { }
        }
    }
}
