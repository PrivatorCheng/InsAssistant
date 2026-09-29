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
    private readonly GeminiQueryPlanningService _geminiQueryPlanningService;
    private readonly GroqQueryPlanningService _groqQueryPlanningService;
    private readonly GptQueryPlanningService _gptQueryPlanningService;
    private readonly GithubQueryPlanningService _githubQueryPlanningService;
    private readonly NimQueryPlanningService _nimQueryPlanningService;
    private readonly MyLlamaQueryPlanningService _myLlamaQueryPlanningService;

    public InsuranceChatCompletionRouterService(
        IConfiguration configuration,
        GeminiQueryPlanningService geminiQueryPlanningService,
        GroqQueryPlanningService groqQueryPlanningService,
        GptQueryPlanningService gptQueryPlanningService,
        GithubQueryPlanningService githubQueryPlanningService,
        NimQueryPlanningService nimQueryPlanningService,
        MyLlamaQueryPlanningService myLlamaQueryPlanningService)
    {
        _configuration = configuration;
        _geminiQueryPlanningService = geminiQueryPlanningService;
        _groqQueryPlanningService = groqQueryPlanningService;
        _gptQueryPlanningService = gptQueryPlanningService;
        _githubQueryPlanningService = githubQueryPlanningService;
        _nimQueryPlanningService = nimQueryPlanningService;
        _myLlamaQueryPlanningService = myLlamaQueryPlanningService;
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
            return _gptQueryPlanningService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("Groq", StringComparison.OrdinalIgnoreCase))
        {
            return _groqQueryPlanningService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("Github", StringComparison.OrdinalIgnoreCase))
        {
            return _githubQueryPlanningService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("Nim", StringComparison.OrdinalIgnoreCase))
        {
            return _nimQueryPlanningService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        if (planner.Equals("MyLlama", StringComparison.OrdinalIgnoreCase))
        {
            return _myLlamaQueryPlanningService.ExecuteAsync(provider, prompt, cancellationToken);
        }

        return _geminiQueryPlanningService.ExecuteAsync(provider, prompt, cancellationToken);
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
            return _geminiQueryPlanningService.ExecuteWithInlineImageAsync(
                provider,
                prompt,
                imageBytes,
                mimeType,
                cancellationToken);
        }

        return ExecuteAsync(provider, prompt, cancellationToken);
    }
}
