using API.Attributes;
using API.Contracts;

namespace API.Services;

/// <summary>
/// 以連續失敗次數管理查詢熔斷。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class QueryCircuitBreakerService : IQueryCircuitBreakerService
{
    private readonly object _syncRoot = new();
    private readonly int _failureThreshold = 3;
    private readonly TimeSpan _openDuration = TimeSpan.FromSeconds(30);

    private int _failureCount;
    private DateTimeOffset? _openedAt;

    public void ThrowIfOpen()
    {
        lock (_syncRoot)
        {
            if (_openedAt is null)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - _openedAt.Value >= _openDuration)
            {
                _openedAt = null;
                _failureCount = 0;
                return;
            }

            throw new InvalidOperationException("查詢服務暫時不可用，請稍後再試。");
        }
    }

    public void RecordSuccess()
    {
        lock (_syncRoot)
        {
            _failureCount = 0;
            _openedAt = null;
        }
    }

    public void RecordFailure()
    {
        lock (_syncRoot)
        {
            _failureCount++;
            if (_failureCount >= _failureThreshold)
            {
                _openedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}
