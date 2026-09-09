using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Models;

namespace API.Services;

/// <summary>
/// 保險教練多輪對話服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class InsuranceChatService : IInsuranceChatService
{
    private static readonly bool EnablePersonalDataCheckBeforeLlm = false;
    private const string DiagModeAutoInsure = "AUTO_INSURE";
    private const string PromptTemplateModeSalesAssistant = "salesAssistant";
    private const string PromptTemplateModeCustomerService = "customerService";
    private const string PromptTemplateModeClaimAssistant = "claimAssistant";

    public const string PersonalDataBlockedReply = "傳送訊息不得包含個資";

    private readonly ConcurrentDictionary<string, List<string>> _sessionCache;
    private readonly IInsuranceBrainService _insuranceBrainService;
    private readonly IInsuranceChatCompletionService _chatCompletionService;
    private readonly IComplianceConsultingService _complianceConsultingService;
    private readonly IGoogleEmbeddingVectorQueryService _googleEmbeddingVectorQueryService;
    private readonly IGeminiContextCacheService _geminiContextCacheService;
    private readonly IPiiDetectionService _piiDetectionService;
    private readonly ILlmLogService _llmLogService;
    private readonly CustomLogger _logger;
    private readonly string _systemPromptTemplate1;
    private readonly string _preVectorPromptTemplate;
    private readonly string _systemPromptTemplate2;
    private readonly string _systemPromptTemplate3;
    private readonly string _systemPromptTemplate4;

    public InsuranceChatService(
        ConcurrentDictionary<string, List<string>> sessionCache,
        IInsuranceBrainService insuranceBrainService,
        IInsuranceChatCompletionService chatCompletionService,
        IComplianceConsultingService complianceConsultingService,
        IGoogleEmbeddingVectorQueryService googleEmbeddingVectorQueryService,
        IGeminiContextCacheService geminiContextCacheService,
        IPiiDetectionService piiDetectionService,
        ILlmLogService llmLogService,
        IWebHostEnvironment environment,
        ILogger<InsuranceChatService> logger)
    {
        _sessionCache = sessionCache;
        _insuranceBrainService = insuranceBrainService;
        _chatCompletionService = chatCompletionService;
        _complianceConsultingService = complianceConsultingService;
        _googleEmbeddingVectorQueryService = googleEmbeddingVectorQueryService;
        _geminiContextCacheService = geminiContextCacheService;
        _piiDetectionService = piiDetectionService;
        _llmLogService = llmLogService;
        _logger = logger.ToCustomLogger();
        _systemPromptTemplate1 = LoadSystemPromptTemplate(environment.ContentRootPath, "Prompt_Sales.txt");
        _preVectorPromptTemplate = LoadSystemPromptTemplate(environment.ContentRootPath, "Prompt_PreVector.txt");
        _systemPromptTemplate2 = LoadSystemPromptTemplate(environment.ContentRootPath, "Prompt_Customer.txt");
        _systemPromptTemplate3 = LoadSystemPromptTemplate(environment.ContentRootPath, "Prompt_Customer_Sales.txt");
        _systemPromptTemplate4 = LoadSystemPromptTemplate(environment.ContentRootPath, "Prompt_Claim.txt");
    }

    public InsuranceChatService(
        ConcurrentDictionary<string, List<string>> sessionCache,
        IInsuranceBrainService insuranceBrainService,
        IInsuranceChatCompletionService chatCompletionService,
        IComplianceConsultingService complianceConsultingService,
        IGoogleEmbeddingVectorQueryService googleEmbeddingVectorQueryService,
        IGeminiContextCacheService geminiContextCacheService,
        IPiiDetectionService piiDetectionService,
        IWebHostEnvironment environment,
        ILogger<InsuranceChatService> logger)
        : this(
            sessionCache,
            insuranceBrainService,
            chatCompletionService,
            complianceConsultingService,
            googleEmbeddingVectorQueryService,
            geminiContextCacheService,
            piiDetectionService,
            NoOpLlmLogService.Instance,
            environment,
            logger)
    {
    }

    public async Task<SimpleChatResponse> ChatAsync(SimpleChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("SessionId 不可為空", nameof(request.SessionId));
        }

        if (string.IsNullOrWhiteSpace(request.UserMessage))
        {
            throw new ArgumentException("UserMessage 不可為空", nameof(request.UserMessage));
        }

        if (EnablePersonalDataCheckBeforeLlm)
        {
            var detectedEntities = _piiDetectionService.DetectPersonalDataEntities(request.UserMessage);
            if (detectedEntities.Count > 0)
            {
                _logger.Info("insurance chat blocked due to pii. sessionId: {}, entities: {}", request.SessionId, detectedEntities);

                return new SimpleChatResponse
                {
                    Reply = PersonalDataBlockedReply,
                    SystemPrompt = string.Empty,
                    StoryScript = string.Empty,
                    ContextCacheName = _geminiContextCacheService.CachedContentName
                };
            }
        }

        _insuranceBrainService.EnsureInitialized();

        var history = _sessionCache.GetOrAdd(request.SessionId, _ => new List<string>());
        List<string> historySnapshot;
        lock (history)
        {
            historySnapshot = history.ToList();
        }

        var promptTemplateMode = ResolvePromptTemplateMode(request);
        if (string.Equals(promptTemplateMode, PromptTemplateModeSalesAssistant, StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteSalesAssistantAsync(
                request,
                history,
                historySnapshot,
                cancellationToken);
        }

        if (string.Equals(promptTemplateMode, PromptTemplateModeClaimAssistant, StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteClaimAssistantAsync(
                request,
                history,
                historySnapshot,
                cancellationToken);
        }

        var selectedTemplate = promptTemplateMode switch
        {
            PromptTemplateModeCustomerService => _systemPromptTemplate2,
            _ => _systemPromptTemplate1
        };

        var systemPrompt = ApplyCommonPromptTokens(selectedTemplate, _insuranceBrainService.AllProductsJson);

        var storyScript = BuildStoryScript(historySnapshot, request.UserMessage);
        var chatResult = await _chatCompletionService.ExecuteAsync(request.LlmProvider, systemPrompt, storyScript, cancellationToken);
        var aiReply = chatResult.Content;
        var llmLogErrors = new List<string>();

        // 記錄 LLM 呼叫日誌
        await LogLlmCallAsync(request.SessionId, chatResult, systemPrompt.Length + storyScript.Length, llmLogErrors, cancellationToken);

        var parsedDialogSpec = TryParseDialogSpec(aiReply);
        var finalDialogSpec = parsedDialogSpec;
        var finalReply = parsedDialogSpec?.ReplyContent ?? aiReply;

        if (string.Equals(promptTemplateMode, PromptTemplateModeCustomerService, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsedDialogSpec?.DiagMode?.Trim(), DiagModeAutoInsure, StringComparison.OrdinalIgnoreCase))
        {
            systemPrompt = ApplyCommonPromptTokens(_systemPromptTemplate3, _insuranceBrainService.AllProductsJson);
            var autoInsureResult = await _chatCompletionService.ExecuteAsync(request.LlmProvider, systemPrompt, storyScript, cancellationToken);
            var autoInsureReply = autoInsureResult.Content;
            var autoInsureDialogSpec = TryParseDialogSpec(autoInsureReply);
            finalDialogSpec = autoInsureDialogSpec ?? finalDialogSpec;
            finalReply = autoInsureDialogSpec?.ReplyContent ?? autoInsureReply;
            
            // 記錄第二次 LLM 呼叫日誌
            await LogLlmCallAsync(request.SessionId, autoInsureResult, systemPrompt.Length + storyScript.Length, llmLogErrors, cancellationToken);
            
            chatResult = autoInsureResult;
        }

        lock (history)
        {
            history.Add($"- 業務員: {request.UserMessage}");
            history.Add($"- AI教練: {finalReply}");
        }

        _logger.Info("insurance chat completed. sessionId: {}, historyCount: {}", request.SessionId, history.Count);

        return new SimpleChatResponse
        {
            Reply = finalReply,
            SystemPrompt = systemPrompt,
            StoryScript = storyScript,
            LlmProvider = chatResult.Provider,
            LlmModel = chatResult.Model,
            CustName = finalDialogSpec?.CustName,
            CustTagNo = finalDialogSpec?.CustTagNo,
            InsureTypeList = finalDialogSpec?.InsureTypeList ?? [],
            TodoInfo = finalDialogSpec?.TodoInfo ?? [],
            TodoDocument = finalDialogSpec?.TodoDocument ?? [],
            LlmLogErrors = llmLogErrors,
            LlmRawReply = chatResult.Content,
            ContextCacheName = _geminiContextCacheService.CachedContentName
        };
    }

    private async Task<SimpleChatResponse> ExecuteSalesAssistantAsync(
        SimpleChatRequest request,
        List<string> history,
        IReadOnlyList<string> historySnapshot,
        CancellationToken cancellationToken)
    {
        var conversationHistory = BuildConversationHistoryText(historySnapshot);
        var preVectorPrompt = ApplyPreVectorPromptTokens(
            _preVectorPromptTemplate,
            conversationHistory,
            request.UserMessage);

        var preVectorResult = await _chatCompletionService.ExecuteAsync(
            request.LlmProvider,
            preVectorPrompt,
            string.Empty,
            cancellationToken);

        var llmLogErrors = new List<string>();
        await LogLlmCallAsync(
            request.SessionId,
            preVectorResult,
            preVectorPrompt.Length,
            llmLogErrors,
            cancellationToken);

        var keywordText = NormalizeKeywordText(preVectorResult.Content);
        if (string.IsNullOrWhiteSpace(keywordText))
        {
            keywordText = request.UserMessage.Trim();
        }

        var keywordEmbeddings = await _googleEmbeddingVectorQueryService.GenerateEmbeddingsAsync([keywordText], cancellationToken);
        var keywordVector = keywordEmbeddings.FirstOrDefault();
        if (keywordVector.IsEmpty)
        {
            throw new InvalidOperationException("關鍵字向量化結果為空。");
        }

        var productClauses = await _complianceConsultingService.SearchProductClausesAsync(keywordVector, cancellationToken);
        var productClausesJson = BuildProductClausesJson(productClauses);
        var salesSystemPrompt = ApplyCommonPromptTokens(_systemPromptTemplate1, productClausesJson);
        var storyScript = BuildStoryScript(historySnapshot, request.UserMessage);

        var chatResult = await _chatCompletionService.ExecuteAsync(request.LlmProvider, salesSystemPrompt, storyScript, cancellationToken);
        await LogLlmCallAsync(request.SessionId, chatResult, salesSystemPrompt.Length + storyScript.Length, llmLogErrors, cancellationToken);

        var parsedDialogSpec = TryParseDialogSpec(chatResult.Content);
        var finalReply = parsedDialogSpec?.ReplyContent ?? chatResult.Content;

        lock (history)
        {
            history.Add($"- 業務員: {request.UserMessage}");
            history.Add($"- AI教練: {finalReply}");
        }

        return new SimpleChatResponse
        {
            Reply = finalReply,
            SystemPrompt = salesSystemPrompt,
            StoryScript = storyScript,
            LlmProvider = chatResult.Provider,
            LlmModel = chatResult.Model,
            CustName = parsedDialogSpec?.CustName,
            CustTagNo = parsedDialogSpec?.CustTagNo,
            InsureTypeList = parsedDialogSpec?.InsureTypeList ?? [],
            TodoInfo = parsedDialogSpec?.TodoInfo ?? [],
            TodoDocument = parsedDialogSpec?.TodoDocument ?? [],
            LlmLogErrors = llmLogErrors,
            LlmRawReply = chatResult.Content,
            LlmKeywordSystemPrompt = preVectorPrompt,
            LlmKeyword = preVectorResult.Content,
            ContextCacheName = _geminiContextCacheService.CachedContentName
        };
    }

    private async Task<SimpleChatResponse> ExecuteClaimAssistantAsync(
        SimpleChatRequest request,
        List<string> history,
        IReadOnlyList<string> historySnapshot,
        CancellationToken cancellationToken)
    {
        var conversationHistory = BuildConversationHistoryText(historySnapshot);
        var preVectorPrompt = ApplyPreVectorPromptTokens(
            _preVectorPromptTemplate,
            conversationHistory,
            request.UserMessage);

        var preVectorResult = await _chatCompletionService.ExecuteAsync(
            request.LlmProvider,
            preVectorPrompt,
            string.Empty,
            cancellationToken);

        var llmLogErrors = new List<string>();
        await LogLlmCallAsync(
            request.SessionId,
            preVectorResult,
            preVectorPrompt.Length,
            llmLogErrors,
            cancellationToken);

        var keywordText = NormalizeKeywordText(preVectorResult.Content);
        if (string.IsNullOrWhiteSpace(keywordText))
        {
            keywordText = request.UserMessage.Trim();
        }

        var keywordEmbeddings = await _googleEmbeddingVectorQueryService.GenerateEmbeddingsAsync([keywordText], cancellationToken);
        var keywordVector = keywordEmbeddings.FirstOrDefault();
        if (keywordVector.IsEmpty)
        {
            throw new InvalidOperationException("關鍵字向量化結果為空。");
        }

        var productClauses = await _complianceConsultingService.SearchProductClausesAsync(keywordVector, cancellationToken);
        var productClausesJson = BuildProductClausesJson(productClauses);
        var lawReferences = await _complianceConsultingService.SearchLawReferencesAsync(keywordVector, cancellationToken);
        var lawReferencesJson = BuildLawReferencesJson(lawReferences);

        var claimSystemPrompt = ApplyClaimPromptTokens(
            _systemPromptTemplate4,
            productClausesJson,
            lawReferencesJson,
            request.UploadedDocumentTitles,
            request.PolicyInsureTypeList);

        var storyScript = BuildStoryScript(historySnapshot, request.UserMessage);
        var chatResult = await _chatCompletionService.ExecuteAsync(request.LlmProvider, claimSystemPrompt, storyScript, cancellationToken);
        await LogLlmCallAsync(request.SessionId, chatResult, claimSystemPrompt.Length + storyScript.Length, llmLogErrors, cancellationToken);

        var parsedDialogSpec = TryParseDialogSpec(chatResult.Content);
        var finalReply = parsedDialogSpec?.ReplyContent ?? chatResult.Content;

        lock (history)
        {
            history.Add($"- 業務員: {request.UserMessage}");
            history.Add($"- AI教練: {finalReply}");
        }

        return new SimpleChatResponse
        {
            Reply = finalReply,
            SystemPrompt = claimSystemPrompt,
            StoryScript = storyScript,
            LlmProvider = chatResult.Provider,
            LlmModel = chatResult.Model,
            CustName = parsedDialogSpec?.CustName,
            CustTagNo = parsedDialogSpec?.CustTagNo,
            InsureTypeList = parsedDialogSpec?.InsureTypeList ?? [],
            TodoInfo = parsedDialogSpec?.TodoInfo ?? [],
            TodoDocument = parsedDialogSpec?.TodoDocument ?? [],
            LlmLogErrors = llmLogErrors,
            LlmRawReply = chatResult.Content,
            LlmKeywordSystemPrompt = preVectorPrompt,
            LlmKeyword = preVectorResult.Content,
            ContextCacheName = _geminiContextCacheService.CachedContentName
        };
    }

    public bool ResetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId 不可為空", nameof(sessionId));
        }

        var removed = _sessionCache.TryRemove(sessionId, out _);
        _logger.Info("insurance chat session reset. sessionId: {}, removed: {}", sessionId, removed);
        return removed;
    }

    public void HydrateSession(string sessionId, IEnumerable<string> historyEntries)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId 不可為空", nameof(sessionId));
        }

        ArgumentNullException.ThrowIfNull(historyEntries);

        var normalizedEntries = historyEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim())
            .ToList();

        var history = _sessionCache.AddOrUpdate(
            sessionId,
            _ => normalizedEntries,
            (_, _) => normalizedEntries);

        _logger.Info("insurance chat session hydrated. sessionId: {}, historyCount: {}", sessionId, history.Count);
    }

    private static string LoadSystemPromptTemplate(string contentRootPath, string fileName)
    {
        var rootBasedPath = Path.GetFullPath(Path.Combine(contentRootPath, "..", "doc", fileName));
        var apiBasedPath = Path.GetFullPath(Path.Combine(contentRootPath, "doc", fileName));

        var resolvedPath = File.Exists(rootBasedPath)
            ? rootBasedPath
            : File.Exists(apiBasedPath)
                ? apiBasedPath
                : null;

        if (resolvedPath is null)
        {
            throw new FileNotFoundException($"找不到 {fileName}", $"已嘗試: {rootBasedPath} 與 {apiBasedPath}");
        }

        return File.ReadAllText(resolvedPath);
    }

    private static string ResolvePromptTemplateMode(SimpleChatRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PromptTemplateMode))
        {
            var normalizedMode = request.PromptTemplateMode.Trim();
            if (string.Equals(normalizedMode, PromptTemplateModeClaimAssistant, StringComparison.OrdinalIgnoreCase))
            {
                return PromptTemplateModeClaimAssistant;
            }

            if (string.Equals(normalizedMode, PromptTemplateModeCustomerService, StringComparison.OrdinalIgnoreCase))
            {
                return PromptTemplateModeCustomerService;
            }

            if (string.Equals(normalizedMode, PromptTemplateModeSalesAssistant, StringComparison.OrdinalIgnoreCase))
            {
                return PromptTemplateModeSalesAssistant;
            }
        }

        return request.UsePromptTemplate1
            ? PromptTemplateModeSalesAssistant
            : PromptTemplateModeCustomerService;
    }

    private static string ApplyCommonPromptTokens(string template, string allProductsJson)
    {
        return template.Replace("{brain.AllProductsJson}", allProductsJson, StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyPreVectorPromptTokens(string template, string conversationHistory, string currentQuery)
    {
        return template
            .Replace("{conversationHistory}", conversationHistory, StringComparison.OrdinalIgnoreCase)
            .Replace("{currentQuery}", currentQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static string ApplyClaimPromptTokens(
        string template,
        string productClausesJson,
        string claimReferenceData,
        IEnumerable<string>? uploadedDocumentTitles,
        IEnumerable<string>? policyInsureTypeList)
    {
        var uploadedDocumentsText = BuildUploadedDocumentsPromptText(uploadedDocumentTitles);
        var policyInsureTypesText = BuildPolicyInsureTypesPromptText(policyInsureTypeList);
        return template
            .Replace("{brain.AllProductsJson}", productClausesJson, StringComparison.OrdinalIgnoreCase)
            .Replace("{POLICY_INSTYPE}", policyInsureTypesText, StringComparison.OrdinalIgnoreCase)
            .Replace("{CLAIM_REFERENCE_DATA}", claimReferenceData, StringComparison.OrdinalIgnoreCase)
            .Replace("{UPLOADED_DOCUMENTS}", uploadedDocumentsText, StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveBrainPromptToken(string template)
    {
        return template.Replace("{brain.AllProductsJson}", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUploadedDocumentsPromptText(IEnumerable<string>? uploadedDocumentTitles)
    {
        if (uploadedDocumentTitles is null)
        {
            return "(無)";
        }

        var normalizedTitles = uploadedDocumentTitles
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Select(title => title.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedTitles.Count == 0)
        {
            return "(無)";
        }

        return string.Join(Environment.NewLine, normalizedTitles.Select(title => $"- {title}"));
    }

    private static string BuildPolicyInsureTypesPromptText(IEnumerable<string>? policyInsureTypeList)
    {
        if (policyInsureTypeList is null)
        {
            return "(無)";
        }

        var normalizedTypes = policyInsureTypeList
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedTypes.Count == 0)
        {
            return "(無)";
        }

        return string.Join(Environment.NewLine, normalizedTypes.Select(type => $"- {type}"));
    }

    private static string BuildStoryScript(IEnumerable<string> history, string userMessage)
    {
        // 使用 StringBuilder 將每輪歷史與本輪回報串成單一劇本，避免逐次字串相加造成大量暫時物件。
        var builder = new StringBuilder();
        builder.AppendLine("【過去的多輪對話歷史紀錄】");

        foreach (var entry in history)
        {
            builder.AppendLine(entry);
        }

        builder.AppendLine();
        builder.AppendLine("【業務員最新回傳的客戶現況與回覆】");
        builder.AppendLine(userMessage);

        return builder.ToString();
    }

    private static string BuildConversationHistoryText(IEnumerable<string> history)
    {
        return string.Join(Environment.NewLine, history);
    }

    private static string BuildProductClausesJson(IEnumerable<ComplianceProductClauseSearchResult> productClauses)
    {
        var payload = productClauses.Select(clause => new
        {
            clause.ProductName,
            clause.ItemType,
            clause.MarketingTags,
            ClauseText = clause.Content
        });

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static string BuildLawReferencesJson(IEnumerable<ComplianceLawReferenceSearchResult> lawReferences)
    {
        var payload = lawReferences.Select(reference => new
        {
            reference.SourceNo,
            reference.Guid,
            reference.LawName,
            reference.ArticleNumber,
            reference.ArticleText
        });

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static string NormalizeKeywordText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = ExtractJsonPayload(trimmed);
        }

        var lines = trimmed
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("```", StringComparison.Ordinal))
            .ToList();

        return string.Join(" ", lines).Trim();
    }

    private static DialogSpecPayload? TryParseDialogSpec(string aiReply)
    {
        if (string.IsNullOrWhiteSpace(aiReply))
        {
            return null;
        }

        var payload = ExtractJsonPayload(aiReply);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var dialogSpec = DeserializeDialogSpec(payload);
        if (dialogSpec is null)
        {
            var sanitizedPayload = EscapeRawNewlinesInsideJsonStrings(payload);
            if (!string.Equals(sanitizedPayload, payload, StringComparison.Ordinal))
            {
                dialogSpec = DeserializeDialogSpec(sanitizedPayload);
            }
        }

        if (dialogSpec is null || string.IsNullOrWhiteSpace(dialogSpec.ReplyContent))
        {
            return null;
        }

        return dialogSpec;
    }

    private static DialogSpecPayload? DeserializeDialogSpec(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<DialogSpecPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static string EscapeRawNewlinesInsideJsonStrings(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(payload.Length + 16);
        var inString = false;
        var escaped = false;

        for (var index = 0; index < payload.Length; index++)
        {
            var ch = payload[index];

            if (escaped)
            {
                builder.Append(ch);
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                builder.Append(ch);
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                builder.Append(ch);
                inString = !inString;
                continue;
            }

            if (inString && ch == '\r')
            {
                if (index + 1 < payload.Length && payload[index + 1] == '\n')
                {
                    index++;
                }

                builder.Append("\\n");
                continue;
            }

            if (inString && ch == '\n')
            {
                builder.Append("\\n");
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string ExtractJsonPayload(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split('\n');
            if (lines.Length < 3)
            {
                return string.Empty;
            }

            var bodyLines = lines.Skip(1).Take(lines.Length - 2);
            trimmed = string.Join("\n", bodyLines).Trim();
        }

        var startIndex = trimmed.IndexOf('{');
        if (startIndex < 0)
        {
            return trimmed;
        }

        var inString = false;
        var escaped = false;
        var depth = 0;

        for (var index = startIndex; index < trimmed.Length; index++)
        {
            var ch = trimmed[index];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return trimmed[startIndex..(index + 1)].Trim();
                }
            }
        }

        return trimmed[startIndex..].Trim();
    }

    private async Task LogLlmCallAsync(
        string sessionId,
        InsuranceChatCompletionResult result,
        int promptLength,
        List<string> llmLogErrors,
        CancellationToken cancellationToken)
    {
        try
        {
            var logErrorMessage = await _llmLogService.LogLlmCallAsync(
                sessionId,
                result.Provider ?? "Unknown",
                result.Model ?? "Unknown",
                result.InputToken,
                result.OutputToken,
                result.CacheLength,
                result.DurationMs,
                promptLength,
                result.Content.Length,
                result.IsSuccess,
                DateTime.Now.AddMilliseconds(-result.DurationMs),
                DateTime.Now,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(logErrorMessage))
            {
                llmLogErrors.Add(logErrorMessage);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "failed to log llm call. sessionId: {}, provider: {}", sessionId, result.Provider);
            llmLogErrors.Add(exception.Message);
            // 不拋出異常，避免日誌記錄影響主業務流程
        }
    }

    private sealed class DialogSpecPayload
    {
        [JsonPropertyName("diag_mode")]
        public string? DiagMode { get; set; }

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

        [JsonPropertyName("reply_content")]
        public string? ReplyContent { get; set; }
    }

    private sealed class NoOpLlmLogService : ILlmLogService
    {
        public static readonly NoOpLlmLogService Instance = new();

        private NoOpLlmLogService()
        {
        }

        public Task<string?> LogLlmCallAsync(
            string sessionId,
            string provider,
            string model,
            int inputToken,
            int outputToken,
            int cacheLength,
            long durationMs,
            int promptLength,
            int responseLength,
            bool successFlag,
            DateTime requestTime,
            DateTime responseTime,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
