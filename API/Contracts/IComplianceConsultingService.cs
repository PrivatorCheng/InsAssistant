using API.Models;

namespace API.Contracts;

/// <summary>
/// 法遵向量檢索服務介面。
/// </summary>
public interface IComplianceConsultingService
{
    /// <summary>
    /// 依查詢內容檢索最相符法規資料。
    /// </summary>
    Task<IReadOnlyList<ComplianceLawReferenceSearchResult>> SearchLawReferencesAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依查詢向量檢索最相符法規資料。
    /// </summary>
    Task<IReadOnlyList<ComplianceLawReferenceSearchResult>> SearchLawReferencesAsync(
        ReadOnlyMemory<float> queryVector,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依查詢內容檢索最相符商品條款資料。
    /// </summary>
    Task<IReadOnlyList<ComplianceProductClauseSearchResult>> SearchProductClausesAsync(
        string query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依查詢向量檢索最相符商品條款資料。
    /// </summary>
    Task<IReadOnlyList<ComplianceProductClauseSearchResult>> SearchProductClausesAsync(
        ReadOnlyMemory<float> queryVector,
        CancellationToken cancellationToken = default);
}
