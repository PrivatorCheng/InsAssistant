using API.Contracts;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.G1;

/// <summary>
/// Text-to-SQL 查詢控制器。
/// </summary>
[Route("api/g1/text-to-sql")]
[ApiController]
[Authorize(Roles = "CLAF305")]
public class TextToSqlController : ControllerBase
{
    private readonly ITextToSqlService _textToSqlService;
    private readonly IConfiguration _configuration;

    public TextToSqlController(
        ITextToSqlService textToSqlService,
        IConfiguration configuration)
    {
        _textToSqlService = textToSqlService;
        _configuration = configuration;
    }

    /// <summary>
    /// 取得可選擇的 LLM 提供者清單。
    /// </summary>
    [HttpGet("llm-providers")]
    [AllowAnonymous]
    public ResponseDataSchema<LlmProviderOptionsResponse> GetLlmProviders()
    {
        var providers = LlmProviderResolver.ResolveAvailableProviderOptions(_configuration);
        var defaultProvider = LlmProviderResolver.ResolveProvider(_configuration);

        return new ResponseDataSchema<LlmProviderOptionsResponse>
        {
            Code = 0,
            Msg = "查詢成功",
            Data = new LlmProviderOptionsResponse
            {
                Providers = providers,
                DefaultProvider = defaultProvider
            },
            TraceId = HttpContext.TraceIdentifier
        };
    }

    /// <summary>
    /// 自然語言查詢報表。
    /// </summary>
    [HttpPost("queryTextToSql")]
    [AllowAnonymous]
    public async Task<ResponseDataSchema<TextToSqlResult>> QueryTextToSql(
        [FromBody] NaturalLanguageQueryRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        return new ResponseDataSchema<TextToSqlResult>
        {
            Code = -1,
            Msg = "Step1-Step5 功能已停用",
            Data = null,
            TraceId = HttpContext.TraceIdentifier
        };
    }

}
