using System;
using System.IO;
using System.IO.Compression;

namespace QHotUpdateSystem.Compression
{
    /// <summary>
    /// GZip 单文件解压器
    /// </summary>
    public class GZipCompressor : ICompressor
    {
        public string Algo => "gzip";

        public bool Decompress(string srcFile, string dstFile, out string error)
        {
            error = null;
            try
            {
                using (var fs = new FileStream(srcFile, FileMode.Open, FileAccess.Read))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var ofs = new FileStream(dstFile, FileMode.Create, FileAccess.Write))
                {
                    gz.CopyTo(ofs);
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