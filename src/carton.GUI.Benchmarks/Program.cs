using System.Diagnostics;
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
