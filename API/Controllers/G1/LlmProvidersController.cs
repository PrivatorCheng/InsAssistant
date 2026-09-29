using API.Models;
using API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.G1;

/// <summary>
/// LLM 提供者選項控制器。
/// </summary>
[Route("api/g1/llm-providers")]
[ApiController]
public class LlmProvidersController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public LlmProvidersController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 取得可選擇的 LLM 提供者清單。
    /// </summary>
    [HttpGet("options")]
    [AllowAnonymous]
    public ResponseDataSchema<LlmProviderOptionsResponse> GetOptions()
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
}