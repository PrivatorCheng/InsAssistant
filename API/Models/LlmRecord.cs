using System.Text.Json.Serialization;

namespace API.Models;

/// <summary>
/// 單次 LLM 呼叫紀錄。
/// </summary>
public sealed class LlmRecord
{
    /// <summary>
    /// 呼叫步驟代號（STEP1 / STEP3）。
    /// </summary>
    [JsonPropertyName("step")]
    public required string Step { get; set; }

    /// <summary>
    /// LLM 提供者。
    /// </summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; set; }

    /// <summary>
    /// LLM 模型名稱。
    /// </summary>
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    /// <summary>
    /// 請求 token 數。
    /// </summary>
    [JsonPropertyName("inputToken")]
    public int InputToken { get; set; }

    /// <summary>
    /// 回應 token 數。
    /// </summary>
    [JsonPropertyName("outputToken")]
    public int OutputToken { get; set; }

    /// <summary>
    /// Cache token 數。
    /// </summary>
    [JsonPropertyName("cacheLength")]
    public int CacheLength { get; set; }

    /// <summary>
    /// 呼叫耗時（毫秒）。
    /// </summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    /// <summary>
    /// Prompt 長度。
    /// </summary>
    [JsonPropertyName("promptLength")]
    public int PromptLength { get; set; }

    /// <summary>
    /// 回應內容長度。
    /// </summary>
    [JsonPropertyName("responseLength")]
    public int ResponseLength { get; set; }
}