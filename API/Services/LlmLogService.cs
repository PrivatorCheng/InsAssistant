using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Persistence.SysDatabase;
using API.Persistence.SysDatabase.Entity;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// LLM 呼叫日誌服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class LlmLogService : ILlmLogService
{
    private const string SysId = "TEST_SALE";
    private readonly SysDatabaseContext _context;
    private readonly CustomLogger _logger;

    public LlmLogService(
        SysDatabaseContext context,
        ILogger<LlmLogService> logger)
    {
        _context = context;
        _logger = logger.ToCustomLogger();
    }

    /// <summary>
    /// 記錄 LLM 呼叫日誌。
    /// </summary>
    public async Task<string?> LogLlmCallAsync(
        string sessionId,
        string provider,
        string model,
        int inputToken,
        int outputToken,
        int cacheLength,
        long durationMs,
        int promptLength,
        int responseLength,
        bool successFlag,
        DateTime requestTime,
        DateTime responseTime,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 取得下一個序號
            var querySeq = await GetNextSequenceAsync(sessionId, cancellationToken);

            var log = new SysmLlmLog
            {
                SysId = SysId,
                SessionId = sessionId,
                QuerySeq = querySeq,
                Provider = provider,
                Model = model,
                InputToken = inputToken,
                OutputToken = outputToken,
                CacheLength = cacheLength,
                DurationMs = durationMs,
                PromptLength = promptLength,
                ResponseLength = responseLength,
                SuccessFlag = successFlag ? "Y" : "N",
                RequestTime = requestTime,
                ResponseTime = responseTime
            };

            _context.SysmLlmLogs.Add(log);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.Info("llm call logged. sessionId: {}, provider: {}, model: {}, querySeq: {}", 
                sessionId, provider, model, querySeq);

            return null;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "failed to log llm call. sessionId: {}, provider: {}, model: {}", 
                sessionId, provider, model);
            // 不拋出異常，避免日誌記錄影響主業務流程
            return exception.Message;
        }
    }

    /// <summary>
    /// 取得下一個序號。
    /// </summary>
    private async Task<int> GetNextSequenceAsync(string sessionId, CancellationToken cancellationToken)
    {
        var maxSeq = await _context.SysmLlmLogs
            .Where(log => log.SysId == SysId && log.SessionId == sessionId)
            .MaxAsync(log => (int?)log.QuerySeq, cancellationToken);

        return (maxSeq ?? 0) + 1;
    }
}
