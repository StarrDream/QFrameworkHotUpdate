using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// Hash 计算工具
    /// </summary>
    public static class HashUtility
    {
        public static string Compute(string text, string algo)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            byte[] data = Encoding.UTF8.GetBytes(text);
            return Compute(data, algo);
        }

        public static string Compute(byte[] data, string algo)
        {
            if (data == null) return string.Empty;
            if (algo == null) algo = "md5";
            using (HashAlgorithm h = SelectAlgo(algo))
            {
                var hash = h.ComputeHash(data);
                return ToHex(hash);
            }
        }

        /// <summary>
        /// 流式哈希：用于大文件，避免一次性读取全部内容
        /// </summary>
        public static string ComputeStream(Stream stream, string algo, int bufferSize = 1024 * 64)
        {
            if (stream == null) return string.Empty;
            if (algo == null) algo = "md5";
            using (HashAlgorithm h = SelectAlgo(algo))
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

        static HashAlgorithm SelectAlgo(string algo)
        {
            switch (algo.ToLower())
            {
                case "sha1": return SHA1.Create();
                case "md5":
                default: return MD5.Create();
            }
        }

        public static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
