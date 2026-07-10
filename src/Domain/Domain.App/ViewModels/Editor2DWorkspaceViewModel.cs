using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;
using Domain.App.Services;

namespace Domain.App.ViewModels;

/// <summary>
/// Owns the editable 2D document and its interaction state independently from
/// the editor shell and from exported DXF artifacts.
/// </summary>
public sealed class Editor2DWorkspaceViewModel : ObservableObject
{
    private readonly Stack<Editor2DWorkspaceState> _undo = new();
    private readonly Stack<Editor2DWorkspaceState> _redo = new();
    private Editor2DWorkspaceState _state = Editor2DWorkspaceState.Empty;
    private IReadOnlyList<Editor2DPreviewPath> _sewingHolePreviewPaths = [];
    private string? _editingSewingHoleOperationId;
    private Editor2DSewingHoleOperation? _selectedSewingHoleOperation;

    public Editor2DWorkspaceState State => _state;

    public Editor2DPreviewDocument Document => _state.Document;

    public bool IsInitialized => _state.IsInitialized;

    public Editor2DTool ActiveTool => _state.ActiveTool;

    public bool IsSewingHoleToolActive => ActiveTool == Editor2DTool.AddSewingHoles;

    public IReadOnlyList<string> SelectedPathIds => _state.SelectedPathIds ?? [];

    public IReadOnlyList<Editor2DMeasurement> Measurements => _state.Measurements ?? [];

    public string? SelectedMeasurementId => _state.SelectedMeasurementId;

    public IReadOnlyList<Editor2DLayer> Layers => _state.Layers ?? [];

    public string? ActiveLayerId => _state.ActiveLayerId;

    public Editor2DLayer? ActiveLayer => Layers.FirstOrDefault(layer => layer.Id == ActiveLayerId);

    public IReadOnlyList<Editor2DCornerParameter> CornerParameters => _state.CornerParameters ?? [];

    public Editor2DSewingHoleParameters SewingHoleParameters => _state.SewingHoleParameters ?? Editor2DSewingHoleParameters.Default;
    public IReadOnlyList<Editor2DSewingHoleOperation> SewingHoleOperations => _state.SewingHoleOperations ?? [];
    public IReadOnlyList<Editor2DPreviewPath> SewingHolePreviewPaths => _sewingHolePreviewPaths;
    public int SewingHolePreviewCount => _sewingHolePreviewPaths.Count;
    public bool HasSewingHolePreview => _sewingHolePreviewPaths.Count > 0;
    public bool CanPreviewSewingHoles => SelectedPathIds.Any(id => Document.Paths.Any(path => path.Id == id));
    public bool CanCommitSewingHoles => HasSewingHolePreview;
    public IReadOnlyList<Editor2DSewingCornerMode> SewingCornerModes { get; } = Enum.GetValues<Editor2DSewingCornerMode>();
    public double SewingHoleDiameter { get => SewingHoleParameters.Diameter; set => UpdateSewingParameters(p => p with { Diameter = Math.Max(0.02, value) }); }
    public double SewingHolePitch { get => SewingHoleParameters.Pitch; set => UpdateSewingParameters(p => p with { Pitch = Math.Max(0.1, value) }); }
    public double SewingHoleMargin { get => SewingHoleParameters.Margin; set => UpdateSewingParameters(p => p with { Margin = value }); }
    public Editor2DSewingCornerMode SewingCornerMode { get => SewingHoleParameters.CornerMode; set => UpdateSewingParameters(p => p with { CornerMode = value }); }
    public double SewingCornerClearance { get => SewingHoleParameters.CornerClearance; set => UpdateSewingParameters(p => p with { CornerClearance = Math.Max(0, value) }); }
    public bool SewingAvoidanceEnabled { get => SewingHoleParameters.AvoidanceEnabled; set => UpdateSewingParameters(p => p with { AvoidanceEnabled = value }); }
    public double SewingAvoidanceClearance { get => SewingHoleParameters.AvoidanceClearance; set => UpdateSewingParameters(p => p with { AvoidanceClearance = Math.Max(0, value) }); }
    public bool SewingSymmetricDistribution { get => SewingHoleParameters.SymmetricDistribution; set => UpdateSewingParameters(p => p with { SymmetricDistribution = value }); }
    public int SewingAvoidPathCount => (SewingHoleParameters.AvoidPathIds ?? []).Count;
    public Editor2DSewingHoleOperation? SelectedSewingHoleOperation
    {
        get => _selectedSewingHoleOperation;
        set
        {
            if (Equals(_selectedSewingHoleOperation, value))
                return;
            _selectedSewingHoleOperation = value;
            OnPropertyChanged();
            if (value is not null)
                BeginEditSewingHoleOperation(value.Id);
        }
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Apply(Editor2DWorkspaceState state, bool recordHistory = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        var normalized = Normalize(state);
        if (Equals(_state, normalized))
            return;

        if (recordHistory)
        {
            _undo.Push(_state);
            _redo.Clear();
        }

        _state = normalized;
        RaiseStateChanged();
    }

    public void SetDocument(Editor2DPreviewDocument document)
    {
        ClearSewingHolePreview();
        Edit(state => state with { Document = document, IsInitialized = true });
    }

    public void ClearDocument()
    {
        ClearSewingHolePreview();
        Edit(_ => Editor2DWorkspaceState.Empty);
    }

    public void Edit(Func<Editor2DWorkspaceState, Editor2DWorkspaceState> transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        Apply(transaction(_state));
    }

    public void SetActiveTool(Editor2DTool tool)
    {
        if (tool != Editor2DTool.AddSewingHoles)
            ClearSewingHolePreview();
        Apply(_state with { ActiveTool = tool }, recordHistory: false);
    }

    public void SetSelection(IReadOnlyList<string> selectedPathIds)
    {
        ClearSewingHolePreview();
        var editableIds = Layers
            .Where(layer => layer.IsVisible && !layer.IsLocked)
            .SelectMany(layer => layer.PathIds)
            .ToHashSet(StringComparer.Ordinal);
        var normalized = selectedPathIds.Where(editableIds.Contains).Distinct(StringComparer.Ordinal).ToArray();
        var editingOperation = SewingHoleOperations.FirstOrDefault(operation => operation.Id == _editingSewingHoleOperationId);
        if (editingOperation is not null
            && !editingOperation.SourcePathIds.Order(StringComparer.Ordinal).SequenceEqual(normalized.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            _editingSewingHoleOperationId = null;
            _selectedSewingHoleOperation = null;
            OnPropertyChanged(nameof(SelectedSewingHoleOperation));
        }
        Apply(
            _state with { SelectedPathIds = normalized },
            recordHistory: false);
        OnPropertyChanged(nameof(CanPreviewSewingHoles));
    }

    public void SetMeasurements(
        IReadOnlyList<Editor2DMeasurement> measurements,
        string? selectedMeasurementId = null,
        bool recordHistory = true)
        => Apply(
            _state with { Measurements = measurements, SelectedMeasurementId = selectedMeasurementId },
            recordHistory);

    public void SetSelectedMeasurement(string? selectedMeasurementId)
        => Apply(_state with { SelectedMeasurementId = selectedMeasurementId }, recordHistory: false);

    public Editor2DLayer CreateLayer(string? name = null)
    {
        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var layer = new Editor2DLayer(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(name) ? $"Layer {layers.Count + 1}" : name.Trim(),
            [],
            Order: layers.Count);
        layers.Add(layer);
        Apply(_state with { Layers = layers, ActiveLayerId = layer.Id });
        return layer;
    }

    public Editor2DLayer ImportReferenceImage(
        string fileName,
        string dataBase64,
        int pixelWidth,
        int pixelHeight)
    {
        if (string.IsNullOrWhiteSpace(dataBase64))
            throw new ArgumentException("Reference image data is required.", nameof(dataBase64));
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Reference image dimensions must be positive.");

        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var scale = Math.Min(1.0, 240.0 / Math.Max(pixelWidth, pixelHeight));
        var id = Guid.NewGuid().ToString("N");
        var image = new Editor2DReferenceImage(
            id,
            string.IsNullOrWhiteSpace(fileName) ? "Reference image" : fileName.Trim(),
            dataBase64,
            pixelWidth,
            pixelHeight,
            X: 0.0,
            Y: 0.0,
            Width: pixelWidth * scale,
            Height: pixelHeight * scale,
            CalibrationUnitsPerPixel: scale);
        var layer = new Editor2DLayer(
            id,
            image.FileName,
            [],
            Order: layers.Count,
            Kind: Editor2DLayerKind.ReferenceImage,
            ReferenceImage: image);
        layers.Add(layer);
        Apply(_state with { Layers = layers, ActiveLayerId = layer.Id, SelectedPathIds = [] });
        return layer;
    }

    public bool UpdateReferenceImageTransform(
        string layerId,
        double x,
        double y,
        double width,
        double height,
        double rotationDegrees)
        => UpdateReferenceImage(layerId, image => image with
        {
            X = x,
            Y = y,
            Width = Math.Max(0.001, width),
            Height = Math.Max(0.001, height),
            RotationDegrees = NormalizeRotation(rotationDegrees),
        });

    public bool CalibrateReferenceImage(string layerId, double realWorldWidth)
        => UpdateReferenceImage(layerId, image =>
        {
            if (!double.IsFinite(realWorldWidth) || realWorldWidth <= 0.0)
                return image;

            var unitsPerPixel = realWorldWidth / image.PixelWidth;
            return image with
            {
                Width = realWorldWidth,
                Height = image.PixelHeight * unitsPerPixel,
                CalibrationUnitsPerPixel = unitsPerPixel,
            };
        });

    public bool SetReferenceImageOpacity(string layerId, double opacity)
        => UpdateReferenceImage(layerId, image => image with { Opacity = Math.Clamp(opacity, 0.0, 1.0) });

    public bool SetReferenceImageTraceThreshold(string layerId, double threshold)
        => UpdateReferenceImage(layerId, image => image with { TraceThreshold = Math.Clamp(threshold, 0.0, 1.0) });

    public Editor2DPreviewPath? TraceReferenceImageBounds(string layerId)
    {
        var sourceLayer = Layers.FirstOrDefault(layer => layer.Id == layerId && layer.IsReferenceImage);
        var image = sourceLayer?.ReferenceImage;
        if (sourceLayer is null || image is null || sourceLayer.IsLocked || !sourceLayer.IsVisible)
            return null;

        var halfWidth = image.Width / 2.0;
        var halfHeight = image.Height / 2.0;
        var radians = image.RotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        Editor2DPoint Rotate(double x, double y) => new(
            image.X + (x * cosine) - (y * sine),
            image.Y + (x * sine) + (y * cosine));
        var points = new[]
        {
            Rotate(-halfWidth, -halfHeight),
            Rotate(halfWidth, -halfHeight),
            Rotate(halfWidth, halfHeight),
            Rotate(-halfWidth, halfHeight),
        };
        var trace = new Editor2DPreviewPath(
            $"trace-{Guid.NewGuid():N}",
            "REFERENCE_TRACE",
            points,
            IsClosed: true);

        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var geometryLayerIndex = layers.FindIndex(layer => layer.Kind == Editor2DLayerKind.Geometry && !layer.IsLocked);
        if (geometryLayerIndex < 0)
        {
            layers.Add(new Editor2DLayer(
                Guid.NewGuid().ToString("N"),
                "Traced geometry",
                [trace.Id],
                Order: layers.Count));
        }
        else
        {
            layers[geometryLayerIndex] = layers[geometryLayerIndex] with
            {
                PathIds = layers[geometryLayerIndex].PathIds.Append(trace.Id).ToArray(),
            };
        }

        var nextPaths = _state.Document.Paths.Append(trace).ToArray();
        var minX = nextPaths.SelectMany(path => path.Points).Min(point => point.X);
        var minY = nextPaths.SelectMany(path => path.Points).Min(point => point.Y);
        var maxX = nextPaths.SelectMany(path => path.Points).Max(point => point.X);
        var maxY = nextPaths.SelectMany(path => path.Points).Max(point => point.Y);
        var entityCounts = new Dictionary<string, int>(_state.Document.EntityCounts, StringComparer.OrdinalIgnoreCase);
        entityCounts[trace.EntityType] = entityCounts.GetValueOrDefault(trace.EntityType) + 1;
        Apply(_state with
        {
            Document = _state.Document with
            {
                Paths = nextPaths,
                Bounds = new Editor2DBounds(minX, minY, maxX, maxY),
                EntityCounts = entityCounts,
            },
            Layers = layers,
            SelectedPathIds = [trace.Id],
        });
        return trace;
    }

    public bool SelectLayer(string layerId)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null)
            return false;

        Apply(
            _state with { ActiveLayerId = layer.Id, SelectedPathIds = layer.PathIds },
            recordHistory: false);
        return true;
    }

    public bool ToggleLayerVisibility(string layerId)
        => UpdateLayer(layerId, layer => layer with { IsVisible = !layer.IsVisible });

    public bool ToggleLayerLock(string layerId)
        => UpdateLayer(layerId, layer => layer with { IsLocked = !layer.IsLocked });

    public bool MoveLayer(string layerId, int direction)
    {
        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var index = layers.FindIndex(layer => layer.Id == layerId);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= layers.Count)
            return false;

        (layers[index], layers[target]) = (layers[target], layers[index]);
        Apply(_state with
        {
            Layers = layers.Select((layer, order) => layer with { Order = order }).ToArray(),
        });
        return true;
    }

    public bool AssignPathsToLayer(string layerId, IReadOnlyList<string> pathIds)
    {
        var validPathIds = pathIds
            .Where(id => _state.Document.Paths.Any(path => path.Id == id))
            .ToHashSet(StringComparer.Ordinal);
        if (!Layers.Any(layer => layer.Id == layerId && layer.Kind == Editor2DLayerKind.Geometry))
            return false;

        var layers = Layers.Select(layer => layer with
        {
            PathIds = layer.Id == layerId
                ? layer.PathIds.Concat(validPathIds).Distinct(StringComparer.Ordinal).ToArray()
                : layer.PathIds.Where(id => !validPathIds.Contains(id)).ToArray(),
        }).ToArray();
        Apply(_state with { Layers = layers });
        return true;
    }

    public void SetCornerParameters(IReadOnlyList<Editor2DCornerParameter> parameters)
        => Apply(_state with { CornerParameters = parameters });

    public bool UpdateCornerParameter(string parameterId, double value)
    {
        var parameter = CornerParameters.FirstOrDefault(item => item.Id == parameterId);
        if (parameter is null || value <= 0 || !double.IsFinite(value))
            return false;

        var nextParameters = CornerParameters
            .Select(item => item.Id == parameterId ? item with { Value = value } : item)
            .ToArray();
        var path = _state.Document.Paths.FirstOrDefault(item => item.Id == parameter.PathId);
        if (path is null)
            return false;

        var sourcePath = path with { Points = parameter.SourcePoints };
        var rendered = Editor2DCornerGeometry.Apply(sourcePath, nextParameters);
        var paths = _state.Document.Paths.Select(item => item.Id == path.Id ? rendered : item).ToArray();
        Apply(_state with
        {
            Document = _state.Document with { Paths = paths },
            CornerParameters = nextParameters,
        });
        return true;
    }

    public bool RefreshSewingHolePreview()
    {
        if (!CanPreviewSewingHoles)
        {
            ClearSewingHolePreview();
            return false;
        }

        var operationId = _editingSewingHoleOperationId ?? Guid.NewGuid().ToString("N");
        _sewingHolePreviewPaths = Editor2DSewingHoleGeometry.BuildPreview(
            Document, SelectedPathIds, SewingHoleParameters, operationId);
        NotifySewingPreviewChanged();
        return _sewingHolePreviewPaths.Count > 0;
    }

    public void ClearSewingHolePreview()
    {
        if (_sewingHolePreviewPaths.Count == 0)
            return;
        _sewingHolePreviewPaths = [];
        NotifySewingPreviewChanged();
    }

    public void UseSelectionAsSewingAvoidancePaths()
        => UpdateSewingParameters(parameters => parameters with
        {
            AvoidPathIds = SelectedPathIds.Distinct(StringComparer.Ordinal).ToArray(),
            AvoidanceEnabled = true,
        });

    public void ClearSewingAvoidancePaths()
        => UpdateSewingParameters(parameters => parameters with { AvoidPathIds = [] });

    public bool BeginEditSewingHoleOperation(string operationId)
    {
        var operation = SewingHoleOperations.FirstOrDefault(candidate => candidate.Id == operationId);
        if (operation is null)
            return false;
        _editingSewingHoleOperationId = operation.Id;
        Apply(_state with { SewingHoleParameters = operation.Parameters, SelectedPathIds = operation.SourcePathIds }, recordHistory: false);
        NotifySewingParametersChanged();
        return RefreshSewingHolePreview();
    }

    public bool CommitSewingHolePreview()
    {
        if (_sewingHolePreviewPaths.Count == 0)
            return false;

        var operationId = _editingSewingHoleOperationId ?? Guid.NewGuid().ToString("N");
        var previous = SewingHoleOperations.FirstOrDefault(operation => operation.Id == operationId);
        var replacedIds = (previous?.GeneratedPathIds ?? []).ToHashSet(StringComparer.Ordinal);
        var generated = _sewingHolePreviewPaths.Select((path, index) => path with { Id = $"sew-{operationId}-{index}" }).ToArray();
        var paths = Document.Paths.Where(path => !replacedIds.Contains(path.Id)).Concat(generated).ToArray();
        var operation = new Editor2DSewingHoleOperation(
            operationId, SelectedPathIds.ToArray(), generated.Select(path => path.Id).ToArray(), SewingHoleParameters);
        var activeLayerId = ActiveLayerId ?? Layers.FirstOrDefault(layer => layer.Kind == Editor2DLayerKind.Geometry)?.Id;
        var layers = Layers.Select(layer => layer with
        {
            PathIds = layer.PathIds.Where(id => !replacedIds.Contains(id))
                .Concat(layer.Id == activeLayerId ? generated.Select(path => path.Id) : [])
                .Distinct(StringComparer.Ordinal).ToArray(),
        }).ToArray();
        Apply(_state with
        {
            Document = RebuildDocument(Document, paths),
            Layers = layers,
            SewingHoleOperations = SewingHoleOperations.Where(candidate => candidate.Id != operationId).Append(operation).ToArray(),
            SelectedPathIds = operation.SourcePathIds,
            IsInitialized = true,
        });
        _editingSewingHoleOperationId = operationId;
        _selectedSewingHoleOperation = operation;
        OnPropertyChanged(nameof(SelectedSewingHoleOperation));
        ClearSewingHolePreview();
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;

        _redo.Push(_state);
        _state = _undo.Pop();
        RaiseStateChanged();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;

        _undo.Push(_state);
        _state = _redo.Pop();
        RaiseStateChanged();
        return true;
    }

    public void ClearHistory()
    {
        if (_undo.Count == 0 && _redo.Count == 0)
            return;

        _undo.Clear();
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private static Editor2DWorkspaceState Normalize(Editor2DWorkspaceState state)
    {
        var pathIds = state.Document.Paths.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var selectedPathIds = (state.SelectedPathIds ?? [])
            .Where(pathIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var measurements = state.Measurements ?? [];
        var selectedMeasurementId = measurements.Any(item => item.Id == state.SelectedMeasurementId)
            ? state.SelectedMeasurementId
            : null;

        var layers = NormalizeLayers(state, pathIds);
        var activeLayerId = layers.Any(layer => layer.Id == state.ActiveLayerId)
            ? state.ActiveLayerId
            : layers.FirstOrDefault()?.Id;

        return state with
        {
            SelectedPathIds = selectedPathIds,
            Measurements = measurements,
            SelectedMeasurementId = selectedMeasurementId,
            PolygonSides = Math.Clamp(state.PolygonSides, 3, 128),
            ExpandedRectanglePathIds = (state.ExpandedRectanglePathIds ?? [])
                .Where(pathIds.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Layers = layers,
            ActiveLayerId = activeLayerId,
            CornerParameters = (state.CornerParameters ?? [])
                .Where(parameter => pathIds.Contains(parameter.PathId)
                    && parameter.CornerIndex >= 0
                    && parameter.Value > 0
                    && double.IsFinite(parameter.Value))
                .DistinctBy(parameter => parameter.Id, StringComparer.Ordinal)
                .ToArray(),
            SewingHoleParameters = NormalizeSewingParameters(state.SewingHoleParameters),
            SewingHoleOperations = (state.SewingHoleOperations ?? [])
                .Where(operation => operation.SourcePathIds.All(pathIds.Contains))
                .Select(operation => operation with
                {
                    GeneratedPathIds = operation.GeneratedPathIds.Where(pathIds.Contains).ToArray(),
                    Parameters = NormalizeSewingParameters(operation.Parameters),
                })
                .ToArray(),
        };
    }

    private bool UpdateLayer(string layerId, Func<Editor2DLayer, Editor2DLayer> update)
    {
        if (!Layers.Any(layer => layer.Id == layerId))
            return false;

        Apply(_state with
        {
            Layers = Layers.Select(layer => layer.Id == layerId ? update(layer) : layer).ToArray(),
        });
        return true;
    }

    private bool UpdateReferenceImage(
        string layerId,
        Func<Editor2DReferenceImage, Editor2DReferenceImage> update)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId && candidate.IsReferenceImage);
        if (layer?.ReferenceImage is null || layer.IsLocked)
            return false;

        return UpdateLayer(layerId, candidate => candidate with
        {
            ReferenceImage = update(candidate.ReferenceImage!),
            PathIds = [],
        });
    }

    private static double NormalizeRotation(double rotationDegrees)
    {
        if (!double.IsFinite(rotationDegrees))
            return 0.0;
        var normalized = rotationDegrees % 360.0;
        return normalized < -180.0 ? normalized + 360.0 : normalized > 180.0 ? normalized - 360.0 : normalized;
    }

    private static IReadOnlyList<Editor2DLayer> NormalizeLayers(
        Editor2DWorkspaceState state,
        IReadOnlySet<string> documentPathIds)
    {
        if (!state.IsInitialized && (state.Layers is null || state.Layers.Count == 0))
            return [];

        var layers = (state.Layers ?? [])
            .Where(layer => !string.IsNullOrWhiteSpace(layer.Id))
            .DistinctBy(layer => layer.Id, StringComparer.Ordinal)
            .OrderBy(layer => layer.Order)
            .Select((layer, order) => layer with
            {
                Name = string.IsNullOrWhiteSpace(layer.Name) ? $"Layer {order + 1}" : layer.Name.Trim(),
                PathIds = layer.Kind == Editor2DLayerKind.ReferenceImage
                    ? []
                    : layer.PathIds.Where(documentPathIds.Contains).Distinct(StringComparer.Ordinal).ToArray(),
                ReferenceImage = NormalizeReferenceImage(layer.ReferenceImage),
                Order = order,
            })
            .Where(layer => layer.Kind != Editor2DLayerKind.ReferenceImage || layer.ReferenceImage is not null)
            .ToList();
        if (layers.Count == 0)
            layers.Add(new Editor2DLayer("layer-1", "Layer 1", [], Order: 0));
        else if (!layers.Any(layer => layer.Kind == Editor2DLayerKind.Geometry))
            layers.Insert(0, new Editor2DLayer("layer-1", "Layer 1", [], Order: 0));

        layers = layers
            .Select((layer, order) => layer with { Order = order })
            .ToList();

        var assigned = layers.SelectMany(layer => layer.PathIds).ToHashSet(StringComparer.Ordinal);
        var unassigned = documentPathIds.Where(id => !assigned.Contains(id)).ToArray();
        if (unassigned.Length > 0)
        {
            var targetIndex = layers.FindIndex(layer =>
                layer.Id == state.ActiveLayerId && layer.Kind == Editor2DLayerKind.Geometry);
            if (targetIndex < 0)
                targetIndex = layers.FindIndex(layer => layer.Kind == Editor2DLayerKind.Geometry);
            layers[targetIndex] = layers[targetIndex] with
            {
                PathIds = layers[targetIndex].PathIds.Concat(unassigned).Distinct(StringComparer.Ordinal).ToArray(),
            };
        }

        return layers;
    }

    private static Editor2DReferenceImage? NormalizeReferenceImage(Editor2DReferenceImage? image)
    {
        if (image is null
            || string.IsNullOrWhiteSpace(image.Id)
            || string.IsNullOrWhiteSpace(image.DataBase64)
            || image.PixelWidth <= 0
            || image.PixelHeight <= 0)
        {
            return null;
        }

        return image with
        {
            FileName = string.IsNullOrWhiteSpace(image.FileName) ? "Reference image" : image.FileName.Trim(),
            Width = Math.Max(0.001, image.Width),
            Height = Math.Max(0.001, image.Height),
            RotationDegrees = NormalizeRotation(image.RotationDegrees),
            Opacity = Math.Clamp(image.Opacity, 0.0, 1.0),
            CalibrationUnitsPerPixel = image.CalibrationUnitsPerPixel > 0.0
                ? image.CalibrationUnitsPerPixel
                : image.Width / image.PixelWidth,
            TraceThreshold = Math.Clamp(image.TraceThreshold, 0.0, 1.0),
        };
    }

    private void UpdateSewingParameters(Func<Editor2DSewingHoleParameters, Editor2DSewingHoleParameters> update)
    {
        var normalized = NormalizeSewingParameters(update(SewingHoleParameters));
        if (Equals(normalized, SewingHoleParameters))
            return;
        Apply(_state with { SewingHoleParameters = normalized }, recordHistory: false);
        NotifySewingParametersChanged();
        if (HasSewingHolePreview)
            RefreshSewingHolePreview();
    }

    private static Editor2DSewingHoleParameters NormalizeSewingParameters(Editor2DSewingHoleParameters? parameters)
    {
        var value = parameters ?? Editor2DSewingHoleParameters.Default;
        return value with
        {
            Diameter = Math.Max(0.02, value.Diameter),
            Pitch = Math.Max(0.1, value.Pitch),
            CornerClearance = Math.Max(0, value.CornerClearance),
            AvoidanceClearance = Math.Max(0, value.AvoidanceClearance),
            AvoidPathIds = (value.AvoidPathIds ?? []).Distinct(StringComparer.Ordinal).ToArray(),
        };
    }

    private void NotifySewingParametersChanged()
    {
        OnPropertyChanged(nameof(SewingHoleParameters));
        OnPropertyChanged(nameof(SewingHoleDiameter));
        OnPropertyChanged(nameof(SewingHolePitch));
        OnPropertyChanged(nameof(SewingHoleMargin));
        OnPropertyChanged(nameof(SewingCornerMode));
        OnPropertyChanged(nameof(SewingCornerClearance));
        OnPropertyChanged(nameof(SewingAvoidanceEnabled));
        OnPropertyChanged(nameof(SewingAvoidanceClearance));
        OnPropertyChanged(nameof(SewingSymmetricDistribution));
        OnPropertyChanged(nameof(SewingAvoidPathCount));
    }

    private void NotifySewingPreviewChanged()
    {
        OnPropertyChanged(nameof(SewingHolePreviewPaths));
        OnPropertyChanged(nameof(SewingHolePreviewCount));
        OnPropertyChanged(nameof(HasSewingHolePreview));
        OnPropertyChanged(nameof(CanCommitSewingHoles));
    }

    private static Editor2DPreviewDocument RebuildDocument(Editor2DPreviewDocument source, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        var bounds = points.Length == 0
            ? new Editor2DBounds(0, 0, 0, 0)
            : new Editor2DBounds(points.Min(point => point.X), points.Min(point => point.Y), points.Max(point => point.X), points.Max(point => point.Y));
        return source with
        {
            Paths = paths,
            Bounds = bounds,
            EntityCounts = paths.GroupBy(path => path.EntityType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
        };
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(ActiveTool));
        OnPropertyChanged(nameof(IsSewingHoleToolActive));
        OnPropertyChanged(nameof(SelectedPathIds));
        OnPropertyChanged(nameof(Measurements));
        OnPropertyChanged(nameof(SelectedMeasurementId));
        OnPropertyChanged(nameof(Layers));
        OnPropertyChanged(nameof(ActiveLayerId));
        OnPropertyChanged(nameof(ActiveLayer));
        OnPropertyChanged(nameof(CornerParameters));
        OnPropertyChanged(nameof(SewingHoleParameters));
        OnPropertyChanged(nameof(SewingHoleOperations));
        if (_selectedSewingHoleOperation is not null)
        {
            _selectedSewingHoleOperation = SewingHoleOperations.FirstOrDefault(operation => operation.Id == _selectedSewingHoleOperation.Id);
            OnPropertyChanged(nameof(SelectedSewingHoleOperation));
        }
        OnPropertyChanged(nameof(CanPreviewSewingHoles));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
