namespace API.Models;

/// <summary>
/// LLM 請求分層輸入模型。
/// </summary>
public sealed class LlmPromptEnvelope
{
    /// <summary>
    /// 系統提示詞（角色、規則、輸出格式）。
    /// </summary>
    public required string SystemPrompt { get; set; }

    /// <summary>
    /// 外部檢索上下文（例如向量檢索結果）。
    /// </summary>
    public string ContextText { get; set; } = string.Empty;

    /// <summary>
    /// 歷史對話內容。
    /// </summary>
    public string HistoryText { get; set; } = string.Empty;

    /// <summary>
    /// 本輪使用者輸入。
    /// </summary>
    public string UserText { get; set; } = string.Empty;
}