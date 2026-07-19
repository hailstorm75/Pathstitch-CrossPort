using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Domain.App.ViewModels;

namespace Pathstitch.App.Pages;

public partial class EditorCommandPalette : UserControl
{
    private int _selectedIndex = -1;

    public EditorCommandPalette() => InitializeComponent();

    public void FocusSearch()
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.OpenCommandSearch();
        CommandSearchInput.Focus();
        CommandSearchInput.SelectAll();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.CloseCommandSearch();
                ClearSelection(viewModel);
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(viewModel, 1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(viewModel, -1);
                e.Handled = true;
                break;

            case Key.Enter:
                ActivateSelected(viewModel);
                e.Handled = true;
                break;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel)
            return;

        _selectedIndex = -1;
        ClearSelection(viewModel);
    }

    private void MoveSelection(EditorPageViewModel viewModel, int direction)
    {
        var count = viewModel.CommandSearchResults.Count;
        if (count == 0)
        {
            _selectedIndex = -1;
            return;
        }

        _selectedIndex = _selectedIndex < 0
            ? direction > 0 ? 0 : count - 1
            : Math.Clamp(_selectedIndex + direction, 0, count - 1);
        ClearSelection(viewModel);
        viewModel.CommandSearchResults[_selectedIndex].IsCommandSearchSelected = true;
        CommandSearchResults.ScrollIntoView(_selectedIndex);
    }

    private async void ActivateSelected(EditorPageViewModel viewModel)
    {
        var results = viewModel.CommandSearchResults;
        if (_selectedIndex < 0 || _selectedIndex >= results.Count)
            _selectedIndex = results.Count > 0 ? 0 : -1;

        if (_selectedIndex >= 0)
            await viewModel.ActivateCommandSearchItemAsync(results[_selectedIndex].Identifier);

        ClearSelection(viewModel);
        _selectedIndex = -1;
    }

    private async void OnCommandClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button { Tag: string identifier })
        {
            return;
        }

        await viewModel.ActivateCommandSearchItemAsync(identifier);
        ClearSelection(viewModel);
        _selectedIndex = -1;
    }

    private void OnCommandPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button { DataContext: EditorSidebarToolItemViewModel item })
            return;

        var index = viewModel.CommandSearchResults
            .ToList()
            .FindIndex(candidate => ReferenceEquals(candidate, item));
        if (index < 0)
            return;

        _selectedIndex = index;
        ClearSelection(viewModel);
        item.IsCommandSearchSelected = true;
        CommandSearchResults.ScrollIntoView(index);
    }

    private static void ClearSelection(EditorPageViewModel viewModel)
    {
        foreach (var item in viewModel.CommandSearchResults.Where(item => item.IsCommandSearchSelected))
            item.IsCommandSearchSelected = false;
    }
}
