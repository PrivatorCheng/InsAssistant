using API.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace API.Tests;

public class QueryPlanningPromptProviderTests
{
    [Fact]
    public void BuildSystemPrompt_ShouldInjectFormulaObject_WithFormulaNameAndFormula()
    {
        var rootPath = CreateTempRoot();

        try
        {
            var docPath = Path.Combine(rootPath, "doc");
            Directory.CreateDirectory(docPath);
            Directory.CreateDirectory(Path.Combine(docPath, "db_layout"));

            File.WriteAllText(Path.Combine(docPath, "Step3Prompt.txt"), "公式\n{{FORMULA_LIST}}");
            File.WriteAllText(Path.Combine(docPath, "FormulaDescription.txt"), """
[
  {
    "formula_name": "預估精準度",
    "formula": {
      "operator": "DIVIDE",
      "left": "賠付金額",
      "right": "預估金額"
    }
  }
]
""");

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(Path.Combine(rootPath, "API"));

            var provider = new QueryPlanningPromptProvider(environment.Object);
            var prompt = provider.BuildSystemPrompt(
                "依險種和受理日期計算預估精準度",
                ["預估精準度"],
                "clam_clm_main, clam_stl_instype",
                "-",
                ["clam_clm_main", "clam_stl_instype"],
                "[]",
                null);

            Assert.Contains("\"formula_name\": \"預估精準度\"", prompt, StringComparison.Ordinal);
            Assert.Contains("\"formula\": {", prompt, StringComparison.Ordinal);
            Assert.DoesNotContain("{{FORMULA_LIST}}", prompt, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static string CreateTempRoot()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"query-planning-prompt-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "API"));
        return rootPath;
    }
}
