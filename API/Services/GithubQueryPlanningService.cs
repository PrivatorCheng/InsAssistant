using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using API.Contracts;
using API.Infrastructure;
using API.Models;

namespace API.Services;

/// <summary>
/// Github Models LLM 設定。
/// </summary>
public sealed class GithubSettings
{
    public required string Planner { get; set; }

    public required string Pat { get; set; }

    public required string Endpoint { get; set; }

    public required string Model { get; set; }
}

internal sealed class GithubChatResponse
{
    [JsonPropertyName("choices")]
    public List<GithubChoice>? Choices { get; set; }

    [JsonPropertyName("error")]
    public GithubError? Error { get; set; }

    [JsonPropertyName("usage")]
    public GithubUsage? Usage { get; set; }
}

internal sealed class GithubChoice
{
    [JsonPropertyName("message")]
    public GithubMessage? Message { get; set; }
}

internal sealed class GithubMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class GithubError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class GithubUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }
}

/// <summary>
/// 使用 Github Models REST API 生成 QuerySpec 的服務。
/// </summary>
public sealed class GithubQueryPlanningService : IQueryPlanningService, IInsuranceChatCompletionService
{
    private readonly IConfiguration _configuration;
    private readonly QueryPlanningPromptProvider _promptProvider;
    private readonly QueryPlanningSharedProcessor _sharedProcessor;
    private readonly CustomLogger _logger;
    private readonly HttpClient _httpClient;

    public GithubQueryPlanningService(
        IConfiguration configuration,
        QueryPlanningPromptProvider promptProvider,
        QueryPlanningSharedProcessor sharedProcessor,
        ILogger<GithubQueryPlanningService> logger)
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
        var settings = GetSettings("Github");
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

                    var endpoint = settings.Endpoint.TrimEnd('/');
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(requestPayload),
                            System.Text.Encoding.UTF8,
                            "application/json")
                    };

                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Pat);

                    var response = await _httpClient.SendAsync(request, cancellationToken);
                    var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                    _logger.Info("github api response. model: {}, status: {}, content-type: {}, response-length: {}", model, (int)response.StatusCode, response.Content.Headers.ContentType, responseText.Length);

                    if (!response.IsSuccessStatusCode)
                    {
                        var githubError = _sharedProcessor.TryReadApiErrorMessage(responseText);
                        _logger.Info("github api failed. model: {}, status: {}, response: {}", model, (int)response.StatusCode, responseText);
                        throw new InvalidOperationException($"Github Models({model}) API 失敗：{response.StatusCode}。{githubError ?? "請檢查 Endpoint、PAT、模型名稱與權限"}");
                    }

                    var githubResponse = JsonSerializer.Deserialize<GithubChatResponse>(responseText);
                    if (githubResponse?.Error is not null)
                    {
                        throw new InvalidOperationException($"Github Models({model}) 錯誤：{githubResponse.Error.Message}");
                    }

                    var textContent = githubResponse?.Choices?.FirstOrDefault()?.Message?.Content;
                    if (string.IsNullOrWhiteSpace(textContent))
                    {
                        throw new InvalidOperationException($"Github Models({model}) 回傳空內容");
                    }

                    var llmJson = _sharedProcessor.ExtractJsonText(textContent);
                    lastLlmJson = llmJson;
                    var formulaSpec = _sharedProcessor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);
                    var llmRecord = new LlmRecord
                    {
                        Step = "STEP3",
                        Provider = "Github",
                        Model = model,
                        InputToken = githubResponse?.Usage?.PromptTokens ?? 0,
                        OutputToken = githubResponse?.Usage?.CompletionTokens ?? 0,
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
                        Provider = "Github",
                        LlmRecord = llmRecord
                    };
                }
                catch (Exception modelException) when (modelException is not StepExecutionException)
                {
                    lastException = modelException;
                    _logger.Info("github model failed. model: {}, message: {}", model, modelException.Message);
                }
            }

            if (lastException is not null)
            {
                throw lastException;
            }

            throw new InvalidOperationException("Github Models model 清單為空，請檢查 LlmSettings:Github:Model 設定");
        }
        catch (Exception exception) when (exception is not StepExecutionException)
        {
            _logger.Error(exception, "github query planning failed. question: {}", preprocessResult.NormalizedQuestion);
            throw new StepExecutionException(
                3,
                "LLM FormulaSpec 生成",
                $"[Github Models] {exception.Message}",
                new Step3FailureDebugData
                {
                    SystemPrompt = systemPrompt,
                    LlmJson = lastLlmJson,
                    Model = lastModel
                },
                exception);
        }
    }

    private GithubSettings GetSettings(string? provider)
    {
        var normalizedProvider = LlmProviderResolver.NormalizeProvider(provider);
        var providerCandidates = new List<string>
        {
            provider?.Trim() ?? string.Empty,
            normalizedProvider,
            "Github",
            "GitHub"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in providerCandidates)
        {
            var settings = _configuration.GetSection($"LlmSettings:{candidate}").Get<GithubSettings>();
            if (settings is not null)
            {
                return settings;
            }
        }

        throw new InvalidOperationException($"Github 設定不存在，已嘗試區段：{string.Join(", ", providerCandidates)}");
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

                var endpoint = settings.Endpoint.TrimEnd('/');
                var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.Pat);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    stopwatch.Stop();
                    var githubError = _sharedProcessor.TryReadApiErrorMessage(responseText);
                    throw new InvalidOperationException($"Github Models({model}) API 失敗：{response.StatusCode}。{githubError ?? "請檢查 Endpoint、PAT、模型名稱與權限"}");
                }

                var githubResponse = JsonSerializer.Deserialize<GithubChatResponse>(responseText);
                if (githubResponse?.Error is not null)
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"Github Models({model}) 錯誤：{githubResponse.Error.Message}");
                }

                var textContent = githubResponse?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    stopwatch.Stop();
                    throw new InvalidOperationException($"Github Models({model}) 回傳空內容");
                }

                stopwatch.Stop();
                var inputToken = githubResponse?.Usage?.PromptTokens ?? 0;
                var outputToken = githubResponse?.Usage?.CompletionTokens ?? 0;

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
                _logger.Info("github chat model failed. model: {}, message: {}", model, modelException.Message);
            }
        }

        throw lastException ?? new InvalidOperationException("Github Models model 清單為空，請檢查 LlmSettings:Github:Model 設定");
    }
}
