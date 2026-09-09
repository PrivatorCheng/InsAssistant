using System.ComponentModel.DataAnnotations;
using API.Attributes;

namespace API.Models;

/// <summary>
/// 自然語言查詢請求。
/// </summary>
public class NaturalLanguageQueryRequest
{
    /// <summary>
    /// 使用者輸入查詢句。
    /// </summary>
    [Required]
    [MaxLength(500)]
    public required string Query { get; set; }

    /// <summary>
    /// 部門代號，用於資料權限。
    /// </summary>
    [MaxLength(20)]
    public string? DepartmentId { get; set; }

    /// <summary>
    /// 使用者識別。
    /// </summary>
    [SensitiveData(SensitiveDataType.Id)]
    [MaxLength(50)]
    public string? UserId { get; set; }

    /// <summary>
    /// 指定 Step3 使用的 LLM 提供者。
    /// </summary>
    [MaxLength(30)]
    public string? LlmProvider { get; set; }

    /// <summary>
    /// 目前選取的查詢歷史檔名；有值時寫回同檔，否則建立新檔。
    /// </summary>
    [MaxLength(100)]
    public string? SelectedHistoryFileName { get; set; }
}
