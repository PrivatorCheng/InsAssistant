using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace API.Repositories;

/// <summary>
/// 車險商品 JSON 檔案讀取儲存庫。
/// </summary>
[Service(ServiceLifetime.Singleton)]
public sealed class InsuranceProductRepository : IInsuranceProductRepository
{
    private static readonly JsonSerializerOptions UnescapedJsonSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly IWebHostEnvironment _environment;
    private readonly CustomLogger _logger;

    public InsuranceProductRepository(
        IWebHostEnvironment environment,
        ILogger<InsuranceProductRepository> logger)
    {
        _environment = environment;
        _logger = logger.ToCustomLogger();
    }

    public string LoadAllProductsJson()
    {
        var sourceDirectory = ResolveSourceDirectory();
        var files = Directory
            .GetFiles(sourceDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
        {
            _logger.Info("insurance products json not found. sourceDirectory: {}", sourceDirectory);
            return "[]";
        }

        var productDocuments = new List<ProductDocumentItem>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            try
            {
                var jsonNode = JsonNode.Parse(text);
                RemoveEmbeddingBlocks(jsonNode);

                productDocuments.Add(new ProductDocumentItem
                {
                    FileName = Path.GetFileName(file),
                    Json = jsonNode,
                    RawText = null
                });
            }
            catch (JsonException)
            {
                productDocuments.Add(new ProductDocumentItem
                {
                    FileName = Path.GetFileName(file),
                    Json = null,
                    RawText = text
                });
            }
        }

        var mergedJson = JsonSerializer.Serialize(productDocuments, UnescapedJsonSerializerOptions);
        _logger.Info("insurance products json loaded. sourceDirectory: {}, fileCount: {}, payloadLength: {}", sourceDirectory, files.Count, mergedJson.Length);

        return mergedJson;
    }

    private static void RemoveEmbeddingBlocks(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var key in jsonObject.Select(property => property.Key).ToList())
            {
                if (key.Equals("Embedding", StringComparison.OrdinalIgnoreCase))
                {
                    jsonObject.Remove(key);
                    continue;
                }

                RemoveEmbeddingBlocks(jsonObject[key]);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                RemoveEmbeddingBlocks(item);
            }
        }
    }

    private string ResolveSourceDirectory()
    {
        var rootBasedPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", "VectorDBJson"));
        if (Directory.Exists(rootBasedPath))
        {
            return rootBasedPath;
        }

        var apiBasedPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "doc", "VectorDBJson"));
        if (Directory.Exists(apiBasedPath))
        {
            return apiBasedPath;
        }

        throw new DirectoryNotFoundException($"找不到車險商品目錄，已嘗試: {rootBasedPath} 與 {apiBasedPath}");
    }

    private sealed class ProductDocumentItem
    {
        public required string FileName { get; set; }

        public JsonNode? Json { get; set; }

        public string? RawText { get; set; }
    }
}
