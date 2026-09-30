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
/// 使用 OpenAI GPT 的對話完成服務。
/// </summary>
public sealed class GptChatCompletionService : IInsuranceChatCompletionService
{
    private readonly IConfiguration _configuration;
    private readonly CustomLogger _logger;
    private readonly HttpClient _httpClient;

    public GptChatCompletionService(
        IConfiguration configuration,
        ILogger<GptChatCompletionService> logger)
    {
        _configuration = configuration;
        _logger = logger.ToCustomLogger();
        _httpClient = new HttpClient();
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
                    var gptError = TryReadApiErrorMessage(responseText);
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