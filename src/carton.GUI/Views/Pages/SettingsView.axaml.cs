using Avalonia.Controls;
using carton.ViewModels;

namespace carton.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnUseSystemThemeAccentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SetThemeAccentMode(useSystemAccent: true);
        }
    }

    private void OnUseCustomThemeAccentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.SetThemeAccentMode(useSystemAccent: false);
        }
    }
}
