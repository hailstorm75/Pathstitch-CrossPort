using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class EditorActivityLogTray : UserControl
{
    private EditorPageViewModel? _viewModel;

    public EditorActivityLogTray()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as EditorPageViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ScrollToLatestActivity();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorPageViewModel.ActivityLog) or nameof(EditorPageViewModel.IsActivityLogExpanded))
            ScrollToLatestActivity();
    }

    private void ScrollToLatestActivity()
        => Dispatcher.UIThread.Post(ActivityScrollViewer.ScrollToEnd, DispatcherPriority.Background);
}
