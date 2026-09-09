namespace API.Models;

/// <summary>
/// Step 3 失敗時回傳前端的除錯資料。
/// </summary>
public sealed class Step3FailureDebugData
{
    public required string SystemPrompt { get; set; }

    public string? LlmJson { get; set; }

    public string? Model { get; set; }
}
