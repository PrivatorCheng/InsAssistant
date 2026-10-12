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

    /// <summary>
    /// 將文字中的個資內容遮罩為不可識別字串。
    /// </summary>
    string MaskPersonalData(string text);
}