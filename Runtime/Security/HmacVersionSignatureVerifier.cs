using System.Text;
using System.Security.Cryptography;

namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 简单 HMAC-SHA256 校验（版本文件 sign 字段 = Hex(HMAC(secret, jsonWithoutSignField))）
    /// 注意：编辑器构建时需用相同 secret 生成
    /// </summary>
    public class HmacVersionSignatureVerifier : IVersionSignatureVerifier
    {
        private readonly string _secret;
        public HmacVersionSignatureVerifier(string secret)
        {
            _secret = secret;
        }

        public bool Verify(string json, string sign)
        {
            if (string.IsNullOrEmpty(_secret)) return false;
            if (string.IsNullOrEmpty(sign)) return false;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(json));
                var hex = HashUtility.ToHex(hash);
                return string.Equals(hex, sign, System.StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}