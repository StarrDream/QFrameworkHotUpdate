using System;
using System.Collections.Generic;
using QHotUpdateSystem.Version;

namespace QHotUpdateSystem.Dependency
{
    /// <summary>
    /// 运行期 Bundle 依赖闭包计算器（只做简单 DFS，不做复杂拓扑缓存）
    /// </summary>
    public class BundleDependencyResolver
    {
        private readonly Dictionary<string, string[]> _deps = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        public BundleDependencyResolver(BundleDependencyNode[] nodes)
        {
            if (nodes == null) return;
            foreach (var n in nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.name)) continue;
                _deps[n.name] = n.deps ?? Array.Empty<string>();
            }
        }

        public bool Has(string bundleName) => _deps.ContainsKey(bundleName);

        /// <summary>
        /// 计算依赖闭包（包含 roots 自身）
        /// </summary>
        public HashSet<string> GetClosure(IEnumerable<string> roots)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            if (roots != null)
            {
                foreach (var r in roots)
                {
                    if (string.IsNullOrEmpty(r)) continue;
                    if (result.Add(r)) stack.Push(r);
                }
            }

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!_deps.TryGetValue(cur, out var children) || children == null) continue;
                foreach (var c in children)
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    if (result.Add(c)) stack.Push(c);
                }
            }

            return result;
        }
    }
}