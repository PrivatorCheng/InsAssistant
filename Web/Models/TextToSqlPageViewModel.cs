namespace Web.Models;

/// <summary>
/// Text-to-SQL 首頁所需資料。
/// </summary>
public class TextToSqlPageViewModel
{
    /// <summary>
    /// API 基底網址。
    /// </summary>
    public required string ApiBaseUrl { get; set; }

    /// <summary>
    /// 是否顯示 Debug 面板。
    /// </summary>
    public bool ShowDebugPanel { get; set; }

    /// <summary>
    /// 影像上傳處理旗標（A=LLM, B=Tesseract）。
    /// </summary>
    public string ImageProcessFlag { get; set; } = "A";
}
