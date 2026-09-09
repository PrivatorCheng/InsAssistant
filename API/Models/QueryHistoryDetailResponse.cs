using System.Text.Json;

namespace API.Models;

/// <summary>
/// 查詢歷程明細。
/// </summary>
public sealed class QueryHistoryDetailResponse
{
    /// <summary>
    /// 查詢摘要。
    /// </summary>
    public required string QuerySummary { get; set; }

    /// <summary>
    /// 查詢歷史清單。
    /// </summary>
    public required List<QueryHistoryEntryResponse> Histories { get; set; }
}

/// <summary>
/// 單筆查詢歷史。
/// </summary>
public sealed class QueryHistoryEntryResponse
{
    /// <summary>
    /// 查詢資料。
    /// </summary>
    public required string QueryData { get; set; }

    /// <summary>
    /// 查詢結果。
    /// </summary>
    public required JsonElement QueryResult { get; set; }
}
