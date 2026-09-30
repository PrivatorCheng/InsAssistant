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
/// 使用 NIM 的對話完成服務（OpenAI 相容 API）。
/// </summary>
public sealed class NimChatCompletionService : IInsuranceChatCompletionService
{
    private const string DefaultNimChatCompletionsEndpoint = "https://integrate.api.nvidia.com/v1/chat/completions";

    private readonly IConfiguration _configuration;
    private readonly CustomLogger _logger;
    private readonly HttpClient _httpClient;

    public NimChatCompletionService(
        IConfiguration configuration,
        ILogger<NimChatCompletionService> logger)
    {
        _configuration = configuration;
        _logger = logger.ToCustomLogger();
        _httpClient = new HttpClient();
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

    public async Task<InsuranceChatCompletionResult> ExecuteAsync(
        string? llmProvider,
        LlmPromptEnvelope prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

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
                    messages = BuildMessages(prompt)
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
                    var nimError = TryReadApiErrorMessage(responseText);
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