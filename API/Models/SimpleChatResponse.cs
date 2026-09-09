namespace API.Models;

/// <summary>
/// 簡易對話回覆。
/// </summary>
public class SimpleChatResponse
{
    /// <summary>
    /// AI 教練回覆內容。
    /// </summary>
    public required string Reply { get; set; }

    /// <summary>
    /// 本次呼叫 LLM 使用的 System Prompt。
    /// </summary>
    public required string SystemPrompt { get; set; }

    /// <summary>
    /// 本次呼叫 LLM 傳送的 Story Script。
    /// </summary>
    public required string StoryScript { get; set; }

    /// <summary>
    /// 本次對話實際使用的 Provider。
    /// </summary>
    public string? LlmProvider { get; set; }

    /// <summary>
    /// 本次對話實際命中的模型名稱。
    /// </summary>
    public string? LlmModel { get; set; }

    /// <summary>
    /// 客戶姓名（由業務員助理模式的 DialogSpec 回覆解析）。
    /// </summary>
    public string? CustName { get; set; }

    /// <summary>
    /// 客戶車牌號碼（由業務員助理模式的 DialogSpec 回覆解析）。
    /// </summary>
    public string? CustTagNo { get; set; }

    /// <summary>
    /// 客戶提及的理賠險種清單。
    /// </summary>
    public List<string> InsureTypeList { get; set; } = [];

    /// <summary>
    /// 尚待確認的賠案資訊清單。
    /// </summary>
    public List<TodoInfoItem> TodoInfo { get; set; } = [];

    /// <summary>
    /// 尚待上傳的賠案文件清單。
    /// </summary>
    public List<TodoDocumentItem> TodoDocument { get; set; } = [];

    /// <summary>
    /// 本次對話對應的歷史檔名。
    /// </summary>
    public string? HistoryFileName { get; set; }

    /// <summary>
    /// 本次對話可檢視的已上傳檔案清單。
    /// </summary>
    public List<SimpleChatUploadedFileItem> UploadedFiles { get; set; } = [];

    /// <summary>
    /// LLM 呼叫紀錄寫入失敗訊息清單（僅供 Debug 顯示）。
    /// </summary>
    public List<string> LlmLogErrors { get; set; } = [];

    /// <summary>
    /// 最後一次 LLM 原始回覆內容（僅供 Debug 顯示）。
    /// </summary>
    public string? LlmRawReply { get; set; }

    /// <summary>
    /// 第一次呼叫 LLM（關鍵字擷取）使用的 System Prompt（僅供 Debug 顯示）。
    /// </summary>
    public string? LlmKeywordSystemPrompt { get; set; }

    /// <summary>
    /// 第一次呼叫 LLM（關鍵字擷取）回傳結果（僅供 Debug 顯示）。
    /// </summary>
    public string? LlmKeyword { get; set; }

    /// <summary>
    /// 系統起始時建立並持有的 Gemini Context Cache 名稱（僅供 Debug 顯示）。
    /// </summary>
    public string? ContextCacheName { get; set; }
}
