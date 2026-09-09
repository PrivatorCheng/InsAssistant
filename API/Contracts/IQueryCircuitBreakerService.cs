namespace API.Contracts;

/// <summary>
/// 控制資料庫查詢的熔斷機制。
/// </summary>
public interface IQueryCircuitBreakerService
{
    void ThrowIfOpen();

    void RecordSuccess();

    void RecordFailure();
}
