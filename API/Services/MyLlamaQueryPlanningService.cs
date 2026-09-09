using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.Contracts;
using API.Infrastructure;
using API.Models;

namespace API.Services;

/// <summary>
/// MyLlama（本地 Ollama）LLM 設定。
/// </summary>
public sealed class MyLlamaSettings
{
    public required string Planner { get; set; }

    public required string Endpoint { get; set; }

    public required string Model { get; set; }
}

internal sealed class MyLlamaChatResponse
{
    [JsonPropertyName("message")]
    public MyLlamaMessage? Message { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }
}

internal sealed class MyLlamaMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

/// <summary>
/// 使用本地 MyLlama（Ollama）生成 QuerySpec 的服務。
/// </summary>
public sealed class MyLlamaQueryPlanningService : IQueryPlanningService, IInsuranceChatCompletionService
{
    private readonly IConfiguration _configuration;
    private readonly QueryPlanningPromptProvider _promptProvider;
    private readonly QueryPlanningSharedProcessor _sharedProcessor;
    private readonly CustomLogger _logger;
    private readonly HttpClient _httpClient;

    public MyLlamaQueryPlanningService(
        IConfiguration configuration,
        QueryPlanningPromptProvider promptProvider,
        QueryPlanningSharedProcessor sharedProcessor,
        ILogger<MyLlamaQueryPlanningService> logger)
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
        var settings = GetSettings("MyLlama");
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
                    var endpoint = settings.Endpoint.TrimEnd('/');
                    var requestPayload = new
                    {
                        model,
                        stream = false,
                        format = "json",
                        options = new { temperature = 0 },
                        messages = new object[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        }
                    };

                    var stopwatch = Stopwatch.StartNew();
                    using var content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        System.Text.Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{endpoint}/api/chat", content, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Info("myllama api failed. model: {}, status: {}, response: {}", model, (int)response.StatusCode, responseText);
                        throw new InvalidOperationException($"MyLlama({model}) API 失敗：{response.StatusCode}");
                    }

                    var llamaResponse = JsonSerializer.Deserialize<MyLlamaChatResponse>(responseText);
                    if (!string.IsNullOrWhiteSpace(llamaResponse?.Error))
                    {
                        throw new InvalidOperationException($"MyLlama({model}) 錯誤：{llamaResponse.Error}");
                    }

                    var textContent = llamaResponse?.Message?.Content;
                    if (string.IsNullOrWhiteSpace(textContent))
                    {
                        throw new InvalidOperationException($"MyLlama({model}) 回傳空內容");
                    }

                    var llmJson = _sharedProcessor.ExtractJsonText(textContent);
                    lastLlmJson = llmJson;
                    var formulaSpec = _sharedProcessor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);
                    var llmRecord = new LlmRecord
                    {
                        Step = "STEP3",
                        Provider = "MyLlama",
                        Model = model,
                        InputToken = llamaResponse?.PromptEvalCount ?? 0,
                        OutputToken = llamaResponse?.EvalCount ?? 0,
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
                        Provider = "MyLlama",
                        LlmRecord = llmRecord
                    };
                }
                catch (Exception modelException) when (modelException is not StepExecutionException)
                {
                    lastException = modelException;
                    _logger.Info("myllama model failed. model: {}, message: {}", model, modelException.Message);
                }
            }

            if (lastException is not null)
            {
                throw lastException;
            }

            throw new InvalidOperationException("MyLlama model 清單為空，請檢查 LlmSettings:MyLlama:Model 設定");
        }
        catch (Exception exception) when (exception is not StepExecutionException)
        {
            _logger.Error(exception, "myllama query planning failed. question: {}", preprocessResult.NormalizedQuestion);
            throw new StepExecutionException(
                3,
                "LLM FormulaSpec 生成",
                $"[MyLlama] {exception.Message}",
                new Step3FailureDebugData
                {
                    SystemPrompt = systemPrompt,
                    LlmJson = lastLlmJson,
                    Model = lastModel
                },
                exception);
        }
    }

    private MyLlamaSettings GetSettings(string? provider)
    {
        var normalizedProvider = LlmProviderResolver.NormalizeProvider(provider);
        var providerCandidates = new List<string>
        {
            provider?.Trim() ?? string.Empty,
            normalizedProvider,
            "MyLlama"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in providerCandidates)
        {
            var settings = _configuration.GetSection($"LlmSettings:{candidate}").Get<MyLlamaSettings>();
            if (settings is not null)
            {
                return settings;
            }
        }

        throw new InvalidOperationException($"MyLlama 設定不存在，已嘗試區段：{string.Join(", ", providerCandidates)}");
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

                var endpoint = settings.Endpoint.TrimEnd('/');
                var requestPayload = new
                {
                    model,
                    stream = false,
                    options = new { temperature = 0 },
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessage }
                    }
                };

                using var content = new StringContent(
                    JsonSerializer.Serialize(requestPayload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync($"{endpoint}/api/chat", content, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"MyLlama({model}) API 失敗：{response.StatusCode}");
                }

                var llamaResponse = JsonSerializer.Deserialize<MyLlamaChatResponse>(responseText);
                if (!string.IsNullOrWhiteSpace(llamaResponse?.Error))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"MyLlama({model}) 錯誤：{llamaResponse.Error}");
                }

                var textContent = llamaResponse?.Message?.Content;
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"MyLlama({model}) 回傳空內容");
                }

                stopwatch.Stop();

                return new InsuranceChatCompletionResult
                {
                    Content = textContent.Trim(),
                    Model = model,
                    Provider = llmProvider,
                    InputToken = 0,
                    OutputToken = 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    IsSuccess = true
                };
            }
            catch (Exception modelException)
            {
                lastException = modelException;
                _logger.Info("myllama chat model failed. model: {}, message: {}", model, modelException.Message);
            }
        }

        throw lastException ?? new InvalidOperationException("MyLlama model 清單為空，請檢查 LlmSettings:MyLlama:Model 設定");
    }
}
