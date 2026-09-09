namespace API.Models;

/// <summary>
/// 對話模型回應與命中資訊。
/// </summary>
public sealed class InsuranceChatCompletionResult
{
    /// <summary>
    /// 模型回覆內容。
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// 實際命中的模型名稱。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 本次回覆使用的 Provider。
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 輸入 Token 數。
    /// </summary>
    public int InputToken { get; set; }

    /// <summary>
    /// 輸出 Token 數。
    /// </summary>
    public int OutputToken { get; set; }

    /// <summary>
    /// Cache Token 數。
    /// </summary>
    public int CacheLength { get; set; }

    /// <summary>
    /// 呼叫耗時（毫秒）。
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// 呼叫是否成功。
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// 失敗原因（僅在失敗時有值）。
    /// </summary>
    public string? ErrorMessage { get; set; }
}
