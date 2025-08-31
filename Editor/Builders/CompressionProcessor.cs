using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using K4os.Compression.LZ4.Streams;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace QHotUpdateSystem.Editor.Builders
{
    /// <summary>
    /// 压缩处理：按算法生成单文件压缩包
    /// </summary>
    public static class CompressionProcessor
    {
        public static bool Compress(string srcFile, string dstFile, string algo, out long compressedSize, out string error)
        {
            error = null;
            compressedSize = 0;
            try
            {
                switch (algo.ToLower())
                {
                    case "zip":
                        return CompressZip(srcFile, dstFile, out compressedSize, out error);
                    case "gzip":
                        return CompressGZip(srcFile, dstFile, out compressedSize, out error);
                    case "lz4":
                        return CompressLZ4(srcFile, dstFile, out compressedSize, out error);
                    default:
                        error = "未知压缩算法: " + algo;
                        return false;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static bool CompressZip(string srcFile, string dstFile, out long compressedSize, out string error)
        {
            error = null;
            compressedSize = 0;
            try
            {
                if (File.Exists(dstFile)) File.Delete(dstFile);
                using (var z = ZipFile.Open(dstFile, ZipArchiveMode.Create))
                {
                    z.CreateEntryFromFile(srcFile, Path.GetFileName(srcFile), CompressionLevel.Optimal);
                }

                compressedSize = new FileInfo(dstFile).Length;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static bool CompressGZip(string srcFile, string dstFile, out long compressedSize, out string error)
        {
            error = null;
            compressedSize = 0;
            try
            {
                if (File.Exists(dstFile)) File.Delete(dstFile);
                using (var fs = new FileStream(dstFile, FileMode.Create, FileAccess.Write))
                using (var gz = new GZipStream(fs, CompressionMode.Compress))
                using (var src = new FileStream(srcFile, FileMode.Open, FileAccess.Read))
                {
                    src.CopyTo(gz);
                }

                compressedSize = new FileInfo(dstFile).Length;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static bool CompressLZ4(string srcFile, string dstFile, out long compressedSize, out string error)
        {
            error = null;
            compressedSize = 0;
            try
            {
                if (File.Exists(dstFile)) File.Delete(dstFile);
                using (var srcStream = new FileStream(srcFile, FileMode.Open, FileAccess.Read))
                using (var dstStream = new FileStream(dstFile, FileMode.Create, FileAccess.Write))
                using (var lz4Stream = LZ4Stream.Encode(dstStream, new LZ4EncoderSettings()))
                {
                    srcStream.CopyTo(lz4Stream);
                }

                compressedSize = new FileInfo(dstFile).Length;
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