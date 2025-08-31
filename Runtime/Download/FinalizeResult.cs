namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 文件最终化（校验+落盘）结果
    /// </summary>
    public struct FinalizeResult
    {
        public bool Success;
        public DownloadErrorCode ErrorCode;
        public string ErrorMessage;
        /// <summary>
        /// 是否需要重新完整下载（例如哈希失败导致 .part 无效）
        /// </summary>
        public bool TempInvalidated;

        public static FinalizeResult Ok() => new FinalizeResult { Success = true, ErrorCode = DownloadErrorCode.None };
        public static FinalizeResult Fail(DownloadErrorCode code, string msg, bool invalidate = false)
            => new FinalizeResult { Success = false, ErrorCode = code, ErrorMessage = msg, TempInvalidated = invalidate };
    }
}