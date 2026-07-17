using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;
using Domain.App.Services;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Domain.App.ViewModels;

/// <summary>
/// Owns the editable 2D document and its interaction state independently from
/// the editor shell and from exported DXF artifacts.
/// </summary>
public sealed partial class Editor2DWorkspaceViewModel : ObservableObject
{
    private static readonly IReadOnlySet<string> SupportedConvertLineStyles = new HashSet<string>(
        ["dashed", "dotted", "zigzag", "wave", "striped", "square", "triangle"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, (string Key, double Default, double Minimum, bool Integer)[]> ConvertLineSettings =
        new Dictionary<string, (string, double, double, bool)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["dashed"] = [("dash_length", 4, .1, false), ("gap", 3, .1, false)],
            ["dotted"] = [("spacing", 3, .2, false), ("dot_radius", .5, .05, false)],
            ["zigzag"] = [("wavelength", 6, .5, false), ("amplitude", 2, 0, false)],
            ["wave"] = [("wavelength", 6, .5, false), ("amplitude", 2, 0, false), ("samples_per_wave", 12, 4, true)],
            ["striped"] = [("dash_length", 3, .1, false), ("gap", 3, .1, false), ("tilt", 45, -360, false)],
            ["square"] = [("spacing", 4, .3, false), ("size", 1.5, .1, false)],
            ["triangle"] = [("spacing", 5, .3, false), ("size", 2, .1, false)],
        };
    private static readonly IReadOnlySet<string> ConvertLinePhysicalLengthSettings = new HashSet<string>(
        ["dash_length", "gap", "spacing", "dot_radius", "wavelength", "amplitude", "size"],
        StringComparer.OrdinalIgnoreCase);
    private readonly IReferenceImageTraceService? _referenceImageTraceService;
    private readonly IReferenceImageBackgroundRemovalService? _referenceImageBackgroundRemovalService;
    private readonly Stack<Editor2DWorkspaceState> _undo = new();
    private readonly Stack<Editor2DWorkspaceState> _redo = new();
    private readonly Dictionary<string, Editor2DMirrorLink> _mirrorLinks = new(StringComparer.Ordinal);
    private Editor2DWorkspaceState? _measurementEditOrigin;
    private CornerToolSession? _cornerToolSession;
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
    private readonly ObservableCollection<string> _sewingSideOptionItems = ["Left", "Right", "Both"];
    private bool _sewingUsesRadialSideVocabulary;
    private string _convertLineStyle = "dashed";
    private readonly Dictionary<string, string> _convertLineParameterText = new(StringComparer.OrdinalIgnoreCase);
    private string _offsetMode = "Curve";
    private string _offsetSide = "Outward";
    private string _offsetDistanceText = "12";
    private string _offsetBBoxDistanceText = "12";
    private string _offsetBBoxFilletText = "0";
    private bool _offsetConstruction;
    private string _addThicknessWidthText = "3";
    private string _cleanupToleranceText = "0.1";
    private string _patternMode = "Rectangular";
    private string _patternDistanceMode = "Spacing";
    private string _patternCopiesXText = "3";
    private string _patternCopiesYText = "1";
    private string _patternSpacingXText = "10";
    private string _patternSpacingYText = "10";
    private string _patternExtentXText = "40";
    private string _patternExtentYText = "40";
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
    private IReadOnlyList<Editor2DPreviewPath> _referenceImageTracePreviewPaths = [];
    private string? _referenceImageTraceLayerId;
    private CancellationTokenSource? _referenceImageTraceCancellation;
    private Task<bool> _referenceImageTraceTask = Task.FromResult(false);
    private long _referenceImageTraceGeneration;
    private bool _isReferenceImageTracePreviewPending;
    private Editor2DWorkspaceState? _referenceImageTransformOrigin;
    private string? _referenceImageTransformLayerId;
    private string? _editingSewingHoleOperationId;
    private Editor2DSewingHoleOperation? _selectedSewingHoleOperation;

    private sealed record CornerToolSession(
        Editor2DWorkspaceState State,
        IReadOnlyList<Editor2DWorkspaceState> Undo,
        IReadOnlyList<Editor2DWorkspaceState> Redo)
    {
        public bool HasChanges { get; set; }
        public string? ActiveParameterId { get; set; }
    }

    public Editor2DWorkspaceViewModel(
        IReferenceImageTraceService? referenceImageTraceService = null,
        IReferenceImageBackgroundRemovalService? referenceImageBackgroundRemovalService = null)
    {
        _referenceImageTraceService = referenceImageTraceService;
        _referenceImageBackgroundRemovalService = referenceImageBackgroundRemovalService;
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
    internal bool OffsetConstruction { get => _offsetConstruction; set => SetProperty(ref _offsetConstruction, value); }
    internal string AddThicknessWidthText { get => _addThicknessWidthText; set => SetProperty(ref _addThicknessWidthText, value); }
    internal string CleanupToleranceText { get => _cleanupToleranceText; set => SetProperty(ref _cleanupToleranceText, value); }
    internal string PatternMode { get => _patternMode; set => SetProperty(ref _patternMode, value); }
    internal string PatternDistanceMode { get => _patternDistanceMode; set => SetProperty(ref _patternDistanceMode, value); }
    internal string PatternCopiesXText { get => _patternCopiesXText; set => SetProperty(ref _patternCopiesXText, value); }
    internal string PatternCopiesYText { get => _patternCopiesYText; set => SetProperty(ref _patternCopiesYText, value); }
    internal string PatternSpacingXText { get => _patternSpacingXText; set => SetProperty(ref _patternSpacingXText, value); }
    internal string PatternSpacingYText { get => _patternSpacingYText; set => SetProperty(ref _patternSpacingYText, value); }
    internal string PatternExtentXText { get => _patternExtentXText; set => SetProperty(ref _patternExtentXText, value); }
    internal string PatternExtentYText { get => _patternExtentYText; set => SetProperty(ref _patternExtentYText, value); }
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

    public bool IsSewingHoleInspectorVisible
        => IsSewingHoleToolActive || SewingHoleOperations.Count > 0;

    public IReadOnlyList<string> SelectedPathIds => _state.SelectedPathIds ?? [];

    /// <summary>
    /// Session-only mirror metadata. Links intentionally do not participate in
    /// project serialization or undo/redo snapshots; geometry restores never
    /// recreate links, and stale entries are pruned when geometry changes.
    /// </summary>
    public IReadOnlyDictionary<string, Editor2DMirrorLink> MirrorLinks => _mirrorLinks;

    public bool HasMirrorLinkSelection => SelectedPathIds.Any(_mirrorLinks.ContainsKey);

    public IReadOnlyList<Editor2DMeasurement> Measurements => _state.Measurements ?? [];

    public string? SelectedMeasurementId => _state.SelectedMeasurementId;

    public IReadOnlyList<Editor2DLayer> Layers => _state.Layers ?? [];

    public IReadOnlyList<Editor2DLayerFolder> Folders => _state.Folders ?? [];

    public string? ActiveLayerId => _state.ActiveLayerId;

    public Editor2DLayer? ActiveLayer => Layers.FirstOrDefault(layer => layer.Id == ActiveLayerId);

    public Editor2DLayerFolder CreateFolder(string? name = null, string? parentFolderId = null)
    {
        var folders = Folders.ToList();
        var parent = folders.Any(folder => folder.Id == parentFolderId) ? parentFolderId : null;
        var folder = new Editor2DLayerFolder(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(name) ? $"Folder {folders.Count + 1}" : name.Trim(),
            parent);
        Apply(_state with { Folders = folders.Append(folder).ToArray() });
        return folder;
    }

    public bool RenameFolder(string folderId, string name)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;
        var folder = Folders.FirstOrDefault(item => item.Id == folderId);
        if (folder is null || string.Equals(folder.Name, normalized, StringComparison.Ordinal))
            return false;
        Apply(_state with { Folders = Folders.Select(item => item.Id == folderId ? item with { Name = normalized } : item).ToArray() });
        return true;
    }

    public bool DeleteFolder(string folderId)
    {
        if (!Folders.Any(folder => folder.Id == folderId))
            return false;
        Apply(_state with
        {
            Folders = Folders.Where(folder => folder.Id != folderId)
                .Select(folder => folder.ParentFolderId == folderId ? folder with { ParentFolderId = null } : folder).ToArray(),
            Layers = Layers.Select(layer => layer.ParentFolderId == folderId ? layer with { ParentFolderId = null } : layer).ToArray(),
        });
        return true;
    }

    public bool MoveLayerToFolder(string layerId, string? folderId)
    {
        if (!Layers.Any(layer => layer.Id == layerId)
            || (folderId is not null && !Folders.Any(folder => folder.Id == folderId)))
            return false;
        Apply(_state with
        {
            Layers = Layers.Select(layer => layer.Id == layerId ? layer with { ParentFolderId = folderId } : layer).ToArray(),
        });
        return true;
    }

    public bool MoveFolderToFolder(string folderId, string? parentFolderId)
    {
        var folder = Folders.FirstOrDefault(item => item.Id == folderId);
        if (folder is null
            || string.Equals(folder.ParentFolderId, parentFolderId, StringComparison.Ordinal)
            || WouldCreateFolderCycle(folderId, parentFolderId))
            return false;

        Apply(_state with
        {
            Folders = Folders.Select(item => item.Id == folderId
                ? item with { ParentFolderId = parentFolderId }
                : item).ToArray(),
        });
        return true;
    }

    public bool MoveFolder(string folderId, int direction)
    {
        var folder = Folders.FirstOrDefault(item => item.Id == folderId);
        if (folder is null || direction == 0)
            return false;
        var siblings = Folders
            .Where(item => item.ParentFolderId == folder.ParentFolderId)
            .ToList();
        var siblingIndex = siblings.FindIndex(item => item.Id == folderId);
        var targetSiblingIndex = siblingIndex + Math.Sign(direction);
        if (siblingIndex < 0 || targetSiblingIndex < 0 || targetSiblingIndex >= siblings.Count)
            return false;

        var folders = Folders.ToList();
        var sourceIndex = folders.FindIndex(item => item.Id == folderId);
        var targetIndex = folders.FindIndex(item => item.Id == siblings[targetSiblingIndex].Id);
        (folders[sourceIndex], folders[targetIndex]) = (folders[targetIndex], folders[sourceIndex]);
        Apply(_state with { Folders = folders });
        return true;
    }

    public bool ReorderHierarchyItem(string sourceId, string targetId)
    {
        if (string.Equals(sourceId, targetId, StringComparison.Ordinal))
            return false;
        var sourceFolder = Folders.FirstOrDefault(item => item.Id == sourceId);
        var targetFolder = Folders.FirstOrDefault(item => item.Id == targetId);
        var sourceLayer = Layers.FirstOrDefault(item => item.Id == sourceId);
        var targetLayer = Layers.FirstOrDefault(item => item.Id == targetId);
        if ((sourceFolder is null && sourceLayer is null) || (targetFolder is null && targetLayer is null))
            return false;

        var destinationFolderId = targetFolder?.Id ?? targetLayer?.ParentFolderId;
        if (sourceFolder is not null)
        {
            if (WouldCreateFolderCycle(sourceFolder.Id, destinationFolderId))
                return false;
            var folders = Folders.Where(item => item.Id != sourceFolder.Id).ToList();
            folders.Insert(0, sourceFolder with { ParentFolderId = destinationFolderId });
            Apply(_state with { Folders = folders });
            return true;
        }

        var layers = Layers.OrderBy(item => item.Order)
            .Where(item => item.Id != sourceLayer!.Id)
            .ToList();
        var insertionIndex = targetFolder is not null
            ? 0
            : Math.Max(0, layers.FindIndex(item => item.Id == targetLayer!.Id));
        layers.Insert(insertionIndex, sourceLayer! with { ParentFolderId = destinationFolderId });
        Apply(_state with
        {
            Layers = layers.Select((item, order) => item with { Order = order }).ToArray(),
        });
        return true;
    }

    private bool WouldCreateFolderCycle(string folderId, string? parentFolderId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (parentFolderId is not null && visited.Add(parentFolderId))
        {
            if (string.Equals(parentFolderId, folderId, StringComparison.Ordinal))
                return true;
            var parent = Folders.FirstOrDefault(item => item.Id == parentFolderId);
            if (parent is null)
                return true;
            parentFolderId = parent.ParentFolderId;
        }
        return parentFolderId is not null;
    }

    public IReadOnlyList<Editor2DCornerParameter> CornerParameters => _state.CornerParameters ?? [];

    public Editor2DSewingHoleParameters SewingHoleParameters => _state.SewingHoleParameters ?? Editor2DSewingHoleParameters.Default;
    public IReadOnlyList<Editor2DSewingHoleOperation> SewingHoleOperations => _state.SewingHoleOperations ?? [];

    public IReadOnlyList<Editor2DConvertLineGroup> ConvertLineGroups => _state.ConvertLineGroups ?? [];

    public IReadOnlyList<Editor2DImportGroup> ImportGroups => _state.ImportGroups ?? [];

    public Editor2DImportGroup? FindImportGroupForPath(string pathId)
        => ImportGroups.FirstOrDefault(group => group.GeneratedPathIds.Contains(pathId, StringComparer.Ordinal));

    public Editor2DImportGroup? GetSelectedImportGroup()
    {
        var groups = GetSelectedImportGroups();
        return groups.Count == 1 ? groups[0] : null;
    }

    public IReadOnlyList<Editor2DImportGroup> GetSelectedImportGroups()
        => SelectedPathIds
            .Select(FindImportGroupForPath)
            .Where(group => group is not null)
            .DistinctBy(group => group!.Id, StringComparer.Ordinal)
            .Cast<Editor2DImportGroup>()
            .ToArray();
    public IReadOnlyList<Editor2DPreviewPath> SewingHolePreviewPaths => _sewingHolePreviewPaths;
    public IReadOnlyList<Editor2DPreviewPath> ReferenceImageTracePreviewPaths => _referenceImageTracePreviewPaths;
    public string? ReferenceImageTraceLayerId => _referenceImageTraceLayerId;
    public bool HasReferenceImageTraceSession => _referenceImageTraceLayerId is not null;
    public bool IsReferenceImageTracePreviewPending => _isReferenceImageTracePreviewPending;
    public Editor2DReferenceImage? ReferenceImageTraceSource
        => Layers.FirstOrDefault(layer => layer.Id == _referenceImageTraceLayerId)?.ReferenceImage;
    public bool IsReferenceImageTransformEditActive => _referenceImageTransformOrigin is not null;
    public int SewingHolePreviewCount => _sewingHolePreviewPaths.Count;
    public bool HasSewingHolePreview => _sewingHolePreviewPaths.Count > 0;
    public bool CanPreviewSewingHoles => SelectedPathIds.Any(id => Document.Paths.Any(path => path.Id == id));
    public bool CanCommitSewingHoles => HasSewingHolePreview;
    public IReadOnlyList<Editor2DSewingCornerMode> SewingCornerModes { get; } = Enum.GetValues<Editor2DSewingCornerMode>();
    public IReadOnlyList<Editor2DSewingDistributionMode> SewingDistributionModes { get; } = Enum.GetValues<Editor2DSewingDistributionMode>();
    public IReadOnlyList<Editor2DSewingPattern> SewingPatterns { get; } = Enum.GetValues<Editor2DSewingPattern>();
    public IReadOnlyList<Editor2DSewingSide> SewingSides { get; } = Enum.GetValues<Editor2DSewingSide>();
    public double SewingHoleDiameter { get => SewingHoleParameters.Diameter; set => UpdateSewingParameters(p => p with { Diameter = Math.Max(0.02, value) }); }
    public double SewingHolePitch { get => SewingHoleParameters.Pitch; set => UpdateSewingParameters(p => p with { Pitch = Math.Max(0.1, value) }); }
    public double SewingHoleMargin { get => SewingHoleParameters.Margin; set => UpdateSewingParameters(p => p with { Margin = Math.Max(0, value) }); }
    public Editor2DSewingPattern SewingPattern { get => SewingHoleParameters.Pattern; set => UpdateSewingParameters(p => p with { Pattern = value }); }
    public Editor2DSewingSide SewingSide { get => SewingHoleParameters.Side; set => UpdateSewingParameters(p => p with { Side = value }); }
    public double SewingSaddleSpacing { get => SewingHoleParameters.SaddleSpacing; set => UpdateSewingParameters(p => p with { SaddleSpacing = Math.Max(0, value) }); }
    public bool IsSaddleSewingPattern => SewingPattern == Editor2DSewingPattern.Saddle;
    public bool SewingUsesRadialSideVocabulary
        => UsesRadialSewingSideVocabulary(SelectedPathIds);
    public IReadOnlyList<string> SewingSideOptionItems => _sewingSideOptionItems;
    public string SewingSideSelection
    {
        get => SewingSide switch
        {
            Editor2DSewingSide.Left when SewingUsesRadialSideVocabulary => "Inner",
            Editor2DSewingSide.Right when SewingUsesRadialSideVocabulary => "Outer",
            Editor2DSewingSide.Left => "Left",
            Editor2DSewingSide.Right => "Right",
            _ => "Both",
        };
        set
        {
            var side = value?.Trim() switch
            {
                "Outer" => Editor2DSewingSide.Right,
                "Inner" => Editor2DSewingSide.Left,
                "Left" => Editor2DSewingSide.Left,
                "Right" => Editor2DSewingSide.Right,
                "Both" => Editor2DSewingSide.Both,
                _ => SewingSide,
            };
            SewingSide = side;
        }
    }
    public Editor2DSewingCornerMode SewingCornerMode { get => SewingHoleParameters.CornerMode; set => UpdateSewingParameters(p => p with { CornerMode = value }); }
    public double SewingCornerClearance { get => SewingHoleParameters.CornerClearance; set => UpdateSewingParameters(p => p with { CornerClearance = Math.Max(0, value) }); }
    public bool SewingAvoidanceEnabled { get => SewingHoleParameters.AvoidanceEnabled; set => UpdateSewingParameters(p => p with { AvoidanceEnabled = value }); }
    public double SewingAvoidanceClearance { get => SewingHoleParameters.AvoidanceClearance; set => UpdateSewingParameters(p => p with { AvoidanceClearance = Math.Max(0, value) }); }
    public bool SewingSymmetricDistribution { get => SewingHoleParameters.SymmetricDistribution; set => UpdateSewingParameters(p => p with { SymmetricDistribution = value }); }
    public Editor2DSewingDistributionMode SewingDistributionMode { get => SewingHoleParameters.DistributionMode; set => UpdateSewingParameters(p => p with { DistributionMode = value }); }
    public int SewingHoleCount { get => SewingHoleParameters.Count; set => UpdateSewingParameters(p => p with { Count = Math.Max(1, value) }); }
    public bool SewingVariableSpacingEnabled { get => SewingHoleParameters.VariableSpacingEnabled; set => UpdateSewingParameters(p => p with { VariableSpacingEnabled = value }); }
    public double SewingVariableSpacingMin { get => SewingHoleParameters.VariableSpacingMin; set => UpdateSewingParameters(p => p with { VariableSpacingMin = Math.Max(0.1, value) }); }
    public double SewingVariableSpacingMax { get => SewingHoleParameters.VariableSpacingMax; set => UpdateSewingParameters(p => p with { VariableSpacingMax = Math.Max(0.1, value) }); }
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

    public void Apply(
        Editor2DWorkspaceState state,
        bool recordHistory = true,
        bool rebuildMeasurementCaches = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (recordHistory && ReferenceEquals(state.ConvertLineGroups, _state.ConvertLineGroups))
            state = DetachEditedConvertLineGroups(state);
        var normalized = Normalize(
            state,
            rebuildMeasurementCaches && !ReferenceEquals(state.Measurements, _state.Measurements));
        if (Equals(_state, normalized))
            return;

        if (recordHistory)
        {
            CommitReferenceImageTransformEdit();
            _undo.Push(_state);
            _redo.Clear();
        }

        _state = normalized;
        var traceSessionInvalid = _referenceImageTraceLayerId is not null
            && !Layers.Any(layer => layer.Id == _referenceImageTraceLayerId
                && layer.IsReferenceImage
                && layer.IsVisible
                && !layer.IsLocked);
        var transformSessionInvalid = _referenceImageTransformOrigin is not null
            && !Layers.Any(layer => layer.Id == _referenceImageTransformLayerId
                && layer.IsReferenceImage
                && layer.IsVisible
                && !layer.IsLocked);
        if (traceSessionInvalid)
            CancelReferenceImageTrace();
        if (transformSessionInvalid)
        {
            _referenceImageTransformOrigin = null;
            _referenceImageTransformLayerId = null;
        }
        RemoveMissingMirrorLinks();
        RaiseStateChanged();
        if (transformSessionInvalid)
            OnPropertyChanged(nameof(IsReferenceImageTransformEditActive));
    }

    private Editor2DWorkspaceState DetachEditedConvertLineGroups(Editor2DWorkspaceState state)
    {
        var previousPaths = Document.Paths.ToDictionary(path => path.Id, StringComparer.Ordinal);
        var nextPaths = state.Document.Paths.ToDictionary(path => path.Id, StringComparer.Ordinal);
        var retained = ConvertLineGroups.Where(group => group.GeneratedPathIds.All(id =>
            previousPaths.TryGetValue(id, out var previous)
            && nextPaths.TryGetValue(id, out var next)
            && ReferenceEquals(previous, next))).ToArray();
        return retained.Length == ConvertLineGroups.Count ? state : state with { ConvertLineGroups = retained };
    }

    public void SetDocument(Editor2DPreviewDocument document)
    {
        ClearSewingHolePreview();
        Edit(state => state with { Document = document, IsInitialized = true });
    }

    public string? CreateLine(
        Editor2DPoint start,
        Editor2DPoint end,
        string? pathId = null)
    {
        var id = string.IsNullOrWhiteSpace(pathId) ? $"line-{Guid.NewGuid():N}" : pathId.Trim();
        var length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        if (!double.IsFinite(start.X)
            || !double.IsFinite(start.Y)
            || !double.IsFinite(end.X)
            || !double.IsFinite(end.Y)
            || !double.IsFinite(length)
            || length <= 1e-6
            || Document.Paths.Any(path => path.Id.Equals(id, StringComparison.Ordinal))
            || Measurements.Any(measurement => measurement.Id.Equals($"{id}:length", StringComparison.Ordinal)))
        {
            return null;
        }

        var path = new Editor2DPreviewPath(id, "LINE", [start, end], false, Start: start);
        var measurement = new Editor2DMeasurement(
            $"{id}:length",
            start,
            end,
            IsAutoDimension: true,
            EntityPathId: id,
            DimensionType: "length");
        ClearSewingHolePreview();
        Apply(_state with
        {
            Document = RebuildDocument(Document, [.. Document.Paths, path]),
            IsInitialized = true,
            SelectedPathIds = [id],
            Measurements = Measurements.Append(measurement).ToArray(),
        });
        return id;
    }

    public string? CreateCircle(
        Editor2DPoint center,
        Editor2DPoint edge,
        string? pathId = null)
    {
        var id = string.IsNullOrWhiteSpace(pathId) ? $"circle-{Guid.NewGuid():N}" : pathId.Trim();
        var radius = Math.Sqrt(Math.Pow(edge.X - center.X, 2) + Math.Pow(edge.Y - center.Y, 2));
        if (!double.IsFinite(center.X)
            || !double.IsFinite(center.Y)
            || !double.IsFinite(edge.X)
            || !double.IsFinite(edge.Y)
            || !double.IsFinite(radius)
            || radius <= 1e-6
            || Document.Paths.Any(path => path.Id.Equals(id, StringComparison.Ordinal))
            || Measurements.Any(measurement => measurement.Id.Equals($"{id}:radius", StringComparison.Ordinal)))
        {
            return null;
        }

        var radiusEnd = new Editor2DPoint(center.X + radius, center.Y);
        var path = new Editor2DPreviewPath(
            id,
            "CIRCLE",
            Editor2DGeometry.BuildCirclePoints(center, radius),
            true,
            Center: center,
            Radius: radius,
            StartAngleDegrees: 0,
            EndAngleDegrees: 360);
        var measurement = new Editor2DMeasurement(
            $"{id}:radius",
            center,
            radiusEnd,
            IsAutoDimension: true,
            EntityPathId: id,
            DimensionType: "radius");
        ClearSewingHolePreview();
        Apply(_state with
        {
            Document = RebuildDocument(Document, [.. Document.Paths, path]),
            IsInitialized = true,
            SelectedPathIds = [id],
            Measurements = Measurements.Append(measurement).ToArray(),
        });
        return id;
    }

    public string? CreateRectangle(
        Editor2DPoint start,
        Editor2DPoint end,
        double initialFilletRadius = 0.0,
        Editor2DFilletContinuity continuity = Editor2DFilletContinuity.G1,
        string? pathId = null)
    {
        var id = string.IsNullOrWhiteSpace(pathId) ? $"rectangle-{Guid.NewGuid():N}" : pathId.Trim();
        var creation = Editor2DRectangleCreationService.Create(id, start, end, initialFilletRadius, continuity);
        if (creation is null || Document.Paths.Any(path => path.Id == creation.Path.Id))
            return null;

        var minX = Math.Min(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxX = Math.Max(start.X, end.X);
        var maxY = Math.Max(start.Y, end.Y);
        var width = maxX - minX;
        var height = maxY - minY;
        var offset = Math.Max(Math.Min(width, height) * 0.15, 8.0);
        var rectP1 = new Editor2DPoint(minX, minY);
        var rectP2 = new Editor2DPoint(maxX, maxY);
        var filletRadius = creation.CornerParameters.FirstOrDefault()?.Value ?? 0.0;
        var autoMeasurements = new Editor2DMeasurement[]
        {
            new($"{creation.Path.Id}:width", new(minX, minY - offset), new(maxX, minY - offset),
                true, creation.Path.Id, "width", rectP1, rectP2, filletRadius, OffsetDistance: -offset),
            new($"{creation.Path.Id}:height", new(minX - offset, minY), new(minX - offset, maxY),
                true, creation.Path.Id, "height", rectP1, rectP2, filletRadius, OffsetDistance: -offset),
        };

        ClearSewingHolePreview();
        Apply(_state with
        {
            Document = RebuildDocument(Document, [.. Document.Paths, creation.Path]),
            IsInitialized = true,
            SelectedPathIds = [creation.Path.Id],
            CornerParameters = CornerParameters.Concat(creation.CornerParameters).ToArray(),
            Measurements = Measurements.Concat(autoMeasurements).ToArray(),
        });
        return creation.Path.Id;
    }

    public string? CreateText(
        Editor2DPoint boxStart,
        Editor2DPoint boxEnd,
        string text,
        double height,
        string fontFamily,
        double characterSpacing,
        bool bold,
        bool italic,
        bool underline,
        string fitMode = "None",
        string? pathId = null)
    {
        var id = string.IsNullOrWhiteSpace(pathId) ? $"text-{Guid.NewGuid():N}" : pathId.Trim();
        var path = Editor2DTextCreationService.Create(
            id, boxStart, boxEnd, text, height, fontFamily, characterSpacing,
            bold, italic, underline, fitMode);
        if (path is null || Document.Paths.Any(candidate => candidate.Id == path.Id))
            return null;

        ClearSewingHolePreview();
        Apply(_state with
        {
            Document = RebuildDocument(Document, [.. Document.Paths, path]),
            IsInitialized = true,
            SelectedPathIds = [path.Id],
        });
        return path.Id;
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
        var usedRadialVocabulary = SewingUsesRadialSideVocabulary;
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
        if (usedRadialVocabulary != SewingUsesRadialSideVocabulary)
        {
            SewingSide = SewingSide switch
            {
                Editor2DSewingSide.Left => Editor2DSewingSide.Right,
                Editor2DSewingSide.Right => Editor2DSewingSide.Left,
                _ => SewingSide,
            };
        }
        OnPropertyChanged(nameof(CanPreviewSewingHoles));
    }

    private bool UsesRadialSewingSideVocabulary(IReadOnlyList<string> selectedPathIds)
    {
        var selectedIds = selectedPathIds.ToHashSet(StringComparer.Ordinal);
        var sources = Document.Paths.Where(path => selectedIds.Contains(path.Id)).ToArray();
        return sources.Length > 0
            && sources.All(path => !path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
                && path.Points.Count < 2);
    }

    public void SetMeasurements(
        IReadOnlyList<Editor2DMeasurement> measurements,
        string? selectedMeasurementId = null,
        bool recordHistory = true)
        => Apply(
            _state with { Measurements = measurements, SelectedMeasurementId = selectedMeasurementId },
            recordHistory && _measurementEditOrigin is null,
            rebuildMeasurementCaches: false);

    public void BeginMeasurementEdit()
        => _measurementEditOrigin ??= _state;

    public void EndMeasurementEdit()
    {
        if (_measurementEditOrigin is not { } origin)
            return;

        _measurementEditOrigin = null;
        if ((origin.Measurements ?? []).SequenceEqual(_state.Measurements ?? []))
            return;

        _undo.Push(origin with
        {
            SelectedPathIds = _state.SelectedPathIds,
            SelectedMeasurementId = _state.SelectedMeasurementId,
        });
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

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
        var varName = measurement.VarName;
        if (string.IsNullOrWhiteSpace(varName))
            varName = NextDimensionVariableName();
        var trial = Measurements.Select(item => item.Id == measurementId
            ? item with { Expression = normalized, VarName = varName, IsParametric = true }
            : item).ToArray();
        if (!TryResolveMeasurementGraph(trial, out var values, out error))
            return false;

        return TryApplyMeasurementValue(
            measurement,
            trial,
            values,
            values[varName],
            measurementId,
            out error);
    }

    public bool TrySetMeasurementValue(string measurementId, double value, out string error)
    {
        error = string.Empty;
        var measurement = Measurements.FirstOrDefault(item => item.Id == measurementId);
        if (measurement is null)
        {
            error = "Select a measurement first";
            return false;
        }
        if (measurement.Driven)
        {
            error = "Driven measurements cannot resize geometry.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(measurement.EntityPathId))
        {
            error = "The measurement is not attached to editable geometry.";
            return false;
        }
        if (!double.IsFinite(value) || value <= 1e-6)
        {
            error = "Dimension value must be positive and finite.";
            return false;
        }
        if (Math.Abs(measurement.Distance - value) <= 1e-9)
            return true;

        var trial = Measurements.ToArray();
        if (!string.IsNullOrWhiteSpace(measurement.VarName))
        {
            trial = trial.Select(item => item.Id == measurementId
                ? item with
                {
                    Expression = value.ToString("R", CultureInfo.InvariantCulture),
                    IsParametric = true,
                }
                : item).ToArray();
        }
        if (!TryResolveMeasurementGraph(trial, out var values, out error))
            return false;
        if (!string.IsNullOrWhiteSpace(measurement.VarName))
            value = values[measurement.VarName!];

        return TryApplyMeasurementValue(
            measurement,
            trial,
            values,
            value,
            measurementId,
            out error);
    }

    private bool TryApplyMeasurementValue(
        Editor2DMeasurement measurement,
        Editor2DMeasurement[] trial,
        IReadOnlyDictionary<string, double> values,
        double targetValue,
        string selectedMeasurementId,
        out string error)
    {
        error = string.Empty;
        var updatedDocument = Document;
        var updatedCornerParameters = CornerParameters;
        var preservesRectangleCorners = false;
        if (!measurement.Driven && measurement.EntityPathId is { } attachedPathId)
        {
            var attachedPath = Document.Paths.FirstOrDefault(path =>
                path.Id.Equals(attachedPathId, StringComparison.Ordinal));
            var attachedCornerParameters = CornerParameters
                .Where(parameter => parameter.PathId.Equals(attachedPathId, StringComparison.Ordinal))
                .ToArray();
            if (attachedPath is null
                || !Editor2DGeometry.TryResizeForAttachedDimension(
                    attachedPath,
                    measurement.DimensionType,
                    targetValue,
                    attachedCornerParameters,
                    out var resizedPath,
                    out var resizedCornerParameters))
            {
                error = "The attached geometry cannot be resized by this dimension.";
                return false;
            }

            preservesRectangleCorners = measurement.DimensionType?.Trim().Equals(
                "width",
                StringComparison.OrdinalIgnoreCase) == true
                || measurement.DimensionType?.Trim().Equals(
                    "height",
                    StringComparison.OrdinalIgnoreCase) == true;
            if (preservesRectangleCorners)
            {
                updatedCornerParameters = CornerParameters
                    .Where(parameter => !parameter.PathId.Equals(attachedPathId, StringComparison.Ordinal))
                    .Concat(resizedCornerParameters)
                    .ToArray();
            }
            updatedDocument = RebuildDocument(
                Document,
                Document.Paths.Select(path => path.Id.Equals(attachedPathId, StringComparison.Ordinal)
                    ? resizedPath
                    : path).ToArray());
            var hasRectangleCorners = Editor2DGeometry.TryGetAttachedRectangleCorners(
                resizedPath,
                resizedCornerParameters,
                out var rectP1,
                out var rectP2);
            trial = trial.Select(item =>
            {
                if (!attachedPathId.Equals(item.EntityPathId, StringComparison.Ordinal)
                    || !Editor2DGeometry.TryBuildAttachedMeasurement(
                        resizedPath,
                        item.DimensionType,
                        item.OffsetDistance,
                        item.PlacementAngleDegrees,
                        resizedCornerParameters,
                        out var start,
                        out var end))
                {
                    return item;
                }
                var isRectangleDimension = item.DimensionType?.Trim().Equals(
                    "width",
                    StringComparison.OrdinalIgnoreCase) == true
                    || item.DimensionType?.Trim().Equals(
                        "height",
                        StringComparison.OrdinalIgnoreCase) == true;
                return item with
                {
                    Start = start,
                    End = end,
                    RectP1 = hasRectangleCorners && isRectangleDimension ? rectP1 : item.RectP1,
                    RectP2 = hasRectangleCorners && isRectangleDimension ? rectP2 : item.RectP2,
                };
            }).ToArray();
        }

        var updatedMeasurements = ApplyResolvedMeasurementValues(trial, values, updateEndpoints: true);
        var nextState = _state with
        {
            Document = updatedDocument,
            Measurements = updatedMeasurements,
            SelectedMeasurementId = selectedMeasurementId,
            CornerParameters = updatedCornerParameters,
        };
        if (measurement.EntityPathId is { } editedPathId
            && !ReferenceEquals(updatedDocument, Document))
        {
            nextState = PruneSemanticOwnershipForEditedPaths(
                nextState,
                new HashSet<string>([editedPathId], StringComparer.Ordinal),
                preservesRectangleCorners
                    ? new HashSet<string>([editedPathId], StringComparer.Ordinal)
                    : null);
        }
        Apply(nextState, rebuildMeasurementCaches: false);
        return true;
    }

    public bool SetMeasurementDriven(string measurementId, bool driven)
    {
        var measurement = Measurements.FirstOrDefault(item => item.Id == measurementId && !item.IsAutoDimension);
        if (measurement is null)
            return false;
        var varName = string.IsNullOrWhiteSpace(measurement.VarName)
            ? NextDimensionVariableName()
            : measurement.VarName!;
        var expression = string.IsNullOrWhiteSpace(measurement.Expression)
            ? measurement.Distance.ToString("R", CultureInfo.InvariantCulture)
            : measurement.Expression!;
        var trial = Measurements.Select(item => item.Id == measurementId
            ? item with
            {
                Driven = driven,
                IsParametric = true,
                VarName = varName,
                Expression = expression,
            }
            : item).ToArray();
        if (!TryResolveMeasurementGraph(trial, out var values, out _))
            return false;
        SetMeasurements(
            ApplyResolvedMeasurementValues(trial, values, updateEndpoints: true),
            measurementId);
        return true;
    }

    private static bool TryResolveMeasurementGraph(
        IReadOnlyList<Editor2DMeasurement> measurements,
        out IReadOnlyDictionary<string, double> values,
        out string error)
    {
        var expressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in measurements.Where(item => !string.IsNullOrWhiteSpace(item.VarName)))
        {
            var candidate = item.Expression ?? item.Distance.ToString("R", CultureInfo.InvariantCulture);
            if (!expressions.TryAdd(item.VarName!, candidate))
            {
                values = new Dictionary<string, double>();
                error = $"Duplicate dimension variable '{item.VarName}'.";
                return false;
            }
        }

        var resolvedValues = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolutionError = string.Empty;
        bool TryResolve(string name, out double resolved)
        {
            if (resolvedValues.TryGetValue(name, out resolved)) return true;
            if (!expressions.TryGetValue(name, out var candidate))
            {
                resolutionError = $"Unknown variable '{name}'.";
                resolved = 0.0;
                return false;
            }
            if (!resolving.Add(name))
            {
                resolutionError = $"Circular dependency involving '{name}'.";
                resolved = 0.0;
                return false;
            }
            if (!Editor2DDimensionExpression.TryGetReferencedVariables(candidate, out var dependencies, out resolutionError))
            {
                resolving.Remove(name);
                resolved = 0.0;
                return false;
            }
            var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies)
            {
                if (!TryResolve(dependency, out var dependencyValue))
                {
                    resolving.Remove(name);
                    resolved = 0.0;
                    return false;
                }
                variables[dependency] = dependencyValue;
            }
            var success = Editor2DDimensionExpression.TryEvaluate(candidate, variables, out resolved, out resolutionError);
            resolving.Remove(name);
            if (!success) return false;
            if (!double.IsFinite(resolved) || resolved <= 0.0)
            {
                resolutionError = $"Dimension variable '{name}' must be positive and finite.";
                return false;
            }
            resolvedValues[name] = resolved;
            return true;
        }

        foreach (var name in expressions.Keys)
        {
            if (TryResolve(name, out _)) continue;
            values = resolvedValues;
            error = resolutionError;
            return false;
        }
        values = resolvedValues;
        error = string.Empty;
        return true;
    }

    private static Editor2DMeasurement[] ApplyResolvedMeasurementValues(
        IReadOnlyList<Editor2DMeasurement> measurements,
        IReadOnlyDictionary<string, double> values,
        bool updateEndpoints)
        => measurements.Select(item =>
        {
            if (string.IsNullOrWhiteSpace(item.VarName) || !values.TryGetValue(item.VarName, out var value))
                return item with { EvaluatedValue = null };
            var updated = item with { EvaluatedValue = value };
            if (!updateEndpoints || updated.Driven || !string.IsNullOrWhiteSpace(updated.EntityPathId))
                return updated;
            var dx = item.End.X - item.Start.X;
            var dy = item.End.Y - item.Start.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            var unitX = length > 1e-9 ? dx / length : 1.0;
            var unitY = length > 1e-9 ? dy / length : 0.0;
            return updated with
            {
                End = new Editor2DPoint(item.Start.X + (unitX * value), item.Start.Y + (unitY * value)),
            };
        }).ToArray();

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

    private Editor2DWorkspaceState PruneSemanticOwnershipForEditedPaths(
        Editor2DWorkspaceState state,
        IReadOnlySet<string> editedPathIds,
        IReadOnlySet<string>? preservedCornerPathIds = null)
        => state with
        {
            CornerParameters = (state.CornerParameters ?? [])
                .Where(parameter => !editedPathIds.Contains(parameter.PathId)
                    || preservedCornerPathIds?.Contains(parameter.PathId) == true)
                .ToArray(),
            ConvertLineGroups = (state.ConvertLineGroups ?? [])
                .Where(group => !group.GeneratedPathIds.Any(editedPathIds.Contains))
                .ToArray(),
            ImportGroups = (state.ImportGroups ?? [])
                .Where(group => !group.GeneratedPathIds.Any(editedPathIds.Contains))
                .ToArray(),
            SewingHoleOperations = (state.SewingHoleOperations ?? [])
                .Where(operation => !operation.SourcePathIds.Any(editedPathIds.Contains)
                    && !operation.GeneratedPathIds.Any(editedPathIds.Contains))
                .ToArray(),
            ExpandedRectanglePathIds = (state.ExpandedRectanglePathIds ?? [])
                .Where(id => !editedPathIds.Contains(id))
                .ToArray(),
        };

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

    public bool UpdatePathVertex(string pathId, int vertexIndex, Editor2DPoint point)
    {
        if (string.IsNullOrWhiteSpace(pathId)
            || !double.IsFinite(point.X)
            || !double.IsFinite(point.Y))
        {
            return false;
        }

        var sourcePath = Document.Paths.FirstOrDefault(path =>
            path.Id.Equals(pathId, StringComparison.Ordinal));
        if (sourcePath is null
            || sourcePath.IsAxisAlignedRectangle
            || !(sourcePath.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
                 || sourcePath.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
                 || sourcePath.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase))
            || vertexIndex < 0
            || vertexIndex >= sourcePath.Points.Count
            || sourcePath.Points[vertexIndex] == point)
        {
            return false;
        }

        var points = sourcePath.Points.ToArray();
        points[vertexIndex] = point;
        var updatedPath = sourcePath with
        {
            Points = points,
            Start = vertexIndex == 0 && sourcePath.Start is not null ? point : sourcePath.Start,
        };
        var cornerParameters = CornerParameters.Select(parameter =>
        {
            if (!parameter.PathId.Equals(pathId, StringComparison.Ordinal)
                || parameter.SourcePoints.Count != sourcePath.Points.Count)
            {
                return parameter;
            }

            var sourcePoints = parameter.SourcePoints.ToArray();
            sourcePoints[vertexIndex] = point;
            return parameter with { SourcePoints = sourcePoints };
        }).ToArray();
        var pathCornerParameters = cornerParameters
            .Where(parameter => parameter.PathId.Equals(pathId, StringComparison.Ordinal))
            .ToArray();
        var measurements = Measurements.Select(measurement =>
        {
            if (!measurement.IsAutoDimension
                || !pathId.Equals(measurement.EntityPathId, StringComparison.Ordinal))
            {
                return measurement;
            }

            return RebuildAttachedAutoMeasurement(
                       measurement,
                       updatedPath,
                       pathCornerParameters,
                       measurement.Id,
                       pathId)
                   ?? measurement;
        }).ToArray();
        var paths = Document.Paths.Select(path =>
            path.Id.Equals(pathId, StringComparison.Ordinal) ? updatedPath : path).ToArray();

        Apply(
            _state with
            {
                Document = RebuildDocument(Document, paths),
                Measurements = measurements,
                CornerParameters = cornerParameters,
            },
            rebuildMeasurementCaches: false);
        return true;
    }

    public bool ReplacePath(string sourcePathId, IReadOnlyList<Editor2DPreviewPath> replacements)
    {
        if (string.IsNullOrWhiteSpace(sourcePathId)
            || !Document.Paths.Any(path => path.Id.Equals(sourcePathId, StringComparison.Ordinal)))
        {
            return false;
        }

        var normalizedReplacements = replacements
            .Where(path => !string.IsNullOrWhiteSpace(path.Id))
            .DistinctBy(path => path.Id, StringComparer.Ordinal)
            .ToArray();
        var replacementIds = normalizedReplacements.Select(path => path.Id).ToArray();
        var paths = Document.Paths.SelectMany(path => path.Id.Equals(sourcePathId, StringComparison.Ordinal)
            ? normalizedReplacements
            : [path]).ToArray();
        var layers = Layers.Select(layer => layer with
        {
            PathIds = layer.PathIds.SelectMany(id => id.Equals(sourcePathId, StringComparison.Ordinal)
                ? replacementIds
                : [id]).ToArray(),
        }).ToArray();

        Apply(_state with
        {
            Document = RebuildDocument(Document, paths),
            Layers = layers,
            SelectedPathIds = [],
            SelectedMeasurementId = null,
            Measurements = Measurements.Where(measurement =>
                !sourcePathId.Equals(measurement.EntityPathId, StringComparison.Ordinal)).ToArray(),
            CornerParameters = CornerParameters.Where(parameter =>
                !sourcePathId.Equals(parameter.PathId, StringComparison.Ordinal)).ToArray(),
            ConvertLineGroups = ConvertLineGroups.Where(group =>
                !group.GeneratedPathIds.Contains(sourcePathId, StringComparer.Ordinal)).ToArray(),
            ImportGroups = ImportGroups.Where(group =>
                !group.GeneratedPathIds.Contains(sourcePathId, StringComparer.Ordinal)).ToArray(),
            SewingHoleOperations = SewingHoleOperations.Where(operation =>
                !operation.SourcePathIds.Contains(sourcePathId, StringComparer.Ordinal)
                && !operation.GeneratedPathIds.Contains(sourcePathId, StringComparer.Ordinal)).ToArray(),
            ExpandedRectanglePathIds = ExpandedRectanglePathIds.Where(id =>
                !id.Equals(sourcePathId, StringComparison.Ordinal)).ToArray(),
        });
        return true;
    }

    public bool ApplySelectionTransform(Editor2DAffineTransform transform, bool createCopy = false)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (!transform.IsFinite)
            throw new ArgumentOutOfRangeException(nameof(transform), "Transform values must be finite.");

        var selectedIds = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var selectedPaths = Document.Paths.Where(path => selectedIds.Contains(path.Id)).ToArray();
        if (selectedPaths.Length == 0)
            return false;

        if (createCopy)
        {
            var copyIds = selectedPaths.ToDictionary(
                path => path.Id,
                path => $"{path.Id}:copy:{Guid.NewGuid():N}",
                StringComparer.Ordinal);
            var copies = selectedPaths.ToDictionary(
                path => path.Id,
                path => Editor2DGeometry.TransformPath(path, transform, copyIds[path.Id]),
                StringComparer.Ordinal);
            var copiedPaths = Document.Paths.SelectMany(path => copies.TryGetValue(path.Id, out var copy)
                ? new[] { path, copy }
                : new[] { path }).ToArray();
            var copyHasUniformScale = transform.TryGetUniformScale(out var copyUniformScale);
            var cornerCopies = CornerParameters
                .Where(parameter => copyIds.ContainsKey(parameter.PathId))
                .Select(parameter => parameter with
                {
                    Id = $"{parameter.Id}:copy:{Guid.NewGuid():N}",
                    PathId = copyIds[parameter.PathId],
                    Value = copyHasUniformScale ? parameter.Value * copyUniformScale : parameter.Value,
                    SourcePoints = parameter.SourcePoints.Select(transform.TransformPoint).ToArray(),
                })
                .ToArray();
            var convertLineCopies = ConvertLineGroups
                .Where(group => group.GeneratedPathIds.Count > 0
                    && group.GeneratedPathIds.All(copyIds.ContainsKey))
                .Select(group => group with
                {
                    Id = $"{group.Id}:copy:{Guid.NewGuid():N}",
                    Settings = group.Settings.ToDictionary(
                        pair => pair.Key,
                        pair => copyHasUniformScale && ConvertLinePhysicalLengthSettings.Contains(pair.Key)
                            ? pair.Value * copyUniformScale
                            : pair.Value,
                        StringComparer.OrdinalIgnoreCase),
                    Sources = group.Sources.Select(source => source with
                    {
                        SourcePath = Editor2DGeometry.TransformPath(
                            source.SourcePath,
                            transform,
                            $"{source.SourcePath.Id}:copy:{Guid.NewGuid():N}"),
                        GeneratedPathIds = source.GeneratedPathIds.Select(id => copyIds[id]).ToArray(),
                    }).ToArray(),
                })
                .ToArray();
            var layers = Layers.Select(layer => layer with
            {
                PathIds = layer.PathIds.SelectMany(id => copyIds.TryGetValue(id, out var copyId)
                    ? new[] { id, copyId }
                    : new[] { id }).ToArray(),
            }).ToArray();
            var usedMeasurementIds = Measurements
                .Select(measurement => measurement.Id)
                .ToHashSet(StringComparer.Ordinal);
            string NextCopyMeasurementId(string copyPathId, string normalizedType)
            {
                var baseId = $"{copyPathId}:{normalizedType}";
                if (usedMeasurementIds.Add(baseId))
                    return baseId;
                for (var index = 2; ; index++)
                {
                    var candidate = $"{baseId}:{index}";
                    if (usedMeasurementIds.Add(candidate))
                        return candidate;
                }
            }
            var measurementCopies = Measurements
                .Where(measurement => measurement.IsAutoDimension
                    && measurement.EntityPathId is { } pathId
                    && copyIds.ContainsKey(pathId))
                .Select(measurement =>
                {
                    var sourcePathId = measurement.EntityPathId!;
                    var copyPath = copies[sourcePathId];
                    var typeToken = AttachedAutoMeasurementTypeToken(measurement.DimensionType);
                    var transformedMeasurement = TransformAttachedMeasurementMetadata(
                        measurement,
                        transform,
                        copyHasUniformScale ? copyUniformScale : Math.Sqrt(Math.Abs(transform.Determinant)),
                        transform.Determinant < 0.0
                            ? -(copyHasUniformScale ? copyUniformScale : Math.Sqrt(Math.Abs(transform.Determinant)))
                            : copyHasUniformScale ? copyUniformScale : Math.Sqrt(Math.Abs(transform.Determinant)));
                    var measurementId = NextCopyMeasurementId(copyPath.Id, typeToken);
                    var rebuilt = RebuildAttachedAutoMeasurement(
                        transformedMeasurement,
                        copyPath,
                        cornerCopies.Where(parameter => parameter.PathId.Equals(copyPath.Id, StringComparison.Ordinal)).ToArray(),
                        measurementId,
                        copyPath.Id);
                    return (rebuilt ?? transformedMeasurement with
                    {
                        Id = measurementId,
                        EntityPathId = copyPath.Id,
                    }) with
                    {
                        VarName = null,
                        Expression = null,
                        IsParametric = false,
                        EvaluatedValue = null,
                    };
                })
                .ToArray();

            Apply(_state with
            {
                Document = RebuildDocument(Document, copiedPaths),
                SelectedPathIds = copies.Values.Select(path => path.Id).ToArray(),
                Measurements = Measurements.Concat(measurementCopies).ToArray(),
                CornerParameters = CornerParameters.Concat(cornerCopies).ToArray(),
                ConvertLineGroups = ConvertLineGroups.Concat(convertLineCopies).ToArray(),
                Layers = layers,
            });
            return true;
        }

        var paths = Document.Paths.Select(path => selectedIds.Contains(path.Id)
            ? Editor2DGeometry.TransformPath(path, transform, path.Id)
            : path).ToArray();
        var hasUniformScale = transform.TryGetUniformScale(out var uniformScale);
        var measurementScale = hasUniformScale ? uniformScale : Math.Sqrt(Math.Abs(transform.Determinant));
        var measurementOffsetScale = transform.Determinant < 0.0 ? -measurementScale : measurementScale;
        var cornerParameters = CornerParameters.Select(parameter => selectedIds.Contains(parameter.PathId)
            ? parameter with
            {
                Value = hasUniformScale ? parameter.Value * uniformScale : parameter.Value,
                SourcePoints = parameter.SourcePoints.Select(transform.TransformPoint).ToArray(),
            }
            : parameter).ToArray();
        var transformedPathsById = paths.ToDictionary(path => path.Id, StringComparer.Ordinal);
        var measurements = Measurements.Select(measurement =>
        {
            if (measurement.EntityPathId is not { } pathId || !selectedIds.Contains(pathId))
                return measurement;

            var transformedMeasurement = TransformAttachedMeasurementMetadata(
                measurement,
                transform,
                measurementScale,
                measurementOffsetScale);
            if (!measurement.IsAutoDimension)
                return transformedMeasurement;

            var pathCornerParameters = cornerParameters
                .Where(parameter => parameter.PathId.Equals(pathId, StringComparison.Ordinal))
                .ToArray();
            return RebuildAttachedAutoMeasurement(
                       transformedMeasurement,
                       transformedPathsById[pathId],
                       pathCornerParameters,
                       measurement.Id,
                       pathId)
                   ?? transformedMeasurement;
        }).ToArray();
        var convertLineGroups = ConvertLineGroups.Select(group =>
        {
            var selectedGeneratedCount = group.GeneratedPathIds.Count(selectedIds.Contains);
            if (selectedGeneratedCount == 0)
                return group;
            if (selectedGeneratedCount != group.GeneratedPathIds.Count)
                return null;
            return group with
            {
                Settings = group.Settings.ToDictionary(
                    pair => pair.Key,
                    pair => hasUniformScale && ConvertLinePhysicalLengthSettings.Contains(pair.Key)
                        ? pair.Value * uniformScale
                        : pair.Value,
                    StringComparer.OrdinalIgnoreCase),
                Sources = group.Sources.Select(source => source with
                {
                    SourcePath = Editor2DGeometry.TransformPath(source.SourcePath, transform, source.SourcePath.Id),
                }).ToArray(),
            };
        }).Where(group => group is not null).Cast<Editor2DConvertLineGroup>().ToArray();

        Apply(
            _state with
            {
                Document = RebuildDocument(Document, paths),
                SelectedPathIds = SelectedPathIds.ToArray(),
                Measurements = measurements,
                CornerParameters = cornerParameters,
                ConvertLineGroups = convertLineGroups,
            },
            rebuildMeasurementCaches: false);
        return true;
    }

    private static Editor2DMeasurement TransformAttachedMeasurementMetadata(
        Editor2DMeasurement measurement,
        Editor2DAffineTransform transform,
        double measurementScale,
        double offsetScale)
        => measurement with
        {
            Start = transform.TransformPoint(measurement.Start),
            End = transform.TransformPoint(measurement.End),
            RectP1 = measurement.RectP1 is { } rectP1 ? transform.TransformPoint(rectP1) : null,
            RectP2 = measurement.RectP2 is { } rectP2 ? transform.TransformPoint(rectP2) : null,
            FilletRadius = measurement.FilletRadius * measurementScale,
            OffsetDistance = measurement.OffsetDistance * offsetScale,
            PlacementAngleDegrees = measurement.PlacementAngleDegrees is { } placementAngle
                ? transform.TransformDirectionDegrees(placementAngle)
                : null,
            EvaluatedValue = null,
        };

    private static Editor2DMeasurement? RebuildAttachedAutoMeasurement(
        Editor2DMeasurement measurement,
        Editor2DPreviewPath path,
        IReadOnlyList<Editor2DCornerParameter> cornerParameters,
        string measurementId,
        string entityPathId)
    {
        measurement = RecoverLegacyRectangleMeasurementOffset(measurement, path, cornerParameters);
        var normalizedType = measurement.DimensionType?.Trim().ToLowerInvariant();
        if (normalizedType is not ("length" or "radius" or "width" or "height")
            || !Editor2DGeometry.TryBuildAttachedMeasurement(
                path,
                normalizedType,
                measurement.OffsetDistance,
                measurement.PlacementAngleDegrees,
                cornerParameters,
                out var start,
                out var end))
        {
            return measurement with
            {
                Id = measurementId,
                EntityPathId = entityPathId,
                EvaluatedValue = null,
            };
        }

        var rectP1 = measurement.RectP1;
        var rectP2 = measurement.RectP2;
        if (normalizedType is "width" or "height"
            && Editor2DGeometry.TryGetAttachedRectangleCorners(
                path,
                cornerParameters,
                out var first,
                out var opposite))
        {
            rectP1 = first;
            rectP2 = opposite;
        }

        return measurement with
        {
            Id = measurementId,
            EntityPathId = entityPathId,
            Start = start,
            End = end,
            RectP1 = rectP1,
            RectP2 = rectP2,
            EvaluatedValue = null,
        };
    }

    private static Editor2DMeasurement RecoverLegacyRectangleMeasurementOffset(
        Editor2DMeasurement measurement,
        Editor2DPreviewPath path,
        IReadOnlyList<Editor2DCornerParameter> cornerParameters)
    {
        if (Math.Abs(measurement.OffsetDistance) > 1e-9)
            return measurement;
        var normalizedType = measurement.DimensionType?.Trim().ToLowerInvariant();
        if (normalizedType is not ("width" or "height")
            || !Editor2DGeometry.TryBuildAttachedMeasurement(
                path,
                normalizedType,
                0.0,
                measurement.PlacementAngleDegrees,
                cornerParameters,
                out var zeroStart,
                out _))
        {
            return measurement;
        }

        var visibleOffset = normalizedType == "width"
            ? measurement.Start.Y - zeroStart.Y
            : measurement.Start.X - zeroStart.X;
        return Math.Abs(visibleOffset) <= 1e-9
            ? measurement
            : measurement with { OffsetDistance = visibleOffset };
    }

    private static string AttachedAutoMeasurementTypeToken(string? dimensionType)
    {
        var normalized = dimensionType?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
            return "auto";
        var token = new string(normalized
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray()).Trim('-');
        return token.Length == 0 ? "auto" : token;
    }

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

    public bool BreakMirrorLinksForSelection()
    {
        var linkedPathIds = SelectedPathIds
            .Where(_mirrorLinks.ContainsKey)
            .ToArray();
        if (linkedPathIds.Length == 0)
            return false;

        var remove = new HashSet<string>(linkedPathIds, StringComparer.Ordinal);
        foreach (var pathId in linkedPathIds)
            remove.Add(_mirrorLinks[pathId].PartnerPathId);
        foreach (var pathId in remove)
            _mirrorLinks.Remove(pathId);

        RaiseMirrorLinksChanged();
        return true;
    }

    public int ExpandSelectedRectangles()
    {
        if (SelectedPathIds.Count == 0)
            return 0;

        var selected = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var parametricRectangleIds = CornerParameters
            .GroupBy(parameter => parameter.PathId, StringComparer.Ordinal)
            .Where(group => group.FirstOrDefault()?.SourcePoints is { Count: 4 } source
                && Editor2DGeometry.IsAxisAlignedRectangle(source, isClosed: true))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var expandedIds = Document.Paths
            .Where(path => selected.Contains(path.Id)
                && (path.IsAxisAlignedRectangle || parametricRectangleIds.Contains(path.Id)))
            .Select(path => path.Id)
            .ToHashSet(StringComparer.Ordinal);
        var expanded = expandedIds.Count;
        var paths = Document.Paths.Select(path =>
        {
            if (!expandedIds.Contains(path.Id))
                return path;
            return path with { IsAxisAlignedRectangle = false };
        }).ToArray();
        if (expanded > 0)
        {
            Apply(_state with
            {
                Document = RebuildDocument(Document, paths),
                CornerParameters = CornerParameters.Where(parameter => !expandedIds.Contains(parameter.PathId)).ToArray(),
                Measurements = Measurements.Where(measurement =>
                    !measurement.IsAutoDimension || measurement.EntityPathId is null || !expandedIds.Contains(measurement.EntityPathId)).ToArray(),
            });
        }
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

    public bool RenameLayer(string layerId, string name)
    {
        var normalizedName = name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            return false;

        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null || string.Equals(layer.Name, normalizedName, StringComparison.Ordinal))
            return false;

        Apply(_state with
        {
            Layers = Layers.Select(candidate => candidate.Id == layerId
                ? candidate with { Name = normalizedName }
                : candidate).ToArray(),
        });
        return true;
    }

    public bool SetLayerColor(string layerId, string colorHex)
    {
        var normalized = colorHex.Trim().ToUpperInvariant();
        if (!IsValidColorHex(normalized))
            return false;

        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null || string.Equals(layer.ColorHex, normalized, StringComparison.OrdinalIgnoreCase))
            return false;

        Apply(_state with
        {
            Layers = Layers.Select(candidate => candidate.Id == layerId
                ? candidate with { ColorHex = normalized }
                : candidate).ToArray(),
        });
        return true;
    }

    public bool DeleteLayer(string layerId)
    {
        var ordered = Layers.OrderBy(layer => layer.Order).ToArray();
        var sourceIndex = Array.FindIndex(ordered, layer => layer.Id == layerId);
        if (sourceIndex < 0)
            return false;

        var source = ordered[sourceIndex];
        if (_referenceImageTraceLayerId == source.Id)
            CancelReferenceImageTrace();
        if (_referenceImageTransformLayerId == source.Id)
            CommitReferenceImageTransformEdit();
        if (source.Kind == Editor2DLayerKind.Geometry)
        {
            var geometryLayers = ordered.Where(layer => layer.Kind == Editor2DLayerKind.Geometry).ToArray();
            if (geometryLayers.Length <= 1)
                return false;

            var geometryIndex = Array.FindIndex(geometryLayers, layer => layer.Id == source.Id);
            var target = geometryIndex > 0 ? geometryLayers[geometryIndex - 1] : geometryLayers[1];
            var reassignedPathIds = source.PathIds.Distinct(StringComparer.Ordinal).ToArray();
            var remaining = ordered
                .Where(layer => layer.Id != source.Id)
                .Select(layer => layer.Id == target.Id
                    ? layer with
                    {
                        PathIds = layer.PathIds
                            .Concat(reassignedPathIds)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                    }
                    : layer)
                .OrderBy(layer => layer.Order)
                .Select((layer, order) => layer with { Order = order })
                .ToArray();

            Apply(_state with
            {
                Layers = remaining,
                ActiveLayerId = ActiveLayerId == source.Id ? target.Id : ActiveLayerId,
            });
            return true;
        }

        var nextLayers = ordered
            .Where(layer => layer.Id != source.Id)
            .OrderBy(layer => layer.Order)
            .Select((layer, order) => layer with { Order = order })
            .ToArray();
        if (nextLayers.Length == 0)
            return false;

        var fallbackIndex = Math.Min(sourceIndex, nextLayers.Length - 1);
        var fallbackActiveLayerId = ActiveLayerId == source.Id
            ? nextLayers[fallbackIndex].Id
            : ActiveLayerId;

        Apply(_state with
        {
            Layers = nextLayers,
            ActiveLayerId = fallbackActiveLayerId,
        });
        return true;
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

        CommitReferenceImageTransformEdit();
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
        BeginReferenceImageTransformEdit(layer.Id);
        return layer;
    }

    public bool UpdateReferenceImageTransform(
        string layerId,
        double x,
        double y,
        double width,
        double height,
        double rotationDegrees)
        => UpdateReferenceImageAndRefreshTrace(layerId, image => image with
        {
            X = x,
            Y = y,
            Width = Math.Max(0.001, width),
            Height = Math.Max(0.001, height),
            RotationDegrees = NormalizeRotation(rotationDegrees),
        });

    public bool BeginReferenceImageTransformEdit(string layerId)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId && candidate.IsReferenceImage);
        if (layer?.ReferenceImage is null || !layer.IsVisible || layer.IsLocked)
            return false;
        if (_referenceImageTransformLayerId == layerId && _referenceImageTransformOrigin is not null)
            return true;

        CommitReferenceImageTransformEdit();
        _referenceImageTransformOrigin = _state;
        _referenceImageTransformLayerId = layerId;
        OnPropertyChanged(nameof(IsReferenceImageTransformEditActive));
        return true;
    }

    public bool CommitReferenceImageTransformEdit()
    {
        if (_referenceImageTransformOrigin is not { } origin)
            return false;

        _referenceImageTransformOrigin = null;
        _referenceImageTransformLayerId = null;
        OnPropertyChanged(nameof(IsReferenceImageTransformEditActive));
        if (Equals(origin, _state))
            return false;

        _undo.Push(origin);
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        return true;
    }

    public bool CancelReferenceImageTransformEdit()
    {
        if (_referenceImageTransformOrigin is not { } origin)
            return false;

        var layerId = _referenceImageTransformLayerId;
        _referenceImageTransformOrigin = null;
        _referenceImageTransformLayerId = null;
        OnPropertyChanged(nameof(IsReferenceImageTransformEditActive));
        if (Equals(origin, _state))
            return false;

        Apply(origin, recordHistory: false);
        if (layerId is not null && _referenceImageTraceLayerId == layerId)
            RefreshReferenceImageTracePreview();
        return true;
    }

    public bool CalibrateReferenceImage(string layerId, double realWorldWidth)
        => UpdateReferenceImageAndRefreshTrace(layerId, image =>
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

    public bool CalibrateReferenceImageDistance(string layerId, double measuredDistance, double targetDistance)
    {
        if (!double.IsFinite(measuredDistance) || measuredDistance <= 1e-9
            || !double.IsFinite(targetDistance) || targetDistance <= 0.0)
            return false;
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId && candidate.IsReferenceImage);
        if (layer is null || !layer.IsVisible || layer.IsLocked)
            return false;
        var ratio = targetDistance / measuredDistance;
        var image = layer.ReferenceImage!;
        var width = image.Width * ratio;
        var height = image.Height * ratio;
        var unitsPerPixel = image.CalibrationUnitsPerPixel * ratio;
        if (!double.IsFinite(ratio) || ratio <= 0.0
            || !double.IsFinite(width) || width <= 0.0
            || !double.IsFinite(height) || height <= 0.0
            || !double.IsFinite(unitsPerPixel) || unitsPerPixel <= 0.0)
            return false;
        return UpdateReferenceImageAndRefreshTrace(layerId, image => image with
        {
            Width = width,
            Height = height,
            CalibrationUnitsPerPixel = unitsPerPixel,
        });
    }

    public bool SetReferenceImageOpacity(string layerId, double opacity)
        => UpdateReferenceImage(layerId, image => image with { Opacity = Math.Clamp(opacity, 0.0, 1.0) });

    public bool SetReferenceImageDepth(string layerId, Editor2DReferenceImageDepth depth)
    {
        if (!Enum.IsDefined(depth))
            return false;

        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId && candidate.IsReferenceImage);
        if (layer?.ReferenceImage is null || layer.IsLocked || layer.ReferenceImage.Depth == depth)
            return false;

        return UpdateReferenceImage(layerId, image => image with { Depth = depth });
    }

    public bool RemoveReferenceImageBackground(string layerId)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId && candidate.IsReferenceImage);
        var image = layer?.ReferenceImage;
        if (layer is null || image is null || layer.IsLocked || _referenceImageBackgroundRemovalService is null)
            return false;

        var original = image.OriginalDataBase64 ?? image.DataBase64;
        var removed = _referenceImageBackgroundRemovalService.RemoveBackground(original);
        if (string.IsNullOrWhiteSpace(removed))
            return false;

        return UpdateReferenceImageAndRefreshTrace(layerId, current => current with
        {
            DataBase64 = removed,
            OriginalDataBase64 = original,
            BackgroundRemoved = true,
        });
    }

    public bool RestoreReferenceImageBackground(string layerId)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId && candidate.IsReferenceImage);
        var image = layer?.ReferenceImage;
        if (layer is null || image is null || layer.IsLocked || string.IsNullOrWhiteSpace(image.OriginalDataBase64))
            return false;

        return UpdateReferenceImageAndRefreshTrace(layerId, current => current with
        {
            DataBase64 = current.OriginalDataBase64!,
            OriginalDataBase64 = null,
            BackgroundRemoved = false,
        });
    }

    public bool SetReferenceImageTraceThreshold(string layerId, double threshold)
        => SetReferenceImageTraceOptions(layerId, image => image with
        {
            TraceThreshold = Math.Clamp(threshold, 0.0, 1.0),
        });

    public bool SetReferenceImageTraceOptions(
        string layerId,
        Func<Editor2DReferenceImage, Editor2DReferenceImage> update)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId && candidate.IsReferenceImage);
        if (layer?.ReferenceImage is null || layer.IsLocked)
            return false;

        var candidateImage = update(layer.ReferenceImage);
        var updatedImage = candidateImage with
        {
            TraceThreshold = double.IsFinite(candidateImage.TraceThreshold)
                ? Math.Clamp(candidateImage.TraceThreshold, 0.0, 1.0)
                : 0.5,
            TraceTolerance = double.IsFinite(candidateImage.TraceTolerance)
                ? Math.Clamp(candidateImage.TraceTolerance, 1.0, 100.0)
                : 50.0,
            TraceCornerSmoothness = double.IsFinite(candidateImage.TraceCornerSmoothness)
                ? Math.Clamp(candidateImage.TraceCornerSmoothness, 0.0, 100.0)
                : 50.0,
            TracePathOptimization = double.IsFinite(candidateImage.TracePathOptimization)
                ? Math.Clamp(candidateImage.TracePathOptimization, 0.0, 100.0)
                : 50.0,
        };
        if (Equals(layer.ReferenceImage, updatedImage))
            return false;

        Apply(_state with
        {
            Layers = Layers.Select(candidate => candidate.Id == layerId
                ? candidate with { ReferenceImage = updatedImage, PathIds = [] }
                : candidate).ToArray(),
        }, recordHistory: false);
        if (_referenceImageTraceLayerId == layerId)
            RefreshReferenceImageTracePreview();
        return true;
    }

    public bool BeginReferenceImageTrace(string layerId)
    {
        var sourceLayer = Layers.FirstOrDefault(layer => layer.Id == layerId && layer.IsReferenceImage);
        if (sourceLayer?.ReferenceImage is null
            || sourceLayer.IsLocked
            || !sourceLayer.IsVisible
            || _referenceImageTraceService is null)
            return false;

        CommitReferenceImageTransformEdit();
        _referenceImageTraceLayerId = layerId;
        RefreshReferenceImageTracePreview();
        return true;
    }

    public bool RefreshReferenceImageTracePreview()
    {
        var sourceLayer = Layers.FirstOrDefault(layer => layer.Id == _referenceImageTraceLayerId && layer.IsReferenceImage);
        var image = sourceLayer?.ReferenceImage;
        if (sourceLayer is null
            || image is null
            || sourceLayer.IsLocked
            || !sourceLayer.IsVisible
            || _referenceImageTraceService is null)
        {
            CancelReferenceImageTrace();
            return false;
        }

        var options = new Editor2DReferenceImageTraceOptions(
            image.TraceThreshold,
            image.TraceTolerance,
            image.TraceCornerSmoothness,
            image.TracePathOptimization,
            image.TraceSilhouetteOnly);
        _referenceImageTraceCancellation?.Cancel();
        _referenceImageTraceCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _referenceImageTraceCancellation = cancellation;
        var generation = ++_referenceImageTraceGeneration;
        _referenceImageTracePreviewPaths = [];
        _isReferenceImageTracePreviewPending = true;
        RaiseReferenceImageTraceChanged();
        _referenceImageTraceTask = RefreshReferenceImageTracePreviewAsync(
            sourceLayer,
            image,
            options,
            generation,
            cancellation);
        return true;
    }

    public Task<bool> WaitForReferenceImageTracePreviewAsync()
        => _referenceImageTraceTask;

    private async Task<bool> RefreshReferenceImageTracePreviewAsync(
        Editor2DLayer sourceLayer,
        Editor2DReferenceImage image,
        Editor2DReferenceImageTraceOptions options,
        long generation,
        CancellationTokenSource cancellation)
    {
        IReadOnlyList<IReadOnlyList<Editor2DPoint>> contours;
        try
        {
            contours = await Task.Run(
                () => _referenceImageTraceService!.TraceContours(image.DataBase64, options),
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            contours = [];
        }

        if (cancellation.IsCancellationRequested
            || generation != _referenceImageTraceGeneration
            || _referenceImageTraceLayerId != sourceLayer.Id)
            return false;
        if (!Equals(
                Layers.FirstOrDefault(layer => layer.Id == sourceLayer.Id)?.ReferenceImage,
                image))
        {
            _isReferenceImageTracePreviewPending = false;
            if (ReferenceEquals(_referenceImageTraceCancellation, cancellation))
            {
                _referenceImageTraceCancellation = null;
                cancellation.Dispose();
            }
            RaiseReferenceImageTraceChanged();
            return false;
        }

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

        _referenceImageTracePreviewPaths = contours
            .Where(contour => contour.Count >= 3)
            .Select((contour, index) => new Editor2DPreviewPath(
                $"trace-preview-{sourceLayer.Id}-{index}",
                "REFERENCE_TRACE",
                contour.Select(Transform).ToArray(),
                IsClosed: true))
            .ToArray();
        _isReferenceImageTracePreviewPending = false;
        if (ReferenceEquals(_referenceImageTraceCancellation, cancellation))
        {
            _referenceImageTraceCancellation = null;
            cancellation.Dispose();
        }
        RaiseReferenceImageTraceChanged();
        return _referenceImageTracePreviewPaths.Count > 0;
    }

    public Editor2DPreviewPath? CommitReferenceImageTrace()
    {
        var sourceLayer = Layers.FirstOrDefault(layer => layer.Id == _referenceImageTraceLayerId && layer.IsReferenceImage);
        if (_isReferenceImageTracePreviewPending
            || sourceLayer?.ReferenceImage is null
            || _referenceImageTracePreviewPaths.Count == 0)
            return null;

        var traces = _referenceImageTracePreviewPaths.Select(path => path with
        {
            Id = $"trace-{Guid.NewGuid():N}",
        }).ToArray();
        var targetNameBase = Path.GetFileNameWithoutExtension(sourceLayer.Name);
        var targetName = $"{(string.IsNullOrWhiteSpace(targetNameBase) ? "Reference" : targetNameBase)}_traced";
        var layers = Layers.OrderBy(layer => layer.Order).ToList();
        var targetIndex = layers.FindIndex(layer => layer.Kind == Editor2DLayerKind.Geometry
            && !layer.IsLocked
            && layer.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (targetIndex < 0)
        {
            layers.Add(new Editor2DLayer(
                Guid.NewGuid().ToString("N"),
                targetName,
                traces.Select(trace => trace.Id).ToArray(),
                Order: layers.Count,
                ColorHex: "#22C55E"));
            targetIndex = layers.Count - 1;
        }
        else
        {
            layers[targetIndex] = layers[targetIndex] with
            {
                PathIds = layers[targetIndex].PathIds.Concat(traces.Select(trace => trace.Id)).ToArray(),
            };
        }
        var targetLayerId = layers[targetIndex].Id;
        layers = layers.Select(layer => layer.Id == sourceLayer.Id
            ? layer with { IsVisible = false }
            : layer).ToList();

        Apply(_state with
        {
            Document = RebuildDocument(_state.Document, _state.Document.Paths.Concat(traces).ToArray()),
            Layers = layers,
            ActiveLayerId = targetLayerId,
            SelectedPathIds = traces.Select(trace => trace.Id).ToArray(),
        });
        var first = traces[0];
        CancelReferenceImageTrace();
        return first;
    }

    public void CancelReferenceImageTrace()
    {
        if (_referenceImageTraceLayerId is null
            && _referenceImageTracePreviewPaths.Count == 0
            && !_isReferenceImageTracePreviewPending)
            return;
        _referenceImageTraceGeneration++;
        _referenceImageTraceCancellation?.Cancel();
        _referenceImageTraceCancellation?.Dispose();
        _referenceImageTraceCancellation = null;
        _referenceImageTraceLayerId = null;
        _referenceImageTracePreviewPaths = [];
        _isReferenceImageTracePreviewPending = false;
        RaiseReferenceImageTraceChanged();
    }

    private void RaiseReferenceImageTraceChanged()
    {
        OnPropertyChanged(nameof(ReferenceImageTracePreviewPaths));
        OnPropertyChanged(nameof(ReferenceImageTraceLayerId));
        OnPropertyChanged(nameof(HasReferenceImageTraceSession));
        OnPropertyChanged(nameof(ReferenceImageTraceSource));
        OnPropertyChanged(nameof(IsReferenceImageTracePreviewPending));
        ReferenceImageTraceChanged?.Invoke();
    }

    public async Task<Editor2DPreviewPath?> TraceReferenceImageBoundsAsync(string layerId)
    {
        if (!BeginReferenceImageTrace(layerId))
            return null;
        await WaitForReferenceImageTracePreviewAsync();
        var trace = CommitReferenceImageTrace();
        if (trace is null)
            CancelReferenceImageTrace();
        return trace;
    }

    public event Action? ReferenceImageTraceChanged;

    public bool SelectLayer(string layerId)
    {
        var layer = Layers.FirstOrDefault(candidate => candidate.Id == layerId);
        if (layer is null)
            return false;

        if (_referenceImageTransformLayerId != layerId)
            CommitReferenceImageTransformEdit();
        var documentPathIds = Document.Paths.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var selectedPathIds = layer.PathIds.Where(documentPathIds.Contains).ToArray();
        Apply(
            _state with
            {
                ActiveLayerId = layer.Id,
                SelectedPathIds = selectedPathIds,
                ActiveTool = !layer.IsReferenceImage && selectedPathIds.Length > 0
                    ? Editor2DTool.Select
                    : ActiveTool,
            },
            recordHistory: false);
        if (layer.IsReferenceImage && layer.IsVisible && !layer.IsLocked)
            BeginReferenceImageTransformEdit(layer.Id);
        return true;
    }

    public bool ToggleLayerVisibility(string layerId)
    {
        if (_referenceImageTransformLayerId == layerId)
            CommitReferenceImageTransformEdit();
        var changed = UpdateLayer(layerId, layer => layer with { IsVisible = !layer.IsVisible });
        if (changed && _referenceImageTraceLayerId == layerId)
        {
            if (Layers.First(layer => layer.Id == layerId).IsVisible)
                RefreshReferenceImageTracePreview();
            else
                CancelReferenceImageTrace();
        }
        return changed;
    }

    public bool ToggleLayerLock(string layerId)
    {
        if (_referenceImageTransformLayerId == layerId)
            CommitReferenceImageTransformEdit();
        var changed = UpdateLayer(layerId, layer => layer with { IsLocked = !layer.IsLocked });
        if (changed && _referenceImageTraceLayerId == layerId
            && Layers.First(layer => layer.Id == layerId).IsLocked)
            CancelReferenceImageTrace();
        return changed;
    }

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

    public bool MergeSelectedLayers()
    {
        var selectedPathIds = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var selectedLayers = Layers
            .Where(layer => layer.Kind == Editor2DLayerKind.Geometry
                && layer.PathIds.Any(selectedPathIds.Contains))
            .OrderBy(layer => layer.Order)
            .ToArray();
        if (selectedLayers.Length < 2 || selectedLayers.Any(layer => layer.IsLocked))
            return false;

        var target = selectedLayers[0];
        var mergedPathIds = selectedLayers
            .SelectMany(layer => layer.PathIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selectedIds = selectedLayers.Select(layer => layer.Id).ToHashSet(StringComparer.Ordinal);
        var layers = Layers
            .Where(layer => !selectedIds.Contains(layer.Id))
            .Append(target with { PathIds = mergedPathIds })
            .OrderBy(layer => layer.Order)
            .Select((layer, order) => layer with { Order = order })
            .ToArray();
        Apply(_state with
        {
            Layers = layers,
            ActiveLayerId = target.Id,
            SelectedPathIds = mergedPathIds,
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

    public bool IsCornerToolSessionActive => _cornerToolSession is not null;

    public string? BeginCornerToolSession(
        Editor2DCornerKind kind,
        Editor2DFilletContinuity continuity = Editor2DFilletContinuity.G1)
    {
        if (_cornerToolSession is not null)
            return _cornerToolSession.ActiveParameterId;

        _cornerToolSession = new CornerToolSession(_state, _undo.ToArray(), _redo.ToArray());
        var selectedIds = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var path = Document.Paths.FirstOrDefault(candidate =>
            selectedIds.Contains(candidate.Id)
            && candidate.Points.Count >= 3
            && (candidate.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
                || candidate.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase)));
        if (path is null)
            return null;

        var existingForPath = CornerParameters.Where(parameter => parameter.PathId == path.Id).ToArray();
        var sourcePoints = existingForPath.FirstOrDefault()?.SourcePoints ?? path.Points;
        var targetIndices = path.IsClosed
            ? Enumerable.Range(0, sourcePoints.Count).ToArray()
            : Enumerable.Range(1, Math.Max(sourcePoints.Count - 2, 0)).ToArray();
        if (targetIndices.Length == 0)
            return null;

        var existingByIndex = existingForPath.ToDictionary(parameter => parameter.CornerIndex);
        var seeded = targetIndices.Select(index =>
        {
            if (existingByIndex.TryGetValue(index, out var existing))
                return existing with { Kind = kind };
            return new Editor2DCornerParameter(
                $"{path.Id}:{index}",
                path.Id,
                index,
                kind,
                DefaultCornerPreset(sourcePoints, index),
                sourcePoints.ToArray(),
                continuity);
        }).ToArray();
        var otherParameters = CornerParameters.Where(parameter => parameter.PathId != path.Id).ToArray();
        var nextParameters = otherParameters.Concat(seeded).ToArray();
        var rendered = Editor2DCornerGeometry.Apply(path with { Points = sourcePoints }, nextParameters);
        Apply(_state with
        {
            Document = RebuildDocument(Document, Document.Paths.Select(candidate => candidate.Id == path.Id ? rendered : candidate).ToArray()),
            CornerParameters = nextParameters,
            SelectedPathIds = [path.Id],
        }, recordHistory: false);
        _cornerToolSession.HasChanges = !Equals(_cornerToolSession.State, _state);
        _cornerToolSession.ActiveParameterId = seeded[^1].Id;
        return seeded[^1].Id;
    }

    public bool ConfirmCornerToolSession()
    {
        var session = _cornerToolSession;
        if (session is null)
            return false;
        _cornerToolSession = null;
        RestoreHistory(_undo, session.Undo);
        if (session.HasChanges)
        {
            _undo.Push(session.State);
            _redo.Clear();
        }
        else
        {
            RestoreHistory(_redo, session.Redo);
        }
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        return true;
    }

    public bool CancelCornerToolSession()
    {
        var session = _cornerToolSession;
        if (session is null)
            return false;
        _cornerToolSession = null;
        _state = Normalize(session.State);
        RestoreHistory(_undo, session.Undo);
        RestoreHistory(_redo, session.Redo);
        RemoveMissingMirrorLinks();
        RaiseStateChanged();
        return true;
    }

    public bool UpsertCornerParameter(Editor2DCornerParameter parameter)
    {
        if (parameter.Value < 0 || !double.IsFinite(parameter.Value))
            return false;
        var path = Document.Paths.FirstOrDefault(item => item.Id == parameter.PathId);
        if (path is null || parameter.SourcePoints.Count < 3)
            return false;

        var nextParameters = CornerParameters
            .Where(item => item.Id != parameter.Id)
            .Append(parameter)
            .ToArray();
        var rendered = Editor2DCornerGeometry.Apply(path with { Points = parameter.SourcePoints }, nextParameters);
        Apply(_state with
        {
            Document = RebuildDocument(Document, Document.Paths.Select(item => item.Id == path.Id ? rendered : item).ToArray()),
            CornerParameters = nextParameters,
            SelectedPathIds = [path.Id],
        }, recordHistory: _cornerToolSession is null);
        if (_cornerToolSession is not null)
        {
            _cornerToolSession.HasChanges = true;
            _cornerToolSession.ActiveParameterId = parameter.Id;
        }
        return true;
    }

    public bool UpdateCornerParameter(string parameterId, double value)
    {
        var parameter = CornerParameters.FirstOrDefault(item => item.Id == parameterId);
        if (parameter is null || value < 0 || !double.IsFinite(value))
            return false;
        return UpsertCornerParameter(parameter with { Value = value });
    }

    public bool UpdateCornerParameterContinuity(string parameterId, Editor2DFilletContinuity continuity)
    {
        var parameter = CornerParameters.FirstOrDefault(item => item.Id == parameterId);
        if (parameter is null || parameter.Kind != Editor2DCornerKind.Fillet)
            return false;

        var path = Document.Paths.FirstOrDefault(item => item.Id == parameter.PathId);
        if (path is null)
            return false;
        var nextParameters = CornerParameters
            .Select(item => item.PathId == parameter.PathId && item.Kind == Editor2DCornerKind.Fillet
                ? item with { Continuity = continuity }
                : item)
            .ToArray();
        var sourcePoints = parameter.SourcePoints;
        var rendered = Editor2DCornerGeometry.Apply(path with { Points = sourcePoints }, nextParameters);
        Apply(_state with
        {
            Document = RebuildDocument(Document, Document.Paths.Select(item => item.Id == path.Id ? rendered : item).ToArray()),
            CornerParameters = nextParameters,
            SelectedPathIds = [path.Id],
        }, recordHistory: _cornerToolSession is null);
        if (_cornerToolSession is not null)
            _cornerToolSession.HasChanges = true;
        return true;
    }

    private static double DefaultCornerPreset(IReadOnlyList<Editor2DPoint> points, int index)
    {
        var previous = points[index == 0 ? points.Count - 1 : index - 1];
        var current = points[index];
        var next = points[index == points.Count - 1 ? 0 : index + 1];
        var maxFit = Math.Min(CornerDistance(previous, current), CornerDistance(current, next)) * 0.5;
        return new[] { 10.0, 5.0, 2.0 }.FirstOrDefault(preset => preset <= maxFit);
    }

    private static double CornerDistance(Editor2DPoint left, Editor2DPoint right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));

    private static void RestoreHistory(
        Stack<Editor2DWorkspaceState> target,
        IReadOnlyList<Editor2DWorkspaceState> topFirst)
    {
        target.Clear();
        foreach (var state in topFirst.Reverse())
            target.Push(state);
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
        EndMeasurementEdit();
        CommitReferenceImageTransformEdit();
        CancelReferenceImageTrace();
        if (_undo.Count == 0)
            return false;

        _redo.Push(_state);
        _state = _undo.Pop();
        RemoveMissingMirrorLinks();
        RaiseStateChanged();
        return true;
    }

    public bool Redo()
    {
        EndMeasurementEdit();
        CommitReferenceImageTransformEdit();
        CancelReferenceImageTrace();
        if (_redo.Count == 0)
            return false;

        _undo.Push(_state);
        _state = _redo.Pop();
        RemoveMissingMirrorLinks();
        RaiseStateChanged();
        return true;
    }

    public void ClearHistory()
    {
        _measurementEditOrigin = null;
        var transformEditWasActive = _referenceImageTransformOrigin is not null;
        _referenceImageTransformOrigin = null;
        _referenceImageTransformLayerId = null;
        if (transformEditWasActive)
            OnPropertyChanged(nameof(IsReferenceImageTransformEditActive));
        if (_undo.Count == 0 && _redo.Count == 0)
            return;

        _undo.Clear();
        _redo.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private static Editor2DWorkspaceState Normalize(
        Editor2DWorkspaceState state,
        bool rebuildMeasurementCaches = true)
    {
        var pathIds = state.Document.Paths.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var selectedPathIds = (state.SelectedPathIds ?? [])
            .Where(pathIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var measurements = (state.Measurements ?? [])
            .Where(measurement => measurement.EntityPathId is null || pathIds.Contains(measurement.EntityPathId))
            .Select(measurement => measurement.EvaluatedValue is { } cached
                && (!double.IsFinite(cached) || cached <= 0.0)
                    ? measurement with { EvaluatedValue = null }
                    : measurement)
            .ToArray();
        if (rebuildMeasurementCaches)
        {
            measurements = TryResolveMeasurementGraph(measurements, out var values, out _)
                ? ApplyResolvedMeasurementValues(measurements, values, updateEndpoints: false)
                : measurements.Select(measurement => measurement with { EvaluatedValue = null }).ToArray();
        }
        var selectedMeasurementId = measurements.Any(item => item.Id == state.SelectedMeasurementId)
            ? state.SelectedMeasurementId
            : null;

        var folders = NormalizeFolders(state.Folders);
        var layers = NormalizeLayers(state with { Folders = folders }, pathIds);
        var convertLineGroups = NormalizeConvertLineGroups(state.ConvertLineGroups, pathIds, layers);
        var importGroups = NormalizeImportGroups(state.ImportGroups, pathIds, layers, state.Document.Paths);
        var importedUnsupportedEntityTypes = importGroups
            .SelectMany(group => group.UnsupportedEntityTypes ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseUnsupportedEntityTypes = (state.BaseUnsupportedEntityTypes
                ?? state.Document.UnsupportedEntityTypes.Where(type => !importedUnsupportedEntityTypes.Contains(type)).ToArray())
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unsupportedEntityTypes = baseUnsupportedEntityTypes
            .Concat(importedUnsupportedEntityTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var document = state.Document.UnsupportedEntityTypes.SequenceEqual(
            unsupportedEntityTypes,
            StringComparer.OrdinalIgnoreCase)
                ? state.Document
                : state.Document with { UnsupportedEntityTypes = unsupportedEntityTypes };
        var activeLayerId = layers.Any(layer => layer.Id == state.ActiveLayerId)
            ? state.ActiveLayerId
            : layers.FirstOrDefault()?.Id;

        return state with
        {
            Document = document,
            SelectedPathIds = selectedPathIds,
            Measurements = measurements,
            SelectedMeasurementId = selectedMeasurementId,
            PolygonSides = Math.Clamp(state.PolygonSides, 3, 128),
            ExpandedRectanglePathIds = (state.ExpandedRectanglePathIds ?? [])
                .Where(pathIds.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Layers = layers,
            Folders = folders,
            ActiveLayerId = activeLayerId,
            CornerParameters = (state.CornerParameters ?? [])
                .Where(parameter => pathIds.Contains(parameter.PathId)
                    && parameter.CornerIndex >= 0
                    && parameter.Value >= 0
                    && double.IsFinite(parameter.Value))
                .DistinctBy(parameter => parameter.Id, StringComparer.Ordinal)
                .ToArray(),
            SewingHoleParameters = NormalizeSewingParameters(state.SewingHoleParameters, pathIds),
            SewingHoleOperations = (state.SewingHoleOperations ?? [])
                .Where(operation => operation.SourcePathIds.All(pathIds.Contains))
                .Select(operation => operation with
                {
                    GeneratedPathIds = operation.GeneratedPathIds.Where(pathIds.Contains).ToArray(),
                    Parameters = NormalizeSewingParameters(operation.Parameters, pathIds),
                })
                .ToArray(),
            ConvertLineGroups = convertLineGroups,
            ImportGroups = importGroups,
            BaseUnsupportedEntityTypes = baseUnsupportedEntityTypes,
        };
    }

    private static IReadOnlyList<Editor2DImportGroup> NormalizeImportGroups(
        IReadOnlyList<Editor2DImportGroup>? groups,
        IReadOnlySet<string> documentPathIds,
        IReadOnlyList<Editor2DLayer> layers,
        IReadOnlyList<Editor2DPreviewPath> documentPaths)
    {
        var normalized = new List<Editor2DImportGroup>();
        var claimedGeneratedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in groups ?? [])
        {
            if (group is null
                || string.IsNullOrWhiteSpace(group.Id)
                || string.IsNullOrWhiteSpace(group.SourceFilePath)
                || !double.IsFinite(group.AppliedUnitScale)
                || group.AppliedUnitScale <= 0.0
                || group.GeneratedPathIds is null)
                continue;

            var normalizedId = group.Id.Trim();
            if (normalized.Any(existing => existing.Id == normalizedId))
                continue;

            var generatedIds = group.GeneratedPathIds
                .Where(id => !string.IsNullOrWhiteSpace(id) && documentPathIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .Where(claimedGeneratedIds.Add)
                .ToArray();
            if (generatedIds.Length == 0)
                continue;

            var owningLayer = layers.FirstOrDefault(layer =>
                layer.Kind == Editor2DLayerKind.Geometry
                && generatedIds.Any(id => layer.PathIds.Contains(id, StringComparer.Ordinal)));
            if (owningLayer is null)
                continue;

            var owningLayerPathIndex = owningLayer.PathIds
                .Select((id, index) => (id, index))
                .Where(item => generatedIds.Contains(item.id, StringComparer.Ordinal))
                .Select(item => item.index)
                .DefaultIfEmpty(0)
                .Min();
            var documentPathIndex = documentPaths
                .Select((path, index) => (path.Id, index))
                .Where(item => generatedIds.Contains(item.Id, StringComparer.Ordinal))
                .Select(item => item.index)
                .DefaultIfEmpty(0)
                .Min();

            normalized.Add(group with
            {
                Id = normalizedId,
                SourceFilePath = group.SourceFilePath.Trim(),
                GeneratedPathIds = generatedIds,
                OwningLayerId = owningLayer.Id,
                OwningLayerPathIndex = owningLayerPathIndex,
                DocumentPathIndex = documentPathIndex,
                UnsupportedEntityTypes = (group.UnsupportedEntityTypes ?? [])
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .Select(type => type.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            });
        }

        return normalized;
    }

    private static IReadOnlyList<Editor2DConvertLineGroup> NormalizeConvertLineGroups(
        IReadOnlyList<Editor2DConvertLineGroup>? groups,
        IReadOnlySet<string> documentPathIds,
        IReadOnlyList<Editor2DLayer> layers)
    {
        var layerIds = layers.Where(layer => layer.Kind == Editor2DLayerKind.Geometry)
            .Select(layer => layer.Id)
            .ToHashSet(StringComparer.Ordinal);
        var claimedGeneratedIds = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<Editor2DConvertLineGroup>();
        foreach (var group in groups ?? [])
        {
            if (group is null || string.IsNullOrWhiteSpace(group.Id))
                continue;
            var groupId = group.Id.Trim();
            if (normalized.Any(existing => existing.Id == groupId)
                || group.Settings is null
                || group.Sources is null
                || group.Settings.Any(setting => string.IsNullOrWhiteSpace(setting.Key) || !double.IsFinite(setting.Value)))
                continue;

            var style = !string.IsNullOrWhiteSpace(group.Style) && SupportedConvertLineStyles.Contains(group.Style)
                ? group.Style.Trim().ToLowerInvariant()
                : "dashed";
            var settings = CanonicalizeConvertLineSettings(style, group.Settings);

            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var sources = new List<Editor2DConvertLineSource>();
            foreach (var source in group.Sources ?? [])
            {
                if (source is null
                    || source.SourcePath is null
                    || string.IsNullOrWhiteSpace(source.SourcePath.Id)
                    || string.IsNullOrWhiteSpace(source.SourcePath.EntityType)
                    || source.SourcePath.Points is null
                    || source.GeneratedPathIds is null
                    || !sourceIds.Add(source.SourcePath.Id)
                    || !Editor2DGeometry.IsConvertibleLinePath(source.SourcePath))
                    continue;

                var generatedIds = source.GeneratedPathIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Where(documentPathIds.Contains)
                    .Where(claimedGeneratedIds.Add)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (generatedIds.Length == 0)
                    continue;

                sources.Add(source with
                {
                    SourceLayerId = source.SourceLayerId is not null && layerIds.Contains(source.SourceLayerId)
                        ? source.SourceLayerId
                        : null,
                    SourceLayerPathIndex = Math.Max(0, source.SourceLayerPathIndex),
                    SourceDocumentPathIndex = Math.Max(0, source.SourceDocumentPathIndex),
                    GeneratedPathIds = generatedIds,
                });
            }

            if (sources.Count == 0)
                continue;
            normalized.Add(group with
            {
                Id = groupId,
                Style = style,
                Settings = settings,
                Sources = sources,
            });
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, double> CanonicalizeConvertLineSettings(
        string style,
        IReadOnlyDictionary<string, double> settings)
    {
        var suppliedSettings = new Dictionary<string, double>(settings, StringComparer.OrdinalIgnoreCase);
        return ConvertLineSettings[style].ToDictionary(
            definition => definition.Key,
            definition =>
            {
                var value = suppliedSettings.TryGetValue(definition.Key, out var supplied) && double.IsFinite(supplied)
                    ? Math.Max(supplied, definition.Minimum)
                    : definition.Default;
                return definition.Integer ? Math.Round(value) : value;
            },
            StringComparer.OrdinalIgnoreCase);
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

        var layers = Layers.Select(candidate => candidate.Id == layerId
            ? candidate with
            {
                ReferenceImage = update(candidate.ReferenceImage!),
                PathIds = [],
            }
            : candidate).ToArray();
        Apply(_state with
        {
            Layers = layers,
        }, recordHistory: _referenceImageTransformOrigin is null || _referenceImageTransformLayerId != layerId);
        return true;
    }

    private bool UpdateReferenceImageAndRefreshTrace(
        string layerId,
        Func<Editor2DReferenceImage, Editor2DReferenceImage> update)
    {
        var changed = UpdateReferenceImage(layerId, update);
        if (changed && _referenceImageTraceLayerId == layerId)
            RefreshReferenceImageTracePreview();
        return changed;
    }

    private static double NormalizeRotation(double rotationDegrees)
    {
        if (!double.IsFinite(rotationDegrees))
            return 0.0;
        var normalized = rotationDegrees % 360.0;
        return normalized < -180.0 ? normalized + 360.0 : normalized > 180.0 ? normalized - 360.0 : normalized;
    }

    private static bool IsValidColorHex(string colorHex)
        => (colorHex.Length is 7 or 9)
            && colorHex[0] == '#'
            && colorHex.Skip(1).All(Uri.IsHexDigit);

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
                ParentFolderId = state.Folders?.Any(folder => folder.Id == layer.ParentFolderId) == true
                    ? layer.ParentFolderId
                    : null,
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

    private static IReadOnlyList<Editor2DLayerFolder> NormalizeFolders(IReadOnlyList<Editor2DLayerFolder>? source)
    {
        var folders = (source ?? [])
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
            .DistinctBy(folder => folder.Id, StringComparer.Ordinal)
            .Select((folder, index) => folder with
            {
                Name = string.IsNullOrWhiteSpace(folder.Name) ? $"Folder {index + 1}" : folder.Name.Trim(),
            })
            .ToDictionary(folder => folder.Id, StringComparer.Ordinal);
        foreach (var folder in folders.Values.ToArray())
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { folder.Id };
            var parent = folder.ParentFolderId;
            while (parent is not null && folders.TryGetValue(parent, out var parentFolder))
            {
                if (!seen.Add(parent))
                {
                    folders[folder.Id] = folder with { ParentFolderId = null };
                    break;
                }
                parent = parentFolder.ParentFolderId;
            }
            if (parent is not null && !folders.ContainsKey(parent))
                folders[folder.Id] = folders[folder.Id] with { ParentFolderId = null };
        }
        return folders.Values.ToArray();
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
            TraceThreshold = double.IsFinite(image.TraceThreshold)
                ? Math.Clamp(image.TraceThreshold, 0.0, 1.0)
                : 0.5,
            TraceTolerance = double.IsFinite(image.TraceTolerance)
                ? Math.Clamp(image.TraceTolerance, 1.0, 100.0)
                : 50.0,
            TraceCornerSmoothness = double.IsFinite(image.TraceCornerSmoothness)
                ? Math.Clamp(image.TraceCornerSmoothness, 0.0, 100.0)
                : 50.0,
            TracePathOptimization = double.IsFinite(image.TracePathOptimization)
                ? Math.Clamp(image.TracePathOptimization, 0.0, 100.0)
                : 50.0,
            Depth = Enum.IsDefined(image.Depth) ? image.Depth : Editor2DReferenceImageDepth.Back,
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

    private static Editor2DSewingHoleParameters NormalizeSewingParameters(
        Editor2DSewingHoleParameters? parameters,
        IReadOnlySet<string>? livePathIds = null)
    {
        var value = parameters ?? Editor2DSewingHoleParameters.Default;
        var side = value.Side;
        if (value.Margin < 0)
        {
            side = side switch
            {
                Editor2DSewingSide.Left => Editor2DSewingSide.Right,
                Editor2DSewingSide.Right => Editor2DSewingSide.Left,
                _ => side,
            };
        }
        return value with
        {
            Diameter = Math.Max(0.02, value.Diameter),
            Pitch = Math.Max(0.1, value.Pitch),
            Margin = Math.Abs(value.Margin),
            Side = side,
            SaddleSpacing = Math.Max(0, value.SaddleSpacing),
            CornerClearance = Math.Max(0, value.CornerClearance),
            AvoidanceClearance = Math.Max(0, value.AvoidanceClearance),
            AvoidPathIds = (value.AvoidPathIds ?? [])
                .Where(id => livePathIds is null || livePathIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Count = Math.Max(1, value.Count),
            VariableSpacingMin = Math.Max(0.1, Math.Min(value.VariableSpacingMin, value.VariableSpacingMax)),
            VariableSpacingMax = Math.Max(0.1, Math.Max(value.VariableSpacingMin, value.VariableSpacingMax)),
        };
    }

    private void NotifySewingParametersChanged()
    {
        OnPropertyChanged(nameof(SewingHoleParameters));
        OnPropertyChanged(nameof(SewingHoleDiameter));
        OnPropertyChanged(nameof(SewingHolePitch));
        OnPropertyChanged(nameof(SewingHoleMargin));
        OnPropertyChanged(nameof(SewingPattern));
        OnPropertyChanged(nameof(SewingSide));
        OnPropertyChanged(nameof(SewingSideSelection));
        OnPropertyChanged(nameof(SewingSaddleSpacing));
        OnPropertyChanged(nameof(IsSaddleSewingPattern));
        OnPropertyChanged(nameof(SewingCornerMode));
        OnPropertyChanged(nameof(SewingCornerClearance));
        OnPropertyChanged(nameof(SewingAvoidanceEnabled));
        OnPropertyChanged(nameof(SewingAvoidanceClearance));
        OnPropertyChanged(nameof(SewingSymmetricDistribution));
        OnPropertyChanged(nameof(SewingDistributionMode));
        OnPropertyChanged(nameof(SewingHoleCount));
        OnPropertyChanged(nameof(SewingVariableSpacingEnabled));
        OnPropertyChanged(nameof(SewingVariableSpacingMin));
        OnPropertyChanged(nameof(SewingVariableSpacingMax));
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
        var usesRadialVocabulary = SewingUsesRadialSideVocabulary;
        if (usesRadialVocabulary != _sewingUsesRadialSideVocabulary)
        {
            _sewingUsesRadialSideVocabulary = usesRadialVocabulary;
            _sewingSideOptionItems[0] = usesRadialVocabulary ? "Outer" : "Left";
            _sewingSideOptionItems[1] = usesRadialVocabulary ? "Inner" : "Right";
        }

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(Document));
        OnPropertyChanged(nameof(IsInitialized));
        OnPropertyChanged(nameof(ActiveTool));
        OnPropertyChanged(nameof(SnapEnabled));
        OnPropertyChanged(nameof(IsSewingHoleToolActive));
        OnPropertyChanged(nameof(IsSewingHoleInspectorVisible));
        OnPropertyChanged(nameof(SelectedPathIds));
        OnPropertyChanged(nameof(SewingUsesRadialSideVocabulary));
        OnPropertyChanged(nameof(SewingSideOptionItems));
        OnPropertyChanged(nameof(SewingSideSelection));
        OnPropertyChanged(nameof(HasMirrorLinkSelection));
        OnPropertyChanged(nameof(Measurements));
        OnPropertyChanged(nameof(SelectedMeasurementId));
        OnPropertyChanged(nameof(Layers));
        OnPropertyChanged(nameof(Folders));
        OnPropertyChanged(nameof(ActiveLayerId));
        OnPropertyChanged(nameof(ActiveLayer));
        OnPropertyChanged(nameof(CornerParameters));
        OnPropertyChanged(nameof(SewingHoleParameters));
        OnPropertyChanged(nameof(SewingHoleOperations));
        OnPropertyChanged(nameof(ConvertLineGroups));
        OnPropertyChanged(nameof(ImportGroups));
        if (_selectedSewingHoleOperation is not null)
        {
            _selectedSewingHoleOperation = SewingHoleOperations.FirstOrDefault(operation => operation.Id == _selectedSewingHoleOperation.Id);
            OnPropertyChanged(nameof(SelectedSewingHoleOperation));
        }
        OnPropertyChanged(nameof(CanPreviewSewingHoles));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void AddMirrorLinks(
        IReadOnlyList<Editor2DPreviewPath> sources,
        IReadOnlyList<Editor2DPreviewPath> copies,
        Editor2DPoint axisStart,
        Editor2DPoint axisEnd,
        bool mirror)
    {
        for (var index = 0; index < Math.Min(sources.Count, copies.Count); index++)
        {
            var sourceId = sources[index].Id;
            var copyId = copies[index].Id;
            _mirrorLinks[sourceId] = new Editor2DMirrorLink(copyId, axisStart, axisEnd, mirror);
            _mirrorLinks[copyId] = new Editor2DMirrorLink(sourceId, axisStart, axisEnd, mirror);
        }
        RaiseMirrorLinksChanged();
    }

    private void RemoveMissingMirrorLinks()
    {
        if (_mirrorLinks.Count == 0)
            return;

        var pathIds = Document.Paths.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
        var remove = _mirrorLinks
            .Where(pair => !pathIds.Contains(pair.Key) || !pathIds.Contains(pair.Value.PartnerPathId))
            .SelectMany(pair => new[] { pair.Key, pair.Value.PartnerPathId })
            .ToHashSet(StringComparer.Ordinal);
        if (remove.Count == 0)
            return;

        foreach (var pathId in remove)
            _mirrorLinks.Remove(pathId);
        RaiseMirrorLinksChanged();
    }

    private void RaiseMirrorLinksChanged()
    {
        OnPropertyChanged(nameof(MirrorLinks));
        OnPropertyChanged(nameof(HasMirrorLinkSelection));
    }
}
