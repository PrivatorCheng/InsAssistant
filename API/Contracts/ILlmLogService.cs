namespace API.Contracts;

/// <summary>
/// LLM 呼叫日誌服務介面。
/// </summary>
public interface ILlmLogService
{
    /// <summary>
    /// 記錄 LLM 呼叫日誌。
    /// </summary>
    /// <param name="sessionId">對話工作階段識別碼。</param>
    /// <param name="provider">LLM 提供者。</param>
    /// <param name="model">LLM 模型名稱。</param>
    /// <param name="inputToken">輸入 Token 長度。</param>
    /// <param name="outputToken">輸出 Token 長度。</param>
    /// <param name="cacheLength">Cache Token 長度。</param>
    /// <param name="durationMs">呼叫耗時（毫秒）。</param>
    /// <param name="promptLength">提示詞長度。</param>
    /// <param name="responseLength">回應長度。</param>
    /// <param name="successFlag">是否成功（true=Y, false=N）。</param>
    /// <param name="requestTime">呼叫起始時間。</param>
    /// <param name="responseTime">呼叫結束時間。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<string?> LogLlmCallAsync(
        string sessionId,
        string provider,
        string model,
        int inputToken,
        int outputToken,
        int cacheLength,
        long durationMs,
        int promptLength,
        int responseLength,
        bool successFlag,
        DateTime requestTime,
        DateTime responseTime,
        CancellationToken cancellationToken = default);
}
