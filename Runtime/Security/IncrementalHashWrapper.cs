using System;
using System.Security.Cryptography;

namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 增量哈希封装 (批次3: 支持 sha256)
    /// 对未压缩且非断点续传的文件使用，用于减少二次读取。
    /// </summary>
    public class IncrementalHashWrapper : IDisposable
    {
        private readonly HashAlgorithm _algo;
        private bool _finalized;
        private byte[] _buffer;
        private int _bufLen;

        private IncrementalHashWrapper(HashAlgorithm algo)
        {
            _algo = algo;
            _buffer = new byte[0];
        }

        public static IncrementalHashWrapper Create(string algo)
        {
            algo = (algo ?? "md5").ToLowerInvariant();
            HashAlgorithm h = algo switch
            {
                "md5" => MD5.Create(),
                "sha1" => SHA1.Create(),
                "sha256" => SHA256.Create(),
                _ => MD5.Create()
            };
            return new IncrementalHashWrapper(h);
        }

        public void Append(byte[] data, int offset, int count)
        {
            if (_finalized) throw new InvalidOperationException("Already finalized");
            if (count <= 0) return;
            // 直接 TransformBlock，不缓存全部数据
            _algo.TransformBlock(data, offset, count, null, 0);
        }

        public string FinalHex()
        {
            if (_finalized) throw new InvalidOperationException("Already finalized");
            _algo.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            _finalized = true;
            return HashUtility.ComputeBytes(_algo.Hash, "md5") switch
            {
                _ => BytesToHex(_algo.Hash) // 直接用十六进制输出算法真实结果
            };
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
            _buffer = null;
        }
    }
}
