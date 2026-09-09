using System.Text.Json.Serialization;
using Microsoft.Extensions.VectorData;

namespace API.Models.VectorDb;

/// <summary>
/// 商品條款向量資料記錄。
/// </summary>
public sealed class InsuranceProductClauseVectorRecord
{
    /// <summary>
    /// 向量資料主鍵。
    /// </summary>
    [VectorStoreKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 條款語意向量。
    /// </summary>
    [VectorStoreVector(3072)]
    public required ReadOnlyMemory<float> Vector { get; set; }

    /// <summary>
    /// 串聯後的條款文字。
    /// </summary>
    [VectorStoreData]
    [JsonPropertyName("content")]
    public required string Content { get; set; }

    /// <summary>
    /// 商品名稱。
    /// </summary>
    [VectorStoreData]
    [JsonPropertyName("product_name")]
    public required string ProductName { get; set; }

    /// <summary>
    /// 項目類型。
    /// </summary>
    [VectorStoreData]
    [JsonPropertyName("item_type")]
    public required string ItemType { get; set; }

    /// <summary>
    /// 行銷標籤。
    /// </summary>
    [VectorStoreData]
    [JsonPropertyName("marketing_tags")]
    public List<string> MarketingTags { get; set; } = [];
}
