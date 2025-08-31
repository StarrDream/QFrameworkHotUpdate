namespace QHotUpdateSystem.Utility
{
    public interface IJsonSerializer
    {
        string Serialize(object obj, bool pretty = false);
        T Deserialize<T>(string json);
    }
}