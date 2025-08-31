using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 哈希计算工具（统一精简版）
    /// </summary>
    public static class HashUtility
    {
        /// <summary>对字符串（UTF8）计算哈希</summary>
        public static string Compute(string text, string algo)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return Compute(Encoding.UTF8.GetBytes(text), algo);
        }

        /// <summary>对字节数组计算哈希</summary>
        public static string Compute(byte[] data, string algo)
        {
            if (data == null) return string.Empty;
            using (var h = SelectAlgo(algo))
            {
                return ToHex(h.ComputeHash(data));
            }
        }

        /// <summary>
        /// 对流进行分块计算哈希（不会关闭传入流）。
        /// 默认 64KB buffer；可根据大文件需求调节。
        /// </summary>
        public static string ComputeStream(Stream stream, string algo, int bufferSize = 64 * 1024)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (bufferSize <= 0) bufferSize = 64 * 1024;

            using (var h = SelectAlgo(algo))
            {
                var buffer = new byte[bufferSize];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    h.TransformBlock(buffer, 0, read, null, 0);
                }
                h.TransformFinalBlock(buffer, 0, 0);
                return ToHex(h.Hash);
            }
        }

        /// <summary>统一算法选择（默认 md5）</summary>
        private static HashAlgorithm SelectAlgo(string algo)
        {
            switch ((algo ?? "md5").ToLowerInvariant())
            {
                case "sha1": return SHA1.Create();
                case "sha256": return SHA256.Create();
                case "md5":
                default: return MD5.Create();
            }
        }

        /// <summary>字节数组转十六进制（小写）</summary>
        public static string ToHex(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
