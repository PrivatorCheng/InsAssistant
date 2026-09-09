namespace API.Models;

/// <summary>
/// 商品條款向量檢索結果。
/// </summary>
public sealed class ComplianceProductClauseSearchResult
{
    /// <summary>
    /// 商品名稱。
    /// </summary>
    public required string ProductName { get; set; }

    /// <summary>
    /// 條款內容。
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// 條款類型。
    /// </summary>
    public required string ItemType { get; set; }

    /// <summary>
    /// 行銷標籤。
    /// </summary>
    public List<string> MarketingTags { get; set; } = [];
}
