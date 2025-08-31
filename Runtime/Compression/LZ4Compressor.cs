using System;
using System.IO;
using K4os.Compression.LZ4.Streams;

namespace QHotUpdateSystem.Compression
{
    /// <summary>
    /// LZ4 压缩解压器，使用 k4os.compression.lz4
    /// </summary>
    public class LZ4Compressor : ICompressor
    {
        public string Algo => "lz4";

        public bool Decompress(string srcFile, string dstFile, out string error)
        {
            error = null;
            try
            {
                using (var srcStream = new FileStream(srcFile, FileMode.Open, FileAccess.Read))
                using (var lz4Stream = LZ4Stream.Decode(srcStream))
                using (var dstStream = new FileStream(dstFile, FileMode.Create, FileAccess.Write))
                {
                    lz4Stream.CopyTo(dstStream);
                }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}