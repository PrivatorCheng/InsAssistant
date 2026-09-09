using System.Text.Json;
using System.Text.Json.Serialization;
using API.Attributes;
using API.Contracts;
using API.Models;

namespace API.Services;

internal sealed class LlmQuerySpec
{
    [JsonPropertyName("query_type")]
    public string? QueryType { get; set; }

    [JsonPropertyName("query_desc")]
    public string? QueryDesc { get; set; }

    [JsonPropertyName("targetTables")]
    public List<string>? TargetTables { get; set; }

    [JsonPropertyName("joins")]
    public List<LlmJoinDetail>? Joins { get; set; }

    [JsonPropertyName("dataFields")]
    public List<LlmDimensionDetail>? DataFields { get; set; }

    [JsonPropertyName("dimensions")]
    public List<LlmDimensionDetail>? LegacyDimensions { get; set; }

    [JsonPropertyName("metrics")]
    public List<LlmMetricDetail>? Metrics { get; set; }

    [JsonPropertyName("filters")]
    public List<LlmFilterDetail>? Filters { get; set; }

    [JsonPropertyName("sort")]
    public List<LlmSortDetail>? Sort { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("formulaSpec")]
    public LlmFormulaSpec? FormulaSpec { get; set; }

    [JsonPropertyName("tasks")]
    public List<LlmFormulaTask>? Tasks { get; set; }

    [JsonPropertyName("formula")]
    public LlmFormulaExpression? Formula { get; set; }
}

internal sealed class LlmFormulaSpec
{
    [JsonPropertyName("formula_name")]
    public string? FormulaName { get; set; }

    [JsonPropertyName("tasks")]
    public List<LlmFormulaTask>? Tasks { get; set; }

    [JsonPropertyName("formula")]
    public LlmFormulaExpression? Formula { get; set; }
}

internal sealed class LlmFormulaTask
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; set; }

    [JsonPropertyName("querySentence")]
    public string? QuerySentence { get; set; }

    [JsonPropertyName("querySpec")]
    public LlmQuerySpec? QuerySpec { get; set; }
}

internal sealed class LlmFormulaExpression
{
    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("left")]
    public string? Left { get; set; }

    [JsonPropertyName("right")]
    public string? Right { get; set; }

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }

    [JsonPropertyName("formula_name")]
    public string? FormulaName { get; set; }
}

internal sealed class LlmJoinDetail
{
    [JsonPropertyName("leftTable")]
    public string? LeftTable { get; set; }

    [JsonPropertyName("joinType")]
    public string? JoinType { get; set; }

    [JsonPropertyName("leftField")]
    public string? LeftField { get; set; }

    [JsonPropertyName("leftFields")]
    public List<string>? LeftFields { get; set; }

    [JsonPropertyName("rightTable")]
    public string? RightTable { get; set; }

    [JsonPropertyName("rightField")]
    public string? RightField { get; set; }

    [JsonPropertyName("rightFields")]
    public List<string>? RightFields { get; set; }

    [JsonPropertyName("on")]
    public List<LlmJoinCondition>? On { get; set; }
}

internal sealed class LlmJoinCondition
{
    [JsonPropertyName("leftField")]
    public string? LeftField { get; set; }

    [JsonPropertyName("rightField")]
    public string? RightField { get; set; }
}

internal sealed class LlmDimensionDetail
{
    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }
}

internal sealed class LlmMetricDetail
{
    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("aggregation")]
    public string? Aggregation { get; set; }

    [JsonPropertyName("alias")]
    public string? Alias { get; set; }
}

internal sealed class LlmFilterDetail
{
    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("operator")]
    public string? Operator { get; set; }

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

internal sealed class LlmSortDetail
{
    [JsonPropertyName("table")]
    public string? Table { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }
}

[Service(ServiceLifetime.Singleton)]
public sealed class QueryPlanningSharedProcessor
{
    private readonly IWhitelistLayoutService _whitelistLayoutService;

    public QueryPlanningSharedProcessor(IWhitelistLayoutService whitelistLayoutService)
    {
        _whitelistLayoutService = whitelistLayoutService;
    }

    public (List<string> AvailableTables, Dictionary<string, IReadOnlyCollection<string>> TableFieldMap) BuildContext(List<string> selectedLayouts)
    {
        var availableTables = selectedLayouts
            .Select(Path.GetFileNameWithoutExtension)
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("table_list", StringComparison.OrdinalIgnoreCase))
            .Select(x => x!)
            .ToList();

        var tableFieldMap = availableTables.ToDictionary(
            table => table,
            table => _whitelistLayoutService.GetFields(table),
            StringComparer.OrdinalIgnoreCase);

        return (availableTables, tableFieldMap);
    }

    public string BuildTableFieldText(Dictionary<string, IReadOnlyCollection<string>> tableFieldMap)
    {
        return string.Join("; ", tableFieldMap.Select(pair =>
        {
            var fields = pair.Value.Take(30);
            return $"{pair.Key}:[{string.Join(",", fields)}]";
        }));
    }

    public string BuildUserPrompt(PreprocessResult preprocessResult)
    {
        return $@"使用者正規化問題：{preprocessResult.NormalizedQuestion}
起始日期：{preprocessResult.StartDate:yyyy-MM-dd}
結束日期：{preprocessResult.EndDate:yyyy-MM-dd}

請根據問題與可用表格生成 FormulaSpec JSON。";
    }

    public string ExtractJsonText(string textContent)
    {
        var jsonMatch = System.Text.RegularExpressions.Regex.Match(
            textContent,
            @"\{[\s\S]*\}",
            System.Text.RegularExpressions.RegexOptions.RightToLeft);

        if (!jsonMatch.Success)
        {
            throw new InvalidOperationException($"無法從 LLM 回應中提取 JSON：{textContent}");
        }

        return jsonMatch.Value;
    }

    public string? TryReadApiErrorMessage(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public FormulaSpec ParseAndNormalize(
        string llmJson,
        PreprocessResult preprocessResult,
        List<string> availableTables,
        Dictionary<string, IReadOnlyCollection<string>> tableFieldMap)
    {
        var llmFormulaSpec = JsonSerializer.Deserialize<LlmFormulaSpec>(
            llmJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (llmFormulaSpec is null)
        {
            throw new InvalidOperationException("LLM JSON 反序列化失敗");
        }

        var formulaSpec = ConvertToFormulaSpec(llmFormulaSpec);
        formulaSpec = NormalizeFormulaSpec(formulaSpec, preprocessResult, availableTables, tableFieldMap);
        ValidateFormulaSpec(formulaSpec);

        return formulaSpec;
    }

    private FormulaSpec NormalizeFormulaSpec(
        FormulaSpec formulaSpec,
        PreprocessResult preprocessResult,
        List<string> availableTables,
        Dictionary<string, IReadOnlyCollection<string>> tableFieldMap)
    {
        formulaSpec.FormulaName = string.IsNullOrWhiteSpace(formulaSpec.FormulaName)
            ? null
            : formulaSpec.FormulaName.Trim();

        formulaSpec.Tasks ??= new List<FormulaTaskSpec>();

        foreach (var task in formulaSpec.Tasks)
        {
            task.TaskId = string.IsNullOrWhiteSpace(task.TaskId) ? null : task.TaskId.Trim();
            task.QuerySentence = string.IsNullOrWhiteSpace(task.QuerySentence) ? null : task.QuerySentence.Trim();

            if (task.QuerySpec is null)
            {
                continue;
            }

            var normalizedTaskQuerySpec = NormalizeQuerySpec(task.QuerySpec, preprocessResult, availableTables, tableFieldMap);
            if (normalizedTaskQuerySpec.TargetTables.Count == 0)
            {
                normalizedTaskQuerySpec = BuildHeuristicSpec(preprocessResult, availableTables, tableFieldMap);
            }

            task.QuerySpec = normalizedTaskQuerySpec;
        }

        if (formulaSpec.Formula is not null)
        {
            formulaSpec.Formula.Operator = string.IsNullOrWhiteSpace(formulaSpec.Formula.Operator)
                ? null
                : formulaSpec.Formula.Operator.Trim().ToUpperInvariant();
            formulaSpec.Formula.Left = string.IsNullOrWhiteSpace(formulaSpec.Formula.Left)
                ? null
                : formulaSpec.Formula.Left.Trim();
            formulaSpec.Formula.Right = string.IsNullOrWhiteSpace(formulaSpec.Formula.Right)
                ? null
                : formulaSpec.Formula.Right.Trim();
            formulaSpec.Formula.Alias = string.IsNullOrWhiteSpace(formulaSpec.Formula.Alias)
                ? null
                : formulaSpec.Formula.Alias.Trim();
            formulaSpec.Formula.FormulaName = string.IsNullOrWhiteSpace(formulaSpec.Formula.FormulaName)
                ? null
                : formulaSpec.Formula.FormulaName.Trim();
        }

        return formulaSpec;
    }

    private void ValidateFormulaSpec(FormulaSpec formulaSpec)
    {
        if (formulaSpec.Tasks.Count == 0)
        {
            throw new InvalidOperationException("Step3 必須回傳 FormulaSpec.tasks");
        }

        var hasTaskQuerySpec = false;
        foreach (var task in formulaSpec.Tasks)
        {
            if (task.QuerySpec is null)
            {
                continue;
            }

            hasTaskQuerySpec = true;
            ValidateQuerySpec(task.QuerySpec);
        }

        if (!hasTaskQuerySpec)
        {
            throw new InvalidOperationException("FormulaSpec.tasks 每個 task 至少要包含一個 querySpec");
        }
    }

    private static FormulaSpec ConvertToFormulaSpec(LlmFormulaSpec source)
    {
        var tasks = (source.Tasks ?? new List<LlmFormulaTask>())
            .Select(task => new FormulaTaskSpec
            {
                TaskId = task.TaskId,
                QuerySentence = task.QuerySentence,
                QuerySpec = task.QuerySpec is null ? null : ConvertToQuerySpec(task.QuerySpec)
            })
            .ToList();

        FormulaExpressionSpec? formula = null;
        if (source.Formula is not null)
        {
            formula = new FormulaExpressionSpec
            {
                Operator = source.Formula.Operator,
                Left = source.Formula.Left,
                Right = source.Formula.Right,
                Alias = source.Formula.Alias,
                FormulaName = source.Formula.FormulaName
            };
        }

        return new FormulaSpec
        {
            FormulaName = source.FormulaName,
            Tasks = tasks,
            Formula = formula
        };
    }

    private static QuerySpec ConvertToQuerySpec(LlmQuerySpec source)
    {
        var targetTables = source.TargetTables?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];

        var joins = new List<JoinDetail>();
        foreach (var join in source.Joins ?? [])
        {
            if (string.IsNullOrWhiteSpace(join.LeftTable) || string.IsNullOrWhiteSpace(join.RightTable))
            {
                continue;
            }

            var joinType = string.IsNullOrWhiteSpace(join.JoinType) ? null : join.JoinType;

            if (join.On is { Count: > 0 })
            {
                foreach (var condition in join.On.Where(x =>
                             !string.IsNullOrWhiteSpace(x.LeftField) &&
                             !string.IsNullOrWhiteSpace(x.RightField)))
                {
                    joins.Add(new JoinDetail
                    {
                        LeftTable = join.LeftTable!,
                        LeftField = condition.LeftField!,
                        RightTable = join.RightTable!,
                        RightField = condition.RightField!,
                        JoinType = joinType
                    });
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(join.LeftField) && !string.IsNullOrWhiteSpace(join.RightField))
            {
                joins.Add(new JoinDetail
                {
                    LeftTable = join.LeftTable!,
                    LeftField = join.LeftField!,
                    RightTable = join.RightTable!,
                    RightField = join.RightField!,
                    JoinType = joinType
                });

                continue;
            }

            if (join.LeftFields is { Count: > 0 } && join.RightFields is { Count: > 0 })
            {
                var pairCount = Math.Min(join.LeftFields.Count, join.RightFields.Count);
                for (var i = 0; i < pairCount; i++)
                {
                    var leftField = join.LeftFields[i];
                    var rightField = join.RightFields[i];
                    if (string.IsNullOrWhiteSpace(leftField) || string.IsNullOrWhiteSpace(rightField))
                    {
                        continue;
                    }

                    joins.Add(new JoinDetail
                    {
                        LeftTable = join.LeftTable!,
                        LeftField = leftField,
                        RightTable = join.RightTable!,
                        RightField = rightField,
                        JoinType = joinType
                    });
                }
            }
        }

        var dataFields = (source.DataFields ?? source.LegacyDimensions)?
            .Where(x => !string.IsNullOrWhiteSpace(x.Table) && !string.IsNullOrWhiteSpace(x.Field))
            .Select(x => new DimensionDetail
            {
                Table = x.Table!,
                Field = x.Field!,
                Alias = x.Alias
            })
            .ToList() ?? [];

        var metrics = source.Metrics?
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Table) &&
                !string.IsNullOrWhiteSpace(x.Field) &&
                !string.IsNullOrWhiteSpace(x.Aggregation))
            .Select(x => new MetricDetail
            {
                Table = x.Table!,
                Field = x.Field!,
                Aggregation = x.Aggregation!,
                Alias = string.IsNullOrWhiteSpace(x.Alias) ? "metricValue" : x.Alias!
            })
            .ToList() ?? [];

        var filters = source.Filters?
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Table) &&
                !string.IsNullOrWhiteSpace(x.Field) &&
                !string.IsNullOrWhiteSpace(x.Operator))
            .Select(x => new FilterDetail
            {
                Table = x.Table!,
                Field = x.Field!,
                Operator = x.Operator!,
                Value = NormalizeFilterValues(x.Value)
            })
            .ToList() ?? [];

        var sort = source.Sort?
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Table) &&
                !string.IsNullOrWhiteSpace(x.Field))
            .Select(x => new SortDetail
            {
                Table = x.Table!,
                Field = x.Field!,
                Direction = string.IsNullOrWhiteSpace(x.Direction) ? "ASC" : x.Direction!
            })
            .ToList() ?? [];

        return new QuerySpec
        {
            QueryType = source.QueryType,
            QueryDesc = source.QueryDesc,
            TargetTables = targetTables,
            Joins = joins,
            DataFields = dataFields,
            Metrics = metrics,
            Filters = filters,
            Sort = sort,
            Limit = source.Limit
        };
    }

    private static List<string> NormalizeFilterValues(JsonElement rawValue)
    {
        if (rawValue.ValueKind == JsonValueKind.Undefined || rawValue.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (rawValue.ValueKind == JsonValueKind.Array)
        {
            return rawValue
                .EnumerateArray()
                .Select(ConvertFilterValueToString)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .ToList();
        }

        var single = ConvertFilterValueToString(rawValue);
        return string.IsNullOrWhiteSpace(single) ? [] : [single];
    }

    private static string? ConvertFilterValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private QuerySpec NormalizeQuerySpec(
        QuerySpec spec,
        PreprocessResult preprocessResult,
        List<string> availableTables,
        Dictionary<string, IReadOnlyCollection<string>> tableFieldMap)
    {
        spec.TargetTables ??= new List<string>();
        spec.Joins ??= new List<JoinDetail>();
        spec.DataFields ??= new List<DimensionDetail>();
        spec.Metrics ??= new List<MetricDetail>();
        spec.Filters ??= new List<FilterDetail>();
        spec.Sort ??= new List<SortDetail>();
        spec.Limit ??= 200;
        if (string.IsNullOrWhiteSpace(spec.QueryType))
        {
            spec.QueryType = InferQueryType(preprocessResult.NormalizedQuestion);
        }

        if (string.IsNullOrWhiteSpace(spec.QueryDesc))
        {
            spec.QueryDesc = null;
        }
        else
        {
            spec.QueryDesc = spec.QueryDesc.Trim();
        }

        spec.TargetTables = spec.TargetTables
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeIdentifier)
            .ToList();

        foreach (var dimension in spec.DataFields)
        {
            dimension.Table = NormalizeIdentifier(dimension.Table);
            dimension.Field = NormalizeIdentifier(dimension.Field);
            dimension.Alias = string.IsNullOrWhiteSpace(dimension.Alias) ? null : dimension.Alias.Trim();
        }

        foreach (var join in spec.Joins)
        {
            join.LeftTable = NormalizeIdentifier(join.LeftTable);
            join.LeftField = NormalizeIdentifier(join.LeftField);
            join.RightTable = NormalizeIdentifier(join.RightTable);
            join.RightField = NormalizeIdentifier(join.RightField);
            join.JoinType = string.IsNullOrWhiteSpace(join.JoinType) ? null : join.JoinType.Trim().ToUpperInvariant();
        }

        foreach (var metric in spec.Metrics)
        {
            metric.Table = NormalizeIdentifier(metric.Table);
            metric.Field = NormalizeIdentifier(metric.Field);
            metric.Aggregation = metric.Aggregation.Trim().ToUpperInvariant();
            metric.Alias = string.IsNullOrWhiteSpace(metric.Alias) ? "metricValue" : metric.Alias.Trim();
        }

        foreach (var filter in spec.Filters)
        {
            filter.Table = NormalizeIdentifier(filter.Table);
            filter.Field = NormalizeIdentifier(filter.Field);
            filter.Operator = filter.Operator.Trim().ToUpperInvariant();
        }

        foreach (var sort in spec.Sort)
        {
            sort.Table = NormalizeIdentifier(sort.Table);
            sort.Field = NormalizeIdentifier(sort.Field);
            sort.Direction = sort.Direction.Trim().ToUpperInvariant();
        }

        return spec;
    }

    private QuerySpec BuildHeuristicSpec(
        PreprocessResult preprocessResult,
        List<string> availableTables,
        Dictionary<string, IReadOnlyCollection<string>> tableFieldMap)
    {
        var question = preprocessResult.NormalizedQuestion;

        var preferredTables = new[] { "clam_stl_instype", "clam_stl_main", "clam_clm_main" };
        var targetTable = preferredTables.FirstOrDefault(preferred =>
                availableTables.Any(table => table.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
            ?? availableTables.FirstOrDefault()
            ?? "clam_stl_instype";

        var fields = tableFieldMap.TryGetValue(targetTable, out var targetFields)
            ? targetFields
            : _whitelistLayoutService.GetFields(targetTable);

        if (fields.Count == 0)
        {
            var fallbackTable = availableTables.FirstOrDefault(table =>
            {
                var candidateFields = tableFieldMap.TryGetValue(table, out var mapped)
                    ? mapped
                    : _whitelistLayoutService.GetFields(table);
                return candidateFields.Count > 0;
            });

            if (!string.IsNullOrWhiteSpace(fallbackTable))
            {
                targetTable = fallbackTable;
                fields = tableFieldMap.TryGetValue(targetTable, out var mapped)
                    ? mapped
                    : _whitelistLayoutService.GetFields(targetTable);
            }
        }

        var metrics = new List<MetricDetail>();

        if (question.Contains("件數", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("數量", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("筆數", StringComparison.OrdinalIgnoreCase))
        {
            var countField = ResolveExistingField(fields, ["iclaim"]) ?? fields.FirstOrDefault()?.ToLowerInvariant() ?? "iclaim";
            metrics.Add(new MetricDetail
            {
                Table = targetTable,
                Field = countField,
                Aggregation = "COUNT",
                Alias = "claimCount"
            });
        }

        if (question.Contains("金額", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("總金額", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("加總", StringComparison.OrdinalIgnoreCase) ||
            question.Contains("總額", StringComparison.OrdinalIgnoreCase))
        {
            var amountCandidates = new[] { "mstl", "mapp", "mapp_total1", "mfee" };
            var amountField = ResolveExistingField(fields, amountCandidates);
            if (!string.IsNullOrWhiteSpace(amountField))
            {
                metrics.Add(new MetricDetail
                {
                    Table = targetTable,
                    Field = amountField,
                    Aggregation = "SUM",
                    Alias = "totalAmount"
                });
            }
        }

        if (metrics.Count == 0)
        {
            var fallbackField = ResolveExistingField(fields, ["iclaim"]) ?? fields.FirstOrDefault()?.ToLowerInvariant() ?? "iclaim";
            metrics.Add(new MetricDetail
            {
                Table = targetTable,
                Field = fallbackField,
                Aggregation = "COUNT",
                Alias = "claimCount"
            });
        }

        var filters = new List<FilterDetail>();
        if (preprocessResult.StartDate.HasValue && preprocessResult.EndDate.HasValue)
        {
            var dateCandidates = new[] { "dcreate", "dapply", "tcreate", "tupdate", "dest" };
            var dateField = ResolveExistingField(fields, dateCandidates);
            if (!string.IsNullOrWhiteSpace(dateField))
            {
                filters.Add(new FilterDetail
                {
                    Table = targetTable,
                    Field = dateField,
                    Operator = "BETWEEN",
                    Value =
                    [
                        preprocessResult.StartDate.Value.ToString("yyyy-MM-dd"),
                        preprocessResult.EndDate.Value.ToString("yyyy-MM-dd")
                    ]
                });
            }
        }

        return new QuerySpec
        {
            QueryType = InferQueryType(question),
            QueryDesc = question,
            TargetTables = [targetTable],
            Joins = new List<JoinDetail>(),
            DataFields = new List<DimensionDetail>(),
            Metrics = metrics,
            Filters = filters,
            Sort = new List<SortDetail>(),
            Limit = 200
        };
    }

    private static string? ResolveExistingField(IReadOnlyCollection<string> fields, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var matched = fields.FirstOrDefault(field => field.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matched))
            {
                return matched.ToLowerInvariant();
            }
        }

        return null;
    }

    private static string NormalizeIdentifier(string value)
    {
        var trimmed = value.Trim().Trim('`', '"', '[', ']');
        var filtered = new string(trimmed.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());
        var normalized = string.IsNullOrWhiteSpace(filtered) ? trimmed : filtered;
        return normalized.ToLowerInvariant();
    }

    private static string? NormalizeQueryType(string? queryType)
    {
        if (string.IsNullOrWhiteSpace(queryType))
        {
            return null;
        }

        return queryType.Trim().ToLowerInvariant();
    }

    private static string InferQueryType(string question)
    {
        return question.Contains("明細", StringComparison.OrdinalIgnoreCase) ? "detail" : "summary";
    }

    private void ValidateQuerySpec(QuerySpec spec)
    {
        foreach (var table in spec.TargetTables)
        {
            if (!_whitelistLayoutService.IsValidTable(table))
            {
                throw new InvalidOperationException($"資料表 {table} 不在白名單內");
            }
        }

        foreach (var dimension in spec.DataFields)
        {
            if (!_whitelistLayoutService.IsValidField(dimension.Table, dimension.Field))
            {
                throw new InvalidOperationException($"無效資料欄位：{dimension.Table}.{dimension.Field}");
            }
        }

        foreach (var metric in spec.Metrics)
        {
            if (!_whitelistLayoutService.IsValidField(metric.Table, metric.Field))
            {
                throw new InvalidOperationException($"無效指標欄位：{metric.Table}.{metric.Field}");
            }
        }

        foreach (var join in spec.Joins)
        {
            if (!_whitelistLayoutService.IsValidTable(join.LeftTable) || !_whitelistLayoutService.IsValidTable(join.RightTable))
            {
                throw new InvalidOperationException($"無效關聯資料表：{join.LeftTable} 或 {join.RightTable}");
            }

            if (!_whitelistLayoutService.IsValidField(join.LeftTable, join.LeftField) ||
                !_whitelistLayoutService.IsValidField(join.RightTable, join.RightField))
            {
                throw new InvalidOperationException($"無效關聯欄位：{join.LeftTable}.{join.LeftField} 或 {join.RightTable}.{join.RightField}");
            }
        }

        foreach (var filter in spec.Filters)
        {
            if (!_whitelistLayoutService.IsValidField(filter.Table, filter.Field))
            {
                throw new InvalidOperationException($"無效過濾欄位：{filter.Table}.{filter.Field}");
            }
        }

    }
}
