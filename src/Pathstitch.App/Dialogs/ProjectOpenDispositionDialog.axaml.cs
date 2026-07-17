using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.Services;

namespace Pathstitch.App.Dialogs;

public sealed partial class ProjectOpenDispositionDialog : Window
{
    public ProjectOpenDispositionDialog()
    {
        InitializeComponent();
        Closing += (_, _) => Result ??= ProjectOpenDisposition.Cancel;
    }

    public ProjectOpenDisposition? Result { get; private set; }

    public void SetProjectName(string projectName)
    {
        var displayName = string.IsNullOrWhiteSpace(projectName) ? "the selected project" : $"“{projectName}”";
        ProjectMessage.Text = $"Combine {displayName} with this project, or open it in a new window?";
    }

    private void OnCombineClicked(object? sender, RoutedEventArgs e) => Complete(ProjectOpenDisposition.Combine);

    private void OnNewWindowClicked(object? sender, RoutedEventArgs e) => Complete(ProjectOpenDisposition.NewWindow);

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Complete(ProjectOpenDisposition.Cancel);

    private void Complete(ProjectOpenDisposition result)
    {
        Result = result;
        Close(result);
    }
}
