using Avalonia;
using Avalonia.Controls;
using System;
using System.ComponentModel;
using carton.ViewModels;

namespace carton.Views.Pages;

public partial class ConnectionsView : UserControl
{
    private ConnectionsViewModel? _subscribedViewModel;

    public ConnectionsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        UnsubscribeViewModel();

        base.OnDataContextChanged(e);

        SubscribeViewModel(DataContext as ConnectionsViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeViewModel(DataContext as ConnectionsViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeViewModel(ConnectionsViewModel? viewModel)
    {
        if (viewModel == null)
        {
            return;
        }

        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _subscribedViewModel = viewModel;
    }

    private void UnsubscribeViewModel()
    {
        if (_subscribedViewModel == null)
        {
            return;
        }

        _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedViewModel = null;
    }

    private void OnConnectionsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ConnectionsViewModel viewModel)
        {
            return;
        }

        for (var i = 0; i < e.AddedItems.Count; i++)
        {
            if (e.AddedItems[i] is ConnectionItemViewModel connection)
            {
                viewModel.SelectedConnection = connection;
                return;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionsViewModel.SelectedConnection) &&
            DataContext is ConnectionsViewModel { SelectedConnection: null })
        {
            ConnectionsListBox.SelectedItem = null;
        }
    }
}
