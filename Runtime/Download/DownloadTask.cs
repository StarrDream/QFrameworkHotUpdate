// ========================================
// 文件名: DownloadTask.cs
// 路径: Plugins/QHotUpdateSystem/Runtime/Download/DownloadTask.cs
// 修改说明 (批次1):
// 1. 移除本文件内重复的 DownloadTaskState 枚举定义，统一使用 Core 中的枚举
// 2. 补充字段与重试计数字段注释
// 3. 保持现有逻辑（包括 RetryCount 语义：0 表示第一次尝试；MaxRetry 表示允许的“重试次数” -> 总尝试 = MaxRetry + 1）
// ========================================

using System;
using QHotUpdateSystem.Core; // 引入统一的 DownloadTaskState 枚举
using QHotUpdateSystem.Version;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 单文件下载任务
    /// 说明：
    /// - RetryCount 含义：已进行的尝试次数计数（从 0 开始）。当 RetryCount == 0 表示“第一次尝试”。
    ///   若 Options.MaxRetry = N，则最多会进行 N+1 次实际下载尝试（0..N）。
    /// </summary>
    public class DownloadTask
    {
        public string Module; // 所属模块名
        public FileEntry File; // 对应的版本文件描述
        public string RemoteUrl; // 远端下载地址
        public string TempPath; // 临时文件路径 (.part)
        public string FinalPath; // 最终落地路径

        public long TotalBytes; // 目标总字节（可能是压缩文件大小或原文件大小，取决于是否压缩）
        public long DownloadedBytes; // 当前会话新增写入（不含 ExistingBytes）
        public long ExistingBytes; // 断点续传已存在的字节
        public long RemoteSize; // 服务器端声明大小（可选赋值，用于校验）

        public int RetryCount; // 已进行的尝试次数（初始 0）。逻辑请参见上方说明。
        public DownloadPriority Priority;
        public DownloadTaskState State;

        public bool SupportResume = true; // 是否支持断点续传
        public string ResumeMetaPath; // 续传元数据文件路径

        public bool IsCompressed => File.compressed;
        public bool IsFinished => State == DownloadTaskState.Completed;

        public string LastError;

        public Action<DownloadTask> OnCompleted;
    }
}