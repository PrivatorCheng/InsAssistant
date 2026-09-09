using System.Diagnostics;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using Google.GenAI;
using Google.GenAI.Types;

namespace API.Services;

/// <summary>
/// Google Gemini LLM 設定。
/// </summary>
public sealed class GoogleGeminiSettings
{
    public required string Planner { get; set; }

    public required string ApiKey { get; set; }

    public required string Model { get; set; }
}

/// <summary>
/// 使用 Google Gemini REST API 生成 QuerySpec 的服務。
/// </summary>
public sealed class GeminiQueryPlanningService : IQueryPlanningService, IInsuranceChatCompletionService
{
    private const string ContextCacheEnabledConfigPath = "LlmSettings:ContextCacheEnabled";
    private readonly IConfiguration _configuration;
    private readonly QueryPlanningPromptProvider _promptProvider;
    private readonly QueryPlanningSharedProcessor _sharedProcessor;
    private readonly IGeminiContextCacheService _geminiContextCacheService;
    private readonly CustomLogger _logger;

    public GeminiQueryPlanningService(
        IConfiguration configuration,
        QueryPlanningPromptProvider promptProvider,
        QueryPlanningSharedProcessor sharedProcessor,
        IGeminiContextCacheService geminiContextCacheService,
        ILogger<GeminiQueryPlanningService> logger)
    {
        _configuration = configuration;
        _promptProvider = promptProvider;
        _sharedProcessor = sharedProcessor;
        _geminiContextCacheService = geminiContextCacheService;
        _logger = logger.ToCustomLogger();
    }

    public async Task<Step3PlanningResult> PlanAsync(
        PreprocessResult preprocessResult,
        List<string> selectedLayouts,
        FormulaSpec? previousFormulaSpec,
        string entityListText,
        CancellationToken cancellationToken)
    {
        var settings = GetSettings(LlmProviderResolver.ResolveProvider(_configuration));
        var (availableTables, tableFieldMap) = _sharedProcessor.BuildContext(selectedLayouts);
        var tableFieldText = _sharedProcessor.BuildTableFieldText(tableFieldMap);
        var systemPrompt = _promptProvider.BuildSystemPrompt(
            preprocessResult.NormalizedQuestion,
            preprocessResult.Keywords,
            string.Join(", ", availableTables),
            tableFieldText,
            availableTables,
            entityListText,
            previousFormulaSpec);
        var userPrompt = _sharedProcessor.BuildUserPrompt(preprocessResult);
        var models = ParseModels(settings.Model);
        Exception? lastException = null;
        string? lastLlmJson = null;
        string? lastModel = null;

        try
        {
            var cachedContentName = await TryResolveCachedContentNameAsync(cancellationToken);

            foreach (var model in models)
            {
                lastModel = model;
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    var client = new Client(apiKey: settings.ApiKey);

                    var mergedPrompt = BuildMergedUserPrompt(systemPrompt, userPrompt);

                    var contents = new List<Content>
                    {
                        new()
                        {
                            Role = "user",
                            Parts = new List<Part>
                            {
                                new() { Text = mergedPrompt }
                            }
                        }
                    };

                    var response = await GenerateContentWithOptionalCacheAsync(
                        client,
                        model,
                        contents,
                        cachedContentName,
                        cancellationToken);

                    var textContent = ExtractTextFromResponse(response);
                    if (string.IsNullOrWhiteSpace(textContent))
                    {
                        throw new InvalidOperationException($"Gemini({model}) 回傳空內容");
                    }

                    var llmJson = _sharedProcessor.ExtractJsonText(textContent);
                    lastLlmJson = llmJson;
                    var formulaSpec = _sharedProcessor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);
                    var inputToken = response.UsageMetadata?.PromptTokenCount ?? 0;
                    var outputToken = response.UsageMetadata?.CandidatesTokenCount ?? 0;
                    var cacheLength = response.UsageMetadata?.CachedContentTokenCount ?? 0;
                    var llmRecord = new LlmRecord
                    {
                        Step = "STEP3",
                        Provider = "GoogleGemini",
                        Model = model,
                        InputToken = inputToken,
                        OutputToken = outputToken,
                        CacheLength = cacheLength,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        PromptLength = mergedPrompt.Length,
                        ResponseLength = textContent.Length
                    };

                    return new Step3PlanningResult
                    {
                        FormulaSpec = formulaSpec,
                        SystemPrompt = systemPrompt,
                        LlmJson = llmJson,
                        Model = model,
                        Provider = "GoogleGemini",
                        LlmRecord = llmRecord
                    };
                }
                catch (Exception modelException) when (modelException is not StepExecutionException)
                {
                    lastException = modelException;
                    _logger.Info("gemini model failed. model: {}, message: {}", model, modelException.Message);
                }
            }

            if (lastException is not null)
            {
                throw lastException;
            }

            throw new InvalidOperationException("Gemini model 清單為空，請檢查 LlmSettings:GoogleGemini:Model 設定");
        }
        catch (Exception exception) when (exception is not StepExecutionException)
        {
            _logger.Error(exception, "gemini query planning failed. question: {}", preprocessResult.NormalizedQuestion);
            throw new StepExecutionException(
                3,
                "LLM FormulaSpec 生成",
                exception.Message,
                new Step3FailureDebugData
                {
                    SystemPrompt = systemPrompt,
                    LlmJson = lastLlmJson,
                    Model = lastModel
                },
                exception);
        }
    }

    /// <summary>
    /// 對話模式呼叫 Gemini 並回傳純文字結果。
    /// </summary>
    public async Task<InsuranceChatCompletionResult> ExecuteAsync(string? llmProvider, string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings(llmProvider);
        var models = ParseModels(settings.Model);
        var cachedContentName = await TryResolveCachedContentNameAsync(cancellationToken);
        Exception? lastException = null;

        foreach (var model in models)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var client = new Client(apiKey: settings.ApiKey);

                var mergedPrompt = BuildMergedUserPrompt(systemPrompt, userMessage);

                var contents = new List<Content>
                {
                    new()
                    {
                        Role = "user",
                        Parts = new List<Part>
                        {
                            new() { Text = mergedPrompt }
                        }
                    }
                };

                var response = await GenerateContentWithOptionalCacheAsync(
                    client,
                    model,
                    contents,
                    cachedContentName,
                    cancellationToken);

                var responseTextContent = ExtractTextFromResponse(response);

                if (string.IsNullOrWhiteSpace(responseTextContent))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"Gemini({model}) 回傳空內容");
                }

                stopwatch.Stop();
                var inputToken = response.UsageMetadata?.PromptTokenCount ?? 0;
                var outputToken = response.UsageMetadata?.CandidatesTokenCount ?? 0;
                var cacheLength = response.UsageMetadata?.CachedContentTokenCount ?? 0;

                return new InsuranceChatCompletionResult
                {
                    Content = responseTextContent.Trim(),
                    Model = model,
                    Provider = llmProvider,
                    InputToken = inputToken,
                    OutputToken = outputToken,
                    CacheLength = cacheLength,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    IsSuccess = true
                };
            }
            catch (Exception modelException)
            {
                lastException = modelException;
                _logger.Info("gemini chat model failed. model: {}, message: {}", model, modelException.Message);
            }
        }

        var result = new InsuranceChatCompletionResult
        {
            Content = "LLM 呼叫失敗",
            Model = models.LastOrDefault(),
            Provider = llmProvider,
            IsSuccess = false,
            ErrorMessage = lastException?.Message ?? "Gemini model 清單為空，請檢查 LlmSettings:GoogleGemini:Model 設定"
        };

        throw lastException ?? new InvalidOperationException(result.ErrorMessage);
    }

    /// <summary>
    /// 對話模式呼叫 Gemini，使用 inlineData 送入影像內容。
    /// </summary>
    public async Task<InsuranceChatCompletionResult> ExecuteWithInlineImageAsync(
        string? llmProvider,
        string systemPrompt,
        string userMessage,
        byte[] imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("imageBytes 不可為空", nameof(imageBytes));
        }

        var normalizedMimeType = string.IsNullOrWhiteSpace(mimeType)
            ? "image/jpeg"
            : mimeType.Trim();

        var settings = GetSettings(llmProvider);
        var models = ParseModels(settings.Model);
        Exception? lastException = null;

        foreach (var model in models)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var client = new Client(apiKey: settings.ApiKey);

                var mergedPrompt = BuildMergedUserPrompt(systemPrompt, userMessage);

                var contents = new List<Content>
                {
                    new()
                    {
                        Role = "user",
                        Parts = new List<Part>
                        {
                            new() { Text = mergedPrompt },
                            new()
                            {
                                InlineData = new Blob
                                {
                                    Data = imageBytes,
                                    MimeType = normalizedMimeType
                                }
                            }
                        }
                    }
                };

                // 影像上傳辨識不接 context cache，避免與對話快取脈絡耦合。
                var response = await client.Models.GenerateContentAsync(
                    model: model,
                    contents: contents,
                    config: new GenerateContentConfig(),
                    cancellationToken: cancellationToken);

                var responseTextContent = ExtractTextFromResponse(response);
                if (string.IsNullOrWhiteSpace(responseTextContent))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"Gemini({model}) 回傳空內容");
                }

                stopwatch.Stop();
                var inputToken = response.UsageMetadata?.PromptTokenCount ?? 0;
                var outputToken = response.UsageMetadata?.CandidatesTokenCount ?? 0;
                var cacheLength = response.UsageMetadata?.CachedContentTokenCount ?? 0;

                return new InsuranceChatCompletionResult
                {
                    Content = responseTextContent.Trim(),
                    Model = model,
                    Provider = llmProvider,
                    InputToken = inputToken,
                    OutputToken = outputToken,
                    CacheLength = cacheLength,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    IsSuccess = true
                };
            }
            catch (Exception modelException)
            {
                lastException = modelException;
                _logger.Info("gemini image chat model failed. model: {}, message: {}", model, modelException.Message);
            }
        }

        throw lastException ?? new InvalidOperationException("Gemini model 清單為空，請檢查 LlmSettings:GoogleGemini:Model 設定");
    }

    private GoogleGeminiSettings GetSettings(string? provider)
    {
        var resolvedProvider = string.IsNullOrWhiteSpace(provider)
            ? LlmProviderResolver.ResolveProvider(_configuration)
            : LlmProviderResolver.NormalizeProvider(provider);

        var providerCandidates = new List<string> { resolvedProvider };

        // 舊設定僅有 GoogleGemini 時，允許回退到 New/Old 版本。
        if (resolvedProvider.Equals("GoogleGemini", StringComparison.OrdinalIgnoreCase))
        {
            providerCandidates.Add("GoogleGeminiNew");
            providerCandidates.Add("GoogleGeminiOld");
        }

        foreach (var candidate in providerCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var settings = _configuration.GetSection($"LlmSettings:{candidate}").Get<GoogleGeminiSettings>();
            if (settings is not null)
            {
                return settings;
            }
        }

        throw new InvalidOperationException(
            $"Gemini 設定不存在，已嘗試區段：{string.Join(", ", providerCandidates)}");
    }

    private static List<string> ParseModels(string modelSetting)
    {
        return modelSetting
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string? ExtractTextFromResponse(GenerateContentResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            return response.Text;
        }

        var textParts = response.Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .Select(part => part.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        return textParts is null || textParts.Count == 0
            ? null
            : string.Join("\n", textParts);
    }

    private static string BuildMergedUserPrompt(string systemPrompt, string userPrompt)
    {
        return $"[SYSTEM_INSTRUCTION]\n{systemPrompt}\n\n[USER_MESSAGE]\n{userPrompt}";
    }

    private async Task<string?> TryResolveCachedContentNameAsync(CancellationToken cancellationToken)
    {
        if (!IsContextCacheEnabled())
        {
            return null;
        }

        await _geminiContextCacheService.EnsureInitializedAsync(cancellationToken);
        var cachedContentName = _geminiContextCacheService.CachedContentName;
        if (string.IsNullOrWhiteSpace(cachedContentName))
        {
            throw new InvalidOperationException("Gemini context cache 不可用：CachedContentName 為空");
        }

        return cachedContentName;
    }

    private async Task<GenerateContentResponse> GenerateContentWithOptionalCacheAsync(
        Client client,
        string model,
        List<Content> contents,
        string? cachedContentName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cachedContentName))
        {
            return await client.Models.GenerateContentAsync(
                model: model,
                contents: contents,
                config: new GenerateContentConfig(),
                cancellationToken: cancellationToken);
        }

        var cacheConfig = new GenerateContentConfig
        {
            CachedContent = cachedContentName
        };

        return await client.Models.GenerateContentAsync(
            model: model,
            contents: contents,
            config: cacheConfig,
            cancellationToken: cancellationToken);
    }

    private bool IsContextCacheEnabled()
    {
        return _configuration.GetValue<bool>(ContextCacheEnabledConfigPath);
    }
}
