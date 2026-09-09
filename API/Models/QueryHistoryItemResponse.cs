namespace API.Models;

/// <summary>
/// 查詢歷程摘要項目。
/// </summary>
public sealed class QueryHistoryItemResponse
{
    /// <summary>
    /// 歷程檔名。
    /// </summary>
    public required string FileName { get; set; }

    /// <summary>
    /// 查詢描述（query_desc）。
    /// </summary>
    public required string QueryDesc { get; set; }
}
