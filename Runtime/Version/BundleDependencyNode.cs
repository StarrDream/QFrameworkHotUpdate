using System;

namespace QHotUpdateSystem.Version
{
    /// <summary>
    /// 单个 Bundle 的直接依赖节点
    /// name 与 VersionInfo.modules[].files[].name 对应（最终输出文件名）
    /// deps 为其直接依赖的其它 bundle（同样为最终名）。运行期会做闭包计算。
    /// </summary>
    [Serializable]
    public class BundleDependencyNode
    {
        public string name;
        public string[] deps;
    }
}