namespace API.Contracts;

/// <summary>
/// 個資偵測服務介面。
/// </summary>
public interface IPiiDetectionService
{
    /// <summary>
    /// 偵測文字中命中的個資類型。
    /// </summary>
    IReadOnlyCollection<string> DetectPersonalDataEntities(string text);
}