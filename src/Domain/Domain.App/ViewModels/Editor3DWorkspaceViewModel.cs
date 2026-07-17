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
    private string _planeSelectionModeValue = "origin";
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
    private int _netLayoutIndex = 1;
    private int _unrollModeIndex;
    private bool _liveRecomputeEnabled;
    private bool _wholeBodyRecompute;
    private int _seamControlModeIndex;
    private IReadOnlyList<EditorSeamEdge3D> _forcedSeams = [];
    private IReadOnlyList<EditorSeamEdge3D> _forbiddenSeams = [];
    private int _globalSeamDecorationIndex;
    private SelectedFace3D? _anchorFace;
    private IReadOnlyList<EditorSeamDecoration3D> _seamDecorations = [];
    private EditorSeamEdge3D? _selectedSeamEdge;
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
    public string ViewportStateText => _viewportStateText;
    public string SelectionSummary => _selectionSummary;
    public string LastViewportEvent => _lastViewportEvent;
    public bool ViewportReady => _viewportReady;
    public int SelectedFaceCount => _selectedFaceCount;
    public string DistortionDataJson => _distortionDataJson;
    public bool ThreeDOrthographic => _threeDOrthographic;
    public bool IsPlaneSelectionActive => _isPlaneSelectionActive;
    public PlaneSelectionModeType PlaneSelectionModeType => _planeSelectionModeType;
    public string? SelectedProjectionPlane => _selectedProjectionPlane;
    public int? SelectedProjectionFaceIndex => _selectedProjectionFaceIndex;
    public int? SelectedProjectionBodyIndex => _selectedProjectionBodyIndex;
    public double PlaneOffset => _planeOffset;
    public string PlaneOffsetText => _planeOffsetText;
    public bool IsPlaneOffsetTextValid => _isPlaneOffsetTextValid;
    public string ProjectionSelectionSummary => _projectionSelectionSummary;
    public int BodyOffsetCount => _bodyOffsetCount;
    public bool IsSelectedBodyOffsetXTextValid => _isSelectedBodyOffsetXTextValid;
    public bool IsSelectedBodyOffsetYTextValid => _isSelectedBodyOffsetYTextValid;
    public bool IsSelectedBodyOffsetZTextValid => _isSelectedBodyOffsetZTextValid;
    public int DistortionModeIndex => _distortionModeIndex;
    public int NetLayoutIndex => _netLayoutIndex;
    public int UnrollModeIndex => _unrollModeIndex;
    public bool LiveRecomputeEnabled => _liveRecomputeEnabled;
    public bool WholeBodyRecompute => _wholeBodyRecompute;
    public int SeamControlModeIndex => _seamControlModeIndex;
    public IReadOnlyList<EditorSeamEdge3D> ForcedSeams => _forcedSeams;
    public IReadOnlyList<EditorSeamEdge3D> ForbiddenSeams => _forbiddenSeams;
    public int GlobalSeamDecorationIndex => _globalSeamDecorationIndex;
    public SelectedFace3D? AnchorFace => _anchorFace;
    public IReadOnlyList<EditorSeamDecoration3D> SeamDecorations => _seamDecorations;
    public EditorSeamEdge3D? SelectedSeamEdge => _selectedSeamEdge;
    public string SelectedBodyOffsetXText => _selectedBodyOffsetXText;
    public string SelectedBodyOffsetYText => _selectedBodyOffsetYText;
    public string SelectedBodyOffsetZText => _selectedBodyOffsetZText;
    public string BodyMoveStepText => _bodyMoveStepText;
    public bool IsUpdatingBodyOffsetText => _isUpdatingBodyOffsetText;
    public IReadOnlyList<string> PendingSourceModelPaths => _pendingSourceModelPaths;

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
        SetBodies(bodies);
        SetViewportJson(viewportJson);
        SetProperty(ref _sourceModelPath, sourceModelPath, nameof(SourceModelPath));
        RequestCoordination(Editor3DWorkspaceCoordinationKind.PersistState);
    }

    public void SetFaceSelection(
        IReadOnlyList<SelectedFace3D> selectedFaces,
        IReadOnlyList<SelectedFaceDetails> selectedFaceDetails)
    {
        ArgumentNullException.ThrowIfNull(selectedFaces);
        ArgumentNullException.ThrowIfNull(selectedFaceDetails);
        SetSelectedFaces(selectedFaces);
        SetSelectedFaceDetails(selectedFaceDetails);
        _selectedFaceCount = selectedFaces.Count;
        RequestCoordination(Editor3DWorkspaceCoordinationKind.SelectionChanged);
    }

    internal bool SetBodies(IReadOnlyList<Body3D> bodies)
        => SetProperty(ref _bodies, bodies, nameof(Bodies));

    internal bool SetSelectedFaces(IReadOnlyList<SelectedFace3D> selectedFaces)
        => SetProperty(ref _selectedFaces, selectedFaces, nameof(SelectedFaces));

    internal bool SetSelectedFaceDetails(IReadOnlyList<SelectedFaceDetails> selectedFaceDetails)
        => SetProperty(ref _selectedFaceDetails, selectedFaceDetails, nameof(SelectedFaceDetails));

    internal bool SetSelectedBodyIndex(int? selectedBodyIndex)
        => SetProperty(ref _selectedBodyIndex, selectedBodyIndex, nameof(SelectedBodyIndex));

    internal bool SetBodyOffsets(IReadOnlyList<BodyOffset3D> bodyOffsets)
        => SetProperty(ref _bodyOffsets, bodyOffsets, nameof(BodyOffsets));

    internal bool SetViewportJson(string? viewportJson)
        => SetProperty(ref _viewportJsonContent, viewportJson, nameof(ViewportJsonContent));

    internal bool SetSourceModelPath(string? value) => SetProperty(ref _sourceModelPath, value, nameof(SourceModelPath));
    internal bool SetViewportStateText(string value) => SetProperty(ref _viewportStateText, value, nameof(ViewportStateText));
    internal bool SetSelectionSummary(string value) => SetProperty(ref _selectionSummary, value, nameof(SelectionSummary));
    internal bool SetLastViewportEvent(string value) => SetProperty(ref _lastViewportEvent, value, nameof(LastViewportEvent));
    internal bool SetViewportReady(bool value) => SetProperty(ref _viewportReady, value, nameof(ViewportReady));
    internal bool SetSelectedFaceCount(int value) => SetProperty(ref _selectedFaceCount, value, nameof(SelectedFaceCount));
    internal bool SetDistortionDataJson(string value) => SetProperty(ref _distortionDataJson, value, nameof(DistortionDataJson));
    internal bool SetThreeDOrthographic(bool value) => SetProperty(ref _threeDOrthographic, value, nameof(ThreeDOrthographic));
    internal bool SetPlaneSelectionActive(bool value) => SetProperty(ref _isPlaneSelectionActive, value, nameof(IsPlaneSelectionActive));
    internal bool SetPlaneSelectionMode(PlaneSelectionModeType value)
    {
        _planeSelectionModeValue = value == PlaneSelectionModeType.Face ? "face" : "origin";
        return SetProperty(ref _planeSelectionModeType, value, nameof(PlaneSelectionModeType));
    }
    internal bool SetSelectedProjectionPlane(string? value) => SetProperty(ref _selectedProjectionPlane, value, nameof(SelectedProjectionPlane));
    internal bool SetSelectedProjectionFaceIndex(int? value) => SetProperty(ref _selectedProjectionFaceIndex, value, nameof(SelectedProjectionFaceIndex));
    internal bool SetSelectedProjectionBodyIndex(int? value) => SetProperty(ref _selectedProjectionBodyIndex, value, nameof(SelectedProjectionBodyIndex));
    internal bool SetPlaneOffset(double value) => SetProperty(ref _planeOffset, value, nameof(PlaneOffset));
    internal bool SetPlaneOffsetText(string value) => SetProperty(ref _planeOffsetText, value, nameof(PlaneOffsetText));
    internal bool SetPlaneOffsetTextValid(bool value) => SetProperty(ref _isPlaneOffsetTextValid, value, nameof(IsPlaneOffsetTextValid));
    internal bool SetProjectionSelectionSummary(string value) => SetProperty(ref _projectionSelectionSummary, value, nameof(ProjectionSelectionSummary));
    internal bool SetBodyOffsetCount(int value) => SetProperty(ref _bodyOffsetCount, value, nameof(BodyOffsetCount));
    internal bool SetBodyOffsetTextValid(char axis, bool value) => axis switch
    {
        'X' => SetProperty(ref _isSelectedBodyOffsetXTextValid, value, nameof(IsSelectedBodyOffsetXTextValid)),
        'Y' => SetProperty(ref _isSelectedBodyOffsetYTextValid, value, nameof(IsSelectedBodyOffsetYTextValid)),
        _ => SetProperty(ref _isSelectedBodyOffsetZTextValid, value, nameof(IsSelectedBodyOffsetZTextValid)),
    };
    internal bool SetDistortionModeIndex(int value) => SetProperty(ref _distortionModeIndex, value, nameof(DistortionModeIndex));
    internal bool SetNetLayoutIndex(int value) => SetProperty(ref _netLayoutIndex, Math.Clamp(value, 0, 1), nameof(NetLayoutIndex));
    internal bool SetUnrollModeIndex(int value) => SetProperty(ref _unrollModeIndex, Math.Clamp(value, 0, 2), nameof(UnrollModeIndex));
    internal bool SetLiveRecomputeEnabled(bool value) => SetProperty(ref _liveRecomputeEnabled, value, nameof(LiveRecomputeEnabled));
    internal bool SetWholeBodyRecompute(bool value) => SetProperty(ref _wholeBodyRecompute, value, nameof(WholeBodyRecompute));
    internal bool SetSeamControlModeIndex(int value) => SetProperty(ref _seamControlModeIndex, Math.Clamp(value, 0, 2), nameof(SeamControlModeIndex));
    internal bool SetForcedSeams(IReadOnlyList<EditorSeamEdge3D> value) => SetProperty(ref _forcedSeams, value, nameof(ForcedSeams));
    internal bool SetForbiddenSeams(IReadOnlyList<EditorSeamEdge3D> value) => SetProperty(ref _forbiddenSeams, value, nameof(ForbiddenSeams));
    internal bool SetGlobalSeamDecorationIndex(int value) => SetProperty(ref _globalSeamDecorationIndex, Math.Clamp(value, 0, 2), nameof(GlobalSeamDecorationIndex));
    internal bool SetAnchorFace(SelectedFace3D? value) => SetProperty(ref _anchorFace, value, nameof(AnchorFace));
    internal bool SetSeamDecorations(IReadOnlyList<EditorSeamDecoration3D> value) => SetProperty(ref _seamDecorations, value, nameof(SeamDecorations));
    internal bool SetSelectedSeamEdge(EditorSeamEdge3D? value) => SetProperty(ref _selectedSeamEdge, value, nameof(SelectedSeamEdge));

    public void SetSeamDecoration(EditorSeamEdge3D edge, string decoration)
    {
        var normalized = decoration is "tabs" or "holes" or "none" ? decoration : "none";
        SetSeamDecorations(_seamDecorations
            .Where(item => item.Edge != edge)
            .Append(new EditorSeamDecoration3D(edge, normalized))
            .ToArray());
    }

    public void ClearSeamDecoration(EditorSeamEdge3D edge)
        => SetSeamDecorations(_seamDecorations.Where(item => item.Edge != edge).ToArray());

    public void ToggleSeamEdge(int bodyIndex, int edgeIndex)
    {
        var edge = new EditorSeamEdge3D(bodyIndex, edgeIndex);
        SetSelectedSeamEdge(edge);
        if (_seamControlModeIndex == 1)
            SetForcedSeams(ToggleEdge(_forcedSeams, edge));
        else if (_seamControlModeIndex == 2)
            SetForbiddenSeams(ToggleEdge(_forbiddenSeams, edge));
    }

    public void ClearActiveSeamOverrides()
    {
        if (_seamControlModeIndex == 1)
            SetForcedSeams([]);
        else if (_seamControlModeIndex == 2)
            SetForbiddenSeams([]);
    }
    internal bool SetSelectedBodyOffsetText(char axis, string value) => axis switch
    {
        'X' => SetProperty(ref _selectedBodyOffsetXText, value, nameof(SelectedBodyOffsetXText)),
        'Y' => SetProperty(ref _selectedBodyOffsetYText, value, nameof(SelectedBodyOffsetYText)),
        _ => SetProperty(ref _selectedBodyOffsetZText, value, nameof(SelectedBodyOffsetZText)),
    };
    internal bool SetBodyMoveStepText(string value) => SetProperty(ref _bodyMoveStepText, value, nameof(BodyMoveStepText));
    internal bool SetUpdatingBodyOffsetText(bool value) => SetProperty(ref _isUpdatingBodyOffsetText, value, nameof(IsUpdatingBodyOffsetText));
    internal bool SetPendingSourceModelPaths(IReadOnlyList<string> value) => SetProperty(ref _pendingSourceModelPaths, value, nameof(PendingSourceModelPaths));
    internal CancellationTokenSource BeginDistortionRefresh(CancellationToken cancellationToken = default)
    {
        var next = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        var previous = _distortionRefreshCancellationTokenSource;
        _distortionRefreshCancellationTokenSource = next;
        previous?.Cancel();
        previous?.Dispose();
        return next;
    }
    internal void CompleteDistortionRefresh(CancellationTokenSource request)
    {
        if (ReferenceEquals(_distortionRefreshCancellationTokenSource, request))
            _distortionRefreshCancellationTokenSource = null;
        request.Dispose();
    }
    internal CancellationTokenSource BeginLiveRecompute()
    {
        var next = new CancellationTokenSource();
        CancelLiveRecompute();
        _liveRecomputeCancellationTokenSource = next;
        return next;
    }
    internal void CancelLiveRecompute()
    {
        var current = _liveRecomputeCancellationTokenSource;
        _liveRecomputeCancellationTokenSource = null;
        current?.Cancel();
        current?.Dispose();
    }
    internal void CompleteLiveRecompute(CancellationTokenSource request)
    {
        if (ReferenceEquals(_liveRecomputeCancellationTokenSource, request))
            _liveRecomputeCancellationTokenSource = null;
        request.Dispose();
    }

    internal bool ResetViewportLifecycle()
    {
        _pendingViewportScripts.Clear();
        return SetViewportReady(false);
    }
    internal bool MarkViewportReady()
    {
        var changed = SetViewportReady(true);
        while (_pendingViewportScripts.TryDequeue(out var script))
            RequestViewportScript(script);
        return changed;
    }
    internal void DispatchViewportScript(string script)
    {
        if (_viewportReady)
            RequestViewportScript(script);
        else
            _pendingViewportScripts.Enqueue(script);
    }

    public Task<EditorModelLoadResult> LoadModelAsync(string path, CancellationToken token = default)
        => _operationService.LoadModelAsync(path, token);
    public Task<EditorModelLoadResult> LoadModelsAsync(IReadOnlyList<string> paths, string? existing, CancellationToken token = default)
        => _operationService.LoadModelsAsync(paths, existing, token);
    public Task<EditorOperationResult> ProjectAsync(EditorProjectionRequest request, CancellationToken token = default)
        => _operationService.ProjectEdgesAsync(request, token);
    public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken token = default)
        => _operationService.UnfoldAsync(request, token);
    public Task<EditorFaceDistortionResult> ComputeDistortionAsync(string? path, SelectedFace3D face, string mode, CancellationToken token = default)
        => _operationService.ComputeFaceDistortionAsync(path, face, mode, token);

    public Editor3DWorkspaceState CaptureState()
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
            new EditorProjectionWorkspaceState(
                _planeSelectionModeValue,
                _selectedProjectionPlane,
                _selectedProjectionFaceIndex,
                _selectedProjectionBodyIndex,
                _planeOffset),
            new EditorUnfoldWorkspaceState(
                _netLayoutIndex, _distortionModeIndex, _unrollModeIndex, _globalSeamDecorationIndex, _seamControlModeIndex,
                _liveRecomputeEnabled, _wholeBodyRecompute,
                "5", "1", "4", "2",
                _forcedSeams.Count == 0 ? null : _forcedSeams,
                _forbiddenSeams.Count == 0 ? null : _forbiddenSeams,
                _anchorFace,
                _seamDecorations.Count == 0 ? null : _seamDecorations));

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
        _planeSelectionModeValue = state.Projection.PlaneSelectionModeType;
        _planeSelectionModeType = string.Equals(_planeSelectionModeValue, "face", StringComparison.OrdinalIgnoreCase)
            ? PlaneSelectionModeType.Face
            : PlaneSelectionModeType.Origin;
        _selectedProjectionPlane = state.Projection.SelectedProjectionPlane;
        _selectedProjectionFaceIndex = state.Projection.SelectedProjectionFaceIndex;
        _selectedProjectionBodyIndex = state.Projection.SelectedProjectionBodyIndex;
        _planeOffset = state.Projection.PlaneOffset;
        _distortionModeIndex = state.Unfold.DistortionModeIndex;
        _netLayoutIndex = Math.Clamp(state.Unfold.NetLayoutIndex, 0, 1);
        _unrollModeIndex = Math.Clamp(state.Unfold.UnrollModeIndex, 0, 2);
        _liveRecomputeEnabled = state.Unfold.LiveRecomputeEnabled;
        _wholeBodyRecompute = state.Unfold.WholeBodyRecompute;
        _seamControlModeIndex = state.Unfold.SeamControlModeIndex;
        _forcedSeams = state.Unfold.ForcedSeams ?? [];
        _forbiddenSeams = state.Unfold.ForbiddenSeams ?? [];
        _globalSeamDecorationIndex = state.Unfold.GlobalSeamDecorationIndex;
        _anchorFace = state.Unfold.AnchorFace;
        _seamDecorations = state.Unfold.SeamDecorations ?? [];
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
        OnPropertyChanged(nameof(ThreeDOrthographic));
        OnPropertyChanged(nameof(PlaneSelectionModeType));
        OnPropertyChanged(nameof(SelectedProjectionPlane));
        OnPropertyChanged(nameof(SelectedProjectionFaceIndex));
        OnPropertyChanged(nameof(SelectedProjectionBodyIndex));
        OnPropertyChanged(nameof(PlaneOffset));
        OnPropertyChanged(nameof(DistortionModeIndex));
        OnPropertyChanged(nameof(NetLayoutIndex));
        OnPropertyChanged(nameof(UnrollModeIndex));
        OnPropertyChanged(nameof(LiveRecomputeEnabled));
        OnPropertyChanged(nameof(WholeBodyRecompute));
        OnPropertyChanged(nameof(SeamControlModeIndex));
        OnPropertyChanged(nameof(ForcedSeams));
        OnPropertyChanged(nameof(ForbiddenSeams));
        OnPropertyChanged(nameof(GlobalSeamDecorationIndex));
        OnPropertyChanged(nameof(AnchorFace));
        OnPropertyChanged(nameof(SeamDecorations));
        OnPropertyChanged(nameof(SelectedSeamEdge));
    }

    private static IReadOnlyList<EditorSeamEdge3D> ToggleEdge(
        IReadOnlyList<EditorSeamEdge3D> source,
        EditorSeamEdge3D edge)
        => source.Contains(edge)
            ? source.Where(candidate => candidate != edge).ToArray()
            : source.Append(edge).ToArray();

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
