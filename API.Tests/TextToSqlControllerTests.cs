using API.Contracts;
using API.Controllers.G1;
using API.Models;
using API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Claims;

namespace API.Tests;

public class TextToSqlControllerTests
{
    [Fact]
    public async Task QueryTextToSql_ShouldReturnDisabledMessage_WhenFeatureIsTurnedOff()
    {
        var mockService = new Mock<ITextToSqlService>();

        var mockConfiguration = new Mock<IConfiguration>();

        var controller = new TextToSqlController(mockService.Object, mockConfiguration.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        var request = new NaturalLanguageQueryRequest
        {
            Query = "查詢保單",
            DepartmentId = null,
            UserId = "G901",
            LlmProvider = "Gpt",
            SelectedHistoryFileName = null
        };

        var response = await controller.QueryTextToSql(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(-1, response.Code);
        Assert.Equal("Step1-Step5 功能已停用", response.Msg);
        Assert.Null(response.Data);
        mockService.Verify(
            service => service.QueryAsync(It.IsAny<NaturalLanguageQueryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
