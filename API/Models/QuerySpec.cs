namespace API.Models;

using System.Text.Json.Serialization;

/// <summary>
/// 公式任務中的結構化查詢藍圖（task-level querySpec）。
/// </summary>
public class QuerySpec
{
    [JsonPropertyName("query_type")]
    public string? QueryType { get; set; }

    [JsonPropertyName("query_desc")]
    public string? QueryDesc { get; set; }

    public required List<string> TargetTables { get; set; }

    public required List<JoinDetail> Joins { get; set; }

    [JsonPropertyName("dataFields")]
    public required List<DimensionDetail> DataFields { get; set; }

    public required List<MetricDetail> Metrics { get; set; }

    public required List<FilterDetail> Filters { get; set; }

    public required List<SortDetail> Sort { get; set; }

    public int? Limit { get; set; }
}

/// <summary>
/// 公式查詢規格。
/// </summary>
public class FormulaSpec
{
    [JsonPropertyName("formula_name")]
    public string? FormulaName { get; set; }

    [JsonPropertyName("tasks")]
    public List<FormulaTaskSpec> Tasks { get; set; } = new();

    [JsonPropertyName("formula")]
    public FormulaExpressionSpec? Formula { get; set; }
}

/// <summary>
/// 公式任務規格。
/// </summary>
public class FormulaTaskSpec
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("querySentence")]
    public string? QuerySentence { get; set; }

    [JsonPropertyName("querySpec")]
    public QuerySpec? QuerySpec { get; set; }
}

/// <summary>
/// 公式計算規格。
/// </summary>
public class FormulaExpressionSpec
{
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("left")]
    public string? Left { get; set; }

    [JsonPropertyName("right")]
    public string? Right { get; set; }

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("formula_name")]
    public string? FormulaName { get; set; }
}

/// <summary>
/// 維度欄位。
/// </summary>
public class DimensionDetail
{
    public required string Table { get; set; }

    public required string Field { get; set; }

    public string? Alias { get; set; }
}

/// <summary>
/// Join 定義。
/// </summary>
public class JoinDetail
{
    public required string LeftTable { get; set; }

    public required string LeftField { get; set; }

    public required string RightTable { get; set; }

    public required string RightField { get; set; }

    public string? JoinType { get; set; }
}

/// <summary>
/// 指標欄位。
/// </summary>
public class MetricDetail
{
    public required string Table { get; set; }

    public required string Field { get; set; }

    public required string Aggregation { get; set; }

    public required string Alias { get; set; }
}

/// <summary>
/// 過濾條件。
/// </summary>
public class FilterDetail
{
    public required string Table { get; set; }

    public required string Field { get; set; }

    public required string Operator { get; set; }

    public required List<string> Value { get; set; }
}

/// <summary>
/// 排序條件。
/// </summary>
public class SortDetail
{
    public required string Table { get; set; }

    public required string Field { get; set; }

    public required string Direction { get; set; }
}
