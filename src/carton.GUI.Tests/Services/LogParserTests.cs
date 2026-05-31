using carton.GUI.Services;
using Xunit;

namespace carton.GUI.Tests.Services;

public sealed class LogParserTests
{
    [Fact]
    public void ParseSingBoxLog_CompactFatalPrefix_ReturnsErrorLevelAndMessage()
    {
        var input = "FATAL[0000] decode config at C:\\test\\runtime.json: json: unknown field \"store_dns\"";

        var parsed = LogParser.ParseSingBoxLog(input, "21:30:40");

        Assert.Equal("21:30:40", parsed.Time);
        Assert.Equal("Fatal", parsed.Level);
        Assert.Equal("decode config at C:\\test\\runtime.json: json: unknown field \"store_dns\"", parsed.Message);
    }

    [Fact]
    public void ParseSingBoxLog_AnsiCompactFatalPrefix_StripsAnsiAndReturnsErrorLevel()
    {
        var input = "\u001b[31mFATAL\u001b[0m[0000] decode config at C:\\test\\runtime.json";

        var parsed = LogParser.ParseSingBoxLog(input, "21:30:40");

        Assert.Equal("Fatal", parsed.Level);
        Assert.Equal("decode config at C:\\test\\runtime.json", parsed.Message);
    }

    [Fact]
    public void ParseSingBoxLog_StructuredErrorLog_PreservesParsedTime()
    {
        var input = "main router 2026-05-31T21:30:40+08:00 ERROR outbound unavailable";

        var parsed = LogParser.ParseSingBoxLog(input, "00:00:00");

        Assert.Equal("21:30:40", parsed.Time);
        Assert.Equal("Error", parsed.Level);
        Assert.Equal("outbound unavailable", parsed.Message);
    }

    [Fact]
    public void ParseSingBoxLog_PlainMessage_DefaultsToInfo()
    {
        var input = "startup complete";

        var parsed = LogParser.ParseSingBoxLog(input, "21:30:40");

        Assert.Equal("Info", parsed.Level);
        Assert.Equal("startup complete", parsed.Message);
    }
}
