using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

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
    IGeometryKernelDescriptorProvider geometryKernelDescriptorProvider,
    IReferenceImageTraceService? referenceImageTraceService = null,
    IReferenceImageBackgroundRemovalService? referenceImageBackgroundRemovalService = null,
    IUnsavedChangesPromptService? unsavedChangesPromptService = null,
    IEditorImportUnitsPromptService? importUnitsPromptService = null) : BasePageViewModel(logger)
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
    private readonly IUnsavedChangesPromptService _unsavedChangesPromptService =
        unsavedChangesPromptService ?? CancelUnsavedChangesPromptService.Instance;
    private readonly IEditorImportUnitsPromptService _importUnitsPromptService =
        importUnitsPromptService ?? CancelEditorImportUnitsPromptService.Instance;
    private readonly Editor2DWorkspaceViewModel _twoDWorkspace = new(
        referenceImageTraceService,
        referenceImageBackgroundRemovalService);
    private readonly EditorBatchWorkspaceViewModel _batchWorkspace = new();
    private ProjectSession? _projectSession;
    private string _projectName = string.Empty;
    private string _projectTitle = "Editor";
    private string _projectSubtitle = "No project loaded";
    private string _statusText = "Waiting for session";
    private string? _errorMessage;
    private string? _lastGeneratedOutputPath;
    private string? _generatedOutputDataBase64;
    private EditorGeneratedOutputSummary? _generatedOutputSummary;
    private EditorMode _activeEditorMode = EditorMode.ThreeD;
    private StepGeometryDocument? _stepTopology;
    private bool _suppressTwoDDocumentPersistence;
    private bool _suppressTwoDViewportPersistence;
    private bool _isApplyingTwoDWorkspaceState;
    private bool _isRefreshingDerivedTwoDMeasurements;
    private ProjectTemplateDefinition? _template;
    private ProjectSessionOrigin? _sessionOrigin;
    private bool _isLoading;
    private readonly Stack<IReadOnlyList<BodyOffset3D>> _threeDBodyMoveUndo = [];
    private readonly Stack<IReadOnlyList<BodyOffset3D>> _threeDBodyMoveRedo = [];
    private bool _isApplyingBodyMoveHistory;

    private sealed class CancelUnsavedChangesPromptService : IUnsavedChangesPromptService
    {
        public static CancelUnsavedChangesPromptService Instance { get; } = new();

        public Task<UnsavedChangesPromptResult> PromptToSaveAsync(
            string documentName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(UnsavedChangesPromptResult.Cancel);
    }

    private sealed class CancelEditorImportUnitsPromptService : IEditorImportUnitsPromptService
    {
        public static CancelEditorImportUnitsPromptService Instance { get; } = new();

        public Task<double?> PromptAsync(Editor2DImportUnitsInfo info, CancellationToken cancellationToken = default)
            => Task.FromResult<double?>(null);
    }
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
    private GeometryKernelDescriptor _geometryKernel => _threeDWorkspace.GeometryKernel;
    private string _viewportStateText { get => _threeDWorkspace.ViewportStateText; set => _threeDWorkspace.SetViewportStateText(value); }
    private string _selectionSummary { get => _threeDWorkspace.SelectionSummary; set => _threeDWorkspace.SetSelectionSummary(value); }
    private string _lastViewportEvent { get => _threeDWorkspace.LastViewportEvent; set => _threeDWorkspace.SetLastViewportEvent(value); }
    private bool _viewportReady { get => _threeDWorkspace.ViewportReady; set => _threeDWorkspace.SetViewportReady(value); }
    private int _selectedFaceCount { get => _threeDWorkspace.SelectedFaceCount; set => _threeDWorkspace.SetSelectedFaceCount(value); }
    private string? _sourceModelPath { get => _threeDWorkspace.SourceModelPath; set => _threeDWorkspace.SetSourceModelPath(value); }
    private string _distortionDataJson { get => _threeDWorkspace.DistortionDataJson; set => _threeDWorkspace.SetDistortionDataJson(value); }
    private bool _threeDOrthographic { get => _threeDWorkspace.ThreeDOrthographic; set => _threeDWorkspace.SetThreeDOrthographic(value); }
    private bool _isPlaneSelectionActive { get => _threeDWorkspace.IsPlaneSelectionActive; set => _threeDWorkspace.SetPlaneSelectionActive(value); }
    private PlaneSelectionModeType _planeSelectionModeType { get => _threeDWorkspace.PlaneSelectionModeType; set => _threeDWorkspace.SetPlaneSelectionMode(value); }
    private string? _selectedProjectionPlane { get => _threeDWorkspace.SelectedProjectionPlane; set => _threeDWorkspace.SetSelectedProjectionPlane(value); }
    private int? _selectedProjectionFaceIndex { get => _threeDWorkspace.SelectedProjectionFaceIndex; set => _threeDWorkspace.SetSelectedProjectionFaceIndex(value); }
    private int? _selectedProjectionBodyIndex { get => _threeDWorkspace.SelectedProjectionBodyIndex; set => _threeDWorkspace.SetSelectedProjectionBodyIndex(value); }
    private double _planeOffset { get => _threeDWorkspace.PlaneOffset; set => _threeDWorkspace.SetPlaneOffset(value); }
    private string _planeOffsetText { get => _threeDWorkspace.PlaneOffsetText; set => _threeDWorkspace.SetPlaneOffsetText(value); }
    private bool _isPlaneOffsetTextValid { get => _threeDWorkspace.IsPlaneOffsetTextValid; set => _threeDWorkspace.SetPlaneOffsetTextValid(value); }
    private string _projectionSelectionSummary { get => _threeDWorkspace.ProjectionSelectionSummary; set => _threeDWorkspace.SetProjectionSelectionSummary(value); }
    private int _bodyOffsetCount { get => _threeDWorkspace.BodyOffsetCount; set => _threeDWorkspace.SetBodyOffsetCount(value); }
    private bool _isSelectedBodyOffsetXTextValid { get => _threeDWorkspace.IsSelectedBodyOffsetXTextValid; set => _threeDWorkspace.SetBodyOffsetTextValid('X', value); }
    private bool _isSelectedBodyOffsetYTextValid { get => _threeDWorkspace.IsSelectedBodyOffsetYTextValid; set => _threeDWorkspace.SetBodyOffsetTextValid('Y', value); }
    private bool _isSelectedBodyOffsetZTextValid { get => _threeDWorkspace.IsSelectedBodyOffsetZTextValid; set => _threeDWorkspace.SetBodyOffsetTextValid('Z', value); }
    private int _distortionModeIndex { get => _threeDWorkspace.DistortionModeIndex; set => _threeDWorkspace.SetDistortionModeIndex(value); }
    private int _netLayoutIndex { get => _threeDWorkspace.NetLayoutIndex; set => _threeDWorkspace.SetNetLayoutIndex(value); }
    private int _unrollModeIndex { get => _threeDWorkspace.UnrollModeIndex; set => _threeDWorkspace.SetUnrollModeIndex(value); }
    private bool _liveRecomputeEnabled { get => _threeDWorkspace.LiveRecomputeEnabled; set => _threeDWorkspace.SetLiveRecomputeEnabled(value); }
    private bool _wholeBodyRecompute { get => _threeDWorkspace.WholeBodyRecompute; set => _threeDWorkspace.SetWholeBodyRecompute(value); }
    private string _selectedBodyOffsetXText { get => _threeDWorkspace.SelectedBodyOffsetXText; set => _threeDWorkspace.SetSelectedBodyOffsetText('X', value); }
    private string _selectedBodyOffsetYText { get => _threeDWorkspace.SelectedBodyOffsetYText; set => _threeDWorkspace.SetSelectedBodyOffsetText('Y', value); }
    private string _selectedBodyOffsetZText { get => _threeDWorkspace.SelectedBodyOffsetZText; set => _threeDWorkspace.SetSelectedBodyOffsetText('Z', value); }
    private string _bodyMoveStepText { get => _threeDWorkspace.BodyMoveStepText; set => _threeDWorkspace.SetBodyMoveStepText(value); }
    private bool _isUpdatingBodyOffsetText { get => _threeDWorkspace.IsUpdatingBodyOffsetText; set => _threeDWorkspace.SetUpdatingBodyOffsetText(value); }
    private IReadOnlyList<string> _pendingSourceModelPaths { get => _threeDWorkspace.PendingSourceModelPaths; set => _threeDWorkspace.SetPendingSourceModelPaths(value); }
    private IReadOnlyList<string> _pendingTwoDFilePaths = [];
    private IReadOnlyList<string> _pendingReferenceImagePaths = [];
    private string? _twoDPatternGuidePathId;
    private Editor2DPoint? _twoDPatternPivot;
    private bool _twoDPatternPivotPicking;
    private string _twoDPrecisionDeltaXText = "0";
    private string _twoDPrecisionDeltaYText = "0";
    private string _twoDPrecisionRotationText = "0";
    private Editor2DPoint? _twoDScalePivot;
    private bool _twoDScalePivotPicking;
    private string _twoDScaleFactorText = "1";
    private bool _twoDScaleFromCenter = true;
    private bool _twoDMirrorLineMode;
    private Editor2DPoint? _twoDMirrorAxisStart;
    private Editor2DPoint? _twoDMirrorAxisEnd;

    private bool SetWorkspaceFacadeValue<T>(T current, T value, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return false;
        assign(value);
        OnPropertyChanged(propertyName);
        return true;
    }
}
