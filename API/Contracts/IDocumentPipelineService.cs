namespace API.Contracts;

/// <summary>
/// 文件處理服務。
/// </summary>
public interface IDocumentPipelineService
{
    /// <summary>
    /// 將 PDF 文件內容抽取後，進行結構化抽取並整理為 JSON 字串。
    /// </summary>
    /// <param name="file">上傳文件。</param>
    /// <param name="userId">登入使用者 ID。</param>
    /// <param name="sessionId">對話工作階段 ID。</param>
    /// <param name="llmProvider">指定 LLM 提供者。</param>
    /// <param name="documentType">文件類型（claimApplication、medicalReceipt、settlementAgreement、unknown）。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>JSON 字串。</returns>
    Task<string> ExtractPdfAsStructuredJsonAsync(
        IFormFile file,
        string? userId,
        string? sessionId,
        string? llmProvider,
        string? documentType,
        CancellationToken cancellationToken);
}
