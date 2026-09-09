using API.Models;

namespace API.Contracts;

/// <summary>
/// Google Embedding 向量查詢服務介面。
/// </summary>
public interface IGoogleEmbeddingVectorQueryService
{
    /// <summary>
    /// 批次生成文字向量。
    /// </summary>
    /// <param name="texts">待向量化文字集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>對應輸入順序的向量集合。</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 將查詢文字與候選內容做相似度比對，回傳前 N 名結果。
    /// </summary>
    /// <param name="query">查詢文字。</param>
    /// <param name="candidates">候選內容集合。</param>
    /// <param name="topK">回傳前 N 筆，最小值為 1。</param>
    /// <param name="sessionId">可選的工作階段識別碼，未提供時自動產生。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>向量查詢結果。</returns>
    Task<GoogleEmbeddingVectorQueryResult> QueryAsync(
        string query,
        IReadOnlyList<GoogleEmbeddingVectorCandidate> candidates,
        int topK = 5,
        string? sessionId = null,
        CancellationToken cancellationToken = default);
}