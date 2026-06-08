using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace carton.GUI.Helpers;

public class EmojiTextHelper
{
    private static readonly FontFamily EmojiFont = new("avares://carton/Assets/Fonts#Twemoji COLRv0");

    // Match common emojis, symbols, and flag sequences.
    // - Flag sequences: pairs of regional indicators (U+1F1E6-U+1F1FF)
    // - \uD83C range: skip Enclosed Alphanumeric Supplement (U+1F100-U+1F16F = \uDD00-\uDD6F)
    //   which contains text symbols like 🄻 that should NOT be rendered with the emoji font.
    // - \uD83D-\uD83E: standard emoji ranges (Emoticons, Supplemental Symbols, etc.)
    // - BMP symbols: Miscellaneous Symbols, Dingbats, etc.
    private static readonly Regex EmojiRegex = new Regex(
        @"(\uD83C[\uDDE6-\uDDFF]){2}|" +              // flag sequences
        @"\uD83C[\uDC00-\uDCFF\uDD70-\uDDE5\uDE00-\uDFFF]|" + // \uD83C, excluding enclosed alphanumerics (\uDD00-\uDD6F)
        @"[\uD83D-\uD83E][\uDC00-\uDFFF]|" +          // \uD83D-\uD83E full range
        @"[\u2600-\u27BF]\uFE0F?",                     // BMP symbols
        RegexOptions.Compiled);

    // Remembers the last full text rendered into each TextBlock so recycled list
    // items that are re-assigned identical content skip the inline rebuild.
    private static readonly ConditionalWeakTable<TextBlock, string> LastRenderedText = new();

    public static readonly AttachedProperty<string> TextProperty =
        AvaloniaProperty.RegisterAttached<EmojiTextHelper, TextBlock, string>("Text");

    public static readonly AttachedProperty<string> PrefixProperty =
        AvaloniaProperty.RegisterAttached<EmojiTextHelper, TextBlock, string>("Prefix");

    static EmojiTextHelper()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>(OnTextChanged);
        PrefixProperty.Changed.AddClassHandler<TextBlock>(OnTextChanged);
    }

    public static string GetText(AvaloniaObject element) => element.GetValue(TextProperty);
    public static void SetText(AvaloniaObject element, string value) => element.SetValue(TextProperty, value);

    public static string GetPrefix(AvaloniaObject element) => element.GetValue(PrefixProperty);
    public static void SetPrefix(AvaloniaObject element, string value) => element.SetValue(PrefixProperty, value);

    private static void OnTextChanged(TextBlock textBlock, AvaloniaPropertyChangedEventArgs e)
    {
        var text = GetText(textBlock);
        var prefix = GetPrefix(textBlock);

        var fullText = string.IsNullOrEmpty(prefix)
            ? text
            : string.Concat(prefix, text);

        fullText ??= string.Empty;

        // Recycled list items frequently re-assign the same text. Skip the whole
        // rebuild (and the emoji scan) when the rendered content is unchanged.
        if (LastRenderedText.TryGetValue(textBlock, out var previous) &&
            string.Equals(previous, fullText, StringComparison.Ordinal))
        {
            return;
        }

        LastRenderedText.AddOrUpdate(textBlock, fullText);

        // Clear the plain Text value before rebuilding content so Text and Inlines
        // do not render together during prefix/text update races.
        textBlock.Text = string.Empty;

        if (textBlock.Inlines != null)
        {
            textBlock.Inlines.Clear();
        }
        else
        {
            textBlock.Inlines = new InlineCollection();
        }

        if (string.IsNullOrEmpty(fullText))
        {
            textBlock.Text = string.Empty;
            return;
        }

        // Fast path: if no character can even begin an emoji match, render as plain
        // text without paying for the compiled regex or any inline allocations.
        // This covers virtually all ASCII log/connection text.
        if (!ContainsEmojiCandidate(fullText))
        {
            textBlock.Text = fullText;
            return;
        }

        var matches = EmojiRegex.Matches(fullText);
        if (matches.Count == 0)
        {
            textBlock.Text = fullText;
            return;
        }

        int lastIndex = 0;
        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                textBlock.Inlines.Add(new Run { Text = fullText.Substring(lastIndex, match.Index - lastIndex) });
            }

            // Force FontWeight.Normal for emojis to preserve color
            textBlock.Inlines.Add(new Run
            {
                Text = match.Value,
                FontFamily = EmojiFont,
                FontWeight = FontWeight.Normal,
                FontStyle = FontStyle.Normal
            });

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < fullText.Length)
        {
            textBlock.Inlines.Add(new Run { Text = fullText.Substring(lastIndex) });
        }
    }

    /// <summary>
    /// Cheap pre-filter that returns true only if <paramref name="text"/> contains a
    /// character that could start an <see cref="EmojiRegex"/> match: a BMP symbol in
    /// U+2600..U+27BF, or any high surrogate (the lead of a U+1Fxxx emoji/flag).
    /// It is a strict superset of the regex, so a false result guarantees no match —
    /// letting plain ASCII/CJK text skip the compiled regex entirely.
    /// </summary>
    private static bool ContainsEmojiCandidate(string text)
    {
        var span = text.AsSpan();

        // SIMD-accelerated scan: a BMP symbol in U+2600..U+27BF, or any high
        // surrogate (the lead of a U+1Fxxx emoji / regional-indicator flag).
        return span.ContainsAnyInRange('☀', '➿')
            || span.ContainsAnyInRange('\uD800', '\uDBFF');
    }
}
