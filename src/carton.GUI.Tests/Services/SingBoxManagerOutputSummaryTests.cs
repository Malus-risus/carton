using carton.Core.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

/// <summary>
/// The start-failure status line only gets a short diagnostic tail of the kernel
/// console output. These pin down which lines count as diagnostics and how the
/// tail is chosen; the full capture stays on the kernel log channel.
/// </summary>
public sealed class SingBoxManagerOutputSummaryTests
{
    [Theory]
    [InlineData("ERROR[0000] start service: listen tcp 127.0.0.1:2080: bind: address already in use")]
    [InlineData("FATAL[0000] start service: initialize inbound/tun[tun-in]: configure tun interface: operation not permitted")]
    [InlineData("WARN[0012] dns: lookup failed")]
    [InlineData("ERROR start service: bad config")]
    [InlineData("+0800 2026-09-10 00:42:52 ERROR [1234 0ms] inbound/tun[tun-in]: create tun failed")]
    [InlineData("panic: runtime error: invalid memory address or nil pointer dereference")]
    public void IsKernelDiagnosticLine_TrueForWarnOrWorse(string line)
    {
        Assert.True(SingBoxManager.IsKernelDiagnosticLine(line));
    }

    [Theory]
    [InlineData("INFO[0000] router: loaded geoip database")]
    [InlineData("DEBUG[0001] outbound/direct[direct]: dial tcp")]
    [InlineData("+0800 2026-09-10 00:42:52 INFO sing-box started (0.12s)")]
    [InlineData("INFO[0000] outbound/vmess[ERROR-HK-01]: WARN inside the message must not count")]
    [InlineData("goroutine 1 [running]:")]
    [InlineData("")]
    public void IsKernelDiagnosticLine_FalseForInfoAndLevelWordsInsideTheMessage(string line)
    {
        Assert.False(SingBoxManager.IsKernelDiagnosticLine(line));
    }

    [Fact]
    public void SummarizeKernelOutput_KeepsOnlyTheDiagnosticTail()
    {
        var lines = new[]
        {
            "INFO[0000] router: loaded geoip database",
            "WARN[0000] first warning",
            "INFO[0000] inbound/mixed[mixed-in]: tcp server started",
            "ERROR[0000] second problem",
            "FATAL[0000] start service: bind: address already in use",
        };

        var summary = SingBoxManager.SummarizeKernelOutput(lines, maxLines: 2);

        Assert.Equal(
            "ERROR[0000] second problem\nFATAL[0000] start service: bind: address already in use",
            summary);
    }

    [Fact]
    public void SummarizeKernelOutput_FallsBackToTheLastLinesWithoutDiagnostics()
    {
        var lines = new[] { "INFO[0000] one", "INFO[0000] two", "INFO[0000] three" };

        var summary = SingBoxManager.SummarizeKernelOutput(lines, maxLines: 2);

        Assert.Equal("INFO[0000] two\nINFO[0000] three", summary);
    }

    [Fact]
    public void SummarizeKernelOutput_EmptyWhenNothingBuffered()
    {
        Assert.Equal(string.Empty, SingBoxManager.SummarizeKernelOutput(Array.Empty<string>(), maxLines: 5));
    }
}
