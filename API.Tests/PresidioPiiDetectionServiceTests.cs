using API.Services;

namespace API.Tests;

public class PresidioPiiDetectionServiceTests
{
    [Fact]
    public void MaskPersonalData_ShouldMaskNameWithTitle_WhenSentenceContainsManagerTitle()
    {
        var service = new PresidioPiiDetectionService();
        const string input = "我今天要去拜訪張三經理，他剛牽了一台全新的特斯拉 Model Y。";

        var masked = service.MaskPersonalData(input);

        Assert.DoesNotContain("張三", masked, StringComparison.Ordinal);
        Assert.Contains("經理", masked, StringComparison.Ordinal);
        Assert.NotEqual(input, masked);
    }
}
