using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Contracts;
using API.Infrastructure;
using API.Models;

namespace API.Services;

/// <summary>
/// NIM（NVIDIA Inference Microservices）LLM 設定。
/// </summary>
public sealed class NimSettings
{
    public required string Planner { get; set; }

    public required string ApiKey { get; set; }

    public required string Model { get; set; }

    public string? Endpoint { get; set; }
}

internal sealed class NimChatResponse
{
    [JsonPropertyName("choices")]
    public List<NimChoice>? Choices { get; set; }

    [JsonPropertyName("error")]
    public NimError? Error { get; set; }

    [JsonPropertyName("usage")]
    public NimUsage? Usage { get; set; }
}

internal sealed class NimChoice
{
    [JsonPropertyName("message")]
    public NimMessage? Message { get; set; }
}

internal sealed class NimMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class NimError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class NimUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }
}

/// <summary>
/// 使用 NIM 生成 QuerySpec 的服務（OpenAI 相容 API）。
/// </summary>
public sealed class NimQueryPlanningService : IQueryPlanningService, IInsuranceChatCompletionService
{
    private const string DefaultNimChatCompletionsEndpoint = "https://integrate.api.nvidia.com/v1/chat/completions";

    private readonly IConfiguration _configuration;
    private readonly QueryPlanningPromptProvider _promptProvider;
    private readonly QueryPlanningSharedProcessor _sharedProcessor;
    private readonly CustomLogger _logger;
    private readonly HttpClient _httpClient;

    public NimQueryPlanningService(
        IConfiguration configuration,
        QueryPlanningPromptProvider promptProvider,
        QueryPlanningSharedProcessor sharedProcessor,
        ILogger<NimQueryPlanningService> logger)
    {
        _configuration = configuration;
        _promptProvider = promptProvider;
        _sharedProcessor = sharedProcessor;
        _logger = logger.ToCustomLogger();
        _httpClient = new HttpClient();
    }

    public async Task<Step3PlanningResult> PlanAsync(
        PreprocessResult preprocessResult,
        List<string> selectedLayouts,
        FormulaSpec? previousFormulaSpec,
        string entityListText,
        CancellationToken cancellationToken = default)
    {
        var settings = GetSettings("NIM");
        var endpoint = ResolveEndpoint(settings);
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
            foreach (var model in models)
            {
                lastModel = model;
                try
                {
                    var stopwatch = Stopwatch.StartNew();
                    var requestPayload = new
                    {
                        model,
                        temperature = 0,
                        response_format = new { type = "json_object" },
                        messages = new object[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    };

                    var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(requestPayload),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };

                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                    var contentType = response.Content.Headers.ContentType?.MediaType;

                    if (!response.IsSuccessStatusCode)
                    {
                        var nimError = _sharedProcessor.TryReadApiErrorMessage(responseText);
                        _logger.Info("nim api failed. model: {}, status: {}, response: {}", model, (int)response.StatusCode, responseText);
                        throw new InvalidOperationException($"NIM({model}) API 失敗：{response.StatusCode}。{nimError ?? "請檢查 API Key、模型名稱與權限"}");
                    }

                    if (IsLikelyHtmlResponse(responseText, contentType))
                    {
                        throw new InvalidOperationException(
                            $"NIM({model}) 回傳非 JSON 內容，content-type: {contentType ?? "unknown"}，preview: {BuildResponsePreview(responseText)}。請檢查 Endpoint 與模型權限");
                    }

                    NimChatResponse? nimResponse;
                    try
                    {
                        nimResponse = JsonSerializer.Deserialize<NimChatResponse>(responseText);
                    }
                    catch (JsonException jsonException)
                    {
                        throw new InvalidOperationException(
                            $"NIM({model}) 回傳 JSON 解析失敗，content-type: {contentType ?? "unknown"}，preview: {BuildResponsePreview(responseText)}",
                            jsonException);
                    }

                    if (nimResponse?.Error is not null)
                    {
                        throw new InvalidOperationException($"NIM({model}) 錯誤：{nimResponse.Error.Message}");
                    }

                    var textContent = nimResponse?.Choices?.FirstOrDefault()?.Message?.Content;
                    if (string.IsNullOrWhiteSpace(textContent))
                    {
                        throw new InvalidOperationException($"NIM({model}) 回傳空內容");
                    }

                    var llmJson = _sharedProcessor.ExtractJsonText(textContent);
                    lastLlmJson = llmJson;
                    var formulaSpec = _sharedProcessor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);
                    var llmRecord = new LlmRecord
                    {
                        Step = "STEP3",
                        Provider = "Nim",
                        Model = model,
                        InputToken = nimResponse?.Usage?.PromptTokens ?? 0,
                        OutputToken = nimResponse?.Usage?.CompletionTokens ?? 0,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        PromptLength = systemPrompt.Length + userPrompt.Length,
                        ResponseLength = responseText.Length
                    };

                    return new Step3PlanningResult
                    {
                        FormulaSpec = formulaSpec,
                        SystemPrompt = systemPrompt,
                        LlmJson = llmJson,
                        Model = model,
                        Provider = "Nim",
                        LlmRecord = llmRecord
                    };
                }
                catch (Exception modelException) when (modelException is not StepExecutionException)
                {
                    lastException = modelException;
                    _logger.Info("nim model failed. model: {}, message: {}", model, modelException.Message);
                }
            }

            if (lastException is not null)
            {
                throw lastException;
            }

            throw new InvalidOperationException("NIM model 清單為空，請檢查 LlmSettings:NIM:Model 設定");
        }
        catch (Exception exception) when (exception is not StepExecutionException)
        {
            _logger.Error(exception, "nim query planning failed. question: {}", preprocessResult.NormalizedQuestion);
            throw new StepExecutionException(
                3,
                "LLM FormulaSpec 生成",
                $"[NIM] {exception.Message}",
                new Step3FailureDebugData
                {
                    SystemPrompt = systemPrompt,
                    LlmJson = lastLlmJson,
                    Model = lastModel
                },
                exception);
        }
    }

    private NimSettings GetSettings(string? provider)
    {
        var normalizedProvider = LlmProviderResolver.NormalizeProvider(provider);
        var providerCandidates = new List<string>
        {
            provider?.Trim() ?? string.Empty,
            normalizedProvider,
            "NIM",
            "Nim"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in providerCandidates)
        {
            var settings = _configuration.GetSection($"LlmSettings:{candidate}").Get<NimSettings>();
            if (settings is not null)
            {
                return settings;
            }
        }

        throw new InvalidOperationException($"NIM 設定不存在，已嘗試區段：{string.Join(", ", providerCandidates)}");
    }

    private static List<string> ParseModels(string modelSetting)
    {
        return modelSetting
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static string ResolveEndpoint(NimSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return settings.Endpoint.Trim();
        }

        return DefaultNimChatCompletionsEndpoint;
    }

    public async Task<InsuranceChatCompletionResult> ExecuteAsync(string? llmProvider, string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings(llmProvider);
        var endpoint = ResolveEndpoint(settings);
        var models = ParseModels(settings.Model);
        Exception? lastException = null;

        foreach (var model in models)
        {
            try
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                var requestPayload = new
                {
                    model,
                    temperature = 0,
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessage }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.MediaType;

                if (!response.IsSuccessStatusCode)
                {
                    stopwatch.Stop();
                    var nimError = _sharedProcessor.TryReadApiErrorMessage(responseText);
                    throw new InvalidOperationException($"NIM({model}) API 失敗：{response.StatusCode}。{nimError ?? "請檢查 API Key、模型名稱與權限"}");
                }

                if (IsLikelyHtmlResponse(responseText, contentType))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException(
                        $"NIM({model}) 回傳非 JSON 內容，content-type: {contentType ?? "unknown"}，preview: {BuildResponsePreview(responseText)}。請檢查 Endpoint 與模型權限");
                }

                NimChatResponse? nimResponse;
                try
                {
                    nimResponse = JsonSerializer.Deserialize<NimChatResponse>(responseText);
                }
                catch (JsonException jsonException)
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException(
                        $"NIM({model}) 回傳 JSON 解析失敗，content-type: {contentType ?? "unknown"}，preview: {BuildResponsePreview(responseText)}",
                        jsonException);
                }

                if (nimResponse?.Error is not null)
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"NIM({model}) 錯誤：{nimResponse.Error.Message}");
                }

                var textContent = nimResponse?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"NIM({model}) 回傳空內容");
                }

                stopwatch.Stop();
                var inputToken = nimResponse?.Usage?.PromptTokens ?? 0;
                var outputToken = nimResponse?.Usage?.CompletionTokens ?? 0;

                return new InsuranceChatCompletionResult
                {
                    Content = textContent.Trim(),
                    Model = model,
                    Provider = llmProvider,
                    InputToken = inputToken,
                    OutputToken = outputToken,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    IsSuccess = true
                };
            }
            catch (Exception modelException)
            {
                lastException = modelException;
                _logger.Info("nim chat model failed. model: {}, message: {}", model, modelException.Message);
            }
        }

        throw lastException ?? new InvalidOperationException("NIM model 清單為空，請檢查 LlmSettings:NIM:Model 設定");
    }

    private static bool IsLikelyHtmlResponse(string responseText, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var trimmed = responseText.TrimStart();
        return trimmed.StartsWith("<", StringComparison.Ordinal);
    }

    private static string BuildResponsePreview(string responseText)
    {
        var normalized = responseText
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (normalized.Length <= 120)
        {
            return normalized;
        }

        return normalized[..120];
    }
}