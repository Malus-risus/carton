using carton.GUI.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

/// <summary>
/// Regression for the user-reported log line: sing-box emits an ANSI color code
/// followed by a line-counter sequence (ESC[NNNN]) - the counter's terminator is ']',
/// not a CSI letter. The stripper previously only accepted CSI-letter terminators for
/// ESC sequences, so the ESC byte was swallowed by the UI while "[0001]" leaked into
/// the visible message.
/// </summary>
public sealed class AnsiReproProbe
{
    [Theory]
    [InlineData("\u001b[37mDEBUG\u001b[0m\u001b[0001] dns: lookup succeed for dm.89330595.xyz: 69.63.220.57")]
    [InlineData("\u001b[0001] dns: lookup succeed")]
    public void EscCounterSequences_FullyStripped(string raw)
    {
        var parsed = LogParser.ParseSingBoxLog(raw, "21:50:16");

        Assert.True(parsed.Message.IndexOf('') < 0, "ESC leaked into message");
        Assert.True(parsed.Message.IndexOf("[0001]") < 0, "[0001] leaked into message");
        Assert.True(parsed.Message.IndexOf("[0012]") < 0, "[0012] leaked into message");
        Assert.Contains("dns: lookup succeed", parsed.Message);
    }

    [Fact]
    public void EscCounterSequence_InlineLevelWithCounter()
    {
        var parsed = LogParser.ParseSingBoxLog("INFO\u001b[0m\u001b[0012] outbound/proxy: connected", "21:50:16");
        Assert.True(parsed.Message.IndexOf('\u001b') < 0, "ESC leaked into message");
        Assert.True(parsed.Message.IndexOf("[0012]") < 0, "[0012] leaked into message");
        Assert.Contains("outbound/proxy: connected", parsed.Message);
    }

    [Fact]
    public void OrphanStripper_LeavesPlainBracketedTextIntact()
    {
        // The >=2-digit guard and the orphan/ESC terminator split: ordinary bracketed
        // text must survive the ANSI stripper untouched (the layer-level parser may
        // still interpret sing-box prefixes downstream, but never eats brackets here).
        var stripped = carton.Core.Services.KernelLogCleaner.StripAnsi("read config arr[1] done");
        Assert.Equal("read config arr[1] done", stripped);
    }

    [Fact]
    public void BareCounterPrefix_StillParsedAsBefore()
    {
        // The legacy "FATAL[0000] msg" shape (no ESC) keeps its established behaviour:
        // stripped by the counter/prefix parser, not by the ANSI stripper.
        var parsed = LogParser.ParseSingBoxLog("FATAL[0000] decode config at C:\\test\\runtime.json", "21:30:40");
        Assert.Equal("Fatal", parsed.Level);
        Assert.Equal("decode config at C:\\test\\runtime.json", parsed.Message);
    }
}
