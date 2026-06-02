using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using carton.Core.Models;
using FluentAvalonia.Styling;

namespace carton.GUI.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    bool UseSystemThemeAccent { get; }
    string ThemeAccent { get; }
    event EventHandler<AppTheme>? ThemeChanged;
    void Initialize(AppTheme theme, bool useSystemThemeAccent, string themeAccent);
    void ApplyTheme(AppTheme theme);
    void ApplyAccent(bool useSystemThemeAccent, string themeAccent);
}

public sealed class ThemeService : IThemeService
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
    public static ThemeService Instance => _instance.Value;

    private bool _initialized;
    private int _accentUpdateVersion;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.System;
    public bool UseSystemThemeAccent { get; private set; }
    public string ThemeAccent { get; private set; } = "#FF0078D7";

    public event EventHandler<AppTheme>? ThemeChanged;

    private ThemeService()
    {
    }

    public void Initialize(AppTheme theme, bool useSystemThemeAccent, string themeAccent)
    {
        if (_initialized)
        {
            ApplyTheme(theme);
            ApplyAccent(useSystemThemeAccent, themeAccent);
            return;
        }

        ApplyTheme(theme);
        ApplyAccent(useSystemThemeAccent, themeAccent);
        _initialized = true;
    }

    public void ApplyTheme(AppTheme theme)
    {
        CurrentTheme = theme;

        var app = Application.Current ?? throw new InvalidOperationException("Application is not ready");
        app.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        ThemeChanged?.Invoke(this, theme);
    }

    public void ApplyAccent(bool useSystemThemeAccent, string themeAccent)
    {
        UseSystemThemeAccent = useSystemThemeAccent;

        var normalizedAccent = NormalizeAccent(themeAccent);
        ThemeAccent = normalizedAccent;

        var app = Application.Current ?? throw new InvalidOperationException("Application is not ready");
        var fluentTheme = app.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        var accentUpdateVersion = ++_accentUpdateVersion;
        if (fluentTheme != null)
        {
            if (useSystemThemeAccent)
            {
                fluentTheme.CustomAccentColor = null;
                fluentTheme.PreferUserAccentColor = true;
            }
            else
            {
                fluentTheme.CustomAccentColor = Color.Parse(normalizedAccent);
                fluentTheme.PreferUserAccentColor = false;
            }
        }

        if (useSystemThemeAccent)
        {
            // 跟随系统色:FluentAvalonia 设置 PreferUserAccentColor 后,SystemAccentColor
            // 资源要到下一渲染周期才刷新回真实系统色,此刻同步读取会拿到旧的自定义色。
            // 先用当前能拿到的值更新一次避免空窗,再在下一周期读到真实系统色后二次更新。
            UpdateCartonAccentBrush(app, ResolveCurrentAccentColor(app, fluentTheme, normalizedAccent));
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!UseSystemThemeAccent || accentUpdateVersion != _accentUpdateVersion)
                    {
                        return;
                    }

                    UpdateCartonAccentBrush(app, ResolveCurrentAccentColor(app, fluentTheme, normalizedAccent));
                },
                DispatcherPriority.Background);
        }
        else
        {
            // 自定义色:目标色已知,直接更新即可即时生效,无需读取尚未刷新的 SystemAccentColor。
            UpdateCartonAccentBrush(app, Color.Parse(normalizedAccent));
        }
    }

    private static string NormalizeAccent(string? themeAccent)
    {
        if (!string.IsNullOrWhiteSpace(themeAccent) && Color.TryParse(themeAccent, out var color))
        {
            return FormatColor(color);
        }

        return "#FF0078D7";
    }

    private static void UpdateCartonAccentBrush(Application app, Color color)
    {
        if (app.Resources.TryGetResource("CartonAccentBrush", null, out var resource) &&
            resource is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        else
        {
            app.Resources["CartonAccentBrush"] = new SolidColorBrush(color);
        }

        // 选中/展开态边框用弱化的强调色(70% alpha),需跟随当前强调色一起更新
        var borderColor = new Color(0xB3, color.R, color.G, color.B);
        if (app.Resources.TryGetResource("CartonAccentBorderBrush", null, out var borderResource) &&
            borderResource is SolidColorBrush borderBrush)
        {
            borderBrush.Color = borderColor;
        }
        else
        {
            app.Resources["CartonAccentBorderBrush"] = new SolidColorBrush(borderColor);
        }
    }

    private static Color ResolveCurrentAccentColor(Application app, FluentAvaloniaTheme? fluentTheme, string fallbackAccent)
    {
        // SystemAccentColor 由 FluentAvaloniaTheme 存放在它自身的资源里(app.Styles 中),
        // app.Resources.TryGetResource 只查 Resources 不查 Styles,因此要先从 FA 实例查,
        // 再退回 app.Resources,最后才用 fallback。
        var variant = app.ActualThemeVariant;
        if (fluentTheme is Avalonia.Controls.IResourceNode faNode &&
            faNode.TryGetResource("SystemAccentColor", variant, out var faResource) &&
            TryConvertColor(faResource, out var faColor))
        {
            return faColor;
        }

        if (app.Resources.TryGetResource("SystemAccentColor", variant, out var accentResource) &&
            TryConvertColor(accentResource, out var resColor))
        {
            return resColor;
        }

        return Color.Parse(fallbackAccent);
    }

    private static bool TryConvertColor(object? resource, out Color color)
    {
        switch (resource)
        {
            case Color c:
                color = c;
                return true;
            case SolidColorBrush b:
                color = b.Color;
                return true;
            default:
                color = default;
                return false;
        }
    }

    private static string FormatColor(Color color)
        => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
