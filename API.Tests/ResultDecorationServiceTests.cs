using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using API.Contracts;
using API.Infrastructure;
using API.Models;
using API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace API.Tests;

/// <summary>
/// 結果裝飾服務測試。
/// </summary>
public class ResultDecorationServiceTests
{
    private readonly Mock<IReportRepository> _mockRepository;
    private readonly Mock<ILogger<ResultDecorationService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly ResultDecorationService _service;

    public ResultDecorationServiceTests()
    {
        _mockRepository = new Mock<IReportRepository>();
        _mockLogger = new Mock<ILogger<ResultDecorationService>>();
        _mockConfiguration = new Mock<IConfiguration>();

        _service = new ResultDecorationService(
            _mockRepository.Object,
            _mockLogger.Object,
            _mockConfiguration.Object);
    }

    [Fact]
    public async Task DecorateResultsAsync_ShouldReturnOriginalRows_WhenNoRulesMatch()
    {
        // Arrange
        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                { "claim_id", "CLM001" },
                { "insure_type", "001" }
            }
        };

        var targetTables = new List<string> { "clam_clm_main" };
        var dataFields = new List<DimensionDetail>
        {
            new() { Table = "clam_clm_main", Field = "claim_id", Alias = "claim_id" },
            new() { Table = "clam_clm_main", Field = "insure_type", Alias = "insure_type" }
        };

        // Act
        var result = await _service.DecorateResultsAsync(rows, targetTables, dataFields, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("001", result[0]["insure_type"]);
    }

    [Fact]
    public async Task DecorateResultsAsync_ShouldHandleEmptyRows()
    {
        // Arrange
        var rows = new List<Dictionary<string, object?>>();
        var targetTables = new List<string> { "clam_clm_main" };
        var dataFields = new List<DimensionDetail>();

        // Act
        var result = await _service.DecorateResultsAsync(rows, targetTables, dataFields, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task DecorateResultsAsync_ShouldHandleNullValues()
    {
        // Arrange
        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                { "claim_id", "CLM001" },
                { "insure_type", null }
            }
        };

        var targetTables = new List<string> { "clam_clm_main" };
        var dataFields = new List<DimensionDetail>
        {
            new() { Table = "clam_clm_main", Field = "claim_id", Alias = "claim_id" },
            new() { Table = "clam_clm_main", Field = "insure_type", Alias = "insure_type" }
        };

        // Act
        var result = await _service.DecorateResultsAsync(rows, targetTables, dataFields, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Null(result[0]["insure_type"]);
    }

    [Fact]
    public async Task DecorateResultsAsync_ShouldHandleCaseInsensitiveColumns()
    {
        // Arrange
        var rows = new List<Dictionary<string, object?>>
        {
            new()
            {
                { "Claim_ID", "CLM001" },
                { "Insure_Type", "INSURE_TYPE_001" }
            }
        };

        var targetTables = new List<string> { "clam_clm_main" };
        var dataFields = new List<DimensionDetail>
        {
            new() { Table = "clam_clm_main", Field = "claim_id", Alias = "Claim_ID" },
            new() { Table = "clam_clm_main", Field = "insure_type", Alias = "Insure_Type" }
        };

        // Act
        var result = await _service.DecorateResultsAsync(rows, targetTables, dataFields, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.True(result[0].ContainsKey("Insure_Type"));
    }
}
