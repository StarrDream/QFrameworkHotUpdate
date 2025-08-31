namespace QHotUpdateSystem.Compression
{
    /// <summary>
    /// 压缩解压接口（当前只需解压）
    /// </summary>
    public interface ICompressor
    {
        string Algo { get; }
        bool Decompress(string srcFile, string dstFile, out string error);
    }
}