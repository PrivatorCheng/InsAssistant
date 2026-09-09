using Microsoft.Extensions.VectorData;

namespace API.Models.VectorDb;

/// <summary>
/// 保險法規向量資料記錄。
/// </summary>
public sealed class InsuranceLawVectorRecord
{
    /// <summary>
    /// 向量資料主鍵。
    /// </summary>
    [VectorStoreKey]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 條文語意向量。
    /// </summary>
    [VectorStoreVector(3072)]
    public required ReadOnlyMemory<float> Vector { get; set; }

    /// <summary>
    /// 來源代號。
    /// </summary>
    [VectorStoreData]
    public required string SourceNo { get; set; }

    /// <summary>
    /// 原始資料 Guid。
    /// </summary>
    [VectorStoreData]
    public required string OriginalGuid { get; set; }

    /// <summary>
    /// 法規名稱。
    /// </summary>
    [VectorStoreData]
    public required string LawName { get; set; }

    /// <summary>
    /// 條號。
    /// </summary>
    [VectorStoreData]
    public required string ArticleNumber { get; set; }

    /// <summary>
    /// 條文純文字。
    /// </summary>
    [VectorStoreData]
    public required string CleanText { get; set; }
}
