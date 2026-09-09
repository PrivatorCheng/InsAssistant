namespace API.Services;

internal static class LlmProviderResolver
{
    private static readonly IReadOnlyDictionary<string, string> ProviderAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GoogleGemini"] = "GoogleGemini",
            ["Gemini"] = "GoogleGemini",
            ["Groq"] = "Groq",
            ["Gpt"] = "Gpt",
            ["OpenAI"] = "Gpt",
            ["Github"] = "Github",
            ["GitHub"] = "Github",
            ["NIM"] = "Nim",
            ["Nim"] = "Nim",
            ["NvidiaNim"] = "Nim",
            ["MyLlama"] = "MyLlama",
            ["Llama"] = "MyLlama",
            ["Ollama"] = "MyLlama"
        };

    public static string ResolveProvider(IConfiguration configuration)
    {
        var provider = configuration["LlmSettings:Provider"]
            ?? configuration["LlmSettings:Privider"]
            ?? configuration["LlmSettings:Privoder"]
            ?? "GoogleGemini";

        return NormalizeProvider(provider);
    }

    public static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return "GoogleGemini";
        }

        var normalized = provider.Trim();
        return ProviderAliases.TryGetValue(normalized, out var canonical)
            ? canonical
            : normalized;
    }

    public static string NormalizePlanner(string? planner)
    {
        if (string.IsNullOrWhiteSpace(planner))
        {
            return "Gemini";
        }

        var normalized = planner.Trim();
        if (normalized.Equals("Gtp", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return "Gpt";
        }

        if (normalized.Equals("GoogleGemini", StringComparison.OrdinalIgnoreCase))
        {
            return "Gemini";
        }

        if (normalized.Equals("GitHub", StringComparison.OrdinalIgnoreCase))
        {
            return "Github";
        }

        if (normalized.Equals("NIM", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Nim", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("NvidiaNim", StringComparison.OrdinalIgnoreCase))
        {
            return "Nim";
        }

        if (normalized.Equals("Llama", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            return "MyLlama";
        }

        return normalized;
    }

    public static string ResolvePlanner(IConfiguration configuration, string provider)
    {
        var planner = configuration[$"LlmSettings:{provider}:Planner"]
            ?? configuration[$"LlmSettings:{provider}:planner"]
            ?? configuration[$"LlmSettings:{provider}:PlannerName"]
            ?? provider;

        return NormalizePlanner(planner);
    }

    public static List<string> ResolveAvailableProviders(IConfiguration configuration)
    {
        var llmSection = configuration.GetSection("LlmSettings");
        var providers = llmSection.GetChildren()
            .Select(section => section.Key)
            .Where(key => IsProviderSection(llmSection.GetSection(key)))
            .Select(NormalizeProvider)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (providers.Count == 0)
        {
            providers.Add(ResolveProvider(configuration));
        }

        return providers;
    }

    public static List<API.Models.LlmProviderOptionItem> ResolveAvailableProviderOptions(IConfiguration configuration)
    {
        var llmSection = configuration.GetSection("LlmSettings");
        var options = llmSection.GetChildren()
            .Where(IsProviderSection)
            .Select(section => new API.Models.LlmProviderOptionItem
            {
                Provider = NormalizeProvider(section.Key),
                DisplayFlag = section.GetValue<bool?>("DisplayFlag") ?? true
            })
            .GroupBy(option => option.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(option => option.Provider, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            options.Add(new API.Models.LlmProviderOptionItem
            {
                Provider = ResolveProvider(configuration),
                DisplayFlag = true
            });
        }

        return options;
    }

    private static bool IsProviderSection(IConfigurationSection section)
    {
        if (section.Key.Equals("Provider", StringComparison.OrdinalIgnoreCase)
            || section.Key.Equals("Privider", StringComparison.OrdinalIgnoreCase)
            || section.Key.Equals("Privoder", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var planner = section["Planner"]
            ?? section["planner"]
            ?? section["PlannerName"];

        return !string.IsNullOrWhiteSpace(planner);
    }
}
