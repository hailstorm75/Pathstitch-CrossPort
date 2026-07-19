namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private bool _learnModeEnabled = true;

    public bool LearnModeEnabled
    {
        get => _learnModeEnabled;
        set
        {
            if (!SetProperty(ref _learnModeEnabled, value))
                return;
            OnPropertyChanged(nameof(LearnModeToggleHelpText));
            MarkDocumentDirty();
            NotifyCommandPaletteStateChanged();
        }
    }

    public string LearnModeToggleHelpText => LearnModeEnabled
        ? "Contextual tool guidance is visible"
        : "Contextual tool guidance is hidden";

    public void ToggleLearnMode() => LearnModeEnabled = !LearnModeEnabled;
}
