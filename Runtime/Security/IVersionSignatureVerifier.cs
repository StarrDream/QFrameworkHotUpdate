namespace QHotUpdateSystem.Security
{
    /// <summary>
    /// 版本签名校验接口（运行期）
    /// </summary>
    public interface IVersionSignatureVerifier
    {
        bool Verify(string serializedVersionJson, string sign);
    }
}