namespace API.Contracts;

using API.Models;

/// <summary>
/// 對話模型呼叫介面。
/// </summary>
public interface IInsuranceChatCompletionService
{
    /// <summary>
    /// 送出分層提示詞（System/Context/History/User），取得模型回覆。
    /// </summary>
    Task<InsuranceChatCompletionResult> ExecuteAsync(
        string? llmProvider,
        LlmPromptEnvelope prompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 送出分層提示詞與影像（二進位）取得模型回覆。
    /// </summary>
    Task<InsuranceChatCompletionResult> ExecuteWithInlineImageAsync(
        string? llmProvider,
        LlmPromptEnvelope prompt,
        byte[] imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(llmProvider, prompt, cancellationToken);
    }
}
