using System.Data;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using Microsoft.Data.SqlClient;

namespace API.Services;

/// <summary>
/// 系統使用者登入服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class SystemUserLoginService : ISystemUserLoginService
{
    private readonly IConfiguration _configuration;
    private readonly CustomLogger _logger;

    public SystemUserLoginService(IConfiguration configuration, ILogger<SystemUserLoginService> logger)
    {
        _configuration = configuration;
        _logger = logger.ToCustomLogger();
    }

    public async Task<SystemUserLoginResult?> LoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString("SysDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:SysDatabase 未設定。");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        const string sql = "SELECT TOP (1) user_id, user_name FROM sysp_user WHERE user_id = @userId";

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandType = CommandType.Text;
            command.Parameters.AddWithValue("@userId", userId.Trim());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new SystemUserLoginResult
            {
                UserId = reader["user_id"]?.ToString() ?? userId.Trim(),
                UserName = reader["user_name"]?.ToString() ?? string.Empty
            };
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "system user login failed. userId: {}", userId);
            throw;
        }
    }
}
