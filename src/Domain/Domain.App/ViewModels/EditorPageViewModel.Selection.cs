using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public IReadOnlyList<SelectedFace3D> SelectedFaces
    {
        get => _selectedFaces;
        private set
        {
            if (!SetProperty(ref _selectedFaces, value))
                return;

            OnPropertyChanged(nameof(CanUnfoldSelected));
            OnPropertyChanged(nameof(UnfoldHintText));
            OnPropertyChanged(nameof(CanInspectFaceDistortion));
            OnPropertyChanged(nameof(FaceDistortionInspectorHint));
            OnPropertyChanged(nameof(FaceDistortionStatusText));
            OnPropertyChanged(nameof(CanUseNativeSeparateFlattenSelected));
            OnPropertyChanged(nameof(SeparateFlattenSelectionSummary));
            OnPropertyChanged(nameof(UnfoldSelectedExecutionSummary));
            OnPropertyChanged(nameof(HasFaceSelection));
            OnPropertyChanged(nameof(SelectedFacesQueueSummary));
            OnPropertyChanged(nameof(CanUseCurrentSelectionAsProjectionFace));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionLabel));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionHint));
            OnPropertyChanged(nameof(CanRefreshFaceDistortion));
            OnPropertyChanged(nameof(FaceDistortionSurfaceBehaviorSummary));
            OnPropertyChanged(nameof(FaceDistortionModeSummary));
            OnPropertyChanged(nameof(CanRefreshActiveUnfoldPreview));
            OnPropertyChanged(nameof(UnfoldPreviewScopeSummary));
        }
    }

    public IReadOnlyList<SelectedFaceDetails> SelectedFaceDetails
    {
        get => _selectedFaceDetails;
        private set
        {
            if (!SetProperty(ref _selectedFaceDetails, value))
                return;

            OnPropertyChanged(nameof(MeasuredFaceTitle));
            OnPropertyChanged(nameof(MeasuredFaceSubtitle));
            OnPropertyChanged(nameof(CanUseNativeSeparateFlattenSelected));
            OnPropertyChanged(nameof(SeparateFlattenSelectionSummary));
            OnPropertyChanged(nameof(UnfoldSelectedExecutionSummary));
            OnPropertyChanged(nameof(SelectedFacesQueueSummary));
            OnPropertyChanged(nameof(CanUseCurrentSelectionAsProjectionFace));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionLabel));
            OnPropertyChanged(nameof(ProjectionFaceSelectionActionHint));
            OnPropertyChanged(nameof(FaceDistortionSurfaceBehaviorSummary));
            OnPropertyChanged(nameof(UnfoldPreviewScopeSummary));
        }
    }

    public bool HasFaceSelection => SelectedFaces.Count > 0;

    public string SelectedFacesQueueSummary => SelectedFaceDetails.Count switch
    {
        0 => "No selection",
        1 => "1 face in queue",
        _ => $"{SelectedFaceDetails.Count} faces in queue",
    };

    public void SelectFaceFromPanel(int bodyIndex, int faceIndex)
        => SelectFaceFromPanel(bodyIndex, faceIndex, isShiftKey: false);

    public void SelectFaceFromPanel(int bodyIndex, int faceIndex, bool isShiftKey)
        => ApplyFaceSelection(new SelectedFace3D(bodyIndex, faceIndex), isShiftKey, syncViewport: true);

    public void ClearSelectedFaces()
    {
        if (!HasFaceSelection)
            return;

        SelectedFaces = [];
        RefreshSelectionState();
        RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));
        StatusText = "Selection cleared";
    }

    public void RemoveSelectedFaceFromQueue(int bodyIndex, int faceIndex)
    {
        var selection = new SelectedFace3D(bodyIndex, faceIndex);
        if (!SelectedFaces.Contains(selection))
            return;

        SelectedFaces = SelectedFaces
            .Where(x => x != selection)
            .ToArray();

        RefreshSelectionState();
        RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));
        StatusText = HasFaceSelection
            ? $"Removed B{bodyIndex + 1} : F{faceIndex} from the selection queue"
            : "Selection cleared";
    }

    private void ApplyViewportSelection(EditorViewportMessage message)
    {
        if (message.BodyIndex is null || message.FaceIndex is null)
            return;

        ApplyFaceSelection(
            new SelectedFace3D(message.BodyIndex.Value, message.FaceIndex.Value),
            message.IsShiftKey,
            syncViewport: false);
    }

    private void RefreshSelectionState()
    {
        SelectedFaceCount = SelectedFaces.Count;
        SyncBodyFaceSelectionState();

        if (SelectedFaces.Count == 0)
        {
            SelectionSummary = "No selection";
            SelectedFaceDetails = [];
            SyncProjectionFaceSelectionFromCurrentSelection();

            RequestDistortionRefresh();
            RequestLiveRecompute();
            return;
        }

        var details = SelectedFaces
            .Select(selection =>
            {
                var body = Bodies.FirstOrDefault(x => x.BodyIndex == selection.BodyIndex);
                var face = body?.Faces.FirstOrDefault(x => x.FaceIndex == selection.FaceIndex);
                return body is null || face is null
                    ? null
                    : new SelectedFaceDetails(selection.BodyIndex, body.Name, selection.FaceIndex, face.Type, face.Area);
            })
            .Where(x => x is not null)
            .Cast<SelectedFaceDetails>()
            .ToArray();

        SelectedFaceDetails = details;
        SelectionSummary = details.Length switch
        {
            0 => "No selection",
            1 => $"B{details[0].BodyIndex + 1} : F{details[0].FaceIndex}",
            _ => $"{details.Length} faces selected",
        };

        SyncProjectionFaceSelectionFromCurrentSelection();
        RequestDistortionRefresh(TimeSpan.FromMilliseconds(150));
        RequestLiveRecompute();
    }

    private void ApplyFaceSelection(SelectedFace3D selection, bool isShiftKey, bool syncViewport)
    {
        if (IsMeasureToolActive)
        {
            SelectedFaces = [selection];
            RefreshSelectionState();
            if (SelectedFaceDetails.Count == 1)
                StatusText = $"Measuring {SelectionSummary}";
            if (syncViewport)
                RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));
            return;
        }

        if (isShiftKey)
        {
            if (SelectedFaces.Contains(selection))
                SelectedFaces = SelectedFaces.Where(x => x != selection).ToArray();
            else
                SelectedFaces = SelectedFaces.Concat([selection]).ToArray();
        }
        else
        {
            SelectedFaces = [selection];
        }

        RefreshSelectionState();
        if (syncViewport)
            RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));
    }

    private bool NormalizeSelectionForActiveTool(bool syncViewport)
    {
        if (!IsMeasureToolActive || SelectedFaces.Count <= 1)
            return false;

        SelectedFaces = [SelectedFaces[^1]];
        RefreshSelectionState();
        if (syncViewport)
            RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));

        return true;
    }

    private void SyncBodyFaceSelectionState()
    {
        if (Bodies.Count == 0)
            return;

        var selectedFaces = SelectedFaces.ToHashSet();
        Bodies = Bodies
            .Select(body => body with
            {
                Faces = body.Faces
                    .Select(face => face with
                    {
                        BodyIndex = body.BodyIndex,
                        IsSelected = selectedFaces.Contains(new SelectedFace3D(body.BodyIndex, face.FaceIndex)),
                    })
                    .ToArray(),
            })
            .ToArray();
    }
}
