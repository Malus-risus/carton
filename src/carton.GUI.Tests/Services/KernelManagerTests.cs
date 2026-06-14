using System.Reflection;
using carton.Core.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class KernelManagerTests
{
    [Theory]
    [InlineData("sing-box version 1.14.0", "1.14.0")]
    [InlineData("sing-box reF1nd 1.14.0", "1.14.0")]
    [InlineData("sing-box version 1.14.0-re", "1.14.0-re")]
    [InlineData("sing-box version 1.13.13-reF1nd", "1.13.13-reF1nd")]
    [InlineData("sing-box version 1.14.0-alpha.31-reF1nd", "1.14.0-alpha.31-reF1nd")]
    [InlineData("reF1nd sing-box 1.13.9", "1.13.9")]
    public void ParseInstalledVersion_ExtractsSemanticVersion(string output, string expected)
    {
        var method = typeof(KernelManager).GetMethod(
            "ParseInstalledVersion",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var actual = method.Invoke(null, [new[] { output }]);
        Assert.Equal(expected, actual);
    }
}
