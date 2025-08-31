using System.Security.Cryptography;
using System.Text;

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
            HashAlgorithm h = algo.ToLower() == "sha1" ? SHA1.Create() : MD5.Create();
            var hash = h.ComputeHash(data);
            return ToHex(hash);
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