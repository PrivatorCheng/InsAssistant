namespace API.Services;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// 查詢結果裝飾服務實作。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class ResultDecorationService : IResultDecorationService
{
    private readonly IReportRepository _reportRepository;
    private readonly CustomLogger _logger;
    private readonly IConfiguration _configuration;
    private static readonly Lazy<List<ResultDecorationRule>> DecorationRules =
        new(() => LoadDecorationRules());

    public ResultDecorationService(
        IReportRepository reportRepository,
        ILogger<ResultDecorationService> logger,
        IConfiguration configuration)
    {
        _reportRepository = reportRepository;
        _logger = logger.ToCustomLogger();
        _configuration = configuration;
    }

    /// <summary>
    /// 對查詢結果進行裝飾轉換。
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> DecorateResultsAsync(
        List<Dictionary<string, object?>> rows,
        List<string> targetTables,
        List<DimensionDetail> dataFields,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0 || targetTables.Count == 0)
        {
            return rows;
        }

        var rules = DecorationRules.Value;
        if (rules.Count == 0)
        {
            return rows;
        }

        // 建立欄位對應表：
        // 1) table.field -> alias (精準匹配)
        // 2) field -> aliases (跨表 fallback，支援公式查詢合併結果)
        var fieldTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fieldAliasMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataField in dataFields)
        {
            var key = $"{dataField.Table}.{dataField.Field}".ToLower();
            var alias = dataField.Alias ?? dataField.Field;
            fieldTable[key] = alias;

            if (!fieldAliasMap.TryGetValue(dataField.Field, out var aliases))
            {
                aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                fieldAliasMap[dataField.Field] = aliases;
            }

            aliases.Add(alias);
        }

        var decoratedRows = new List<Dictionary<string, object?>>(rows);

        // 逐筆資料進行裝飾
        for (var i = 0; i < decoratedRows.Count; i++)
        {
            var row = decoratedRows[i];
            
            // 尋找適用的裝飾規則
            foreach (var rule in rules)
            {
                // 檢查是否屬於目標表且包含來源欄位
                var fieldKey = $"{rule.SourceTable}.{rule.SourceField}".ToLower();
                var candidateAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (fieldTable.TryGetValue(fieldKey, out var exactColumnAlias))
                {
                    candidateAliases.Add(exactColumnAlias);
                }

                // 若沒有精準 table.field 命中，允許同 field 名稱跨表 fallback。
                if (fieldAliasMap.TryGetValue(rule.SourceField, out var fallbackAliases))
                {
                    foreach (var alias in fallbackAliases)
                    {
                        candidateAliases.Add(alias);
                    }
                }

                if (candidateAliases.Count == 0)
                {
                    continue;
                }

                foreach (var columnAlias in candidateAliases)
                {
                    if (!row.TryGetValue(columnAlias, out var sourceValue) || sourceValue == null)
                    {
                        continue;
                    }

                    try
                    {
                        // 從比對資料表查詢對應的顯示值
                        var displayValue = await GetDisplayValueAsync(
                            rule,
                            sourceValue.ToString() ?? string.Empty,
                            cancellationToken);

                        if (displayValue != null)
                        {
                            row[columnAlias] = displayValue;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "裝飾欄位失敗: table={}, field={}, value={}", rule.SourceTable, rule.SourceField, sourceValue);
                    }
                }
            }
        }

        return decoratedRows;
    }

    /// <summary>
    /// 從比對資料表查詢顯示值。
    /// </summary>
    private async Task<string?> GetDisplayValueAsync(
        ResultDecorationRule rule,
        string lookupValue,
        CancellationToken cancellationToken)
    {
        // 建立查詢 SQL
        var sql = BuildLookupSql(rule, lookupValue);
        if (string.IsNullOrWhiteSpace(sql))
        {
            return null;
        }

        var parameters = new Dictionary<string, object?>
        {
            ["lookupValue"] = lookupValue
        };

        try
        {
            var results = await _reportRepository.QueryAsync(sql, parameters, cancellationToken);
            if (results.Count == 0)
            {
                return null;
            }

            var firstRow = results[0];
            var displayFieldLower = rule.DisplayField.ToLower();

            // 尋找相符的欄位（忽略大小寫）
            foreach (var kvp in firstRow)
            {
                if (kvp.Key.Equals(rule.DisplayField, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value?.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "查詢顯示值失敗: table={}, field={}", rule.LookupTable, rule.DisplayField);
        }

        return null;
    }

    /// <summary>
    /// 建立查詢 SQL。
    /// </summary>
    private static string BuildLookupSql(ResultDecorationRule rule, string lookupValue)
    {
        var sql = $"SELECT {rule.DisplayField} FROM {rule.LookupTable} WHERE {rule.LookupField} = @lookupValue";

        if (!string.IsNullOrWhiteSpace(rule.QueryCondition))
        {
            sql += $" AND {rule.QueryCondition}";
        }

        return sql;
    }

    /// <summary>
    /// 從 doc/ResultDecorate.txt 加載裝飾規則。
    /// </summary>
    private static List<ResultDecorationRule> LoadDecorationRules()
    {
        var rules = new List<ResultDecorationRule>();

        try
        {
            var absolutePath = ResolveRulesFilePath();

            if (absolutePath == null || !File.Exists(absolutePath))
            {
                return rules;
            }

            var lines = File.ReadAllLines(absolutePath);
            foreach (var line in lines)
            {
                // 跳過空行和標題行
                if (string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith("來源資料表"))
                {
                    continue;
                }

                var parts = line.Split('|');
                if (parts.Length < 5)
                {
                    continue;
                }

                var rule = new ResultDecorationRule
                {
                    SourceTable = parts[0].Trim(),
                    SourceField = parts[1].Trim(),
                    LookupTable = parts[2].Trim(),
                    LookupField = parts[3].Trim(),
                    DisplayField = parts[4].Trim(),
                    QueryCondition = parts.Length > 5 ? parts[5].Trim() : null
                };

                if (!string.IsNullOrEmpty(rule.SourceTable)
                    && !string.IsNullOrEmpty(rule.SourceField)
                    && !string.IsNullOrEmpty(rule.LookupTable)
                    && !string.IsNullOrEmpty(rule.LookupField)
                    && !string.IsNullOrEmpty(rule.DisplayField))
                {
                    rules.Add(rule);
                }
            }
        }
        catch (Exception ex)
        {
            // 記錄錯誤但不中斷執行
            // 此處會使用 NLog 記錄
            _ = ex;
        }

        return rules;
    }

    /// <summary>
    /// 解析 ResultDecorate 規則檔路徑。
    /// </summary>
    private static string? ResolveRulesFilePath()
    {
        var fileName = "ResultDecorate.txt";
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "doc", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "doc", fileName)
        };

        // 由執行目錄往上尋找，支援 API/bin/Debug/netX 等執行路徑。
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            candidates.Add(Path.Combine(current.FullName, "doc", fileName));
            current = current.Parent;
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
