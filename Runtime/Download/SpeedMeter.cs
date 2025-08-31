using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 速率统计（线程安全版）：
    /// - 使用 Stopwatch 而非 UnityEngine.Time，避免后台线程访问主线程 API。
    /// - 记录窗口期内的字节总量 => 平均速度 = sum / windowSeconds。
    /// - 多线程调用 AddSample 安全：使用简易锁或 Interlocked（此处用锁保持实现直观）。
    /// </summary>
    public class SpeedMeter
    {
        private readonly double _windowSeconds;
        private readonly Queue<(long bytes, double timeSec)> _samples = new Queue<(long, double)>();
        private long _sum;
        private readonly object _lock = new object();
        private readonly Stopwatch _watch;

        public SpeedMeter(double windowSeconds = 3d)
        {
            _windowSeconds = windowSeconds <= 0 ? 3d : windowSeconds;
            _watch = Stopwatch.StartNew();
        }

        public void AddSample(long bytes)
        {
            if (bytes <= 0) return;
            var now = _watch.Elapsed.TotalSeconds;
            lock (_lock)
            {
                _samples.Enqueue((bytes, now));
                _sum += bytes;
                Cleanup(now);
            }
        }

        private void Cleanup(double now)
        {
            while (_samples.Count > 0)
            {
                var head = _samples.Peek();
                if (now - head.timeSec > _windowSeconds)
                {
                    _samples.Dequeue();
                    _sum -= head.bytes;
                }
                else break;
            }
        }

        public float GetSpeed()
        {
            var now = _watch.Elapsed.TotalSeconds;
            lock (_lock)
            {
                Cleanup(now);
                if (_windowSeconds <= 0) return 0f;
                return (float)(_sum / _windowSeconds);
            }
        }
    }
}