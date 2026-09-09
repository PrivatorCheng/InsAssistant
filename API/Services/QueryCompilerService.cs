using System.Text;
using API.Attributes;
using API.Contracts;
using API.Models;

namespace API.Services;

/// <summary>
/// Step 4：白名單檢查與 SQL 編譯。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class QueryCompilerService : IQueryCompilerService
{
    private readonly IWhitelistLayoutService _whitelistLayoutService;

    public QueryCompilerService(IWhitelistLayoutService whitelistLayoutService)
    {
        _whitelistLayoutService = whitelistLayoutService;
    }

    public CompiledSql Compile(QuerySpec querySpec, Dictionary<string, string> piiTokenMap, string? departmentId)
    {
        if (querySpec.TargetTables.Count == 0)
        {
            throw new InvalidOperationException("QuerySpec.TargetTables 不可為空。");
        }

        foreach (var table in querySpec.TargetTables)
        {
            if (!_whitelistLayoutService.IsValidTable(table))
            {
                throw new InvalidOperationException($"無效資料表：{table}");
            }
        }

        var baseTable = ResolveBaseTable(querySpec);
        var tableAliasMap = BuildTableAliasMap(querySpec);

        var parameters = new Dictionary<string, object?>();
        var whereClauses = new List<string>();
        var parameterIndex = 0;

        foreach (var filter in querySpec.Filters)
        {
            if (!_whitelistLayoutService.IsValidField(filter.Table, filter.Field))
            {
                throw new InvalidOperationException($"無效欄位：{filter.Table}.{filter.Field}");
            }

            var qualifiedField = QualifyField(tableAliasMap, filter.Table, filter.Field);

            switch (filter.Operator.ToUpperInvariant())
            {
                case "EQUALS":
                {
                    var value = RestorePii(filter.Value.FirstOrDefault(), piiTokenMap);
                    var p = $"@p{parameterIndex++}";
                    parameters[p] = value;
                    whereClauses.Add($"{qualifiedField} = {p}");
                    break;
                }
                case "NOT_EQUALS":
                {
                    var value = RestorePii(filter.Value.FirstOrDefault(), piiTokenMap);
                    var p = $"@p{parameterIndex++}";
                    parameters[p] = value;
                    whereClauses.Add($"{qualifiedField} <> {p}");
                    break;
                }
                case "IS_NULL":
                {
                    whereClauses.Add($"{qualifiedField} IS NULL");
                    break;
                }
                case "BETWEEN":
                {
                    if (filter.Value.Count < 2)
                    {
                        continue;
                    }

                    var p1 = $"@p{parameterIndex++}";
                    var p2 = $"@p{parameterIndex++}";
                    parameters[p1] = RestorePii(filter.Value[0], piiTokenMap);
                    parameters[p2] = RestorePii(filter.Value[1], piiTokenMap);
                    whereClauses.Add($"{qualifiedField} BETWEEN {p1} AND {p2}");
                    break;
                }
                case "LIKE":
                {
                    var p = $"@p{parameterIndex++}";
                    parameters[p] = $"%{RestorePii(filter.Value.FirstOrDefault(), piiTokenMap)}%";
                    whereClauses.Add($"{qualifiedField} LIKE {p}");
                    break;
                }
                case "GREATER_THAN":
                {
                    var p = $"@p{parameterIndex++}";
                    parameters[p] = RestorePii(filter.Value.FirstOrDefault(), piiTokenMap);
                    whereClauses.Add($"{qualifiedField} > {p}");
                    break;
                }
                case "LESS_THAN":
                {
                    var p = $"@p{parameterIndex++}";
                    parameters[p] = RestorePii(filter.Value.FirstOrDefault(), piiTokenMap);
                    whereClauses.Add($"{qualifiedField} < {p}");
                    break;
                }
                   case "IN":
                   {
                       var values = filter.Value.Select(v => RestorePii(v, piiTokenMap)).ToList();
                    if (values.Count == 0)
                    {
                        continue;
                    }

                       var pList = values.Select(v => $"@p{parameterIndex++}").ToList();
                       for (int i = 0; i < values.Count; i++)
                       {
                           parameters[pList[i]] = values[i];
                       }
                       whereClauses.Add($"{qualifiedField} IN ({string.Join(", ", pList)})");
                       break;
                   }
                default:
                    throw new InvalidOperationException($"不支援的過濾運算子：{filter.Operator}");
            }
        }

        if (!string.IsNullOrWhiteSpace(departmentId) && _whitelistLayoutService.IsValidField(baseTable, "dept_id"))
        {
            var p = $"@p{parameterIndex++}";
            parameters[p] = departmentId;
            whereClauses.Add($"{QualifyField(tableAliasMap, baseTable, "dept_id")} = {p}");
        }

        var selectClause = BuildSelect(querySpec, tableAliasMap);
        var limit = querySpec.Limit.GetValueOrDefault(200);
        var sqlBuilder = new StringBuilder();
        sqlBuilder.Append("SELECT TOP (").Append(limit).Append(") ").Append(selectClause)
            .Append(" FROM [").Append(baseTable).Append("] AS [").Append(tableAliasMap[baseTable]).Append("] WITH (NOLOCK)");

        var joinClauses = BuildJoinClauses(querySpec, tableAliasMap, baseTable);
        foreach (var joinClause in joinClauses)
        {
            sqlBuilder.Append(' ').Append(joinClause);
        }

        if (whereClauses.Count > 0)
        {
            sqlBuilder.Append(" WHERE ").Append(string.Join(" AND ", whereClauses));
        }

        var groupBy = BuildGroupBy(querySpec, tableAliasMap);
        if (!string.IsNullOrWhiteSpace(groupBy))
        {
            sqlBuilder.Append(" GROUP BY ").Append(groupBy);
        }

        var orderBy = BuildOrderBy(querySpec, tableAliasMap);
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            sqlBuilder.Append(" ORDER BY ").Append(orderBy);
        }

        return new CompiledSql
        {
            Sql = sqlBuilder.ToString(),
            Parameters = parameters
        };
    }

    private string ResolveBaseTable(QuerySpec querySpec)
    {
        if (querySpec.Joins.Count == 0)
        {
            return querySpec.TargetTables[0];
        }

        var leftTables = querySpec.Joins
            .Select(join => join.LeftTable)
            .Where(table => !string.IsNullOrWhiteSpace(table))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightTables = querySpec.Joins
            .Select(join => join.RightTable)
            .Where(table => !string.IsNullOrWhiteSpace(table))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rootCandidates = leftTables
            .Where(table => !rightTables.Contains(table))
            .ToList();

        var baseTable = rootCandidates.Count > 0
            ? rootCandidates.First()
            : querySpec.Joins
                .Select(join => join.LeftTable)
                .FirstOrDefault(table => !string.IsNullOrWhiteSpace(table))
                ?? querySpec.TargetTables[0];

        if (!_whitelistLayoutService.IsValidTable(baseTable))
        {
            throw new InvalidOperationException($"無效資料表：{baseTable}");
        }

        return baseTable;
    }

    private string BuildSelect(QuerySpec querySpec, IReadOnlyDictionary<string, string> tableAliasMap)
    {
        if (querySpec.Metrics.Count == 0)
        {
            if (querySpec.DataFields.Count == 0)
            {
                return "*";
            }

            var detailSelects = new List<string>();
            foreach (var dimension in querySpec.DataFields)
            {
                if (!_whitelistLayoutService.IsValidField(dimension.Table, dimension.Field))
                {
                    throw new InvalidOperationException($"無效欄位：{dimension.Table}.{dimension.Field}");
                }

                var alias = string.IsNullOrWhiteSpace(dimension.Alias) ? dimension.Field : dimension.Alias;
                detailSelects.Add($"{QualifyField(tableAliasMap, dimension.Table, dimension.Field)} AS [{alias}]");
            }

            return string.Join(", ", detailSelects);
        }

        var selects = new List<string>();
        foreach (var dimension in querySpec.DataFields)
        {
            if (!_whitelistLayoutService.IsValidField(dimension.Table, dimension.Field))
            {
                throw new InvalidOperationException($"無效欄位：{dimension.Table}.{dimension.Field}");
            }

            var alias = string.IsNullOrWhiteSpace(dimension.Alias) ? dimension.Field : dimension.Alias;
            selects.Add($"{QualifyField(tableAliasMap, dimension.Table, dimension.Field)} AS [{alias}]");
        }

        foreach (var metric in querySpec.Metrics)
        {
            if (!_whitelistLayoutService.IsValidField(metric.Table, metric.Field))
            {
                throw new InvalidOperationException($"無效欄位：{metric.Table}.{metric.Field}");
            }

            var qualifiedField = QualifyField(tableAliasMap, metric.Table, metric.Field);
            var expression = BuildAggregationExpression(metric.Aggregation, qualifiedField);

            selects.Add($"{expression} AS [{metric.Alias}]");
        }

        return string.Join(", ", selects);
    }

    private static string BuildGroupBy(QuerySpec querySpec, IReadOnlyDictionary<string, string> tableAliasMap)
    {
        if (querySpec.Metrics.Count == 0 || querySpec.DataFields.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(", ", querySpec.DataFields.Select(x => $"[{tableAliasMap[x.Table]}].[{x.Field}]"));
    }

    private string BuildOrderBy(QuerySpec querySpec, IReadOnlyDictionary<string, string> tableAliasMap)
    {
        if (querySpec.Sort.Count == 0)
        {
            return string.Empty;
        }

        var metricExpressionMap = querySpec.Metrics
            .GroupBy(metric => $"{metric.Table}.{metric.Field}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => BuildAggregationExpression(
                    group.First().Aggregation,
                    QualifyField(tableAliasMap, group.First().Table, group.First().Field)),
                StringComparer.OrdinalIgnoreCase);

        var orderBySegments = new List<string>();
        foreach (var sort in querySpec.Sort)
        {
            if (!_whitelistLayoutService.IsValidField(sort.Table, sort.Field))
            {
                throw new InvalidOperationException($"無效欄位：{sort.Table}.{sort.Field}");
            }

            var direction = sort.Direction.Equals("DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            var metricKey = $"{sort.Table}.{sort.Field}";
            if (metricExpressionMap.TryGetValue(metricKey, out var metricExpression))
            {
                orderBySegments.Add($"{metricExpression} {direction}");
                continue;
            }

            orderBySegments.Add($"{QualifyField(tableAliasMap, sort.Table, sort.Field)} {direction}");
        }

        return string.Join(", ", orderBySegments);
    }

    private static string BuildAggregationExpression(string aggregation, string qualifiedField)
    {
        return aggregation.ToUpperInvariant() switch
        {
            "COUNT" => $"COUNT({qualifiedField})",
            "SUM" => $"SUM({qualifiedField})",
            "AVG" => $"AVG({qualifiedField})",
            "MAX" => $"MAX({qualifiedField})",
            "MIN" => $"MIN({qualifiedField})",
            _ => throw new InvalidOperationException($"不支援的聚合函式：{aggregation}")
        };
    }

    private List<string> BuildJoinClauses(QuerySpec querySpec, IReadOnlyDictionary<string, string> tableAliasMap, string baseTable)
    {
        if (querySpec.Joins.Count == 0)
        {
            return new List<string>();
        }

        var groupedJoins = querySpec.Joins
            .GroupBy(join => new
            {
                LeftTable = join.LeftTable,
                RightTable = join.RightTable,
                JoinType = NormalizeJoinType(join.JoinType)
            })
            .Select(group => new JoinGroup(
                group.Key.LeftTable,
                group.Key.RightTable,
                group.Key.JoinType,
                group.ToList()))
            .ToList();

        var clauses = new List<string>();
        var joinedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { baseTable };
        var pending = new List<JoinGroup>(groupedJoins);

        var progressed = true;
        while (pending.Count > 0 && progressed)
        {
            progressed = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var item = pending[i];
                if (!joinedTables.Contains(item.LeftTable) || joinedTables.Contains(item.RightTable))
                {
                    continue;
                }

                ValidateJoinConditions(item.Conditions);

                var leftAlias = tableAliasMap[item.LeftTable];
                var rightAlias = tableAliasMap[item.RightTable];
                var onClause = string.Join(" AND ", item.Conditions.Select(condition =>
                    $"[{leftAlias}].[{condition.LeftField}] = [{rightAlias}].[{condition.RightField}]"));

                clauses.Add($"{item.JoinType} [{item.RightTable}] AS [{rightAlias}] WITH (NOLOCK) ON {onClause}");
                joinedTables.Add(item.RightTable);
                pending.RemoveAt(i);
                progressed = true;
            }
        }

        if (pending.Count > 0)
        {
            throw new InvalidOperationException("JOIN 關聯無法從主資料表串接，請檢查 joins 定義順序與關聯表。");
        }

        return clauses;
    }

    private sealed record JoinGroup(string LeftTable, string RightTable, string JoinType, List<JoinDetail> Conditions);

    private void ValidateJoinConditions(IEnumerable<JoinDetail> joinConditions)
    {
        foreach (var join in joinConditions)
        {
            if (!_whitelistLayoutService.IsValidField(join.LeftTable, join.LeftField) ||
                !_whitelistLayoutService.IsValidField(join.RightTable, join.RightField))
            {
                throw new InvalidOperationException($"無效關聯欄位：{join.LeftTable}.{join.LeftField} 或 {join.RightTable}.{join.RightField}");
            }
        }
    }

    private static string NormalizeJoinType(string? joinType)
    {
        var normalized = (joinType ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "LEFT" => "LEFT JOIN",
            "LEFT JOIN" => "LEFT JOIN",
            "RIGHT" => "RIGHT JOIN",
            "RIGHT JOIN" => "RIGHT JOIN",
            "FULL" => "FULL JOIN",
            "FULL JOIN" => "FULL JOIN",
            "INNER" => "INNER JOIN",
            "INNER JOIN" => "INNER JOIN",
            _ => "INNER JOIN"
        };
    }

    private static Dictionary<string, string> BuildTableAliasMap(QuerySpec querySpec)
    {
        var tables = querySpec.TargetTables
            .Concat(querySpec.Joins.Select(x => x.LeftTable))
            .Concat(querySpec.Joins.Select(x => x.RightTable))
            .Concat(querySpec.DataFields.Select(x => x.Table))
            .Concat(querySpec.Metrics.Select(x => x.Table))
            .Concat(querySpec.Filters.Select(x => x.Table))
            .Concat(querySpec.Sort.Select(x => x.Table))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tables.Count; i++)
        {
            aliasMap[tables[i]] = $"t{i}";
        }

        return aliasMap;
    }

    private static string QualifyField(IReadOnlyDictionary<string, string> tableAliasMap, string table, string field)
    {
        if (!tableAliasMap.TryGetValue(table, out var alias))
        {
            throw new InvalidOperationException($"欄位 {table}.{field} 找不到對應資料表別名。");
        }

        return $"[{alias}].[{field}]";
    }

    private static string? RestorePii(string? value, Dictionary<string, string> piiTokenMap)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return piiTokenMap.TryGetValue(value, out var restored) ? restored : value;
    }
}
