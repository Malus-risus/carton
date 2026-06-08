using System.Diagnostics;
using System.Text.RegularExpressions;
using carton.Core.Utilities;
using carton.GUI.Services;

var samples = new (string Name, string Message)[]
{
    (
        "compact-fatal",
        "FATAL[0000] decode config at C:\\Users\\bf\\AppData\\Roaming\\Carton\\data\\configs\\runtime\\profile_1.runtime.json: experimental.cache_file.store_dns: json: unknown field \"store_dns\""
    ),
    (
        "ansi-fatal",
        "\u001b[31mFATAL\u001b[0m[0000] decode config at C:\\Users\\bf\\AppData\\Roaming\\Carton\\data\\configs\\runtime\\profile_1.runtime.json: experimental.cache_file.store_dns: json: unknown field \"store_dns\""
    ),
    (
        "structured-error",
        "ERROR 2026-05-31T21:30:40+08:00 main router: outbound unavailable"
    ),
    (
        "plain-info",
        "startup complete"
    )
};

const int warmupIterations = 50_000;
const int measureIterations = 1_000_000;

Console.WriteLine($"Runtime: {Environment.Version}");
Console.WriteLine($"Warmup iterations: {warmupIterations:n0}");
Console.WriteLine($"Measure iterations: {measureIterations:n0}");
Console.WriteLine();

foreach (var sample in samples)
{
    for (var i = 0; i < warmupIterations; i++)
    {
        _ = LogParser.ParseSingBoxLog(sample.Message, "21:30:40");
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    for (var i = 0; i < measureIterations; i++)
    {
        _ = LogParser.ParseSingBoxLog(sample.Message, "21:30:40");
    }
    stopwatch.Stop();
    var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;

    var nsPerOp = stopwatch.Elapsed.TotalMilliseconds * 1_000_000d / measureIterations;
    Console.WriteLine($"{sample.Name,-18} {nsPerOp,10:F1} ns/op  alloc={allocated / (double)measureIterations:F1} B/op");
}

Console.WriteLine();
Console.WriteLine("=== FormatBytes allocation (mixed values incl. idle 0) ===");

long[] byteValues = [0, 0, 0, 512, 1536, 5_242_880, 1_073_741_824];

static string FormatBytesOld(long bytes)
{
    string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
    var index = 0;
    double value = bytes;
    while (value >= 1024 && index < suffixes.Length - 1)
    {
        value /= 1024;
        index++;
    }

    return $"{value:0.##} {suffixes[index]}";
}

void MeasureFormatter(string name, Func<long, string> fn)
{
    foreach (var v in byteValues)
    {
        _ = fn(v);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    const int iters = 2_000_000;
    var before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < iters; i++)
    {
        _ = fn(byteValues[i % byteValues.Length]);
    }
    sw.Stop();
    var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"{name,-26} {sw.Elapsed.TotalMilliseconds * 1_000_000d / iters,8:F1} ns/op  alloc={alloc / (double)iters:F1} B/op");
}

MeasureFormatter("FormatBytes (old)", FormatBytesOld);
MeasureFormatter("FormatBytes (new)", FormatHelper.FormatBytes);
MeasureFormatter("FormatBytesPerSecond (new)", FormatHelper.FormatBytesPerSecond);

Console.WriteLine();
Console.WriteLine("=== Emoji pre-scan vs compiled regex (plain ASCII log line) ===");

const string emojiSample = "2026-05-31T21:30:40+08:00 main router: outbound proxy-hk-01 connected 192.168.1.2:54321";
var emojiRegex = new Regex(
    @"(\uD83C[\uDDE6-\uDDFF]){2}|\uD83C[\uDC00-\uDCFF\uDD70-\uDDE5\uDE00-\uDFFF]|[\uD83D-\uD83E][\uDC00-\uDFFF]|[☀-➿]️?",
    RegexOptions.Compiled);

static bool ScanCandidate(string text)
{
    var span = text.AsSpan();
    return span.ContainsAnyInRange('☀', '➿') || span.ContainsAnyInRange('\uD800', '\uDBFF');
}

void MeasureEmoji(string name, Func<string, bool> hasEmoji)
{
    for (var i = 0; i < 10_000; i++)
    {
        _ = hasEmoji(emojiSample);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    const int iters = 2_000_000;
    var before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < iters; i++)
    {
        _ = hasEmoji(emojiSample);
    }
    sw.Stop();
    var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"{name,-26} {sw.Elapsed.TotalMilliseconds * 1_000_000d / iters,8:F1} ns/op  alloc={alloc / (double)iters:F1} B/op");
}

MeasureEmoji("regex.Matches().Count>0", s => emojiRegex.Matches(s).Count > 0);
MeasureEmoji("pre-scan (new fast path)", ScanCandidate);
