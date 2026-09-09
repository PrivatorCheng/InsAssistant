using System.Text.Json;

namespace API.Infrastructure;

/// <summary>
/// 專案統一日誌包裝。
/// </summary>
public sealed class CustomLogger
{
    private readonly ILogger _logger;

    public CustomLogger(ILogger logger)
    {
        _logger = logger;
    }

    public void Info(string message, params object?[] args)
    {
        _logger.LogInformation(message, Sanitize(args));
    }

    public void Error(Exception exception, string message, params object?[] args)
    {
        _logger.LogError(exception, message, Sanitize(args));
    }

    private static object?[] Sanitize(object?[] args)
    {
        return args.Select(static arg => arg is null ? null : JsonSerializer.Serialize(arg)).Cast<object?>().ToArray();
    }
}
