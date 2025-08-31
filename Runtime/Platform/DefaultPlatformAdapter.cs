using System.IO;
using UnityEngine;

namespace QHotUpdateSystem.Platform
{
    public class DefaultPlatformAdapter : IPlatformAdapter
    {
        public string GetPlatformName() => PlatformName.Current;

        public string GetRemoteVersionFileUrl(string baseUrl)
        {
            baseUrl = Normalize(baseUrl);
            return $"{baseUrl}Versions/version_{GetPlatformName().ToLower()}.json";
        }

        public string GetRemoteAssetFileUrl(string baseUrl, string fileName)
        {
            baseUrl = Normalize(baseUrl);
            return $"{baseUrl}AssetBundles/{GetPlatformName()}/{fileName}";
        }

        public string GetPersistentRoot()
        {
            return Path.Combine(Application.persistentDataPath, "HotUpdate");
        }

        public string GetLocalVersionFilePath()
        {
            return Path.Combine(GetPersistentRoot(), $"version_{GetPlatformName().ToLower()}.json");
        }

        public string GetLocalAssetDir()
        {
            return Path.Combine(GetPersistentRoot(), "AssetBundles", GetPlatformName());
        }

        public string GetTempDir()
        {
            return Path.Combine(GetPersistentRoot(), "temp", GetPlatformName());
        }

        private string Normalize(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return "";
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            return baseUrl;
        }
    }
}