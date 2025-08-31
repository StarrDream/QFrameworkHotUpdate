using System;
using System.IO;
using System.IO.Compression;

namespace QHotUpdateSystem.Compression
{
    /// <summary>
    /// Zip 单文件压缩包解压器（假设每个 zip 内只含一个文件或直接解压到目标）
    /// 发布端请保证 zip 内文件名与目标对应
    /// </summary>
    public class ZipCompressor : ICompressor
    {
        public string Algo => "zip";

        public bool Decompress(string srcFile, string dstFile, out string error)
        {
            error = null;
            try
            {
                using (var archive = ZipFile.OpenRead(srcFile))
                {
                    if (archive.Entries.Count == 0)
                    {
                        error = "Zip empty";
                        return false;
                    }
                    var entry = archive.Entries[0];
                    using (var es = entry.Open())
                    using (var fs = new FileStream(dstFile, FileMode.Create, FileAccess.Write))
                    {
                        es.CopyTo(fs);
                    }
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