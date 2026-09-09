using API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Infrastructure;

/// <summary>
/// 統一 API 例外回應。
/// </summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    private readonly CustomLogger _logger;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
    {
        _logger = logger.ToCustomLogger();
    }

    public void OnException(ExceptionContext context)
    {
        _logger.Error(context.Exception, "unhandled exception. path: {}", context.HttpContext.Request.Path.ToString());

        var message = context.Exception.Message;
        IDictionary<string, string[]>? details = null;

        if (context.Exception is StepExecutionException stepException)
        {
            message = $"Step {stepException.StepNumber} ({stepException.StepName}) 失敗: {stepException.Message}";
            details = new Dictionary<string, string[]>
            {
                ["step"] = [stepException.StepNumber.ToString()],
                ["stepName"] = [stepException.StepName]
            };

            var responseWithDebug = new ResponseDataSchema<object>
            {
                Code = -1,
                Msg = message,
                Data = stepException.DebugData,
                Details = details,
                TraceId = context.HttpContext.TraceIdentifier
            };

            context.Result = new ObjectResult(responseWithDebug)
            {
                StatusCode = 400
            };
            context.ExceptionHandled = true;
            return;
        }

        var response = new ResponseDataSchema<object>
        {
            Code = -1,
            Msg = message,
            Data = null,
            Details = details,
            TraceId = context.HttpContext.TraceIdentifier
        };

        context.Result = new ObjectResult(response)
        {
            StatusCode = 400
        };
        context.ExceptionHandled = true;
    }
}
