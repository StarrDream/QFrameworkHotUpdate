using System.Collections.Generic;
using QHotUpdateSystem.Version;
using QHotUpdateSystem.Platform;

namespace QHotUpdateSystem.Core
{
    /// <summary>
    /// 运行期上下文：集中存放初始化后共享对象
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

        public HotUpdateContext(HotUpdateInitOptions options)
        {
            Options = options;
            PlatformAdapter = options.PlatformAdapter ?? new Platform.DefaultPlatformAdapter();
            JsonSerializer = options.JsonSerializer ?? new Utility.UnityJsonSerializer();
        }
    }
}