using Avalonia.Controls;
using Avalonia.Interactivity;
using Domain.App.Models;
using Pathstitch.App.Services;

namespace Pathstitch.App.Dialogs;

public sealed partial class ModeIntroDialog : Window
{
    private readonly EditorMode _mode;
    private readonly UserPreferencesStore _preferencesStore;

    public ModeIntroDialog()
        : this(EditorMode.TwoD)
    {
    }

    public ModeIntroDialog(EditorMode mode, UserPreferencesStore? preferencesStore = null)
    {
        _mode = mode;
        _preferencesStore = preferencesStore ?? new UserPreferencesStore();
        InitializeComponent();

        var (title, bullets) = mode switch
        {
            EditorMode.TwoD => ("2D Design", new[]
            {
                "Draw and edit rectangles, circles, lines, text, and Bézier paths.",
                "Round corners, offset profiles, add sewing holes, and pattern geometry.",
                "Organize work with layers, then export clean DXF, SVG, or PNG output.",
            }),
            EditorMode.ThreeD => ("3D Import & Unfold", new[]
            {
                "Import one or more STEP, OBJ, or STL source models into one workspace.",
                "Use Move and Plane to position bodies and choose projection faces.",
                "Unfold the result, then send the flat pattern back to 2D for finishing.",
            }),
            _ => ("Batch", new[]
            {
                "Drop in many supported files to process them together.",
                "Apply shared operations and exports across a whole folder of parts.",
            }),
        };

        TitleText.Text = title;
        BulletList.ItemsSource = bullets;
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        if (DontShowAgain.IsChecked == true)
        {
            var preferences = _preferencesStore.Load();
            _preferencesStore.Save(_mode switch
            {
                EditorMode.TwoD => preferences with { TwoDIntroDismissed = true },
                EditorMode.ThreeD => preferences with { ThreeDIntroDismissed = true },
                _ => preferences with { BatchIntroDismissed = true },
            });
        }

        Close();
    }
}
