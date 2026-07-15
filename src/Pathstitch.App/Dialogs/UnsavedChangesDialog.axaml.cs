using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.Services;

namespace Pathstitch.App.Dialogs;

public sealed partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog()
    {
        InitializeComponent();
        Closing += (_, _) => Result ??= UnsavedChangesPromptResult.Cancel;
    }

    public UnsavedChangesPromptResult? Result { get; private set; }

    public void SetDocumentName(string documentName)
    {
        var displayName = string.IsNullOrWhiteSpace(documentName) ? "this document" : $"\u201c{documentName}\u201d";
        DocumentMessage.Text = $"Changes to {displayName} will be lost if you don't save them.";
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e) => Complete(UnsavedChangesPromptResult.Save);

    private void OnDiscardClicked(object? sender, RoutedEventArgs e) => Complete(UnsavedChangesPromptResult.Discard);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Complete(UnsavedChangesPromptResult.Cancel);

    private void Complete(UnsavedChangesPromptResult result)
    {
        Result = result;
        Close(result);
    }
}
