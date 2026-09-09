namespace API.Contracts;

public interface IReportRepository
{
    Task<List<Dictionary<string, object?>>> QueryAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken);
}
