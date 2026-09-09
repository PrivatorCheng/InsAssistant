using System.Data;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using Microsoft.Data.SqlClient;

namespace API.Repositories;

/// <summary>
/// Step 5：參數化查詢執行層。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class ReportRepository : IReportRepository
{
    private readonly IConfiguration _configuration;
    private readonly IQueryCircuitBreakerService _queryCircuitBreakerService;
    private readonly CustomLogger _logger;

    public ReportRepository(
        IConfiguration configuration,
        IQueryCircuitBreakerService queryCircuitBreakerService,
        ILogger<ReportRepository> logger)
    {
        _configuration = configuration;
        _queryCircuitBreakerService = queryCircuitBreakerService;
        _logger = logger.ToCustomLogger();
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(
        string sql,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        _queryCircuitBreakerService.ThrowIfOpen();

        var connectionString = _configuration.GetConnectionString("MainDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:MainDatabase 未設定。");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 10;
            command.CommandType = CommandType.Text;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
            }

            var rows = new List<Dictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value;
                }

                rows.Add(row);
            }

            _queryCircuitBreakerService.RecordSuccess();
            return rows;
        }
        catch (Exception exception)
        {
            _queryCircuitBreakerService.RecordFailure();
            _logger.Error(exception, "query failed. sql: {}, params: {}", sql, parameters);
            throw;
        }
    }
}
