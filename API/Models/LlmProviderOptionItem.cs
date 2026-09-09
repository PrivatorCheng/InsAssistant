namespace API.Models;

/// <summary>
/// 單一 LLM 提供者選項。
/// </summary>
public sealed class LlmProviderOptionItem
{
    /// <summary>
    /// LLM 提供者名稱。
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// 是否顯示於前端。
    /// </summary>
    public required bool DisplayFlag { get; set; }
}