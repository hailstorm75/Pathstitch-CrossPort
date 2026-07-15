using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public int? SelectedBodyIndex
    {
        get => _threeDWorkspace.SelectedBodyIndex;
        private set
        {
            if (!_threeDWorkspace.SetSelectedBodyIndex(value))
                return;

            OnPropertyChanged();

            OnPropertyChanged(nameof(SelectedBodySummary));
            OnPropertyChanged(nameof(HasSelectedBody));
            OnPropertyChanged(nameof(CanEditSelectedBodyOffset));
            OnPropertyChanged(nameof(CanEditSelectedBodyOffsetValues));
            OnPropertyChanged(nameof(CanNudgeSelectedBody));
            OnPropertyChanged(nameof(MoveBodiesHint));
            OnPropertyChanged(nameof(SelectedBodyOffsetSummary));
            OnPropertyChanged(nameof(SelectedBodyOffsetMagnitudeText));
            OnPropertyChanged(nameof(CanResetSelectedBodyPosition));
            OnPropertyChanged(nameof(SelectedBodyOffsetInputHint));
            OnPropertyChanged(nameof(MovedBodyOffsets));
            SyncSelectedBodyOffsetText();
            SyncBodySelectionState();
        }
    }

    public bool HasSelectedBody => SelectedBodyIndex is not null;

    public bool IsSelectedBodyOffsetXTextValid
    {
        get => _isSelectedBodyOffsetXTextValid;
        private set
        {
            if (!SetWorkspaceFacadeValue(_isSelectedBodyOffsetXTextValid, value, updated => _isSelectedBodyOffsetXTextValid = updated))
                return;

            OnPropertyChanged(nameof(AreSelectedBodyOffsetInputsValid));
            OnPropertyChanged(nameof(CanNudgeSelectedBody));
            OnPropertyChanged(nameof(CanResetSelectedBodyPosition));
            OnPropertyChanged(nameof(SelectedBodyOffsetInputHint));
        }
    }

    public bool IsSelectedBodyOffsetYTextValid
    {
        get => _isSelectedBodyOffsetYTextValid;
        private set
        {
            if (!SetWorkspaceFacadeValue(_isSelectedBodyOffsetYTextValid, value, updated => _isSelectedBodyOffsetYTextValid = updated))
                return;

            OnPropertyChanged(nameof(AreSelectedBodyOffsetInputsValid));
            OnPropertyChanged(nameof(CanNudgeSelectedBody));
            OnPropertyChanged(nameof(CanResetSelectedBodyPosition));
            OnPropertyChanged(nameof(SelectedBodyOffsetInputHint));
        }
    }

    public bool IsSelectedBodyOffsetZTextValid
    {
        get => _isSelectedBodyOffsetZTextValid;
        private set
        {
            if (!SetWorkspaceFacadeValue(_isSelectedBodyOffsetZTextValid, value, updated => _isSelectedBodyOffsetZTextValid = updated))
                return;

            OnPropertyChanged(nameof(AreSelectedBodyOffsetInputsValid));
            OnPropertyChanged(nameof(CanNudgeSelectedBody));
            OnPropertyChanged(nameof(CanResetSelectedBodyPosition));
            OnPropertyChanged(nameof(SelectedBodyOffsetInputHint));
        }
    }

    public string SelectedBodySummary
        => SelectedBodyIndex is int bodyIndex
            ? Bodies.FirstOrDefault(x => x.BodyIndex == bodyIndex)?.Name ?? $"Body {bodyIndex}"
            : "Select a body from the list or viewport.";

    public bool CanEditSelectedBodyOffsetValues => HasSelectedBody;

    public bool AreSelectedBodyOffsetInputsValid
        => IsSelectedBodyOffsetXTextValid
           && IsSelectedBodyOffsetYTextValid
           && IsSelectedBodyOffsetZTextValid;

    public string SelectedBodyOffsetXText
    {
        get => _selectedBodyOffsetXText;
        set => SetBodyOffsetText(_selectedBodyOffsetXText, value, 0, updated => _selectedBodyOffsetXText = updated, nameof(SelectedBodyOffsetXText));
    }

    public string SelectedBodyOffsetYText
    {
        get => _selectedBodyOffsetYText;
        set => SetBodyOffsetText(_selectedBodyOffsetYText, value, 1, updated => _selectedBodyOffsetYText = updated, nameof(SelectedBodyOffsetYText));
    }

    public string SelectedBodyOffsetZText
    {
        get => _selectedBodyOffsetZText;
        set => SetBodyOffsetText(_selectedBodyOffsetZText, value, 2, updated => _selectedBodyOffsetZText = updated, nameof(SelectedBodyOffsetZText));
    }

    public string BodyMoveStepText
    {
        get => _bodyMoveStepText;
        set
        {
            if (!SetWorkspaceFacadeValue(_bodyMoveStepText, value, updated => _bodyMoveStepText = updated))
                return;

            OnPropertyChanged(nameof(IsBodyMoveStepValid));
            OnPropertyChanged(nameof(CanNudgeSelectedBody));
            OnPropertyChanged(nameof(BodyMoveStepSummary));
        }
    }

    public IReadOnlyList<BodyOffset3D> BodyOffsets
    {
        get => _threeDWorkspace.BodyOffsets;
        private set
        {
            if (!_threeDWorkspace.SetBodyOffsets(value))
                return;

            OnPropertyChanged();

            OnPropertyChanged(nameof(BodyOffsetsSummary));
            OnPropertyChanged(nameof(HasAnyBodyOffsets));
            OnPropertyChanged(nameof(CanResetAllBodyPositions));
            OnPropertyChanged(nameof(SelectedBodyOffsetSummary));
            OnPropertyChanged(nameof(SelectedBodyOffsetMagnitudeText));
            OnPropertyChanged(nameof(CanResetSelectedBodyPosition));
            OnPropertyChanged(nameof(MovedBodyOffsets));
        }
    }

    public int BodyOffsetCount
    {
        get => _bodyOffsetCount;
        private set
        {
            if (!SetWorkspaceFacadeValue(_bodyOffsetCount, value, updated => _bodyOffsetCount = updated))
                return;

            OnPropertyChanged(nameof(BodyOffsetsSummary));
        }
    }

    public string BodyOffsetsSummary => BodyOffsetCount == 0
        ? "No saved offsets"
        : $"{BodyOffsetCount} body offset(s) captured";

    public bool HasAnyBodyOffsets => BodyOffsets.Count > 0;

    public bool CanResetAllBodyPositions => HasAnyBodyOffsets;

    public bool CanUndoThreeDBodyMove => _threeDBodyMoveUndo.Count > 0;

    public bool CanRedoThreeDBodyMove => _threeDBodyMoveRedo.Count > 0;

    public IReadOnlyList<MovedBodyOffsetSummary> MovedBodyOffsets
        => BodyOffsets
            .Select(offset =>
            {
                var body = Bodies.FirstOrDefault(candidate => candidate.BodyIndex == offset.BodyIndex);
                return new MovedBodyOffsetSummary(
                    offset.BodyIndex,
                    body?.Name ?? $"Body {offset.BodyIndex + 1}",
                    offset.X,
                    offset.Y,
                    offset.Z,
                    SelectedBodyIndex == offset.BodyIndex);
            })
            .OrderBy(summary => summary.BodyIndex)
            .ToArray();

    public bool CanResetSelectedBodyPosition
        => HasSelectedBody
           && (!AreSelectedBodyOffsetInputsValid
               || (TryGetSelectedBodyOffset(out var offset)
                   && !IsZeroOffset(offset[0], offset[1], offset[2])));

    public string SelectedBodyOffsetSummary
        => !HasSelectedBody
            ? "Position: Select a body to inspect its exact offset."
            : TryGetSelectedBodyOffset(out var offset)
            ? $"Position: X {FormatOffset(offset[0])} / Y {FormatOffset(offset[1])} / Z {FormatOffset(offset[2])}"
            : "Position: Home";

    public string SelectedBodyOffsetMagnitudeText
        => !HasSelectedBody
            ? "Distance from home: --"
            : TryGetSelectedBodyOffset(out var offset)
            ? $"Distance from home: {Math.Sqrt((offset[0] * offset[0]) + (offset[1] * offset[1]) + (offset[2] * offset[2])):0.###} mm"
            : "Distance from home: 0 mm";

    public bool IsBodyMoveStepValid => TryParseBodyMoveStep(out _, out _);

    public bool CanNudgeSelectedBody
        => HasSelectedBody
           && AreSelectedBodyOffsetInputsValid
           && IsBodyMoveStepValid;

    public string BodyMoveStepSummary
    {
        get
        {
            if (!TryParseBodyMoveStep(out var step, out var usedDefaultFallback))
                return "Enter a numeric step to enable precise nudging.";

            return usedDefaultFallback
                ? "Step of 0 defaults to 1 mm for nudge actions."
                : $"Each nudge moves the selected body by {step:0.###} mm.";
        }
    }

    public string SelectedBodyOffsetInputHint => !HasSelectedBody
        ? "Select a body before editing exact XYZ offsets."
        : !AreSelectedBodyOffsetInputsValid
            ? "Enter valid numeric X, Y, and Z values to update the selected body's exact offset."
            : "Exact XYZ edits apply immediately to the selected body.";

    public string MoveBodiesHint => IsMoveToolActive
        ? HasSelectedBody
            ? "Adjust the selected body's exact offset or use the gizmo in the viewport."
            : "Select a body from the list or click one in the viewport."
        : "Enable Move to select bodies and move them with the viewport gizmo.";

    public void SelectBodyFromPanel(int bodyIndex)
    {
        if (!IsMoveToolActive)
            ActivateMoveTool();

        SelectedBodyIndex = bodyIndex;
        StatusText = $"Body selected: {SelectedBodySummary}";
        RequestBodyMoveStateSync();
    }

    public void ClearSelectedBody()
    {
        if (!HasSelectedBody)
            return;

        SelectedBodyIndex = null;
        StatusText = "Body selection cleared";
        RequestBodyMoveStateSync();
    }

    public void ToggleBodyVisibility(int bodyIndex)
    {
        Bodies = Bodies.Select(body => body.BodyIndex == bodyIndex
                ? body with { Visible = !body.Visible }
                : body)
            .ToArray();

        var updated = Bodies.FirstOrDefault(x => x.BodyIndex == bodyIndex);
        if (updated is null)
            return;

        RequestViewportScript(BuildSetBodyVisibilityScript(bodyIndex, updated.Visible));

        var prunedHiddenBodyState = PruneHiddenBodyActiveState();
        RefreshSelectionState();
        SyncBodySelectionState();
        RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));
        RequestPlaneSelectionStateSync();
        RequestBodyMoveStateSync();
        Request3DStatePersistence();

        StatusText = updated.Visible
            ? $"Body shown: {updated.Name}"
            : prunedHiddenBodyState
                ? $"Body hidden: {updated.Name}. Active editor selections were cleared from the hidden body."
                : $"Body hidden: {updated.Name}";
    }

    public void NudgeSelectedBody(int axis, int direction)
    {
        if (!CanNudgeSelectedBody || !TryGetSelectedBodyOffset(out var offset))
            return;

        offset[axis] += direction * GetBodyMoveStep();
        UpdateSelectedBodyOffset(offset[0], offset[1], offset[2]);
    }

    public void ResetSelectedBodyPosition()
    {
        if (!HasSelectedBody)
            return;

        UpdateSelectedBodyOffset(0.0, 0.0, 0.0);
    }

    public void ResetAllBodyPositions()
    {
        if (!HasAnyBodyOffsets)
            return;

        CaptureBodyMoveUndo();
        BodyOffsets = [];
        BodyOffsetCount = 0;
        SyncSelectedBodyOffsetText();
        RequestBodyMoveStateSync();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        StatusText = "All moved bodies returned to their home positions";
    }

    public void SelectMovedBody(int bodyIndex) => SelectBodyFromPanel(bodyIndex);

    public void ResetBodyPosition(int bodyIndex)
    {
        if (!BodyOffsets.Any(offset => offset.BodyIndex == bodyIndex))
            return;

        CaptureBodyMoveUndo();
        BodyOffsets = BodyOffsets
            .Where(offset => offset.BodyIndex != bodyIndex)
            .OrderBy(offset => offset.BodyIndex)
            .ToArray();

        BodyOffsetCount = BodyOffsets.Count;
        if (SelectedBodyIndex == bodyIndex)
            SyncSelectedBodyOffsetText();

        RequestBodyMoveStateSync();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));

        var bodyName = Bodies.FirstOrDefault(body => body.BodyIndex == bodyIndex)?.Name ?? $"Body {bodyIndex + 1}";
        StatusText = $"Body reset: {bodyName}";
    }

    public bool UndoThreeDBodyMove()
    {
        if (_threeDBodyMoveUndo.Count == 0)
            return false;

        _threeDBodyMoveRedo.Push(BodyOffsets.ToArray());
        ApplyBodyMoveHistory(_threeDBodyMoveUndo.Pop(), "Undid 3D body move");
        return true;
    }

    public bool RedoThreeDBodyMove()
    {
        if (_threeDBodyMoveRedo.Count == 0)
            return false;

        _threeDBodyMoveUndo.Push(BodyOffsets.ToArray());
        ApplyBodyMoveHistory(_threeDBodyMoveRedo.Pop(), "Redid 3D body move");
        return true;
    }

    private void SetBodyOffsetText(string current, string value, int axis, Action<string> assign, string propertyName)
    {
        if (string.Equals(current, value, StringComparison.Ordinal))
            return;
        assign(value);
        OnPropertyChanged(propertyName);

        if (_isUpdatingBodyOffsetText || !HasSelectedBody)
            return;

        if (!TryParseBodyOffsetText(value, out var parsed))
        {
            SetBodyOffsetTextValidity(axis, false);
            return;
        }

        SetBodyOffsetTextValidity(axis, true);

        if (!TryGetSelectedBodyOffset(out var offset))
            return;

        offset[axis] = parsed;
        UpdateSelectedBodyOffset(offset[0], offset[1], offset[2], syncText: false);
    }

    private void UpdateSelectedBodyOffset(double x, double y, double z, bool syncText = true)
    {
        if (SelectedBodyIndex is not int bodyIndex)
            return;

        if (TryGetSelectedBodyOffset(out var previous)
            && Math.Abs(previous[0] - x) < 1e-9
            && Math.Abs(previous[1] - y) < 1e-9
            && Math.Abs(previous[2] - z) < 1e-9)
            return;

        CaptureBodyMoveUndo();

        var normalizedOffsets = BodyOffsets
            .Where(existingOffset => existingOffset.BodyIndex != bodyIndex)
            .ToList();

        if (!IsZeroOffset(x, y, z))
            normalizedOffsets.Add(new BodyOffset3D(bodyIndex, x, y, z));

        BodyOffsets = normalizedOffsets
            .OrderBy(existingOffset => existingOffset.BodyIndex)
            .ToArray();

        BodyOffsetCount = BodyOffsets.Count;
        StatusText = IsZeroOffset(x, y, z)
            ? $"Body reset: {SelectedBodySummary}"
            : $"Body moved: {SelectedBodySummary}";
        RequestBodyMoveStateSync();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(350));

        if (syncText)
            SyncSelectedBodyOffsetText();
    }

    private void CaptureBodyMoveUndo()
    {
        if (_isApplyingBodyMoveHistory)
            return;

        _threeDBodyMoveUndo.Push(BodyOffsets.ToArray());
        _threeDBodyMoveRedo.Clear();
        OnPropertyChanged(nameof(CanUndoThreeDBodyMove));
        OnPropertyChanged(nameof(CanRedoThreeDBodyMove));
        UndoThreeDBodyMoveActionCommand.NotifyCanExecuteChanged();
        RedoThreeDBodyMoveActionCommand.NotifyCanExecuteChanged();
    }

    private void ClearBodyMoveHistory()
    {
        _threeDBodyMoveUndo.Clear();
        _threeDBodyMoveRedo.Clear();
        OnPropertyChanged(nameof(CanUndoThreeDBodyMove));
        OnPropertyChanged(nameof(CanRedoThreeDBodyMove));
        UndoThreeDBodyMoveActionCommand.NotifyCanExecuteChanged();
        RedoThreeDBodyMoveActionCommand.NotifyCanExecuteChanged();
    }

    private void ApplyBodyMoveHistory(IReadOnlyList<BodyOffset3D> offsets, string status)
    {
        _isApplyingBodyMoveHistory = true;
        try
        {
            BodyOffsets = offsets.ToArray();
            BodyOffsetCount = BodyOffsets.Count;
            SyncSelectedBodyOffsetText();
        }
        finally
        {
            _isApplyingBodyMoveHistory = false;
        }

        RequestBodyMoveStateSync();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        StatusText = status;
        OnPropertyChanged(nameof(CanUndoThreeDBodyMove));
        OnPropertyChanged(nameof(CanRedoThreeDBodyMove));
        UndoThreeDBodyMoveActionCommand.NotifyCanExecuteChanged();
        RedoThreeDBodyMoveActionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndoThreeDBodyMove))]
    private void UndoThreeDBodyMoveAction() => UndoThreeDBodyMove();

    [RelayCommand(CanExecute = nameof(CanRedoThreeDBodyMove))]
    private void RedoThreeDBodyMoveAction() => RedoThreeDBodyMove();

    private bool TryGetSelectedBodyOffset(out double[] offset)
    {
        offset = [0.0, 0.0, 0.0];
        if (SelectedBodyIndex is not int bodyIndex)
            return false;

        var existing = BodyOffsets.FirstOrDefault(existingOffset => existingOffset.BodyIndex == bodyIndex);
        if (existing is not null)
            offset = [existing.X, existing.Y, existing.Z];

        return true;
    }

    private double GetBodyMoveStep()
    {
        if (TryParseBodyMoveStep(out var step, out _))
            return step;

        return 1.0;
    }

    private bool TryParseBodyMoveStep(out double step, out bool usedDefaultFallback)
    {
        usedDefaultFallback = false;
        if (double.TryParse(BodyMoveStepText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.TryParse(BodyMoveStepText, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            if (Math.Abs(parsed) < 1e-9)
            {
                usedDefaultFallback = true;
                step = 1.0;
                return true;
            }

            step = parsed;
            return true;
        }

        step = 0.0;
        return false;
    }

    private void SyncSelectedBodyOffsetText()
    {
        _isUpdatingBodyOffsetText = true;
        try
        {
            IsSelectedBodyOffsetXTextValid = true;
            IsSelectedBodyOffsetYTextValid = true;
            IsSelectedBodyOffsetZTextValid = true;

            if (TryGetSelectedBodyOffset(out var offset))
            {
                SelectedBodyOffsetXText = offset[0].ToString("0.###", CultureInfo.InvariantCulture);
                SelectedBodyOffsetYText = offset[1].ToString("0.###", CultureInfo.InvariantCulture);
                SelectedBodyOffsetZText = offset[2].ToString("0.###", CultureInfo.InvariantCulture);
            }
            else
            {
                SelectedBodyOffsetXText = "0";
                SelectedBodyOffsetYText = "0";
                SelectedBodyOffsetZText = "0";
            }
        }
        finally
        {
            _isUpdatingBodyOffsetText = false;
        }
    }

    private void ApplyBodyMove(EditorViewportMessage message)
    {
        if (message.BodyIndex is null || message.X is null || message.Y is null || message.Z is null)
            return;

        SelectedBodyIndex = message.BodyIndex;
        UpdateSelectedBodyOffset(message.X.Value, message.Y.Value, message.Z.Value);
    }

    private void RequestBodyMoveStateSync() => RequestViewportScript(BuildSetBodyMoveStateScript());

    private static bool IsZeroOffset(double x, double y, double z)
        => Math.Abs(x) < 1e-9
           && Math.Abs(y) < 1e-9
           && Math.Abs(z) < 1e-9;

    private static string FormatOffset(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryParseBodyOffsetText(string rawValue, out double value)
    {
        value = 0.0;
        return !string.IsNullOrWhiteSpace(rawValue)
               && (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out value));
    }

    private void SetBodyOffsetTextValidity(int axis, bool isValid)
    {
        switch (axis)
        {
            case 0:
                IsSelectedBodyOffsetXTextValid = isValid;
                break;
            case 1:
                IsSelectedBodyOffsetYTextValid = isValid;
                break;
            case 2:
                IsSelectedBodyOffsetZTextValid = isValid;
                break;
        }
    }

    private void SyncBodySelectionState()
    {
        if (Bodies.Count == 0)
            return;

        var selectedBodyIndex = SelectedBodyIndex;
        Bodies = Bodies
            .Select(body => body with { IsSelected = selectedBodyIndex == body.BodyIndex })
            .ToArray();
    }

    private bool PruneHiddenBodyActiveState()
    {
        var changed = false;

        if (SelectedBodyIndex is int selectedBodyIndex && !IsBodyVisible(selectedBodyIndex))
        {
            SelectedBodyIndex = null;
            changed = true;
        }

        var visibleSelections = SelectedFaces
            .Where(selection => IsBodyVisible(selection.BodyIndex))
            .ToArray();
        if (!SelectedFaces.SequenceEqual(visibleSelections))
        {
            SelectedFaces = visibleSelections;
            changed = true;
        }

        if (SelectedProjectionBodyIndex is int projectionBodyIndex && !IsBodyVisible(projectionBodyIndex))
        {
            if (PlaneSelectionModeType == Domain.App.Models.PlaneSelectionModeType.Face)
                SelectedProjectionPlane = null;

            SelectedProjectionFaceIndex = null;
            SelectedProjectionBodyIndex = null;
            UpdateProjectionSummary();
            changed = true;
        }

        return changed;
    }

    private bool IsBodyVisible(int bodyIndex)
        => Bodies.Any(body => body.BodyIndex == bodyIndex && body.Visible);
}
