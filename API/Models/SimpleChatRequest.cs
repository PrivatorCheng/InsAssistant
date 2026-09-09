using System.ComponentModel.DataAnnotations;
using API.Attributes;

namespace API.Models;

/// <summary>
/// 簡易對話請求。
/// </summary>
public class SimpleChatRequest
{
    /// <summary>
    /// 對話工作階段識別碼。
    /// </summary>
    [Required]
    [MaxLength(100)]
    [SensitiveData(SensitiveDataType.Id)]
    public required string SessionId { get; set; }

    /// <summary>
    /// 業務員最新回報的客戶對話內容。
    /// </summary>
    [Required]
    [MaxLength(4000)]
    [SensitiveData(SensitiveDataType.General)]
    public required string UserMessage { get; set; }

    /// <summary>
    /// 指定本次對話使用的 LLM 提供者。
    /// </summary>
    [MaxLength(30)]
    public string? LlmProvider { get; set; }

    /// <summary>
    /// 對話模式（salesAssistant、customerService、claimAssistant）。
    /// </summary>
    [MaxLength(30)]
    public string? PromptTemplateMode { get; set; }

    /// <summary>
    /// 是否使用 SystemPrompt1 範本；保留相容舊版前端，建議改用 PromptTemplateMode。
    /// </summary>
    public bool UsePromptTemplate1 { get; set; } = true;

    /// <summary>
    /// 已上傳文件的結構化標題清單。
    /// </summary>
    public List<string> UploadedDocumentTitles { get; set; } = [];

    /// <summary>
    /// 本案客戶實際投保險種清單。
    /// </summary>
    public List<string> PolicyInsureTypeList { get; set; } = [];
}
