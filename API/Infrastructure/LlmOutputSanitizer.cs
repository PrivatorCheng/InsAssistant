using System.Text.Encodings.Web;

namespace API.Infrastructure;

/// <summary>
/// 提供 LLM 輸出安全渲染轉換工具。
/// </summary>
public static class LlmOutputSanitizer
{
    /// <summary>
    /// 將不可信文字轉為可安全內嵌於 HTML 的內容，並保留換行顯示。
    /// </summary>
    public static string ToSafeHtml(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalizedText = value
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal);

        var encoded = HtmlEncoder.Default.Encode(normalizedText);
        return encoded
            .Replace("\r\n", "<br/>", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);
    }
}
