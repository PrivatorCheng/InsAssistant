using System.Collections.Concurrent;
using API.Contracts;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace API.Tests;

public class InsuranceChatServiceTests
{
    [Fact]
    public async Task ChatAsync_ShouldAccumulateConversationHistoryForSameSession()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());

        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[{\"p\":\"demo\"}]");

        var capturedScripts = new List<string>();
        var finalChatCallCount = 0;
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string?, string, string, CancellationToken>((_, __, script, ___) => capturedScripts.Add(script))
            .ReturnsAsync(() =>
            {
                var currentScript = capturedScripts.LastOrDefault() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(currentScript))
                {
                    return new InsuranceChatCompletionResult { Content = "停室內車位 通勤里程" };
                }

                finalChatCallCount++;
                if (finalChatCallCount == 1)
                {
                    return new InsuranceChatCompletionResult { Content = "第一輪建議" };
                }

                return new InsuranceChatCompletionResult { Content = "第二輪建議" };
            });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        var first = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S001",
            UserMessage = "客戶有停室內車位"
        });

        var second = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S001",
            UserMessage = "客戶平日通勤里程很高"
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(sessionCache.TryGetValue("S001", out var histories));
        Assert.NotNull(histories);
        Assert.Equal(4, histories!.Count);
        Assert.Contains("- 業務員: 客戶有停室內車位", histories);
        Assert.Contains("- AI教練: 第一輪建議", histories);

        var nonEmptyStoryScripts = capturedScripts
            .Where(script => !string.IsNullOrWhiteSpace(script))
            .ToList();

        Assert.True(nonEmptyStoryScripts.Count >= 2);
        Assert.Contains("【過去的多輪對話歷史紀錄】", nonEmptyStoryScripts[1]);
        Assert.Contains("- 業務員: 客戶有停室內車位", nonEmptyStoryScripts[1]);
        Assert.Contains("- AI教練: 第一輪建議", nonEmptyStoryScripts[1]);
    }

    [Fact]
    public async Task ResetSession_ShouldRemoveSessionHistory()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());

        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[]");
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsuranceChatCompletionResult { Content = "測試回覆" });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S002",
            UserMessage = "測試訊息"
        });

        var removed = sut.ResetSession("S002");

        Assert.True(removed);
        Assert.False(sessionCache.ContainsKey("S002"));
    }

    [Fact]
    public async Task ChatAsync_ShouldSendToLlmEvenWhenMessageContainsPersonalData_WhenPiiGateIsDisabled()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>(MockBehavior.Strict);
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());
        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[]");
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsuranceChatCompletionResult { Content = "PII 訊息照常處理" });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        var response = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S003",
            UserMessage = "我的姓名是王小明，手機是 0912-345-678"
        });

        Assert.Equal("PII 訊息照常處理", response.Reply);
        Assert.True(sessionCache.TryGetValue("S003", out var histories));
        Assert.NotNull(histories);
        Assert.Equal(2, histories!.Count);
        mockBrainService.Verify(service => service.EnsureInitialized(), Times.Once);
        mockCompletionService.Verify(
            service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ChatAsync_ShouldUseReplyContentAndExtractCustomerFields_WhenSalesDialogSpecJsonReturned()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());
        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[]");
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsuranceChatCompletionResult
            {
                Content = """
                    {
                       "cust_name" : "張三",
                       "cust_tag_no" : "ABC-123",
                       "reply_content" : "這是我的回覆"
                    }
                    """
            });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        var response = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S004",
            UserMessage = "客戶想詢問險種",
            UsePromptTemplate1 = true
        });

        Assert.Equal("這是我的回覆", response.Reply);
        Assert.Equal("張三", response.CustName);
        Assert.Equal("ABC-123", response.CustTagNo);
        Assert.True(sessionCache.TryGetValue("S004", out var histories));
        Assert.NotNull(histories);
        Assert.Contains("- AI教練: 這是我的回覆", histories!);
    }

    [Fact]
    public async Task ChatAsync_ShouldContinueUsingHydratedSessionHistory()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());
        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[]");

        var capturedScripts = new List<string>();
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string?, string, string, CancellationToken>((_, __, script, ___) => capturedScripts.Add(script))
            .ReturnsAsync(new InsuranceChatCompletionResult { Content = "續聊回覆" });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        sut.HydrateSession("S005", new[]
        {
            "- 業務員: 第一輪問題",
            "- AI教練: 第一輪回覆"
        });

        var response = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S005",
            UserMessage = "第二輪問題"
        });

        Assert.Equal("續聊回覆", response.Reply);
        Assert.Equal(2, capturedScripts.Count);
        Assert.Contains("- 業務員: 第一輪問題", capturedScripts[1]);
        Assert.Contains("- AI教練: 第一輪回覆", capturedScripts[1]);
        Assert.Contains("第二輪問題", capturedScripts[1]);
    }

    [Fact]
    public async Task ChatAsync_ShouldRouteToCustomerSalesPrompt_WhenCustomerDialogSpecIsAutoInsure()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());
        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[{\"ProductName\":\"測試商品\"}]");

        var capturedPrompts = new List<string>();
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string?, string, string, CancellationToken>((_, prompt, _, __) => capturedPrompts.Add(prompt))
            .ReturnsAsync(() =>
            {
                if (capturedPrompts.Count == 1)
                {
                    return new InsuranceChatCompletionResult
                    {
                        Content = """
                            {
                               "diag_mode" : "AUTO_INSURE",
                               "reply_content" : "第一層智能客服回覆"
                            }
                            """
                    };
                }

                return new InsuranceChatCompletionResult
                {
                    Content = """
                        {
                           "diag_mode" : "AUTO_INSURE",
                           "reply_content" : "第二層車險投保回覆"
                        }
                        """
                };
            });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        var response = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S006",
            UserMessage = "我想幫新車投保",
            UsePromptTemplate1 = false
        });

        Assert.Equal("第二層車險投保回覆", response.Reply);
        Assert.Equal(2, capturedPrompts.Count);
        Assert.Contains("測試提示詞2", capturedPrompts[0]);
        Assert.Contains("測試顧客車險提示詞", capturedPrompts[1]);
        Assert.Contains("測試商品", capturedPrompts[1]);
    }

    [Fact]
    public async Task ChatAsync_ShouldUseReplyContentDirectly_WhenCustomerDialogSpecIsNotAutoInsure()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());
        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[]");
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsuranceChatCompletionResult
            {
                Content = """
                    {
                       "diag_mode" : "QUERY_CLAIM",
                       "reply_content" : "理賠查詢回覆"
                    }
                    """
            });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        var response = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S007",
            UserMessage = "我要查理賠",
            UsePromptTemplate1 = false
        });

        Assert.Equal("理賠查詢回覆", response.Reply);
        mockCompletionService.Verify(
            service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ChatAsync_ShouldUseClaimPromptAndReplaceClaimTokens_WhenClaimAssistantModeSelected()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());
        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[{\"ProductName\":\"測試商品\"}]");

        var capturedPrompts = new List<string>();
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string?, string, string, CancellationToken>((_, prompt, _, __) => capturedPrompts.Add(prompt))
            .ReturnsAsync(() =>
            {
                if (capturedPrompts.Count == 1)
                {
                    return new InsuranceChatCompletionResult
                    {
                        Content = "事故時間 事故地點 車頭碰撞 第三人責任險"
                    };
                }

                return new InsuranceChatCompletionResult
                {
                    Content = """
                        {
                           "cust_name" : "王小明",
                           "cust_tag_no" : "ABC-123",
                           "insure_type_list" : ["丙式車體險", "第三人責任險"],
                                  "todo_info" : [
                                      { "name" : "事故時間", "value" : "" },
                                      { "name" : "事故地點", "value" : "" }
                                  ],
                                  "todo_document" : [
                                      { "name" : "事故筆錄", "uploaded" : false },
                                      { "name" : "醫療診斷證明書", "uploaded" : false }
                                  ],
                           "reply_content" : "理賠助理回覆"
                        }
                        """
                };
            });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        var response = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S008",
            UserMessage = "請協助判斷肇責",
            PromptTemplateMode = "claimAssistant"
        });

        Assert.Equal("理賠助理回覆", response.Reply);
        Assert.Equal("王小明", response.CustName);
        Assert.Equal("ABC-123", response.CustTagNo);
        Assert.Equal(["丙式車體險", "第三人責任險"], response.InsureTypeList);
        Assert.Equal(["事故時間", "事故地點"], response.TodoInfo.Select(item => item.Name));
        Assert.Equal(["事故筆錄", "醫療診斷證明書"], response.TodoDocument.Select(item => item.Name));
        Assert.Equal(2, capturedPrompts.Count);
        Assert.Contains("測試前置向量提示詞", capturedPrompts[0]);
        Assert.Contains("測試理賠提示詞", capturedPrompts[1]);
        Assert.DoesNotContain("{brain.AllProductsJson}", capturedPrompts[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{POLICY_INSTYPE}", capturedPrompts[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{CLAIM_REFERENCE_DATA}", capturedPrompts[1], StringComparison.OrdinalIgnoreCase);
        Assert.Equal("事故時間 事故地點 車頭碰撞 第三人責任險", response.LlmKeyword);
        Assert.Contains("測試前置向量提示詞", response.LlmKeywordSystemPrompt ?? string.Empty);
    }

    [Fact]
    public async Task ChatAsync_ShouldParseDialogSpec_WhenReplyContentContainsRawMultilineText()
    {
        var sessionCache = new ConcurrentDictionary<string, List<string>>();
        var mockBrainService = new Mock<IInsuranceBrainService>();
        var mockCompletionService = new Mock<IInsuranceChatCompletionService>();
        var piiDetectionService = new PresidioPiiDetectionService();
        var mockEnvironment = new Mock<IWebHostEnvironment>();
        var mockLogger = new Mock<ILogger<InsuranceChatService>>();

        mockEnvironment.SetupGet(environment => environment.ContentRootPath).Returns(CreateTempPromptDirectory());
        mockBrainService.SetupGet(service => service.AllProductsJson).Returns("[]");
        mockCompletionService
            .Setup(service => service.ExecuteAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsuranceChatCompletionResult
            {
                Content = @"{
   ""cust_name"" : ""王小明"",
   ""cust_tag_no"" : null,
   ""reply_content"" : ""第一行
第二行
第三行""
}"
            });

        var sut = CreateSut(
            sessionCache,
            mockBrainService.Object,
            mockCompletionService.Object,
            piiDetectionService,
            mockEnvironment.Object,
            mockLogger.Object);

        var response = await sut.ChatAsync(new SimpleChatRequest
        {
            SessionId = "S009",
            UserMessage = "請給我建議",
            PromptTemplateMode = "customerService"
        });

        Assert.Equal("王小明", response.CustName);
        Assert.Equal("第一行\n第二行\n第三行", response.Reply);
    }

    private static string CreateTempPromptDirectory()
    {
        var contentRootPath = Path.Combine(Path.GetTempPath(), "insurance-chat-tests", Guid.NewGuid().ToString("N"));
        var docDirectory = Path.Combine(contentRootPath, "doc");
        Directory.CreateDirectory(docDirectory);

        File.WriteAllText(Path.Combine(docDirectory, "Prompt_Sales.txt"), "測試提示詞1\n{brain.AllProductsJson}");
        File.WriteAllText(Path.Combine(docDirectory, "Prompt_PreVector.txt"), "測試前置向量提示詞\n{conversationHistory}\n{currentQuery}");
        File.WriteAllText(Path.Combine(docDirectory, "Prompt_Customer.txt"), "測試提示詞2\n{brain.AllProductsJson}");
        File.WriteAllText(Path.Combine(docDirectory, "Prompt_Customer_Sales.txt"), "測試顧客車險提示詞\n{brain.AllProductsJson}");
        File.WriteAllText(Path.Combine(docDirectory, "Prompt_Claim.txt"), "測試理賠提示詞\n{brain.AllProductsJson}\n{POLICY_INSTYPE}\n{CLAIM_REFERENCE_DATA}");

        var claimRuleDirectory = Path.Combine(docDirectory, "ClaimRule");
        Directory.CreateDirectory(claimRuleDirectory);
        File.WriteAllText(Path.Combine(claimRuleDirectory, "CarRule.txt"), "測試理賠作業準則");
        return contentRootPath;
    }

    private static InsuranceChatService CreateSut(
        ConcurrentDictionary<string, List<string>> sessionCache,
        IInsuranceBrainService brainService,
        IInsuranceChatCompletionService completionService,
        IPiiDetectionService piiDetectionService,
        IWebHostEnvironment environment,
        ILogger<InsuranceChatService> logger)
    {
        var mockComplianceService = new Mock<IComplianceConsultingService>();
        var mockEmbeddingService = new Mock<IGoogleEmbeddingVectorQueryService>();
        var mockGeminiContextCacheService = new Mock<IGeminiContextCacheService>();

        mockComplianceService
            .Setup(service => service.SearchProductClausesAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ComplianceProductClauseSearchResult>());

        mockComplianceService
            .Setup(service => service.SearchLawReferencesAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ComplianceLawReferenceSearchResult>());

        mockEmbeddingService
            .Setup(service => service.GenerateEmbeddingsAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ReadOnlyMemory<float>(new[] { 0.1f }) });

        mockGeminiContextCacheService
            .SetupGet(service => service.CachedContentName)
            .Returns((string?)null);

        return new InsuranceChatService(
            sessionCache,
            brainService,
            completionService,
            mockComplianceService.Object,
            mockEmbeddingService.Object,
            mockGeminiContextCacheService.Object,
            piiDetectionService,
            environment,
            logger);
    }
}
