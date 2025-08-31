namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 下载失败/结束的分类错误码（基础版本，可后续扩展）。
    /// None: 成功或未出错
    /// Network: 网络层失败（超时/连接失败）
    /// Canceled: 用户取消
    /// IntegrityMismatch: 哈希校验不一致
    /// DecompressFail: 解压失败
    /// IO: 文件系统读写错误
    /// UnsafePath: 路径/名称安全检查失败
    /// Unknown: 未识别的异常
    /// </summary>
    public enum DownloadErrorCode
    {
        None = 0,
        Network,
        Canceled,
        IntegrityMismatch,
        DecompressFail,
        IO,
        UnsafePath,
        Unknown
    }
}