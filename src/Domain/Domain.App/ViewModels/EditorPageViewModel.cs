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
    IEditor3DOperationService editor3DOperationService,
    IGeometryKernelDescriptorProvider geometryKernelDescriptorProvider) : BasePageViewModel(logger)
{
    private static readonly HashSet<string> SupportedSourceModelExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".step",
        ".stp",
        ".obj",
        ".stl",
    };

    private readonly ILogger<EditorPageViewModel> _logger = logger;
    private readonly string _viewportHtml = editorViewportAssetLocator.GetViewportHtml();
    private readonly Uri _viewportBaseUri = editorViewportAssetLocator.GetViewportBaseUri();
    private readonly Queue<string> _pendingViewportScripts = new();
    private readonly IEditor3DOperationService _editor3DOperationService = editor3DOperationService;
    private readonly IEditorOutputLauncherService _editorOutputLauncherService = editorOutputLauncherService;
    private readonly IEditorOutputPreviewService _editorOutputPreviewService = editorOutputPreviewService;
    private readonly GeometryKernelDescriptor _geometryKernel = geometryKernelDescriptorProvider.Current;
    private readonly IProjectFileDialogService _projectFileDialogService = projectFileDialogService;
    private readonly Project3DStateService _project3DStateService = project3DStateService;
    private CancellationTokenSource? _persist3DStateCancellationTokenSource;
    private CancellationTokenSource? _persistGeneratedOutputDocumentCancellationTokenSource;
    private CancellationTokenSource? _distortionRefreshCancellationTokenSource;
    private CancellationTokenSource? _liveRecomputeCancellationTokenSource;

    private ProjectSession? _projectSession;
    private string _projectName = string.Empty;
    private string _projectTitle = "Editor";
    private string _projectSubtitle = "No project loaded";
    private string _statusText = "Waiting for session";
    private string _viewportStateText = "Booting viewport";
    private string _selectionSummary = "No selection";
    private string _lastViewportEvent = "None";
    private string? _errorMessage;
    private bool _viewportReady;
    private int _selectedFaceCount;
    private string? _stepJsonContent;
    private string? _sourceModelPath;
    private string _distortionDataJson = string.Empty;
    private string? _lastGeneratedOutputPath;
    private string? _generatedOutputDataBase64;
    private EditorGeneratedOutputSummary? _generatedOutputSummary;
    private Editor2DPreviewDocument? _generatedOutputPreviewDocument;
    private Editor2DTool _generatedOutputActiveTool = Editor2DTool.Select;
    private int _generatedOutputPolygonSides = 6;
    private IReadOnlyList<string> _generatedOutputSelectedPathIds = [];
    private IReadOnlyList<Editor2DMeasurement> _generatedOutputMeasurements = [];
    private IReadOnlyList<string> _generatedOutputExpandedRectanglePathIds = [];
    private string? _generatedOutputSelectedMeasurementId;
    private string _generatedOutputSelectedTextDraft = string.Empty;
    private string _generatedOutputSelectedTextHeightText = "5";
    private bool _isGeneratedOutputSelectedTextHeightValid = true;
    private bool _isShowingGeneratedOutputWorkspace;
    private double _generatedOutputViewportZoom;
    private double _generatedOutputViewportOffsetX;
    private double _generatedOutputViewportOffsetY;
    private int _generatedOutputFrameRequestToken;
    private bool _suppressGeneratedOutputDocumentPersistence;
    private bool _suppressGeneratedOutputViewportPersistence;
    private ProjectTemplateDefinition? _template;
    private ProjectSessionOrigin? _sessionOrigin;
    private bool _isLoading;
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

    public event Action<string>? ViewportScriptRequested;
}
