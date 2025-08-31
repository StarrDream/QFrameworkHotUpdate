using System.Collections.Generic;

namespace QHotUpdateSystem.Compression
{
    /// <summary>
    /// 压缩器注册表
    /// </summary>
    public static class CompressorRegistry
    {
        private static readonly Dictionary<string, ICompressor> _map = new Dictionary<string, ICompressor>();

        static CompressorRegistry()
        {
            Register(new ZipCompressor());
            Register(new GZipCompressor());
            Register(new LZ4Compressor()); // 占位
        }

        public static void Register(ICompressor compressor)
        {
            if (compressor == null) return;
            _map[compressor.Algo.ToLower()] = compressor;
        }

        public static ICompressor Get(string algo)
        {
            if (string.IsNullOrEmpty(algo)) return null;
            _map.TryGetValue(algo.ToLower(), out var c);
            return c;
        }
    }
}