using System;
using System.Buffers;

namespace carton.GUI.Services;

internal static class LogParser
{
    public static (string Time, string Level, string Message) ParseCartonLog(string message, string currentTime)
    {
        if (string.IsNullOrEmpty(message))
        {
            return (currentTime, "Info", string.Empty);
        }

        message = StripAnsiEscapeSequences(message);

        return message switch
        {
            _ when message.StartsWith("[ERROR] ", StringComparison.OrdinalIgnoreCase)
                => (currentTime, "Error", message["[ERROR] ".Length..]),
            _ when message.StartsWith("[WARNING] ", StringComparison.OrdinalIgnoreCase)
                => (currentTime, "Warn", message["[WARNING] ".Length..]),
            _ when message.StartsWith("[WARN] ", StringComparison.OrdinalIgnoreCase)
                => (currentTime, "Warn", message["[WARN] ".Length..]),
            _ when message.StartsWith("[DEBUG] ", StringComparison.OrdinalIgnoreCase)
                => (currentTime, "Debug", message["[DEBUG] ".Length..]),
            _ when message.StartsWith("[INFO] ", StringComparison.OrdinalIgnoreCase)
                => (currentTime, "Info", message["[INFO] ".Length..]),
            _ => (currentTime, "Info", message)
        };
    }

    public static (string Time, string Level, string Message) ParseSingBoxLog(string message, string currentTime)
    {
        var msg = StripAnsiEscapeSequences(message);

        var span = msg.AsSpan().TrimStart();
        var prefixedLevel = TryStripPrefixedLevel(ref span);
        prefixedLevel ??= TryStripInlineLevel(ref span);

        if (prefixedLevel != null && TryParseCompactSingBoxLog(span, currentTime, prefixedLevel, out var compact))
        {
            return compact;
        }

        var tokenIndex = 0;
        var position = 0;
        var timeTokenStart = -1;
        var timeTokenLength = 0;
        var payloadStart = -1;

        while (position < span.Length)
        {
            while (position < span.Length && span[position] == ' ')
            {
                position++;
            }

            if (position >= span.Length)
            {
                break;
            }

            var start = position;
            while (position < span.Length && span[position] != ' ')
            {
                position++;
            }

            var length = position - start;
            if (tokenIndex == 2)
            {
                timeTokenStart = start;
                timeTokenLength = length;
            }
            else if (tokenIndex == 3)
            {
                payloadStart = start;
                break;
            }

            tokenIndex++;
        }

        if (payloadStart < 0)
        {
            return (currentTime, prefixedLevel ?? "Info", span.ToString());
        }

        var time = ExtractTime(span, timeTokenStart, timeTokenLength, currentTime);

        var payload = span[payloadStart..].TrimStart();
        if (payload.Length == 0)
        {
            return (time, prefixedLevel ?? "Info", string.Empty);
        }

        var separatorIndex = payload.IndexOf(' ');
        ReadOnlySpan<char> levelToken;
        ReadOnlySpan<char> messagePart;
        if (separatorIndex < 0)
        {
            levelToken = payload;
            messagePart = ReadOnlySpan<char>.Empty;
        }
        else
        {
            levelToken = payload[..separatorIndex];
            messagePart = payload[(separatorIndex + 1)..].TrimStart();
        }

        var level = TryNormalizeSingBoxLevel(levelToken, out var parsedLevel)
            ? parsedLevel
            : prefixedLevel ?? "Info";

        if (messagePart.Length > 0 && messagePart[0] == '[')
        {
            var endIndex = messagePart.IndexOf(']');
            if (endIndex > 0)
            {
                messagePart = messagePart[(endIndex + 1)..].TrimStart();
            }
        }

        return (time, level, messagePart.ToString());
    }

    private static string? TryStripPrefixedLevel(ref ReadOnlySpan<char> span)
    {
        if (span.Length < 3 || span[0] != '[')
        {
            return null;
        }

        var endIndex = span.IndexOf(']');
        if (endIndex <= 1)
        {
            return null;
        }

        if (!TryNormalizeSingBoxLevel(span[1..endIndex], out var level))
        {
            return null;
        }

        span = span[(endIndex + 1)..].TrimStart();
        return level;
    }

    private static string? TryStripInlineLevel(ref ReadOnlySpan<char> span)
    {
        if (span.Length == 0)
        {
            return null;
        }

        var matched = char.ToUpperInvariant(span[0]) switch
        {
            'F' when span.Length >= 5 && span.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase)
                => ("Fatal", 5),
            'E' when span.Length >= 5 && span.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
                => ("Error", 5),
            'W' when span.Length >= 7 && span.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase)
                => ("Warn", 7),
            'W' when span.Length >= 4 && span.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
                => ("Warn", 4),
            'D' when span.Length >= 5 && span.StartsWith("DEBUG", StringComparison.OrdinalIgnoreCase)
                => ("Debug", 5),
            'I' when span.Length >= 4 && span.StartsWith("INFO", StringComparison.OrdinalIgnoreCase)
                => ("Info", 4),
            _ => ((string Level, int Length)?)null
        };

        if (!matched.HasValue)
        {
            return null;
        }

        var (level, length) = matched.Value;
        span = span[length..];
        return level;
    }

    private static bool TryParseCompactSingBoxLog(
        ReadOnlySpan<char> span,
        string currentTime,
        string level,
        out (string Time, string Level, string Message) result)
    {
        span = span.TrimStart();
        if (span.Length == 0)
        {
            result = (currentTime, level, string.Empty);
            return true;
        }

        if (span[0] != '[')
        {
            result = default;
            return false;
        }

        var endIndex = span.IndexOf(']');
        if (endIndex <= 0)
        {
            result = default;
            return false;
        }

        var message = span[(endIndex + 1)..].TrimStart();
        result = (currentTime, level, message.ToString());
        return true;
    }

    private static string ExtractTime(ReadOnlySpan<char> span, int start, int length, string fallback)
    {
        if (length < 8)
        {
            return fallback;
        }

        var token = span.Slice(start, length);
        var tIndex = token.IndexOf('T');
        if (tIndex >= 0 && tIndex + 9 <= length)
        {
            return new string(token.Slice(tIndex + 1, 8));
        }

        var first8 = token[..8];
        if (first8.IndexOf('-') >= 0)
        {
            return fallback;
        }

        return new string(first8);
    }

    private static bool TryNormalizeSingBoxLevel(ReadOnlySpan<char> value, out string level)
    {
        if (value.Length == 0)
        {
            level = "Info";
            return false;
        }

        var normalized = char.ToUpperInvariant(value[0]) switch
        {
            'D' when value.Equals("DEBUG", StringComparison.OrdinalIgnoreCase) => "Debug",
            'W' when value.Equals("WARN", StringComparison.OrdinalIgnoreCase) ||
                      value.Equals("WARNING", StringComparison.OrdinalIgnoreCase) => "Warn",
            'E' when value.Equals("ERROR", StringComparison.OrdinalIgnoreCase) => "Error",
            'F' when value.Equals("FATAL", StringComparison.OrdinalIgnoreCase) => "Fatal",
            'I' when value.Equals("INFO", StringComparison.OrdinalIgnoreCase) => "Info",
            _ => null
        };

        level = normalized ?? "Info";
        return normalized != null;
    }

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
                    var endIndex = FindCsiTerminator(message, readIndex + 2);
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
