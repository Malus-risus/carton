using carton.Core.Utilities;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class CartonApplicationInfoTests
{
    [Theory]
    [InlineData("1.13.9", false)]
    [InlineData("v1.13.9", false)]
    [InlineData("1.14.0", true)]
    [InlineData("v1.14.0", true)]
    [InlineData("1.14.0-beta.1", true)]
    [InlineData("1.14.0-alpha.31-reF1nd", true)]
    [InlineData("1.14.0-re", true)]
    [InlineData("1.14.0+reF1nd", true)]
    [InlineData("reF1nd 1.14.0", true)]
    [InlineData("sing-box reF1nd 1.14.0", true)]
    [InlineData("1.13.13-reF1nd", false)]
    [InlineData("reF1nd 1.13.9", false)]
    [InlineData("1.15.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SupportsNativeApi_ReturnsExpectedResult(string? version, bool expected)
    {
        Assert.Equal(expected, CartonApplicationInfo.SupportsNativeApi(version));
    }
}
