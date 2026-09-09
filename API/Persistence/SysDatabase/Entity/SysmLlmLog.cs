using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Persistence.SysDatabase.Entity;

/// <summary>
/// 系統 LLM 呼叫日誌。
/// </summary>
[Table("sysm_llm_log")]
public class SysmLlmLog
{
    /// <summary>
    /// 系統識別碼（固定為 'TEST_SALE'）。
    /// </summary>
    [Key]
    [Column("sys_id")]
    [MaxLength(50)]
    public required string SysId { get; set; }

    /// <summary>
    /// 對話工作階段識別碼。
    /// </summary>
    [Column("session_id")]
    [MaxLength(100)]
    public required string SessionId { get; set; }

    /// <summary>
    /// 序號。
    /// </summary>
    [Column("query_seq")]
    public required int QuerySeq { get; set; }

    /// <summary>
    /// LLM 提供者。
    /// </summary>
    [Column("provider")]
    [MaxLength(50)]
    public required string Provider { get; set; }

    /// <summary>
    /// LLM 模型名稱。
    /// </summary>
    [Column("model")]
    [MaxLength(100)]
    public required string Model { get; set; }

    /// <summary>
    /// 輸入 Token 長度。
    /// </summary>
    [Column("input_token")]
    public required int InputToken { get; set; }

    /// <summary>
    /// 輸出 Token 長度。
    /// </summary>
    [Column("output_token")]
    public required int OutputToken { get; set; }

    /// <summary>
    /// Cache Token 長度。
    /// </summary>
    [Column("cache_length")]
    public required int CacheLength { get; set; }

    /// <summary>
    /// 呼叫耗時（毫秒）。
    /// </summary>
    [Column("duration_ms")]
    public required long DurationMs { get; set; }

    /// <summary>
    /// 提示詞長度。
    /// </summary>
    [Column("prompt_length")]
    public required int PromptLength { get; set; }

    /// <summary>
    /// 回應長度。
    /// </summary>
    [Column("response_length")]
    public required int ResponseLength { get; set; }

    /// <summary>
    /// 呼叫是否成功（Y/N）。
    /// </summary>
    [Column("success_flag")]
    [MaxLength(1)]
    public required string SuccessFlag { get; set; }

    /// <summary>
    /// 呼叫起始時間。
    /// </summary>
    [Column("request_time")]
    public required DateTime RequestTime { get; set; }

    /// <summary>
    /// 呼叫結束時間。
    /// </summary>
    [Column("response_time")]
    public required DateTime ResponseTime { get; set; }
}
