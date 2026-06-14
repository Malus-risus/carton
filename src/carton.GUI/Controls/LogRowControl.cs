using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using carton.ViewModels;

namespace carton.GUI.Controls;

public sealed class LogRowControl : Control
{
    private const double RowHeight = 28;
    private const double LeftPadding = 11;
    private const double TimeColumnWidth = 72;
    private const double SourceColumnWidth = 68;
    private const double LevelColumnWidth = 55;
    private const double ColumnRightGap = 6;

    private static readonly FontFamily TimeFontFamily = new("Cascadia Code,Consolas,monospace");
    private static readonly Typeface TimeTypeface = new(TimeFontFamily);
    private static readonly Typeface NormalTypeface = new(FontFamily.Default);
    private static readonly Typeface LevelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);

    private static readonly SolidColorBrush LightTextMediumBrush = new(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush DarkTextMediumBrush = new(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush FatalBrush = new(Color.FromRgb(181, 28, 41));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(231, 72, 86));
    private static readonly SolidColorBrush WarnBrush = new(Color.FromRgb(249, 168, 37));
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush DebugBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush DefaultLevelBrush = new(Color.FromArgb(80, 128, 128, 128));

    private readonly TextSlot _timeText = new();
    private readonly TextSlot _sourceText = new();
    private readonly TextSlot _levelText = new();
    private readonly TextSlot _messageText = new();
    private LogEntryViewModel? _entry;
    private IBrush _levelBrush = DefaultLevelBrush;

    public LogRowControl()
    {
        MinHeight = RowHeight;
        ClipToBounds = true;
        Focusable = false;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredWidth = double.IsInfinity(availableSize.Width)
            ? LeftPadding + TimeColumnWidth + SourceColumnWidth + LevelColumnWidth
            : availableSize.Width;
        return new Size(desiredWidth, RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return new Size(finalSize.Width, Math.Max(RowHeight, finalSize.Height));
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        SetEntry(DataContext as LogEntryViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetEntry(DataContext as LogEntryViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearEntry();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var width = Bounds.Width;
        var height = Math.Max(RowHeight, Bounds.Height);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        DrawText(context, width, height);
    }

    private void DrawText(DrawingContext context, double width, double height)
    {
        var entry = _entry;
        if (entry is null)
        {
            return;
        }

        var textBrush = GetBrush("CartonControlForegroundBaseMediumBrush", GetTextFallbackBrush());

        var x = LeftPadding;
        DrawLayout(context, _timeText.Get(entry.Time, TimeTypeface, 12, textBrush, TimeColumnWidth - ColumnRightGap), x, height);

        x += TimeColumnWidth;
        DrawLayout(context, _sourceText.Get(entry.SourceDisplayName, NormalTypeface, 12, textBrush, SourceColumnWidth - ColumnRightGap), x, height);

        x += SourceColumnWidth;
        DrawLayout(context, _levelText.Get(entry.Level, LevelTypeface, 11, _levelBrush, LevelColumnWidth - ColumnRightGap), x, height);

        x += LevelColumnWidth;
        var messageWidth = Math.Max(0, width - x);
        if (messageWidth > 0)
        {
            DrawLayout(context, _messageText.Get(entry.Message, NormalTypeface, 12, GetBrush("CartonControlForegroundBaseHighBrush", textBrush), messageWidth), x, height);
        }
    }

    private void SetEntry(LogEntryViewModel? entry)
    {
        if (ReferenceEquals(_entry, entry))
        {
            return;
        }

        _entry = entry;
        _levelBrush = ResolveLevelBrush(entry?.Level);
        ClearTextSlots();
        InvalidateVisual();
    }

    private void ClearEntry()
    {
        _entry = null;
        _levelBrush = DefaultLevelBrush;
        ClearTextSlots();
    }

    private void ClearTextSlots()
    {
        _timeText.Clear();
        _sourceText.Clear();
        _levelText.Clear();
        _messageText.Clear();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ClearTextSlots();
        InvalidateVisual();
    }

    private static void DrawLayout(DrawingContext context, TextLayout layout, double x, double height)
    {
        var y = Math.Max(0, (height - layout.Height) / 2);
        layout.Draw(context, new Point(x, y));
    }

    private IBrush GetBrush(string key, IBrush fallback)
    {
        return this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : fallback;
    }

    private IBrush GetTextFallbackBrush()
        => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? LightTextMediumBrush : DarkTextMediumBrush;

    private static IBrush ResolveLevelBrush(string? level)
    {
        return level switch
        {
            "Fatal" or "fatal" or "FATAL" or "Panic" or "panic" or "PANIC" => FatalBrush,
            "Error" or "error" or "ERROR" => ErrorBrush,
            "Warn" or "warn" or "WARN" or "Warning" or "warning" or "WARNING" => WarnBrush,
            "Info" or "info" or "INFO" => InfoBrush,
            "Debug" or "debug" or "DEBUG" or "Trace" or "trace" or "TRACE" => DebugBrush,
            null or "" => DefaultLevelBrush,
            _ => DefaultLevelBrush
        };
    }

    private sealed class TextSlot
    {
        private string? _text;
        private Typeface _typeface;
        private double _fontSize;
        private double _maxWidth;
        private IBrush? _brush;
        private TextLayout? _layout;

        public TextLayout Get(string? text, Typeface typeface, double fontSize, IBrush brush, double maxWidth)
        {
            maxWidth = Math.Max(0, maxWidth);
            text ??= string.Empty;

            if (_layout != null &&
                _text == text &&
                _typeface == typeface &&
                Math.Abs(_fontSize - fontSize) < 0.001 &&
                Math.Abs(_maxWidth - maxWidth) < 0.5 &&
                ReferenceEquals(_brush, brush))
            {
                return _layout;
            }

            _text = text;
            _typeface = typeface;
            _fontSize = fontSize;
            _maxWidth = maxWidth;
            _brush = brush;
            var oldLayout = _layout;
            _layout = null;
            oldLayout?.Dispose();
            _layout = new TextLayout(
                text,
                typeface,
                fontSize,
                brush,
                TextAlignment.Left,
                TextWrapping.NoWrap,
                TextTrimming.CharacterEllipsis,
                null,
                FlowDirection.LeftToRight,
                maxWidth,
                RowHeight,
                double.NaN,
                0,
                1);
            return _layout;
        }

        public void Clear()
        {
            _text = null;
            _brush = null;
            var oldLayout = _layout;
            _layout = null;
            oldLayout?.Dispose();
        }
    }
}
