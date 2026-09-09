namespace API.Models;

/// <summary>
/// 編譯後 SQL 結果。
/// </summary>
public class CompiledSql
{
    public required string Sql { get; set; }

    public required Dictionary<string, object?> Parameters { get; set; }
}
