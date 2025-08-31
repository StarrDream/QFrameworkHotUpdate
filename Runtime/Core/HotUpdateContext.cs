using System.Collections.Generic;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Platform;
using QHotUpdateSystem.Dependency;

namespace QHotUpdateSystem.Core
{
    /// <summary>
    /// 运行期上下文（新增 Bundle 依赖解析相关字段）
    /// </summary>
    public class HotUpdateContext
    {
        public readonly HotUpdateInitOptions Options;
        public readonly IPlatformAdapter PlatformAdapter;
        public VersionInfo LocalVersion;
        public VersionInfo RemoteVersion;

        public readonly Dictionary<string, ModuleRuntimeState> ModuleStates =
            new Dictionary<string, ModuleRuntimeState>();

        public readonly Utility.IJsonSerializer JsonSerializer;

        // ★ 新增：bundle -> module 映射
        public readonly Dictionary<string, string> BundleToModule =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // ★ 新增：依赖解析器（可能为 null）
        public BundleDependencyResolver BundleResolver;

        public HotUpdateContext(HotUpdateInitOptions options)
        {
            Options = options;
            PlatformAdapter = options.PlatformAdapter ?? new Platform.DefaultPlatformAdapter();
            JsonSerializer = options.JsonSerializer ?? new Utility.UnityJsonSerializer();
        }
    }
}