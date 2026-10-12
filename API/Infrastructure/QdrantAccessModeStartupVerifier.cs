using System.Net;
using System.Net.Http.Json;

namespace API.Infrastructure;

/// <summary>
/// 啟動時驗證 Qdrant 存取模式是否符合唯讀要求。
/// </summary>
public sealed class QdrantAccessModeStartupVerifier : IHostedService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly QdrantAccessModeVerificationOptions _options;
    private readonly CustomLogger _logger;

    public QdrantAccessModeStartupVerifier(
        IHttpClientFactory httpClientFactory,
        QdrantAccessModeVerificationOptions options,
        ILogger<QdrantAccessModeStartupVerifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger.ToCustomLogger();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheme = _options.Https ? "https" : "http";
        var baseAddress = new Uri($"{scheme}://{_options.Host}:{_options.RestPort}/");

        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = baseAddress;
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Remove("api-key");
        client.DefaultRequestHeaders.Add("api-key", _options.ApiKey);

        await VerifyReadProbeAsync(client, cancellationToken);
        await VerifyWriteProbeAsync(client, cancellationToken);

        _logger.Info(
            "qdrant access mode verification passed. host: {}, restPort: {}, accessMode: {}",
            _options.Host,
            _options.RestPort,
            _options.AccessMode);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private async Task VerifyReadProbeAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("collections", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Qdrant 讀取驗測失敗，status={(int)response.StatusCode}，body={errorBody}");
        }
    }

    private async Task VerifyWriteProbeAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var collectionName = $"_access_probe_{Guid.NewGuid():N}";
        var payload = new
        {
            vectors = new
            {
                size = 1,
                distance = "Cosine"
            }
        };

        using var putResponse = await client.PutAsJsonAsync($"collections/{collectionName}", payload, cancellationToken);
        if (putResponse.IsSuccessStatusCode)
        {
            await TryCleanupProbeCollectionAsync(client, collectionName, cancellationToken);
            throw new InvalidOperationException(
                "Qdrant 寫入驗測意外成功，代表目前 API Key 可能具備寫入權限，違反唯讀要求。");
        }

        if (putResponse.StatusCode == HttpStatusCode.Forbidden || putResponse.StatusCode == HttpStatusCode.Unauthorized)
        {
            return;
        }

        var errorBody = await putResponse.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Qdrant 寫入驗測無法確認唯讀權限，status={(int)putResponse.StatusCode}，body={errorBody}");
    }

    private static async Task TryCleanupProbeCollectionAsync(
        HttpClient client,
        string collectionName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var _ = await client.DeleteAsync($"collections/{collectionName}", cancellationToken);
        }
        catch
        {
            // 寫入探測若意外成功，盡力清理；失敗不覆蓋主例外。
        }
    }
}

/// <summary>
/// Qdrant 啟動驗測設定。
/// </summary>
public sealed class QdrantAccessModeVerificationOptions
{
    public required string Host { get; init; }

    public required int RestPort { get; init; }

    public required bool Https { get; init; }

    public required string ApiKey { get; init; }

    public required string AccessMode { get; init; }
}
