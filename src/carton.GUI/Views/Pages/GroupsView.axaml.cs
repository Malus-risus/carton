using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using carton.ViewModels;

namespace carton.Views.Pages;

public partial class GroupsView : UserControl
{
    private const double ProxyCardMinWidth = 200;
    private const double ProxyCardGap = 8;
    private const double ProxyCardHorizontalReserve = 16;
    private const int ProxyCardMinColumns = 2;

    public GroupsView()
    {
        InitializeComponent();
    }

    private void OnGroupItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GroupItemViewModel group } ||
            DataContext is not GroupsViewModel viewModel)
        {
            return;
        }

        if (e.Source is Visual sourceVisual && HasToggleExclusionAncestor(sourceVisual, sender as Visual))
        {
            return;
        }

        if (!viewModel.ToggleGroupExpansionCommand.CanExecute(group))
        {
            return;
        }

        viewModel.ToggleGroupExpansionCommand.Execute(group);
        e.Handled = true;
    }

    private static bool HasToggleExclusionAncestor(Visual sourceVisual, Visual? groupRoot)
    {
        for (Visual? current = sourceVisual; current != null && !ReferenceEquals(current, groupRoot); current = current.GetVisualParent() as Visual)
        {
            if (current is Button || current is Control { DataContext: OutboundItemViewModel })
            {
                return true;
            }
        }

        return false;
    }

    private void OnGroupTestDelayPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GroupItemViewModel group } ||
            DataContext is not GroupsViewModel viewModel)
        {
            return;
        }

        if (viewModel.TestGroupCardCommand.CanExecute(group))
        {
            viewModel.TestGroupCardCommand.Execute(group);
        }

        e.Handled = true;
    }

    private void OnExpandedProxiesListSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Control { DataContext: GroupItemViewModel group })
        {
            return;
        }

        var width = e.NewSize.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            return;
        }

        var availableWidth = Math.Max(0, width - ProxyCardHorizontalReserve);

        var columns = Math.Max(
            ProxyCardMinColumns,
            (int)Math.Floor((availableWidth + ProxyCardGap) / (ProxyCardMinWidth + ProxyCardGap)));

        group.SetExpandedProxyColumns(columns);
    }

}
