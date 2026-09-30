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
/// 使用 Github Models REST API 的對話完成服務。
/// </summary>
public sealed class GithubChatCompletionService : IInsuranceChatCompletionService
{
    private readonly IConfiguration _configuration;
    private readonly CustomLogger _logger;
    private readonly HttpClient _httpClient;

    public GithubChatCompletionService(
        IConfiguration configuration,
        ILogger<GithubChatCompletionService> logger)
    {
        _configuration = configuration;
        _logger = logger.ToCustomLogger();
        _httpClient = new HttpClient();
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

    public async Task<InsuranceChatCompletionResult> ExecuteAsync(
        string? llmProvider,
        LlmPromptEnvelope prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

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
                    messages = BuildMessages(prompt)
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
                    var githubError = TryReadApiErrorMessage(responseText);
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

    private static string? TryReadApiErrorMessage(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static object[] BuildMessages(LlmPromptEnvelope prompt)
    {
        var messages = new List<object>
        {
            new { role = "system", content = prompt.SystemPrompt }
        };

        if (!string.IsNullOrWhiteSpace(prompt.ContextText))
        {
            messages.Add(new { role = "user", content = $"[CONTEXT]\n{prompt.ContextText}" });
        }

        if (!string.IsNullOrWhiteSpace(prompt.HistoryText))
        {
            messages.Add(new { role = "user", content = $"[HISTORY]\n{prompt.HistoryText}" });
        }

        messages.Add(new { role = "user", content = prompt.UserText });
        return messages.ToArray();
    }
}
