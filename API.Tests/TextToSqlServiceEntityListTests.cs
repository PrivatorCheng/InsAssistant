using System.Reflection;
using System.Text.Json;
using API.Contracts;
using API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace API.Tests;

public class TextToSqlServiceEntityListTests
{
    [Fact]
    public async Task BuildEntityListTextAsync_ShouldExpandPersonCityAndSkipUnit()
    {
        var reportRepository = new Mock<IReportRepository>(MockBehavior.Strict);
        reportRepository
            .Setup(repository => repository.QueryAsync(
                "SELECT user_id FROM sysp_user WHERE user_name = @userName",
                It.Is<Dictionary<string, object?>>(parameters =>
                    parameters.ContainsKey("@userName") && Equals(parameters["@userName"], "王小明")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase) { ["user_id"] = "U001" }
            });

        reportRepository
            .Setup(repository => repository.QueryAsync(
                "SELECT post_code FROM clap_postcode WHERE city_name LIKE @cityPrefix",
                It.Is<Dictionary<string, object?>>(parameters =>
                    parameters.ContainsKey("@cityPrefix") && Equals(parameters["@cityPrefix"], "台北%")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase) { ["post_code"] = "100" },
                new(StringComparer.OrdinalIgnoreCase) { ["post_code"] = "103" }
            });

        reportRepository
            .Setup(repository => repository.QueryAsync(
                "SELECT post_code FROM clap_postcode WHERE city_name LIKE @cityPrefix",
                It.Is<Dictionary<string, object?>>(parameters =>
                    parameters.ContainsKey("@cityPrefix") && Equals(parameters["@cityPrefix"], "臺北%")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase) { ["post_code"] = "200" }
            });

        var service = CreateService(reportRepository.Object);
        var entityGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PERSON"] = ["王小明"],
            ["CITY"] = ["台北"],
            ["UNIT"] = ["北一"]
        };

        var jsonText = await InvokeBuildEntityListTextAsync(service, entityGroups);
        var items = ReadEntityListItems(jsonText);

        Assert.Equal(4, items.Count);
        Assert.Contains(items, item => item["ENTITY_NAME"] == "王小明" && item["ENTITY_ID"] == "U001" && item["DATA_TYPE"] == "經辦人員");
        Assert.Contains(items, item => item["ENTITY_NAME"] == "台北" && item["ENTITY_ID"] == "100" && item["DATA_TYPE"] == "郵遞區號");
        Assert.Contains(items, item => item["ENTITY_NAME"] == "台北" && item["ENTITY_ID"] == "103" && item["DATA_TYPE"] == "郵遞區號");
        Assert.Contains(items, item => item["ENTITY_NAME"] == "台北" && item["ENTITY_ID"] == "200" && item["DATA_TYPE"] == "郵遞區號");
        Assert.DoesNotContain(items, item => item["ENTITY_NAME"] == "北一");

        reportRepository.VerifyAll();
    }

    [Fact]
    public async Task BuildEntityListTextAsync_ShouldFallbackPersonToInsuredName_WhenUserNotFound()
    {
        var reportRepository = new Mock<IReportRepository>(MockBehavior.Strict);
        reportRepository
            .Setup(repository => repository.QueryAsync(
                "SELECT user_id FROM sysp_user WHERE user_name = @userName",
                It.Is<Dictionary<string, object?>>(parameters =>
                    parameters.ContainsKey("@userName") && Equals(parameters["@userName"], "陳美麗")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>());

        var service = CreateService(reportRepository.Object);
        var entityGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PERSON"] = ["陳美麗"]
        };

        var jsonText = await InvokeBuildEntityListTextAsync(service, entityGroups);
        var items = ReadEntityListItems(jsonText);

        Assert.Single(items);
        Assert.Equal("陳美麗", items[0]["ENTITY_NAME"]);
        Assert.Equal("陳美麗", items[0]["ENTITY_ID"]);
        Assert.Equal("被保險人名稱", items[0]["DATA_TYPE"]);

        reportRepository.VerifyAll();
    }

    [Fact]
    public async Task BuildEntityListTextAsync_ShouldResolveInsureTypeToCodeValue()
    {
        var reportRepository = new Mock<IReportRepository>(MockBehavior.Strict);
        reportRepository
            .Setup(repository => repository.QueryAsync(
                "SELECT code_value FROM sysp_code WHERE code_type = 'INSURE_TYPE' AND code_name LIKE @codeNamePrefix",
                It.Is<Dictionary<string, object?>>(parameters =>
                    parameters.ContainsKey("@codeNamePrefix") && Equals(parameters["@codeNamePrefix"], "車險%")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase) { ["code_value"] = "CAR001" }
            });

        var service = CreateService(reportRepository.Object);
        var entityGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["INSURE_TYPE"] = ["車險"]
        };

        var jsonText = await InvokeBuildEntityListTextAsync(service, entityGroups);
        var items = ReadEntityListItems(jsonText);

        Assert.Single(items);
        Assert.Equal("車險", items[0]["ENTITY_NAME"]);
        Assert.Equal("CAR001", items[0]["ENTITY_ID"]);
        Assert.Equal("險類", items[0]["DATA_TYPE"]);

        reportRepository.VerifyAll();
    }

    [Fact]
    public void BuildSystemPrompt_ShouldInjectEntityListIntoTemplate()
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(@"c:\Kevin\Source Code\TestNLReport_260618\API");

        var provider = new QueryPlanningPromptProvider(environment.Object);
        var prompt = provider.BuildSystemPrompt(
            "查詢賠案",
            ["賠案"],
            "clam_clm_main",
            "### clam_clm_main",
            ["clam_clm_main"],
            "[{\"ENTITY_NAME\":\"王小明\",\"ENTITY_ID\":\"U001\",\"DATA_TYPE\":\"經辦人員\"}]",
            null);

        Assert.Contains("王小明", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{ENTITY_LIST}}", prompt, StringComparison.Ordinal);
    }

    private static TextToSqlService CreateService(IReportRepository reportRepository)
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(x => x.ContentRootPath).Returns(@"c:\Kevin\Source Code\TestNLReport_260618\API");

        return new TextToSqlService(
            Mock.Of<ILayoutRetrievalService>(),
            null!,
            null!,
            null!,
            null!,
            null!,
            Mock.Of<IQueryCompilerService>(),
            reportRepository,
            Mock.Of<IResultDecorationService>(),
            configuration,
            environment.Object,
            Mock.Of<ILogger<TextToSqlService>>());
    }

    private static async Task<string> InvokeBuildEntityListTextAsync(TextToSqlService service, Dictionary<string, List<string>> entityGroups)
    {
        var method = typeof(TextToSqlService).GetMethod(
            "BuildEntityListTextAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var task = (Task<string>)method!.Invoke(service, [entityGroups, CancellationToken.None])!;
        return await task;
    }

    private static List<Dictionary<string, string>> ReadEntityListItems(string jsonText)
    {
        using var document = JsonDocument.Parse(jsonText);
        return document.RootElement
            .EnumerateArray()
            .Select(item => item.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }
}