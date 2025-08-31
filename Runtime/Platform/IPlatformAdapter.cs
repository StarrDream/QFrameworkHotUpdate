namespace QHotUpdateSystem.Platform
{
    public interface IPlatformAdapter
    {
        string GetPlatformName();
        string GetRemoteVersionFileUrl(string baseUrl);
        string GetRemoteAssetFileUrl(string baseUrl, string fileName);
        string GetPersistentRoot();
        string GetLocalVersionFilePath();
        string GetLocalAssetDir();
        string GetTempDir();
    }
}