using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetBundleHotUpdate
{
    /// <summary>
    ///     AssetBundle依赖管理器
    ///     功能：处理AB包之间的依赖关系，计算下载顺序，检测循环依赖
    /// </summary>
    public class AssetBundleDependencyManager
    {
        private Dictionary<string, AssetBundleInfo> bundleDict;
        private Dictionary<string, List<string>> dependencyGraph;

        /// <summary>
        ///     构建依赖图
        /// </summary>
        /// <param name="bundles">AB包列表</param>
        public void BuildDependencyGraph(List<AssetBundleInfo> bundles)
        {
            bundleDict = bundles.ToDictionary(b => b.bundleName, b => b);
            dependencyGraph = new Dictionary<string, List<string>>();

            // 构建依赖图
            foreach (var bundle in bundles) dependencyGraph[bundle.bundleName] = new List<string>(bundle.dependencies);

            // 计算所有依赖和依赖层级
            foreach (var bundle in bundles)
            {
                bundle.allDependencies = GetAllDependencies(bundle.bundleName);
                bundle.dependencyLevel = CalculateDependencyLevel(bundle.bundleName);
            }

            Debug.Log($"[DependencyManager] 依赖图构建完成，包含 {bundles.Count} 个AB包");
        }

        /// <summary>
        ///     获取所有依赖（包括间接依赖）
        /// </summary>
        private List<string> GetAllDependencies(string bundleName)
        {
            var visited = new HashSet<string>();
            var result = new List<string>();
            GetDependenciesRecursive(bundleName, visited, result);
            return result;
        }

        /// <summary>
        ///     递归获取依赖
        /// </summary>
        private void GetDependenciesRecursive(string bundleName, HashSet<string> visited, List<string> result)
        {
            if (visited.Contains(bundleName) || !dependencyGraph.ContainsKey(bundleName))
                return;

            visited.Add(bundleName);

            foreach (var dependency in dependencyGraph[bundleName])
            {
                if (!result.Contains(dependency)) result.Add(dependency);
                GetDependenciesRecursive(dependency, visited, result);
            }
        }

        /// <summary>
        ///     计算依赖层级
        /// </summary>
        private int CalculateDependencyLevel(string bundleName)
        {
            if (!dependencyGraph.ContainsKey(bundleName) || dependencyGraph[bundleName].Count == 0)
                return 0;

            var maxLevel = 0;
            foreach (var dependency in dependencyGraph[bundleName]) maxLevel = Mathf.Max(maxLevel, CalculateDependencyLevel(dependency));
            return maxLevel + 1;
        }

        /// <summary>
        ///     获取下载顺序（按依赖层级排序）
        /// </summary>
        /// <param name="bundleNames">要下载的AB包名称列表</param>
        /// <returns>正确的下载顺序</returns>
        public List<string> GetDownloadOrder(List<string> bundleNames)
        {
            // 收集所有需要下载的包（包括依赖）
            var allBundles = new HashSet<string>();
            foreach (var bundleName in bundleNames)
            {
                allBundles.Add(bundleName);
                if (bundleDict.ContainsKey(bundleName))
                    foreach (var dependency in bundleDict[bundleName].allDependencies)
                        allBundles.Add(dependency);
            }

            // 按依赖层级排序（依赖层级低的先下载）
            var orderedBundles = allBundles
                .Where(name => bundleDict.ContainsKey(name))
                .OrderBy(name => bundleDict[name].dependencyLevel)
                .ThenBy(name => bundleDict[name].priority)
                .ToList();

            Debug.Log($"[DependencyManager] 计算下载顺序: {string.Join(" -> ", orderedBundles)}");
            return orderedBundles;
        }

        /// <summary>
        ///     检查循环依赖
        /// </summary>
        /// <returns>存在循环依赖的AB包列表</returns>
        public List<string> DetectCircularDependencies()
        {
            var circularDeps = new List<string>();
            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();

            foreach (var bundle in dependencyGraph.Keys)
                if (HasCircularDependency(bundle, visiting, visited))
                    circularDeps.Add(bundle);

            if (circularDeps.Count > 0) Debug.LogError($"[DependencyManager] 检测到循环依赖: {string.Join(", ", circularDeps)}");

            return circularDeps;
        }

        /// <summary>
        ///     检查是否存在循环依赖
        /// </summary>
        private bool HasCircularDependency(string bundleName, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visiting.Contains(bundleName))
                return true;

            if (visited.Contains(bundleName) || !dependencyGraph.ContainsKey(bundleName))
                return false;

            visiting.Add(bundleName);

            foreach (var dependency in dependencyGraph[bundleName])
                if (HasCircularDependency(dependency, visiting, visited))
                    return true;

            visiting.Remove(bundleName);
            visited.Add(bundleName);
            return false;
        }
    }
}