namespace API.Models;

/// <summary>
/// 可選擇的 LLM 提供者清單。
/// </summary>
public sealed class LlmProviderOptionsResponse
{
    /// <summary>
    /// 可使用的 LLM 提供者選項。
    /// </summary>
    public required List<LlmProviderOptionItem> Providers { get; set; }

    /// <summary>
    /// 系統預設的 LLM 提供者。
    /// </summary>
    public required string DefaultProvider { get; set; }
}
