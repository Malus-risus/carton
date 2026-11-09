using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using carton.Core.Models;

namespace carton.GUI.Services;

/// <summary>Owned dialogs share the app material; subscriptions live only as long as ShowDialog.</summary>
public static class OwnedDialogAppearance
{
    private static readonly IBrush LightFallback = new SolidColorBrush(Color.FromRgb(243, 243, 243));
    private static readonly IBrush DarkFallback = new SolidColorBrush(Color.FromRgb(32, 32, 32));

    public static Task ShowAsync(Window dialog, Window owner) => ShowAsync<object?>(dialog, owner);

    public static async Task<T> ShowAsync<T>(Window dialog, Window owner)
    {
        var applying = false;
        var state = WindowMaterialService.Instance;
        void Apply()
        {
            if (applying) return;
            applying = true;
            try
            {
                dialog.RequestedThemeVariant = owner.ActualThemeVariant;
                var requested = state.Material == WindowMaterial.Mica
                    ? WindowTransparencyLevel.Mica : WindowTransparencyLevel.None;
                if (dialog.TransparencyLevelHint.Count == 0 || dialog.TransparencyLevelHint[0] != requested)
                    dialog.TransparencyLevelHint = requested == WindowTransparencyLevel.None
                        ? new[] { requested } : new[] { requested, WindowTransparencyLevel.None };
                var fallback = dialog.ActualThemeVariant == ThemeVariant.Dark ? DarkFallback : LightFallback;
                dialog.TransparencyBackgroundFallback = fallback;
                dialog.Background = requested == WindowTransparencyLevel.Mica && dialog.ActualTransparencyLevel == requested
                    ? Brushes.Transparent : fallback;
                // A child window's fallback must not overwrite the main-window status in settings.
            }
            finally { applying = false; }
        }
        void MaterialChanged(object? sender, EventArgs e) => Apply();
        void PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TopLevel.ActualTransparencyLevelProperty || e.Property == TopLevel.ActualThemeVariantProperty)
                Apply();
        }
        state.Changed += MaterialChanged;
        owner.PropertyChanged += PropertyChanged;
        dialog.PropertyChanged += PropertyChanged;
        dialog.Opened += MaterialChanged;
        try
        {
            Apply();
            return await dialog.ShowDialog<T>(owner);
        }
        finally
        {
            state.Changed -= MaterialChanged;
            owner.PropertyChanged -= PropertyChanged;
            dialog.PropertyChanged -= PropertyChanged;
            dialog.Opened -= MaterialChanged;
        }
    }
}
