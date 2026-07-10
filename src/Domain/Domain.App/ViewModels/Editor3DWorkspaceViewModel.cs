using CommunityToolkit.Mvvm.ComponentModel;
using Domain.App.Models;
using Domain.App.Services;

namespace Domain.App.ViewModels;

/// <summary>
/// Owns the complete mutable 3D workspace independently from the editor shell.
/// The shell coordinates persistence and exported artifacts through the narrow
/// <see cref="CoordinationRequested"/> event instead of owning 3D selections.
/// </summary>
public sealed class Editor3DWorkspaceViewModel : ObservableObject
{
    private readonly IEditor3DOperationService _operationService;
    private readonly Queue<string> _pendingViewportScripts = new();
    private string _viewportStateText = "Booting viewport";
    private string _selectionSummary = "No selection";
    private string _lastViewportEvent = "None";
    private bool _viewportReady;
    private int _selectedFaceCount;
    private string? _viewportJsonContent;
    private string? _sourceModelPath;
    private string _distortionDataJson = string.Empty;
    private Editor3DTool _activeTool = Editor3DTool.Select;
    private bool _threeDOrthographic;
    private bool _isPlaneSelectionActive;
    private PlaneSelectionModeType _planeSelectionModeType = PlaneSelectionModeType.Origin;
    private string? _selectedProjectionPlane;
    private int? _selectedProjectionFaceIndex;
    private int? _selectedProjectionBodyIndex;
    private double _planeOffset;
    private string _planeOffsetText = "0";
    private bool _isPlaneOffsetTextValid = true;
    private string _projectionSelectionSummary = "Selected: None";
    private int? _selectedBodyIndex;
    private IReadOnlyList<Body3D> _bodies = [];
    private IReadOnlyList<SelectedFace3D> _selectedFaces = [];
    private IReadOnlyList<SelectedFaceDetails> _selectedFaceDetails = [];
    private IReadOnlyList<BodyOffset3D> _bodyOffsets = [];
    private int _bodyOffsetCount;
    private bool _isSelectedBodyOffsetXTextValid = true;
    private bool _isSelectedBodyOffsetYTextValid = true;
    private bool _isSelectedBodyOffsetZTextValid = true;
    private int _distortionModeIndex;
    private bool _liveRecomputeEnabled;
    private bool _wholeBodyRecompute;
    private string _selectedBodyOffsetXText = "0";
    private string _selectedBodyOffsetYText = "0";
    private string _selectedBodyOffsetZText = "0";
    private string _bodyMoveStepText = "1";
    private bool _isUpdatingBodyOffsetText;
    private IReadOnlyList<string> _pendingSourceModelPaths = [];
    private CancellationTokenSource? _distortionRefreshCancellationTokenSource;
    private CancellationTokenSource? _liveRecomputeCancellationTokenSource;

    public Editor3DWorkspaceViewModel(
        IEditor3DOperationService operationService,
        string viewportHtml,
        Uri viewportBaseUri,
        GeometryKernelDescriptor geometryKernel)
    {
        _operationService = operationService;
        ViewportHtml = viewportHtml;
        ViewportBaseUri = viewportBaseUri;
        GeometryKernel = geometryKernel;
    }

    public event EventHandler<Editor3DWorkspaceCoordinationEventArgs>? CoordinationRequested;
    public event Action<string>? ViewportScriptRequested;

    public string ViewportHtml { get; }
    public Uri ViewportBaseUri { get; }
    public GeometryKernelDescriptor GeometryKernel { get; }
    public Editor3DTool ActiveTool => _activeTool;
    public IReadOnlyList<Body3D> Bodies => _bodies;
    public IReadOnlyList<SelectedFace3D> SelectedFaces => _selectedFaces;
    public IReadOnlyList<SelectedFaceDetails> SelectedFaceDetails => _selectedFaceDetails;
    public IReadOnlyList<BodyOffset3D> BodyOffsets => _bodyOffsets;
    public int? SelectedBodyIndex => _selectedBodyIndex;
    public string? ViewportJsonContent => _viewportJsonContent;
    public string? SourceModelPath => _sourceModelPath;

    public bool ActivateTool(Editor3DTool tool)
    {
        if (!SetProperty(ref _activeTool, tool, nameof(ActiveTool)))
            return false;

        RequestCoordination(Editor3DWorkspaceCoordinationKind.PersistState);
        return true;
    }

    public void ReplaceBodies(IReadOnlyList<Body3D> bodies, string? viewportJson, string? sourceModelPath)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        UpdateBodies(bodies);
        UpdateViewportJson(viewportJson);
        SetProperty(ref _sourceModelPath, sourceModelPath, nameof(SourceModelPath));
        RequestCoordination(Editor3DWorkspaceCoordinationKind.PersistState);
    }

    public void SetFaceSelection(
        IReadOnlyList<SelectedFace3D> selectedFaces,
        IReadOnlyList<SelectedFaceDetails> selectedFaceDetails)
    {
        ArgumentNullException.ThrowIfNull(selectedFaces);
        ArgumentNullException.ThrowIfNull(selectedFaceDetails);
        UpdateSelectedFaces(selectedFaces);
        UpdateSelectedFaceDetails(selectedFaceDetails);
        _selectedFaceCount = selectedFaces.Count;
        RequestCoordination(Editor3DWorkspaceCoordinationKind.SelectionChanged);
    }

    internal bool UpdateBodies(IReadOnlyList<Body3D> bodies)
        => SetProperty(ref _bodies, bodies, nameof(Bodies));

    internal bool UpdateSelectedFaces(IReadOnlyList<SelectedFace3D> selectedFaces)
        => SetProperty(ref _selectedFaces, selectedFaces, nameof(SelectedFaces));

    internal bool UpdateSelectedFaceDetails(IReadOnlyList<SelectedFaceDetails> selectedFaceDetails)
        => SetProperty(ref _selectedFaceDetails, selectedFaceDetails, nameof(SelectedFaceDetails));

    internal bool UpdateSelectedBodyIndex(int? selectedBodyIndex)
        => SetProperty(ref _selectedBodyIndex, selectedBodyIndex, nameof(SelectedBodyIndex));

    internal bool UpdateBodyOffsets(IReadOnlyList<BodyOffset3D> bodyOffsets)
        => SetProperty(ref _bodyOffsets, bodyOffsets, nameof(BodyOffsets));

    internal bool UpdateViewportJson(string? viewportJson)
        => SetProperty(ref _viewportJsonContent, viewportJson, nameof(ViewportJsonContent));

    public Editor3DWorkspaceState CaptureState(
        EditorProjectionWorkspaceState projection,
        EditorUnfoldWorkspaceState unfold)
        => new(
            _activeTool,
            _threeDOrthographic,
            _viewportJsonContent,
            _sourceModelPath,
            _bodies,
            _bodyOffsets,
            _selectedFaces,
            _selectedFaceDetails,
            _selectedBodyIndex,
            projection,
            unfold);

    public void RestoreState(Editor3DWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _activeTool = state.ActiveTool;
        _threeDOrthographic = state.Orthographic;
        _viewportJsonContent = state.ViewportJson;
        _sourceModelPath = state.SourceModelPath;
        _bodies = state.Bodies ?? [];
        _bodyOffsets = state.BodyOffsets ?? [];
        _bodyOffsetCount = _bodyOffsets.Count;
        _selectedFaces = state.SelectedFaces ?? [];
        _selectedFaceDetails = state.SelectedFaceDetails ?? [];
        _selectedFaceCount = _selectedFaces.Count;
        _selectedBodyIndex = state.SelectedBodyIndex;
        RaiseStateProperties();
    }

    public void RequestViewportScript(string script)
    {
        ViewportScriptRequested?.Invoke(script);
        CoordinationRequested?.Invoke(
            this,
            new Editor3DWorkspaceCoordinationEventArgs(
                Editor3DWorkspaceCoordinationKind.ViewportScript,
                script));
    }

    private void RequestCoordination(Editor3DWorkspaceCoordinationKind kind)
        => CoordinationRequested?.Invoke(this, new Editor3DWorkspaceCoordinationEventArgs(kind));

    private void RaiseStateProperties()
    {
        OnPropertyChanged(nameof(ActiveTool));
        OnPropertyChanged(nameof(Bodies));
        OnPropertyChanged(nameof(SelectedFaces));
        OnPropertyChanged(nameof(SelectedFaceDetails));
        OnPropertyChanged(nameof(BodyOffsets));
        OnPropertyChanged(nameof(SelectedBodyIndex));
        OnPropertyChanged(nameof(ViewportJsonContent));
        OnPropertyChanged(nameof(SourceModelPath));
    }

    internal IEditor3DOperationService OperationService => _operationService;
    internal Queue<string> PendingViewportScripts => _pendingViewportScripts;
    internal ref string ViewportStateTextStorage => ref _viewportStateText;
    internal ref string SelectionSummaryStorage => ref _selectionSummary;
    internal ref string LastViewportEventStorage => ref _lastViewportEvent;
    internal ref bool ViewportReadyStorage => ref _viewportReady;
    internal ref int SelectedFaceCountStorage => ref _selectedFaceCount;
    internal ref string? ViewportJsonContentStorage => ref _viewportJsonContent;
    internal ref string? SourceModelPathStorage => ref _sourceModelPath;
    internal ref string DistortionDataJsonStorage => ref _distortionDataJson;
    internal ref Editor3DTool ActiveToolStorage => ref _activeTool;
    internal ref bool ThreeDOrthographicStorage => ref _threeDOrthographic;
    internal ref bool IsPlaneSelectionActiveStorage => ref _isPlaneSelectionActive;
    internal ref PlaneSelectionModeType PlaneSelectionModeTypeStorage => ref _planeSelectionModeType;
    internal ref string? SelectedProjectionPlaneStorage => ref _selectedProjectionPlane;
    internal ref int? SelectedProjectionFaceIndexStorage => ref _selectedProjectionFaceIndex;
    internal ref int? SelectedProjectionBodyIndexStorage => ref _selectedProjectionBodyIndex;
    internal ref double PlaneOffsetStorage => ref _planeOffset;
    internal ref string PlaneOffsetTextStorage => ref _planeOffsetText;
    internal ref bool IsPlaneOffsetTextValidStorage => ref _isPlaneOffsetTextValid;
    internal ref string ProjectionSelectionSummaryStorage => ref _projectionSelectionSummary;
    internal ref int? SelectedBodyIndexStorage => ref _selectedBodyIndex;
    internal ref IReadOnlyList<Body3D> BodiesStorage => ref _bodies;
    internal ref IReadOnlyList<SelectedFace3D> SelectedFacesStorage => ref _selectedFaces;
    internal ref IReadOnlyList<SelectedFaceDetails> SelectedFaceDetailsStorage => ref _selectedFaceDetails;
    internal ref IReadOnlyList<BodyOffset3D> BodyOffsetsStorage => ref _bodyOffsets;
    internal ref int BodyOffsetCountStorage => ref _bodyOffsetCount;
    internal ref bool IsSelectedBodyOffsetXTextValidStorage => ref _isSelectedBodyOffsetXTextValid;
    internal ref bool IsSelectedBodyOffsetYTextValidStorage => ref _isSelectedBodyOffsetYTextValid;
    internal ref bool IsSelectedBodyOffsetZTextValidStorage => ref _isSelectedBodyOffsetZTextValid;
    internal ref int DistortionModeIndexStorage => ref _distortionModeIndex;
    internal ref bool LiveRecomputeEnabledStorage => ref _liveRecomputeEnabled;
    internal ref bool WholeBodyRecomputeStorage => ref _wholeBodyRecompute;
    internal ref string SelectedBodyOffsetXTextStorage => ref _selectedBodyOffsetXText;
    internal ref string SelectedBodyOffsetYTextStorage => ref _selectedBodyOffsetYText;
    internal ref string SelectedBodyOffsetZTextStorage => ref _selectedBodyOffsetZText;
    internal ref string BodyMoveStepTextStorage => ref _bodyMoveStepText;
    internal ref bool IsUpdatingBodyOffsetTextStorage => ref _isUpdatingBodyOffsetText;
    internal ref IReadOnlyList<string> PendingSourceModelPathsStorage => ref _pendingSourceModelPaths;
    internal ref CancellationTokenSource? DistortionRefreshCancellationStorage => ref _distortionRefreshCancellationTokenSource;
    internal ref CancellationTokenSource? LiveRecomputeCancellationStorage => ref _liveRecomputeCancellationTokenSource;
}

public enum Editor3DWorkspaceCoordinationKind
{
    PersistState,
    SelectionChanged,
    ViewportScript,
}

public sealed record Editor3DWorkspaceCoordinationEventArgs(
    Editor3DWorkspaceCoordinationKind Kind,
    string? ViewportScript = null);
