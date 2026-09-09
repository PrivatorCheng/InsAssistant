using API.Contracts;
using API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers.G1;

/// <summary>
/// 文件處理控制器。
/// </summary>
[ApiController]
[Route("api/g1/document-pipeline")]
public class DocumentPipelineController : ControllerBase
{
    private readonly IDocumentPipelineService _documentPipelineService;

    public DocumentPipelineController(IDocumentPipelineService documentPipelineService)
    {
        _documentPipelineService = documentPipelineService;
    }

    /// <summary>
    /// 將 PDF 文件內容抽取後，進行結構化抽取並整理為 JSON 字串。
    /// </summary>
    [HttpPost("extract-pdf-json")]
    [AllowAnonymous]
    public async Task<ResponseDataSchema<string>> ExtractPdfJson(
        [FromForm] IFormFile? file,
        [FromForm] string? sessionId,
        [FromForm] string? llmProvider,
        [FromForm] string? documentType,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return new ResponseDataSchema<string>
            {
                Code = -1,
                Msg = "請上傳檔案。",
                Data = null,
                TraceId = HttpContext.TraceIdentifier
            };
        }

        try
        {
            var userId = GetLoginUserId();
            var json = await _documentPipelineService.ExtractPdfAsStructuredJsonAsync(
                file,
                userId,
                sessionId,
                llmProvider,
                documentType,
                cancellationToken);
            return new ResponseDataSchema<string>
            {
                Code = 0,
                Msg = "解析成功",
                Data = json,
                TraceId = HttpContext.TraceIdentifier
            };
        }
        catch (NotSupportedException ex)
        {
            return new ResponseDataSchema<string>
            {
                Code = -1,
                Msg = ex.Message,
                Data = null,
                TraceId = HttpContext.TraceIdentifier
            };
        }
        catch (Exception ex)
        {
            return new ResponseDataSchema<string>
            {
                Code = -1,
                Msg = $"解析失敗: {ex.Message}",
                Data = null,
                TraceId = HttpContext.TraceIdentifier
            };
        }
    }

    private string? GetLoginUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)
            && Request.Headers.TryGetValue("X-User-Id", out var headerUserId))
        {
            userId = headerUserId.ToString();
        }

        return userId;
    }
}
