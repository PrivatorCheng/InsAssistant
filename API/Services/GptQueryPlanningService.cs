using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using API.Contracts;
using API.Infrastructure;
using API.Models;

namespace API.Services;

/// <summary>
/// OpenAI GPT LLM 設定。
/// </summary>
public sealed class GptSettings
{
    public required string Planner { get; set; }

    public required string ApiKey { get; set; }

    public required string Model { get; set; }
}

internal sealed class GptChatResponse
{
    [JsonPropertyName("choices")]
    public List<GptChoice>? Choices { get; set; }

    [JsonPropertyName("error")]
    public GptError? Error { get; set; }

    [JsonPropertyName("usage")]
    public GptUsage? Usage { get; set; }
}

internal sealed class GptChoice
{
    [JsonPropertyName("message")]
    public GptMessage? Message { get; set; }
}

internal sealed class GptMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class GptError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class GptUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }
}

/// <summary>
/// 使用 OpenAI GPT 生成 QuerySpec 的服務。
/// </summary>
public sealed class GptQueryPlanningService : IQueryPlanningService, IInsuranceChatCompletionService
{
    private readonly IConfiguration _configuration;
    private readonly QueryPlanningPromptProvider _promptProvider;
    private readonly QueryPlanningSharedProcessor _sharedProcessor;
    private readonly CustomLogger _logger;
    private readonly HttpClient _httpClient;

    public GptQueryPlanningService(
        IConfiguration configuration,
        QueryPlanningPromptProvider promptProvider,
        QueryPlanningSharedProcessor sharedProcessor,
        ILogger<GptQueryPlanningService> logger)
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
        var settings = GetSettings("Gpt");
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

                    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(requestPayload),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };

                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var gptError = _sharedProcessor.TryReadApiErrorMessage(responseText);
                        _logger.Info("gpt api failed. model: {}, status: {}, response: {}", model, (int)response.StatusCode, responseText);
                        throw new InvalidOperationException($"GPT({model}) API 失敗：{response.StatusCode}。{gptError ?? "請檢查 API Key、模型名稱與權限"}");
                    }

                    var gptResponse = JsonSerializer.Deserialize<GptChatResponse>(responseText);
                    if (gptResponse?.Error is not null)
                    {
                        throw new InvalidOperationException($"GPT({model}) 錯誤：{gptResponse.Error.Message}");
                    }

                    var textContent = gptResponse?.Choices?.FirstOrDefault()?.Message?.Content;
                    if (string.IsNullOrWhiteSpace(textContent))
                    {
                        throw new InvalidOperationException($"GPT({model}) 回傳空內容");
                    }

                    var llmJson = _sharedProcessor.ExtractJsonText(textContent);
                    lastLlmJson = llmJson;
                    var formulaSpec = _sharedProcessor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);
                    var llmRecord = new LlmRecord
                    {
                        Step = "STEP3",
                        Provider = "Gpt",
                        Model = model,
                        InputToken = gptResponse?.Usage?.PromptTokens ?? 0,
                        OutputToken = gptResponse?.Usage?.CompletionTokens ?? 0,
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
                        Provider = "Gpt",
                        LlmRecord = llmRecord
                    };
                }
                catch (Exception modelException) when (modelException is not StepExecutionException)
                {
                    lastException = modelException;
                    _logger.Info("gpt model failed. model: {}, message: {}", model, modelException.Message);
                }
            }

            if (lastException is not null)
            {
                throw lastException;
            }

            throw new InvalidOperationException("GPT model 清單為空，請檢查 LlmSettings:Gpt:Model 設定");
        }
        catch (Exception exception) when (exception is not StepExecutionException)
        {
            _logger.Error(exception, "gpt query planning failed. question: {}", preprocessResult.NormalizedQuestion);
            throw new StepExecutionException(
                3,
                "LLM FormulaSpec 生成",
                $"[GPT] {exception.Message}",
                new Step3FailureDebugData
                {
                    SystemPrompt = systemPrompt,
                    LlmJson = lastLlmJson,
                    Model = lastModel
                },
                exception);
        }
    }

    private GptSettings GetSettings(string? provider)
    {
        var normalizedProvider = LlmProviderResolver.NormalizeProvider(provider);
        var providerCandidates = new List<string>
        {
            provider?.Trim() ?? string.Empty,
            normalizedProvider,
            "Gpt"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in providerCandidates)
        {
            var settings = _configuration.GetSection($"LlmSettings:{candidate}").Get<GptSettings>();
            if (settings is not null)
            {
                return settings;
            }
        }

        throw new InvalidOperationException($"GPT 設定不存在，已嘗試區段：{string.Join(", ", providerCandidates)}");
    }

    private static List<string> ParseModels(string modelSetting)
    {
        return modelSetting
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    public async Task<InsuranceChatCompletionResult> ExecuteAsync(string? llmProvider, string systemPrompt, string userMessage, CancellationToken cancellationToken = default)
    {
        var settings = GetSettings(llmProvider);
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

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.ApiKey);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    stopwatch.Stop();
                    var gptError = _sharedProcessor.TryReadApiErrorMessage(responseText);
                    throw new InvalidOperationException($"GPT({model}) API 失敗：{response.StatusCode}。{gptError ?? "請檢查 API Key、模型名稱與權限"}");
                }

                var gptResponse = JsonSerializer.Deserialize<GptChatResponse>(responseText);
                if (gptResponse?.Error is not null)
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"GPT({model}) 錯誤：{gptResponse.Error.Message}");
                }

                var textContent = gptResponse?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"GPT({model}) 回傳空內容");
                }

                stopwatch.Stop();
                var inputToken = gptResponse?.Usage?.PromptTokens ?? 0;
                var outputToken = gptResponse?.Usage?.CompletionTokens ?? 0;

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
                _logger.Info("gpt chat model failed. model: {}, message: {}", model, modelException.Message);
            }
        }

        throw lastException ?? new InvalidOperationException("GPT model 清單為空，請檢查 LlmSettings:Gpt:Model 設定");
    }
}