namespace QHotUpdateSystem.Version
{
    public static class VersionWriter
    {
        public static void UpsertModule(VersionInfo local, ModuleInfo remoteModule)
        {
            if (local.modules == null || local.modules.Length == 0)
            {
                local.modules = new[] { remoteModule };
                return;
            }
            for (int i = 0; i < local.modules.Length; i++)
            {
                if (local.modules[i].name == remoteModule.name)
                {
                    local.modules[i] = remoteModule;
                    return;
                }
            }
            var list = new System.Collections.Generic.List<ModuleInfo>(local.modules) { remoteModule };
            local.modules = list.ToArray();
        }
    }
}