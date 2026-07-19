using CommunityToolkit.Mvvm.Input;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool CanUndo => ActiveEditorMode switch
    {
        EditorMode.TwoD => CanUndoTwoDWorkspace,
        EditorMode.ThreeD => CanUndoThreeDBodyMove,
        _ => false,
    };

    public bool CanRedo => ActiveEditorMode switch
    {
        EditorMode.TwoD => CanRedoTwoDWorkspace,
        EditorMode.ThreeD => CanRedoThreeDBodyMove,
        _ => false,
    };

    public bool CanDelete
        => ActiveEditorMode == EditorMode.TwoD
           && (HasTwoDSelection || HasTwoDSelectedMeasurement);

    public bool CanExportDxf => ActiveEditorMode == EditorMode.TwoD && CanExportTwoDDxf;
    public bool CanExportSvg => ActiveEditorMode == EditorMode.TwoD && CanExportTwoDSvg;
    public bool CanExportPng => ActiveEditorMode == EditorMode.TwoD && CanExportTwoDPng;
    public bool CanExportPdf => ActiveEditorMode == EditorMode.TwoD && CanExportTwoDPdf;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (ActiveEditorMode == EditorMode.TwoD)
            UndoTwoDWorkspace();
        else if (ActiveEditorMode == EditorMode.ThreeD)
            UndoThreeDBodyMove();
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (ActiveEditorMode == EditorMode.TwoD)
            RedoTwoDWorkspace();
        else if (ActiveEditorMode == EditorMode.ThreeD)
            RedoThreeDBodyMove();
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete() => DeleteTwoDSelection();

    [RelayCommand(CanExecute = nameof(CanExportDxf))]
    private Task ExportDxf(CancellationToken cancellationToken) => ExportTwoDDxfAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanExportSvg))]
    private Task ExportSvg(CancellationToken cancellationToken) => ExportTwoDSvgAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanExportPng))]
    private Task ExportPng(CancellationToken cancellationToken) => ExportTwoDPngAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanExportPdf))]
    private Task ExportPdf(CancellationToken cancellationToken) => ExportTwoDPdfAsync(cancellationToken);

    private void NotifyMenuCommands()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CanExportDxf));
        OnPropertyChanged(nameof(CanExportSvg));
        OnPropertyChanged(nameof(CanExportPng));
        OnPropertyChanged(nameof(CanExportPdf));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ExportDxfCommand.NotifyCanExecuteChanged();
        ExportSvgCommand.NotifyCanExecuteChanged();
        ExportPngCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
        NotifyCommandPaletteStateChanged();
    }
}
