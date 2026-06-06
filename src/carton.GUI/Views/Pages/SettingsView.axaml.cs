using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using carton.ViewModels;
using System;

namespace carton.Views.Pages;

public partial class SettingsView : UserControl
{
    private const double SectionAnchorOffset = 12;
    private const double BottomScrollTolerance = 1;

    private bool _suppressScrollSelectionSync;

    public SettingsView()
    {
        InitializeComponent();
        SettingsTabStrip.Tapped += OnSettingsTabStripTapped;
        SettingsScrollViewer.ScrollChanged += OnSettingsScrollViewerScrollChanged;
    }

    private void OnSettingsTabStripTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (SettingsTabStrip.SelectedItem is TabStripItem item && item.Tag is string controlName)
        {
            var target = this.FindControl<Control>(controlName);
            if (target is null || SettingsScrollViewer.Content is not Control scrollContent)
                return;

            var offset = target.TranslatePoint(new Point(0, 0), scrollContent);
            if (offset.HasValue)
            {
                // Coerce to the real scrollable range; Avalonia clamps Offset to this, so
                // comparing against the raw target would mispredict whether ScrollChanged fires.
                var maxOffsetY = Math.Max(0, SettingsScrollViewer.Extent.Height - SettingsScrollViewer.Viewport.Height);
                var targetY = Math.Min(Math.Max(0, offset.Value.Y), maxOffsetY);

                // Only arm suppression when the offset will actually change; otherwise no
                // ScrollChanged fires to consume it and the flag would stick, wrongly
                // swallowing the next real user scroll.
                _suppressScrollSelectionSync =
                    Math.Abs(targetY - SettingsScrollViewer.Offset.Y) > 0.5;

                SettingsScrollViewer.Offset = new Vector(SettingsScrollViewer.Offset.X, targetY);
                SettingsTabStrip.SelectedItem = item;
            }
        }
    }

    private void OnSettingsScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_suppressScrollSelectionSync)
        {
            // Consume exactly the one ScrollChanged raised by the programmatic Offset set
            // in OnSettingsTabStripTapped, then resume syncing on the next (user) scroll.
            _suppressScrollSelectionSync = false;
            return;
        }

        if (sender is not ScrollViewer scrollViewer)
            return;

        if (scrollViewer.Content is not StackPanel panel)
            return;

        if (scrollViewer.Viewport.Height <= 0)
        {
            return;
        }

        var isScrolledToBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >=
                                 scrollViewer.Extent.Height - BottomScrollTolerance;
        Control? activeSection = null;
        Control? firstVisibleSection = null;
        Control? lastVisibleSection = null;

        foreach (var child in panel.Children)
        {
            if (child is not Control section || section.Bounds.Height <= 0)
            {
                continue;
            }

            var point = section.TranslatePoint(new Point(0, 0), scrollViewer);
            if (!point.HasValue)
            {
                continue;
            }

            var top = point.Value.Y;
            var bottom = top + section.Bounds.Height;
            if (bottom > 0 && top < scrollViewer.Viewport.Height)
            {
                firstVisibleSection ??= section;
                lastVisibleSection = section;
            }

            if (!isScrolledToBottom && top <= SectionAnchorOffset)
            {
                activeSection = section;
            }
        }

        activeSection = isScrolledToBottom
            ? lastVisibleSection
            : activeSection ?? firstVisibleSection;

        TabStripItem? matchingItem = null;
        foreach (var item in SettingsTabStrip.Items)
        {
            if (item is TabStripItem tabStripItem &&
                tabStripItem.Tag is string tag &&
                tag == activeSection?.Name)
            {
                matchingItem = tabStripItem;
                break;
            }
        }

        if (matchingItem != null && SettingsTabStrip.SelectedItem != matchingItem)
        {
            SettingsTabStrip.SelectedItem = matchingItem;
        }
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
