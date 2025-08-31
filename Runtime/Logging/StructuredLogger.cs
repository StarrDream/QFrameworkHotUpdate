using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace QHotUpdateSystem.Logging
{
    /// <summary>
    /// 简易结构化日志（JSON 行），避免引入第三方库。
    /// 用途：
    ///  - 诊断/失败点记录上下文；
    ///  - 不替换 HotUpdateLogger，只是补充。
    /// 注意：
    ///  - 为最小侵入，不做异步落盘；如需高性能可后续扩展。
    /// </summary>
    internal static class StructuredLogger
    {
        public enum Level { Debug, Info, Warn, Error }

        public static bool Enable = true;
        public static Level MinimumLevel = Level.Info;

        public static void Log(Level level, string msg, object ctx = null,
            [CallerMemberName] string member = null)
        {
            if (!Enable || level < MinimumLevel) return;
            try
            {
                var sb = new StringBuilder(256);
                sb.Append('{');
                AppendKV(sb, "ts", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                AppendKV(sb, "level", level.ToString());
                AppendKV(sb, "msg", msg);
                if (!string.IsNullOrEmpty(member)) AppendKV(sb, "caller", member);
                if (ctx != null)
                {
                    // 粗略序列化匿名对象或字典
                    if (ctx is IDictionary<string, object> dict)
                    {
                        foreach (var kv in dict)
                        {
                            AppendKV(sb, kv.Key, kv.Value);
                        }
                    }
                    else
                    {
                        var props = ctx.GetType().GetProperties();
                        foreach (var p in props)
                        {
                            object v = null;
                            try { v = p.GetValue(ctx); } catch { }
                            AppendKV(sb, p.Name, v);
                        }
                    }
                }
                TrimTrailingComma(sb);
                sb.Append('}');
                // 输出统一走 Unity 日志（或控制台）
                switch (level)
                {
                    case Level.Error: HotUpdateLogger.Error(sb.ToString()); break;
                    case Level.Warn: HotUpdateLogger.Warn(sb.ToString()); break;
                    default: HotUpdateLogger.Info(sb.ToString()); break;
                }
            }
            catch
            {
                // 忽略结构化日志异常
            }
        }

        private static void AppendKV(StringBuilder sb, string k, object v)
        {
            if (sb[sb.Length - 1] != '{') sb.Append(',');
            sb.Append('"').Append(Escape(k)).Append('"').Append(':');
            if (v == null) { sb.Append("null"); return; }
            if (v is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (v is int || v is long || v is float || v is double || v is decimal)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            var s = v.ToString();
            sb.Append('"').Append(Escape(s)).Append('"');
        }

        private static string Escape(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static void TrimTrailingComma(StringBuilder sb)
        {
            if (sb[sb.Length - 1] == ',')
            {
                sb.Length -= 1;
            }
        }
    }
}
