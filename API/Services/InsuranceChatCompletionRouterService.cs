using API.Attributes;
using API.Contracts;
using API.Models;

namespace API.Services;

/// <summary>
/// 對話用 LLM Provider 路由服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class InsuranceChatCompletionRouterService : IInsuranceChatCompletionService
{
    private readonly IConfiguration _configuration;
    private readonly GeminiChatCompletionService _geminiChatCompletionService;
    private readonly GroqChatCompletionService _groqChatCompletionService;
    private readonly GptChatCompletionService _gptChatCompletionService;
    private readonly GithubChatCompletionService _githubChatCompletionService;
    private readonly NimChatCompletionService _nimChatCompletionService;
    private readonly MyLlamaChatCompletionService _myLlamaChatCompletionService;

    public InsuranceChatCompletionRouterService(
        IConfiguration configuration,
        GeminiChatCompletionService geminiChatCompletionService,
        GroqChatCompletionService groqChatCompletionService,
        GptChatCompletionService gptChatCompletionService,
        GithubChatCompletionService githubChatCompletionService,
        NimChatCompletionService nimChatCompletionService,
        MyLlamaChatCompletionService myLlamaChatCompletionService)
    {
        _configuration = configuration;
        _geminiChatCompletionService = geminiChatCompletionService;
        _groqChatCompletionService = groqChatCompletionService;
        _gptChatCompletionService = gptChatCompletionService;
        _githubChatCompletionService = githubChatCompletionService;
        _nimChatCompletionService = nimChatCompletionService;
        _myLlamaChatCompletionService = myLlamaChatCompletionService;
    }

    public Task<InsuranceChatCompletionResult> ExecuteAsync(
        string? llmProvider,
        LlmPromptEnvelope prompt,
        CancellationToken cancellationToken = default)
    {
        var provider = string.IsNullOrWhiteSpace(llmProvider)
            ? LlmProviderResolver.ResolveProvider(_configuration)
            : LlmProviderResolver.NormalizeProvider(llmProvider);

        var planner = LlmProviderResolver.ResolvePlanner(_configuration, provider);

        if (planner.Equals("Gpt", StringComparison.OrdinalIgnoreCase))
        {
            return _gptChatCompletionService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("Groq", StringComparison.OrdinalIgnoreCase))
        {
            return _groqChatCompletionService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("Github", StringComparison.OrdinalIgnoreCase))
        {
            return _githubChatCompletionService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("Nim", StringComparison.OrdinalIgnoreCase))
        {
            return _nimChatCompletionService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("MyLlama", StringComparison.OrdinalIgnoreCase))
        {
            return _myLlamaChatCompletionService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        return _geminiChatCompletionService.ExecuteAsync(provider, prompt, cancellationToken);
    }

    public Task<InsuranceChatCompletionResult> ExecuteWithInlineImageAsync(
        string? llmProvider,
        LlmPromptEnvelope prompt,
        byte[] imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        var provider = string.IsNullOrWhiteSpace(llmProvider)
            ? LlmProviderResolver.ResolveProvider(_configuration)
            : LlmProviderResolver.NormalizeProvider(llmProvider);

        var planner = LlmProviderResolver.ResolvePlanner(_configuration, provider);

        if (planner.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return _geminiChatCompletionService.ExecuteWithInlineImageAsync(
                provider,
                prompt,
                imageBytes,
                mimeType,
                cancellationToken);
        }

        return ExecuteAsync(provider, prompt, cancellationToken);
    }
}
