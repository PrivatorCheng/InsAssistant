using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.Services;

/// <summary>
/// Gemini 脈絡快取初始化服務。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class GeminiContextCacheService : IGeminiContextCacheService, IHostedService
{
    private const string ContextCacheEnabledConfigPath = "LlmSettings:ContextCacheEnabled";
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly IInsuranceBrainService _insuranceBrainService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CustomLogger _logger;
    private readonly SemaphoreSlim _initializeSemaphore = new(1, 1);
    private volatile bool _isInitialized;
    private string? _currentCacheId;
    private string? _currentApiKey;

    public GeminiContextCacheService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IInsuranceBrainService insuranceBrainService,
        IServiceScopeFactory serviceScopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<GeminiContextCacheService> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _insuranceBrainService = insuranceBrainService;
        _serviceScopeFactory = serviceScopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger.ToCustomLogger();
    }

    public string? CachedContentName { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentCacheId) || string.IsNullOrWhiteSpace(_currentApiKey))
            {
                return;
            }

            var normalizedCacheId = _currentCacheId.StartsWith("/", StringComparison.Ordinal)
                ? _currentCacheId
                : $"/{_currentCacheId}";
            var deleteUri = $"https://googleapis.com{normalizedCacheId}?key={Uri.EscapeDataString(_currentApiKey)}";

            using var client = _httpClientFactory.CreateClient();
            using var response = await client.DeleteAsync(deleteUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.Info("gemini context cache delete returned non-success. cacheId: {}, status: {}, body: {}", _currentCacheId, (int)response.StatusCode, responseBody);
                return;
            }

            _logger.Info("gemini context cache deleted. cacheId: {}", _currentCacheId);
            _currentCacheId = null;
            CachedContentName = null;
            _isInitialized = false;
        }
        catch (Exception exception)
        {
            // 關閉流程以穩定性優先，刪除失敗僅記錄不阻斷主應用關閉。
            _logger.Error(exception, "gemini context cache delete failed during stop. cacheId: {}", _currentCacheId);
        }
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsContextCacheEnabled())
        {
            return;
        }

        if (_isInitialized)
        {
            return;
        }

        await _initializeSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            var settings = ResolveGeminiSettings();
            var requestTime = DateTime.Now;
            var sessionId = $"CACHE{Guid.NewGuid():N}";

            var model = ResolveFirstModel(settings.Model);
            var template = LoadPromptCacheTemplate();
            var claimReferenceData = LoadClaimReferenceData();
            var renderedPrompt = ApplyPromptTokens(template, _insuranceBrainService.AllProductsJson, claimReferenceData);

            _logger.Info("gemini context cache creating. model: {}, promptLength: {}", model, renderedPrompt.Length);

            var client = new Client(apiKey: settings.ApiKey);
            var cacheConfig = new CreateCachedContentConfig
            {
                DisplayName = $"insurance-cache-{DateTime.UtcNow:yyyyMMddHHmmss}",
                Ttl = "1200s",
                Contents = new List<Content>
                {
                    new()
                    {
                        Role = "user",
                        Parts = new List<Part>
                        {
                            new() { Text = renderedPrompt }
                        }
                    }
                }
            };

            var cache = await client.Caches.CreateAsync(model: model, config: cacheConfig);
            if (string.IsNullOrWhiteSpace(cache?.Name))
            {
                throw new InvalidOperationException("Gemini cache 建立成功但未取得 cache name");
            }

            CachedContentName = cache.Name;
            _currentCacheId = cache.Name;
            _currentApiKey = settings.ApiKey;
            _isInitialized = true;
            _logger.Info("gemini context cache initialized. model: {}, cacheName: {}", model, CachedContentName);

            await LogContextCacheInitAsync(
                sessionId,
                provider: "GoogleGemini",
                model,
                inputToken: 0,
                outputToken: 0,
                cacheLength: 0,
                durationMs: (long)(DateTime.Now - requestTime).TotalMilliseconds,
                promptLength: renderedPrompt.Length,
                responseLength: CachedContentName.Length,
                successFlag: true,
                requestTime,
                DateTime.Now,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "gemini context cache initialize failed");
            await LogContextCacheInitAsync(
                $"CACHE{Guid.NewGuid():N}",
                provider: "GoogleGemini",
                model: "Unknown",
                inputToken: 0,
                outputToken: 0,
                cacheLength: 0,
                durationMs: 0,
                promptLength: 0,
                responseLength: 0,
                successFlag: false,
                requestTime: DateTime.Now,
                responseTime: DateTime.Now,
                cancellationToken);
            throw;
        }
        finally
        {
            _initializeSemaphore.Release();
        }
    }

    private async Task LogContextCacheInitAsync(
        string sessionId,
        string provider,
        string model,
        int inputToken,
        int outputToken,
        int cacheLength,
        long durationMs,
        int promptLength,
        int responseLength,
        bool successFlag,
        DateTime requestTime,
        DateTime responseTime,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var llmLogService = scope.ServiceProvider.GetRequiredService<ILlmLogService>();
            await llmLogService.LogLlmCallAsync(
                sessionId,
                provider,
                model,
                inputToken,
                outputToken,
                cacheLength,
                durationMs,
                promptLength,
                responseLength,
                successFlag,
                requestTime,
                responseTime,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "gemini context cache llm log write failed. sessionId: {}, model: {}", sessionId, model);
        }
    }

    private string LoadPromptCacheTemplate()
    {
        var rootBasedPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", "Prompt_Cache.txt"));
        var apiBasedPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "doc", "Prompt_Cache.txt"));

        var resolvedPath = System.IO.File.Exists(rootBasedPath)
            ? rootBasedPath
            : System.IO.File.Exists(apiBasedPath)
                ? apiBasedPath
                : null;

        if (resolvedPath is null)
        {
            throw new FileNotFoundException("找不到 Prompt_Cache.txt", $"已嘗試: {rootBasedPath} 與 {apiBasedPath}");
        }

        return System.IO.File.ReadAllText(resolvedPath);
    }

    private string LoadClaimReferenceData()
    {
        var sourceDirectory = ResolveClaimRuleDirectory();
        var txtFiles = Directory
            .GetFiles(sourceDirectory, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (txtFiles.Count == 0)
        {
            _logger.Info("claim rule txt not found. sourceDirectory: {}", sourceDirectory);
            return string.Empty;
        }

        var contentBlocks = txtFiles
            .Select(path => $"### {Path.GetFileName(path)}{System.Environment.NewLine}{System.IO.File.ReadAllText(path)}")
            .ToList();

        return string.Join(System.Environment.NewLine + System.Environment.NewLine, contentBlocks);
    }

    private string ResolveClaimRuleDirectory()
    {
        var rootBasedPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", "ClaimRule"));
        if (Directory.Exists(rootBasedPath))
        {
            return rootBasedPath;
        }

        var apiBasedPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "doc", "ClaimRule"));
        if (Directory.Exists(apiBasedPath))
        {
            return apiBasedPath;
        }

        throw new DirectoryNotFoundException($"找不到 ClaimRule 目錄，已嘗試: {rootBasedPath} 與 {apiBasedPath}");
    }

    private static string ApplyPromptTokens(string template, string allProductsJson, string claimReferenceData)
    {
        return template
            .Replace("{brain.AllProductsJson}", allProductsJson, StringComparison.OrdinalIgnoreCase)
            .Replace("{{brain.AllProductsJson}}", allProductsJson, StringComparison.OrdinalIgnoreCase)
            .Replace("{CLAIM_REFERENCE_DATA}", claimReferenceData, StringComparison.OrdinalIgnoreCase)
            .Replace("{{CLAIM_REFERENCE_DATA}}", claimReferenceData, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFirstModel(string modelSetting)
    {
        var model = modelSetting
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("LlmSettings:GoogleGemini:Model 設定不可為空");
        }

        return model;
    }

    private GoogleGeminiSettings ResolveGeminiSettings()
    {
        var settings = _configuration.GetSection("LlmSettings:GoogleGemini").Get<GoogleGeminiSettings>();
        if (settings is not null)
        {
            return settings;
        }

        throw new InvalidOperationException("Gemini cache 設定不存在，請設定 LlmSettings:GoogleGemini 區段");
    }

    private bool IsContextCacheEnabled()
    {
        return _configuration.GetValue<bool>(ContextCacheEnabledConfigPath);
    }
}
