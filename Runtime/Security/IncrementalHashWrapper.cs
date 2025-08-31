using System.Security.Cryptography;
using System.Text;

namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 简易增量哈希封装（批次 3 新增）
    /// 用途：
    /// - 边下载写入临时文件，边累积哈希；
    /// - 完成后直接获得十六进制串，避免再次整文件读取。
    /// 说明：
    /// - 仅用于非压缩 & 非断点续传任务；
    /// - 采用 HashAlgorithm.Create(algo)；若失败则不使用增量模式。
    /// </summary>
    internal sealed class IncrementalHashWrapper
    {
        private readonly HashAlgorithm _algo;
        private bool _finalized;

        private IncrementalHashWrapper(HashAlgorithm algo)
        {
            _algo = algo;
        }

        public static IncrementalHashWrapper Create(string algoName)
        {
            try
            {
                var a = HashAlgorithm.Create(algoName);
                if (a == null) return null;
                return new IncrementalHashWrapper(a);
            }
            catch
            {
                return null;
            }
        }

        public void Append(byte[] buffer, int offset, int count)
        {
            if (_finalized || _algo == null || buffer == null || count <= 0) return;
            _algo.TransformBlock(buffer, offset, count, null, 0);
        }

        public string FinalHex()
        {
            if (_finalized) return null;
            _algo.TransformFinalBlock(System.Array.Empty<byte>(), 0, 0);
            _finalized = true;
            var bytes = _algo.Hash;
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}