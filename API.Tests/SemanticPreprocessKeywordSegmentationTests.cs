using API.Models;
using API.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace API.Tests;

public class SemanticPreprocessKeywordSegmentationTests
{
    [Fact]
    public void Process_ShouldExtractChineseKeywords_ForStep2Matching()
    {
        var rootPath = CreateTempRoot();

        try
        {
            var docPath = Path.Combine(rootPath, "doc");
            Directory.CreateDirectory(docPath);
            File.WriteAllText(Path.Combine(docPath, "synonyms.md"), "理賠|賠案");

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(Path.Combine(rootPath, "API"));

            var service = new SemanticPreprocessService(environment.Object);
            var request = new NaturalLanguageQueryRequest
            {
                Query = "請查詢賠案列表",
                DepartmentId = null,
                UserId = null
            };

            var result = service.Process(request);

            Assert.Contains("賠案", result.Keywords, StringComparer.Ordinal);
            Assert.DoesNotContain("請查詢賠案列表", result.Keywords, StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static string CreateTempRoot()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"text-to-sql-seg-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "API"));
        return rootPath;
    }
}
