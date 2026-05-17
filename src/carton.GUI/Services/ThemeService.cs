using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
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

        UpdateCartonAccentBrush(app, ResolveCurrentAccentColor(app, normalizedAccent));
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
            return;
        }

        app.Resources["CartonAccentBrush"] = new SolidColorBrush(color);
    }

    private static Color ResolveCurrentAccentColor(Application app, string fallbackAccent)
    {
        if (app.Resources.TryGetResource("SystemAccentColor", null, out var accentResource))
        {
            return accentResource switch
            {
                Color color => color,
                SolidColorBrush brush => brush.Color,
                _ => Color.Parse(fallbackAccent)
            };
        }

        return Color.Parse(fallbackAccent);
    }

    private static string FormatColor(Color color)
        => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
