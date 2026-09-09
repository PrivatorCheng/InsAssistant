using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Diagnostics;
using API.Attributes;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using Microsoft.AspNetCore.Hosting;

namespace API.Services;

/// <summary>
/// Step 1 ??Step 6 ?擃?隤踵???
/// </summary>
[Service(ServiceLifetime.Scoped)]
public sealed class TextToSqlService : ITextToSqlService
{
    private static readonly object HistoryFileLock = new();
    private static readonly JsonSerializerOptions HistoryJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions HistoryCompactJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions EntityListJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILayoutRetrievalService _layoutRetrievalService;
    private readonly GeminiQueryPlanningService _geminiQueryPlanningService;
    private readonly GroqQueryPlanningService _groqQueryPlanningService;
    private readonly GptQueryPlanningService _gptQueryPlanningService;
    private readonly GithubQueryPlanningService _githubQueryPlanningService;
    private readonly MyLlamaQueryPlanningService _myLlamaQueryPlanningService;
    private readonly IQueryCompilerService _queryCompilerService;
    private readonly IReportRepository _reportRepository;
    private readonly IResultDecorationService _resultDecorationService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly CustomLogger _logger;

    public TextToSqlService(
        ILayoutRetrievalService layoutRetrievalService,
        GeminiQueryPlanningService geminiQueryPlanningService,
        GroqQueryPlanningService groqQueryPlanningService,
        GptQueryPlanningService gptQueryPlanningService,
        GithubQueryPlanningService githubQueryPlanningService,
        MyLlamaQueryPlanningService myLlamaQueryPlanningService,
        IQueryCompilerService queryCompilerService,
        IReportRepository reportRepository,
        IResultDecorationService resultDecorationService,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<TextToSqlService> logger)
    {
        _layoutRetrievalService = layoutRetrievalService;
        _geminiQueryPlanningService = geminiQueryPlanningService;
        _groqQueryPlanningService = groqQueryPlanningService;
        _gptQueryPlanningService = gptQueryPlanningService;
        _githubQueryPlanningService = githubQueryPlanningService;
        _myLlamaQueryPlanningService = myLlamaQueryPlanningService;
        _queryCompilerService = queryCompilerService;
        _reportRepository = reportRepository;
        _resultDecorationService = resultDecorationService;
        _configuration = configuration;
        _environment = environment;
        _logger = logger.ToCustomLogger();
    }

    public async Task<TextToSqlResult> QueryAsync(NaturalLanguageQueryRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var queryForExecution = request.Query;
        var historyUserId = request.UserId;
        var canPersistHistory = CanPersistHistory(historyUserId);
        var step0Prompt = "-";
        var step0LlmJson = "-";
        var step0QueryMode = "NEW";
        FormulaSpec? previousFormulaSpecForContinue = null;
        PreprocessResult? step1 = null;
        List<string>? step2 = null;
        Step3PlanningResult? step3 = null;
        CompiledSql? step4 = null;
        var llmRecords = new List<LlmRecord>();

        try
        {
            var selectedProvider = LlmProviderResolver.NormalizeProvider(request.LlmProvider);
            var planningService = ResolvePlanningService(selectedProvider);

            var previousQuerySummary = "-";
            FormulaSpec? previousFormulaSpec = null;
            if (canPersistHistory)
            {
                TryGetPreviousHistoryContext(historyUserId, request.SelectedHistoryFileName, out previousQuerySummary, out previousFormulaSpec);
            }

            var step1LlmResult = await ExecuteStepAsync(
                1,
                "語意預處理",
                () => ExecuteStep1LlmAsync(selectedProvider, previousQuerySummary, request.Query, cancellationToken));

            queryForExecution = step1LlmResult.QuerySentence;
            step0Prompt = step1LlmResult.Prompt;
            step0LlmJson = step1LlmResult.LlmJson;
            step0QueryMode = step1LlmResult.QueryMode;
            llmRecords.Add(step1LlmResult.LlmRecord);

            if (step0QueryMode.Equals("CONTINUE", StringComparison.OrdinalIgnoreCase))
            {
                previousFormulaSpecForContinue = previousFormulaSpec;
            }

            step1 = BuildPreprocessResult(request.Query, step1LlmResult.QuerySentence, step1LlmResult.Keywords, step1LlmResult.Entities);
            var entityListText = await BuildEntityListTextAsync(step1LlmResult.Entities, cancellationToken);
            step2 = ExecuteStep(2, "Layout 檢索", () => _layoutRetrievalService.RetrieveLayoutFiles(step1.Keywords));
            step3 = await ExecuteStepAsync(3, "FormulaSpec 生成", () => planningService.PlanAsync(step1, step2, previousFormulaSpecForContinue, entityListText, cancellationToken));
            llmRecords.Add(step3.LlmRecord);
            EnsureFormulaSpecTasks(step3.FormulaSpec);
            string step4SqlDebugText;
            List<Dictionary<string, object?>> rows;

            var taskCompiledSqls = ExecuteStep(4, "安全編譯", () => CompileFormulaTaskSqls(step3.FormulaSpec, step1.PiiTokenMap, request.DepartmentId));
            step4 = taskCompiledSqls[0].CompiledSql;
            step4SqlDebugText = BuildTaskSqlDebugText(taskCompiledSqls);
            rows = await ExecuteStepAsync(5, "資料庫執行", () => ExecuteFormulaTasksAsync(step3.FormulaSpec, taskCompiledSqls, cancellationToken));

            var formattedRows = rows
                .Select(row => row.ToDictionary(
                    item => item.Key,
                    item => ResultFormatter.NormalizeValue(item.Value),
                    StringComparer.OrdinalIgnoreCase))
                .ToList();

            var step5BeforeDecorateInfo = new Step5ExecutionInfo
            {
                Success = true,
                RowCount = formattedRows.Count,
                ColumnCount = formattedRows.SelectMany(x => x.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                PreviewRows = formattedRows
                    .Take(5)
                    .Select(row => row.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase))
                    .ToList(),
                ErrorMessage = null
            };

            // Step 5.5: 裝飾查詢結果
            var decorationContext = BuildDecorationContext(step3.FormulaSpec);
            formattedRows = await _resultDecorationService.DecorateResultsAsync(
                formattedRows,
                decorationContext.TargetTables,
                decorationContext.DataFields,
                cancellationToken);


            var columns = formattedRows.SelectMany(x => x.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (columns.Count == 0)
            {
                columns = formattedRows.SelectMany(x => x.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            var step5Info = new Step5ExecutionInfo
            {
                Success = true,
                RowCount = formattedRows.Count,
                ColumnCount = columns.Count,
                PreviewRows = formattedRows.Take(5).ToList(),
                ErrorMessage = null
            };

            var result = new TextToSqlResult
            {
                Report = new ReportResponse
                {
                    Columns = columns,
                    Rows = formattedRows
                },
                Debug = new DebugInfo
                {
                    Step0Prompt = step0Prompt,
                    Step0LlmJson = step0LlmJson,
                    Step1NormalizedQuestion = step1.NormalizedQuestion,
                    Step1Keywords = step1.Keywords,
                    Step1Entities = step1.Entities,
                    Step2Keywords = step1.Keywords,
                    Step2LayoutFiles = step2.Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList(),
                    Step3LlmJson = step3.LlmJson,
                    Step3Model = step3.Model,
                    Step3FormulaSpec = step3.FormulaSpec,
                    Step3SystemPrompt = step3.SystemPrompt,
                    Step4Sql = step4SqlDebugText,
                    Step5ExecutionBeforeDecorate = step5BeforeDecorateInfo,
                    Step5ExecutionAfterDecorate = step5Info,
                    Step5Execution = step5Info
                },
                QueryHistoryFileName = null
            };

            if (canPersistHistory)
            {
                result.QueryHistoryFileName = SaveQueryHistory(historyUserId, queryForExecution, step3.FormulaSpec, result.Report, llmRecords, request.SelectedHistoryFileName);
            }
            return result;
        }
        catch (StepExecutionException exception)
        {
            var normalizedMessage = NormalizeStepErrorMessage(exception);
            var step3FailureDebug = exception.DebugData as Step3FailureDebugData;
            var step3SystemPrompt = step3FailureDebug?.SystemPrompt ?? exception.DebugData as string ?? step3?.SystemPrompt ?? "-";
            var step3LlmJson = step3FailureDebug?.LlmJson ?? step3?.LlmJson ?? "-";
            var step3Model = step3FailureDebug?.Model ?? step3?.Model ?? "-";
            var partialResult = BuildPartialResult(step1, step2, step3, step4, normalizedMessage, step3SystemPrompt, step3LlmJson, step3Model, step0Prompt, step0LlmJson);
            if (canPersistHistory)
            {
                partialResult.QueryHistoryFileName = SaveQueryHistory(historyUserId, queryForExecution, step3?.FormulaSpec ?? partialResult.Debug.Step3FormulaSpec, new
                {
                    partialResult.Report,
                    Error = normalizedMessage
                }, llmRecords, request.SelectedHistoryFileName);
            }
            throw new StepExecutionException(
                exception.StepNumber,
                exception.StepName,
                normalizedMessage,
                partialResult,
                exception.InnerException ?? exception);
        }
        catch (Exception exception)
        {
            if (canPersistHistory)
            {
                SaveQueryHistory(historyUserId, queryForExecution, step3?.FormulaSpec ?? CreateEmptyFormulaSpec(), new
                {
                    Error = exception.Message
                }, llmRecords, request.SelectedHistoryFileName);
            }
            _logger.Error(exception, "text-to-sql failed. request: {}", request);
            throw;
        }
        finally
        {
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            _logger.Info("text-to-sql finished in {} ms", elapsed.TotalMilliseconds);
        }
    }

    private IQueryPlanningService ResolvePlanningService(string requestedProvider)
    {
        var availableProviders = LlmProviderResolver.ResolveAvailableProviders(_configuration);
        var effectiveProvider = string.IsNullOrWhiteSpace(requestedProvider)
            ? LlmProviderResolver.ResolveProvider(_configuration)
            : requestedProvider;

        if (!availableProviders.Contains(effectiveProvider, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"不支援的 LLM 提供者: {effectiveProvider}");
        }

        var planner = LlmProviderResolver.ResolvePlanner(_configuration, effectiveProvider);

        if (planner.Equals("Gpt", StringComparison.OrdinalIgnoreCase))
        {
            return _gptQueryPlanningService;
        }

        if (planner.Equals("Groq", StringComparison.OrdinalIgnoreCase))
        {
            return _groqQueryPlanningService;
        }

        if (planner.Equals("Github", StringComparison.OrdinalIgnoreCase))
        {
            return _githubQueryPlanningService;
        }

        if (planner.Equals("MyLlama", StringComparison.OrdinalIgnoreCase))
        {
            return _myLlamaQueryPlanningService;
        }

        return _geminiQueryPlanningService;
    }

    private bool TryGetPreviousHistoryContext(
        string? userId,
        string? selectedHistoryFileName,
        out string previousQuerySummary,
        out FormulaSpec? previousFormulaSpec)
    {
        previousQuerySummary = "-";
        previousFormulaSpec = null;
        var historyDirectory = GetHistoryDirectoryByUserId(userId);
        var selectedFilePath = ResolveSelectedHistoryFilePath(historyDirectory, selectedHistoryFileName);
        if (selectedFilePath is null)
        {
            return false;
        }

        var payload = TryLoadHistoryStorage(selectedFilePath);
        if (payload is null || payload.Histories.Count == 0)
        {
            return false;
        }

        previousFormulaSpec = BuildPreviousFormulaSpec(payload);

        if (!string.IsNullOrWhiteSpace(payload.Summary) && !payload.Summary.Equals("-", StringComparison.Ordinal))
        {
            previousQuerySummary = payload.Summary.Trim();
        }

        return true;
    }

    private static string NormalizeStepErrorMessage(StepExecutionException exception)
    {
        if (exception.StepNumber == 2)
        {
            return "查詢需求不在查詢資料庫的範疇之內";
        }

        return exception.Message;
    }

    private async Task<Step1ExecutionData> ExecuteStep1LlmAsync(
        string selectedProvider,
        string previousQuerySummary,
        string thisQuerySummary,
        CancellationToken cancellationToken)
    {
        string? step1Prompt = null;
        Step1LlmResponse? llmJson = null;

        try
        {
            var step1PromptTemplate = LoadStep1PromptTemplate();
            var enterpriseTerms = LoadEnterpriseTermsText();
            step1Prompt = step1PromptTemplate
                .Replace("{Today}", DateTime.Today.ToString("yyyy-MM-dd"), StringComparison.Ordinal)
                .Replace("{PreQuery}", previousQuerySummary, StringComparison.Ordinal)
                .Replace("{ThisQuery}", thisQuerySummary, StringComparison.Ordinal)
                .Replace("{EnterpriseTerm}", enterpriseTerms, StringComparison.Ordinal)
                .Replace("{EneterpriseTerm}", enterpriseTerms, StringComparison.Ordinal);

            llmJson = await QueryStep1LlmAsync(selectedProvider, step1Prompt, cancellationToken);
            var step1Result = ParseStep1Result(llmJson.ResponseJson);
            if (string.IsNullOrWhiteSpace(step1Result.QuerySentence))
            {
                throw new InvalidOperationException("Step1 ? querySentence 銝?箇征");
            }

            return new Step1ExecutionData
            {
                Prompt = step1Prompt,
                LlmJson = llmJson.ResponseJson,
                QueryMode = step1Result.QueryMode?.Trim() ?? "NEW",
                QuerySentence = step1Result.QuerySentence?.Trim() ?? string.Empty,
                Keywords = ExpandStep1KeywordsByFormula(step1Result.Keywords),
                Entities = step1Result.Entities ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                LlmRecord = new LlmRecord
                {
                    Step = "STEP1",
                    Provider = llmJson.Provider,
                    Model = llmJson.Model,
                    InputToken = llmJson.InputToken,
                    OutputToken = llmJson.OutputToken,
                    DurationMs = llmJson.DurationMs,
                    PromptLength = llmJson.PromptLength,
                    ResponseLength = llmJson.ResponseLength
                }
            };
        }
        catch (StepExecutionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var debugData = new Step1FailureDebugData
            {
                Prompt = step1Prompt ?? "-",
                LlmJson = llmJson?.ResponseJson ?? "-"
            };

            throw new StepExecutionException(
                1,
                "語意預處理",
                exception.Message,
                debugData,
                exception);
        }
    }

    private string LoadStep1PromptTemplate()
    {
        var path = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", "Step1Prompt.txt"));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("?曆???Step1Prompt.txt", path);
        }

        return File.ReadAllText(path);
    }

    private List<string> ExpandStep1KeywordsByFormula(List<string>? rawKeywords)
    {
        var expandedKeywords = new List<string>();
        var keywordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyword in rawKeywords ?? new List<string>())
        {
            var normalizedKeyword = keyword?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                continue;
            }

            if (keywordSet.Add(normalizedKeyword))
            {
                expandedKeywords.Add(normalizedKeyword);
            }
        }

        if (expandedKeywords.Count == 0)
        {
            return expandedKeywords;
        }

        foreach (var formula in LoadFormulaKeywordDefinitions())
        {
            if (!keywordSet.Contains(formula.FormulaName))
            {
                continue;
            }

            foreach (var relatedKeyword in formula.RelatedKeywords)
            {
                if (keywordSet.Add(relatedKeyword))
                {
                    expandedKeywords.Add(relatedKeyword);
                }
            }
        }

        return expandedKeywords;
    }

    private List<FormulaKeywordDefinition> LoadFormulaKeywordDefinitions()
    {
        var path = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", "FormulaDescription.txt"));
        if (!File.Exists(path))
        {
            return new List<FormulaKeywordDefinition>();
        }

        try
        {
            using var jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new List<FormulaKeywordDefinition>();
            }

            var definitions = new List<FormulaKeywordDefinition>();
            foreach (var formulaElement in jsonDocument.RootElement.EnumerateArray())
            {
                if (formulaElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!formulaElement.TryGetProperty("formula_name", out var formulaNameElement))
                {
                    continue;
                }

                var formulaName = formulaNameElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(formulaName))
                {
                    continue;
                }

                var relatedKeywordSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var relatedKeywords = new List<string>();

                if (formulaElement.TryGetProperty("formula", out var formulaDetail) &&
                    formulaDetail.ValueKind == JsonValueKind.Object)
                {
                    if (formulaDetail.TryGetProperty("left", out var leftElement))
                    {
                        AddKeywordsFromJsonElement(leftElement, relatedKeywordSet, relatedKeywords);
                    }

                    if (formulaDetail.TryGetProperty("right", out var rightElement))
                    {
                        AddKeywordsFromJsonElement(rightElement, relatedKeywordSet, relatedKeywords);
                    }
                }

                definitions.Add(new FormulaKeywordDefinition
                {
                    FormulaName = formulaName,
                    RelatedKeywords = relatedKeywords
                });
            }

            return definitions;
        }
        catch
        {
            return new List<FormulaKeywordDefinition>();
        }
    }

    private static void AddKeywordsFromJsonElement(JsonElement jsonElement, HashSet<string> keywordSet, List<string> keywords)
    {
        if (jsonElement.ValueKind == JsonValueKind.String)
        {
            var value = jsonElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && keywordSet.Add(value))
            {
                keywords.Add(value);
            }

            return;
        }

        if (jsonElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsonElement.EnumerateArray())
            {
                AddKeywordsFromJsonElement(item, keywordSet, keywords);
            }

            return;
        }

        if (jsonElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in jsonElement.EnumerateObject())
        {
            AddKeywordsFromJsonElement(property.Value, keywordSet, keywords);
        }
    }

    private async Task<string> BuildEntityListTextAsync(Dictionary<string, List<string>> entityGroups, CancellationToken cancellationToken)
    {
        var entityList = new List<Step3EntityListItem>();

        foreach (var (entityType, entityValues) in entityGroups)
        {
            if (string.IsNullOrWhiteSpace(entityType) || entityValues.Count == 0)
            {
                continue;
            }

            if (entityType.Equals("PERSON", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entityValue in entityValues.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var resolvedUserIds = await QueryUserIdsAsync(entityValue, cancellationToken);
                    if (resolvedUserIds.Count > 0)
                    {
                        foreach (var userId in resolvedUserIds)
                        {
                            entityList.Add(new Step3EntityListItem
                            {
                                EntityName = entityValue,
                                EntityId = userId,
                                DataType = "經辦人員"
                            });
                        }

                        continue;
                    }

                    entityList.Add(new Step3EntityListItem
                    {
                        EntityName = entityValue,
                        EntityId = entityValue,
                        DataType = "經辦人員"
                    });
                }

                continue;
            }

            if (entityType.Equals("CITY", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entityValue in entityValues.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var resolvedPostCodes = await QueryPostCodesAsync(entityValue, cancellationToken);
                    foreach (var postCode in resolvedPostCodes)
                    {
                        entityList.Add(new Step3EntityListItem
                        {
                            EntityName = entityValue,
                            EntityId = postCode,
                            DataType = "郵遞區號"
                        });
                    }
                }

                continue;
            }

            if (entityType.Equals("INSURE_TYPE", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entityValue in entityValues.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var resolvedInsureTypeCodes = await QueryInsureTypeCodesAsync(entityValue, cancellationToken);
                    foreach (var codeValue in resolvedInsureTypeCodes)
                    {
                        entityList.Add(new Step3EntityListItem
                        {
                            EntityName = entityValue,
                            EntityId = codeValue,
                            DataType = "險別"
                        });
                    }
                }
            }
        }

        return JsonSerializer.Serialize(entityList, EntityListJsonOptions);
    }

    private async Task<List<string>> QueryUserIdsAsync(string entityValue, CancellationToken cancellationToken)
    {
        var rows = await _reportRepository.QueryAsync(
            "SELECT user_id FROM sysp_user WHERE user_name = @userName",
            new Dictionary<string, object?>
            {
                ["@userName"] = entityValue
            },
            cancellationToken);

        return rows
            .Select(row => row.TryGetValue("user_id", out var value) ? value?.ToString() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<string>> QueryPostCodesAsync(string entityValue, CancellationToken cancellationToken)
    {
        var prefixes = BuildCityPrefixes(entityValue);
        var postCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cityPrefix in prefixes)
        {
            var rows = await _reportRepository.QueryAsync(
                "SELECT post_code FROM clap_postcode WHERE city_name LIKE @cityPrefix",
                new Dictionary<string, object?>
                {
                    ["@cityPrefix"] = $"{cityPrefix}%"
                },
                cancellationToken);

            foreach (var postCode in rows
                         .Select(row => row.TryGetValue("post_code", out var value) ? value?.ToString() : null)
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Select(value => value!.Trim()))
            {
                postCodes.Add(postCode);
            }
        }

        return postCodes.ToList();
    }

    private async Task<List<string>> QueryInsureTypeCodesAsync(string entityValue, CancellationToken cancellationToken)
    {
        var rows = await _reportRepository.QueryAsync(
            "SELECT code_value FROM sysp_code WHERE code_type = 'INSURE_TYPE' AND code_name LIKE @codeNamePrefix",
            new Dictionary<string, object?>
            {
                ["@codeNamePrefix"] = $"{entityValue}%"
            },
            cancellationToken);

        return rows
            .Select(row => row.TryGetValue("code_value", out var value) ? value?.ToString() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildCityPrefixes(string entityValue)
    {
        var prefixes = new List<string>();
        if (string.IsNullOrWhiteSpace(entityValue))
        {
            return prefixes;
        }

        var normalizedValue = entityValue.Trim();
        prefixes.Add(normalizedValue);

        if (!normalizedValue.Contains('台'))
        {
            return prefixes;
        }

        var taiwaneseValue = normalizedValue.Replace('台', '臺');
        if (!prefixes.Contains(taiwaneseValue, StringComparer.OrdinalIgnoreCase))
        {
            prefixes.Add(taiwaneseValue);
        }

        return prefixes;
    }

    private string LoadEnterpriseTermsText()
    {
        var path = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "doc", "TermDescription.txt"));
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        var lines = File.ReadAllLines(path);
        var inEnterpriseTermSection = false;
        var terms = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                inEnterpriseTermSection = line.Contains("隡平銵?", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEnterpriseTermSection || !line.StartsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line
                .Split('|')
                .Select(cell => cell.Trim())
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList();

            if (cells.Count < 3)
            {
                continue;
            }

            var termName = cells[0];
            if (termName.Equals("銵??迂", StringComparison.OrdinalIgnoreCase) ||
                termName.Equals("---", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            terms.Add(termName);
        }

        return string.Join(", ", terms.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task<Step1LlmResponse> QueryStep1LlmAsync(string selectedProvider, string step1Prompt, CancellationToken cancellationToken)
    {
        var effectiveProvider = string.IsNullOrWhiteSpace(selectedProvider)
            ? LlmProviderResolver.ResolveProvider(_configuration)
            : selectedProvider;

        if (effectiveProvider.Equals("Gpt", StringComparison.OrdinalIgnoreCase))
        {
            var settings = _configuration.GetSection("LlmSettings:Gpt").Get<GptSettings>()
                ?? throw new InvalidOperationException("LlmSettings:Gpt section not found in configuration");
            return await QueryStep0ByOpenAiCompatibleAsync(
                "Gpt",
                "https://api.openai.com/v1/chat/completions",
                settings.ApiKey,
                ParseModels(settings.Model),
                step1Prompt,
                cancellationToken);
        }

        if (effectiveProvider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
        {
            var settings = _configuration.GetSection("LlmSettings:Groq").Get<GroqSettings>()
                ?? throw new InvalidOperationException("LlmSettings:Groq section not found in configuration");
            return await QueryStep0ByOpenAiCompatibleAsync(
                "Groq",
                "https://api.groq.com/openai/v1/chat/completions",
                settings.ApiKey,
                ParseModels(settings.Model),
                step1Prompt,
                cancellationToken);
        }

        if (effectiveProvider.Equals("Github", StringComparison.OrdinalIgnoreCase))
        {
            var settings = _configuration.GetSection("LlmSettings:Github").Get<GithubSettings>()
                ?? throw new InvalidOperationException("LlmSettings:Github section not found in configuration");
            var endpoint = settings.Endpoint.TrimEnd('/');
            return await QueryStep0ByOpenAiCompatibleAsync(
                "Github",
                $"{endpoint}/chat/completions",
                settings.Pat,
                ParseModels(settings.Model),
                step1Prompt,
                cancellationToken);
        }

        if (effectiveProvider.Equals("MyLlama", StringComparison.OrdinalIgnoreCase))
        {
            var settings = _configuration.GetSection("LlmSettings:MyLlama").Get<MyLlamaSettings>()
                ?? throw new InvalidOperationException("LlmSettings:MyLlama section not found in configuration");
            return await QueryStep0ByMyLlamaAsync(settings, step1Prompt, cancellationToken);
        }

        var geminiSettings = _configuration.GetSection("LlmSettings:GoogleGemini").Get<GoogleGeminiSettings>()
            ?? throw new InvalidOperationException("LlmSettings:GoogleGemini section not found in configuration");
        return await QueryStep0ByGeminiAsync(geminiSettings, step1Prompt, cancellationToken);
    }

    private async Task<Step1LlmResponse> QueryStep0ByMyLlamaAsync(
        MyLlamaSettings settings,
        string step0Prompt,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        var endpoint = settings.Endpoint.TrimEnd('/');

        foreach (var model in ParseModels(settings.Model))
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using var httpClient = new HttpClient();
                var requestPayload = new
                {
                    model,
                    stream = false,
                    format = "json",
                    options = new { temperature = 0 },
                    messages = new object[]
                    {
                        new { role = "user", content = step0Prompt }
                    }
                };

                using var content = new StringContent(
                    JsonSerializer.Serialize(requestPayload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync($"{endpoint}/api/chat", content, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"MyLlama({model}) API 失敗：{response.StatusCode}");
                }

                var responseObject = JsonSerializer.Deserialize<Step0MyLlamaResponse>(responseText);
                if (!string.IsNullOrWhiteSpace(responseObject?.Error))
                {
                    throw new InvalidOperationException($"MyLlama({model}) 錯誤：{responseObject.Error}");
                }

                var textContent = responseObject?.Message?.Content;
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    throw new InvalidOperationException($"MyLlama({model}) 回傳空內容");
                }

                return new Step1LlmResponse
                {
                    Provider = "MyLlama",
                    Model = model,
                    ResponseJson = ExtractJsonText(textContent),
                    InputToken = responseObject?.PromptEvalCount ?? 0,
                    OutputToken = responseObject?.EvalCount ?? 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    PromptLength = step0Prompt.Length,
                    ResponseLength = responseText.Length
                };
            }
            catch (Exception exception)
            {
                lastException = exception;
                _logger.Info("step0 myllama model failed. model: {}, message: {}", model, exception.Message);
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException("MyLlama model 清單為空");
    }

    private async Task<Step1LlmResponse> QueryStep0ByOpenAiCompatibleAsync(
        string provider,
        string endpoint,
        string apiKey,
        List<string> models,
        string step0Prompt,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        foreach (var model in models)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using var httpClient = new HttpClient();
                var requestPayload = new
                {
                    model,
                    temperature = 0,
                    response_format = new { type = "json_object" },
                    messages = new object[]
                    {
                        new { role = "user", content = step0Prompt }
                    }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var response = await httpClient.SendAsync(request, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"{provider}({model}) API 失敗：{response.StatusCode}");
                }

                var responseObject = JsonSerializer.Deserialize<Step0OpenAiLikeResponse>(responseText);
                var textContent = responseObject?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    throw new InvalidOperationException($"{provider}({model}) 回傳空內容");
                }

                return new Step1LlmResponse
                {
                    Provider = provider,
                    Model = model,
                    ResponseJson = ExtractJsonText(textContent),
                    InputToken = responseObject?.Usage?.PromptTokens ?? 0,
                    OutputToken = responseObject?.Usage?.CompletionTokens ?? 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    PromptLength = step0Prompt.Length,
                    ResponseLength = responseText.Length
                };
            }
            catch (Exception exception)
            {
                lastException = exception;
                _logger.Info("step0 {} model failed. model: {}, message: {}", provider, model, exception.Message);
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException($"{provider} model 清單為空");
    }

    private async Task<Step1LlmResponse> QueryStep0ByGeminiAsync(
        GoogleGeminiSettings settings,
        string step0Prompt,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        foreach (var model in ParseModels(settings.Model))
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                using var httpClient = new HttpClient();
                var requestPayload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = step0Prompt }
                            }
                        }
                    }
                };

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(settings.ApiKey)}";
                using var content = new StringContent(
                    JsonSerializer.Serialize(requestPayload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync(url, content, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Gemini({model}) API 失敗：{response.StatusCode}");
                }

                var responseObject = JsonSerializer.Deserialize<GeminiResponse>(responseText);
                var textContent = responseObject?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    throw new InvalidOperationException($"Gemini({model}) 回傳空內容");
                }

                return new Step1LlmResponse
                {
                    Provider = "GoogleGemini",
                    Model = model,
                    ResponseJson = ExtractJsonText(textContent),
                    InputToken = responseObject?.UsageMetadata?.PromptTokenCount ?? 0,
                    OutputToken = responseObject?.UsageMetadata?.CandidatesTokenCount ?? 0,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    PromptLength = step0Prompt.Length,
                    ResponseLength = responseText.Length
                };
            }
            catch (Exception exception)
            {
                lastException = exception;
                _logger.Info("step0 gemini model failed. model: {}, message: {}", model, exception.Message);
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException("Gemini model 清單為空");
    }

    private static Step1Result ParseStep1Result(string jsonText)
    {
        var result = JsonSerializer.Deserialize<Step1Result>(
            jsonText,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (result is null)
        {
            throw new InvalidOperationException("Step1 JSON 反序列化失敗");
        }

        if (string.IsNullOrWhiteSpace(result.QueryMode))
        {
            throw new InvalidOperationException("Step1 回傳 queryMode 不可為空");
        }

        result.QuerySentence = string.IsNullOrWhiteSpace(result.QuerySentence)
            ? result.LegacyQuerySentense
            : result.QuerySentence;

        result.Keywords = result.Keywords?
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        result.Entities = NormalizeStep1Entities(result.EntityRaw);

        if (result.Keywords.Count == 0)
        {
            throw new InvalidOperationException("無法取得查詢關鍵字, 查詢失敗");
        }

        return result;
    }

    private static string ExtractJsonText(string textContent)
    {
        var jsonMatch = Regex.Match(textContent, @"\{[\s\S]*\}", RegexOptions.RightToLeft);
        if (!jsonMatch.Success)
        {
            throw new InvalidOperationException($"無法從 Step1 LLM 回應中提取 JSON：{textContent}");
        }

        return jsonMatch.Value;
    }

    private static Dictionary<string, List<string>> NormalizeStep1Entities(JsonElement entityRaw)
    {
        var entities = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (entityRaw.ValueKind == JsonValueKind.Undefined || entityRaw.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        if (entityRaw.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in entityRaw.EnumerateObject())
            {
                AddEntityValue(entities, property.Name, property.Value);
            }

            return entities.ToDictionary(
                x => x.Key,
                x => x.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
        }

        if (entityRaw.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entityRaw.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in item.EnumerateObject())
                {
                    AddEntityValue(entities, property.Name, property.Value);
                }
            }
        }

        return entities.ToDictionary(
            x => x.Key,
            x => x.Value.ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AddEntityValue(Dictionary<string, HashSet<string>> entities, string type, JsonElement value)
    {
        var normalizedType = type?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType))
        {
            return;
        }

        if (!entities.TryGetValue(normalizedType, out var bucket))
        {
            bucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            entities[normalizedType] = bucket;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var content = value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                bucket.Add(content);
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var content = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                bucket.Add(content);
            }
        }
    }

    private static PreprocessResult BuildPreprocessResult(string originalQuestion, string querySentence, List<string> keywords, Dictionary<string, List<string>> entities)
    {
        var (startDate, endDate) = TryExtractDateRange(querySentence);
        return new PreprocessResult
        {
            OriginalQuestion = originalQuestion.Trim(),
            NormalizedQuestion = querySentence.Trim(),
            PiiTokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StartDate = startDate,
            EndDate = endDate,
            Keywords = keywords,
            Entities = entities
        };
    }

    private static (DateOnly? StartDate, DateOnly? EndDate) TryExtractDateRange(string querySentence)
    {
        if (string.IsNullOrWhiteSpace(querySentence))
        {
            return (null, null);
        }

        var rangePattern = new Regex("(\\d{4}[-/]\\d{2}[-/]\\d{2})\\s*(到|至|-)\\s*(\\d{4}[-/]\\d{2}[-/]\\d{2})", RegexOptions.Compiled);
        var match = rangePattern.Match(querySentence);
        if (!match.Success)
        {
            return (null, null);
        }

        if (!DateOnly.TryParse(match.Groups[1].Value.Replace('/', '-'), out var startDate))
        {
            return (null, null);
        }

        if (!DateOnly.TryParse(match.Groups[3].Value.Replace('/', '-'), out var endDate))
        {
            return (null, null);
        }

        return (startDate, endDate);
    }

    private static List<string> ParseModels(string modelSetting)
    {
        return modelSetting
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static T ExecuteStep<T>(int stepNumber, string stepName, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (StepExecutionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StepExecutionException(stepNumber, stepName, exception.Message, null, exception);
        }
    }

    private static async Task<T> ExecuteStepAsync<T>(int stepNumber, string stepName, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (StepExecutionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new StepExecutionException(stepNumber, stepName, exception.Message, null, exception);
        }
    }

    private static TextToSqlResult BuildPartialResult(
        PreprocessResult? step1,
        List<string>? step2,
        Step3PlanningResult? step3,
        CompiledSql? step4,
        string step5Error,
        string step3SystemPrompt,
        string step3LlmJson,
        string step3Model,
        string step0Prompt,
        string step0LlmJson)
    {
        return new TextToSqlResult
        {
            Report = new ReportResponse
            {
                Columns = new List<string>(),
                Rows = new List<Dictionary<string, object?>>()
            },
            Debug = new DebugInfo
            {
                Step0Prompt = step0Prompt,
                Step0LlmJson = step0LlmJson,
                Step1NormalizedQuestion = step1?.NormalizedQuestion ?? "-",
                Step1Keywords = step1?.Keywords ?? new List<string>(),
                Step1Entities = step1?.Entities ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                Step2Keywords = step1?.Keywords ?? new List<string>(),
                Step2LayoutFiles = step2?.Select(Path.GetFileName).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToList() ?? new List<string>(),
                Step3LlmJson = step3LlmJson,
                Step3Model = step3Model,
                Step3FormulaSpec = step3?.FormulaSpec ?? CreateEmptyFormulaSpec(),
                Step3SystemPrompt = step3?.SystemPrompt ?? step3SystemPrompt,
                Step4Sql = step4 is null ? "-" : $"{step4.Sql}\n-- params: {JsonSerializer.Serialize(step4.Parameters)}",
                Step5ExecutionBeforeDecorate = new Step5ExecutionInfo
                {
                    Success = false,
                    RowCount = 0,
                    ColumnCount = 0,
                    PreviewRows = new List<Dictionary<string, object?>>(),
                    ErrorMessage = step5Error
                },
                Step5ExecutionAfterDecorate = new Step5ExecutionInfo
                {
                    Success = false,
                    RowCount = 0,
                    ColumnCount = 0,
                    PreviewRows = new List<Dictionary<string, object?>>(),
                    ErrorMessage = step5Error
                },
                Step5Execution = new Step5ExecutionInfo
                {
                    Success = false,
                    RowCount = 0,
                    ColumnCount = 0,
                    PreviewRows = new List<Dictionary<string, object?>>(),
                    ErrorMessage = step5Error
                }
            },
            QueryHistoryFileName = null
        };
    }

    private static bool HasFormulaTasks(FormulaSpec formulaSpec)
    {
        return formulaSpec.Tasks.Any(task => task.QuerySpec is not null);
    }

    private static void EnsureFormulaSpecTasks(FormulaSpec formulaSpec)
    {
        if (!HasFormulaTasks(formulaSpec))
        {
            throw new InvalidOperationException("Step3 必須回傳 FormulaSpec.tasks，且每個 task 需包含 querySpec");
        }
    }

    private static (List<string> TargetTables, List<DimensionDetail> DataFields) BuildDecorationContext(FormulaSpec formulaSpec)
    {
        EnsureFormulaSpecTasks(formulaSpec);

        var dataFieldMap = new Dictionary<string, DimensionDetail>(StringComparer.OrdinalIgnoreCase);
        var targetTables = new List<string>();

        var formulaTasks = formulaSpec.Tasks;
        foreach (var task in formulaTasks)
        {
            var taskQuerySpec = task.QuerySpec;
            if (taskQuerySpec is null)
            {
                continue;
            }

            foreach (var table in taskQuerySpec.TargetTables.Where(table => !string.IsNullOrWhiteSpace(table)))
            {
                if (!targetTables.Contains(table, StringComparer.OrdinalIgnoreCase))
                {
                    targetTables.Add(table);
                }
            }

            foreach (var dataField in taskQuerySpec.DataFields)
            {
                var key = $"{dataField.Table}.{dataField.Field}";
                if (!dataFieldMap.ContainsKey(key))
                {
                    dataFieldMap[key] = dataField;
                }
            }
        }

        return (targetTables, dataFieldMap.Values.ToList());
    }

    private List<FormulaTaskCompiledSql> CompileFormulaTaskSqls(
        FormulaSpec formulaSpec,
        Dictionary<string, string> piiTokenMap,
        string? departmentId)
    {
        var formulaTasks = formulaSpec.Tasks;
        var compiledSqls = new List<FormulaTaskCompiledSql>();

        foreach (var task in formulaTasks)
        {
            if (task.QuerySpec is null)
            {
                continue;
            }

            var compiledSql = _queryCompilerService.Compile(task.QuerySpec, piiTokenMap, departmentId);
            var normalizedTaskId = string.IsNullOrWhiteSpace(task.TaskId)
                ? $"task_{compiledSqls.Count + 1}"
                : task.TaskId.Trim();

            compiledSqls.Add(new FormulaTaskCompiledSql
            {
                TaskId = normalizedTaskId,
                CompiledSql = compiledSql
            });
        }

        if (compiledSqls.Count == 0)
        {
            throw new InvalidOperationException("FormulaSpec.tasks 不可為空，且每個 task 必須包含 querySpec");
        }

        return compiledSqls;
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteFormulaTasksAsync(
        FormulaSpec formulaSpec,
        List<FormulaTaskCompiledSql> taskCompiledSqls,
        CancellationToken cancellationToken)
    {
        var taskRows = new List<FormulaTaskRows>(taskCompiledSqls.Count);

        foreach (var taskCompiledSql in taskCompiledSqls)
        {
            var rows = await _reportRepository.QueryAsync(
                taskCompiledSql.CompiledSql.Sql,
                taskCompiledSql.CompiledSql.Parameters,
                cancellationToken);

            var normalizedRows = rows
                .Select(row => row.ToDictionary(
                    item => item.Key,
                    item => ResultFormatter.NormalizeValue(item.Value),
                    StringComparer.OrdinalIgnoreCase))
                .ToList();

            taskRows.Add(new FormulaTaskRows
            {
                TaskId = taskCompiledSql.TaskId,
                Rows = normalizedRows
            });
        }

        var mergedRows = MergeTaskRows(taskRows, formulaSpec.Formula);
        ApplyFormulaCalculation(formulaSpec, mergedRows);
        RemoveHelperTaskColumns(formulaSpec, taskCompiledSqls, mergedRows);

        return mergedRows;
    }

    private static void RemoveHelperTaskColumns(
        FormulaSpec formulaSpec,
        List<FormulaTaskCompiledSql> taskCompiledSqls,
        List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0 || taskCompiledSqls.Count == 0)
        {
            return;
        }

        var businessColumnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in formulaSpec.Tasks)
        {
            if (task.QuerySpec is null)
            {
                continue;
            }

            foreach (var dataField in task.QuerySpec.DataFields)
            {
                businessColumnSet.Add(string.IsNullOrWhiteSpace(dataField.Alias) ? dataField.Field : dataField.Alias);
            }

            foreach (var metric in task.QuerySpec.Metrics)
            {
                businessColumnSet.Add(metric.Alias);
            }
        }

        foreach (var taskId in taskCompiledSqls.Select(x => x.TaskId).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            // 只移除內部 taskId 輔助欄位；若 taskId 與業務欄位同名則保留。
            if (businessColumnSet.Contains(taskId))
            {
                continue;
            }

            foreach (var row in rows)
            {
                row.Remove(taskId);
            }
        }
    }

    private static List<Dictionary<string, object?>> MergeTaskRows(List<FormulaTaskRows> taskRows, FormulaExpressionSpec? formula)
    {
        var isFormulaEmpty = formula is null || string.IsNullOrWhiteSpace(formula.Operator);
        var maxRows = taskRows.Count == 0 ? 0 : taskRows.Max(task => task.Rows.Count);
        var mergedRows = new List<Dictionary<string, object?>>(maxRows);

        for (var rowIndex = 0; rowIndex < maxRows; rowIndex++)
        {
            var mergedRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var taskRow in taskRows)
            {
                if (rowIndex >= taskRow.Rows.Count)
                {
                    continue;
                }

                var currentTaskRow = taskRow.Rows[rowIndex];
                foreach (var (key, value) in currentTaskRow)
                {
                    if (!mergedRow.ContainsKey(key))
                    {
                        mergedRow[key] = value;
                    }
                }

                if (isFormulaEmpty && taskRow.TaskId.Equals("main", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!mergedRow.ContainsKey(taskRow.TaskId) && TryGetTaskScalarValue(currentTaskRow, out var scalarValue))
                {
                    mergedRow[taskRow.TaskId] = scalarValue;
                }
            }

            mergedRows.Add(mergedRow);
        }

        return mergedRows;
    }

    private static bool TryGetTaskScalarValue(Dictionary<string, object?> row, out decimal value)
    {
        foreach (var itemValue in row.Values)
        {
            if (TryConvertToDecimal(itemValue, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static void ApplyFormulaCalculation(FormulaSpec formulaSpec, List<Dictionary<string, object?>> rows)
    {
        var formula = formulaSpec.Formula;
        if (formula is null || string.IsNullOrWhiteSpace(formula.Operator) || rows.Count == 0)
        {
            return;
        }

        var formulaName = string.IsNullOrWhiteSpace(formulaSpec.FormulaName)
            ? null
            : formulaSpec.FormulaName.Trim();

        var formulaResultColumn = !string.IsNullOrWhiteSpace(formulaName)
            ? formulaName
            : !string.IsNullOrWhiteSpace(formula.Alias)
                ? formula.Alias.Trim()
                : !string.IsNullOrWhiteSpace(formula.FormulaName)
                    ? formula.FormulaName.Trim()
                : "公式計算結果";

        foreach (var row in rows)
        {
            if (!TryResolveFormulaOperand(row, formula.Left, out var leftValue) ||
                !TryResolveFormulaOperand(row, formula.Right, out var rightValue))
            {
                continue;
            }

            if (!TryEvaluateFormula(formula.Operator, leftValue, rightValue, out var formulaValue))
            {
                continue;
            }

            row[formulaResultColumn] = formulaValue;
        }
    }

    private static bool TryResolveFormulaOperand(Dictionary<string, object?> row, string? operand, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(operand))
        {
            value = 0;
            return false;
        }

        var trimmedOperand = operand.Trim();

        if (row.TryGetValue(trimmedOperand, out var rowValue) && TryConvertToDecimal(rowValue, out value))
        {
            return true;
        }

        if (decimal.TryParse(trimmedOperand, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (decimal.TryParse(trimmedOperand, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryEvaluateFormula(string? formulaOperator, decimal left, decimal right, out decimal result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(formulaOperator))
        {
            return false;
        }

        switch (formulaOperator.Trim().ToUpperInvariant())
        {
            case "DIVIDE":
                if (right == 0)
                {
                    return false;
                }

                result = left / right;
                return true;
            case "MULTIPLY":
                result = left * right;
                return true;
            case "ADD":
                result = left + right;
                return true;
            case "SUBTRACT":
                result = left - right;
                return true;
            default:
                return false;
        }
    }

    private static bool TryConvertToDecimal(object? value, out decimal decimalValue)
    {
        switch (value)
        {
            case null:
                decimalValue = 0;
                return false;
            case decimal d:
                decimalValue = d;
                return true;
            case int i:
                decimalValue = i;
                return true;
            case long l:
                decimalValue = l;
                return true;
            case short s:
                decimalValue = s;
                return true;
            case double db:
                decimalValue = Convert.ToDecimal(db);
                return true;
            case float f:
                decimalValue = Convert.ToDecimal(f);
                return true;
            case string str:
                if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out decimalValue))
                {
                    return true;
                }

                if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.CurrentCulture, out decimalValue))
                {
                    return true;
                }

                decimalValue = 0;
                return false;
            default:
                return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out decimalValue);
        }
    }

    private static string BuildTaskSqlDebugText(List<FormulaTaskCompiledSql> taskCompiledSqls)
    {
        var blocks = taskCompiledSqls.Select(taskSql =>
            $"[taskId={taskSql.TaskId}]\n{taskSql.CompiledSql.Sql}\n-- params: {JsonSerializer.Serialize(taskSql.CompiledSql.Parameters)}");

        return string.Join("\n\n", blocks);
    }

    private static FormulaSpec CreateEmptyFormulaSpec()
    {
        return new FormulaSpec
        {
            FormulaName = null,
            Tasks = new List<FormulaTaskSpec>(),
            Formula = null
        };
    }

    private string? SaveQueryHistory(string? userId, string query, FormulaSpec formulaSpec, object result, List<LlmRecord>? llmRecords, string? selectedHistoryFileName)
    {
        if (!CanPersistHistory(userId))
        {
            return null;
        }

        try
        {
            lock (HistoryFileLock)
            {
                var historyDirectory = GetHistoryDirectoryByUserId(userId);
                Directory.CreateDirectory(historyDirectory);

                var summary = ResolveQuerySummary(formulaSpec);
                var resultElement = JsonSerializer.SerializeToElement(result, HistoryCompactJsonOptions);

                var filePath = ResolveSelectedHistoryFilePath(historyDirectory, selectedHistoryFileName) ?? GetNextHistoryFilePath(historyDirectory);
                var payload = TryLoadHistoryStorage(filePath) ?? new QueryHistoryStorage
                {
                    Summary = summary,
                    FormulaSpec = formulaSpec,
                    Histories = new List<QueryHistoryEntry>()
                };

                // ?交甈∠瘜????閬?靽??Ｘ???嚗?辣蝥閰Ｘ??
                if (summary.Equals("-", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(payload.Summary) &&
                    !payload.Summary.Equals("-", StringComparison.Ordinal))
                {
                    summary = payload.Summary;
                }

                payload.Summary = summary;
                if (HasMeaningfulFormulaSpec(formulaSpec) || !HasMeaningfulFormulaSpec(payload.FormulaSpec))
                {
                    payload.FormulaSpec = formulaSpec;
                }

                payload.Histories ??= new List<QueryHistoryEntry>();
                payload.Histories.Add(new QueryHistoryEntry
                {
                    QueryData = query,
                    QueryResult = resultElement,
                    LlmRecords = llmRecords?.Select(CloneLlmRecord).ToList() ?? new List<LlmRecord>()
                });

                File.WriteAllText(filePath, JsonSerializer.Serialize(payload, HistoryJsonOptions));
                return Path.GetFileName(filePath);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "save query history failed. query: {}", query);
            return null;
        }
    }

    private string GetHistoryDirectoryByUserId(string? userId)
    {
        var rootHistoryDirectory = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", "query_history"));
        var safeUserId = SanitizePathSegment(userId);
        if (string.IsNullOrWhiteSpace(safeUserId))
        {
            throw new InvalidOperationException("?∠?乩蝙?刻?銝摮??亥岷甇瑕?桅?");
        }

        return Path.Combine(rootHistoryDirectory, safeUserId);
    }

    private static bool CanPersistHistory(string? userId)
    {
        return !string.IsNullOrWhiteSpace(SanitizePathSegment(userId));
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var buffer = value.Trim().Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        return new string(buffer);
    }

    private static QueryHistoryStorage? TryLoadHistoryStorage(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        QueryHistoryStorage? payload;
        try
        {
            var content = File.ReadAllText(filePath);
            payload = JsonSerializer.Deserialize<QueryHistoryStorage>(content);
        }
        catch
        {
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        if (payload.FormulaSpec is null)
        {
            return null;
        }

        payload.Histories ??= new List<QueryHistoryEntry>();

        return payload;
    }

    private static string? ResolveSelectedHistoryFilePath(string historyDirectory, string? selectedHistoryFileName)
    {
        if (string.IsNullOrWhiteSpace(selectedHistoryFileName))
        {
            return null;
        }

        var fileName = Path.GetFileName(selectedHistoryFileName.Trim());
        if (!fileName.Equals(selectedHistoryFileName.Trim(), StringComparison.Ordinal) ||
            !fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var filePath = Path.Combine(historyDirectory, fileName);
        return File.Exists(filePath) ? filePath : null;
    }

    private static bool HasMeaningfulFormulaSpec(FormulaSpec? formulaSpec)
    {
        if (formulaSpec is null)
        {
            return false;
        }

        if (formulaSpec.Formula is not null)
        {
            return true;
        }

        return formulaSpec.Tasks.Any(task => task.QuerySpec is not null);
    }

    private static string ResolveQuerySummary(FormulaSpec formulaSpec)
    {
        var taskSummary = formulaSpec.Tasks
            .Select(task => task.QuerySpec?.QueryDesc)
            .FirstOrDefault(desc => !string.IsNullOrWhiteSpace(desc));

        return string.IsNullOrWhiteSpace(taskSummary) ? "-" : taskSummary.Trim();
    }

    private static FormulaSpec? BuildPreviousFormulaSpec(QueryHistoryStorage payload)
    {
        var formulaSpec = payload.FormulaSpec;
        if (!HasMeaningfulFormulaSpec(formulaSpec))
        {
            return null;
        }

        return CloneFormulaSpec(formulaSpec!);
    }

    private static FormulaSpec CloneFormulaSpec(FormulaSpec source)
    {
        var tasks = source.Tasks.Select(task => new FormulaTaskSpec
        {
            TaskId = task.TaskId,
            QuerySentence = task.QuerySentence,
            QuerySpec = task.QuerySpec is null ? null : CloneQuerySpecWithoutFormula(task.QuerySpec)
        }).ToList();

        FormulaExpressionSpec? formula = null;
        if (source.Formula is not null)
        {
            formula = new FormulaExpressionSpec
            {
                Operator = source.Formula.Operator,
                Left = source.Formula.Left,
                Right = source.Formula.Right,
                Alias = source.Formula.Alias,
                FormulaName = source.Formula.FormulaName
            };
        }

        return new FormulaSpec
        {
            FormulaName = source.FormulaName,
            Tasks = tasks,
            Formula = formula
        };
    }

    private static QuerySpec CloneQuerySpecWithoutFormula(QuerySpec source)
    {
        return new QuerySpec
        {
            QueryType = source.QueryType,
            QueryDesc = source.QueryDesc,
            TargetTables = source.TargetTables
                .Where(table => !string.IsNullOrWhiteSpace(table))
                .ToList(),
            Joins = source.Joins.Select(join => new JoinDetail
            {
                LeftTable = join.LeftTable,
                LeftField = join.LeftField,
                RightTable = join.RightTable,
                RightField = join.RightField,
                JoinType = join.JoinType
            }).ToList(),
            DataFields = source.DataFields.Select(field => new DimensionDetail
            {
                Table = field.Table,
                Field = field.Field,
                Alias = field.Alias
            }).ToList(),
            Metrics = source.Metrics.Select(metric => new MetricDetail
            {
                Table = metric.Table,
                Field = metric.Field,
                Aggregation = metric.Aggregation,
                Alias = metric.Alias
            }).ToList(),
            Filters = source.Filters.Select(filter => new FilterDetail
            {
                Table = filter.Table,
                Field = filter.Field,
                Operator = filter.Operator,
                Value = filter.Value.ToList()
            }).ToList(),
            Sort = source.Sort.Select(sort => new SortDetail
            {
                Table = sort.Table,
                Field = sort.Field,
                Direction = sort.Direction
            }).ToList(),
            Limit = source.Limit
        };
    }

    private static LlmRecord CloneLlmRecord(LlmRecord source)
    {
        return new LlmRecord
        {
            Step = source.Step,
            Provider = source.Provider,
            Model = source.Model,
            InputToken = source.InputToken,
            OutputToken = source.OutputToken,
            DurationMs = source.DurationMs,
            PromptLength = source.PromptLength,
            ResponseLength = source.ResponseLength
        };
    }

    private static string GetNextHistoryFilePath(string historyDirectory)
    {
        var datePrefix = DateTime.Now.ToString("yyyyMMdd");
        var nextSequence = Directory.EnumerateFiles(historyDirectory, $"{datePrefix}*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName) && fileName.Length == 16)
            .Select(fileName => int.TryParse(fileName![8..], out var sequence) ? sequence : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        return Path.Combine(historyDirectory, $"{datePrefix}{nextSequence:00000000}.txt");
    }

    private sealed class QueryHistoryStorage
    {
        [JsonPropertyName("查詢摘要")]
        public required string Summary { get; set; }

        [JsonPropertyName("FormulaSpec")]
        public FormulaSpec? FormulaSpec { get; set; }

        [JsonPropertyName("查詢歷史")]
        public required List<QueryHistoryEntry> Histories { get; set; }
    }

    private sealed class QueryHistoryEntry
    {
        [JsonPropertyName("查詢資料")]
        public required string QueryData { get; set; }

        [JsonPropertyName("查詢結果")]
        public required JsonElement QueryResult { get; set; }

        [JsonPropertyName("LLMRecord")]
        public List<LlmRecord>? LlmRecords { get; set; }
    }

    private sealed class Step0OpenAiLikeResponse
    {
        [JsonPropertyName("choices")]
        public List<Step0OpenAiLikeChoice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public Step0OpenAiLikeUsage? Usage { get; set; }
    }

    private sealed class Step0MyLlamaResponse
    {
        [JsonPropertyName("message")]
        public Step0MyLlamaMessage? Message { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }
    }

    private sealed class Step0MyLlamaMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class Step0OpenAiLikeUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }
    }

    private sealed class Step0OpenAiLikeChoice
    {
        [JsonPropertyName("message")]
        public Step0OpenAiLikeMessage? Message { get; set; }
    }

    private sealed class Step0OpenAiLikeMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class Step1Result
    {
        [JsonPropertyName("queryMode")]
        public string? QueryMode { get; set; }

        [JsonPropertyName("querySentence")]
        public string? QuerySentence { get; set; }

        [JsonPropertyName("querySentense")]
        public string? LegacyQuerySentense { get; set; }

        [JsonPropertyName("keywords")]
        public List<string>? Keywords { get; set; }

        [JsonPropertyName("entity")]
        public JsonElement EntityRaw { get; set; }

        public Dictionary<string, List<string>>? Entities { get; set; }
    }

    private sealed class Step1ExecutionData
    {
        public required string Prompt { get; set; }

        public required string LlmJson { get; set; }

        public required string QueryMode { get; set; }

        public required string QuerySentence { get; set; }

        public required List<string> Keywords { get; set; }

        public required Dictionary<string, List<string>> Entities { get; set; }

        public required LlmRecord LlmRecord { get; set; }
    }

    private sealed class Step1LlmResponse
    {
        public required string Provider { get; set; }

        public required string Model { get; set; }

        public required string ResponseJson { get; set; }

        public required int InputToken { get; set; }

        public required int OutputToken { get; set; }

        public required long DurationMs { get; set; }

        public required int PromptLength { get; set; }

        public required int ResponseLength { get; set; }
    }

    private sealed class FormulaKeywordDefinition
    {
        public required string FormulaName { get; set; }

        public required List<string> RelatedKeywords { get; set; }
    }

    private sealed class FormulaTaskCompiledSql
    {
        public required string TaskId { get; set; }

        public required CompiledSql CompiledSql { get; set; }
    }

    private sealed class FormulaTaskRows
    {
        public required string TaskId { get; set; }

        public required List<Dictionary<string, object?>> Rows { get; set; }
    }

    private sealed class Step3EntityListItem
    {
        [JsonPropertyName("ENTITY_NAME")]
        public required string EntityName { get; set; }

        [JsonPropertyName("ENTITY_ID")]
        public required string EntityId { get; set; }

        [JsonPropertyName("DATA_TYPE")]
        public required string DataType { get; set; }
    }
}
