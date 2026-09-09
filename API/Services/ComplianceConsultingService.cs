using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using API.Models.VectorDb;
using Microsoft.Extensions.VectorData;

namespace API.Services;

/// <summary>
/// 法遵諮詢向量查詢服務。
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class ComplianceConsultingService : IComplianceConsultingService
{
    private const string LawCollectionName = "insurance_regulation_compliance";
    private const string ProductClauseCollectionName = "insurance_product_clause";
    private const int DefaultTopCount = 5;

    private readonly IConfiguration _configuration;
    private readonly IGoogleEmbeddingVectorQueryService _googleEmbeddingVectorQueryService;
    private readonly VectorStore _vectorStore;
    private readonly CustomLogger _logger;

    public ComplianceConsultingService(
        IConfiguration configuration,
        IGoogleEmbeddingVectorQueryService googleEmbeddingVectorQueryService,
        VectorStore vectorStore,
        ILogger<ComplianceConsultingService> logger)
    {
        _configuration = configuration;
        _googleEmbeddingVectorQueryService = googleEmbeddingVectorQueryService;
        _vectorStore = vectorStore;
        _logger = logger.ToCustomLogger();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ComplianceLawReferenceSearchResult>> SearchLawReferencesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var questionVector = await GenerateQueryVectorAsync(query, cancellationToken);
        return await SearchLawReferencesAsync(questionVector, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ComplianceLawReferenceSearchResult>> SearchLawReferencesAsync(
        ReadOnlyMemory<float> queryVector,
        CancellationToken cancellationToken = default)
    {
        return await SearchLawReferencesAsyncCore(queryVector, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ComplianceProductClauseSearchResult>> SearchProductClausesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var questionVector = await GenerateQueryVectorAsync(query, cancellationToken);
        return await SearchProductClausesAsync(questionVector, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ComplianceProductClauseSearchResult>> SearchProductClausesAsync(
        ReadOnlyMemory<float> queryVector,
        CancellationToken cancellationToken = default)
    {
        var vectorCollection = _vectorStore.GetCollection<Guid, InsuranceProductClauseVectorRecord>(ProductClauseCollectionName);

        if (!await vectorCollection.CollectionExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Qdrant 集合不存在：{ProductClauseCollectionName}");
        }

        var topCount = ResolveTopCount();
        var searchOptions = new VectorSearchOptions<InsuranceProductClauseVectorRecord>
        {
            IncludeVectors = false
        };

        var results = new List<ComplianceProductClauseSearchResult>();
        await foreach (var item in vectorCollection.SearchAsync(queryVector, topCount, searchOptions, cancellationToken))
        {
            var record = item.Record;
            if (record == null)
            {
                continue;
            }

            results.Add(new ComplianceProductClauseSearchResult
            {
                ProductName = record.ProductName,
                Content = record.Content,
                ItemType = record.ItemType,
                MarketingTags = [.. record.MarketingTags]
            });
        }

        return results;
    }

    private async Task<IReadOnlyList<ComplianceLawReferenceSearchResult>> SearchLawReferencesAsyncCore(
        ReadOnlyMemory<float> questionVector,
        CancellationToken cancellationToken)
    {
        var vectorCollection = _vectorStore.GetCollection<Guid, InsuranceLawVectorRecord>(LawCollectionName);

        if (!await vectorCollection.CollectionExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Qdrant 集合不存在：{LawCollectionName}");
        }

        var topCount = ResolveTopCount();
        var searchOptions = new VectorSearchOptions<InsuranceLawVectorRecord>
        {
            IncludeVectors = false
        };

        var results = new List<ComplianceLawReferenceSearchResult>();
        await foreach (var item in vectorCollection.SearchAsync(questionVector, topCount, searchOptions, cancellationToken))
        {
            var record = item.Record;
            if (record == null)
            {
                continue;
            }

            results.Add(new ComplianceLawReferenceSearchResult
            {
                SourceNo = record.SourceNo,
                Guid = record.OriginalGuid,
                LawName = record.LawName,
                ArticleNumber = record.ArticleNumber,
                ArticleText = record.CleanText
            });
        }

        return results;
    }

    private async Task<ReadOnlyMemory<float>> GenerateQueryVectorAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query 不可為空", nameof(query));
        }

        var embeddings = await _googleEmbeddingVectorQueryService.GenerateEmbeddingsAsync(
            [query.Trim()],
            cancellationToken);

        var questionVector = embeddings.FirstOrDefault();
        if (questionVector.IsEmpty)
        {
            _logger.Info("compliance consulting query embedding is empty.");
            throw new InvalidOperationException("查詢向量化結果為空。");
        }

        return questionVector;
    }

    private int ResolveTopCount()
    {
        var configuredTopCount = _configuration.GetValue<int?>("ComplianceConsulting:TopK");
        return Math.Max(1, configuredTopCount ?? DefaultTopCount);
    }
}
