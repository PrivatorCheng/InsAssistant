namespace API.Models;

/// <summary>
/// 簡易對話回覆中的已上傳檔案資訊。
/// </summary>
public class SimpleChatUploadedFileItem
{
    /// <summary>
    /// 伺服器儲存檔名。
    /// </summary>
    public required string StoredFileName { get; set; }

    /// <summary>
    /// 原始上傳檔名。
    /// </summary>
    public required string OriginalFileName { get; set; }

    /// <summary>
    /// 結構化標題。
    /// </summary>
    public string Title { get; set; } = string.Empty;
}
