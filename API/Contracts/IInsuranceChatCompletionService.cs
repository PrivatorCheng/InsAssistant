namespace API.Contracts;

using API.Models;

/// <summary>
/// 對話模型呼叫介面。
/// </summary>
public interface IInsuranceChatCompletionService
{
    /// <summary>
    /// 送出系統提示詞與用戶訊息，取得模型回覆。
    /// </summary>
    Task<InsuranceChatCompletionResult> ExecuteAsync(string? llmProvider, string systemPrompt, string userMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// 送出系統提示詞、用戶訊息與影像（二進位）取得模型回覆。
    /// </summary>
    Task<InsuranceChatCompletionResult> ExecuteWithInlineImageAsync(
        string? llmProvider,
        string systemPrompt,
        string userMessage,
        byte[] imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(llmProvider, systemPrompt, userMessage, cancellationToken);
    }
}
