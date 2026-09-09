using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using API.Attributes;
using API.Contracts;
using UglyToad.PdfPig;
using Microsoft.AspNetCore.Hosting;

namespace API.Services;

/// <summary>
/// 文件處理服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public class DocumentPipelineService : IDocumentPipelineService
{
    private static readonly string[] TitleCandidates =
    [
        "車險理賠申請書",
        "理賠申請書",
        "汽車險理賠申請書",
        "和解書",
        "醫療收據"
    ];

    private static readonly string[] KnownFieldLabels =
    [
        "賠案號碼",
        "賠案號碼",
        "申請人",
        "被保險人",
        "駕駛人",
        "駕駛人姓名",
        "車牌號碼",
        "車號",
        "車號",
        "保單號碼",
        "保單號",
        "保險號碼",
        "保險期間",
        "事故日期",
        "事故發生日",
        "發生日期",
        "事故地點",
        "聯絡電話",
        "行動電話",
        "電話",
        "聯絡人TEL",
        "E-mail",
        "住址",
        "處理單位",
        "報案時間",
        "生日",
        "性別",
        "婚姻別",
        "放置地點",
        "對造姓名",
        "駕照號碼",
        "身分證字號",
        "證號",
        "賠案號碼",
        "案號"
    ];

    private static readonly string[] InjuryTableHeaders =
    [
        "類型",
        "身分證字號",
        "出生日期",
        "電話",
        "醫療院所",
        "傷勢"
    ];

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const string UploadedFileManifestName = "_manifest.json";

    private readonly IWebHostEnvironment _environment;

    public DocumentPipelineService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<string> ExtractPdfAsStructuredJsonAsync(
        IFormFile file,
        string? userId,
        string? sessionId,
        string? llmProvider,
        string? documentType,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only PDF is supported.");
        }

        await using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        using var document = PdfDocument.Open(memoryStream);
        var pages = document.GetPages()
            .Select(page => new PdfPageTextPayload
            {
                PageNo = page.Number,
                Text = (page.Text ?? string.Empty).Trim()
            })
            .Where(page => !string.IsNullOrWhiteSpace(page.Text))
            .ToList();

        var payload = new PdfTextPayload
        {
            FileName = file.FileName,
            Extension = extension,
            TotalPages = pages.Count,
            FullText = string.Join(Environment.NewLine + Environment.NewLine, pages.Select(page => page.Text)),
            Pages = pages
        };

        var normalizedDocumentType = NormalizeDocumentType(documentType);
        var schemaInstruction = BuildSchemaInstruction(normalizedDocumentType);
        var step1Structured = BuildStep1StructuredData(payload.FullText);
        var parsedData = BuildFallbackStructuredData(payload.FullText, normalizedDocumentType, step1Structured);
        var parseError = "已停用 LLM；使用規則式抽取。";

        _ = schemaInstruction;
        _ = parsedData;
        _ = parseError;

        var response = new UploadedDocumentExtractedPayload
        {
            Title = step1Structured.Title,
            RawText = payload.FullText
        };

        var fileBytes = memoryStream.ToArray();
        SaveUploadedQuerySpecFile(file.FileName, fileBytes, userId, sessionId, step1Structured.Title, cancellationToken);

        return JsonSerializer.Serialize(response, JsonSerializerOptions);
    }

    private void SaveUploadedQuerySpecFile(string originalFileName, byte[] fileBytes, string? userId, string? sessionId, string? structuredTitle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var safeUserId = SanitizePathSegment(userId);
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            safeUserId = "anonymous";
        }

        var safeSessionId = SanitizePathSegment(sessionId);
        if (string.IsNullOrWhiteSpace(safeSessionId))
        {
            safeSessionId = "unassigned";
        }

        var querySpecRootDirectory = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "query_spec"));
        var sessionDirectory = Path.Combine(querySpecRootDirectory, safeUserId, safeSessionId);
        Directory.CreateDirectory(sessionDirectory);

        var safeFileName = BuildStoredFileName(originalFileName);
        var filePath = Path.Combine(sessionDirectory, safeFileName);
        File.WriteAllBytes(filePath, fileBytes);

        UpsertUploadedFileManifest(sessionDirectory, safeFileName, originalFileName, structuredTitle);
    }

    private static void UpsertUploadedFileManifest(string sessionDirectory, string storedFileName, string originalFileName, string? structuredTitle)
    {
        var manifestPath = Path.Combine(sessionDirectory, UploadedFileManifestName);
        var manifest = ReadUploadedFileManifest(manifestPath);
        var title = (structuredTitle ?? string.Empty).Trim();

        var existing = manifest.Files.FirstOrDefault(item =>
            string.Equals(item.StoredFileName, storedFileName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            manifest.Files.Add(new UploadedFileManifestItem
            {
                StoredFileName = storedFileName,
                OriginalFileName = (originalFileName ?? string.Empty).Trim(),
                Title = title,
                UploadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            });
        }
        else
        {
            existing.OriginalFileName = (originalFileName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                existing.Title = title;
            }
        }

        var manifestJson = JsonSerializer.Serialize(manifest, JsonSerializerOptions);
        File.WriteAllText(manifestPath, manifestJson);
    }

    private static UploadedFileManifestDocument ReadUploadedFileManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return new UploadedFileManifestDocument();
        }

        try
        {
            var content = File.ReadAllText(manifestPath);
            var parsed = JsonSerializer.Deserialize<UploadedFileManifestDocument>(content);
            return parsed ?? new UploadedFileManifestDocument();
        }
        catch
        {
            return new UploadedFileManifestDocument();
        }
    }

    private static string BuildStoredFileName(string? originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        var baseFileName = Path.GetFileNameWithoutExtension(originalFileName ?? string.Empty);
        var safeBaseFileName = SanitizePathSegment(baseFileName);
        if (string.IsNullOrWhiteSpace(safeBaseFileName))
        {
            safeBaseFileName = "uploaded-file";
        }

        var safeExtension = string.IsNullOrWhiteSpace(extension) ? ".pdf" : extension;
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        return $"{timestamp}_{safeBaseFileName}{safeExtension}";
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

    private static string BuildSchemaInstruction(string normalizedDocumentType)
    {
        return normalizedDocumentType switch
        {
            "medicalReceipt" => "MedicalReceiptData schema: { hospital_name: string, treatment_date: string, total_amount: int }",
            "settlementAgreement" => "SettlementData schema: { accident_date: string, total_amount: int, has_waived_lawsuit: bool }",
            "claimApplication" => "ClaimApplicationData schema: { applicant_name: string, id_number: string }",
            _ => "Unknown schema: return {}"
        };
    }

    private static string NormalizeDocumentType(string? documentType)
    {
        var normalized = (documentType ?? string.Empty).Trim();
        if (normalized.Equals("medicalReceipt", StringComparison.OrdinalIgnoreCase))
        {
            return "medicalReceipt";
        }

        if (normalized.Equals("settlementAgreement", StringComparison.OrdinalIgnoreCase))
        {
            return "settlementAgreement";
        }

        if (normalized.Equals("claimApplication", StringComparison.OrdinalIgnoreCase))
        {
            return "claimApplication";
        }

        return "unknown";
    }

    private static object BuildFallbackStructuredData(
        string fullText,
        string normalizedDocumentType,
        Step1StructuredPayload step1Structured)
    {
        var text = Regex.Replace(fullText ?? string.Empty, "\\s+", " ").Trim();

        string? applicantName = FindFirstGroupValue(text, @"(?:申請人|被保險人)[:：]?\s*([\u4e00-\u9fa5A-Za-z]{2,20})");
        string? idNumber = FindFirstGroupValue(text, @"\b([A-Z][12][0-9]{8})\b");
        string? policyNo = FindFirstGroupValue(text, @"(?:保單號碼|保單號|保險號碼)[:：]?\s*([A-Z0-9\-]{4,30})");
        string? plateNo = FindFirstGroupValue(text, @"(?:車牌號碼|車牌|車號)[:：]?\s*([A-Z0-9\-]{4,12})");
        string? accidentDate = FindFirstGroupValue(text, @"(?:事故日期|事故發生日|發生日期)[:：]?\s*([0-9]{2,4}[\-/年\.][0-9]{1,2}[\-/月\.][0-9]{1,2}(?:日)?)");
        string? accidentLocation = ExtractAccidentLocation(fullText);
        string? phone = FindFirstGroupValue(text, @"\b(0[0-9]{8,10})\b");

        var extractedFields = new Dictionary<string, string?>
        {
            ["applicant_name"] = applicantName,
            ["id_number"] = idNumber,
            ["policy_no"] = policyNo,
            ["plate_no"] = plateNo,
            ["accident_date"] = accidentDate,
            ["accident_location"] = accidentLocation,
            ["phone"] = phone
        };

        if (step1Structured.Fields.Count == 0)
        {
            var fallbackFields = new List<Step1FieldPayload>();
            AddFieldIfNotEmpty(fallbackFields, "申請人", applicantName);
            AddFieldIfNotEmpty(fallbackFields, "身分證字號", idNumber);
            AddFieldIfNotEmpty(fallbackFields, "保單號碼", policyNo);
            AddFieldIfNotEmpty(fallbackFields, "車牌號碼", plateNo);
            AddFieldIfNotEmpty(fallbackFields, "事故日期", accidentDate);
            AddFieldIfNotEmpty(fallbackFields, "事故地點", accidentLocation);
            AddFieldIfNotEmpty(fallbackFields, "電話", phone);

            if (fallbackFields.Count > 0)
            {
                step1Structured.Fields = fallbackFields;
            }
        }

        var nonEmptyFieldCount = extractedFields.Values.Count(value => !string.IsNullOrWhiteSpace(value));
        if (nonEmptyFieldCount == 0)
        {
            return new Dictionary<string, object?>
            {
                ["document_type"] = normalizedDocumentType,
                ["source"] = "fallback_regex",
                ["title"] = step1Structured.Title,
                ["fields"] = step1Structured.Fields,
                ["tables"] = step1Structured.Tables,
                ["extracted_fields"] = new Dictionary<string, string?>(),
                ["full_text_excerpt"] = text.Length > 300 ? text[..300] : text
            };
        }

        return new Dictionary<string, object?>
        {
            ["document_type"] = normalizedDocumentType,
            ["source"] = "fallback_regex",
            ["title"] = step1Structured.Title,
            ["fields"] = step1Structured.Fields,
            ["tables"] = step1Structured.Tables,
            ["extracted_fields"] = extractedFields
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value)
        };
    }

    private static Step1StructuredPayload BuildStep1StructuredData(string fullText)
    {
        var normalizedText = Regex.Replace(fullText ?? string.Empty, "\\s+", " ").Trim();
        var title = ExtractTitle(normalizedText);
        var fields = ExtractFields(fullText ?? string.Empty);
        EnrichEssentialFields(fullText, fields);
        var tables = ExtractTableElements(normalizedText);

        return new Step1StructuredPayload
        {
            Title = title,
            Fields = fields,
            Tables = tables
        };
    }

    private static void EnrichEssentialFields(string fullText, List<Step1FieldPayload> fields)
    {
        if (fields is null)
        {
            return;
        }

        var compact = Regex.Replace(fullText ?? string.Empty, "\\s+", " ").Trim();
        var accidentDate = FindFirstGroupValue(compact, @"(?:事故日期|事故發生日|發生日期)\s*[:：]?\s*([0-9]{2,4}[\-/年\.][0-9]{1,2}[\-/月\.][0-9]{1,2}(?:日)?(?:\s*[0-9]{1,2}:[0-9]{2})?)");
        var accidentLocation = ExtractAccidentLocation(fullText);

        AddFieldIfMissing(fields, "事故日期", accidentDate);
        AddFieldIfMissing(fields, "事故地點", accidentLocation);
    }

    private static void AddFieldIfMissing(List<Step1FieldPayload> fields, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalizedValue = value.Trim().Trim('：', ':', '，', ',', ';', '；', '.', '。');
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return;
        }

        var exists = fields.Any(item =>
            string.Equals((item.Label ?? string.Empty).Trim(), label, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace((item.Value ?? string.Empty).Trim()));

        if (exists)
        {
            return;
        }

        fields.Add(new Step1FieldPayload
        {
            Label = label,
            Value = normalizedValue
        });
    }

    private static string ExtractTitle(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return "(無標題)";
        }

        foreach (var candidate in TitleCandidates)
        {
            if (normalizedText.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        var maxLength = Math.Min(30, normalizedText.Length);
        return normalizedText[..maxLength];
    }

    private static List<Step1FieldPayload> ExtractFields(string originalText)
    {
        var fields = new List<Step1FieldPayload>();
        if (string.IsNullOrWhiteSpace(originalText))
        {
            return fields;
        }

        var compactText = originalText
            .Replace("\u3000", " ")
            .Replace("｜", "|")
            .Replace("﹕", ":");
        // 某些 PDF 會將「欄位:」與值斷成兩行，先攤平成同一行避免值被誤判為空。
        compactText = Regex.Replace(compactText, @"([:：])\s*[\r\n]+\s*", "$1 ", RegexOptions.Compiled);
        var uniqueLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 先抓常見的「欄位: 值」片段，這對表格型 PDF 命中率最高。
        var pairRegex = new Regex(
            @"(?<label>[\u4e00-\u9fa5A-Za-z][\u4e00-\u9fa5A-Za-z0-9_\-\s]{0,20})\s*[:：]\s*(?<value>.*?)(?=(?:[\u4e00-\u9fa5A-Za-z][\u4e00-\u9fa5A-Za-z0-9_\-\s]{0,20}\s*[:：])|[\r\n]|$)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        foreach (Match match in pairRegex.Matches(compactText))
        {
            var rawLabel = Regex.Replace(match.Groups["label"].Value ?? string.Empty, "\\s+", string.Empty).Trim();
            var canonicalLabel = ResolveCanonicalLabel(rawLabel);
            if (string.IsNullOrWhiteSpace(canonicalLabel) || !uniqueLabels.Add(canonicalLabel))
            {
                continue;
            }

            var value = Regex.Replace(match.Groups["value"].Value ?? string.Empty, "\\s+", " ").Trim();
            value = value.Trim('：', ':', '，', ',', ';', '；', '.', '。');
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.Length > 80)
            {
                value = value[..80].Trim();
            }

            fields.Add(new Step1FieldPayload
            {
                Label = canonicalLabel,
                Value = value
            });
        }

        if (fields.Count > 0)
        {
            return fields;
        }

        // 若沒有冒號格式，再回退到模糊標籤擷取。
        var labelPatternMap = KnownFieldLabels
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(label => label.Length)
            .ToDictionary(label => label, BuildFlexibleLabelPattern);

        var allLabelAlternation = string.Join("|", labelPatternMap.Values);

        foreach (var (canonicalLabel, flexiblePattern) in labelPatternMap)
        {
            var fieldRegex = new Regex(
                $@"(?:^|[\r\n\s])(?<label>{flexiblePattern})\s*[:：]?\s*(?<value>.*?)(?=(?:{allLabelAlternation})\s*[:：]?|[\r\n]|$)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var match = fieldRegex.Match(compactText);
            if (!match.Success || !uniqueLabels.Add(canonicalLabel))
            {
                continue;
            }

            var value = Regex.Replace(match.Groups["value"].Value ?? string.Empty, "\\s+", " ").Trim();
            value = value.Trim('：', ':', '，', ',', ';', '；', '.', '。');
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.Length > 80)
            {
                value = value[..80].Trim();
            }

            fields.Add(new Step1FieldPayload
            {
                Label = canonicalLabel,
                Value = value
            });
        }

        return fields;
    }

    private static string ResolveCanonicalLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var normalized = label.Replace(" ", string.Empty).Trim();
        foreach (var candidate in KnownFieldLabels.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (normalized.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
                candidate.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return normalized;
    }

    private static void AddFieldIfNotEmpty(List<Step1FieldPayload> fields, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        fields.Add(new Step1FieldPayload
        {
            Label = label,
            Value = value.Trim()
        });
    }

    private static string BuildFlexibleLabelPattern(string label)
    {
        var chars = label.Trim().Select(ch => Regex.Escape(ch.ToString()));
        return string.Join("\\s*", chars);
    }

    private static List<Step1ElementPayload> ExtractTableElements(string normalizedText)
    {
        var rows = new List<List<string>>();
        var rowRegex = new Regex(
            @"(?<type>傷者|死者)\s*(?<id>[A-Z][0-9]{9})\s*(?<birth>[0-9]{2,4}/[0-9]{1,2}/[0-9]{1,2})\s*(?<phone>0[0-9]{8,10})\s*(?<hospital>[\u4e00-\u9fa5A-Za-z0-9]{1,20}醫院)\s*(?<injury>.*?)(?=(傷者|死者|$))",
            RegexOptions.Compiled);

        foreach (Match match in rowRegex.Matches(normalizedText))
        {
            var injury = Regex.Replace(match.Groups["injury"].Value ?? string.Empty, "\\s+", " ").Trim();
            if (injury.Length > 40)
            {
                injury = injury[..40].Trim();
            }

            rows.Add(
            [
                match.Groups["type"].Value.Trim(),
                match.Groups["id"].Value.Trim(),
                match.Groups["birth"].Value.Trim(),
                match.Groups["phone"].Value.Trim(),
                match.Groups["hospital"].Value.Trim(),
                injury
            ]);
        }

        if (rows.Count == 0)
        {
            return [];
        }

        return
        [
            new Step1ElementPayload
            {
                Type = "table",
                Headers = InjuryTableHeaders.ToList(),
                Rows = rows
            }
        ];
    }

    private static string? FindFirstGroupValue(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        var value = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ExtractAccidentLocation(string? originalText)
    {
        if (string.IsNullOrWhiteSpace(originalText))
        {
            return null;
        }

        // 優先以逐行模式擷取「事故地點: xxx」。
        var lineMatch = Regex.Match(
            originalText,
            @"(?:^|[\r\n])\s*事故地點\s*[:：]\s*(?<value>[^\r\n]+)",
            RegexOptions.Multiline);

        if (lineMatch.Success)
        {
            var lineValue = lineMatch.Groups["value"].Value;
            var normalizedLine = lineValue.Split(["報案時間", "事故情形"], StringSplitOptions.None)[0].Trim();
            if (!string.IsNullOrWhiteSpace(normalizedLine))
            {
                return normalizedLine;
            }
        }

        // 回退：OCR 可能把整份文件壓成單行，改以關鍵欄位邊界截斷。
        var compact = Regex.Replace(originalText, "\\s+", " ").Trim();
        var compactMatch = Regex.Match(
            compact,
            @"事故地點\s*[:：]\s*(?<value>.+?)(?=(?:報案時間|事故情形|$))",
            RegexOptions.IgnoreCase);

        if (!compactMatch.Success)
        {
            return null;
        }

        var compactValue = compactMatch.Groups["value"].Value.Trim();
        if (string.IsNullOrWhiteSpace(compactValue))
        {
            return null;
        }

        return compactValue;
    }

    private sealed class PdfTextPayload
    {
        public required string FileName { get; set; }

        public required string Extension { get; set; }

        public required int TotalPages { get; set; }

        public required string FullText { get; set; }

        public required List<PdfPageTextPayload> Pages { get; set; }
    }

    private sealed class UploadedDocumentExtractedPayload
    {
        public required string Title { get; set; }

        public required string RawText { get; set; }
    }

    private sealed class PdfPageTextPayload
    {
        public required int PageNo { get; set; }

        public required string Text { get; set; }
    }

    private sealed class Step1Payload
    {
        public required string ExtractedText { get; set; }

        public required Step1StructuredPayload Structured { get; set; }
    }

    private sealed class Step1StructuredPayload
    {
        public required string Title { get; set; }

        public required List<Step1FieldPayload> Fields { get; set; }

        public required List<Step1ElementPayload> Tables { get; set; }
    }

    private sealed class Step1FieldPayload
    {
        public required string Label { get; set; }

        public required string Value { get; set; }
    }

    private sealed class Step1ElementPayload
    {
        public required string Type { get; set; }

        public required List<string> Headers { get; set; }

        public required List<List<string>> Rows { get; set; }
    }

    private sealed class UploadedFileManifestDocument
    {
        public List<UploadedFileManifestItem> Files { get; set; } = [];
    }

    private sealed class UploadedFileManifestItem
    {
        public required string StoredFileName { get; set; }

        public required string OriginalFileName { get; set; }

        public string Title { get; set; } = string.Empty;

        public string UploadedAt { get; set; } = string.Empty;
    }
}
