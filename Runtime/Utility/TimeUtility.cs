using System;

namespace QHotUpdateSystem.Utility
{
    public static class TimeUtility
    {
        public static long UnixTimeSeconds()
        {
            var span = DateTime.UtcNow - new DateTime(1970, 1, 1);
            return (long)span.TotalSeconds;
        }
    }
}