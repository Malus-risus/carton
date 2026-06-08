namespace carton.Core.Utilities;

public static class FormatHelper
{
    private static readonly string[] ByteSuffixes = ["B", "KB", "MB", "GB", "TB"];

    public static string FormatBytes(long bytes)
    {
        // Idle traffic and zero counters are by far the most common inputs; return
        // a shared constant so the per-second dashboard refresh allocates nothing.
        if (bytes <= 0)
        {
            return "0 B";
        }

        var index = 0;
        double value = bytes;
        while (value >= 1024 && index < ByteSuffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {ByteSuffixes[index]}";
    }

    /// <summary>
    /// Formats a per-second rate (e.g. "1.5 MB/s"). Equivalent to
    /// <see cref="FormatBytes"/> with a "/s" suffix, but avoids the extra
    /// concatenation for the common idle (zero) case.
    /// </summary>
    public static string FormatBytesPerSecond(long bytesPerSecond)
    {
        return bytesPerSecond <= 0 ? "0 B/s" : FormatBytes(bytesPerSecond) + "/s";
    }

    public static string FormatByteProgress(long bytesReceived, long totalBytes, string unknownLabel = "unknown")
    {
        var received = FormatBytes(Math.Max(0, bytesReceived));
        return totalBytes > 0
            ? $"{received} / {FormatBytes(totalBytes)}"
            : $"{received} / {unknownLabel}";
    }
}
