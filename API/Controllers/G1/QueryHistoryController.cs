using API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace API.Controllers.G1;

/// <summary>
/// 查詢歷程控制器。
/// </summary>
[Route("api/g1/query-history")]
[ApiController]
[Authorize(Roles = "CLAF305")]
public class QueryHistoryController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public QueryHistoryController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>
    /// 取得查詢歷程檔案清單（顯示 query_desc）。
    /// </summary>
    [HttpGet("list")]
    public ResponseDataSchema<List<QueryHistoryItemResponse>> List()
    {
        return new ResponseDataSchema<List<QueryHistoryItemResponse>>
        {
            Code = -1,
            Msg = "Step1-Step5 功能已停用",
            Data = new List<QueryHistoryItemResponse>(),
            TraceId = HttpContext.TraceIdentifier
        };
    }

    /// <summary>
    /// 取得單一查詢歷程明細。
    /// </summary>
    [HttpGet("detail")]
    public ResponseDataSchema<QueryHistoryDetailResponse> Detail([FromQuery] string fileName)
    {
        return new ResponseDataSchema<QueryHistoryDetailResponse>
        {
            Code = -1,
            Msg = "Step1-Step5 功能已停用",
            Data = null,
            TraceId = HttpContext.TraceIdentifier
        };
    }

    private string GetCurrentUserHistoryDirectory()
    {
        var rootHistoryDirectory = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "query_history"));
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var safeUserId = SanitizePathSegment(userId);
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            throw new InvalidOperationException("無法取得登入使用者資訊");
        }

        return Path.Combine(rootHistoryDirectory, safeUserId);
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var buffer = value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        return new string(buffer);
    }

    private QueryHistoryDetailResponse ReadHistoryDetail(string filePath)
    {
        var content = System.IO.File.ReadAllText(filePath);
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            if (root.TryGetProperty("查詢摘要", out var summaryElement) &&
                root.TryGetProperty("查詢歷史", out var historiesElement) &&
                historiesElement.ValueKind == JsonValueKind.Array)
            {
                var histories = new List<QueryHistoryEntryResponse>();
                foreach (var item in historiesElement.EnumerateArray())
                {
                    var queryData = item.TryGetProperty("查詢資料", out var queryDataElement)
                        ? queryDataElement.GetString() ?? string.Empty
                        : string.Empty;
                    var queryResult = item.TryGetProperty("查詢結果", out var queryResultElement)
                        ? queryResultElement
                        : JsonDocument.Parse("null").RootElement;

                    histories.Add(new QueryHistoryEntryResponse
                    {
                        QueryData = queryData,
                        QueryResult = queryResult.Clone()
                    });
                }

                return new QueryHistoryDetailResponse
                {
                    QuerySummary = summaryElement.GetString() ?? "-",
                    Histories = histories
                };
            }

            if (root.TryGetProperty("查詢摘要", out var legacySummaryElement) &&
                root.TryGetProperty("查詢資料", out var legacyDataElement) &&
                legacyDataElement.ValueKind == JsonValueKind.Object)
            {
                var queries = legacyDataElement.TryGetProperty("查詢句", out var queriesElement) && queriesElement.ValueKind == JsonValueKind.Array
                    ? queriesElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                    : new List<string>();
                var results = legacyDataElement.TryGetProperty("查詢結果", out var resultsElement) && resultsElement.ValueKind == JsonValueKind.Array
                    ? resultsElement.EnumerateArray().Select(x => x.Clone()).ToList()
                    : new List<JsonElement>();

                var pairCount = Math.Min(queries.Count, results.Count);
                var histories = new List<QueryHistoryEntryResponse>();
                for (var i = 0; i < pairCount; i++)
                {
                    histories.Add(new QueryHistoryEntryResponse
                    {
                        QueryData = queries[i],
                        QueryResult = results[i]
                    });
                }

                return new QueryHistoryDetailResponse
                {
                    QuerySummary = legacySummaryElement.GetString() ?? "-",
                    Histories = histories
                };
            }
        }
        catch
        {
            // 舊純文字格式走下方相容解析
        }

        return ReadLegacyTextDetail(filePath);
    }

    private QueryHistoryDetailResponse ReadLegacyTextDetail(string filePath)
    {
        var summary = ReadQueryDesc(filePath);
        var lines = System.IO.File.ReadAllLines(filePath).ToList();

        var queryMarker = lines.FindIndex(x => x.Trim().Equals("Query:", StringComparison.OrdinalIgnoreCase));
        var querySpecMarker = lines.FindIndex(x => x.Trim().Equals("QuerySpec:", StringComparison.OrdinalIgnoreCase));
        var resultMarker = lines.FindIndex(x => x.Trim().Equals("Result:", StringComparison.OrdinalIgnoreCase));

        var query = string.Empty;
        if (queryMarker >= 0)
        {
            var end = querySpecMarker > queryMarker ? querySpecMarker : lines.Count;
            query = string.Join(Environment.NewLine, lines.Skip(queryMarker + 1).Take(end - queryMarker - 1)).Trim();
        }

        JsonElement resultElement;
        if (resultMarker >= 0)
        {
            var resultText = string.Join(Environment.NewLine, lines.Skip(resultMarker + 1)).Trim();
            try
            {
                resultElement = JsonDocument.Parse(resultText).RootElement.Clone();
            }
            catch
            {
                resultElement = JsonSerializer.SerializeToElement(resultText);
            }
        }
        else
        {
            resultElement = JsonDocument.Parse("null").RootElement.Clone();
        }

        return new QueryHistoryDetailResponse
        {
            QuerySummary = summary,
            Histories = new List<QueryHistoryEntryResponse>
            {
                new()
                {
                    QueryData = query,
                    QueryResult = resultElement
                }
            }
        };
    }

    private static string ReadQueryDesc(string filePath)
    {
        try
        {
            var rawContent = System.IO.File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(rawContent);
            if (document.RootElement.TryGetProperty("查詢摘要", out var summaryElement))
            {
                var summary = summaryElement.GetString();
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    return summary;
                }
            }
        }
        catch
        {
            // 舊格式檔案會走下方相容邏輯。
        }

        const int maxLines = 2000;
        var lines = System.IO.File.ReadLines(filePath).Take(maxLines).ToList();
        var markerIndex = lines.FindIndex(line =>
            line.Trim().Equals("query_desc:", StringComparison.OrdinalIgnoreCase) ||
            line.Trim().Equals("查詢摘要:", StringComparison.OrdinalIgnoreCase));
        if (markerIndex >= 0)
        {
            var buffer = new List<string>();
            for (var i = markerIndex + 1; i < lines.Count; i++)
            {
                var current = lines[i];
                if (current.Trim().Equals("QuerySpec:", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                buffer.Add(current);
            }

            var value = string.Join(Environment.NewLine, buffer).Trim();
            if (!string.IsNullOrWhiteSpace(value) && !value.Equals("-", StringComparison.Ordinal))
            {
                return value;
            }
        }

        var querySpecMarkerIndex = lines.FindIndex(line => line.Trim().Equals("QuerySpec:", StringComparison.OrdinalIgnoreCase));
        if (querySpecMarkerIndex < 0)
        {
            return "-";
        }

        var querySpecLines = new List<string>();
        for (var i = querySpecMarkerIndex + 1; i < lines.Count; i++)
        {
            var current = lines[i];
            if (current.Trim().Equals("Result:", StringComparison.OrdinalIgnoreCase) ||
                current.Trim().Equals("查詢結果:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            querySpecLines.Add(current);
        }

        // 不依賴完整 JSON，直接從 QuerySpec 區段提取 query_desc 行，避免檔案太長被截斷造成解析失敗。
        var queryDescLine = querySpecLines.FirstOrDefault(line => line.Contains("\"query_desc\"", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(queryDescLine))
        {
            var match = Regex.Match(queryDescLine, "\\\"query_desc\\\"\\s*:\\s*(.+?)\\s*,?$");
            if (match.Success)
            {
                var rawValue = match.Groups[1].Value.Trim();
                try
                {
                    var value = JsonSerializer.Deserialize<string>(rawValue);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
                catch
                {
                    var fallback = rawValue.Trim().Trim('"');
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        return fallback;
                    }
                }
            }
        }

        var querySpecText = string.Join(Environment.NewLine, querySpecLines).Trim();
        if (string.IsNullOrWhiteSpace(querySpecText))
        {
            return "-";
        }

        try
        {
            using var document = JsonDocument.Parse(querySpecText);
            if (!document.RootElement.TryGetProperty("query_desc", out var queryDescElement))
            {
                return "-";
            }

            var queryDesc = queryDescElement.GetString();
            return string.IsNullOrWhiteSpace(queryDesc) ? "-" : queryDesc;
        }
        catch
        {
            return "-";
        }
    }
}
