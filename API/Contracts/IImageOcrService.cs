namespace API.Contracts;

/// <summary>
/// 影像 OCR 文字擷取服務介面。
/// </summary>
public interface IImageOcrService
{
    /// <summary>
    /// 將影像位元組內容擷取為純文字。
    /// </summary>
    /// <param name="imageBytes">影像位元組內容。</param>
    /// <param name="originalFileName">原始檔名。</param>
    /// <param name="mimeType">影像 MIME 類型。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>OCR 擷取文字。</returns>
    Task<string> ExtractTextAsync(
        byte[] imageBytes,
        string originalFileName,
        string mimeType,
        CancellationToken cancellationToken = default);
}
