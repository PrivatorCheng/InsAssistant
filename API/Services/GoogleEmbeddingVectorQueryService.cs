using System.Diagnostics;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using Google.GenAI;
using Google.GenAI.Types;

namespace API.Services;

/// <summary>
/// Google Embedding 向量查詢服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class GoogleEmbeddingVectorQueryService : IGoogleEmbeddingVectorQueryService
{
    private readonly IConfiguration _configuration;
    private readonly ILlmLogService _llmLogService;
    private readonly CustomLogger _logger;

    public GoogleEmbeddingVectorQueryService(
        IConfiguration configuration,
        ILlmLogService llmLogService,
        ILogger<GoogleEmbeddingVectorQueryService> logger)
    {
        _configuration = configuration;
        _llmLogService = llmLogService;
        _logger = logger.ToCustomLogger();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return Array.Empty<ReadOnlyMemory<float>>();
        }

        var settings = ResolveSettings();
        var normalizedTexts = texts
            .Select(text => text?.Trim() ?? string.Empty)
            .ToList();

        if (normalizedTexts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("texts 不可包含空值", nameof(texts));
        }

        var effectiveSessionId = $"EMB{Guid.NewGuid():N}";
        var requestTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var vectors = await ExecuteEmbeddingRequestAsync(normalizedTexts, settings, cancellationToken);
            stopwatch.Stop();

            await LogEmbeddingCallAsync(
                effectiveSessionId,
                settings.EmbeddingModel,
                query: string.Join("\n", normalizedTexts),
                responseLength: vectors.Sum(vector => vector.Length),
                successFlag: true,
                requestTime: requestTime,
                responseTime: DateTime.Now,
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            return vectors;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            await LogEmbeddingCallAsync(
                effectiveSessionId,
                settings.EmbeddingModel,
                query: string.Join("\n", normalizedTexts),
                responseLength: exception.Message.Length,
                successFlag: false,
                requestTime: requestTime,
                responseTime: DateTime.Now,
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            _logger.Error(exception, "google embedding generate vectors failed. sessionId: {}", effectiveSessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<GoogleEmbeddingVectorQueryResult> QueryAsync(
        string query,
        IReadOnlyList<GoogleEmbeddingVectorCandidate> candidates,
        int topK = 5,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query 不可為空", nameof(query));
        }

        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("candidates 不可為空", nameof(candidates));
        }

        var normalizedCandidates = candidates
            .Select(candidate => new GoogleEmbeddingVectorCandidate
            {
                Id = candidate.Id,
                Text = (candidate.Text ?? string.Empty).Trim()
            })
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Text))
            .ToList();

        if (normalizedCandidates.Count == 0)
        {
            throw new ArgumentException("candidates 皆為空白內容", nameof(candidates));
        }

        var normalizedTopK = Math.Max(1, topK);
        var effectiveTopK = Math.Min(normalizedTopK, normalizedCandidates.Count);
        var settings = ResolveSettings();
        var effectiveSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? $"EMB{Guid.NewGuid():N}"
            : sessionId.Trim();

        var embeddingRequestTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var embeddingInputs = new List<string>(normalizedCandidates.Count + 1)
            {
                query.Trim()
            };
            embeddingInputs.AddRange(normalizedCandidates.Select(candidate => candidate.Text));

            var vectors = await ExecuteEmbeddingRequestAsync(embeddingInputs, settings, cancellationToken);
            if (vectors.Count != embeddingInputs.Count)
            {
                throw new InvalidOperationException("向量查詢失敗：向量數量與輸入數量不一致。");
            }

            var queryVector = vectors[0].Span;
            var matches = new List<GoogleEmbeddingVectorMatch>(normalizedCandidates.Count);
            for (var index = 0; index < normalizedCandidates.Count; index++)
            {
                var candidate = normalizedCandidates[index];
                var candidateVector = vectors[index + 1].Span;
                var score = CalculateCosineSimilarity(queryVector, candidateVector);

                matches.Add(new GoogleEmbeddingVectorMatch
                {
                    Id = candidate.Id,
                    Text = candidate.Text,
                    Score = score
                });
            }

            var topMatches = matches
                .OrderByDescending(match => match.Score)
                .Take(effectiveTopK)
                .ToList();

            stopwatch.Stop();

            await LogEmbeddingCallAsync(
                effectiveSessionId,
                settings.EmbeddingModel,
                query,
                responseLength: topMatches.Count,
                successFlag: true,
                requestTime: embeddingRequestTime,
                responseTime: DateTime.Now,
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            return new GoogleEmbeddingVectorQueryResult
            {
                SessionId = effectiveSessionId,
                EmbeddingModel = settings.EmbeddingModel,
                Query = query.Trim(),
                Matches = topMatches
            };
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            await LogEmbeddingCallAsync(
                effectiveSessionId,
                settings.EmbeddingModel,
                query,
                responseLength: exception.Message.Length,
                successFlag: false,
                requestTime: embeddingRequestTime,
                responseTime: DateTime.Now,
                durationMs: stopwatch.ElapsedMilliseconds,
                cancellationToken: cancellationToken);

            _logger.Error(exception, "google embedding vector query failed. sessionId: {}", effectiveSessionId);
            throw;
        }
    }

    private static async Task<IReadOnlyList<ReadOnlyMemory<float>>> ExecuteEmbeddingRequestAsync(
        IReadOnlyList<string> normalizedTexts,
        GoogleEmbeddingSettings settings,
        CancellationToken cancellationToken)
    {
        var client = new Client(apiKey: settings.ApiKey);
        var contents = normalizedTexts
            .Select(text => new Content
            {
                Parts =
                [
                    new Part { Text = text }
                ]
            })
            .ToList();

        var response = await client.Models.EmbedContentAsync(
            model: settings.EmbeddingModel,
            contents: contents,
            config: null,
            cancellationToken: cancellationToken);

        if (response.Embeddings == null || response.Embeddings.Count != normalizedTexts.Count)
        {
            throw new InvalidOperationException("Google Embedding 回傳結果數量不正確。");
        }

        return response.Embeddings
            .Select(embedding =>
            {
                var values = embedding.Values;
                if (values == null || values.Count == 0)
                {
                    throw new InvalidOperationException("Google Embedding 回傳空向量。");
                }

                var floatValues = values.Select(value => (float)value).ToArray();
                return new ReadOnlyMemory<float>(floatValues);
            })
            .ToList();
    }

    private async Task LogEmbeddingCallAsync(
        string sessionId,
        string embeddingModel,
        string query,
        int responseLength,
        bool successFlag,
        DateTime requestTime,
        DateTime responseTime,
        long durationMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var logErrorMessage = await _llmLogService.LogLlmCallAsync(
                sessionId,
                provider: "GoogleEmbedding",
                model: embeddingModel,
                inputToken: 0,
                outputToken: 0,
                cacheLength: 0,
                durationMs: Math.Max(0, durationMs),
                promptLength: query.Length,
                responseLength: Math.Max(0, responseLength),
                successFlag,
                requestTime,
                responseTime,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(logErrorMessage))
            {
                _logger.Info("google embedding llm log save failed. sessionId: {}, message: {}", sessionId, logErrorMessage);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "google embedding llm log write failed. sessionId: {}", sessionId);
        }
    }

    private GoogleEmbeddingSettings ResolveSettings()
    {
        var apiKey = _configuration["GoogleEmbedding:ApiKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("缺少 GoogleEmbedding:ApiKey 設定。");
        }

        var embeddingModel = _configuration["GoogleEmbedding:EmbeddingModel"]?.Trim();
        if (string.IsNullOrWhiteSpace(embeddingModel))
        {
            throw new InvalidOperationException("缺少 GoogleEmbedding:EmbeddingModel 設定。");
        }

        var chatModel = _configuration["GoogleEmbedding:ChatModel"]?.Trim() ?? string.Empty;

        return new GoogleEmbeddingSettings
        {
            ApiKey = apiKey,
            ChatModel = chatModel,
            EmbeddingModel = embeddingModel
        };
    }

    private static double CalculateCosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        if (left.Length != right.Length)
        {
            throw new InvalidOperationException("向量維度不一致，無法計算相似度。");
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var index = 0; index < left.Length; index++)
        {
            var leftValue = left[index];
            var rightValue = right[index];
            dot += leftValue * rightValue;
            leftNorm += leftValue * leftValue;
            rightNorm += rightValue * rightValue;
        }

        if (leftNorm <= 0 || rightNorm <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    /// <summary>
    /// Google Embedding 設定。
    /// </summary>
    private sealed class GoogleEmbeddingSettings
    {
        /// <summary>
        /// API 金鑰。
        /// </summary>
        public required string ApiKey { get; set; }

        /// <summary>
        /// 對話模型名稱。
        /// </summary>
        public required string ChatModel { get; set; }

        /// <summary>
        /// Embedding 模型名稱。
        /// </summary>
        public required string EmbeddingModel { get; set; }
    }
}