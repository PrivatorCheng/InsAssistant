namespace API.Infrastructure;

/// <summary>
/// CustomLogger 建立擴充。
/// </summary>
public static class LoggingExtensions
{
    public static CustomLogger ToCustomLogger(this ILogger logger)
    {
        return new CustomLogger(logger);
    }
}
