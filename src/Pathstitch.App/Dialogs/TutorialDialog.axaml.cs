using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Dialogs;

public sealed partial class TutorialDialog : Window
{
    private static readonly (string Title, string Body)[] Steps =
    [
        ("Welcome to Pathstitch", "Pathstitch turns drawings and 3D models into clean patterns ready to cut, score, or stitch."),
        ("1 · Draw a rectangle", "Choose Rectangle from the left tool rail and drag on the canvas. Shapes stay editable so you can refine exact dimensions later."),
        ("2 · Add and edit geometry", "Use Circle, Line, Text, or Pen to build a design. Scale, fillet, chamfer, and offset tools refine the result."),
        ("3 · Organize with layers", "The Layers panel groups geometry for cut, score, and engrave workflows. Select a layer to select its geometry."),
        ("4 · Export your pattern", "Export DXF, SVG, or PNG from the output controls. You can export the whole document or only the current selection."),
        ("You're ready!", "Draw, edit, organize, and export. You can replay this walkthrough anytime from Preferences.")
    ];

    private readonly UserPreferencesStore _preferencesStore;
    private int _step;

    public TutorialDialog()
        : this(new UserPreferencesStore())
    {
    }

    public TutorialDialog(UserPreferencesStore preferencesStore)
    {
        _preferencesStore = preferencesStore;
        InitializeComponent();
        RenderStep();
    }

    private void RenderStep()
    {
        var current = Steps[_step];
        StepTitle.Text = current.Title;
        StepBody.Text = current.Body;
        ProgressText.Text = $"Step {_step + 1} of {Steps.Length}";
        BackButton.IsEnabled = _step > 0;
        NextButton.Content = _step == Steps.Length - 1 ? "Done" : "Next";
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        if (_step <= 0)
            return;
        _step--;
        RenderStep();
    }

    private void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        if (_step < Steps.Length - 1)
        {
            _step++;
            RenderStep();
            return;
        }

        Complete();
    }

    private void OnSkipClicked(object? sender, RoutedEventArgs e) => Complete();

    private void Complete()
    {
        _preferencesStore.Save(_preferencesStore.Load() with { TutorialCompleted = true });
        Close();
    }
}
