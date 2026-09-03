using System.Buffers;

namespace carton.Core.Services;

/// <summary>
/// Strips ANSI escape sequences (color codes and sing-box line counters) from raw
/// kernel log lines. sing-box colors every output channel; the UI log store passes
/// entries that already carry a level through verbatim, so stripping must happen at
/// the ingestion source (gRPC stream) - not only inside the GUI-level parser.
/// </summary>
public static class KernelLogCleaner
{
    /// <summary>Strips ANSI sequences; idempotent (clean text passes through unchanged).</summary>
    public static string StripAnsi(string message) => StripAnsiEscapeSequences(message);

    private static string StripAnsiEscapeSequences(string message)
    {
        var escapeIndex = message.IndexOf('\u001b');
        var orphanCsiIndex = FindOrphanCsiIndex(message);
        if (escapeIndex < 0 && orphanCsiIndex < 0)
        {
            return message;
        }

        var firstSpecialIndex = escapeIndex < 0
            ? orphanCsiIndex
            : orphanCsiIndex < 0
                ? escapeIndex
                : Math.Min(escapeIndex, orphanCsiIndex);

        var rented = ArrayPool<char>.Shared.Rent(message.Length);
        try
        {
            message.AsSpan(0, firstSpecialIndex).CopyTo(rented);
            var writeIndex = firstSpecialIndex;
            for (var readIndex = firstSpecialIndex; readIndex < message.Length; readIndex++)
            {
                var ch = message[readIndex];
                if (ch == '\u001b' &&
                    readIndex + 1 < message.Length &&
                    message[readIndex + 1] == '[')
                {
// Real ESC-prefixed CSI: also accepts ']' (sing-box line counter
                    // ESC[NNNN]) so the renderer swallowing the ESC never leaks
                    // "[NNNN]" into the visible message.
                    var endIndex = FindEscCsiTerminator(message, readIndex + 2);
                    if (endIndex >= 0)
                    {
                        readIndex = endIndex;
                        continue;
                    }
                }

                if (ch == '[' &&
                    readIndex + 1 < message.Length &&
                    IsCsiParameterChar(message[readIndex + 1]))
                {
                    var endIndex = FindCsiTerminator(message, readIndex + 1);
                    if (endIndex >= 0)
                    {
                        readIndex = endIndex;
                        continue;
                    }
                }

                rented[writeIndex++] = ch;
            }

            return new string(rented, 0, writeIndex);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static int FindOrphanCsiIndex(string message)
    {
        for (var i = 0; i < message.Length - 1; i++)
        {
            if (message[i] == '[' && IsCsiParameterChar(message[i + 1]))
            {
                var endIndex = FindCsiTerminator(message, i + 1);
                if (endIndex >= 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindEscCsiTerminator(string message, int startIndex)
    {
        for (var i = startIndex; i < message.Length; i++)
        {
            var ch = message[i];
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
            {
                return i;
            }

            if (ch == ']' && i - startIndex >= 2)
            {
                // sing-box line counter ESC[NNNN]: at least two digit parameters
                // before ']' - the guard keeps ordinary bracketed text intact.
                return i;
            }

            if (!IsCsiParameterChar(ch))
            {
                return -1;
            }
        }

        return -1;
    }

    private static int FindCsiTerminator(string message, int startIndex)
    {
        for (var i = startIndex; i < message.Length; i++)
        {
            var ch = message[i];
            if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
            {
                return i;
            }

            if (!IsCsiParameterChar(ch))
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool IsCsiParameterChar(char ch)
    {
        return (ch >= '0' && ch <= '9') || ch == ';';
    }

}
