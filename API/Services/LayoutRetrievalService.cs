using API.Attributes;
using API.Contracts;

namespace API.Services;

/// <summary>
/// Step 2：Layout 動態檢索。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class LayoutRetrievalService : ILayoutRetrievalService
{
    private const string PreferredTableListFile = "table_layout.md";
    private const string LegacyTableListFile = "table_list.md";
    private const string TermDescriptionFile = "TermDescription.txt";
    private readonly IWebHostEnvironment _environment;

    public LayoutRetrievalService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public List<string> RetrieveLayoutFiles(List<string> keywords)
    {
        var layoutDirectory = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", "db_layout"));
        if (!Directory.Exists(layoutDirectory))
        {
            return new List<string>();
        }

        var tableListPath = ResolveTableListPath(layoutDirectory);
        var tableList = ReadTableListSection(tableListPath);
        var termDescriptionPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", TermDescriptionFile));

        var normalizedKeywords = keywords
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword.Trim())
            .ToList();

        var tableBonusScores = BuildEnterpriseTermBonusByTable(termDescriptionPath, normalizedKeywords);

        var scored = tableList
            .Select(item => new
            {
                File = Path.Combine(layoutDirectory, $"{item.TableName}.md"),
                Score = ScoreTable(item.KeywordText, normalizedKeywords) +
                        (tableBonusScores.TryGetValue(item.TableName, out var bonus) ? bonus : 0)
            })
            .Where(item => File.Exists(item.File))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = scored.Where(x => x.Score > 0).Select(x => x.File).Take(5).ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("關鍵字比對失敗");
        }

        return selected;
    }

    private static string ResolveTableListPath(string layoutDirectory)
    {
        var preferredPath = Path.Combine(layoutDirectory, PreferredTableListFile);
        if (File.Exists(preferredPath))
        {
            return preferredPath;
        }

        var legacyPath = Path.Combine(layoutDirectory, LegacyTableListFile);
        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        throw new InvalidOperationException("找不到資料表清單檔案");
    }

    private static List<TableListItem> ReadTableListSection(string tableListPath)
    {
        var lines = File.ReadAllLines(tableListPath);
        var result = new List<TableListItem>();
        var inTableListSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                inTableListSection = line.Equals("# 資料表清單", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inTableListSection || !line.StartsWith("|", StringComparison.Ordinal))
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

            var tableName = cells[0];
            var keywordText = cells[2];

            if (tableName.Equals("資料表名稱", StringComparison.OrdinalIgnoreCase) ||
                tableName.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new TableListItem
            {
                TableName = tableName,
                KeywordText = keywordText
            });
        }

        return result;
    }

    private static int ScoreTable(string keywordText, List<string> keywords)
    {
        var score = 0;
        var tableKeywords = keywordText
            .Split([',', '，', '、', ';', '；', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var keyword in keywords)
        {
            if (keywordText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
                continue;
            }

            if (tableKeywords.Any(tableKeyword =>
                    keyword.Contains(tableKeyword, StringComparison.OrdinalIgnoreCase) ||
                    tableKeyword.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
        }

        return score;
    }

    private static Dictionary<string, int> BuildEnterpriseTermBonusByTable(string termDescriptionPath, List<string> keywords)
    {
        var bonusByTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(termDescriptionPath) || keywords.Count == 0)
        {
            return bonusByTable;
        }

        var keywordSet = new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase);
        var termRows = ReadEnterpriseTermRows(termDescriptionPath);

        foreach (var row in termRows)
        {
            if (!keywordSet.Contains(row.TermName))
            {
                continue;
            }

            foreach (var table in row.Tables)
            {
                if (bonusByTable.TryGetValue(table, out var score))
                {
                    bonusByTable[table] = score + 100;
                }
                else
                {
                    bonusByTable[table] = 100;
                }
            }
        }

        return bonusByTable;
    }

    private static List<EnterpriseTermRow> ReadEnterpriseTermRows(string termDescriptionPath)
    {
        var lines = File.ReadAllLines(termDescriptionPath);
        var result = new List<EnterpriseTermRow>();
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

            if (cells.Count < 2)
            {
                continue;
            }

            var termName = cells[0];
            var tableCell = cells[1];
            if (termName.Equals("術語名稱", StringComparison.OrdinalIgnoreCase) ||
                termName.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tables = tableCell
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(table => table.Trim())
                .Where(table => !string.IsNullOrWhiteSpace(table))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tables.Count == 0)
            {
                continue;
            }

            result.Add(new EnterpriseTermRow
            {
                TermName = termName,
                Tables = tables
            });
        }

        return result;
    }

    private sealed class TableListItem
    {
        public required string TableName { get; set; }

        public required string KeywordText { get; set; }
    }

    private sealed class EnterpriseTermRow
    {
        public required string TermName { get; set; }

        public required List<string> Tables { get; set; }
    }
}
