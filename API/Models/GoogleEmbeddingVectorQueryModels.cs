namespace API.Models;

/// <summary>
/// 向量查詢候選內容。
/// </summary>
public sealed class GoogleEmbeddingVectorCandidate
{
    /// <summary>
    /// 候選資料識別碼。
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// 候選資料文字內容。
    /// </summary>
    public required string Text { get; set; }
}

/// <summary>
/// 單筆向量相似度比對結果。
/// </summary>
public sealed class GoogleEmbeddingVectorMatch
{
    /// <summary>
    /// 候選資料識別碼。
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// 候選資料文字內容。
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// 餘弦相似度分數，範圍為 -1 到 1。
    /// </summary>
    public required double Score { get; set; }
}

/// <summary>
/// Google Embedding 向量查詢結果。
/// </summary>
public sealed class GoogleEmbeddingVectorQueryResult
{
    /// <summary>
    /// 工作階段識別碼。
    /// </summary>
    public required string SessionId { get; set; }

    /// <summary>
    /// 使用的向量模型名稱。
    /// </summary>
    public required string EmbeddingModel { get; set; }

    /// <summary>
    /// 查詢文字。
    /// </summary>
    public required string Query { get; set; }

    /// <summary>
    /// 依相似度排序後的結果。
    /// </summary>
    public required List<GoogleEmbeddingVectorMatch> Matches { get; set; }
}