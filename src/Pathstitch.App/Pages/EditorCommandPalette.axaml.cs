using System;
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
                viewModel.CommandSearchQuery = string.Empty;
                _selectedIndex = -1;
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
    }

    private void ActivateSelected(EditorPageViewModel viewModel)
    {
        var results = viewModel.CommandSearchResults;
        if (_selectedIndex < 0 || _selectedIndex >= results.Count)
            _selectedIndex = results.Count > 0 ? 0 : -1;

        if (_selectedIndex >= 0)
            viewModel.ActivateCommandSearchItem(results[_selectedIndex].Identifier);

        _selectedIndex = -1;
    }

    private void OnCommandClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorPageViewModel viewModel
            || sender is not Button { Tag: string identifier })
        {
            return;
        }

        viewModel.ActivateCommandSearchItem(identifier);
        _selectedIndex = -1;
    }
}
