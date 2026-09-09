using API.Attributes;
using API.Models;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace API.Services;

/// <summary>
/// 提供 QueryPlanning 共用 System Prompt 模板。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class QueryPlanningPromptProvider
{
    private readonly string _template;
    private readonly string _tableListPath;
    private readonly string _layoutDirectoryPath;
    private readonly string _termDescriptionPath;
    private readonly string _formulaDescriptionPath;

    private const string UnknownDescription = "無資料表說明";
    private const string UnknownNote = "無";
    private static readonly JsonSerializerOptions QuerySpecJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class ForeignKeyInfo
    {
        public required string ParentTable { get; set; }

        public required string ChildTable { get; set; }

        public required string JoinColumns { get; set; }

        public required string Relation { get; set; }
    }

    private sealed class TableInfo
    {
        public required string Name { get; set; }

        public required string Description { get; set; }
    }

    private sealed class TableColumnSpec
    {
        public required string FieldName { get; set; }

        public required string FieldDisplayName { get; set; }

        public required string FieldType { get; set; }

        public required string AllowNull { get; set; }

        public required string Note { get; set; }
    }

    private sealed class EnterpriseTermInfo
    {
        public required string TermName { get; set; }

        public required string Prompt { get; set; }
    }

    private sealed class FormulaPromptInfo
    {
        public required string FormulaName { get; set; }

        public required string FormulaObjectJsonText { get; set; }
    }

    public QueryPlanningPromptProvider(IWebHostEnvironment environment)
    {
        var promptPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "doc", "Step3Prompt.txt"));
        _tableListPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "doc", "db_layout", "table_list.md"));
        _layoutDirectoryPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "doc", "db_layout"));
        _termDescriptionPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "doc", "TermDescription.txt"));
        _formulaDescriptionPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "doc", "FormulaDescription.txt"));
        _template = File.Exists(promptPath)
            ? File.ReadAllText(promptPath)
            : "你是一個企業報表查詢規劃器。查詢句：{{QUERY_SENTENCE}}。可用資料表：{{AVAILABLE_TABLES}}。資料表欄位：{{TABLE_FIELDS}}。資料表結構清單：{{TABLE_STRUCTURE_LIST}}。實體資料清單：{{ENTITY_LIST}}。請只回傳 FormulaSpec JSON。";
    }

    public string BuildSystemPrompt(
        string querySentenceText,
        IReadOnlyCollection<string> step2Keywords,
        string availableTablesText,
        string tableFieldsText,
        IReadOnlyCollection<string> availableTables,
        string entityListText,
        FormulaSpec? previousFormulaSpec = null)
    {
        var availableTableListText = BuildAvailableTableListText(availableTables, availableTablesText);
        var tableFieldSpecText = BuildTableFieldSpecText(availableTables, tableFieldsText);
        var structureListText = BuildTableStructureList(availableTables);
        var previousFormulaSpecText = previousFormulaSpec is null
            ? "-"
            : JsonSerializer.Serialize(previousFormulaSpec, QuerySpecJsonOptions);
        var enterpriseTermsText = BuildEnterpriseTermsText(step2Keywords);
        var formulaListText = BuildFormulaListText(step2Keywords);

        return _template
            .Replace("{{QUERY_SENTENCE}}", querySentenceText, StringComparison.Ordinal)
            .Replace("{{PRE_FORMULA_SPEC}}", previousFormulaSpecText, StringComparison.Ordinal)
            .Replace("{{PRE_QUERY_SPEC}}", previousFormulaSpecText, StringComparison.Ordinal)
            .Replace("{{AVAILABLE_TABLES}}", availableTableListText, StringComparison.Ordinal)
            .Replace("{{TABLE_FIELDS}}", tableFieldSpecText, StringComparison.Ordinal)
            .Replace("{{TABLE_FIELD_TEXT}}", tableFieldSpecText, StringComparison.Ordinal)
            .Replace("{{TABLE_STRUCTURE_LIST}}", structureListText, StringComparison.Ordinal)
                .Replace("{{ENTITY_LIST}}", entityListText, StringComparison.Ordinal)
            .Replace("{{ENTERPRISE_TERMS}}", enterpriseTermsText, StringComparison.Ordinal)
            .Replace("{{FORMULA_LIST}}", formulaListText, StringComparison.Ordinal)
            .Replace("{EnterpriseTerms}", enterpriseTermsText, StringComparison.Ordinal);
    }

    private string BuildFormulaListText(IReadOnlyCollection<string> step2Keywords)
    {
        if (step2Keywords.Count == 0)
        {
            return "-";
        }

        var keywordSet = new HashSet<string>(
            step2Keywords
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Select(keyword => keyword.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (keywordSet.Count == 0)
        {
            return "-";
        }

        var allFormulas = ReadFormulaPromptInfos();
        var matchedFormulas = allFormulas
            .Where(formula => keywordSet.Contains(formula.FormulaName))
            .Select(formula => formula.FormulaObjectJsonText)
            .ToList();

        if (matchedFormulas.Count == 0)
        {
            return "-";
        }

        return $"[{Environment.NewLine}{string.Join($",{Environment.NewLine}", matchedFormulas)}{Environment.NewLine}]";
    }

    private string BuildEnterpriseTermsText(IReadOnlyCollection<string> step2Keywords)
    {
        if (step2Keywords.Count == 0)
        {
            return "-";
        }

        var keywordSet = new HashSet<string>(
            step2Keywords
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Select(keyword => keyword.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (keywordSet.Count == 0)
        {
            return "-";
        }

        var allTerms = ReadEnterpriseTermInfos();
        var matchedTerms = allTerms
            .Where(term => keywordSet.Contains(term.TermName))
            .Select(term => $"- {term.TermName}: {term.Prompt}")
            .ToList();

        return matchedTerms.Count > 0
            ? string.Join(Environment.NewLine, matchedTerms)
            : "-";
    }

    private List<EnterpriseTermInfo> ReadEnterpriseTermInfos()
    {
        var result = new List<EnterpriseTermInfo>();
        if (!File.Exists(_termDescriptionPath))
        {
            return result;
        }

        var lines = File.ReadAllLines(_termDescriptionPath);
        var inEnterpriseTermSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                inEnterpriseTermSection = line.Contains("企業術語", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEnterpriseTermSection || !line.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line
                .Split('|')
                .Select(cell => cell.Trim())
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList();

            if (cells.Count < 3)
            {
                continue;
            }

            var termName = cells[0];
            var prompt = cells[2];
            if (termName.Equals("術語名稱", StringComparison.OrdinalIgnoreCase) ||
                termName.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(prompt) || prompt.Equals("Prompt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new EnterpriseTermInfo
            {
                TermName = termName,
                Prompt = prompt
            });
        }

        return result;
    }

    private List<FormulaPromptInfo> ReadFormulaPromptInfos()
    {
        var result = new List<FormulaPromptInfo>();
        if (!File.Exists(_formulaDescriptionPath))
        {
            return result;
        }

        try
        {
            using var jsonDocument = JsonDocument.Parse(File.ReadAllText(_formulaDescriptionPath));
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var formulaElement in jsonDocument.RootElement.EnumerateArray())
            {
                if (formulaElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!formulaElement.TryGetProperty("formula_name", out var formulaNameElement))
                {
                    continue;
                }

                if (!formulaElement.TryGetProperty("formula", out var formulaBodyElement))
                {
                    continue;
                }

                var formulaName = formulaNameElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(formulaName))
                {
                    continue;
                }

                result.Add(new FormulaPromptInfo
                {
                    FormulaName = formulaName,
                    FormulaObjectJsonText = JsonSerializer.Serialize(
                        new Dictionary<string, object?>
                        {
                            ["formula_name"] = formulaName,
                            ["formula"] = formulaBodyElement
                        },
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        })
                });
            }
        }
        catch
        {
            return new List<FormulaPromptInfo>();
        }

        return result;
    }

    private string BuildAvailableTableListText(IReadOnlyCollection<string> availableTables, string fallbackText)
    {
        var tables = availableTables.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tables.Count == 0)
        {
            return fallbackText;
        }

        var tableInfoMap = ReadTableInfoMap();
        var lines = tables.Select(table =>
        {
            var description = tableInfoMap.TryGetValue(table, out var info)
                ? info.Description
                : UnknownDescription;

            return $"- {table}：{description}";
        });

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildTableFieldSpecText(IReadOnlyCollection<string> availableTables, string fallbackText)
    {
        var tables = availableTables.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (tables.Count == 0)
        {
            return fallbackText;
        }

        var sections = new List<string>();
        foreach (var table in tables)
        {
            var columns = ReadTableColumnSpecs(table);
            if (columns.Count == 0)
            {
                sections.Add($"### {table}{Environment.NewLine}無欄位定義");
                continue;
            }

            var mdTable = new List<string>
            {
                $"### {table}",
                string.Empty,
                "| 欄位名稱 | 欄位說明 | 備註 |",
                "| --- | --- | --- |"
            };

            foreach (var column in columns)
            {
                mdTable.Add($"| {column.FieldName} | {column.FieldDisplayName} | {column.Note} |");
            }

            sections.Add(string.Join(Environment.NewLine, mdTable));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private string BuildTableStructureList(IReadOnlyCollection<string> availableTables)
    {
        var availableSet = new HashSet<string>(availableTables, StringComparer.OrdinalIgnoreCase);
        var fkList = ReadForeignKeyList();

        var lines = new List<string>();
        var seenPair = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fk in fkList.Where(x => availableSet.Contains(x.ParentTable) && availableSet.Contains(x.ChildTable)))
        {
            var pairKey = $"{fk.ParentTable}|{fk.ChildTable}";
            if (seenPair.Add(pairKey))
            {
                lines.Add($"| {fk.ParentTable} | {fk.ChildTable} | {fk.JoinColumns} | {fk.Relation} |");
            }
        }

        // 若可用表中缺少中介表，補上可推導的一跳間接關聯（A->B, B->C 推導 A->C）。
        foreach (var first in fkList)
        {
            foreach (var second in fkList)
            {
                if (!first.ChildTable.Equals(second.ParentTable, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = first.ParentTable;
                var middle = first.ChildTable;
                var target = second.ChildTable;

                if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!availableSet.Contains(source) || !availableSet.Contains(target))
                {
                    continue;
                }

                if (availableSet.Contains(middle))
                {
                    continue;
                }

                var pairKey = $"{source}|{target}";
                if (!seenPair.Add(pairKey))
                {
                    continue;
                }

                var firstColumns = ParseColumns(first.JoinColumns);
                var secondColumns = ParseColumns(second.JoinColumns);
                var commonColumns = firstColumns
                    .Intersect(secondColumns, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var inferredColumns = commonColumns.Count > 0
                    ? string.Join(", ", commonColumns)
                    : first.JoinColumns;

                lines.Add($"| {source} | {target} | {inferredColumns} | 推導(透過 {middle}) |");
            }
        }

        if (lines.Count == 0)
        {
            return "無可用資料表對應的 Foreign Key 關聯。";
        }

        var mdRows = new List<string>
        {
            "| 主表 | 子表 | 關聯欄位 | 關係 |",
            "| --- | --- | --- | --- |"
        };
        mdRows.AddRange(lines);

        return string.Join(Environment.NewLine, mdRows);
    }

    private static List<string> ParseColumns(string rawColumns)
    {
        return rawColumns
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private List<ForeignKeyInfo> ReadForeignKeyList()
    {
        var result = new List<ForeignKeyInfo>();
        if (!File.Exists(_tableListPath))
        {
            return result;
        }

        var lines = File.ReadAllLines(_tableListPath);
        var inForeignKeySection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# "))
            {
                inForeignKeySection = line.Equals("# Foreign Key List", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inForeignKeySection || !line.StartsWith('|'))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 6)
            {
                continue;
            }

            var parent = parts[1].Trim();
            var child = parts[2].Trim();
            var columns = parts[3].Trim();
            var relation = parts[4].Trim();

            if (string.IsNullOrWhiteSpace(parent) ||
                string.IsNullOrWhiteSpace(child) ||
                parent.Equals("主表", StringComparison.OrdinalIgnoreCase) ||
                parent.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new ForeignKeyInfo
            {
                ParentTable = parent,
                ChildTable = child,
                JoinColumns = columns,
                Relation = relation
            });
        }

        return result;
    }

    private Dictionary<string, TableInfo> ReadTableInfoMap()
    {
        var result = new Dictionary<string, TableInfo>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_tableListPath))
        {
            return result;
        }

        var lines = File.ReadAllLines(_tableListPath);
        var inTableListSection = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# "))
            {
                inTableListSection = line.Equals("# 資料表清單", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inTableListSection || !line.StartsWith('|'))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 4)
            {
                continue;
            }

            var tableName = parts[1].Trim();
            var description = parts[2].Trim();
            if (string.IsNullOrWhiteSpace(tableName) ||
                tableName.Equals("資料表名稱", StringComparison.OrdinalIgnoreCase) ||
                tableName.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[tableName] = new TableInfo
            {
                Name = tableName,
                Description = string.IsNullOrWhiteSpace(description) ? UnknownDescription : description
            };
        }

        return result;
    }

    private List<TableColumnSpec> ReadTableColumnSpecs(string table)
    {
        var result = new List<TableColumnSpec>();
        var filePath = Path.Combine(_layoutDirectoryPath, $"{table}.md");
        if (!File.Exists(filePath))
        {
            return result;
        }

        var lines = File.ReadAllLines(filePath);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 8)
            {
                continue;
            }

            var fieldName = parts[3].Trim().Replace("\\", string.Empty);
            var fieldDisplayName = parts[4].Trim();
            var fieldType = parts[5].Trim();
            var allowNull = parts[6].Trim();
            var note = parts[7].Trim();

            if (string.IsNullOrWhiteSpace(fieldName) ||
                fieldName.Equals("英文欄位名稱", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new TableColumnSpec
            {
                FieldName = fieldName,
                FieldDisplayName = string.IsNullOrWhiteSpace(fieldDisplayName) ? "無" : fieldDisplayName,
                FieldType = string.IsNullOrWhiteSpace(fieldType) ? "未知" : fieldType,
                AllowNull = string.IsNullOrWhiteSpace(allowNull) ? "未註明" : allowNull,
                Note = string.IsNullOrWhiteSpace(note) ? UnknownNote : note
            });
        }

        return result;
    }
}
