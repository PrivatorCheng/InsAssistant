namespace API.Models;

/// <summary>
/// Step3 FormulaSpec 規劃結果。
/// </summary>
public class Step3PlanningResult
{
    public required FormulaSpec FormulaSpec { get; set; }

    public required string SystemPrompt { get; set; }

    public required string LlmJson { get; set; }

    public required string Model { get; set; }

    public required string Provider { get; set; }

    public required LlmRecord LlmRecord { get; set; }
}