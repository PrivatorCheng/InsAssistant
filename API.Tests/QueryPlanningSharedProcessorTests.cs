using API.Contracts;
using API.Models;
using API.Services;

namespace API.Tests;

public class QueryPlanningSharedProcessorTests
{
    [Fact]
    public void ParseAndNormalize_ShouldAcceptNumericFilterValue()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var processor = new QueryPlanningSharedProcessor(whitelist);

        const string llmJson = """
        {
            "tasks": [
                {
                    "taskId": "main",
                    "querySpec": {
                        "query_type": "DETAIL",
                        "targetTables": ["clam_clm_main"],
                        "dataFields": [
                            {
                                "table": "clam_clm_main",
                                "field": "iclaim",
                                "alias": "賠案號碼"
                            }
                        ],
                        "metrics": [],
                        "filters": [
                            {
                                "table": "clam_clm_main",
                                "field": "mapp_total1",
                                "operator": "GREATER_THAN",
                                "value": [0]
                            }
                        ],
                        "sort": [],
                        "limit": 10
                    }
                }
            ],
            "formula": null
        }
        """;

            var preprocessResult = new PreprocessResult
            {
                OriginalQuestion = "查詢有賠付金額的賠案",
                NormalizedQuestion = "查詢有賠付金額的賠案",
                Keywords = new List<string> { "賠案", "賠付金額" },
                Entities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                PiiTokenMap = new Dictionary<string, string>(),
                StartDate = null,
                EndDate = null
            };

            var availableTables = new List<string> { "clam_clm_main" };
            var tableFieldMap = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["clam_clm_main"] = whitelist.GetFields("clam_clm_main")
            };

            var formulaSpec = processor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);
            var mainTaskQuerySpec = formulaSpec.Tasks[0].QuerySpec!;

            Assert.Single(mainTaskQuerySpec.Filters);
            Assert.Equal("GREATER_THAN", mainTaskQuerySpec.Filters[0].Operator);
            Assert.Single(mainTaskQuerySpec.Filters[0].Value);
            Assert.Equal("0", mainTaskQuerySpec.Filters[0].Value[0]);
            }

    [Fact]
    public void ParseAndNormalize_ShouldThrow_WhenQuerySpecContainsInvalidField()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var processor = new QueryPlanningSharedProcessor(whitelist);

        const string llmJson = """
                {
                    "tasks": [
                        {
                            "taskId": "main",
                            "querySpec": {
                                "query_type": "MIXED",
                                "targetTables": ["clam_clm_main", "clam_stl_instype"],
                                "joins": [
                                    {
                                        "leftTable": "clam_clm_main",
                                        "rightTable": "clam_stl_instype",
                                        "leftFields": ["insure_type", "iclaim"],
                                        "rightFields": ["insure_type", "iclaim"],
                                        "joinType": "INNER"
                                    }
                                ],
                                "dataFields": [],
                                "metrics": [
                                    {
                                        "table": "clam_clm_main",
                                        "field": "mapp",
                                        "aggregation": "SUM",
                                        "alias": "預估金額"
                                    }
                                ],
                                "filters": [],
                                "sort": [],
                                "limit": 200
                            }
                        }
                    ],
                    "formula": null
                }
        """;

        var preprocessResult = new PreprocessResult
        {
            OriginalQuestion = "查詢2025年賠付金額",
            NormalizedQuestion = "查詢2025年賠付金額",
            Keywords = new List<string> { "賠付", "金額" },
            Entities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            PiiTokenMap = new Dictionary<string, string>(),
            StartDate = null,
            EndDate = null
        };

        var availableTables = new List<string> { "clam_clm_main", "clam_stl_instype" };
        var tableFieldMap = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["clam_clm_main"] = whitelist.GetFields("clam_clm_main"),
            ["clam_stl_instype"] = whitelist.GetFields("clam_stl_instype")
        };

        var action = () => processor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);

        var exception = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains("無效指標欄位", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAndNormalize_ShouldReadRootFormulaName()
    {
        var whitelist = new FakeWhitelistLayoutService();
        var processor = new QueryPlanningSharedProcessor(whitelist);

        const string llmJson = """
        {
            "formula_name": "預估精準度",
            "tasks": [
                {
                    "taskId": "claim_amount",
                    "querySpec": {
                        "query_type": "AGGREGATION",
                        "targetTables": ["clam_stl_instype"],
                        "dataFields": [],
                        "metrics": [
                            {
                                "table": "clam_stl_instype",
                                "field": "mstl",
                                "aggregation": "SUM",
                                "alias": "賠付金額"
                            }
                        ],
                        "filters": [],
                        "sort": [],
                        "limit": 1
                    }
                },
                {
                    "taskId": "estimated_amount",
                    "querySpec": {
                        "query_type": "AGGREGATION",
                        "targetTables": ["clam_clm_main"],
                        "dataFields": [],
                        "metrics": [
                            {
                                "table": "clam_clm_main",
                                "field": "mapp_total1",
                                "aggregation": "SUM",
                                "alias": "預估金額"
                            }
                        ],
                        "filters": [],
                        "sort": [],
                        "limit": 1
                    }
                }
            ],
            "formula": {
                "operator": "DIVIDE",
                "left": "claim_amount",
                "right": "estimated_amount"
            }
        }
        """;

        var preprocessResult = new PreprocessResult
        {
            OriginalQuestion = "依險種計算預估精準度",
            NormalizedQuestion = "依險種計算預估精準度",
            Keywords = new List<string> { "險種", "預估精準度" },
            Entities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            PiiTokenMap = new Dictionary<string, string>(),
            StartDate = null,
            EndDate = null
        };

        var availableTables = new List<string> { "clam_stl_instype", "clam_clm_main" };
        var tableFieldMap = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["clam_clm_main"] = whitelist.GetFields("clam_clm_main"),
            ["clam_stl_instype"] = whitelist.GetFields("clam_stl_instype")
        };

        var formulaSpec = processor.ParseAndNormalize(llmJson, preprocessResult, availableTables, tableFieldMap);

        Assert.Equal("預估精準度", formulaSpec.FormulaName);
    }

    private sealed class FakeWhitelistLayoutService : IWhitelistLayoutService
    {
        private static readonly Dictionary<string, HashSet<string>> TableFields = new(StringComparer.OrdinalIgnoreCase)
        {
            ["clam_clm_main"] =
            [
                "insure_type",
                "iclaim",
                "mapp_total1"
            ],
            ["clam_stl_instype"] =
            [
                "insure_type",
                "iclaim",
                "mstl"
            ]
        };

        public bool IsValidTable(string table)
        {
            return TableFields.ContainsKey(table);
        }

        public bool IsValidField(string table, string field)
        {
            return TableFields.TryGetValue(table, out var fields) && fields.Contains(field);
        }

        public IReadOnlyCollection<string> GetFields(string table)
        {
            return TableFields.TryGetValue(table, out var fields) ? fields : Array.Empty<string>();
        }
    }
}
