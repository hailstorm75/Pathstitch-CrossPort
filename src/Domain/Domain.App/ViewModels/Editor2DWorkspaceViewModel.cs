using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;
using Domain.App.Services;
using System.Globalization;

namespace Domain.App.ViewModels;

/// <summary>
/// Owns the editable 2D document and its interaction state independently from
/// the editor shell and from exported DXF artifacts.
/// </summary>
public sealed partial class Editor2DWorkspaceViewModel : ObservableObject
{
    private readonly IReferenceImageTraceService? _referenceImageTraceService;
    private readonly Stack<Editor2DWorkspaceState> _undo = new();
    private readonly Stack<Editor2DWorkspaceState> _redo = new();
    private Editor2DWorkspaceState _state = Editor2DWorkspaceState.Empty;
    private int _polygonSides = 6;
    private IReadOnlyList<string> _expandedRectanglePathIds = [];
    private string _selectedTextDraft = string.Empty;
    private string _selectedTextHeightText = "5";
    private string _selectedTextFontFamily = "Inter";
    private string _selectedTextCharacterSpacingText = "0";
    private bool _selectedTextBold;
    private bool _selectedTextItalic;
    private bool _selectedTextUnderline;
    private string _selectedTextFitMode = "None";
    private bool _isSelectedTextHeightValid = true;
    private double _viewportZoom;
    private double _viewportOffsetX;
    private double _viewportOffsetY;
    private int _frameRequestToken;
    private string _convertLineStyle = "dashed";
    private readonly Dictionary<string, string> _convertLineParameterText = new(StringComparer.OrdinalIgnoreCase);
    private string _offsetMode = "Curve";
    private string _offsetSide = "Outward";
    private string _offsetDistanceText = "12";
    private string _offsetBBoxDistanceText = "12";
    private string _offsetBBoxFilletText = "0";
    private string _addThicknessWidthText = "3";
    private string _cleanupToleranceText = "0.1";
    private string _patternMode = "Rectangular";
    private string _patternCopiesXText = "3";
    private string _patternCopiesYText = "1";
    private string _patternSpacingXText = "10";
    private string _patternSpacingYText = "10";
    private string _patternCircularCountText = "6";
    private string _patternCircularAngleText = "360";
    private string _patternPathCopiesText = "4";
    private string _patternPathSpacingText = "20";
    private string _glueTabHeightText = "5";
    private string _glueTabType = "Trapezoid";
    private string _glueTabSide = "Left";
    private string _glueTabStartOffsetText = "0";
    private string _glueTabEndOffsetText = "0";
    private IReadOnlyList<Editor2DPreviewPath> _sewingHolePreviewPaths = [];
    private string? _editingSewingHoleOperationId;
    private Editor2DSewingHoleOperation? _selectedSewingHoleOperation;

    public Editor2DWorkspaceViewModel(IReferenceImageTraceService? referenceImageTraceService = null)
    {
        _referenceImageTraceService = referenceImageTraceService;
    }

    public Editor2DWorkspaceState State => _state;

    internal int PolygonSides { get => _polygonSides; set => SetProperty(ref _polygonSides, value); }
    internal IReadOnlyList<string> ExpandedRectanglePathIds { get => _expandedRectanglePathIds; set => SetProperty(ref _expandedRectanglePathIds, value); }
    internal string SelectedTextDraft { get => _selectedTextDraft; set => SetProperty(ref _selectedTextDraft, value); }
    internal string SelectedTextHeightText { get => _selectedTextHeightText; set => SetProperty(ref _selectedTextHeightText, value); }
    internal string SelectedTextFontFamily { get => _selectedTextFontFamily; set => SetProperty(ref _selectedTextFontFamily, value); }
    internal string SelectedTextCharacterSpacingText { get => _selectedTextCharacterSpacingText; set => SetProperty(ref _selectedTextCharacterSpacingText, value); }
    internal bool SelectedTextBold { get => _selectedTextBold; set => SetProperty(ref _selectedTextBold, value); }
    internal bool SelectedTextItalic { get => _selectedTextItalic; set => SetProperty(ref _selectedTextItalic, value); }
    internal bool SelectedTextUnderline { get => _selectedTextUnderline; set => SetProperty(ref _selectedTextUnderline, value); }
    internal string SelectedTextFitMode { get => _selectedTextFitMode; set => SetProperty(ref _selectedTextFitMode, value); }
    internal bool IsSelectedTextHeightValid { get => _isSelectedTextHeightValid; set => SetProperty(ref _isSelectedTextHeightValid, value); }
    internal double ViewportZoom { get => _viewportZoom; set => SetProperty(ref _viewportZoom, value); }
    internal double ViewportOffsetX { get => _viewportOffsetX; set => SetProperty(ref _viewportOffsetX, value); }
    internal double ViewportOffsetY { get => _viewportOffsetY; set => SetProperty(ref _viewportOffsetY, value); }
    internal int FrameRequestToken { get => _frameRequestToken; set => SetProperty(ref _frameRequestToken, value); }
    internal string ConvertLineStyle { get => _convertLineStyle; set => SetProperty(ref _convertLineStyle, value); }
    internal Dictionary<string, string> GetOrInitializeConvertLineParameterText(IReadOnlyDictionary<string, string> defaults)
    {
        if (_convertLineParameterText.Count == 0)
        {
            foreach (var (key, value) in defaults)
                _convertLineParameterText[key] = value;
        }
        return _convertLineParameterText;
    }
    internal string OffsetMode { get => _offsetMode; set => SetProperty(ref _offsetMode, value); }
    internal string OffsetSide { get => _offsetSide; set => SetProperty(ref _offsetSide, value); }
    internal string OffsetDistanceText { get => _offsetDistanceText; set => SetProperty(ref _offsetDistanceText, value); }
    internal string OffsetBBoxDistanceText { get => _offsetBBoxDistanceText; set => SetProperty(ref _offsetBBoxDistanceText, value); }
    internal string OffsetBBoxFilletText { get => _offsetBBoxFilletText; set => SetProperty(ref _offsetBBoxFilletText, value); }
    internal string AddThicknessWidthText { get => _addThicknessWidthText; set => SetProperty(ref _addThicknessWidthText, value); }
    internal string CleanupToleranceText { get => _cleanupToleranceText; set => SetProperty(ref _cleanupToleranceText, value); }
    internal string PatternMode { get => _patternMode; set => SetProperty(ref _patternMode, value); }
    internal string PatternCopiesXText { get => _patternCopiesXText; set => SetProperty(ref _patternCopiesXText, value); }
    internal string PatternCopiesYText { get => _patternCopiesYText; set => SetProperty(ref _patternCopiesYText, value); }
    internal string PatternSpacingXText { get => _patternSpacingXText; set => SetProperty(ref _patternSpacingXText, value); }
    internal string PatternSpacingYText { get => _patternSpacingYText; set => SetProperty(ref _patternSpacingYText, value); }
    internal string PatternCircularCountText { get => _patternCircularCountText; set => SetProperty(ref _patternCircularCountText, value); }
    internal string PatternCircularAngleText { get => _patternCircularAngleText; set => SetProperty(ref _patternCircularAngleText, value); }
    internal string PatternPathCopiesText { get => _patternPathCopiesText; set => SetProperty(ref _patternPathCopiesText, value); }
    internal string PatternPathSpacingText { get => _patternPathSpacingText; set => SetProperty(ref _patternPathSpacingText, value); }
    internal string GlueTabHeightText { get => _glueTabHeightText; set => SetProperty(ref _glueTabHeightText, value); }
    internal string GlueTabType { get => _glueTabType; set => SetProperty(ref _glueTabType, value); }
    internal string GlueTabSide { get => _glueTabSide; set => SetProperty(ref _glueTabSide, value); }
    internal string GlueTabStartOffsetText { get => _glueTabStartOffsetText; set => SetProperty(ref _glueTabStartOffsetText, value); }
    internal string GlueTabEndOffsetText { get => _glueTabEndOffsetText; set => SetProperty(ref _glueTabEndOffsetText, value); }

    public Editor2DPreviewDocument Document => _state.Document;

    public bool IsInitialized => _state.IsInitialized;

    public Editor2DTool ActiveTool => _state.ActiveTool;

    public bool SnapEnabled => _state.SnapEnabled;

    public bool GridVisible => _state.GridVisible;

    public bool ChainSelectionEnabled => _state.ChainSelectionEnabled;

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

    public void SetSnapEnabled(bool enabled)
    {
        Apply(_state with { SnapEnabled = enabled }, recordHistory: false);
    }

    public void SetGridVisible(bool visible)
    {
        Apply(_state with { GridVisible = visible }, recordHistory: false);
    }

    public void SetChainSelectionEnabled(bool enabled)
    {
        Apply(_state with { ChainSelectionEnabled = enabled }, recordHistory: false);
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

    public bool TrySetMeasurementExpression(string measurementId, string expression, out string error)
    {
        error = string.Empty;
        var measurement = Measurements.FirstOrDefault(item => item.Id == measurementId && !item.IsAutoDimension);
        if (measurement is null)
        {
            error = "Select a manual measurement first";
            return false;
        }

        var normalized = expression.Trim();
        var expressions = Measurements
            .Where(item => !string.IsNullOrWhiteSpace(item.VarName))
            .ToDictionary(item => item.VarName!, item => item.Expression ?? item.Distance.ToString(CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var varName = measurement.VarName;
        if (string.IsNullOrWhiteSpace(varName))
            varName = NextDimensionVariableName();
        expressions[varName] = normalized;

        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool TryResolve(string name, out double resolved)
        {
            if (values.TryGetValue(name, out resolved)) return true;
            if (!expressions.TryGetValue(name, out var candidate) || !resolving.Add(name))
            {
                resolved = 0;
                return false;
            }

            var dependencies = Editor2DDimensionExpression.ReferencedVariables(candidate);
            var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies)
            {
                if (!TryResolve(dependency, out var dependencyValue))
                {
                    resolving.Remove(name);
                    resolved = 0;
                    return false;
                }
                variables[dependency] = dependencyValue;
            }

            var success = Editor2DDimensionExpression.TryEvaluate(candidate, variables, out resolved)
                && double.IsFinite(resolved);
            resolving.Remove(name);
            if (success) values[name] = resolved;
            return success;
        }

        if (!TryResolve(varName, out var value) || value <= 0)
        {
            error = "Enter a positive number or arithmetic expression";
            return false;
        }

        var updatedMeasurements = Measurements.Select(item =>
        {
            var itemVariable = item.Id == measurementId ? varName : item.VarName;
            if (string.IsNullOrWhiteSpace(itemVariable) || !values.TryGetValue(itemVariable, out var itemValue))
                return item;

            var updated = item with
            {
                Expression = item.Id == measurementId ? normalized : item.Expression,
                VarName = item.Id == measurementId ? varName : item.VarName,
                IsParametric = item.Id == measurementId || item.IsParametric,
            };
            if (updated.Driven)
                return updated;

            var dx = item.End.X - item.Start.X;
            var dy = item.End.Y - item.Start.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            var unitX = length > 1e-9 ? dx / length : 1.0;
            var unitY = length > 1e-9 ? dy / length : 0.0;
            return updated with
            {
                End = new Editor2DPoint(
                    item.Start.X + (unitX * itemValue),
                    item.Start.Y + (unitY * itemValue)),
            };
        }).ToArray();

        SetMeasurements(
            updatedMeasurements,
            measurementId);
        return true;
    }

    public bool SetMeasurementDriven(string measurementId, bool driven)
    {
        var measurement = Measurements.FirstOrDefault(item => item.Id == measurementId && !item.IsAutoDimension);
        if (measurement is null)
            return false;
        SetMeasurements(
            Measurements.Select(item => item.Id == measurementId
                ? item with { Driven = driven, IsParametric = true }
                : item).ToArray(),
            measurementId);
        return true;
    }

    private string NextDimensionVariableName()
    {
        var used = Measurements
            .Select(item => item.VarName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"d{index}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    public void CommitDocumentEdit(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedPathIds,
        string? selectedMeasurementId = null)
        => Apply(_state with
        {
            Document = document,
            IsInitialized = true,
            SelectedPathIds = selectedPathIds,
            SelectedMeasurementId = selectedMeasurementId,
        });

    public void ClearManualMeasurements()
        => SetMeasurements(
            Measurements.Where(static measurement => measurement.IsAutoDimension).ToArray(),
            selectedMeasurementId: null);

    public bool DeleteSelectedMeasurement()
    {
        if (string.IsNullOrWhiteSpace(SelectedMeasurementId))
            return false;

        SetMeasurements(
            Measurements.Where(measurement => !string.Equals(measurement.Id, SelectedMeasurementId, StringComparison.Ordinal)).ToArray(),
            selectedMeasurementId: null);
        return true;
    }

    public int DeleteSelection()
    {
        if (SelectedPathIds.Count == 0)
            return 0;

        var selected = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var paths = Document.Paths.Where(path => !selected.Contains(path.Id)).ToArray();
        var deleted = Document.Paths.Count - paths.Length;
        if (deleted == 0)
            return 0;

        Apply(_state with
        {
            Document = RebuildDocument(Document, paths),
            SelectedPathIds = [],
            SelectedMeasurementId = null,
        });
        return deleted;
    }

    public int ExpandSelectedRectangles()
    {
        if (SelectedPathIds.Count == 0)
            return 0;

        var selected = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var expanded = 0;
        var paths = Document.Paths.Select(path =>
        {
            if (!path.IsAxisAlignedRectangle || !selected.Contains(path.Id))
                return path;
            expanded++;
            return path with { IsAxisAlignedRectangle = false };
        }).ToArray();
        if (expanded > 0)
            Apply(_state with { Document = RebuildDocument(Document, paths) });
        return expanded;
    }

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
        if (sourceLayer is null
            || image is null
            || sourceLayer.IsLocked
            || !sourceLayer.IsVisible
            || _referenceImageTraceService is null)
            return null;

        var contours = _referenceImageTraceService.TraceContours(image.DataBase64, image.TraceThreshold);
        if (contours.Count == 0)
            return null;

        var radians = image.RotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        Editor2DPoint Transform(Editor2DPoint pixel)
        {
            var localX = ((pixel.X / image.PixelWidth) - 0.5) * image.Width;
            var localY = ((pixel.Y / image.PixelHeight) - 0.5) * image.Height;
            return new Editor2DPoint(
                image.X + (localX * cosine) - (localY * sine),
                image.Y + (localX * sine) + (localY * cosine));
        }
        var traces = contours
            .Where(contour => contour.Count >= 3)
            .Select(contour => new Editor2DPreviewPath(
                $"trace-{Guid.NewGuid():N}",
                "REFERENCE_TRACE",
                contour.Select(Transform).ToArray(),
                IsClosed: true))
            .ToArray();
        if (traces.Length == 0)
            return null;

        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var geometryLayerIndex = layers.FindIndex(layer => layer.Kind == Editor2DLayerKind.Geometry && !layer.IsLocked);
        if (geometryLayerIndex < 0)
        {
            layers.Add(new Editor2DLayer(
                Guid.NewGuid().ToString("N"),
                "Traced geometry",
                traces.Select(trace => trace.Id).ToArray(),
                Order: layers.Count));
        }
        else
        {
            layers[geometryLayerIndex] = layers[geometryLayerIndex] with
            {
                PathIds = layers[geometryLayerIndex].PathIds
                    .Concat(traces.Select(trace => trace.Id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
            };
        }

        var nextPaths = _state.Document.Paths.Concat(traces).ToArray();
        var entityCounts = new Dictionary<string, int>(_state.Document.EntityCounts, StringComparer.OrdinalIgnoreCase);
        entityCounts["REFERENCE_TRACE"] = entityCounts.GetValueOrDefault("REFERENCE_TRACE") + traces.Length;
        Apply(_state with
        {
            Document = RebuildDocument(_state.Document with { EntityCounts = entityCounts }, nextPaths),
            Layers = layers,
            SelectedPathIds = traces.Select(trace => trace.Id).ToArray(),
        });
        return traces[0];
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

    public bool MergeLayerWithBelow(string layerId)
    {
        var ordered = Layers.OrderBy(layer => layer.Order).ToArray();
        var sourceIndex = Array.FindIndex(ordered, layer => layer.Id == layerId);
        if (sourceIndex <= 0)
            return false;

        var source = ordered[sourceIndex];
        var target = ordered[sourceIndex - 1];
        if (source.Kind != Editor2DLayerKind.Geometry
            || target.Kind != Editor2DLayerKind.Geometry
            || source.IsLocked
            || target.IsLocked)
            return false;

        var mergedTarget = target with
        {
            PathIds = target.PathIds.Concat(source.PathIds).Distinct(StringComparer.Ordinal).ToArray(),
        };
        var layers = ordered
            .Where(layer => layer.Id != source.Id)
            .Select(layer => layer.Id == target.Id ? mergedTarget : layer)
            .Select((layer, order) => layer with { Order = order })
            .ToArray();
        Apply(_state with
        {
            Layers = layers,
            ActiveLayerId = ActiveLayerId == source.Id ? target.Id : ActiveLayerId,
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
        OnPropertyChanged(nameof(SnapEnabled));
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
