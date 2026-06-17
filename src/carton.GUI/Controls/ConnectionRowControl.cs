using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using carton.ViewModels;

namespace carton.GUI.Controls;

public sealed class ConnectionRowControl : Control
{
    private const double RowHeight = 90;
    private const double CardMarginX = 8;
    private const double CardMarginY = 4;
    private const double CardRadius = 12;
    private const double PaddingX = 14;
    private const double PaddingY = 9;
    private const double BadgePaddingX = 7;
    private const double BadgeHeight = 20;
    private const double BadgeGap = 8;
    private const double ColumnGap = 14;
    private const double LineGap = 17;

    private static readonly Typeface StrongTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);
    private static readonly Typeface MonoTypeface = new(new FontFamily("Cascadia Code,Consolas,monospace"));

    private static readonly SolidColorBrush LightCardBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush LightHoverCardBrush = new(Color.FromRgb(249, 249, 249));
    private static readonly SolidColorBrush LightBorderBrush = new(Color.FromArgb(0x16, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightTextHighBrush = new(Color.FromRgb(31, 31, 31));
    private static readonly SolidColorBrush LightTextMediumBrush = new(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightBadgeBrush = new(Color.FromArgb(0x14, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush LightGoodBadgeBrush = new(Color.FromRgb(232, 246, 237));
    private static readonly SolidColorBrush LightGoodTextBrush = new(Color.FromRgb(17, 124, 69));

    private static readonly SolidColorBrush DarkCardBrush = new(Color.FromRgb(43, 43, 43));
    private static readonly SolidColorBrush DarkHoverCardBrush = new(Color.FromRgb(50, 50, 50));
    private static readonly SolidColorBrush DarkBorderBrush = new(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkTextHighBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly SolidColorBrush DarkTextMediumBrush = new(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkBadgeBrush = new(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DarkGoodBadgeBrush = new(Color.FromRgb(24, 62, 42));
    private static readonly SolidColorBrush DarkGoodTextBrush = new(Color.FromRgb(101, 214, 154));

    private readonly TextSlot _protocolText = new();
    private readonly TextSlot _destinationText = new();
    private readonly TextSlot _statusText = new();
    private readonly TextSlot _uploadLabelText = new();
    private readonly TextSlot _uploadValueText = new();
    private readonly TextSlot _downloadLabelText = new();
    private readonly TextSlot _downloadValueText = new();
    private readonly TextSlot _inboundText = new();
    private readonly TextSlot _routeText = new();
    private ConnectionItemViewModel? _connection;

    public ConnectionRowControl()
    {
        MinHeight = RowHeight;
        ClipToBounds = true;
        Focusable = false;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredWidth = double.IsInfinity(availableSize.Width) ? 520 : availableSize.Width;
        return new Size(desiredWidth, RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
        => new(finalSize.Width, Math.Max(RowHeight, finalSize.Height));

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        SetConnection(DataContext as ConnectionItemViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetConnection(DataContext as ConnectionItemViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClearConnection();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var connection = _connection;
        var width = Bounds.Width;
        var height = Math.Max(RowHeight, Bounds.Height);
        if (connection is null || width <= 0 || height <= 0)
        {
            return;
        }

        var card = new Rect(CardMarginX, CardMarginY, Math.Max(0, width - CardMarginX * 2), Math.Max(0, height - CardMarginY * 2));
        DrawCard(context, card, connection.IsSelected);
        var contentRect = card.Deflate(new Thickness(PaddingX, PaddingY));
        using (context.PushClip(contentRect))
        {
            DrawContent(context, contentRect, connection);
        }
    }

    private void DrawCard(DrawingContext context, Rect rect, bool selected)
    {
        var background = IsPointerOver
                ? GetBrush("CartonSettingsItemPointerOverBackgroundBrush", IsLightTheme ? LightHoverCardBrush : DarkHoverCardBrush)
                : GetBrush("CartonSettingsItemBackgroundBrush", IsLightTheme ? LightCardBrush : DarkCardBrush);
        var borderBrush = selected
            ? GetBrush("CartonAccentBorderBrush", GetBrush("CartonAccentBrush", LightTextHighBrush))
            : GetBrush("CartonSettingsItemBorderBrush", IsLightTheme ? LightBorderBrush : DarkBorderBrush);

        context.DrawRectangle(background, new Pen(borderBrush, selected ? 1.2 : 1), rect, CardRadius, CardRadius);
    }

    private void DrawContent(DrawingContext context, Rect rect, ConnectionItemViewModel connection)
    {
        var textHigh = GetBrush("CartonControlForegroundBaseHighBrush", IsLightTheme ? LightTextHighBrush : DarkTextHighBrush);
        var textMedium = GetBrush("CartonControlForegroundBaseMediumBrush", IsLightTheme ? LightTextMediumBrush : DarkTextMediumBrush);
        var badgeBrush = GetBrush("CartonControlBackgroundBaseLowBrush", IsLightTheme ? LightBadgeBrush : DarkBadgeBrush);
        var goodBadgeBrush = IsLightTheme ? LightGoodBadgeBrush : DarkGoodBadgeBrush;
        var goodTextBrush = IsLightTheme ? LightGoodTextBrush : DarkGoodTextBrush;

        if (rect.Width < 80)
        {
            return;
        }

        var topY = rect.Y;
        var secondY = topY + BadgeHeight + 8;
        var thirdY = secondY + LineGap;

        var networkMaxWidth = Clamp(rect.Width * 0.22, 42, 86);
        var statusText = connection.IsClosed
            ? GetStringResource("Connections.Status.Closed", connection.Status)
            : GetStringResource("Connections.Status.Active", connection.Status);
        var showStatus = rect.Width >= 240 && !string.IsNullOrWhiteSpace(statusText);
        var statusWidth = showStatus
            ? MeasureBadgeWidth(statusText, _statusText, StrongTypeface, 11, goodTextBrush, 72)
            : 0;

        var protocolRight = DrawBadge(context, connection.NetworkDisplay, _protocolText, rect.X, topY, badgeBrush, textHigh, networkMaxWidth);
        var destinationRight = showStatus ? rect.Right - statusWidth - ColumnGap : rect.Right;
        var destinationWidth = Math.Max(0, destinationRight - protocolRight);
        DrawLayout(context, _destinationText.Get(connection.Destination, StrongTypeface, 12.5, textHigh, destinationWidth), protocolRight, topY + 1);

        if (showStatus)
        {
            DrawBadge(context, statusText, _statusText, rect.Right - statusWidth, topY, goodBadgeBrush, goodTextBrush, statusWidth);
        }

        var rightColumnWidth = rect.Width >= 260 ? Clamp(rect.Width * 0.38, 96, 260) : 0;
        var metricWidth = rightColumnWidth > 0
            ? Math.Max(0, rect.Width - rightColumnWidth - ColumnGap)
            : rect.Width;

        DrawMetric(
            context,
            GetStringResource("Connections.Metric.Up", "UP"),
            connection.UploadTotal,
            _uploadLabelText,
            _uploadValueText,
            rect.X,
            secondY,
            metricWidth,
            textMedium);
        DrawMetric(
            context,
            GetStringResource("Connections.Metric.Down", "DOWN"),
            connection.DownloadTotal,
            _downloadLabelText,
            _downloadValueText,
            rect.X,
            thirdY,
            metricWidth,
            textMedium);

        if (rightColumnWidth > 0)
        {
            var rightX = rect.Right - rightColumnWidth;
            DrawLayout(context, _inboundText.Get(connection.InboundSummary, MonoTypeface, 12, textMedium, rightColumnWidth, TextAlignment.Right), rightX, secondY);
            DrawLayout(context, _routeText.Get(connection.Route, MonoTypeface, 12, textMedium, rightColumnWidth, TextAlignment.Right), rightX, thirdY);
        }
    }

    private double DrawBadge(
        DrawingContext context,
        string text,
        TextSlot slot,
        double x,
        double y,
        IBrush background,
        IBrush foreground,
        double maxWidth)
    {
        text = NormalizeBadgeText(text);

        var layout = slot.Get(text, StrongTypeface, 11, foreground, Math.Max(0, maxWidth - BadgePaddingX * 2));
        var width = MeasureBadgeWidth(text, slot, StrongTypeface, 11, foreground, maxWidth);
        var rect = new Rect(x, y, width, BadgeHeight);
        context.DrawRectangle(background, null, rect, 4, 4);
        layout.Draw(context, new Point(x + BadgePaddingX, y + Math.Max(0, (BadgeHeight - layout.Height) / 2)));
        return x + width + BadgeGap;
    }

    private static double MeasureBadgeWidth(
        string text,
        TextSlot slot,
        Typeface typeface,
        double fontSize,
        IBrush foreground,
        double maxWidth)
    {
        var layout = slot.Get(text, typeface, fontSize, foreground, Math.Max(0, maxWidth - BadgePaddingX * 2));
        return Math.Min(maxWidth, Math.Max(34, layout.Width + BadgePaddingX * 2));
    }

    private static void DrawLayout(DrawingContext context, TextLayout layout, double x, double y)
        => layout.Draw(context, new Point(x, y));

    private static void DrawMetric(
        DrawingContext context,
        string label,
        string value,
        TextSlot labelSlot,
        TextSlot valueSlot,
        double x,
        double y,
        double maxWidth,
        IBrush foreground)
    {
        if (string.IsNullOrWhiteSpace(value) || maxWidth <= 0)
        {
            return;
        }

        var labelLayout = labelSlot.Get(label, MonoTypeface, 12, foreground, maxWidth);
        labelLayout.Draw(context, new Point(x, y));

        var valueX = x + labelLayout.Width + 4;
        var valueWidth = Math.Max(0, maxWidth - labelLayout.Width - 4);
        if (valueWidth <= 0)
        {
            return;
        }

        valueSlot.Get(value, MonoTypeface, 12, foreground, valueWidth).Draw(context, new Point(valueX, y));
    }


    private static string NormalizeBadgeText(string? text)
        => string.IsNullOrWhiteSpace(text) ? "-" : text;

    private static double Clamp(double value, double min, double max)
        => Math.Min(max, Math.Max(min, value));

    private void SetConnection(ConnectionItemViewModel? connection)
    {
        if (ReferenceEquals(_connection, connection))
        {
            return;
        }

        if (_connection != null)
        {
            _connection.PropertyChanged -= OnConnectionPropertyChanged;
        }

        _connection = connection;

        if (_connection != null)
        {
            _connection.PropertyChanged += OnConnectionPropertyChanged;
        }

        ClearTextSlots();
        InvalidateVisual();
    }

    private void ClearConnection()
    {
        if (_connection != null)
        {
            _connection.PropertyChanged -= OnConnectionPropertyChanged;
            _connection = null;
        }

        ClearTextSlots();
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ClearTextSlots();
        InvalidateVisual();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ClearTextSlots();
        InvalidateVisual();
    }

    private void ClearTextSlots()
    {
        _protocolText.Clear();
        _destinationText.Clear();
        _statusText.Clear();
        _uploadLabelText.Clear();
        _uploadValueText.Clear();
        _downloadLabelText.Clear();
        _downloadValueText.Clear();
        _inboundText.Clear();
        _routeText.Clear();
    }

    private bool IsLightTheme => ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;

    private IBrush GetBrush(string key, IBrush fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush
            ? brush
            : fallback;

    private string GetStringResource(string key, string fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is string value
            ? value
            : fallback;

    private sealed class TextSlot
    {
        private string? _text;
        private Typeface _typeface;
        private double _fontSize;
        private double _maxWidth;
        private TextAlignment _alignment;
        private IBrush? _brush;
        private TextLayout? _layout;

        public TextLayout Get(
            string? text,
            Typeface typeface,
            double fontSize,
            IBrush brush,
            double maxWidth,
            TextAlignment alignment = TextAlignment.Left)
        {
            maxWidth = Math.Max(0, maxWidth);
            text ??= string.Empty;

            if (_layout != null &&
                _text == text &&
                _typeface == typeface &&
                Math.Abs(_fontSize - fontSize) < 0.001 &&
                Math.Abs(_maxWidth - maxWidth) < 0.5 &&
                _alignment == alignment &&
                ReferenceEquals(_brush, brush))
            {
                return _layout;
            }

            _text = text;
            _typeface = typeface;
            _fontSize = fontSize;
            _maxWidth = maxWidth;
            _alignment = alignment;
            _brush = brush;
            var oldLayout = _layout;
            _layout = null;
            oldLayout?.Dispose();
            _layout = new TextLayout(
                text,
                typeface,
                fontSize,
                brush,
                alignment,
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
