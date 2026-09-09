using API.Models;

namespace API.Contracts;

/// <summary>
/// Text-to-SQL 主流程服務。
/// </summary>
public interface ITextToSqlService
{
    Task<TextToSqlResult> QueryAsync(NaturalLanguageQueryRequest request, CancellationToken cancellationToken);
}
