using System;

namespace QHotUpdateSystem.Core
{
    /// <summary>
    /// 模块当前状态（运行期对外可查询）
    /// </summary>
    public enum ModuleStatus
    {
        NotInstalled = 0,
        Installed = 1,
        Updating = 2,
        Updated = 3,
        Failed = 4,
        Partial = 5
    }
}