using System;
using System.Security.Cryptography;

namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 增量哈希封装
    /// 仅用于：未压缩 + 不需要断点续传（即一次性从 0 开始下载的场景），在下载过程中直接 Append 数据块。
    /// </summary>
    public class IncrementalHashWrapper : IDisposable
    {
        private readonly HashAlgorithm _algo;
        private bool _finalized;

        private IncrementalHashWrapper(HashAlgorithm algo)
        {
            _algo = algo;
        }

        public static IncrementalHashWrapper Create(string algo)
        {
            algo = (algo ?? "md5").ToLowerInvariant();
            HashAlgorithm h = algo switch
            {
                "sha1" => SHA1.Create(),
                "sha256" => SHA256.Create(),
                _ => MD5.Create()
            };
            return new IncrementalHashWrapper(h);
        }

        /// <summary>
        /// 追加一段数据（下载流中的一块）
        /// </summary>
        public void Append(byte[] data, int offset, int count)
        {
            if (_finalized) throw new InvalidOperationException("Hash already finalized");
            if (count <= 0) return;
            _algo.TransformBlock(data, offset, count, null, 0);
        }

        /// <summary>
        /// 结束并返回十六进制哈希
        /// </summary>
        public string FinalHex()
        {
            if (_finalized) throw new InvalidOperationException("Hash already finalized");
            _algo.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            _finalized = true;
            return BytesToHex(_algo.Hash);
        }

        private string BytesToHex(byte[] bytes)
        {
            char[] c = new char[bytes.Length * 2];
            int p = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                c[p++] = GetHex((b >> 4) & 0xF);
                c[p++] = GetHex(b & 0xF);
            }
            return new string(c);
        }
        private char GetHex(int v) => (char)(v < 10 ? ('0' + v) : ('a' + (v - 10)));

        public void Dispose()
        {
            _algo?.Dispose();
        }
    }
}
