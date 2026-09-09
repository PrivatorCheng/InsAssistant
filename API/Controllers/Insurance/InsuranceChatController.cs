using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Infrastructure;
using API.Contracts;
using API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Insurance;

/// <summary>
/// 保險教練多輪對話控制器。
/// </summary>
[Route("api/insurance/chat")]
[ApiController]
[AllowAnonymous]
public class InsuranceChatController : ControllerBase
{
    private const string PromptTemplateModeSalesAssistant = "salesAssistant";
    private const string PromptTemplateModeClaimAssistant = "claimAssistant";
    private const string ImageUploadLlmMimeType = "image/jpeg";
    private const string UploadedFileManifestName = "_manifest.json";
    private const string PolicySampleFileRelativePath = "doc/SampleData/Policy.json";

    private static readonly object HistoryFileLock = new();
    private static readonly ConcurrentDictionary<string, string> SessionHistoryFileMap = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions HistoryJsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions ManifestJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly JsonSerializerOptions PolicyJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IInsuranceChatService _insuranceChatService;
    private readonly IInsuranceChatCompletionService _chatCompletionService;
    private readonly IImageOcrService _imageOcrService;
    private readonly ILlmLogService _llmLogService;
    private readonly IWebHostEnvironment _environment;
    private readonly CustomLogger _logger;
    private readonly string _imagePromptTemplate;

    public InsuranceChatController(
        IInsuranceChatService insuranceChatService,
        IInsuranceChatCompletionService chatCompletionService,
        IImageOcrService imageOcrService,
        ILlmLogService llmLogService,
        IWebHostEnvironment environment,
        ILogger<InsuranceChatController> logger)
    {
        _insuranceChatService = insuranceChatService;
        _chatCompletionService = chatCompletionService;
        _imageOcrService = imageOcrService;
        _llmLogService = llmLogService;
        _environment = environment;
        _logger = logger.ToCustomLogger();
        _imagePromptTemplate = LoadPromptTemplate(environment.ContentRootPath, "Prompt_Image.txt");
    }

    /// <summary>
    /// 上傳影像（base64）呼叫 LLM 分析，並回傳文字結果。
    /// </summary>
    [HttpPost("image-upload")]
    public async Task<IActionResult> AnalyzeImageUpload([FromBody] ImageUploadChatRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { msg = "請提供請求內容。" });
        }

        var base64Data = (request.Base64Data ?? string.Empty).Trim();
        var mimeType = (request.MimeType ?? string.Empty).Trim();
        var fileName = (request.FileName ?? "image").Trim();
        var normalizedFileName = string.IsNullOrWhiteSpace(fileName) ? "image" : fileName;
        var sessionId = (request.SessionId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(base64Data))
        {
            return BadRequest(new { msg = "影像內容不可為空。" });
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { msg = "請先建立對話工作階段。" });
        }

        if (!string.IsNullOrWhiteSpace(mimeType)
            && !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { msg = "僅支援影像格式。" });
        }

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            return BadRequest(new { msg = "影像 base64 格式錯誤。" });
        }

        try
        {
            var requestTime = DateTime.Now;
            var userMessage = BuildImageUploadUserMessage(mimeType, normalizedFileName);
            var completion = await _chatCompletionService.ExecuteWithInlineImageAsync(
                request.LlmProvider,
                _imagePromptTemplate,
                userMessage,
                imageBytes,
                ImageUploadLlmMimeType,
                cancellationToken);

            var imageTitle = ExtractImageUploadTitle(completion.Content, normalizedFileName);
            SaveUploadedImageQuerySpecFile(sessionId, normalizedFileName, imageTitle, imageBytes);

            var safeUserId = GetSafeUserId();
            var logSessionId = string.IsNullOrWhiteSpace(safeUserId)
                ? "image-upload-anonymous"
                : $"image-upload-{safeUserId}";
            var responseTime = DateTime.Now;

            _ = await _llmLogService.LogLlmCallAsync(
                logSessionId,
                completion.Provider ?? "Unknown",
                completion.Model ?? "Unknown",
                completion.InputToken,
                completion.OutputToken,
                completion.CacheLength,
                completion.DurationMs,
                _imagePromptTemplate.Length + userMessage.Length,
                completion.Content.Length,
                completion.IsSuccess,
                requestTime,
                responseTime,
                cancellationToken);

            return Ok(new ImageUploadChatResponse
            {
                Result = completion.Content,
                DocumentTitle = imageTitle,
                LlmProvider = completion.Provider,
                LlmModel = completion.Model
            });
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "insurance chat image upload failed. fileName: {}, mimeType: {}", fileName, mimeType);
            return StatusCode(StatusCodes.Status500InternalServerError, new { msg = "影像分析失敗。" });
        }
    }

    /// <summary>
    /// 上傳影像（base64）使用 Tesseract OCR 掃描，並回傳文字結果。
    /// </summary>
    [HttpPost("image-upload-tesseract")]
    public async Task<IActionResult> AnalyzeImageUploadByTesseract([FromBody] ImageUploadChatRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { msg = "請提供請求內容。" });
        }

        var base64Data = (request.Base64Data ?? string.Empty).Trim();
        var mimeType = (request.MimeType ?? string.Empty).Trim();
        var fileName = (request.FileName ?? "image").Trim();
        var normalizedFileName = string.IsNullOrWhiteSpace(fileName) ? "image" : fileName;
        var sessionId = (request.SessionId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(base64Data))
        {
            return BadRequest(new { msg = "影像內容不可為空。" });
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest(new { msg = "請先建立對話工作階段。" });
        }

        if (!string.IsNullOrWhiteSpace(mimeType)
            && !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { msg = "僅支援影像格式。" });
        }

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(base64Data);
        }
        catch (FormatException)
        {
            return BadRequest(new { msg = "影像 base64 格式錯誤。" });
        }

        try
        {
            var ocrText = await _imageOcrService.ExtractTextAsync(
                imageBytes,
                normalizedFileName,
                mimeType,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(ocrText))
            {
                return BadRequest(new { msg = "影像掃描未取得文字。" });
            }

            var documentTitle = normalizedFileName;
            SaveUploadedImageQuerySpecFile(sessionId, normalizedFileName, documentTitle, imageBytes);

            return Ok(new ImageUploadChatResponse
            {
                Result = ocrText,
                DocumentTitle = documentTitle,
                LlmProvider = "Tesseract",
                LlmModel = "Tesseract-OCR"
            });
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "insurance chat image upload tesseract failed. fileName: {}, mimeType: {}", fileName, mimeType);
            var message = string.IsNullOrWhiteSpace(exception.Message)
                ? "影像掃描失敗。"
                : $"影像掃描失敗：{exception.Message}";
            return StatusCode(StatusCodes.Status500InternalServerError, new { msg = message });
        }
    }

    /// <summary>
    /// 送出對話訊息並取得 AI 回覆。
    /// </summary>
    [HttpPost]
    public async Task<SimpleChatResponse> Chat([FromBody] SimpleChatRequest request, CancellationToken cancellationToken)
    {
        var response = await _insuranceChatService.ChatAsync(request, cancellationToken);
        if (IsConversationHistoryMode(request))
        {
            var historyFileName = TrySaveConversationHistory(
                request.SessionId,
                request.UserMessage,
                response.Reply,
                response.CustName,
                response.CustTagNo,
                response.InsureTypeList,
                response.TodoInfo,
                response.TodoDocument);

            if (!string.IsNullOrWhiteSpace(historyFileName))
            {
                var safeUserId = GetSafeUserIdOrAnonymous();
                response.HistoryFileName = historyFileName;
                response.UploadedFiles = ListUploadedFiles(safeUserId, historyFileName)
                    .Select(item => new SimpleChatUploadedFileItem
                    {
                        StoredFileName = item.StoredFileName,
                        OriginalFileName = item.OriginalFileName,
                        Title = item.Title
                    })
                    .ToList();
            }
        }

        return response;
    }

    /// <summary>
    /// 取得保單清單（由 doc/SampleData/Policy.json 讀取）。
    /// </summary>
    [HttpGet("policies")]
    public IActionResult GetPolicies()
    {
        try
        {
            var policyFilePath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", PolicySampleFileRelativePath));
            if (!System.IO.File.Exists(policyFilePath))
            {
                _logger.Info("policy sample file not found. path: {}", policyFilePath);
                return StatusCode(StatusCodes.Status500InternalServerError, new { msg = "保單資料檔不存在。" });
            }

            var json = System.IO.File.ReadAllText(policyFilePath, Encoding.UTF8);
            var policyList = JsonSerializer.Deserialize<List<PolicyListItem>>(json, PolicyJsonSerializerOptions) ?? [];

            foreach (var policy in policyList)
            {
                policy.PolicyNo = (policy.PolicyNo ?? string.Empty).Trim();
                policy.InsuredName = (policy.InsuredName ?? string.Empty).Trim();
                policy.PlateNo = (policy.PlateNo ?? string.Empty).Trim();
                policy.InsureTypeList = (policy.InsureTypeList ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToList();
            }

            return Ok(policyList);
        }
        catch (JsonException exception)
        {
            _logger.Error(exception, "policy sample file parse failed. relativePath: {}", PolicySampleFileRelativePath);
            return StatusCode(StatusCodes.Status500InternalServerError, new { msg = "保單資料格式錯誤。" });
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "load policy sample failed. relativePath: {}", PolicySampleFileRelativePath);
            return StatusCode(StatusCodes.Status500InternalServerError, new { msg = "讀取保單資料失敗。" });
        }
    }

    /// <summary>
    /// 重設指定工作階段對話紀錄。
    /// </summary>
    [HttpPost("reset/{sessionId}")]
    public IActionResult ResetChat([FromRoute] string sessionId)
    {
        _insuranceChatService.ResetSession(sessionId);
        return Ok();
    }

    /// <summary>
    /// 取得登入使用者的對話歷史清單。
    /// </summary>
    [HttpGet("histories")]
    public IActionResult GetHistories()
    {
        if (!IsConversationHistoryModeRequested())
        {
            return Ok(Array.Empty<ConversationHistoryListItem>());
        }

        var safeUserId = GetSafeUserIdOrAnonymous();

        var userDirectory = Path.Combine(GetQueryHistoryRootDirectory(), safeUserId);
        if (!Directory.Exists(userDirectory))
        {
            return Ok(Array.Empty<ConversationHistoryListItem>());
        }

        var items = Directory.EnumerateFiles(userDirectory, "*.txt")
            .OrderByDescending(Path.GetFileName)
            .Select(filePath =>
            {
                var document = ReadHistoryDocument(filePath);
                var fileName = Path.GetFileName(filePath);
                return new ConversationHistoryListItem
                {
                    FileName = fileName,
                    Description = BuildHistoryDescription(document, fileName)
                };
            })
            .ToList();

        return Ok(items);
    }

    /// <summary>
    /// 載入指定對話歷史，並回灌為可續聊的工作階段。
    /// </summary>
    [HttpGet("histories/{fileName}")]
    public IActionResult GetHistory([FromRoute] string fileName)
    {
        if (!IsConversationHistoryModeRequested())
        {
            return NotFound();
        }

        var safeUserId = GetSafeUserIdOrAnonymous();

        var safeFileName = SanitizePathSegment(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return BadRequest();
        }

        var normalizedFileName = safeFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? safeFileName
            : $"{safeFileName}.txt";
        var filePath = Path.Combine(GetQueryHistoryRootDirectory(), safeUserId, normalizedFileName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        var document = ReadHistoryDocument(filePath);
        var sessionId = $"history-{Path.GetFileNameWithoutExtension(normalizedFileName)}";
        var historyEntries = document.Conversations.SelectMany(conversation => new[]
        {
            $"- 業務員: {conversation.Ask}",
            $"- AI教練: {conversation.Reply}"
        });

        _insuranceChatService.HydrateSession(sessionId, historyEntries);

        lock (HistoryFileLock)
        {
            SessionHistoryFileMap[$"{safeUserId}:{SanitizePathSegment(sessionId)}"] = filePath;
        }

        return Ok(new ConversationHistoryDetailResponse
        {
            FileName = normalizedFileName,
            SessionId = sessionId,
            CustName = document.CustName,
            CustTagNo = document.CustTagNo,
            InsureTypeList = document.InsureTypeList,
            TodoInfo = document.TodoInfo,
            TodoDocument = document.TodoDocument,
            UploadedFiles = ListUploadedFiles(safeUserId, normalizedFileName),
            Conversations = document.Conversations
                .Select(conversation => new ConversationHistoryResponseItem
                {
                    Ask = conversation.Ask,
                    Reply = conversation.Reply
                })
                .ToList()
        });
    }

    /// <summary>
    /// 檢視指定對話歷史對應的已上傳文件。
    /// </summary>
    [HttpGet("histories/{fileName}/uploaded-files/{storedFileName}")]
    public IActionResult ViewUploadedFile([FromRoute] string fileName, [FromRoute] string storedFileName)
    {
        if (!IsConversationHistoryModeRequested())
        {
            return NotFound();
        }

        var safeUserId = GetSafeUserIdOrAnonymous();

        var safeFileName = SanitizePathSegment(fileName);
        var safeStoredFileName = SanitizePathSegment(storedFileName);
        if (string.IsNullOrWhiteSpace(safeFileName) || string.IsNullOrWhiteSpace(safeStoredFileName))
        {
            return BadRequest();
        }

        var normalizedFileName = safeFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? safeFileName
            : $"{safeFileName}.txt";

        var uploadedDirectory = GetUploadedFileDirectoryPath(safeUserId, normalizedFileName);
        var uploadedFilePath = Path.Combine(uploadedDirectory, safeStoredFileName);
        if (!System.IO.File.Exists(uploadedFilePath))
        {
            return NotFound();
        }

        var contentType = GetContentType(uploadedFilePath);
        var fileNameForHeader = safeStoredFileName;
        Response.Headers.ContentDisposition = BuildInlineContentDisposition(fileNameForHeader);
        return PhysicalFile(uploadedFilePath, contentType, enableRangeProcessing: true);
    }

    /// <summary>
    /// 刪除指定對話歷史檔案。
    /// </summary>
    [HttpDelete("histories/{fileName}")]
    public IActionResult DeleteHistory([FromRoute] string fileName)
    {
        if (!IsConversationHistoryModeRequested())
        {
            return NotFound();
        }

        var safeUserId = GetSafeUserIdOrAnonymous();

        var safeFileName = SanitizePathSegment(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return BadRequest();
        }

        var normalizedFileName = safeFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? safeFileName
            : $"{safeFileName}.txt";
        var filePath = Path.Combine(GetQueryHistoryRootDirectory(), safeUserId, normalizedFileName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }

        try
        {
            lock (HistoryFileLock)
            {
                var keysToRemove = SessionHistoryFileMap
                    .Where(pair => pair.Key.StartsWith($"{safeUserId}:", StringComparison.Ordinal)
                        && string.Equals(pair.Value, filePath, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    SessionHistoryFileMap.TryRemove(key, out _);
                }

                System.IO.File.Delete(filePath);
            }

            _logger.Info("insurance chat history deleted. userId: {}, filePath: {}", safeUserId, filePath);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "insurance chat history delete failed. userId: {}, filePath: {}", safeUserId, filePath);
            return StatusCode(StatusCodes.Status500InternalServerError, new { msg = "刪除對話紀錄失敗。" });
        }
    }

    private string? TrySaveConversationHistory(
        string sessionId,
        string query,
        string reply,
        string? custName,
        string? custTagNo,
        IEnumerable<string>? insureTypeList,
        IEnumerable<TodoInfoItem>? todoInfo,
        IEnumerable<TodoDocumentItem>? todoDocument)
    {
        var safeUserId = GetSafeUserIdOrAnonymous();

        var safeSessionId = SanitizePathSegment(sessionId);
        if (string.IsNullOrWhiteSpace(safeSessionId))
        {
            return null;
        }

        try
        {
            var userDirectory = Path.Combine(GetQueryHistoryRootDirectory(), safeUserId);
            Directory.CreateDirectory(userDirectory);

            var sessionMapKey = $"{safeUserId}:{safeSessionId}";
            string filePath;

            lock (HistoryFileLock)
            {
                if (!SessionHistoryFileMap.TryGetValue(sessionMapKey, out var mappedFilePath)
                    || string.IsNullOrWhiteSpace(mappedFilePath)
                    || !System.IO.File.Exists(mappedFilePath))
                {
                    mappedFilePath = BuildNextHistoryFilePath(userDirectory);
                    SessionHistoryFileMap[sessionMapKey] = mappedFilePath;
                }

                filePath = mappedFilePath;

                var historyDocument = ReadHistoryDocument(filePath);
                if (string.IsNullOrWhiteSpace(historyDocument.CustName) && !string.IsNullOrWhiteSpace(custName))
                {
                    historyDocument.CustName = custName.Trim();
                }

                if (string.IsNullOrWhiteSpace(historyDocument.CustTagNo) && !string.IsNullOrWhiteSpace(custTagNo))
                {
                    historyDocument.CustTagNo = custTagNo.Trim();
                }

                MergeDistinctStrings(historyDocument.InsureTypeList, insureTypeList);
                MergeDistinctTodoInfoItems(historyDocument.TodoInfo, todoInfo);
                MergeDistinctTodoDocumentItems(historyDocument.TodoDocument, todoDocument);

                historyDocument.Conversations.Add(new ConversationEntry
                {
                    Ask = (query ?? string.Empty).Trim(),
                    Reply = (reply ?? string.Empty).Trim()
                });

                var json = JsonSerializer.Serialize(historyDocument, HistoryJsonSerializerOptions);

                System.IO.File.WriteAllText(filePath, json);
            }

            PromoteUploadedQuerySpecFiles(safeUserId, safeSessionId, filePath);

            _logger.Info("insurance chat history persisted. userId: {}, sessionId: {}, filePath: {}", safeUserId, safeSessionId, filePath);
            return Path.GetFileName(filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "insurance chat history persist failed. userId: {}, sessionId: {}", safeUserId, safeSessionId);
            return null;
        }
    }

    private string GetQueryHistoryRootDirectory()
    {
        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "query_history"));
    }

    private static string GetContentType(string filePath)
    {
        var extension = (Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static string BuildInlineContentDisposition(string fileName)
    {
        var normalizedFileName = (fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedFileName))
        {
            normalizedFileName = "file";
        }

        // HTTP header filename fallback must be ASCII-safe.
        var asciiFallback = new string(normalizedFileName
            .Select(ch => ch <= 0x7F && ch > 0x1F ? ch : '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(asciiFallback))
        {
            asciiFallback = "file";
        }

        var encodedUtf8 = Uri.EscapeDataString(normalizedFileName);
        return $"inline; filename=\"{asciiFallback}\"; filename*=UTF-8''{encodedUtf8}";
    }

    private static string LoadPromptTemplate(string contentRootPath, string fileName)
    {
        var rootBasedPath = Path.GetFullPath(Path.Combine(contentRootPath, "..", "doc", fileName));
        var apiBasedPath = Path.GetFullPath(Path.Combine(contentRootPath, "doc", fileName));

        var resolvedPath = System.IO.File.Exists(rootBasedPath)
            ? rootBasedPath
            : System.IO.File.Exists(apiBasedPath)
                ? apiBasedPath
                : null;

        if (resolvedPath is null)
        {
            throw new FileNotFoundException($"找不到 {fileName}", $"已嘗試: {rootBasedPath} 與 {apiBasedPath}");
        }

        return System.IO.File.ReadAllText(resolvedPath);
    }

    private static string BuildImageUploadUserMessage(string mimeType, string fileName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("請根據系統提示詞分析以下影像資料。");
        builder.AppendLine($"- file_name: {fileName}");
        builder.AppendLine($"- upload_mime_type: {(string.IsNullOrWhiteSpace(mimeType) ? "image/unknown" : mimeType)}");
        builder.AppendLine($"- llm_inline_mime_type: {ImageUploadLlmMimeType}");
        builder.AppendLine("- image_payload: (透過 inlineData 傳送)");
        return builder.ToString();
    }

    private void SaveUploadedImageQuerySpecFile(string sessionId, string originalFileName, string title, byte[] fileBytes)
    {
        var safeUserId = GetSafeUserId();
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            safeUserId = "anonymous";
        }

        var safeSessionId = SanitizePathSegment(sessionId);
        if (string.IsNullOrWhiteSpace(safeSessionId))
        {
            safeSessionId = "unassigned";
        }

        var sessionDirectory = Path.Combine(GetQuerySpecRootDirectory(), safeUserId, safeSessionId);
        Directory.CreateDirectory(sessionDirectory);

        var safeFileName = BuildStoredFileName(originalFileName);
        var filePath = Path.Combine(sessionDirectory, safeFileName);
        System.IO.File.WriteAllBytes(filePath, fileBytes);

        UpsertUploadedFileManifest(sessionDirectory, safeFileName, originalFileName, title);
    }

    private static void UpsertUploadedFileManifest(string sessionDirectory, string storedFileName, string originalFileName, string? title)
    {
        var manifestPath = Path.Combine(sessionDirectory, UploadedFileManifestName);
        var manifest = ReadUploadedFileManifest(sessionDirectory);
        var normalizedTitle = (title ?? string.Empty).Trim();

        var existing = manifest.Files.FirstOrDefault(item =>
            string.Equals(item.StoredFileName, storedFileName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            manifest.Files.Add(new UploadedFileManifestItem
            {
                StoredFileName = storedFileName,
                OriginalFileName = (originalFileName ?? string.Empty).Trim(),
                Title = normalizedTitle
            });
        }
        else
        {
            existing.OriginalFileName = (originalFileName ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedTitle))
            {
                existing.Title = normalizedTitle;
            }
        }

        var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonSerializerOptions);
        System.IO.File.WriteAllText(manifestPath, manifestJson);
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

        var safeExtension = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        return $"{timestamp}_{safeBaseFileName}{safeExtension}";
    }

    private static string ExtractImageUploadTitle(string llmContent, string fallbackFileName)
    {
        var normalizedContent = NormalizeJsonPayload(llmContent);
        if (!string.IsNullOrWhiteSpace(normalizedContent))
        {
            try
            {
                using var document = JsonDocument.Parse(normalizedContent);
                if (document.RootElement.TryGetProperty("title", out var titleElement)
                    && titleElement.ValueKind == JsonValueKind.String)
                {
                    var title = (titleElement.GetString() ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        return title;
                    }
                }
            }
            catch
            {
                // 使用 fallbackFileName。
            }
        }

        return (fallbackFileName ?? string.Empty).Trim();
    }

    private static string NormalizeJsonPayload(string value)
    {
        var text = (value ?? string.Empty).Trim();
        text = text.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        text = text.Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return text;
    }

    private string GetUploadedFileDirectoryPath(string safeUserId, string normalizedHistoryFileName)
    {
        var historyFileNameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedHistoryFileName);
        return Path.Combine(GetQueryHistoryRootDirectory(), safeUserId, historyFileNameWithoutExtension);
    }

    /// <summary>
    /// 影像上傳呼叫 LLM 的請求模型。
    /// </summary>
    public sealed class ImageUploadChatRequest
    {
        /// <summary>
        /// 影像內容 base64 字串。
        /// </summary>
        public string Base64Data { get; set; } = string.Empty;

        /// <summary>
        /// 對話工作階段識別碼。
        /// </summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>
        /// 影像 MIME 類型。
        /// </summary>
        public string MimeType { get; set; } = string.Empty;

        /// <summary>
        /// 原始檔名。
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 指定 LLM Provider。
        /// </summary>
        public string? LlmProvider { get; set; }
    }

    /// <summary>
    /// 影像上傳呼叫 LLM 的回應模型。
    /// </summary>
    public sealed class ImageUploadChatResponse
    {
        /// <summary>
        /// LLM 回傳結果。
        /// </summary>
        public string Result { get; set; } = string.Empty;

        /// <summary>
        /// 影像辨識出的文件標題。
        /// </summary>
        public string DocumentTitle { get; set; } = string.Empty;

        /// <summary>
        /// LLM Provider。
        /// </summary>
        public string? LlmProvider { get; set; }

        /// <summary>
        /// LLM Model。
        /// </summary>
        public string? LlmModel { get; set; }
    }

    private List<UploadedHistoryFileItem> ListUploadedFiles(string safeUserId, string normalizedHistoryFileName)
    {
        var uploadedDirectory = GetUploadedFileDirectoryPath(safeUserId, normalizedHistoryFileName);
        if (!Directory.Exists(uploadedDirectory))
        {
            return [];
        }

        var manifest = ReadUploadedFileManifest(uploadedDirectory);
        var manifestMap = manifest.Files
            .Where(item => !string.IsNullOrWhiteSpace(item.StoredFileName))
            .ToDictionary(item => item.StoredFileName, item => item, StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(uploadedDirectory)
            .Select(Path.GetFileName)
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Where(file => !string.Equals(file, UploadedFileManifestName, StringComparison.OrdinalIgnoreCase))
            .Select(fileName =>
            {
                manifestMap.TryGetValue(fileName!, out var manifestItem);
                return new UploadedHistoryFileItem
                {
                    StoredFileName = fileName!,
                    OriginalFileName = string.IsNullOrWhiteSpace(manifestItem?.OriginalFileName) ? fileName! : manifestItem!.OriginalFileName,
                    Title = (manifestItem?.Title ?? string.Empty).Trim()
                };
            })
            .OrderBy(item => item.StoredFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static UploadedFileManifestDocument ReadUploadedFileManifest(string uploadedDirectory)
    {
        var manifestPath = Path.Combine(uploadedDirectory, UploadedFileManifestName);
        if (!System.IO.File.Exists(manifestPath))
        {
            return new UploadedFileManifestDocument();
        }

        try
        {
            var content = System.IO.File.ReadAllText(manifestPath, Encoding.UTF8);
            var parsed = JsonSerializer.Deserialize<UploadedFileManifestDocument>(content, ManifestJsonSerializerOptions);
            return parsed ?? new UploadedFileManifestDocument();
        }
        catch
        {
            return new UploadedFileManifestDocument();
        }
    }

    private string GetQuerySpecRootDirectory()
    {
        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "query_spec"));
    }

    private void PromoteUploadedQuerySpecFiles(string safeUserId, string safeSessionId, string historyFilePath)
    {
        var stagedDirectory = Path.Combine(GetQuerySpecRootDirectory(), safeUserId, safeSessionId);
        if (!Directory.Exists(stagedDirectory))
        {
            return;
        }

        var historyFileNameWithoutExtension = Path.GetFileNameWithoutExtension(historyFilePath);
        if (string.IsNullOrWhiteSpace(historyFileNameWithoutExtension))
        {
            return;
        }

        var targetDirectory = Path.Combine(GetQueryHistoryRootDirectory(), safeUserId, historyFileNameWithoutExtension);
        Directory.CreateDirectory(targetDirectory);

        foreach (var stagedFilePath in Directory.EnumerateFiles(stagedDirectory))
        {
            var fileName = Path.GetFileName(stagedFilePath);
            var targetFilePath = BuildNonConflictingFilePath(targetDirectory, fileName);
            System.IO.File.Move(stagedFilePath, targetFilePath);
        }

        if (!Directory.EnumerateFileSystemEntries(stagedDirectory).Any())
        {
            Directory.Delete(stagedDirectory, false);
        }
    }

    private static string BuildNonConflictingFilePath(string targetDirectory, string fileName)
    {
        var candidatePath = Path.Combine(targetDirectory, fileName);
        if (!System.IO.File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;
        while (true)
        {
            var nextFileName = $"{baseName}_{counter}{extension}";
            var nextPath = Path.Combine(targetDirectory, nextFileName);
            if (!System.IO.File.Exists(nextPath))
            {
                return nextPath;
            }

            counter++;
        }
    }

    private static bool IsConversationHistoryMode(SimpleChatRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PromptTemplateMode))
        {
            var mode = request.PromptTemplateMode.Trim();
            return string.Equals(mode, PromptTemplateModeSalesAssistant, StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, PromptTemplateModeClaimAssistant, StringComparison.OrdinalIgnoreCase);
        }

        return request.UsePromptTemplate1;
    }

    private bool IsConversationHistoryModeRequested()
    {
        if (Request.Query.TryGetValue("promptTemplateMode", out var promptModeValues))
        {
            var mode = promptModeValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return string.Equals(mode, PromptTemplateModeSalesAssistant, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, PromptTemplateModeClaimAssistant, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (!Request.Query.TryGetValue("usePromptTemplate1", out var values))
        {
            return true;
        }

        var rawValue = values.ToString().Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        return rawValue.Equals("true", StringComparison.OrdinalIgnoreCase)
            || rawValue.Equals("1", StringComparison.OrdinalIgnoreCase);
    }

    private string GetSafeUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)
            && Request.Headers.TryGetValue("X-User-Id", out var headerUserId))
        {
            userId = headerUserId.ToString();
        }

        return SanitizePathSegment(userId);
    }

    private string GetSafeUserIdOrAnonymous()
    {
        var safeUserId = GetSafeUserId();
        return string.IsNullOrWhiteSpace(safeUserId) ? "anonymous" : safeUserId;
    }

    private static string BuildHistoryDescription(ConversationHistoryDocument document, string fileName)
    {
        var displayFileName = Path.GetFileNameWithoutExtension(fileName);
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(document.CustName))
        {
            parts.Add(document.CustName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(document.CustTagNo))
        {
            parts.Add(document.CustTagNo.Trim());
        }

        parts.Add(displayFileName);
        return string.Concat(parts);
    }

    private static void MergeDistinctStrings(List<string> target, IEnumerable<string>? source)
    {
        if (source is null)
        {
            return;
        }

        var existing = new HashSet<string>(target.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var normalized = item.Trim();
            if (existing.Add(normalized))
            {
                target.Add(normalized);
            }
        }
    }

    private static void MergeDistinctTodoInfoItems(List<TodoInfoItem> target, IEnumerable<TodoInfoItem>? source)
    {
        if (source is null)
        {
            return;
        }

        var existing = new HashSet<string>(target.Select(BuildTodoInfoItemKey), StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            var normalized = NormalizeTodoInfoItem(item);
            if (normalized is null)
            {
                continue;
            }

            if (existing.Add(BuildTodoInfoItemKey(normalized)))
            {
                target.Add(normalized);
            }
        }
    }

    private static void MergeDistinctTodoDocumentItems(List<TodoDocumentItem> target, IEnumerable<TodoDocumentItem>? source)
    {
        if (source is null)
        {
            return;
        }

        var existing = new HashSet<string>(target.Select(item => item.Name.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            var normalized = NormalizeTodoDocumentItem(item);
            if (normalized is null)
            {
                continue;
            }

            if (existing.Add(normalized.Name))
            {
                target.Add(normalized);
                continue;
            }

            var existingItem = target.FirstOrDefault(targetItem => string.Equals(targetItem.Name.Trim(), normalized.Name, StringComparison.OrdinalIgnoreCase));
            if (existingItem is not null && normalized.Uploaded && !existingItem.Uploaded)
            {
                existingItem.Uploaded = true;
            }
        }
    }

    private static TodoDocumentItem? NormalizeTodoDocumentItem(TodoDocumentItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var name = (item.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new TodoDocumentItem
        {
            Name = name,
            Uploaded = item.Uploaded
        };
    }

    private static TodoInfoItem? NormalizeTodoInfoItem(TodoInfoItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var name = (item.Name ?? string.Empty).Trim();
        var value = (item.Value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return new TodoInfoItem
        {
            Name = string.IsNullOrWhiteSpace(name) ? value : name,
            Value = value
        };
    }

    private static string BuildTodoInfoItemKey(TodoInfoItem item)
    {
        return $"{item.Name.Trim()}\u001f{item.Value.Trim()}";
    }

    private static string BuildNextHistoryFilePath(string userDirectory)
    {
        var datePart = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var nextSequence = 1;
        var files = Directory.EnumerateFiles(userDirectory, $"{datePart}*.txt");
        foreach (var file in files)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (fileNameWithoutExtension.Length != 12)
            {
                continue;
            }

            var sequenceText = fileNameWithoutExtension[8..];
            if (!int.TryParse(sequenceText, out var sequence))
            {
                continue;
            }

            if (sequence >= nextSequence)
            {
                nextSequence = sequence + 1;
            }
        }

        var nextFileName = $"{datePart}{nextSequence:0000}.txt";
        return Path.Combine(userDirectory, nextFileName);
    }

    private static ConversationHistoryDocument ReadHistoryDocument(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            return new ConversationHistoryDocument();
        }

        try
        {
            var content = System.IO.File.ReadAllText(filePath);
            var document = JsonSerializer.Deserialize<ConversationHistoryDocument>(content);
            return document ?? new ConversationHistoryDocument();
        }
        catch
        {
            return new ConversationHistoryDocument();
        }
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

    private sealed class ConversationHistoryDocument
    {
        [JsonPropertyName("cust_name")]
        public string? CustName { get; set; }

        [JsonPropertyName("cust_tag_no")]
        public string? CustTagNo { get; set; }

        [JsonPropertyName("insure_type_list")]
        public List<string> InsureTypeList { get; set; } = [];

        [JsonPropertyName("todo_info")]
        public List<TodoInfoItem> TodoInfo { get; set; } = [];

        [JsonPropertyName("todo_document")]
        public List<TodoDocumentItem> TodoDocument { get; set; } = [];

        [JsonPropertyName("conversations")]
        public List<ConversationEntry> Conversations { get; set; } = [];
    }

    private sealed class ConversationEntry
    {
        [JsonPropertyName("ask")]
        public required string Ask { get; set; }

        [JsonPropertyName("reply")]
        public required string Reply { get; set; }
    }

    private sealed class ConversationHistoryListItem
    {
        [JsonPropertyName("fileName")]
        public required string FileName { get; set; }

        [JsonPropertyName("description")]
        public required string Description { get; set; }
    }

    private sealed class ConversationHistoryDetailResponse
    {
        [JsonPropertyName("fileName")]
        public required string FileName { get; set; }

        [JsonPropertyName("sessionId")]
        public required string SessionId { get; set; }

        [JsonPropertyName("cust_name")]
        public string? CustName { get; set; }

        [JsonPropertyName("cust_tag_no")]
        public string? CustTagNo { get; set; }

        [JsonPropertyName("insure_type_list")]
        public List<string> InsureTypeList { get; set; } = [];

        [JsonPropertyName("todo_info")]
        public List<TodoInfoItem> TodoInfo { get; set; } = [];

        [JsonPropertyName("todo_document")]
        public List<TodoDocumentItem> TodoDocument { get; set; } = [];

        [JsonPropertyName("uploaded_files")]
        public List<UploadedHistoryFileItem> UploadedFiles { get; set; } = [];

        [JsonPropertyName("conversations")]
        public required List<ConversationHistoryResponseItem> Conversations { get; set; }
    }

    private sealed class UploadedHistoryFileItem
    {
        [JsonPropertyName("storedFileName")]
        public required string StoredFileName { get; set; }

        [JsonPropertyName("originalFileName")]
        public required string OriginalFileName { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
    }

    private sealed class UploadedFileManifestDocument
    {
        [JsonPropertyName("Files")]
        public List<UploadedFileManifestItem> Files { get; set; } = [];
    }

    private sealed class UploadedFileManifestItem
    {
        [JsonPropertyName("StoredFileName")]
        public required string StoredFileName { get; set; }

        [JsonPropertyName("OriginalFileName")]
        public string OriginalFileName { get; set; } = string.Empty;

        [JsonPropertyName("Title")]
        public string Title { get; set; } = string.Empty;
    }

    private sealed class ConversationHistoryResponseItem
    {
        [JsonPropertyName("ask")]
        public required string Ask { get; set; }

        [JsonPropertyName("reply")]
        public required string Reply { get; set; }
    }
}
