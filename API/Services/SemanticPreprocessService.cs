using System.Text.RegularExpressions;
using API.Attributes;
using API.Contracts;
using API.Models;

namespace API.Services;

/// <summary>
/// Step 1：語意預處理與個資遮罩。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class SemanticPreprocessService : ISemanticPreprocessService
{
    private static readonly Regex IdRegex = new("[A-Z][12]\\d{8}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PhoneRegex = new("09\\d{8}", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly HashSet<string> StopKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "請",
        "請問",
        "查",
        "查詢",
        "請查",
        "請查詢",
        "幫我",
        "我要",
        "給我",
        "列出",
        "顯示",
        "看",
        "看看",
        "一下",
        "列表",
        "資料",
        "明細"
    };
    private static readonly string[] DomainTerms =
    [
        "受理日期",
        "事故發生時間",
        "賠款金額",
        "總金額",
        "案件數",
        "賠案號碼",
        "賠案",
        "理賠",
        "賠款",
        "案件",
        "險種",
        "險別",
        "狀態",
        "結案",
        "金額",
        "數量",
        "件數",
        "筆數",
        "去年",
        "今年",
        "本月",
        "上個月"
    ];
    private static readonly string[] DomainTermsSorted =
        DomainTerms.OrderByDescending(x => x.Length).ToArray();
    private readonly Dictionary<string, string> _synonyms;
    private readonly Regex? _synonymRegex;

    public SemanticPreprocessService(IWebHostEnvironment environment)
    {
        _synonyms = LoadSynonyms(environment.ContentRootPath);
        _synonymRegex = BuildSynonymRegex(_synonyms);
    }

    public PreprocessResult Process(NaturalLanguageQueryRequest request)
    {
        var tokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalized = request.Query.Trim();
        normalized = Mask(normalized, IdRegex, "REDACTED_ID", tokenMap);
        normalized = Mask(normalized, PhoneRegex, "REDACTED_PHONE", tokenMap);
        normalized = Mask(normalized, EmailRegex, "REDACTED_EMAIL", tokenMap);

        var now = DateOnly.FromDateTime(DateTime.Today);
        DateOnly? startDate = null;
        DateOnly? endDate = null;

        if (normalized.Contains("去年", StringComparison.OrdinalIgnoreCase))
        {
            var lastYear = now.Year - 1;
            startDate = new DateOnly(lastYear, 1, 1);
            endDate = new DateOnly(lastYear, 12, 31);
            normalized = normalized.Replace("去年", $"{startDate:yyyy-MM-dd} 到 {endDate:yyyy-MM-dd}", StringComparison.OrdinalIgnoreCase);
        }
        else if (normalized.Contains("今年", StringComparison.OrdinalIgnoreCase))
        {
            startDate = new DateOnly(now.Year, 1, 1);
            endDate = new DateOnly(now.Year, 12, 31);
            normalized = normalized.Replace("今年", $"{startDate:yyyy-MM-dd} 到 {endDate:yyyy-MM-dd}", StringComparison.OrdinalIgnoreCase);
        }
        else if (normalized.Contains("上個月", StringComparison.OrdinalIgnoreCase))
        {
            var firstDay = new DateOnly(now.Year, now.Month, 1).AddMonths(-1);
            startDate = firstDay;
            endDate = firstDay.AddMonths(1).AddDays(-1);
            normalized = normalized.Replace("上個月", $"{startDate:yyyy-MM-dd} 到 {endDate:yyyy-MM-dd}", StringComparison.OrdinalIgnoreCase);
        }
        else if (normalized.Contains("本月", StringComparison.OrdinalIgnoreCase) || normalized.Contains("這個月", StringComparison.OrdinalIgnoreCase))
        {
            startDate = new DateOnly(now.Year, now.Month, 1);
            endDate = startDate.Value.AddMonths(1).AddDays(-1);
        }
        else if (normalized.Contains("今天", StringComparison.OrdinalIgnoreCase))
        {
            startDate = now;
            endDate = now;
        }
        else if (normalized.Contains("近30天", StringComparison.OrdinalIgnoreCase))
        {
            endDate = now;
            startDate = now.AddDays(-29);
        }

        normalized = ApplySynonyms(normalized);
        normalized = NormalizeDateRangeText(normalized, ref startDate, ref endDate);

        var keywords = ExtractKeywords(normalized);
        return new PreprocessResult
        {
            OriginalQuestion = request.Query.Trim(),
            NormalizedQuestion = normalized,
            PiiTokenMap = tokenMap,
            StartDate = startDate,
            EndDate = endDate,
            Keywords = keywords,
            Entities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private string ApplySynonyms(string text)
    {
        if (_synonymRegex is null)
        {
            return text;
        }

        // Use one-pass regex replacement so newly replaced text will not be matched again.
        return _synonymRegex.Replace(text, match => _synonyms[match.Value]);
    }

    private static Dictionary<string, string> LoadSynonyms(string contentRootPath)
    {
        var candidates = new List<string>
        {
            Path.Combine(contentRootPath, "..", "doc", "synonyms.md"),
            Path.Combine(contentRootPath, "doc", "synonyms.md")
        };

        var current = new DirectoryInfo(contentRootPath);
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            candidates.Add(Path.Combine(current.FullName, "doc", "synonyms.md"));
            current = current.Parent;
        }

        candidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filePath = candidates.FirstOrDefault(File.Exists);
        if (filePath is null)
        {
            throw new FileNotFoundException($"Synonyms file not found. Expected one of: {string.Join(", ", candidates)}");
        }

        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var parts = trimmed.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                continue;
            }

            dictionary[parts[0]] = parts[1];
        }

        if (dictionary.Count == 0)
        {
            throw new InvalidOperationException($"Synonyms file is empty or invalid: {filePath}");
        }

        return dictionary;
    }

    private static Regex? BuildSynonymRegex(Dictionary<string, string> synonyms)
    {
        var patterns = synonyms.Keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .OrderByDescending(key => key.Length)
            .ThenBy(key => key, StringComparer.Ordinal)
            .Select(Regex.Escape)
            .ToArray();

        if (patterns.Length == 0)
        {
            return null;
        }

        return new Regex(string.Join("|", patterns), RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private static string NormalizeDateRangeText(string text, ref DateOnly? startDate, ref DateOnly? endDate)
    {
        var rangePattern = new Regex("(\\d{4}[-/]\\d{2}[-/]\\d{2})\\s*(到|至|-)\\s*(\\d{4}[-/]\\d{2}[-/]\\d{2})", RegexOptions.Compiled);
        var match = rangePattern.Match(text);
        if (!match.Success)
        {
            return text;
        }

        if (DateOnly.TryParse(match.Groups[1].Value.Replace('/', '-'), out var start) &&
            DateOnly.TryParse(match.Groups[3].Value.Replace('/', '-'), out var end))
        {
            startDate = start;
            endDate = end;
            return rangePattern.Replace(text, $"{start:yyyy-MM-dd} 到 {end:yyyy-MM-dd}");
        }

        return text;
    }

    private static string Mask(string input, Regex regex, string tokenPrefix, Dictionary<string, string> map)
    {
        var index = map.Count + 1;
        return regex.Replace(input, match =>
        {
            var token = $"[{tokenPrefix}_{index++}]";
            map[token] = match.Value;
            return token;
        });
    }

    private static List<string> ExtractKeywords(string text)
    {
        var separators = new[] { ' ', '\t', ',', '，', '。', '.', ':', '：', ';', '；', '/', '\\' };
        var coarseTokens = text
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(SegmentKeywordToken)
            .Where(x => x.Length >= 2)
            .Where(x => !StopKeywords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        return coarseTokens;
    }

    private static IEnumerable<string> SegmentKeywordToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            yield break;
        }

        if (!token.Any(IsCjkCharacter))
        {
            yield return token;
            yield break;
        }

        var normalizedToken = token.Trim();
        var matched = DomainTermsSorted
            .Where(term => normalizedToken.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matched.Count > 0)
        {
            foreach (var keyword in matched)
            {
                yield return keyword;
            }

            yield break;
        }

        if (normalizedToken.Length >= 2)
        {
            yield return normalizedToken;
        }
    }

    private static bool IsCjkCharacter(char c)
    {
        return c >= '\u4e00' && c <= '\u9fff';
    }
}
