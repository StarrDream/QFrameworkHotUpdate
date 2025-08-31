#if UNITY_EDITOR
using System.Text;
using System.Security.Cryptography;

namespace QHotUpdateSystem.Editor.Security
{
    /// <summary>
    /// 与运行期 HmacVersionSignatureVerifier 对应的构建期签名器
    /// </summary>
    public static class HmacVersionSigner
    {
        public static string Sign(string json, string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "";
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
#endif