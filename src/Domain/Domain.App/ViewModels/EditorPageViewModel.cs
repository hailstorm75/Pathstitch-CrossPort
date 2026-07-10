using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

[NavigationPage(NavigationAddressBook.EditorPage)]
public sealed partial class EditorPageViewModel(
    ILogger<EditorPageViewModel> logger,
    IEditorViewportAssetLocator editorViewportAssetLocator,
    Project3DStateService project3DStateService,
    IProjectFileDialogService projectFileDialogService,
    IEditorOutputLauncherService editorOutputLauncherService,
    IEditorOutputPreviewService editorOutputPreviewService,
    IEditor2DGeometryKernelService editor2DGeometryKernelService,
    IEditor3DOperationService editor3DOperationService,
    IGeometryKernelDescriptorProvider geometryKernelDescriptorProvider) : BasePageViewModel(logger)
{
    private static readonly HashSet<string> SupportedSourceModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".obj",
        ".stl",
        ".step",
        ".stp",
    };

    private readonly ILogger<EditorPageViewModel> _logger = logger;
    private readonly Editor3DWorkspaceViewModel _threeDWorkspace = new(
        editor3DOperationService,
        editorViewportAssetLocator.GetViewportHtml(),
        editorViewportAssetLocator.GetViewportBaseUri(),
        geometryKernelDescriptorProvider.Current);
    private readonly IEditor2DGeometryKernelService _editor2DGeometryKernelService = editor2DGeometryKernelService;
    private readonly IEditorOutputLauncherService _editorOutputLauncherService = editorOutputLauncherService;
    private readonly IEditorOutputPreviewService _editorOutputPreviewService = editorOutputPreviewService;
    private readonly IProjectFileDialogService _projectFileDialogService = projectFileDialogService;
    private readonly Project3DStateService _project3DStateService = project3DStateService;
    private readonly Editor2DWorkspaceViewModel _twoDWorkspace = new();
    private readonly EditorBatchWorkspaceViewModel _batchWorkspace = new();
    private CancellationTokenSource? _persist3DStateCancellationTokenSource;
    private CancellationTokenSource? _persistTwoDDocumentCancellationTokenSource;

    private ProjectSession? _projectSession;
    private string _projectName = string.Empty;
    private string _projectTitle = "Editor";
    private string _projectSubtitle = "No project loaded";
    private string _statusText = "Waiting for session";
    private string? _errorMessage;
    private string? _lastGeneratedOutputPath;
    private string? _generatedOutputDataBase64;
    private EditorGeneratedOutputSummary? _generatedOutputSummary;
    private int _twoDPolygonSides = 6;
    private IReadOnlyList<string> _twoDExpandedRectanglePathIds = [];
    private string _twoDSelectedTextDraft = string.Empty;
    private string _twoDSelectedTextHeightText = "5";
    private string _twoDSelectedTextFontFamily = "Inter";
    private string _twoDSelectedTextCharacterSpacingText = "0";
    private bool _twoDSelectedTextBold;
    private bool _twoDSelectedTextItalic;
    private bool _twoDSelectedTextUnderline;
    private bool _isTwoDSelectedTextHeightValid = true;
    private EditorMode _activeEditorMode = EditorMode.ThreeD;
    private double _twoDViewportZoom;
    private double _twoDViewportOffsetX;
    private double _twoDViewportOffsetY;
    private int _twoDFrameRequestToken;
    private StepGeometryDocument? _stepTopology;
    private bool _suppressTwoDDocumentPersistence;
    private bool _suppressTwoDViewportPersistence;
    private bool _isApplyingTwoDWorkspaceState;
    private bool _isRefreshingDerivedTwoDMeasurements;
    private ProjectTemplateDefinition? _template;
    private ProjectSessionOrigin? _sessionOrigin;
    private bool _isLoading;
    public event Action<string>? ViewportScriptRequested
    {
        add => _threeDWorkspace.ViewportScriptRequested += value;
        remove => _threeDWorkspace.ViewportScriptRequested -= value;
    }

    public Editor2DWorkspaceViewModel TwoDWorkspace => _twoDWorkspace;

    public Editor3DWorkspaceViewModel ThreeDWorkspace => _threeDWorkspace;

    public EditorBatchWorkspaceViewModel BatchWorkspace => _batchWorkspace;

    private string _viewportHtml => _threeDWorkspace.ViewportHtml;
    private Uri _viewportBaseUri => _threeDWorkspace.ViewportBaseUri;
    private Queue<string> _pendingViewportScripts => _threeDWorkspace.PendingViewportScripts;
    private IEditor3DOperationService _editor3DOperationService => _threeDWorkspace.OperationService;
    private GeometryKernelDescriptor _geometryKernel => _threeDWorkspace.GeometryKernel;
    private ref string _viewportStateText => ref _threeDWorkspace.ViewportStateTextStorage;
    private ref string _selectionSummary => ref _threeDWorkspace.SelectionSummaryStorage;
    private ref string _lastViewportEvent => ref _threeDWorkspace.LastViewportEventStorage;
    private ref bool _viewportReady => ref _threeDWorkspace.ViewportReadyStorage;
    private ref int _selectedFaceCount => ref _threeDWorkspace.SelectedFaceCountStorage;
    private ref string? _sourceModelPath => ref _threeDWorkspace.SourceModelPathStorage;
    private ref string _distortionDataJson => ref _threeDWorkspace.DistortionDataJsonStorage;
    private ref bool _threeDOrthographic => ref _threeDWorkspace.ThreeDOrthographicStorage;
    private ref bool _isPlaneSelectionActive => ref _threeDWorkspace.IsPlaneSelectionActiveStorage;
    private ref PlaneSelectionModeType _planeSelectionModeType => ref _threeDWorkspace.PlaneSelectionModeTypeStorage;
    private ref string? _selectedProjectionPlane => ref _threeDWorkspace.SelectedProjectionPlaneStorage;
    private ref int? _selectedProjectionFaceIndex => ref _threeDWorkspace.SelectedProjectionFaceIndexStorage;
    private ref int? _selectedProjectionBodyIndex => ref _threeDWorkspace.SelectedProjectionBodyIndexStorage;
    private ref double _planeOffset => ref _threeDWorkspace.PlaneOffsetStorage;
    private ref string _planeOffsetText => ref _threeDWorkspace.PlaneOffsetTextStorage;
    private ref bool _isPlaneOffsetTextValid => ref _threeDWorkspace.IsPlaneOffsetTextValidStorage;
    private ref string _projectionSelectionSummary => ref _threeDWorkspace.ProjectionSelectionSummaryStorage;
    private ref int _bodyOffsetCount => ref _threeDWorkspace.BodyOffsetCountStorage;
    private ref bool _isSelectedBodyOffsetXTextValid => ref _threeDWorkspace.IsSelectedBodyOffsetXTextValidStorage;
    private ref bool _isSelectedBodyOffsetYTextValid => ref _threeDWorkspace.IsSelectedBodyOffsetYTextValidStorage;
    private ref bool _isSelectedBodyOffsetZTextValid => ref _threeDWorkspace.IsSelectedBodyOffsetZTextValidStorage;
    private ref int _distortionModeIndex => ref _threeDWorkspace.DistortionModeIndexStorage;
    private ref bool _liveRecomputeEnabled => ref _threeDWorkspace.LiveRecomputeEnabledStorage;
    private ref bool _wholeBodyRecompute => ref _threeDWorkspace.WholeBodyRecomputeStorage;
    private ref string _selectedBodyOffsetXText => ref _threeDWorkspace.SelectedBodyOffsetXTextStorage;
    private ref string _selectedBodyOffsetYText => ref _threeDWorkspace.SelectedBodyOffsetYTextStorage;
    private ref string _selectedBodyOffsetZText => ref _threeDWorkspace.SelectedBodyOffsetZTextStorage;
    private ref string _bodyMoveStepText => ref _threeDWorkspace.BodyMoveStepTextStorage;
    private ref bool _isUpdatingBodyOffsetText => ref _threeDWorkspace.IsUpdatingBodyOffsetTextStorage;
    private ref IReadOnlyList<string> _pendingSourceModelPaths => ref _threeDWorkspace.PendingSourceModelPathsStorage;
    private ref CancellationTokenSource? _distortionRefreshCancellationTokenSource => ref _threeDWorkspace.DistortionRefreshCancellationStorage;
    private ref CancellationTokenSource? _liveRecomputeCancellationTokenSource => ref _threeDWorkspace.LiveRecomputeCancellationStorage;
}
