using API.Contracts;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace API.Tests;

public class SemanticPreprocessServiceTests
{
    [Fact]
    public void Process_ShouldMaskPiiAndNormalizeLastYear()
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(m => m.ContentRootPath).Returns(System.IO.Directory.GetCurrentDirectory());
        var service = new SemanticPreprocessService(mockEnv.Object);
        var request = new NaturalLanguageQueryRequest
        {
            Query = "\u8ACB\u67E5\u53BB\u5E74 A123456789 \u7406\u8CE0\u6848\u4EF6\uFF0C\u806F\u7D61\u96FB\u8A71 0912345678",
            DepartmentId = "G1",
            UserId = "A123456789"
        };

        var result = service.Process(request);

        Assert.Contains("REDACTED_ID", result.NormalizedQuestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REDACTED_PHONE", result.NormalizedQuestion, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.StartDate);
        Assert.NotNull(result.EndDate);
        Assert.NotEmpty(result.PiiTokenMap);
    }
}

public class QueryCompilerServiceTests
{
    [Fact]
    public void Compile_ShouldGenerateParameterizedSqlWithNolockAndDepartmentFilter()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var service = new QueryCompilerService(whitelist);
        var querySpec = new QuerySpec
        {
            TargetTables = ["clam_clm_main"],
            Joins = new List<JoinDetail>(),
            DataFields = new List<DimensionDetail>(),
            Metrics =
            [
                new MetricDetail
                {
                    Table = "clam_clm_main",
                    Field = "iclaim",
                    Aggregation = "COUNT",
                    Alias = "claimCount"
                }
            ],
            Filters =
            [
                new FilterDetail
                {
                    Table = "clam_clm_main",
                    Field = "iclaim",
                    Operator = "EQUALS",
                    Value = ["[REDACTED_ID_1]"]
                }
            ],
            Sort = new List<SortDetail>(),
            Limit = 100
        };

        var result = service.Compile(
            querySpec,
            new Dictionary<string, string> { ["[REDACTED_ID_1]"] = "A123456789" },
            "D001");

        Assert.Contains("WITH (NOLOCK)", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TOP (100)", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[dept_id]", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@p0", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("A123456789", result.Parameters["@p0"]);
        Assert.Equal("D001", result.Parameters["@p1"]);
    }

    [Fact]
    public void Compile_ShouldGenerateParameterizedSqlForInOperator()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var service = new QueryCompilerService(whitelist);
        var querySpec = new QuerySpec
        {
            TargetTables = ["clam_clm_main"],
            Joins = new List<JoinDetail>(),
            DataFields = new List<DimensionDetail>(),
            Metrics =
            [
                new MetricDetail
                {
                    Table = "clam_clm_main",
                    Field = "iclaim",
                    Aggregation = "COUNT",
                    Alias = "claimCount"
                }
            ],
            Filters =
            [
                new FilterDetail
                {
                    Table = "clam_clm_main",
                    Field = "insure_type",
                    Operator = "IN",
                    Value = ["A", "B"]
                }
            ],
            Sort = new List<SortDetail>(),
            Limit = 100
        };

        var result = service.Compile(querySpec, new Dictionary<string, string>(), null);

        Assert.Contains("[t0].[insure_type] IN (@p0, @p1)", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("A", result.Parameters["@p0"]);
        Assert.Equal("B", result.Parameters["@p1"]);
    }

    [Fact]
    public void Compile_ShouldThrow_WhenFieldNotInWhitelist()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var service = new QueryCompilerService(whitelist);
        var querySpec = new QuerySpec
        {
            TargetTables = ["clam_clm_main"],
            Joins = new List<JoinDetail>(),
            DataFields = new List<DimensionDetail>(),
            Metrics =
            [
                new MetricDetail
                {
                    Table = "clam_clm_main",
                    Field = "unknown_field",
                    Aggregation = "COUNT",
                    Alias = "c1"
                }
            ],
            Filters = new List<FilterDetail>(),
            Sort = new List<SortDetail>(),
            Limit = 10
        };

        var action = () => service.Compile(querySpec, new Dictionary<string, string>(), null);
        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void Compile_ShouldOrderByAggregationExpression_WhenSortFieldMatchesMetricField()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var service = new QueryCompilerService(whitelist);
        var querySpec = new QuerySpec
        {
            TargetTables = ["clam_clm_main", "clam_stl_instype"],
            Joins =
            [
                new JoinDetail
                {
                    LeftTable = "clam_clm_main",
                    LeftField = "iclaim",
                    RightTable = "clam_stl_instype",
                    RightField = "iclaim",
                    JoinType = "INNER"
                }
            ],
            DataFields =
            [
                new DimensionDetail
                {
                    Table = "clam_clm_main",
                    Field = "iclaim",
                    Alias = "�߮׸��X"
                }
            ],
            Metrics =
            [
                new MetricDetail
                {
                    Table = "clam_stl_instype",
                    Field = "mstl",
                    Aggregation = "SUM",
                    Alias = "�ߴڪ��B"
                }
            ],
            Filters = new List<FilterDetail>(),
            Sort =
            [
                new SortDetail
                {
                    Table = "clam_stl_instype",
                    Field = "mstl",
                    Direction = "DESC"
                }
            ],
            Limit = 10
        };

        var result = service.Compile(querySpec, new Dictionary<string, string>(), null);

        Assert.Contains("ORDER BY SUM([t1].[mstl]) DESC", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORDER BY [t1].[mstl] DESC", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compile_ShouldResolveBaseTableFromJoins_WhenTargetTablesOrderIsReversed()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var service = new QueryCompilerService(whitelist);
        var querySpec = new QuerySpec
        {
            TargetTables = ["clam_stl_instype", "clam_clm_main"],
            Joins =
            [
                new JoinDetail
                {
                    LeftTable = "clam_clm_main",
                    LeftField = "iclaim",
                    RightTable = "clam_stl_instype",
                    RightField = "iclaim",
                    JoinType = "INNER"
                }
            ],
            DataFields =
            [
                new DimensionDetail
                {
                    Table = "clam_clm_main",
                    Field = "iclaim",
                    Alias = "claimNo"
                }
            ],
            Metrics =
            [
                new MetricDetail
                {
                    Table = "clam_stl_instype",
                    Field = "mstl",
                    Aggregation = "SUM",
                    Alias = "sumMstl"
                }
            ],
            Filters = new List<FilterDetail>(),
            Sort = new List<SortDetail>(),
            Limit = 10
        };

        var result = service.Compile(querySpec, new Dictionary<string, string>(), null);

        Assert.Contains("FROM [clam_clm_main] AS [t1]", result.Sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOIN [clam_stl_instype] AS [t0]", result.Sql, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeWhitelistLayoutService : IWhitelistLayoutService
    {
        private static readonly Dictionary<string, HashSet<string>> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["clam_clm_main"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "iclaim",
                "insure_type",
                "dept_id"
            },
            ["clam_stl_instype"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "iclaim",
                "mstl"
            }
        };

        public bool IsValidTable(string table)
        {
            return Map.ContainsKey(table);
        }

        public bool IsValidField(string table, string field)
        {
            return Map.TryGetValue(table, out var set) && set.Contains(field);
        }

        public IReadOnlyCollection<string> GetFields(string table)
        {
            return Map.TryGetValue(table, out var set) ? set : Array.Empty<string>();
        }
    }
}

