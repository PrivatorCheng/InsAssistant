namespace API.Models;

/// <summary>
/// 報表回應。
/// </summary>
public class ReportResponse
{
    public required List<string> Columns { get; set; }

    public required List<Dictionary<string, object?>> Rows { get; set; }
}

/// <summary>
/// 調試資訊。
/// </summary>
public class DebugInfo
{
    public required string Step0Prompt { get; set; }

    public required string Step0LlmJson { get; set; }

    public required string Step1NormalizedQuestion { get; set; }

    public required List<string> Step1Keywords { get; set; }

    public required Dictionary<string, List<string>> Step1Entities { get; set; }

    public required List<string> Step2Keywords { get; set; }

    public required List<string> Step2LayoutFiles { get; set; }

    public required string Step3LlmJson { get; set; }

    public required string Step3Model { get; set; }

    public required FormulaSpec Step3FormulaSpec { get; set; }

    public required string Step3SystemPrompt { get; set; }

    public required string Step4Sql { get; set; }

    public required Step5ExecutionInfo Step5ExecutionBeforeDecorate { get; set; }

    public required Step5ExecutionInfo Step5ExecutionAfterDecorate { get; set; }

    public required Step5ExecutionInfo Step5Execution { get; set; }
}

/// <summary>
/// Step 5 執行資訊。
/// </summary>
public class Step5ExecutionInfo
{
    public required bool Success { get; set; }

    public required int RowCount { get; set; }

    public required int ColumnCount { get; set; }

    public required List<Dictionary<string, object?>> PreviewRows { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Text-to-SQL 執行結果。
/// </summary>
public class TextToSqlResult
{
    public required ReportResponse Report { get; set; }

    public required DebugInfo Debug { get; set; }

    /// <summary>
    /// 本次查詢儲存的歷史檔名。
    /// </summary>
    public string? QueryHistoryFileName { get; set; }
}
