namespace API.Models;

/// <summary>
/// 查詢結果裝飾規則。
/// </summary>
public class ResultDecorationRule
{
    /// <summary>
    /// 來源資料表。
    /// </summary>
    public required string SourceTable { get; set; }

    /// <summary>
    /// 來源欄位。
    /// </summary>
    public required string SourceField { get; set; }

    /// <summary>
    /// 比對資料表。
    /// </summary>
    public required string LookupTable { get; set; }

    /// <summary>
    /// 比對欄位。
    /// </summary>
    public required string LookupField { get; set; }

    /// <summary>
    /// 顯示欄位。
    /// </summary>
    public required string DisplayField { get; set; }

    /// <summary>
    /// 查詢條件（可選）。
    /// </summary>
    public string? QueryCondition { get; set; }
}
