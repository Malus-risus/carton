using carton.Core.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

/// <summary>
/// Tests for the hard kernel version gate: every entry point (start, online
/// download, custom install) must reject kernels older than 1.14.0 with a clear
/// message - the runtime config only speaks the 1.14 contract (services.api +
/// store_dns), so an old kernel would die with a cryptic FATAL instead.
/// </summary>
public sealed class KernelVersionGuardTests
{
    [Theory]
    [InlineData("1.14.0", true)]
    [InlineData("v1.14.0", true)]
    [InlineData("1.14.1", true)]
    [InlineData("1.15.0-alpha.1", true)]
    [InlineData("2.0.0", true)]
    [InlineData("1.13.9", false)]
    [InlineData("v1.13.9", false)]
    [InlineData("1.13.21", false)]
    [InlineData("1.12.0", false)]
    [InlineData("1.9.5", false)]
    public void IsSupported_EvaluatesMinimumVersion(string version, bool expected)
    {
        Assert.Equal(expected, KernelVersionGuard.IsSupported(version));
    }

    [Fact]
    public void IsSupported_RejectsUnknownByDefault()
    {
        Assert.False(KernelVersionGuard.IsSupported(null));
        Assert.False(KernelVersionGuard.IsSupported(""));
        Assert.False(KernelVersionGuard.IsSupported("   "));
    }

    [Fact]
    public void IsSupported_AllowsUnknownWhenExplicitlyPermitted()
    {
        // Pre-install probes may not have a version yet; the caller decides.
        Assert.True(KernelVersionGuard.IsSupported(null, allowUnknown: true));
    }

    [Theory]
    [InlineData("sing-box version 1.14.0\r\n\r\nEnvironment: go1.24", "1.14.0", true)]
    [InlineData("sing-box version 1.13.9", "1.13.9", false)]
    [InlineData("1.14.0", "1.14.0", true)]
    [InlineData("v1.14.1-beta.2 something", "1.14.1-beta.2", true)]
    public void IsSupported_ParsesRawVersionCommandOutput(string raw, string extracted, bool expected)
    {
        Assert.Equal(extracted, KernelVersionGuard.ExtractVersionNumber(raw));
        Assert.Equal(expected, KernelVersionGuard.IsSupported(raw));
    }

    [Fact]
    public void BuildUnsupportedMessage_ContainsVersionAndMinimum()
    {
        var message = KernelVersionGuard.BuildUnsupportedMessage("1.13.9");

        Assert.Contains("1.13.9", message);
        Assert.Contains(KernelVersionGuard.MinimumRequiredVersion, message);
        Assert.Contains("1.14.0", message);
    }

    [Fact]
    public void BuildUnsupportedMessage_HandlesUnknownVersion()
    {
        var message = KernelVersionGuard.BuildUnsupportedMessage(null);

        Assert.Contains(KernelVersionGuard.MinimumRequiredVersion, message);
        Assert.DoesNotContain("1.13", message);
    }

    [Fact]
    public void MinimumRequiredVersion_MatchesNativeApiFloor()
    {
        // The gate and the capability check must agree on the floor.
        Assert.True(carton.Core.Utilities.CartonApplicationInfo.SupportsNativeApi(KernelVersionGuard.MinimumRequiredVersion));
    }
}
