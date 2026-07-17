using Domain.App.Models;
using System.Globalization;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private readonly HashSet<string> _expandedTwoDFolderIds = new(StringComparer.Ordinal);

    public IReadOnlyList<Editor2DLayer> TwoDLayers => _twoDWorkspace.Layers;

    public IReadOnlyList<Editor2DLayerFolder> TwoDFolders => _twoDWorkspace.Folders;

    public IReadOnlyList<Editor2DLayerHierarchyItem> TwoDLayerHierarchyItems
        => BuildTwoDLayerHierarchyItems();

    public string? TwoDActiveLayerId => _twoDWorkspace.ActiveLayerId;

    public bool TwoDReferenceImageTransformEditActive => _twoDWorkspace.IsReferenceImageTransformEditActive;

    public IReadOnlyList<Editor2DReferenceImage> TwoDReferenceImages => TwoDLayers
        .Where(layer => layer.IsVisible && layer.ReferenceImage is not null)
        .Select(layer => layer.ReferenceImage!)
        .ToArray();

    public Editor2DReferenceImage? TwoDActiveReferenceImage
        => _twoDWorkspace.ActiveLayer?.ReferenceImage;

    public bool HasTwoDActiveReferenceImage => TwoDActiveReferenceImage is not null;

    public bool TwoDActiveReferenceImageLocked
        => _twoDWorkspace.ActiveLayer?.IsLocked == true;

    public bool HasTwoDReferenceTraceSession => _twoDWorkspace.HasReferenceImageTraceSession;

    public bool IsTwoDReferenceTraceRunning => _twoDWorkspace.IsReferenceImageTracePreviewPending;

    public bool ShowTwoDReferenceCalibration
        => HasTwoDActiveReferenceImage && !HasTwoDReferenceTraceSession;

    public bool CanCommitTwoDReferenceTrace
        => !IsTwoDReferenceTraceRunning && _twoDWorkspace.ReferenceImageTracePreviewPaths.Count > 0;

    public string TwoDReferenceTraceSummary
        => IsTwoDReferenceTraceRunning
            ? "Tracing image…"
            : CanCommitTwoDReferenceTrace
            ? $"{_twoDWorkspace.ReferenceImageTracePreviewPaths.Count} cyan contour(s) ready"
            : "No trace contours found";

    public double TwoDReferenceTraceThreshold
    {
        get => _twoDWorkspace.ReferenceImageTraceSource?.TraceThreshold ?? 0.5;
        set => UpdateTwoDReferenceTraceOptions(image => image with { TraceThreshold = value });
    }

    public double TwoDReferenceTraceTolerance
    {
        get => _twoDWorkspace.ReferenceImageTraceSource?.TraceTolerance ?? 50.0;
        set => UpdateTwoDReferenceTraceOptions(image => image with { TraceTolerance = value });
    }

    public double TwoDReferenceTraceCornerSmoothness
    {
        get => _twoDWorkspace.ReferenceImageTraceSource?.TraceCornerSmoothness ?? 50.0;
        set => UpdateTwoDReferenceTraceOptions(image => image with { TraceCornerSmoothness = value });
    }

    public double TwoDReferenceTracePathOptimization
    {
        get => _twoDWorkspace.ReferenceImageTraceSource?.TracePathOptimization ?? 50.0;
        set => UpdateTwoDReferenceTraceOptions(image => image with { TracePathOptimization = value });
    }

    public bool TwoDReferenceTraceSilhouetteOnly
    {
        get => _twoDWorkspace.ReferenceImageTraceSource?.TraceSilhouetteOnly ?? false;
        set => UpdateTwoDReferenceTraceOptions(image => image with { TraceSilhouetteOnly = value });
    }

    private string _twoDReferenceCalibrationWidthText = "100";
    private bool _twoDReferencePointCalibrationActive;
    private IReadOnlyList<Editor2DPoint> _twoDReferenceCalibrationPoints = [];
    private string _twoDReferenceCalibrationTargetText = string.Empty;
    private string? _twoDReferenceCalibrationLayerId;

    public string TwoDReferenceCalibrationWidthText
    {
        get => _twoDReferenceCalibrationWidthText;
        set => SetProperty(ref _twoDReferenceCalibrationWidthText, value ?? string.Empty);
    }

    public bool TwoDReferencePointCalibrationActive
    {
        get => _twoDReferencePointCalibrationActive;
        set
        {
            if (SetProperty(ref _twoDReferencePointCalibrationActive, value))
                OnPropertyChanged(nameof(TwoDReferencePointCalibrationSummary));
        }
    }

    public IReadOnlyList<Editor2DPoint> TwoDReferenceCalibrationPoints
    {
        get => _twoDReferenceCalibrationPoints;
        set
        {
            if (SetProperty(ref _twoDReferenceCalibrationPoints, value ?? []))
            {
                OnPropertyChanged(nameof(TwoDReferencePointCalibrationSummary));
                OnPropertyChanged(nameof(TwoDReferenceCalibrationAwaitingDistance));
            }
        }
    }

    public string TwoDReferenceCalibrationTargetText
    {
        get => _twoDReferenceCalibrationTargetText;
        set => SetProperty(ref _twoDReferenceCalibrationTargetText, value ?? string.Empty);
    }

    public string TwoDReferencePointCalibrationSummary
        => TwoDReferencePointCalibrationActive
            ? $"Pick two points on the canvas ({TwoDReferenceCalibrationPoints.Count}/2)."
            : TwoDReferenceCalibrationPoints.Count == 2
                ? "Enter the known distance between the picked points."
                : "Pick two canvas points with a known real-world distance.";

    public bool TwoDReferenceCalibrationAwaitingDistance
        => TwoDReferenceCalibrationPoints.Count == 2;

    public IReadOnlyList<string> TwoDHiddenPathIds => TwoDLayers
        .Where(layer => !layer.IsVisible)
        .SelectMany(layer => layer.PathIds)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public void CreateTwoDLayer()
    {
        var layer = _twoDWorkspace.CreateLayer();
        RefreshTwoDLayerFacade();
        RecordActivity("Create Layer", layer.Name, layer.Id);
    }

    public void CreateTwoDFolder(string? parentFolderId = null)
    {
        var folder = _twoDWorkspace.CreateFolder(parentFolderId: parentFolderId);
        _expandedTwoDFolderIds.Add(folder.Id);
        ExpandTwoDFolderAncestry(parentFolderId);
        RefreshTwoDLayerFacade();
    }

    public bool ToggleTwoDFolderExpanded(string folderId)
    {
        if (!TwoDFolders.Any(folder => folder.Id == folderId))
            return false;
        if (!_expandedTwoDFolderIds.Add(folderId))
            _expandedTwoDFolderIds.Remove(folderId);
        OnPropertyChanged(nameof(TwoDLayerHierarchyItems));
        return true;
    }

    public void RenameTwoDFolder(string folderId, string name)
    {
        if (_twoDWorkspace.RenameFolder(folderId, name))
            RefreshTwoDLayerFacade();
    }

    public void DeleteTwoDFolder(string folderId)
    {
        if (_twoDWorkspace.DeleteFolder(folderId))
        {
            _expandedTwoDFolderIds.Remove(folderId);
            RefreshTwoDLayerFacade();
        }
    }

    public void MoveTwoDLayerToFolder(string layerId, string? folderId)
    {
        if (_twoDWorkspace.MoveLayerToFolder(layerId, folderId))
        {
            ExpandTwoDFolderAncestry(folderId);
            RefreshTwoDLayerFacade();
        }
    }

    public void MoveTwoDFolderToFolder(string folderId, string? parentFolderId)
    {
        if (_twoDWorkspace.MoveFolderToFolder(folderId, parentFolderId))
        {
            ExpandTwoDFolderAncestry(parentFolderId);
            RefreshTwoDLayerFacade();
        }
    }

    public void MoveTwoDFolder(string folderId, int direction)
    {
        if (_twoDWorkspace.MoveFolder(folderId, direction))
            RefreshTwoDLayerFacade();
    }

    public void ReorderTwoDHierarchyItem(string sourceId, string targetId)
    {
        if (!_twoDWorkspace.ReorderHierarchyItem(sourceId, targetId))
            return;
        var destinationFolderId = TwoDFolders.FirstOrDefault(folder => folder.Id == sourceId)?.ParentFolderId
            ?? TwoDLayers.FirstOrDefault(layer => layer.Id == sourceId)?.ParentFolderId;
        ExpandTwoDFolderAncestry(destinationFolderId);
        RefreshTwoDLayerFacade();
    }

    public async Task ImportTwoDReferenceImageAsync(CancellationToken cancellationToken = default)
    {
        var imagePath = await _projectFileDialogService
            .PickReferenceImageFileAsync(cancellationToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(true);
            if (!TryPrepareReferenceImage(bytes, out var image) || image is null)
            {
                StatusText = "Unsupported reference image format";
                return;
            }

            if (!TwoDWorkspace.IsInitialized)
                TwoDDocument = Editor2DWorkspaceState.Empty.Document;
            _twoDWorkspace.ImportReferenceImage(
                Path.GetFileName(imagePath),
                Convert.ToBase64String(image.Data),
                image.PixelWidth,
                image.PixelHeight);
            RefreshTwoDLayerFacade();
            StatusText = $"Imported reference image: {Path.GetFileName(imagePath)}";
            if (_twoDWorkspace.ActiveLayer is { } layer)
                RecordActivity("Import Reference Image", Path.GetFileName(imagePath), layer.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            StatusText = $"Could not import reference image: {ex.Message}";
        }
    }

    public void MoveTwoDReferenceImage(string layerId, double deltaX, double deltaY)
    {
        var image = GetReferenceImage(layerId);
        if (image is null)
            return;
        if (_twoDWorkspace.UpdateReferenceImageTransform(
                layerId,
                image.X + deltaX,
                image.Y + deltaY,
                image.Width,
                image.Height,
                image.RotationDegrees))
        {
            RefreshTwoDLayerFacade();
        }
    }

    public void UpdateTwoDReferenceImageTransform(
        string layerId,
        double x,
        double y,
        double width,
        double height,
        double rotationDegrees)
    {
        if (_twoDWorkspace.UpdateReferenceImageTransform(layerId, x, y, width, height, rotationDegrees))
        {
            if (_twoDWorkspace.IsReferenceImageTransformEditActive)
                RefreshTwoDReferenceImagePreviewFacade();
            else
                RefreshTwoDLayerFacade();
        }
    }

    public bool BeginTwoDReferenceImageTransform(string layerId)
        => _twoDWorkspace.BeginReferenceImageTransformEdit(layerId);

    public void CommitTwoDReferenceImageTransform()
    {
        var committed = _twoDWorkspace.CommitReferenceImageTransformEdit();
        OnPropertyChanged(nameof(TwoDReferenceImageTransformEditActive));
        if (!committed)
            return;

        RefreshTwoDReferenceImagePreviewFacade();
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        NotifyTwoDHistoryCommands();
    }

    public void CancelTwoDReferenceImageTransform()
    {
        _twoDWorkspace.CancelReferenceImageTransformEdit();
        RefreshTwoDLayerFacade();
    }

    public void ScaleTwoDReferenceImage(string layerId, double factor)
    {
        var image = GetReferenceImage(layerId);
        if (image is null || factor <= 0.0)
            return;
        if (_twoDWorkspace.UpdateReferenceImageTransform(
                layerId,
                image.X,
                image.Y,
                image.Width * factor,
                image.Height * factor,
                image.RotationDegrees))
        {
            RefreshTwoDLayerFacade();
        }
    }

    public void RotateTwoDReferenceImage(string layerId, double deltaDegrees)
    {
        var image = GetReferenceImage(layerId);
        if (image is null)
            return;
        if (_twoDWorkspace.UpdateReferenceImageTransform(
                layerId,
                image.X,
                image.Y,
                image.Width,
                image.Height,
                image.RotationDegrees + deltaDegrees))
        {
            RefreshTwoDLayerFacade();
        }
    }

    public void AdjustTwoDReferenceImageOpacity(string layerId, double delta)
    {
        var image = GetReferenceImage(layerId);
        if (image is not null && _twoDWorkspace.SetReferenceImageOpacity(layerId, image.Opacity + delta))
            RefreshTwoDLayerFacade();
    }

    public void SetTwoDReferenceImageOpacity(string layerId, double opacity)
    {
        if (_twoDWorkspace.SetReferenceImageOpacity(layerId, opacity))
        {
            if (_twoDWorkspace.IsReferenceImageTransformEditActive)
                RefreshTwoDReferenceImagePreviewFacade();
            else
                RefreshTwoDLayerFacade();
        }
    }

    public void SetTwoDReferenceImageDepth(string layerId, Editor2DReferenceImageDepth depth)
    {
        if (_twoDWorkspace.SetReferenceImageDepth(layerId, depth))
            RefreshTwoDLayerFacade();
    }

    public void AdjustTwoDReferenceTraceThreshold(string layerId, double delta)
    {
        var image = GetReferenceImage(layerId);
        if (image is not null && _twoDWorkspace.SetReferenceImageTraceThreshold(layerId, image.TraceThreshold + delta))
            RefreshTwoDLayerFacade();
    }

    public void CalibrateTwoDReferenceImage(string layerId)
    {
        if (!double.TryParse(
                TwoDReferenceCalibrationWidthText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var width))
        {
            StatusText = "Calibration width must be a positive number";
            return;
        }

        if (_twoDWorkspace.CalibrateReferenceImage(layerId, width))
        {
            RefreshTwoDLayerFacade();
            StatusText = $"Reference image calibrated to {width:0.###} units wide";
            RecordActivity("Calibrate Reference Image", $"Width {width:0.###} units", layerId);
        }
    }

    public bool BeginTwoDReferencePointCalibration()
    {
        var layer = _twoDWorkspace.ActiveLayer;
        if (layer is null || !layer.IsReferenceImage || !layer.IsVisible || layer.IsLocked)
            return false;
        _twoDReferenceCalibrationLayerId = layer.Id;
        TwoDReferenceCalibrationPoints = [];
        TwoDReferenceCalibrationTargetText = string.Empty;
        TwoDReferencePointCalibrationActive = true;
        OnPropertyChanged(nameof(TwoDReferencePointCalibrationSummary));
        StatusText = "Pick the first reference calibration point";
        return true;
    }

    public void CaptureTwoDReferenceCalibrationPoints(Editor2DPoint start, Editor2DPoint end)
    {
        TwoDReferenceCalibrationPoints = [start, end];
        TwoDReferencePointCalibrationActive = false;
        var measured = Distance(start, end);
        TwoDReferenceCalibrationTargetText = measured.ToString("0.###", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(TwoDReferencePointCalibrationSummary));
        StatusText = "Enter the known reference distance";
    }

    public bool CommitTwoDReferencePointCalibration()
    {
        if (_twoDReferenceCalibrationLayerId is not { } layerId
            || TwoDActiveLayerId != layerId
            || TwoDReferenceCalibrationPoints.Count != 2
            || !double.TryParse(TwoDReferenceCalibrationTargetText, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)
            || !double.IsFinite(target) || target <= 0.0)
        {
            StatusText = "Calibration distance must be a positive number";
            return false;
        }
        var measured = Distance(TwoDReferenceCalibrationPoints[0], TwoDReferenceCalibrationPoints[1]);
        if (!_twoDWorkspace.CalibrateReferenceImageDistance(layerId, measured, target))
            return false;
        CancelTwoDReferencePointCalibration();
        RefreshTwoDLayerFacade();
        StatusText = $"Reference image calibrated to {target:0.###} units between points";
        RecordActivity("Calibrate Reference Image", $"Distance {target:0.###} units", layerId);
        return true;
    }

    public void CancelTwoDReferencePointCalibration()
    {
        TwoDReferencePointCalibrationActive = false;
        TwoDReferenceCalibrationPoints = [];
        TwoDReferenceCalibrationTargetText = string.Empty;
        _twoDReferenceCalibrationLayerId = null;
        OnPropertyChanged(nameof(TwoDReferencePointCalibrationSummary));
    }

    private static double Distance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(right.X - left.X, 2) + Math.Pow(right.Y - left.Y, 2));

    public void TraceTwoDReferenceImage(string layerId)
    {
        if (!_twoDWorkspace.BeginReferenceImageTrace(layerId))
            return;

        RefreshTwoDLayerFacade();
        StatusText = IsTwoDReferenceTraceRunning
            ? "Tracing reference image"
            : CanCommitTwoDReferenceTrace
            ? "Adjust tracing options, then generate vectors"
            : "No trace contours found; adjust tracing options";
    }

    public void CommitTwoDReferenceTrace()
    {
        if (_twoDWorkspace.CommitReferenceImageTrace() is null)
            return;
        RefreshTwoDLayerFacade();
        StatusText = "Reference image vectorized; source image hidden";
        RecordActivity("Trace Reference Image", "Generated editable vectors");
    }

    public void CancelTwoDReferenceTrace()
    {
        _twoDWorkspace.CancelReferenceImageTrace();
        RefreshTwoDLayerFacade();
        StatusText = "Reference image tracing cancelled";
    }

    public void RemoveTwoDReferenceImageBackground(string layerId)
    {
        if (_twoDWorkspace.RemoveReferenceImageBackground(layerId))
        {
            RefreshTwoDLayerFacade();
            StatusText = "Reference image background removed";
            RecordActivity("Remove Image Background", "Removed reference image background", layerId);
        }
    }

    public void RestoreTwoDReferenceImageBackground(string layerId)
    {
        if (_twoDWorkspace.RestoreReferenceImageBackground(layerId))
        {
            RefreshTwoDLayerFacade();
            StatusText = "Reference image background restored";
            RecordActivity("Restore Image Background", "Restored reference image background", layerId);
        }
    }

    public void SelectTwoDLayer(string layerId)
    {
        if (_twoDWorkspace.SelectLayer(layerId))
        {
            CancelTwoDReferencePointCalibration();
            RefreshTwoDLayerFacade();
        }
    }

    public void ToggleTwoDLayerVisibility(string layerId)
    {
        if (_twoDWorkspace.ToggleLayerVisibility(layerId))
        {
            CancelTwoDReferencePointCalibration();
            RefreshTwoDLayerFacade();
        }
    }

    public void ToggleTwoDLayerLock(string layerId)
    {
        if (_twoDWorkspace.ToggleLayerLock(layerId))
        {
            CancelTwoDReferencePointCalibration();
            RefreshTwoDLayerFacade();
        }
    }

    public void MoveTwoDLayer(string layerId, int direction)
    {
        if (_twoDWorkspace.MoveLayer(layerId, direction))
            RefreshTwoDLayerFacade();
    }

    public void RenameTwoDLayer(string layerId, string name)
    {
        if (_twoDWorkspace.RenameLayer(layerId, name))
        {
            RefreshTwoDLayerFacade();
            RecordActivity("Rename Layer", name.Trim(), layerId);
        }
    }

    public bool SetTwoDLayerColor(string layerId, string colorHex)
    {
        _twoDWorkspace.CommitLayerColorEdit();
        if (!_twoDWorkspace.SetLayerColor(layerId, colorHex))
            return false;
        RefreshTwoDLayerFacade();
        RecordActivity("Change Layer Color", colorHex.Trim().ToUpperInvariant(), layerId);
        return true;
    }

    public bool BeginTwoDLayerColorEdit(string layerId)
        => _twoDWorkspace.BeginLayerColorEdit(layerId);

    public bool PreviewTwoDLayerColor(string layerId, string colorHex)
    {
        if (!_twoDWorkspace.UpdateLayerColorEdit(layerId, colorHex))
            return false;
        RefreshTwoDLayerFacade(requestPersistence: false);
        return true;
    }

    public bool CommitTwoDLayerColorEdit(string? layerId = null)
    {
        if (!_twoDWorkspace.CommitLayerColorEdit())
            return false;
        RefreshTwoDLayerFacade();
        if (TwoDLayers.FirstOrDefault(layer => layer.Id == layerId) is { } layer)
            RecordActivity("Change Layer Color", layer.ColorHex, layer.Id);
        return true;
    }

    public void DeleteTwoDLayer(string layerId)
    {
        if (_twoDWorkspace.DeleteLayer(layerId))
        {
            CancelTwoDReferencePointCalibration();
            RefreshTwoDLayerFacade();
            RecordActivity("Delete Layer", "Deleted layer", layerId);
        }
    }

    public void AssignTwoDSelectionToLayer(string layerId)
    {
        if (_twoDWorkspace.AssignPathsToLayer(layerId, TwoDSelectedPathIds))
            RefreshTwoDLayerFacade();
    }

    private void RefreshTwoDLayerFacade(bool requestPersistence = true)
    {
        ReconcileExpandedTwoDFolderIds();
        NotifyTwoDWorkspaceFacadeProperties();
        OnPropertyChanged(nameof(TwoDLayers));
        OnPropertyChanged(nameof(TwoDFolders));
        OnPropertyChanged(nameof(TwoDLayerHierarchyItems));
        OnPropertyChanged(nameof(TwoDActiveLayerId));
        OnPropertyChanged(nameof(TwoDHiddenPathIds));
        OnPropertyChanged(nameof(TwoDReferenceImages));
        OnPropertyChanged(nameof(TwoDActiveReferenceImage));
        OnPropertyChanged(nameof(HasTwoDActiveReferenceImage));
        OnPropertyChanged(nameof(TwoDActiveReferenceImageLocked));
        OnPropertyChanged(nameof(TwoDReferenceImageTransformEditActive));
        OnPropertyChanged(nameof(HasTwoDReferenceTraceSession));
        OnPropertyChanged(nameof(IsTwoDReferenceTraceRunning));
        OnPropertyChanged(nameof(ShowTwoDReferenceCalibration));
        OnPropertyChanged(nameof(CanCommitTwoDReferenceTrace));
        OnPropertyChanged(nameof(TwoDReferenceTraceSummary));
        OnPropertyChanged(nameof(TwoDReferenceTraceThreshold));
        OnPropertyChanged(nameof(TwoDReferenceTraceTolerance));
        OnPropertyChanged(nameof(TwoDReferenceTraceCornerSmoothness));
        OnPropertyChanged(nameof(TwoDReferenceTracePathOptimization));
        OnPropertyChanged(nameof(TwoDReferenceTraceSilhouetteOnly));
        if (requestPersistence)
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }

    private void RefreshTwoDReferenceImagePreviewFacade()
    {
        OnPropertyChanged(nameof(TwoDReferenceImages));
        OnPropertyChanged(nameof(TwoDActiveReferenceImage));
        OnPropertyChanged(nameof(TwoDReferenceImageTransformEditActive));
    }

    private void OnReferenceImageTraceChanged()
        => RefreshTwoDLayerFacade(requestPersistence: false);

    private void UpdateTwoDReferenceTraceOptions(
        Func<Editor2DReferenceImage, Editor2DReferenceImage> update)
    {
        if (_twoDWorkspace.ReferenceImageTraceLayerId is not { } layerId
            || !_twoDWorkspace.SetReferenceImageTraceOptions(layerId, update))
            return;
        RefreshTwoDLayerFacade();
    }

    private Editor2DReferenceImage? GetReferenceImage(string layerId)
        => TwoDLayers.FirstOrDefault(layer => layer.Id == layerId)?.ReferenceImage;

    private IReadOnlyList<Editor2DLayerHierarchyItem> BuildTwoDLayerHierarchyItems()
    {
        var rows = new List<Editor2DLayerHierarchyItem>(TwoDFolders.Count + TwoDLayers.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void AddChildren(string? parentFolderId, int depth)
        {
            foreach (var folder in TwoDFolders.Where(item => item.ParentFolderId == parentFolderId))
            {
                if (!visited.Add(folder.Id))
                    continue;
                var isExpanded = _expandedTwoDFolderIds.Contains(folder.Id);
                rows.Add(new(folder.Id, folder.Name, depth, true, isExpanded, Folder: folder));
                if (isExpanded)
                    AddChildren(folder.Id, depth + 1);
            }

            foreach (var layer in TwoDLayers.Where(item => item.ParentFolderId == parentFolderId))
                rows.Add(new(layer.Id, layer.Name, depth, false, false, Layer: layer));
        }

        AddChildren(null, 0);
        return rows;
    }

    private void ExpandTwoDFolderAncestry(string? folderId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (folderId is not null && visited.Add(folderId))
        {
            _expandedTwoDFolderIds.Add(folderId);
            folderId = TwoDFolders.FirstOrDefault(folder => folder.Id == folderId)?.ParentFolderId;
        }
    }

    private void ReconcileExpandedTwoDFolderIds()
    {
        var folderIds = TwoDFolders.Select(folder => folder.Id).ToHashSet(StringComparer.Ordinal);
        _expandedTwoDFolderIds.RemoveWhere(id => !folderIds.Contains(id));
    }
}
