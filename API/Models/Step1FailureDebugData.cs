namespace API.Models;

/// <summary>
/// Step 1 失敗時回傳前端的除錯資料。
/// </summary>
public sealed class Step1FailureDebugData
{
    public required string Prompt { get; set; }

    public string? LlmJson { get; set; }
}
