namespace API.Models;

/// <summary>
/// 法規向量檢索結果。
/// </summary>
public sealed class ComplianceLawReferenceSearchResult
{
    /// <summary>
    /// 來源編號。
    /// </summary>
    public required string SourceNo { get; set; }

    /// <summary>
    /// 原始資料 Guid。
    /// </summary>
    public required string Guid { get; set; }

    /// <summary>
    /// 法規名稱。
    /// </summary>
    public required string LawName { get; set; }

    /// <summary>
    /// 條號。
    /// </summary>
    public required string ArticleNumber { get; set; }

    /// <summary>
    /// 條文內容。
    /// </summary>
    public required string ArticleText { get; set; }
}
