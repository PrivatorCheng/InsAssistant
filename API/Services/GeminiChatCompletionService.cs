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
/// 使用 Google Gemini REST API 的對話完成服務。
/// </summary>
public sealed class GeminiChatCompletionService : IInsuranceChatCompletionService
{
    private const string ContextCacheEnabledConfigPath = "LlmSettings:ContextCacheEnabled";
    private readonly IConfiguration _configuration;
    private readonly IGeminiContextCacheService _geminiContextCacheService;
    private readonly CustomLogger _logger;

    public GeminiChatCompletionService(
        IConfiguration configuration,
        IGeminiContextCacheService geminiContextCacheService,
        ILogger<GeminiChatCompletionService> logger)
    {
        _configuration = configuration;
        _geminiContextCacheService = geminiContextCacheService;
        _logger = logger.ToCustomLogger();
    }

    /// <summary>
    /// 對話模式呼叫 Gemini 並回傳純文字結果。
    /// </summary>
    public async Task<InsuranceChatCompletionResult> ExecuteAsync(
        string? llmProvider,
        LlmPromptEnvelope prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

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
                var contents = BuildPromptContents(prompt);

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
        LlmPromptEnvelope prompt,
        byte[] imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
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

                var contents = BuildPromptContents(prompt, includeUserText: false);
                contents.Add(new Content
                {
                    Role = "user",
                    Parts = new List<Part>
                    {
                        new() { Text = string.IsNullOrWhiteSpace(prompt.UserText) ? "請解析這張影像。" : prompt.UserText },
                        new()
                        {
                            InlineData = new Blob
                            {
                                Data = imageBytes,
                                MimeType = normalizedMimeType
                            }
                        }
                    }
                });

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

    private static List<Content> BuildPromptContents(LlmPromptEnvelope prompt, bool includeUserText = true)
    {
        var contents = new List<Content>
        {
            new()
            {
                Role = "user",
                Parts = new List<Part>
                {
                    new() { Text = $"[SYSTEM_PROMPT]\n{prompt.SystemPrompt}" }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(prompt.ContextText))
        {
            contents.Add(new Content
            {
                Role = "user",
                Parts = new List<Part>
                {
                    new() { Text = $"[CONTEXT]\n{prompt.ContextText}" }
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(prompt.HistoryText))
        {
            contents.Add(new Content
            {
                Role = "user",
                Parts = new List<Part>
                {
                    new() { Text = $"[HISTORY]\n{prompt.HistoryText}" }
                }
            });
        }

        if (includeUserText && !string.IsNullOrWhiteSpace(prompt.UserText))
        {
            contents.Add(new Content
            {
                Role = "user",
                Parts = new List<Part>
                {
                    new() { Text = prompt.UserText }
                }
            });
        }

        return contents;
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
