using API.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace API.Tests;

public class LayoutRetrievalServiceTests
{
    [Fact]
    public void RetrieveLayoutFiles_ShouldThrow_WhenNoKeywordMatchesAnyLayout()
    {
        var rootPath = CreateTempRoot();

        try
        {
            var layoutPath = Path.Combine(rootPath, "doc", "db_layout");
            Directory.CreateDirectory(layoutPath);
            File.WriteAllText(Path.Combine(layoutPath, "clam_clm_main.md"), "任意內容");
            File.WriteAllText(Path.Combine(layoutPath, "table_list.md"), """
# 資料表清單
| 資料表名稱 | 資料表說明 | 關鍵字 |
| clam_clm_main | 賠案資料主表 | 賠案, 保單 |

# Foreign Key List
| 主表 | 子表 | 關聯欄位 | 關係 |
""");

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(Path.Combine(rootPath, "API"));

            var service = new LayoutRetrievalService(environment.Object);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                service.RetrieveLayoutFiles(["完全不存在的關鍵字"]));

            Assert.Equal("關鍵字比對失敗", exception.Message);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void RetrieveLayoutFiles_ShouldReturnMatchedLayout_WhenKeywordMatches()
    {
        var rootPath = CreateTempRoot();

        try
        {
            var layoutPath = Path.Combine(rootPath, "doc", "db_layout");
            Directory.CreateDirectory(layoutPath);
            var matchedFile = Path.Combine(layoutPath, "clam_clm_main.md");
            File.WriteAllText(matchedFile, "任意內容");
            File.WriteAllText(Path.Combine(layoutPath, "table_list.md"), """
# 資料表清單
| 資料表名稱 | 資料表說明 | 關鍵字 |
| clam_clm_main | 賠案資料主表 | 賠案, 保單, 被保險人 |

# Foreign Key List
| 主表 | 子表 | 關聯欄位 | 關係 |
""");

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(Path.Combine(rootPath, "API"));

            var service = new LayoutRetrievalService(environment.Object);
            var result = service.RetrieveLayoutFiles(["賠案"]);

            Assert.Contains(matchedFile, result, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void RetrieveLayoutFiles_ShouldApplyEnterpriseTermBonus_ForAllMappedTables()
    {
        var rootPath = CreateTempRoot();

        try
        {
            var docPath = Path.Combine(rootPath, "doc");
            var layoutPath = Path.Combine(docPath, "db_layout");
            Directory.CreateDirectory(layoutPath);

            var firstFile = Path.Combine(layoutPath, "clam_clm_instype.md");
            var secondFile = Path.Combine(layoutPath, "clam_stl_instype.md");
            File.WriteAllText(firstFile, "任意內容");
            File.WriteAllText(secondFile, "任意內容");

            File.WriteAllText(Path.Combine(layoutPath, "table_list.md"), """
# 資料表清單
| 資料表名稱 | 資料表說明 | 關鍵字 |
| clam_clm_instype | 賠案預估險種資料表 | 預估 |
| clam_stl_instype | 計算書險種資料表 | 理賠 |

# Foreign Key List
| 主表 | 子表 | 關聯欄位 | 關係 |
""");

            File.WriteAllText(Path.Combine(docPath, "TermDescription.txt"), """
# 企業術語
| 術語名稱 | 資料表 | Prompt |
| 未決金額 | clam_clm_instype, clam_stl_instype | 任意 |
""");

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(Path.Combine(rootPath, "API"));

            var service = new LayoutRetrievalService(environment.Object);
            var result = service.RetrieveLayoutFiles(["未決金額"]);

            Assert.Contains(firstFile, result, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(secondFile, result, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void RetrieveLayoutFiles_ShouldMatch_WhenKeywordContainsTableKeyword()
    {
        var rootPath = CreateTempRoot();

        try
        {
            var layoutPath = Path.Combine(rootPath, "doc", "db_layout");
            Directory.CreateDirectory(layoutPath);
            var matchedFile = Path.Combine(layoutPath, "clam_clm_main.md");
            File.WriteAllText(matchedFile, "任意內容");
            File.WriteAllText(Path.Combine(layoutPath, "table_list.md"), """
# 資料表清單
| 資料表名稱 | 資料表說明 | 關鍵字 |
| clam_clm_main | 賠案資料主表 | 賠案, 受理 |

# Foreign Key List
| 主表 | 子表 | 關聯欄位 | 關係 |
""");

            var environment = new Mock<IWebHostEnvironment>();
            environment.SetupGet(x => x.ContentRootPath).Returns(Path.Combine(rootPath, "API"));

            var service = new LayoutRetrievalService(environment.Object);
            var result = service.RetrieveLayoutFiles(["受理日期"]);

            Assert.Contains(matchedFile, result, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static string CreateTempRoot()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"text-to-sql-layout-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "API"));
        return rootPath;
    }
}
