using System.Collections.Generic;

namespace QHotUpdateSystem.Download
{
    /// <summary>
    /// 速率统计：维护最近 windowSeconds 内的字节采样
    /// </summary>
    public class SpeedMeter
    {
        private readonly float _windowSeconds;
        private readonly Queue<(long bytes, float time)> _samples = new Queue<(long, float)>();
        private long _sum;
        private float _time;

        public SpeedMeter(float windowSeconds = 3f)
        {
            _windowSeconds = windowSeconds;
        }

        public void AddSample(long bytes)
        {
            if (bytes <= 0) return;
            _time += UnityEngine.Time.unscaledDeltaTime;
            _samples.Enqueue((bytes, _time));
            _sum += bytes;
            Cleanup();
        }

        void Cleanup()
        {
            while (_samples.Count > 0)
            {
                var head = _samples.Peek();
                if (_time - head.time > _windowSeconds)
                {
                    _samples.Dequeue();
                    _sum -= head.bytes;
                }
                else break;
            }
        }

        public float GetSpeed()
        {
            Cleanup();
            return _windowSeconds > 0 ? _sum / _windowSeconds : 0f;
        }
    }
}