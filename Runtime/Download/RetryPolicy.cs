using System;
using System.Threading.Tasks;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 简单重试策略：指数退避（基础 1s，上限 8s）
    /// </summary>
    public static class RetryPolicy
    {
        public static async Task DelayForRetry(int retryIndex)
        {
            int seconds = (int)Math.Min(1 << retryIndex, 8);
            await Task.Delay(seconds * 1000);
        }
    }
}