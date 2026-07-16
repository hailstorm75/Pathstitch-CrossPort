using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private string? _twoDTextFontPreview;

    private sealed record TwoDConvertLineParameterDefinition(
        string Key,
        string Label,
        double DefaultValue,
        double MinimumValue,
        bool IsInteger = false);

    private static readonly IReadOnlyList<string> TwoDConvertLineStyleOrder =
    [
        "dashed",
        "dotted",
        "zigzag",
        "wave",
        "striped",
        "square",
        "triangle",
    ];

    private static readonly IReadOnlyDictionary<string, TwoDConvertLineParameterDefinition[]> TwoDConvertLineParameterDefinitions =
        new Dictionary<string, TwoDConvertLineParameterDefinition[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["dashed"] =
            [
                new("dash_length", "Dash Length (mm)", 4.0, 0.1),
                new("gap", "Gap (mm)", 3.0, 0.1),
            ],
            ["dotted"] =
            [
                new("spacing", "Spacing (mm)", 3.0, 0.2),
                new("dot_radius", "Dot Radius (mm)", 0.5, 0.05),
            ],
            ["zigzag"] =
            [
                new("wavelength", "Wavelength (mm)", 6.0, 0.5),
                new("amplitude", "Amplitude (mm)", 2.0, 0.0),
            ],
            ["wave"] =
            [
                new("wavelength", "Wavelength (mm)", 6.0, 0.5),
                new("amplitude", "Amplitude (mm)", 2.0, 0.0),
                new("samples_per_wave", "Samples / Wave", 12.0, 4.0, IsInteger: true),
            ],
            ["striped"] =
            [
                new("dash_length", "Stroke Length (mm)", 3.0, 0.1),
                new("gap", "Spacing (mm)", 3.0, 0.1),
                new("tilt", "Tilt (deg)", 45.0, -360.0),
            ],
            ["square"] =
            [
                new("spacing", "Spacing (mm)", 4.0, 0.3),
                new("size", "Size (mm)", 1.5, 0.1),
            ],
            ["triangle"] =
            [
                new("spacing", "Spacing (mm)", 5.0, 0.3),
                new("size", "Size (mm)", 2.0, 0.1),
            ],
        };

    private static readonly IReadOnlyList<string> TwoDOffsetModeOptions =
    [
        "Curve",
        "BBox",
    ];

    private static readonly IReadOnlyList<string> TwoDOffsetSideOptions =
    [
        "Outward",
        "Inward",
    ];

    private static readonly IReadOnlyList<string> TwoDPatternModeOptions =
    [
        "Rectangular",
        "Circular",
        "Path",
    ];

    private static readonly IReadOnlyList<string> TwoDGlueTabTypeOptions =
    [
        "Trapezoid",
        "Triangle",
    ];

    private static readonly IReadOnlyList<string> TwoDGlueTabSideOptions =
    [
        "Left",
        "Right",
    ];

    private EditorGeneratedOutputContext? _generatedOutputContext;

    public string? LastGeneratedOutputPath
    {
        get => _lastGeneratedOutputPath;
        private set
        {
            if (!SetProperty(ref _lastGeneratedOutputPath, value))
                return;

            OnPropertyChanged(nameof(HasGeneratedOutput));
            OnPropertyChanged(nameof(GeneratedOutputFileName));
            OnPropertyChanged(nameof(GeneratedOutputFolder));
            OnPropertyChanged(nameof(OutputStatusSummary));
        }
    }

    public EditorGeneratedOutputContext? GeneratedOutputContext
    {
        get => _generatedOutputContext;
        private set
        {
            if (!SetProperty(ref _generatedOutputContext, value))
                return;

            OnPropertyChanged(nameof(HasGeneratedOutputContext));
            OnPropertyChanged(nameof(GeneratedOutputSourceSummary));
            OnPropertyChanged(nameof(GeneratedOutputScopeSummary));
            OnPropertyChanged(nameof(GeneratedOutputConfigurationSummary));
            OnPropertyChanged(nameof(GeneratedOutputCreatedSummary));
        }
    }

    public EditorGeneratedOutputSummary? GeneratedOutputSummary
    {
        get => _generatedOutputSummary;
        private set
        {
            if (!SetProperty(ref _generatedOutputSummary, value))
                return;

            OnPropertyChanged(nameof(HasGeneratedOutputInspection));
            OnPropertyChanged(nameof(HasGeneratedOutputFileOnDisk));
            OnPropertyChanged(nameof(HasGeneratedOutputGeometry));
            OnPropertyChanged(nameof(GeneratedOutputFileHealthSummary));
            OnPropertyChanged(nameof(GeneratedOutputFileSizeSummary));
            OnPropertyChanged(nameof(GeneratedOutputLastModifiedSummary));
            OnPropertyChanged(nameof(GeneratedOutputPreviewGeometrySummary));
            OnPropertyChanged(nameof(GeneratedOutputEntityMixSummary));
            OnPropertyChanged(nameof(GeneratedOutputUnsupportedEntitySummary));
            OnPropertyChanged(nameof(GeneratedOutputBoundsSummary));
            OnPropertyChanged(nameof(OutputStatusSummary));
            OnPropertyChanged(nameof(OutputPreviewButtonLabel));
        }
    }

    public Editor2DPreviewDocument? TwoDDocument
    {
        get => _twoDWorkspace.IsInitialized ? _twoDWorkspace.Document : null;
        set
        {
            var normalizedDocument = ApplyExpandedRectangleOverrides(value);
            var currentDocument = TwoDDocument;
            var documentChanged = !Equals(currentDocument, normalizedDocument);
            if (documentChanged)
            {
                if (normalizedDocument is null)
                    _twoDWorkspace.ClearDocument();
                else
                    _twoDWorkspace.SetDocument(normalizedDocument);
            }
            SyncTwoDExpandedRectanglePathIds(normalizedDocument);
            if (!documentChanged)
                return;

            OnPropertyChanged(nameof(HasTwoDWorkspaceDocument));
            OnPropertyChanged(nameof(HasTwoDPreview));
            OnPropertyChanged(nameof(HasNoTwoDPreview));
            OnPropertyChanged(nameof(TwoDLayers));
            OnPropertyChanged(nameof(TwoDActiveLayerId));
            OnPropertyChanged(nameof(TwoDHiddenPathIds));
            OnPropertyChanged(nameof(TwoDViewportSummary));
            OnPropertyChanged(nameof(TwoDSelectionSummary));
            OnPropertyChanged(nameof(OutputStatusSummary));
            OnPropertyChanged(nameof(OutputPreviewButtonLabel));
            OnPropertyChanged(nameof(CanFrameHome));
            OnPropertyChanged(nameof(WorkspaceModeHint));
            OnPropertyChanged(nameof(HasTwoDConvertibleLineSelection));
            OnPropertyChanged(nameof(CanApplyTwoDConvertLines));
            OnPropertyChanged(nameof(TwoDConvertLineSummary));
            OnPropertyChanged(nameof(HasTwoDCurveOffsetSelection));
            OnPropertyChanged(nameof(CanApplyTwoDOffset));
            OnPropertyChanged(nameof(TwoDOffsetSummary));
            OnPropertyChanged(nameof(HasTwoDThicknessSourceSelection));
            OnPropertyChanged(nameof(CanApplyTwoDAddThickness));
            OnPropertyChanged(nameof(TwoDAddThicknessSummary));
            OnPropertyChanged(nameof(HasTwoDCleanupCandidates));
            OnPropertyChanged(nameof(CanApplyTwoDCleanup));
            OnPropertyChanged(nameof(TwoDCleanupSummary));
            OnPropertyChanged(nameof(CanApplyTwoDBoolean));
            OnPropertyChanged(nameof(CanApplyTwoDStrokeToFill));
            OnPropertyChanged(nameof(CanApplyTwoDFillToStroke));
            OnPropertyChanged(nameof(CanApplyTwoDPattern));
            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(CanApplyTwoDPaperFoldingCreases));
            OnPropertyChanged(nameof(CanApplyTwoDGlueTabs));
            OnPropertyChanged(nameof(TwoDPaperFoldingSummary));
            RefreshDerivedTwoDMeasurements(normalizedDocument);
            SyncTwoDSelectedTextEditorState();

            if (!_suppressTwoDDocumentPersistence)
            {
                if (string.IsNullOrWhiteSpace(LastGeneratedOutputPath))
                    Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
                else
                    RequestTwoDDocumentPersistence(TimeSpan.FromMilliseconds(80));
            }
        }
    }

    public Editor2DTool TwoDActiveTool
    {
        get => _twoDWorkspace.ActiveTool;
        set
        {
            if (_twoDWorkspace.ActiveTool == value)
            {
                if (value == Editor2DTool.Move)
                {
                    TwoDMoveCreateCopy = false;
                    ResetTwoDMovePointToPoint();
                }
                else if (value == Editor2DTool.Mirror)
                {
                    ResetTwoDMirrorStaging();
                }
                return;
            }

            if (_twoDWorkspace.ActiveTool == Editor2DTool.Mirror || value == Editor2DTool.Mirror)
                ResetTwoDMirrorStaging();
            _twoDWorkspace.SetActiveTool(value);
            if (value == Editor2DTool.Move)
                TwoDMoveCreateCopy = false;
            ResetTwoDMovePointToPoint();
            ClearTwoDCircularPatternPivot();
            TwoDPatternGuidePathId = null;
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
            ClearTwoDScalePivot();
            OnPropertyChanged();

            SyncSidebarToolStates();
            OnPropertyChanged(nameof(ActiveToolLabel));
            OnPropertyChanged(nameof(IsTwoDSelectToolActive));
            OnPropertyChanged(nameof(IsTwoDMoveToolActive));
            OnPropertyChanged(nameof(IsTwoDPanToolActive));
            OnPropertyChanged(nameof(IsTwoDMeasureToolActive));
            OnPropertyChanged(nameof(IsTwoDDimensionToolActive));
            OnPropertyChanged(nameof(IsTwoDLineToolActive));
            OnPropertyChanged(nameof(IsTwoDRectangleToolActive));
            OnPropertyChanged(nameof(IsTwoDCircleToolActive));
            OnPropertyChanged(nameof(IsTwoDPolygonToolActive));
            OnPropertyChanged(nameof(IsTwoDTextToolActive));
            OnPropertyChanged(nameof(IsTwoDPenToolActive));
            OnPropertyChanged(nameof(IsTwoDScaleToolActive));
            OnPropertyChanged(nameof(IsTwoDMirrorToolActive));
            OnPropertyChanged(nameof(IsTwoDTrimToolActive));
            OnPropertyChanged(nameof(IsTwoDFilletToolActive));
            OnPropertyChanged(nameof(IsTwoDChamferToolActive));
            OnPropertyChanged(nameof(IsTwoDConvertLinesToolActive));
            OnPropertyChanged(nameof(IsTwoDOffsetToolActive));
            OnPropertyChanged(nameof(IsTwoDAddThicknessToolActive));
            OnPropertyChanged(nameof(IsTwoDCleanupToolActive));
            OnPropertyChanged(nameof(IsTwoDPatternToolActive));
            OnPropertyChanged(nameof(IsTwoDPaperFoldingToolActive));
            OnPropertyChanged(nameof(IsTwoDSewingHoleToolActive));
            OnPropertyChanged(nameof(TwoDToolHint));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public int TwoDPolygonSides
    {
        get => _twoDPolygonSides;
        set
        {
            var normalized = Math.Clamp(value, 3, 64);
            if (!SetWorkspaceFacadeValue(_twoDPolygonSides, normalized, updated => _twoDPolygonSides = updated))
                return;

            OnPropertyChanged(nameof(TwoDPolygonSidesSummary));
            SyncTwoDWorkspaceState(recordHistory: false);
            if (!_suppressTwoDViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public bool TwoDSnapEnabled
    {
        get => _twoDWorkspace.SnapEnabled;
        set
        {
            if (_twoDWorkspace.SnapEnabled == value)
                return;

            _twoDWorkspace.SetSnapEnabled(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDSnappingSummary));
            SyncTwoDWorkspaceState(recordHistory: false);
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public string TwoDSnappingSummary => TwoDSnapEnabled ? "Snapping: On (N)" : "Snapping: Off (N)";

    public void ToggleTwoDSnapping() => TwoDSnapEnabled = !TwoDSnapEnabled;

    public bool TwoDGridVisible
    {
        get => _twoDWorkspace.GridVisible;
        set
        {
            if (_twoDWorkspace.GridVisible == value)
                return;

            _twoDWorkspace.SetGridVisible(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDGridSummary));
            SyncTwoDWorkspaceState(recordHistory: false);
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public string TwoDGridSummary => TwoDGridVisible ? "Grid: On (Shift+G)" : "Grid: Off (Shift+G)";

    public void ToggleTwoDGrid() => TwoDGridVisible = !TwoDGridVisible;

    public bool TwoDChainSelectionEnabled
    {
        get => _twoDWorkspace.ChainSelectionEnabled;
        set
        {
            if (_twoDWorkspace.ChainSelectionEnabled == value)
                return;

            _twoDWorkspace.SetChainSelectionEnabled(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDChainSelectionSummary));
            SyncTwoDWorkspaceState(recordHistory: false);
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public string TwoDChainSelectionSummary => TwoDChainSelectionEnabled ? "Chain selection: On (A)" : "Chain selection: Off (A)";

    public void ToggleTwoDChainSelection() => TwoDChainSelectionEnabled = !TwoDChainSelectionEnabled;

    public IReadOnlyList<string> TwoDSelectedPathIds
    {
        get => _twoDWorkspace.SelectedPathIds;
        set
        {
            var normalized = value
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (_twoDWorkspace.SelectedPathIds.SequenceEqual(normalized, StringComparer.Ordinal))
                return;

            _twoDWorkspace.SetSelection(normalized);
            if (normalized.Length == 0)
                ResetTwoDMovePointToPoint();
            TwoDPatternGuidePathId = null;
            OnPropertyChanged();

            OnPropertyChanged(nameof(HasTwoDSelection));
            OnPropertyChanged(nameof(TwoDSelectionCount));
            OnPropertyChanged(nameof(CanApplyTwoDScale));
            OnPropertyChanged(nameof(CanConfirmTwoDMirror));
            OnPropertyChanged(nameof(TwoDMirrorStageHint));
            OnPropertyChanged(nameof(TwoDMirrorObjectSummary));
            OnPropertyChanged(nameof(TwoDSelectedRectangleCount));
            OnPropertyChanged(nameof(CanExpandTwoDRectangles));
            OnPropertyChanged(nameof(TwoDSelectionSummary));
            OnPropertyChanged(nameof(TwoDToolHint));
            OnPropertyChanged(nameof(HasTwoDConvertibleLineSelection));
            OnPropertyChanged(nameof(CanApplyTwoDConvertLines));
            OnPropertyChanged(nameof(TwoDConvertLineSummary));
            OnPropertyChanged(nameof(HasTwoDCurveOffsetSelection));
            OnPropertyChanged(nameof(CanApplyTwoDOffset));
            OnPropertyChanged(nameof(TwoDOffsetSummary));
            OnPropertyChanged(nameof(HasTwoDThicknessSourceSelection));
            OnPropertyChanged(nameof(CanApplyTwoDAddThickness));
            OnPropertyChanged(nameof(TwoDAddThicknessSummary));
            OnPropertyChanged(nameof(HasTwoDCleanupCandidates));
            OnPropertyChanged(nameof(CanApplyTwoDCleanup));
            OnPropertyChanged(nameof(TwoDCleanupSummary));
            OnPropertyChanged(nameof(CanApplyTwoDStrokeToFill));
            OnPropertyChanged(nameof(CanApplyTwoDFillToStroke));
            OnPropertyChanged(nameof(CanApplyTwoDPattern));
            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
            OnPropertyChanged(nameof(CanApplyTwoDPaperFoldingCreases));
            OnPropertyChanged(nameof(CanApplyTwoDGlueTabs));
            OnPropertyChanged(nameof(TwoDPaperFoldingSummary));
            SyncTwoDSelectedTextEditorState();
        }
    }

    public IReadOnlyList<Editor2DMeasurement> TwoDMeasurements
    {
        get => _twoDWorkspace.Measurements;
        set
        {
            var normalized = value
                .DistinctBy(static measurement => measurement.Id)
                .ToArray();
            if (_twoDWorkspace.Measurements.SequenceEqual(normalized))
                return;

            var previousSelectedMeasurementId = TwoDSelectedMeasurementId;
            _twoDWorkspace.SetMeasurements(
                normalized,
                previousSelectedMeasurementId,
                recordHistory: !_isRefreshingDerivedTwoDMeasurements && !_isApplyingTwoDWorkspaceState);
            OnPropertyChanged();
            if (!string.Equals(
                    previousSelectedMeasurementId,
                    TwoDSelectedMeasurementId,
                    StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(TwoDSelectedMeasurementId));
                OnPropertyChanged(nameof(HasTwoDSelectedMeasurement));
            }

            OnPropertyChanged(nameof(HasTwoDMeasurements));
            OnPropertyChanged(nameof(TwoDAutoDimensionCount));
            OnPropertyChanged(nameof(TwoDMeasurementSummary));
            OnPropertyChanged(nameof(TwoDSelectedMeasurementExpressionText));
            OnPropertyChanged(nameof(TwoDSelectedMeasurementDriven));
            OnPropertyChanged(nameof(TwoDToolHint));
        }
    }

    public string? TwoDSelectedMeasurementId
    {
        get => _twoDWorkspace.SelectedMeasurementId;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(_twoDWorkspace.SelectedMeasurementId, normalized, StringComparison.Ordinal))
                return;

            _twoDWorkspace.SetSelectedMeasurement(normalized);
            OnPropertyChanged();

            OnPropertyChanged(nameof(HasTwoDSelectedMeasurement));
            OnPropertyChanged(nameof(TwoDSelectedMeasurementExpressionText));
            OnPropertyChanged(nameof(TwoDSelectedMeasurementDriven));
            OnPropertyChanged(nameof(TwoDMeasurementSummary));
        }
    }

    public string TwoDSelectedMeasurementExpressionText
    {
        get
        {
            var measurement = TwoDMeasurements.FirstOrDefault(item => item.Id == TwoDSelectedMeasurementId);
            return measurement?.Expression ?? measurement?.Distance.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
        }
        set
        {
            if (string.IsNullOrWhiteSpace(TwoDSelectedMeasurementId))
                return;
            if (!_twoDWorkspace.TrySetMeasurementExpression(TwoDSelectedMeasurementId, value ?? string.Empty, out var error))
            {
                StatusText = error;
                return;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDMeasurementSummary));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        }
    }

    public bool TwoDSelectedMeasurementDriven
    {
        get => TwoDMeasurements.FirstOrDefault(item => item.Id == TwoDSelectedMeasurementId)?.Driven == true;
        set
        {
            if (string.IsNullOrWhiteSpace(TwoDSelectedMeasurementId)
                || !_twoDWorkspace.SetMeasurementDriven(TwoDSelectedMeasurementId, value))
                return;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDSelectedMeasurementExpressionText));
            OnPropertyChanged(nameof(TwoDMeasurementSummary));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        }
    }

    public double TwoDViewportZoom
    {
        get => _twoDViewportZoom;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDViewportZoom, value, updated => _twoDViewportZoom = updated))
                return;

            OnPropertyChanged(nameof(TwoDViewportSummary));
            SyncTwoDWorkspaceState(recordHistory: false);
            if (!_suppressTwoDViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public double TwoDViewportOffsetX
    {
        get => _twoDViewportOffsetX;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDViewportOffsetX, value, updated => _twoDViewportOffsetX = updated))
                return;

            SyncTwoDWorkspaceState(recordHistory: false);
            if (!_suppressTwoDViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public double TwoDViewportOffsetY
    {
        get => _twoDViewportOffsetY;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDViewportOffsetY, value, updated => _twoDViewportOffsetY = updated))
                return;

            SyncTwoDWorkspaceState(recordHistory: false);
            if (!_suppressTwoDViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public int TwoDFrameRequestToken
    {
        get => _twoDFrameRequestToken;
        private set => SetWorkspaceFacadeValue(_twoDFrameRequestToken, value, updated => _twoDFrameRequestToken = updated);
    }

    public bool HasGeneratedOutput => !string.IsNullOrWhiteSpace(LastGeneratedOutputPath);

    public bool HasGeneratedOutputInspection => GeneratedOutputSummary is not null;

    public bool HasGeneratedOutputContext => GeneratedOutputContext is not null;

    public bool HasGeneratedOutputFileOnDisk => GeneratedOutputSummary?.FileExists == true;

    public bool HasGeneratedOutputGeometry => GeneratedOutputSummary is { FileExists: true, PreviewPathCount: > 0 };

    public bool CanRefreshGeneratedOutput => HasGeneratedOutput;

    public string GeneratedOutputFileName => HasGeneratedOutput
        ? Path.GetFileName(LastGeneratedOutputPath) ?? string.Empty
        : "No generated output yet";

    public string GeneratedOutputFolder => HasGeneratedOutput
        ? Path.GetDirectoryName(LastGeneratedOutputPath) ?? string.Empty
        : string.Empty;

    public string GeneratedOutputSourceSummary => GeneratedOutputContext is null
        ? string.Empty
        : $"{GeneratedOutputContext.SourceTool} / {GeneratedOutputContext.TriggerLabel}";

    public string GeneratedOutputScopeSummary => GeneratedOutputContext?.ScopeSummary ?? string.Empty;

    public string GeneratedOutputConfigurationSummary => GeneratedOutputContext?.ConfigurationSummary ?? string.Empty;

    public string GeneratedOutputCreatedSummary => GeneratedOutputContext is null
        ? string.Empty
        : $"Generated: {GeneratedOutputContext.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";

    public bool HasTwoDPreview => TwoDDocument is { Paths.Count: > 0 };

    public bool HasTwoDWorkspaceDocument => TwoDDocument is not null;

    public bool HasNoTwoDPreview => !HasTwoDPreview;

    public bool IsTwoDSelectToolActive => TwoDActiveTool == Editor2DTool.Select;

    public bool IsTwoDMoveToolActive => TwoDActiveTool == Editor2DTool.Move;

    private bool _twoDMoveCreateCopy;

    public bool TwoDMoveCreateCopy
    {
        get => _twoDMoveCreateCopy;
        set => SetProperty(ref _twoDMoveCreateCopy, value);
    }

    private bool _twoDMovePointToPointActive;
    private Editor2DPoint? _twoDMovePointToPointSource;

    public bool TwoDMovePointToPointActive
    {
        get => _twoDMovePointToPointActive;
        set
        {
            if (!SetProperty(ref _twoDMovePointToPointActive, value))
                return;
            if (!value)
                TwoDMovePointToPointSource = null;
            OnPropertyChanged(nameof(TwoDMovePointToPointActionLabel));
            OnPropertyChanged(nameof(TwoDMovePointToPointSummary));
        }
    }

    public Editor2DPoint? TwoDMovePointToPointSource
    {
        get => _twoDMovePointToPointSource;
        set
        {
            if (!SetProperty(ref _twoDMovePointToPointSource, value))
                return;
            OnPropertyChanged(nameof(TwoDMovePointToPointActionLabel));
            OnPropertyChanged(nameof(TwoDMovePointToPointSummary));
        }
    }

    public string TwoDMovePointToPointActionLabel => !TwoDMovePointToPointActive
        ? "Start Point-to-Point"
        : TwoDMovePointToPointSource is null ? "Click source point…" : "Click destination…";

    public string TwoDMovePointToPointSummary => !TwoDMovePointToPointActive
        ? "Move selection by picking exact source and destination points."
        : TwoDMovePointToPointSource is null
            ? "Pick source point on canvas."
            : $"Source: {TwoDMovePointToPointSource.X:0.###}, {TwoDMovePointToPointSource.Y:0.###} mm";

    public void ToggleTwoDMovePointToPoint()
    {
        if (!HasTwoDSelection)
        {
            StatusText = "Select one or more 2D entities before starting point-to-point move";
            return;
        }

        TwoDMovePointToPointSource = null;
        TwoDMovePointToPointActive = !TwoDMovePointToPointActive;
    }

    private void ResetTwoDMovePointToPoint()
    {
        TwoDMovePointToPointActive = false;
        TwoDMovePointToPointSource = null;
    }

    public string TwoDPrecisionDeltaXText
    {
        get => _twoDPrecisionDeltaXText;
        set => SetWorkspaceFacadeValue(_twoDPrecisionDeltaXText, value ?? string.Empty, updated => _twoDPrecisionDeltaXText = updated);
    }

    public string TwoDPrecisionDeltaYText
    {
        get => _twoDPrecisionDeltaYText;
        set => SetWorkspaceFacadeValue(_twoDPrecisionDeltaYText, value ?? string.Empty, updated => _twoDPrecisionDeltaYText = updated);
    }

    public string TwoDPrecisionRotationText
    {
        get => _twoDPrecisionRotationText;
        set => SetWorkspaceFacadeValue(_twoDPrecisionRotationText, value ?? string.Empty, updated => _twoDPrecisionRotationText = updated);
    }

    public bool ApplyTwoDPreciseTransform()
    {
        if (!TryParseTwoDPrecisionValue(TwoDPrecisionDeltaXText, out var deltaX)
            || !TryParseTwoDPrecisionValue(TwoDPrecisionDeltaYText, out var deltaY)
            || !TryParseTwoDPrecisionValue(TwoDPrecisionRotationText, out var rotation))
        {
            StatusText = "Enter valid ΔX, ΔY, and rotation values";
            return false;
        }

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyPreciseTransform(deltaX, deltaY, rotation));
    }

    private static bool TryParseTwoDPrecisionValue(string? text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    public bool IsTwoDPanToolActive => TwoDActiveTool == Editor2DTool.Pan;

    public bool IsTwoDMeasureToolActive => TwoDActiveTool == Editor2DTool.Measure;

    public bool IsTwoDDimensionToolActive => TwoDActiveTool == Editor2DTool.Dimension;

    public bool IsTwoDLineToolActive => TwoDActiveTool == Editor2DTool.SketchLine;

    public bool IsTwoDRectangleToolActive => TwoDActiveTool == Editor2DTool.SketchRectangle;

    public bool IsTwoDCircleToolActive => TwoDActiveTool == Editor2DTool.SketchCircle;

    public bool IsTwoDPolygonToolActive => TwoDActiveTool == Editor2DTool.SketchPolygon;

    public bool IsTwoDTextToolActive => TwoDActiveTool == Editor2DTool.SketchText;

    public bool IsTwoDPenToolActive => TwoDActiveTool == Editor2DTool.Pen;

    public bool IsTwoDScaleToolActive => TwoDActiveTool == Editor2DTool.Scale;

    public string TwoDScaleFactorText
    {
        get => _twoDScaleFactorText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDScaleFactorText, value ?? string.Empty, updated => _twoDScaleFactorText = updated))
                return;
            OnPropertyChanged(nameof(CanApplyTwoDScale));
        }
    }

    public bool TwoDScaleFromCenter
    {
        get => _twoDScaleFromCenter;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDScaleFromCenter, value, updated => _twoDScaleFromCenter = updated))
                return;
            OnPropertyChanged(nameof(TwoDScalePivotSummary));
        }
    }

    public bool CanApplyTwoDScale
        => HasTwoDSelection
            && TryParseTwoDPrecisionValue(TwoDScaleFactorText, out var factor)
            && double.IsFinite(factor)
            && factor > 0.0;

    public Editor2DPoint? TwoDScalePivot
    {
        get => _twoDScalePivot;
        set
        {
            if (Equals(_twoDScalePivot, value))
                return;
            _twoDScalePivot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDScalePivotSummary));
        }
    }

    public bool TwoDScalePivotPicking
    {
        get => _twoDScalePivotPicking;
        set => SetWorkspaceFacadeValue(_twoDScalePivotPicking, value, updated => _twoDScalePivotPicking = updated);
    }

    public string TwoDScalePivotSummary => TwoDScalePivot is { } pivot
        ? $"Pivot: ({pivot.X:0.###}, {pivot.Y:0.###})"
        : TwoDScaleFromCenter
            ? "Pivot: selection center"
            : "Pivot: selection lower-left corner";

    public bool ApplyTwoDScale()
    {
        if (!TryParseTwoDPrecisionValue(TwoDScaleFactorText, out var factor)
            || !double.IsFinite(factor)
            || factor <= 0.0)
        {
            StatusText = "Enter a positive finite scale factor";
            return false;
        }

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyScale(factor, TwoDScaleFromCenter, TwoDScalePivot));
    }

    public void PickTwoDScalePivot()
    {
        if (!IsTwoDScaleToolActive)
            return;
        TwoDScalePivotPicking = true;
        StatusText = "Click a point on the canvas for the scale pivot";
    }

    private void ClearTwoDScalePivot()
    {
        TwoDScalePivot = null;
        TwoDScalePivotPicking = false;
    }

    public bool IsTwoDMirrorToolActive => TwoDActiveTool == Editor2DTool.Mirror;

    public bool TwoDMirrorLineMode
    {
        get => _twoDMirrorLineMode;
        set
        {
            if (_twoDMirrorLineMode == value)
                return;
            _twoDMirrorLineMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDMirrorStageHint));
        }
    }

    public Editor2DPoint? TwoDMirrorAxisStart
    {
        get => _twoDMirrorAxisStart;
        set
        {
            if (Equals(_twoDMirrorAxisStart, value))
                return;
            _twoDMirrorAxisStart = value;
            OnPropertyChanged();
            NotifyTwoDMirrorStageChanged();
        }
    }

    public Editor2DPoint? TwoDMirrorAxisEnd
    {
        get => _twoDMirrorAxisEnd;
        set
        {
            if (Equals(_twoDMirrorAxisEnd, value))
                return;
            _twoDMirrorAxisEnd = value;
            OnPropertyChanged();
            NotifyTwoDMirrorStageChanged();
        }
    }

    public string TwoDMirrorStageHint
        => !HasTwoDSelection
            ? "Objects mode: click shapes to mirror."
            : !TwoDMirrorLineMode && TwoDMirrorAxisStart is null
                ? "Switch to Mirror Line, then pick the axis."
                : TwoDMirrorLineMode && TwoDMirrorAxisStart is null
                    ? "Click a line (or two points) for the mirror axis."
                    : TwoDMirrorAxisEnd is null
                        ? "Click the second axis point."
                        : "Adjust options, then Confirm Mirror.";

    public string TwoDMirrorObjectSummary
        => $"Objects: {TwoDSelectionCount}";

    public string TwoDMirrorAxisSummary
        => TwoDMirrorAxisEnd is null ? "Mirror line: —" : "Mirror line: set";

    public bool CanConfirmTwoDMirror
        => HasTwoDSelection
            && TwoDMirrorAxisStart is { } start
            && TwoDMirrorAxisEnd is { } end
            && Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2)) > 1e-8;

    public bool ConfirmTwoDMirror()
    {
        if (TwoDMirrorAxisStart is not { } start || TwoDMirrorAxisEnd is not { } end)
        {
            StatusText = "Select objects and pick a valid mirror axis";
            return false;
        }

        var completed = CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyMirror(start, end));
        if (completed)
            ResetTwoDMirrorStaging();
        return completed;
    }

    public void CancelTwoDMirror(bool exitTool = false)
    {
        ResetTwoDMirrorStaging();
        if (exitTool && IsTwoDMirrorToolActive)
            TwoDActiveTool = Editor2DTool.Select;
    }

    public void ClearTwoDMirrorObjects() => TwoDSelectedPathIds = [];

    public void ClearTwoDMirrorAxis()
    {
        TwoDMirrorAxisStart = null;
        TwoDMirrorAxisEnd = null;
    }

    private void ResetTwoDMirrorStaging()
    {
        _twoDMirrorLineMode = false;
        _twoDMirrorAxisStart = null;
        _twoDMirrorAxisEnd = null;
        OnPropertyChanged(nameof(TwoDMirrorLineMode));
        OnPropertyChanged(nameof(TwoDMirrorAxisStart));
        OnPropertyChanged(nameof(TwoDMirrorAxisEnd));
        NotifyTwoDMirrorStageChanged();
    }

    private void NotifyTwoDMirrorStageChanged()
    {
        OnPropertyChanged(nameof(TwoDMirrorStageHint));
        OnPropertyChanged(nameof(CanConfirmTwoDMirror));
        OnPropertyChanged(nameof(TwoDMirrorAxisSummary));
    }

    public bool IsTwoDTrimToolActive => TwoDActiveTool == Editor2DTool.Trim;

    public bool IsTwoDFilletToolActive => TwoDActiveTool == Editor2DTool.Fillet;

    public bool IsTwoDChamferToolActive => TwoDActiveTool == Editor2DTool.Chamfer;

    public bool IsTwoDConvertLinesToolActive => TwoDActiveTool == Editor2DTool.ConvertLines;

    public bool IsTwoDOffsetToolActive => TwoDActiveTool == Editor2DTool.Offset;

    public bool IsTwoDAddThicknessToolActive => TwoDActiveTool == Editor2DTool.AddThickness;

    public bool IsTwoDCleanupToolActive => TwoDActiveTool == Editor2DTool.Cleanup;

    public bool IsTwoDPatternToolActive => TwoDActiveTool == Editor2DTool.Patterning;

    public bool IsTwoDPaperFoldingToolActive => TwoDActiveTool == Editor2DTool.PaperFolding;

    public bool IsTwoDSewingHoleToolActive => TwoDActiveTool == Editor2DTool.AddSewingHoles;

    public double TwoDSewingHoleMargin
    {
        get => _twoDSewingHoleMargin;
        set
        {
            var normalized = Math.Max(0.0, value);
            if (!SetWorkspaceFacadeValue(_twoDSewingHoleMargin, normalized, updated => _twoDSewingHoleMargin = updated))
                return;

            OnPropertyChanged();
            SyncTwoDWorkspaceState(recordHistory: false);
        }
    }

    public bool HasTwoDSelection => TwoDSelectedPathIds.Count > 0;

    public int TwoDSelectionCount => TwoDSelectedPathIds.Count;

    public int TwoDSelectedRectangleCount
        => TwoDDocument is null
            ? 0
            : TwoDDocument.Paths.Count(path =>
                path.IsAxisAlignedRectangle
                && TwoDSelectedPathIds.Contains(path.Id, StringComparer.Ordinal));

    public bool CanExpandTwoDRectangles => TwoDSelectedRectangleCount > 0;

    public bool CanApplyTwoDBoolean
        => TwoDDocument is not null
            && TwoDSelectedPathIds.Count >= 2
            && TwoDSelectedPathIds.All(id => TwoDDocument.Paths.Any(path =>
                path.Id.Equals(id, StringComparison.Ordinal) && path.IsClosed && path.Points.Count >= 3));

    public bool CanApplyTwoDStrokeToFill
        => TwoDDocument is not null
            && TwoDSelectedPathIds.Any(id => TwoDDocument.Paths.Any(path =>
                path.Id.Equals(id, StringComparison.Ordinal) && path.IsClosed && !path.IsFilled));

    public bool CanApplyTwoDFillToStroke
        => TwoDDocument is not null
            && TwoDSelectedPathIds.Any(id => TwoDDocument.Paths.Any(path =>
                path.Id.Equals(id, StringComparison.Ordinal) && path.IsClosed && path.IsFilled));

    public bool HasTwoDMeasurements => TwoDMeasurements.Any(static measurement => !measurement.IsAutoDimension);

    public bool HasTwoDSelectedMeasurement => !string.IsNullOrWhiteSpace(TwoDSelectedMeasurementId);

    public int TwoDAutoDimensionCount => TwoDMeasurements.Count(static measurement => measurement.IsAutoDimension);

    public bool HasSingleTwoDTextSelection => TryGetSingleSelectedTwoDTextPath(out _);

    public IReadOnlyList<string> TwoDConvertLineStyleOptions => TwoDConvertLineStyleOrder;

    public string TwoDConvertLineStyle
    {
        get => _twoDConvertLineStyle;
        set
        {
            var normalized = NormalizeTwoDConvertLineStyle(value);
            if (!SetWorkspaceFacadeValue(_twoDConvertLineStyle, normalized, updated => _twoDConvertLineStyle = updated))
                return;

            NotifyTwoDConvertLineParameterStateChanged();
        }
    }

    public bool HasTwoDConvertibleLineSelection => GetTwoDConvertibleSelectionCount() > 0;

    public bool CanApplyTwoDConvertLines => HasTwoDConvertibleLineSelection;

    public string TwoDConvertLineSummary
    {
        get
        {
            var convertibleSelectionCount = GetTwoDConvertibleSelectionCount();
            return convertibleSelectionCount == 0
                ? "Select LINE, LWPOLYLINE, or POLYLINE geometry to apply a native pattern conversion."
                : $"{convertibleSelectionCount} convertible entit{(convertibleSelectionCount == 1 ? "y" : "ies")} selected. Apply {TwoDConvertLineStyle} geometry to replace the current linework.";
        }
    }

    public bool HasTwoDConvertLineFirstParameter => GetTwoDConvertLineParameterDefinitions(TwoDConvertLineStyle).Length >= 1;

    public bool HasTwoDConvertLineSecondParameter => GetTwoDConvertLineParameterDefinitions(TwoDConvertLineStyle).Length >= 2;

    public bool HasTwoDConvertLineThirdParameter => GetTwoDConvertLineParameterDefinitions(TwoDConvertLineStyle).Length >= 3;

    public string TwoDConvertLineFirstParameterLabel => GetTwoDConvertLineParameterLabel(0);

    public string TwoDConvertLineSecondParameterLabel => GetTwoDConvertLineParameterLabel(1);

    public string TwoDConvertLineThirdParameterLabel => GetTwoDConvertLineParameterLabel(2);

    public string TwoDConvertLineFirstParameterText
    {
        get => GetTwoDConvertLineParameterText(0);
        set => SetTwoDConvertLineParameterText(0, value);
    }

    public string TwoDConvertLineSecondParameterText
    {
        get => GetTwoDConvertLineParameterText(1);
        set => SetTwoDConvertLineParameterText(1, value);
    }

    public string TwoDConvertLineThirdParameterText
    {
        get => GetTwoDConvertLineParameterText(2);
        set => SetTwoDConvertLineParameterText(2, value);
    }

    public IReadOnlyList<string> TwoDOffsetModeOptionItems => TwoDOffsetModeOptions;

    public IReadOnlyList<string> TwoDOffsetSideOptionItems => TwoDOffsetSideOptions;

    public string TwoDOffsetMode
    {
        get => _twoDOffsetMode;
        set
        {
            var normalized = NormalizeTwoDOffsetMode(value);
            if (!SetWorkspaceFacadeValue(_twoDOffsetMode, normalized, updated => _twoDOffsetMode = updated))
                return;

            OnPropertyChanged(nameof(IsTwoDCurveOffsetMode));
            OnPropertyChanged(nameof(IsTwoDBBoxOffsetMode));
            OnPropertyChanged(nameof(HasTwoDCurveOffsetSelection));
            OnPropertyChanged(nameof(CanApplyTwoDOffset));
            OnPropertyChanged(nameof(TwoDOffsetSummary));
        }
    }

    public string TwoDOffsetSide
    {
        get => _twoDOffsetSide;
        set
        {
            var normalized = NormalizeTwoDOffsetSide(value);
            if (!SetWorkspaceFacadeValue(_twoDOffsetSide, normalized, updated => _twoDOffsetSide = updated))
                return;

            OnPropertyChanged(nameof(TwoDOffsetSummary));
        }
    }

    public string TwoDOffsetDistanceText
    {
        get => _twoDOffsetDistanceText;
        set => SetWorkspaceFacadeValue(_twoDOffsetDistanceText, value ?? string.Empty, updated => _twoDOffsetDistanceText = updated);
    }

    public string TwoDOffsetBBoxDistanceText
    {
        get => _twoDOffsetBBoxDistanceText;
        set => SetWorkspaceFacadeValue(_twoDOffsetBBoxDistanceText, value ?? string.Empty, updated => _twoDOffsetBBoxDistanceText = updated);
    }

    public string TwoDOffsetBBoxFilletText
    {
        get => _twoDOffsetBBoxFilletText;
        set => SetWorkspaceFacadeValue(_twoDOffsetBBoxFilletText, value ?? string.Empty, updated => _twoDOffsetBBoxFilletText = updated);
    }

    public bool IsTwoDCurveOffsetMode => string.Equals(TwoDOffsetMode, "Curve", StringComparison.Ordinal);

    public bool IsTwoDBBoxOffsetMode => string.Equals(TwoDOffsetMode, "BBox", StringComparison.Ordinal);

    public bool HasTwoDCurveOffsetSelection => GetTwoDCurveOffsetSelectionCount() > 0;

    public bool CanApplyTwoDOffset
        => IsTwoDBBoxOffsetMode
            ? HasTwoDSelection
            : HasTwoDCurveOffsetSelection;

    public string TwoDOffsetSummary
    {
        get
        {
            if (IsTwoDBBoxOffsetMode)
            {
                return HasTwoDSelection
                    ? $"BBox offset will wrap the current selection in a new rectangular profile. Rounded corners use the current fillet value."
                    : "Select one or more 2D entities to create a bounding-box offset profile.";
            }

            var curveOffsetSelectionCount = GetTwoDCurveOffsetSelectionCount();
            return curveOffsetSelectionCount == 0
                ? "Select LINE, LWPOLYLINE, POLYLINE, CIRCLE, or ARC geometry to add an OpenGeometry offset copy."
                : $"{curveOffsetSelectionCount} offsettable entit{(curveOffsetSelectionCount == 1 ? "y" : "ies")} selected. Open-path offsets follow path direction; closed paths expand or shrink.";
        }
    }

    public string TwoDAddThicknessWidthText
    {
        get => _twoDAddThicknessWidthText;
        set => SetWorkspaceFacadeValue(_twoDAddThicknessWidthText, value ?? string.Empty, updated => _twoDAddThicknessWidthText = updated);
    }

    public bool HasTwoDThicknessSourceSelection => GetTwoDThicknessSourcePathCount() > 0;

    public bool CanApplyTwoDAddThickness => GetTwoDThicknessCandidatePaths().Count > 0;

    public string TwoDAddThicknessSummary
    {
        get
        {
            var selectedSourceCount = GetTwoDThicknessSourcePathCount();
            if (TwoDSelectedPathIds.Count > 0)
            {
                return selectedSourceCount == 0
                    ? "The current selection does not contain open line or polyline centerlines that can be thickened."
                    : $"{selectedSourceCount} open line/polyline centerline{(selectedSourceCount == 1 ? string.Empty : "s")} selected. Apply Thickness to create closed outline geometry.";
            }

            var fallbackCount = GetTwoDThicknessCandidatePaths().Count;
            return fallbackCount == 0
                ? "No eligible open line/polyline centerlines are loaded in the 2D workspace."
                : $"No selection is active. Applying Thickness will process all {fallbackCount} eligible open line/polyline centerlines in the current 2D workspace.";
        }
    }

    public string TwoDCleanupToleranceText
    {
        get => _twoDCleanupToleranceText;
        set => SetWorkspaceFacadeValue(_twoDCleanupToleranceText, value ?? string.Empty, updated => _twoDCleanupToleranceText = updated);
    }

    public bool HasTwoDCleanupCandidates => GetTwoDCleanupCandidatePaths().Count > 0;

    public bool CanApplyTwoDCleanup => HasTwoDCleanupCandidates;

    public string TwoDCleanupSummary
    {
        get
        {
            var candidateCount = GetTwoDCleanupCandidatePaths().Count;
            return candidateCount == 0
                ? "No open line or polyline chains are available for Join/Cleanup."
                : $"Join/Cleanup will process {candidateCount} open line/polyline entit{(candidateCount == 1 ? "y" : "ies")} in the current 2D workspace. This local 2D pass joins nearby endpoints and removes degenerate open segments.";
        }
    }

    public IReadOnlyList<string> TwoDPatternModeOptionItems => TwoDPatternModeOptions;

    public string TwoDPatternMode
    {
        get => _twoDPatternMode;
        set
        {
            var normalized = NormalizeTwoDPatternMode(value);
            if (!SetWorkspaceFacadeValue(_twoDPatternMode, normalized, updated => _twoDPatternMode = updated))
                return;

            OnPropertyChanged(nameof(IsTwoDRectangularPatternMode));
            OnPropertyChanged(nameof(IsTwoDCircularPatternMode));
            OnPropertyChanged(nameof(IsTwoDPathPatternMode));
            OnPropertyChanged(nameof(TwoDPatternSummary));
            ClearTwoDCircularPatternPivot();
            TwoDPatternGuidePathId = null;
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public bool IsTwoDRectangularPatternMode => string.Equals(TwoDPatternMode, "Rectangular", StringComparison.Ordinal);

    public bool IsTwoDCircularPatternMode => string.Equals(TwoDPatternMode, "Circular", StringComparison.Ordinal);

    public bool IsTwoDPathPatternMode => string.Equals(TwoDPatternMode, "Path", StringComparison.Ordinal);

    public Editor2DPoint? TwoDPatternPivot
    {
        get => _twoDPatternPivot;
        set
        {
            if (Equals(_twoDPatternPivot, value))
                return;
            _twoDPatternPivot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TwoDPatternPivotSummary));
            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public bool TwoDPatternPivotPicking
    {
        get => _twoDPatternPivotPicking;
        set => SetWorkspaceFacadeValue(_twoDPatternPivotPicking, value, updated => _twoDPatternPivotPicking = updated);
    }

    public string TwoDPatternPivotSummary => TwoDPatternPivot is { } pivot
        ? $"pivot ({pivot.X:0.###}, {pivot.Y:0.###})"
        : "the selection center";

    public void PickTwoDCircularPatternPivot()
    {
        if (!IsTwoDCircularPatternMode)
            return;
        TwoDPatternPivotPicking = true;
        StatusText = "Click a point on the canvas for the circular pattern pivot";
    }

    private void ClearTwoDCircularPatternPivot()
    {
        TwoDPatternPivot = null;
        TwoDPatternPivotPicking = false;
    }

    public string? TwoDPatternGuidePathId
    {
        get => _twoDPatternGuidePathId;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (!SetWorkspaceFacadeValue(_twoDPatternGuidePathId, normalized, updated => _twoDPatternGuidePathId = updated))
                return;
            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternGuideSummary => TwoDPatternGuidePathId is null
        ? "Click guide path on canvas"
        : "Guide path selected";

    public string TwoDPatternCopiesXText
    {
        get => _twoDPatternCopiesXText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternCopiesXText, value ?? string.Empty, updated => _twoDPatternCopiesXText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternCopiesYText
    {
        get => _twoDPatternCopiesYText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternCopiesYText, value ?? string.Empty, updated => _twoDPatternCopiesYText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternSpacingXText
    {
        get => _twoDPatternSpacingXText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternSpacingXText, value ?? string.Empty, updated => _twoDPatternSpacingXText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternSpacingYText
    {
        get => _twoDPatternSpacingYText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternSpacingYText, value ?? string.Empty, updated => _twoDPatternSpacingYText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternCircularCountText
    {
        get => _twoDPatternCircularCountText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternCircularCountText, value ?? string.Empty, updated => _twoDPatternCircularCountText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternCircularAngleText
    {
        get => _twoDPatternCircularAngleText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternCircularAngleText, value ?? string.Empty, updated => _twoDPatternCircularAngleText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternPathCopiesText
    {
        get => _twoDPatternPathCopiesText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternPathCopiesText, value ?? string.Empty, updated => _twoDPatternPathCopiesText = updated))
                return;
            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public string TwoDPatternPathSpacingText
    {
        get => _twoDPatternPathSpacingText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDPatternPathSpacingText, value ?? string.Empty, updated => _twoDPatternPathSpacingText = updated))
                return;
            OnPropertyChanged(nameof(TwoDPatternSummary));
            OnPropertyChanged(nameof(TwoDPatternPreviewPaths));
        }
    }

    public bool CanApplyTwoDPattern => TwoDDocument is not null && HasTwoDSelection;

    public string TwoDPatternSummary
    {
        get
        {
            if (!HasTwoDSelection)
                return "Select source geometry before applying Pattern.";

            if (IsTwoDCircularPatternMode)
            {
                return $"Circular pattern will create {TwoDPatternCircularCountText} total instance(s) across {TwoDPatternCircularAngleText} deg around {TwoDPatternPivotSummary}. Count includes the source selection.";
            }

            if (IsTwoDPathPatternMode)
                return $"Path pattern will create {TwoDPatternPathCopiesText} copies at {TwoDPatternPathSpacingText} mm spacing. {TwoDPatternGuideSummary}.";

            return $"Rectangular pattern will create a {TwoDPatternCopiesXText} x {TwoDPatternCopiesYText} layout using {TwoDPatternSpacingXText} mm / {TwoDPatternSpacingYText} mm spacing. Counts include the source selection.";
        }
    }

    public IReadOnlyList<Editor2DPreviewPath> TwoDPatternPreviewPaths
        => TwoDActiveTool == Editor2DTool.Patterning
            ? _twoDWorkspace.GetPatternPreviewPaths(TwoDPatternPivot, TwoDPatternGuidePathId)
            : [];

    public IReadOnlyList<string> TwoDGlueTabTypeOptionItems => TwoDGlueTabTypeOptions;

    public IReadOnlyList<string> TwoDGlueTabSideOptionItems => TwoDGlueTabSideOptions;

    public string TwoDGlueTabType
    {
        get => _twoDGlueTabType;
        set
        {
            var normalized = NormalizeTwoDGlueTabType(value);
            if (!SetWorkspaceFacadeValue(_twoDGlueTabType, normalized, updated => _twoDGlueTabType = updated))
                return;

            OnPropertyChanged(nameof(TwoDPaperFoldingSummary));
        }
    }

    public string TwoDGlueTabSide
    {
        get => _twoDGlueTabSide;
        set
        {
            var normalized = NormalizeTwoDGlueTabSide(value);
            if (!SetWorkspaceFacadeValue(_twoDGlueTabSide, normalized, updated => _twoDGlueTabSide = updated))
                return;

            OnPropertyChanged(nameof(TwoDPaperFoldingSummary));
        }
    }

    public string TwoDGlueTabHeightText
    {
        get => _twoDGlueTabHeightText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDGlueTabHeightText, value ?? string.Empty, updated => _twoDGlueTabHeightText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPaperFoldingSummary));
        }
    }

    public string TwoDGlueTabStartOffsetText
    {
        get => _twoDGlueTabStartOffsetText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDGlueTabStartOffsetText, value ?? string.Empty, updated => _twoDGlueTabStartOffsetText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPaperFoldingSummary));
        }
    }

    public string TwoDGlueTabEndOffsetText
    {
        get => _twoDGlueTabEndOffsetText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDGlueTabEndOffsetText, value ?? string.Empty, updated => _twoDGlueTabEndOffsetText = updated))
                return;

            OnPropertyChanged(nameof(TwoDPaperFoldingSummary));
        }
    }

    public bool CanApplyTwoDPaperFoldingCreases => HasTwoDConvertibleLineSelection;

    public bool CanApplyTwoDGlueTabs => GetSelectedTwoDGlueTabPaths().Count > 0;

    public string TwoDPaperFoldingSummary
    {
        get
        {
            var creaseCount = GetTwoDConvertibleSelectionCount();
            var glueTabCount = GetSelectedTwoDGlueTabPaths().Count;
            if (!HasTwoDSelection)
                return "Select line or polyline geometry to convert it into dashed crease geometry, or select LINE entities to add glue tabs.";

            if (glueTabCount > 0)
            {
                return $"{glueTabCount} LINE entit{(glueTabCount == 1 ? "y is" : "ies are")} ready for {TwoDGlueTabType.ToLowerInvariant()} glue tabs on the {TwoDGlueTabSide.ToLowerInvariant()} side. Dashed creases can use {creaseCount} selected line/polyline entit{(creaseCount == 1 ? "y" : "ies")}.";
            }

            return creaseCount == 0
                ? "The current selection can be used for Paper Folding only if it contains line or polyline geometry."
                : $"{creaseCount} selected line/polyline entit{(creaseCount == 1 ? "y is" : "ies are")} ready for dashed crease conversion. Select LINE entities to enable glue tabs.";
        }
    }

    public string TwoDSelectedTextDraft
    {
        get => _twoDSelectedTextDraft;
        set => SetWorkspaceFacadeValue(_twoDSelectedTextDraft, value ?? string.Empty, updated => _twoDSelectedTextDraft = updated);
    }

    public string TwoDSelectedTextHeightText
    {
        get => _twoDSelectedTextHeightText;
        set
        {
            if (!SetWorkspaceFacadeValue(_twoDSelectedTextHeightText, value ?? string.Empty, updated => _twoDSelectedTextHeightText = updated))
                return;

            SetTwoDSelectedTextHeightValidity(true);
            OnPropertyChanged(nameof(CanApplyTwoDSelectedText));
        }
    }

    public string TwoDSelectedTextFontFamily
    {
        get => _twoDSelectedTextFontFamily;
        set => SetWorkspaceFacadeValue(_twoDSelectedTextFontFamily, string.IsNullOrWhiteSpace(value) ? "Inter" : value.Trim(), updated => _twoDSelectedTextFontFamily = updated);
    }

    public string? TwoDTextFontPreview
    {
        get => _twoDTextFontPreview;
        set => SetProperty(ref _twoDTextFontPreview, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    public string TwoDSelectedTextCharacterSpacingText
    {
        get => _twoDSelectedTextCharacterSpacingText;
        set => SetWorkspaceFacadeValue(_twoDSelectedTextCharacterSpacingText, value ?? "0", updated => _twoDSelectedTextCharacterSpacingText = updated);
    }

    public bool TwoDSelectedTextBold
    {
        get => _twoDSelectedTextBold;
        set => SetWorkspaceFacadeValue(_twoDSelectedTextBold, value, updated => _twoDSelectedTextBold = updated);
    }

    public bool TwoDSelectedTextItalic
    {
        get => _twoDSelectedTextItalic;
        set => SetWorkspaceFacadeValue(_twoDSelectedTextItalic, value, updated => _twoDSelectedTextItalic = updated);
    }

    public bool TwoDSelectedTextUnderline
    {
        get => _twoDSelectedTextUnderline;
        set => SetWorkspaceFacadeValue(_twoDSelectedTextUnderline, value, updated => _twoDSelectedTextUnderline = updated);
    }

    public IReadOnlyList<string> TwoDSelectedTextFitModeOptions => ["None", "Height", "Width", "Both"];

    public string TwoDSelectedTextFitMode
    {
        get => _twoDSelectedTextFitMode;
        set
        {
            var normalized = TwoDSelectedTextFitModeOptions.Contains(value, StringComparer.Ordinal)
                ? value
                : "None";
            SetWorkspaceFacadeValue(_twoDSelectedTextFitMode, normalized, updated => _twoDSelectedTextFitMode = updated);
        }
    }

    public bool IsTwoDSelectedTextHeightValid => _isTwoDSelectedTextHeightValid;

    public bool IsTwoDSelectedTextHeightInvalid => !_isTwoDSelectedTextHeightValid;

    public bool CanApplyTwoDSelectedText
        => HasSingleTwoDTextSelection && !string.IsNullOrWhiteSpace(TwoDSelectedTextHeightText);

    public EditorMode ActiveEditorMode
    {
        get => _activeEditorMode;
        private set
        {
            if (!SetProperty(ref _activeEditorMode, value))
                return;

            SyncSidebarToolStates();
            OnPropertyChanged(nameof(SidebarTools));
            OnPropertyChanged(nameof(CommandSearchResults));
            OnPropertyChanged(nameof(IsCommandSearchEmpty));
            OnPropertyChanged(nameof(ActiveToolLabel));
            OnPropertyChanged(nameof(IsShowingTwoDWorkspace));
            OnPropertyChanged(nameof(IsShowing3DWorkspace));
            OnPropertyChanged(nameof(IsShowingBatchWorkspace));
            OnPropertyChanged(nameof(WorkspaceSurfaceTitle));
            OnPropertyChanged(nameof(WorkspaceModeHint));
            OnPropertyChanged(nameof(OutputPreviewButtonLabel));
            OnPropertyChanged(nameof(OutputStatusSummary));
            OnPropertyChanged(nameof(ShowViewportEmptyState));
            OnPropertyChanged(nameof(CanFrameHome));
            OnPropertyChanged(nameof(ShowSelectionPanel));
            OnPropertyChanged(nameof(ShowMoveBodiesPanel));
            OnPropertyChanged(nameof(ShowProjectionPanel));
            OnPropertyChanged(nameof(ShowMeasurePanel));
            OnPropertyChanged(nameof(ShowUnfoldPanel));
            OnPropertyChanged(nameof(ShowOutputPanel));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public bool IsShowingTwoDWorkspace => ActiveEditorMode == EditorMode.TwoD;

    public bool IsShowing3DWorkspace => ActiveEditorMode == EditorMode.ThreeD;

    public bool IsShowingBatchWorkspace => ActiveEditorMode == EditorMode.Batch;

    public string WorkspaceSurfaceTitle => ActiveEditorMode switch
    {
        EditorMode.TwoD => "2D Workspace",
        EditorMode.ThreeD => "3D Workspace",
        EditorMode.Batch => "Batch Workspace",
        _ => "Editor Workspace",
    };

    public string OutputStatusSummary
    {
        get
        {
            if (HasTwoDWorkspaceDocument)
            {
                return IsShowingTwoDWorkspace
                    ? "Native 2D workspace active."
                    : "2D workspace ready.";
            }

            if (!HasGeneratedOutput)
                return "Run Projection or Unfold to create a 2D result.";

            if (GeneratedOutputSummary is null)
                return "Generated output saved. Refresh the inspector to load DXF details.";

            if (!GeneratedOutputSummary.FileExists)
                return "The saved DXF path no longer exists on disk. Regenerate or restore the file.";

            return GeneratedOutputSummary.PreviewPathCount == 0
                ? GeneratedOutputSummary.UnsupportedEntityCount > 0
                    ? "DXF found, but it only contains entities the local preview does not render yet."
                    : "DXF found, but no previewable linework was detected."
                : "DXF inspected. Open or reveal the file for a closer check.";
        }
    }

    public string OutputPreviewButtonLabel
        => IsShowingTwoDWorkspace
            ? "2D Workspace Visible"
            : HasTwoDWorkspaceDocument
                ? "Show 2D Workspace"
                : HasGeneratedOutputFileOnDisk
                    ? "Preview Unavailable"
                    : "Start 2D Workspace";

    public string TwoDViewportSummary => TwoDDocument is { } document
        ? document.Paths.Count == 0
            ? TwoDViewportZoom > 0.0
                ? $"Native viewport · empty sketch · {TwoDViewportZoom * 100.0:0}%"
                : "Native viewport · empty sketch"
            : TwoDViewportZoom > 0.0
                ? $"Native viewport · {document.Paths.Count} path(s) · {TwoDViewportZoom * 100.0:0}%"
                : $"Native viewport · {document.Paths.Count} path(s)"
        : "No local 2D viewport loaded.";

    public string TwoDSelectionSummary => TwoDDocument is null
        ? "No 2D geometry loaded."
        : TwoDDocument.Paths.Count == 0
            ? "Empty 2D sketch. Start drawing with the native tools."
        : TwoDSelectionCount switch
        {
            0 => "No 2D entities selected.",
            1 when TwoDSelectedRectangleCount == 1 => "1 constrained rectangle selected.",
            1 => "1 2D entity selected.",
            _ when TwoDSelectedRectangleCount == 0 => $"{TwoDSelectionCount} 2D entities selected.",
            _ => $"{TwoDSelectionCount} 2D entities selected ({TwoDSelectedRectangleCount} constrained rectangle{(TwoDSelectedRectangleCount == 1 ? string.Empty : "s")}).",
        };

    public string TwoDMeasurementSummary
    {
        get
        {
            var manualMeasurements = TwoDMeasurements
                .Where(static measurement => !measurement.IsAutoDimension)
                .ToArray();
            var autoDimensionCount = TwoDAutoDimensionCount;
            var selectedMeasurement = !string.IsNullOrWhiteSpace(TwoDSelectedMeasurementId)
                ? manualMeasurements.FirstOrDefault(measurement => string.Equals(measurement.Id, TwoDSelectedMeasurementId, StringComparison.Ordinal))
                : null;

            var manualSummary = manualMeasurements.Length switch
            {
                0 => "No manual measurements.",
                1 => $"1 measurement · {manualMeasurements[0].Distance:0.###} mm",
                _ => $"{manualMeasurements.Length} measurements · last {manualMeasurements[^1].Distance:0.###} mm",
            };

            if (selectedMeasurement is not null)
                manualSummary = $"{manualSummary} Selected: {selectedMeasurement.Distance:0.###} mm.";

            if (autoDimensionCount == 0)
                return manualSummary;

            var autoSummary = $"{autoDimensionCount} auto dimension{(autoDimensionCount == 1 ? string.Empty : "s")}.";
            return manualMeasurements.Length == 0
                ? autoSummary
                : $"{manualSummary} {autoSummary}";
        }
    }

    public string GeneratedOutputFileHealthSummary => GeneratedOutputSummary switch
    {
        null when HasGeneratedOutput => "Inspector not loaded yet.",
        null => "No generated DXF saved.",
        { FileExists: false } => "Saved DXF is missing on disk.",
        _ => "Saved DXF found on disk.",
    };

    public string GeneratedOutputFileSizeSummary => GeneratedOutputSummary is { FileExists: true } summary
        ? $"Size: {FormatFileSize(summary.FileSizeBytes)}"
        : string.Empty;

    public string GeneratedOutputLastModifiedSummary => GeneratedOutputSummary is { FileExists: true, LastModifiedUtc: { } timestamp }
        ? $"Updated: {timestamp.ToLocalTime():yyyy-MM-dd HH:mm}"
        : string.Empty;

    public string GeneratedOutputPreviewGeometrySummary => GeneratedOutputSummary is { FileExists: true } summary
        ? summary.PreviewPathCount == 0
            ? "Preview geometry: none"
            : $"Preview geometry: {summary.PreviewPathCount} paths ({summary.ClosedPathCount} closed, {summary.OpenPathCount} open)"
        : string.Empty;

    public string GeneratedOutputEntityMixSummary => GeneratedOutputSummary is { FileExists: true } summary
        ? BuildEntityMixSummary(summary)
        : string.Empty;

    public string GeneratedOutputUnsupportedEntitySummary => GeneratedOutputSummary is { FileExists: true, UnsupportedEntityCount: > 0 } summary
        ? $"Unsupported preview entities: {summary.UnsupportedEntityCount} ({summary.UnsupportedEntityTypes})"
        : string.Empty;

    public string GeneratedOutputBoundsSummary => GeneratedOutputSummary is { FileExists: true, PreviewPathCount: > 0 } summary
        ? $"Bounds: {FormatDimension(summary.Width)} x {FormatDimension(summary.Height)}"
        : string.Empty;

    public string WorkspaceModeHint => ActiveEditorMode switch
    {
        EditorMode.TwoD => "Viewing the local 2D workspace. Use the 2D tool chips",
        EditorMode.Batch => "Validate queued projects in an isolated batch workspace without opening or mutating either editor workspace.",
        _ => !HasLoadedModel
            ? HasTwoDWorkspaceDocument
                ? "A dedicated 2D workspace is ready. Load a model any time if you also want to project or unfold 3D geometry."
                : "Load a model to begin the 3D workspace, or switch to the 2D workspace to sketch directly."
            : !HasUsableSourceModelAsset
                ? "Viewing a restored 3D workspace. 3D operations stay disabled until the source asset is restored."
                : "Viewing the interactive 3D workspace.",
    };

    public string TwoDPolygonSidesSummary => $"Polygon sides: {TwoDPolygonSides}";

    public string TwoDToolHint => TwoDActiveTool switch
    {
        Editor2DTool.Select => "Select tool: click linework to select it, or drag a marquee to select multiple paths. Shift-click adds or removes from the selection.",
        Editor2DTool.Move => "Move tool: drag a selected entity set to reposition it directly in the 2D workspace.",
        Editor2DTool.Pan => "Pan tool: left-drag to move the 2D workspace. Mouse wheel zoom stays available on every tool.",
        Editor2DTool.Measure => "Measure tool: click once to place the start point, click again to place the end point, and press Escape to cancel the in-progress measurement.",
        Editor2DTool.Dimension => "Dimension tool: click a line to place an attached length dimension, click a circle or arc to place an attached radius dimension, or click empty space twice for a reference distance. Press Escape to cancel an in-progress reference dimension.",
        Editor2DTool.Scale => "Scale tool: select entities, choose center or corner scaling, pick an optional custom pivot, then drag the handle or enter an exact factor.",
        Editor2DTool.Mirror => "Mirror tool: select entities, click once to place the mirror axis start, then click again to place the axis end and mirror the selection.",
        Editor2DTool.Offset => "Offset tool: keep geometry selected, choose Curve or BBox mode in the lower 2D panel, and apply an OpenGeometry offset copy. Open paths offset relative to their point order.",
        Editor2DTool.AddThickness => "Add Thickness tool: thicken selected open line or polyline centerlines into closed OpenGeometry outlines, or process every eligible open centerline when nothing is selected.",
        Editor2DTool.Cleanup => "Join/Cleanup tool: apply endpoint cleanup across open line and polyline geometry using the current tolerance. This local 2D pass joins nearby chain endpoints and removes degenerate open segments.",
        Editor2DTool.Patterning => "Pattern tool: keep geometry selected, choose Rectangular or Circular mode in the lower 2D panel, and apply local duplicates. This pass uses the current selection center as the circular pivot.",
        Editor2DTool.PaperFolding => "Paper Folding tool: convert selected linework to dashed crease geometry or add glue-tab outlines to selected LINE entities. This local 2D pass writes explicit geometry instead of DXF linetype metadata.",
        Editor2DTool.AddSewingHoles => "Add Holes / Sewing tool: select boundary paths and configure pitch, margin, corner, and avoidance behavior in the sewing inspector.",
        Editor2DTool.Trim => "Trim tool: click a hovered straight segment to remove the piece under the cursor between the nearest intersections. This local 2D pass trims lines and polylines.",
        Editor2DTool.Fillet => "Fillet tool: click a corner handle, then drag its arrow or edit the active value to round that corner.",
        Editor2DTool.Chamfer => "Chamfer tool: click a corner handle, then drag its arrow or edit the active value to bevel that corner.",
        Editor2DTool.ConvertLines => "Convert Lines tool: keep line or polyline geometry selected, pick a style in the lower 2D panel, and apply it to replace the selected source paths with local patterned geometry.",
        Editor2DTool.SketchLine => "Line tool: click once to place the start point, then click again to create a new line segment in the 2D workspace.",
        Editor2DTool.SketchRectangle => "Rectangle tool: click once to place the first corner, then click again to create a sharp constrained rectangle with attached width and height dimensions.",
        Editor2DTool.SketchCircle => "Circle tool: click once to place the center, then click again to set the radius and add a circular path to the 2D workspace.",
        Editor2DTool.SketchPolygon => "Polygon tool: click once to place the center, then click again to set the radius and add a regular closed polygon using the current side count.",
        Editor2DTool.SketchText => "Text tool: click once to place one corner of a text box, then click again to size it. Edit the selected TEXT content and height from the 2D selection panel.",
        Editor2DTool.Pen => "Pen tool: click to add path vertices, click the first vertex to close the path, and press Enter to commit an open path.",
        _ => string.Empty,
    };

    public async Task OpenGeneratedOutputAsync(CancellationToken cancellationToken = default)
    {
        if (!HasGeneratedOutputFileOnDisk || string.IsNullOrWhiteSpace(LastGeneratedOutputPath))
            return;

        await _editorOutputLauncherService.OpenOutputAsync(LastGeneratedOutputPath, cancellationToken).ConfigureAwait(true);
    }

    public async Task RevealGeneratedOutputAsync(CancellationToken cancellationToken = default)
    {
        if (!HasGeneratedOutputFileOnDisk || string.IsNullOrWhiteSpace(LastGeneratedOutputPath))
            return;

        await _editorOutputLauncherService.RevealOutputAsync(LastGeneratedOutputPath, cancellationToken).ConfigureAwait(true);
    }

    public async Task RefreshGeneratedOutputAsync(CancellationToken cancellationToken = default)
    {
        if (!HasGeneratedOutput || string.IsNullOrWhiteSpace(LastGeneratedOutputPath))
            return;

        StatusText = "Refreshing generated output";
        ErrorMessage = null;

        await UpdateGeneratedOutputPreviewAsync(
            LastGeneratedOutputPath,
            activatePreviewWorkspace: false,
            cancellationToken,
            persistState: false).ConfigureAwait(true);

        StatusText = HasGeneratedOutputFileOnDisk
            ? "Generated output refreshed"
            : "Generated output missing";
        ViewportStateText = OutputStatusSummary;
    }

    public void Show3DWorkspace() => _ = SetActiveEditorModeAsync(EditorMode.ThreeD);

    public Task Show3DWorkspaceAsync(CancellationToken cancellationToken = default)
        => SetActiveEditorModeAsync(EditorMode.ThreeD, cancellationToken);

    public void ShowTwoDWorkspace() => _ = ShowTwoDWorkspaceAsync();

    public async Task ShowTwoDWorkspaceAsync(CancellationToken cancellationToken = default)
        => await SetActiveEditorModeAsync(EditorMode.TwoD, cancellationToken).ConfigureAwait(true);

    public async Task SetActiveEditorModeAsync(EditorMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown editor mode.");

        cancellationToken.ThrowIfCancellationRequested();

        if (mode == EditorMode.TwoD && TwoDDocument is null)
            await EnsureTwoDWorkspaceDocumentAsync(cancellationToken).ConfigureAwait(true);

        if (mode == EditorMode.TwoD && TwoDDocument is null)
            return;

        ActiveEditorMode = mode;
        StatusText = mode switch
        {
            EditorMode.TwoD when HasTwoDPreview => "2D workspace active",
            EditorMode.TwoD => "Blank 2D sketch workspace active",
            EditorMode.ThreeD => "3D workspace active",
            EditorMode.Batch => "Batch workspace active",
            _ => "Editor workspace active",
        };
        ViewportStateText = OutputStatusSummary;
    }

    public void ActivateTwoDSelectTool() => TwoDActiveTool = Editor2DTool.Select;

    public void ActivateTwoDMoveTool()
    {
        TwoDMoveCreateCopy = false;
        ResetTwoDMovePointToPoint();
        TwoDActiveTool = Editor2DTool.Move;
    }

    public void ActivateTwoDPanTool() => TwoDActiveTool = Editor2DTool.Pan;

    public void ActivateTwoDMeasureTool() => TwoDActiveTool = Editor2DTool.Measure;

    public void ActivateTwoDDimensionTool() => TwoDActiveTool = Editor2DTool.Dimension;

    public void ActivateTwoDScaleTool() => TwoDActiveTool = Editor2DTool.Scale;

    public void ActivateTwoDMirrorTool() => TwoDActiveTool = Editor2DTool.Mirror;

    public void ActivateTwoDOffsetTool() => TwoDActiveTool = Editor2DTool.Offset;

    public void ActivateTwoDAddThicknessTool() => TwoDActiveTool = Editor2DTool.AddThickness;

    public void ActivateTwoDCleanupTool() => TwoDActiveTool = Editor2DTool.Cleanup;

    public void ActivateTwoDPatternTool() => TwoDActiveTool = Editor2DTool.Patterning;

    public void ActivateTwoDPaperFoldingTool() => TwoDActiveTool = Editor2DTool.PaperFolding;

    public void ActivateTwoDTrimTool() => TwoDActiveTool = Editor2DTool.Trim;

    public void ActivateTwoDFilletTool() => TwoDActiveTool = Editor2DTool.Fillet;

    public void ActivateTwoDChamferTool() => TwoDActiveTool = Editor2DTool.Chamfer;

    public void ActivateTwoDConvertLinesTool() => TwoDActiveTool = Editor2DTool.ConvertLines;

    public void ActivateTwoDLineTool() => TwoDActiveTool = Editor2DTool.SketchLine;

    public void ActivateTwoDRectangleTool() => TwoDActiveTool = Editor2DTool.SketchRectangle;

    public void ActivateTwoDCircleTool() => TwoDActiveTool = Editor2DTool.SketchCircle;

    public void ActivateTwoDPolygonTool() => TwoDActiveTool = Editor2DTool.SketchPolygon;

    public void ActivateTwoDTextTool() => TwoDActiveTool = Editor2DTool.SketchText;

    public void ActivateTwoDPenTool() => TwoDActiveTool = Editor2DTool.Pen;

    public void IncrementTwoDPolygonSides() => TwoDPolygonSides++;

    public void DecrementTwoDPolygonSides() => TwoDPolygonSides--;

    public void ClearTwoDSelection()
    {
        _twoDWorkspace.SetSelection([]);
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
    }

    public void ClearTwoDSelectedMeasurement()
    {
        _twoDWorkspace.SetSelectedMeasurement(null);
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
    }

    public void ClearTwoDMeasurements()
    {
        _twoDWorkspace.ClearManualMeasurements();
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
    }

    public async Task<bool> ApplyTwoDOffsetAsync(CancellationToken cancellationToken = default)
    {
        if (TwoDDocument is null)
            return false;

        if (IsTwoDBBoxOffsetMode)
            return ApplyTwoDBoundingBoxOffset();

        if (!TryParseTwoDOffsetDistance(TwoDOffsetDistanceText, "offset distance", 0.1, out var offsetDistance, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }
        StatusText = "Offset running through OpenGeometry";
        var result = await _twoDWorkspace.ApplyCurveOffsetAsync(
            _editor2DGeometryKernelService,
            offsetDistance,
            string.Equals(TwoDOffsetSide, "Outward", StringComparison.Ordinal),
            cancellationToken).ConfigureAwait(true);
        return CompleteTwoDWorkspaceOperation(result);
    }

    public async Task<bool> ApplyTwoDBooleanAsync(string operation, CancellationToken cancellationToken = default)
    {
        if (!CanApplyTwoDBoolean || !Enum.TryParse<Editor2DBooleanOperation>(operation, true, out var parsed))
        {
            StatusText = "Select at least two closed paths before applying a boolean operation";
            return false;
        }

        StatusText = $"{parsed} running through OpenGeometry";
        return CompleteTwoDWorkspaceOperation(await _twoDWorkspace.ApplyBooleanAsync(
            _editor2DGeometryKernelService, parsed, cancellationToken).ConfigureAwait(true));
    }

    public async Task<bool> ApplyTwoDAddThicknessAsync(CancellationToken cancellationToken = default)
    {
        if (TwoDDocument is null)
            return false;

        if (!TryParseTwoDOffsetDistance(TwoDAddThicknessWidthText, "thickness width", 0.1, out var thickness, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        StatusText = "Add Thickness running through OpenGeometry";
        return CompleteTwoDWorkspaceOperation(await _twoDWorkspace.ApplyThicknessAsync(
            _editor2DGeometryKernelService, thickness, cancellationToken).ConfigureAwait(true));
    }

    public bool ApplyTwoDCleanup()
    {
        if (TwoDDocument is null)
            return false;

        if (!TryParseTwoDOffsetDistance(TwoDCleanupToleranceText, "cleanup tolerance", 0.0001, out var tolerance, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyCleanup(tolerance));
    }

    public bool ApplyTwoDStrokeToFill()
    {
        if (TwoDDocument is null)
            return false;

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyStrokeToFill());
    }

    public bool ApplyTwoDFillToStroke()
    {
        if (TwoDDocument is null)
            return false;

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyFillToStroke());
    }

    public bool ApplyTwoDPattern()
    {
        if (TwoDDocument is null)
            return false;

        if (!CanApplyTwoDPattern)
        {
            StatusText = "Select one or more 2D entities before applying Pattern";
            return false;
        }

        if (IsTwoDCircularPatternMode)
            return ApplyTwoDCircularPattern();
        if (IsTwoDPathPatternMode)
            return ApplyTwoDPathPattern();
        return ApplyTwoDRectangularPattern();
    }

    public bool ApplyTwoDPaperFoldingCreases()
    {
        if (TwoDDocument is null)
            return false;
        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyCreases());
    }

    public bool ApplyTwoDGlueTabs()
    {
        if (TwoDDocument is null)
            return false;

        if (!TryParseTwoDOffsetDistance(TwoDGlueTabHeightText, "glue tab height", 0.1, out var height, out var heightErrorMessage))
        {
            StatusText = heightErrorMessage;
            return false;
        }

        if (!TryParseTwoDOffsetDistance(TwoDGlueTabStartOffsetText, "glue tab start offset", 0.0, out var startOffset, out var startErrorMessage))
        {
            StatusText = startErrorMessage;
            return false;
        }

        if (!TryParseTwoDOffsetDistance(TwoDGlueTabEndOffsetText, "glue tab end offset", 0.0, out var endOffset, out var endErrorMessage))
        {
            StatusText = endErrorMessage;
            return false;
        }

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyGlueTabs(
            height, TwoDGlueTabType, TwoDGlueTabSide, startOffset, endOffset));
    }

    public bool ApplyTwoDConvertLines()
    {
        if (TwoDDocument is null)
            return false;

        if (!TryResolveTwoDConvertLineSettings(out var settings, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyConvertedLines(TwoDConvertLineStyle, settings));
    }

    private bool ApplyTwoDBoundingBoxOffset()
    {
        if (TwoDDocument is null)
            return false;

        if (!TryParseTwoDOffsetDistance(TwoDOffsetBBoxDistanceText, "BBox offset distance", 0.1, out var offsetDistance, out var distanceErrorMessage))
        {
            StatusText = distanceErrorMessage;
            return false;
        }

        if (!TryParseTwoDOffsetDistance(TwoDOffsetBBoxFilletText, "BBox fillet radius", 0.0, out var cornerRadius, out var filletErrorMessage))
        {
            StatusText = filletErrorMessage;
            return false;
        }

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyBoundingBoxOffset(offsetDistance, cornerRadius));
    }

    public bool ApplyTwoDSelectedText()
    {
        if (TwoDDocument is null
            || !TryGetSingleSelectedTwoDTextPath(out var selectedTextPath))
        {
            return false;
        }

        if (!TryParseTwoDSelectedTextHeight(TwoDSelectedTextHeightText, out var textHeight))
        {
            SetTwoDSelectedTextHeightValidity(false);
            StatusText = "Enter a valid text height in millimeters";
            return false;
        }

        var normalizedText = string.IsNullOrWhiteSpace(TwoDSelectedTextDraft)
            ? "Label"
            : TwoDSelectedTextDraft.Replace("\r\n", "\n");
        var normalizedHeight = Math.Max(textHeight, 0.1);
        if (!double.TryParse(TwoDSelectedTextCharacterSpacingText, NumberStyles.Float, CultureInfo.InvariantCulture, out var characterSpacing)
            || !double.IsFinite(characterSpacing))
        {
            StatusText = "Enter valid character spacing in millimeters";
            return false;
        }

        var result = _twoDWorkspace.ApplySelectedText(
            normalizedText, normalizedHeight, TwoDSelectedTextFontFamily, characterSpacing,
            TwoDSelectedTextBold, TwoDSelectedTextItalic, TwoDSelectedTextUnderline,
            TwoDSelectedTextFitMode);
        if (!CompleteTwoDWorkspaceOperation(result))
            return false;
        SetTwoDSelectedTextHeightValidity(true);
        TwoDSelectedTextHeightText = normalizedHeight.ToString("0.###", CultureInfo.InvariantCulture);
        TwoDSelectedTextDraft = normalizedText;
        TwoDTextFontPreview = null;
        return true;
    }

    public bool DeleteTwoDSelection()
    {
        if (DeleteTwoDSelectedMeasurement())
            return true;

        if (TwoDDocument is null)
            return false;

        var deleted = _twoDWorkspace.DeleteSelection();
        if (deleted == 0)
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        StatusText = _twoDWorkspace.Document.Paths.Count == 0
            ? "Deleted the selected 2D entities"
            : $"Deleted {deleted} selected 2D entit{(deleted == 1 ? "y" : "ies")}";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool DeleteTwoDSelectedMeasurement()
    {
        if (!_twoDWorkspace.DeleteSelectedMeasurement())
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        StatusText = "Deleted the selected 2D measurement";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ExpandTwoDRectangles()
    {
        if (TwoDDocument is null)
            return false;
        var expandedRectangleCount = _twoDWorkspace.ExpandSelectedRectangles();
        if (expandedRectangleCount == 0)
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        StatusText = expandedRectangleCount == 1
            ? "Expanded the selected constrained rectangle"
            : $"Expanded {expandedRectangleCount} selected constrained rectangles";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool MergeTwoDLayerWithBelow(string layerId)
    {
        if (!_twoDWorkspace.MergeLayerWithBelow(layerId))
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        StatusText = "Merged the layer with the layer below";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool MergeTwoDSelectedLayers()
    {
        if (!_twoDWorkspace.MergeSelectedLayers())
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        StatusText = "Merged selected layers";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public void FrameTwoDToContent()
    {
        if (!HasTwoDWorkspaceDocument)
            return;

        TwoDFrameRequestToken++;
    }

    private void ApplyGeneratedOutput(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            _generatedOutputDataBase64 = null;

        LastGeneratedOutputPath = string.IsNullOrWhiteSpace(outputPath)
            ? null
            : Path.GetFullPath(outputPath);
    }

    private async Task HandleSuccessfulGeneratedOutputAsync(
        string? outputPath,
        EditorGeneratedOutputContext outputContext,
        CancellationToken cancellationToken)
    {
        SelectedFaces = [];
        SelectedFaceDetails = [];
        SelectedFaceCount = 0;
        SelectionSummary = "No selection";
        RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));
        SetDistortionData(string.Empty);
        GeneratedOutputContext = outputContext;

        await UpdateGeneratedOutputPreviewAsync(
            outputPath,
            activatePreviewWorkspace: true,
            cancellationToken).ConfigureAwait(true);

        ActivateOutputTool();
        StatusText = string.IsNullOrWhiteSpace(outputPath)
            ? StatusText
            : $"{StatusText} and prepared local 2D workspace";
    }

    private async Task UpdateGeneratedOutputPreviewAsync(
        string? outputPath,
        bool activatePreviewWorkspace,
        CancellationToken cancellationToken,
        bool persistState = true)
    {
        ApplyGeneratedOutput(outputPath);

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            GeneratedOutputSummary = null;
            SetTwoDDocument(null, activatePreviewWorkspace);
            return;
        }

        var summary = await _editorOutputPreviewService
            .InspectOutputAsync(outputPath, cancellationToken)
            .ConfigureAwait(true);
        GeneratedOutputSummary = summary;

        if (summary is { FileExists: true })
        {
            _generatedOutputDataBase64 = await TryReadGeneratedOutputDataBase64Async(outputPath, cancellationToken)
                .ConfigureAwait(true);
        }

        Editor2DPreviewDocument? previewDocument = null;
        if (summary is { FileExists: true })
        {
            previewDocument = await _editorOutputPreviewService
                .LoadPreviewDocumentAsync(outputPath, cancellationToken)
                .ConfigureAwait(true);
        }

        SetTwoDDocument(previewDocument, activatePreviewWorkspace);

        if (persistState)
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }

    private void ClearTwoDState()
    {
        LastGeneratedOutputPath = null;
        GeneratedOutputSummary = null;
        GeneratedOutputContext = null;
        SetTwoDDocument(null, activatePreviewWorkspace: false);
    }

    private Task EnsureTwoDWorkspaceDocumentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var document = TwoDDocument ?? CreateEmptyTwoDDocument();
        SetTwoDDocument(document, activatePreviewWorkspace: false);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        return Task.CompletedTask;
    }

    private static Editor2DPreviewDocument CreateEmptyTwoDDocument()
        => new(
            [],
            new Editor2DBounds(0.0, 0.0, 0.0, 0.0),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            []);

    private void SetTwoDDocument(
        Editor2DPreviewDocument? document,
        bool activatePreviewWorkspace = true)
    {
        ApplyTwoDDocument(document, requestPersistence: false);
        ResetTwoDEditorState();
        ResetTwoDViewportState(document is not null);

        if (document is null)
            ActiveEditorMode = EditorMode.ThreeD;
        else if (activatePreviewWorkspace)
            ActiveEditorMode = EditorMode.TwoD;
    }

    private void ResetTwoDViewportState(bool requestFrame)
    {
        ApplyTwoDViewportState(0.0, 0.0, 0.0, requestPersistence: false);

        if (requestFrame)
            TwoDFrameRequestToken++;
    }

    private void ResetTwoDEditorState()
    {
        TwoDActiveTool = Editor2DTool.Select;
        TwoDSelectedPathIds = [];
        TwoDSelectedMeasurementId = null;
        RefreshDerivedTwoDMeasurements(TwoDDocument);
    }

    private void ApplyTwoDDocument(
        Editor2DPreviewDocument? document,
        bool requestPersistence)
    {
        var previousSuppression = _suppressTwoDDocumentPersistence;
        _suppressTwoDDocumentPersistence = !requestPersistence;

        try
        {
            TwoDDocument = document;
        }
        finally
        {
            _suppressTwoDDocumentPersistence = previousSuppression;
        }
    }

    private void ApplyTwoDViewportState(
        double zoom,
        double offsetX,
        double offsetY,
        bool requestPersistence)
    {
        var previousSuppression = _suppressTwoDViewportPersistence;
        _suppressTwoDViewportPersistence = true;

        try
        {
            TwoDViewportZoom = zoom;
            TwoDViewportOffsetX = offsetX;
            TwoDViewportOffsetY = offsetY;
        }
        finally
        {
            _suppressTwoDViewportPersistence = previousSuppression;
        }

        if (requestPersistence)
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }

    private static async Task<string?> TryReadGeneratedOutputDataBase64Async(string outputPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            return null;

        var dxfBytes = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(dxfBytes);
    }

    private static string FormatDimension(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private void RefreshDerivedTwoDMeasurements(Editor2DPreviewDocument? document)
    {
        var manualMeasurements = document is null
            ? TwoDMeasurements
                .Where(static measurement => !measurement.IsAutoDimension)
                .ToArray()
            : RebuildAttachedTwoDMeasurements(
                document,
                TwoDMeasurements.Where(static measurement => !measurement.IsAutoDimension));
        var autoMeasurements = document is null
            ? []
            : BuildAutoTwoDMeasurements(document);
        _isRefreshingDerivedTwoDMeasurements = true;
        try
        {
            TwoDMeasurements = autoMeasurements
                .Concat(manualMeasurements)
                .ToArray();
        }
        finally
        {
            _isRefreshingDerivedTwoDMeasurements = false;
        }
    }

    private static IReadOnlyList<Editor2DMeasurement> RebuildAttachedTwoDMeasurements(
        Editor2DPreviewDocument document,
        IEnumerable<Editor2DMeasurement> manualMeasurements)
    {
        var measurements = new List<Editor2DMeasurement>();
        foreach (var measurement in manualMeasurements)
        {
            if (string.IsNullOrWhiteSpace(measurement.EntityPathId))
            {
                measurements.Add(measurement);
                continue;
            }

            var path = document.Paths.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, measurement.EntityPathId, StringComparison.Ordinal));
            if (path is null)
                continue;

            if (!Editor2DGeometry.TryBuildAttachedMeasurement(
                    path,
                    measurement.DimensionType,
                    measurement.OffsetDistance,
                    measurement.PlacementAngleDegrees,
                    out var start,
                    out var end))
            {
                measurements.Add(measurement);
                continue;
            }

            measurements.Add(measurement with
            {
                Start = start,
                End = end,
            });
        }

        return measurements;
    }

    private Editor2DPreviewDocument? ApplyExpandedRectangleOverrides(Editor2DPreviewDocument? document)
    {
        if (document is null || _twoDExpandedRectanglePathIds.Count == 0)
            return document;

        var expandedRectangleIds = new HashSet<string>(_twoDExpandedRectanglePathIds, StringComparer.Ordinal);
        var changed = false;
        var nextPaths = document.Paths
            .Select(path =>
            {
                if (!path.IsAxisAlignedRectangle
                    || !expandedRectangleIds.Contains(path.Id)
                    || !Editor2DGeometry.IsAxisAlignedRectangle(path.Points, path.IsClosed))
                {
                    return path;
                }

                changed = true;
                return path with { IsAxisAlignedRectangle = false };
            })
            .ToArray();

        return changed
            ? document with { Paths = nextPaths }
            : document;
    }

    private void SyncTwoDExpandedRectanglePathIds(Editor2DPreviewDocument? document)
    {
        _twoDExpandedRectanglePathIds = document is null
            ? []
            : document.Paths
                .Where(path => !path.IsAxisAlignedRectangle && Editor2DGeometry.IsAxisAlignedRectangle(path.Points, path.IsClosed))
                .Select(static path => path.Id)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }

    private static IReadOnlyList<string> NormalizeTwoDExpandedRectanglePathIds(IReadOnlyList<string>? pathIds)
        => pathIds is null
            ? []
            : pathIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private void RequestTwoDDocumentPersistence(TimeSpan? delay = null)
        => MarkDocumentDirty();

    private static Dictionary<string, string> CreateTwoDConvertLineParameterText()
    {
        var parameterText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (style, definitions) in TwoDConvertLineParameterDefinitions)
        {
            foreach (var definition in definitions)
                parameterText[BuildTwoDConvertLineParameterMapKey(style, definition.Key)] = FormatTwoDConvertLineValue(definition.DefaultValue, definition.IsInteger);
        }

        return parameterText;
    }

    private static string NormalizeTwoDConvertLineStyle(string? style)
        => !string.IsNullOrWhiteSpace(style)
           && TwoDConvertLineParameterDefinitions.ContainsKey(style)
            ? style
            : "dashed";

    private static string NormalizeTwoDOffsetMode(string? mode)
        => string.Equals(mode, "BBox", StringComparison.OrdinalIgnoreCase)
            ? "BBox"
            : "Curve";

    private static string NormalizeTwoDPatternMode(string? mode)
        => mode switch
        {
            _ when string.Equals(mode, "Circular", StringComparison.OrdinalIgnoreCase) => "Circular",
            _ when string.Equals(mode, "Path", StringComparison.OrdinalIgnoreCase) => "Path",
            _ => "Rectangular",
        };

    private static string NormalizeTwoDGlueTabType(string? tabType)
        => string.Equals(tabType, "Triangle", StringComparison.OrdinalIgnoreCase)
            ? "Triangle"
            : "Trapezoid";

    private static string NormalizeTwoDGlueTabSide(string? side)
        => string.Equals(side, "Right", StringComparison.OrdinalIgnoreCase)
            ? "Right"
            : "Left";

    private static string NormalizeTwoDOffsetSide(string? side)
        => string.Equals(side, "Inward", StringComparison.OrdinalIgnoreCase)
            ? "Inward"
            : "Outward";

    private static TwoDConvertLineParameterDefinition[] GetTwoDConvertLineParameterDefinitions(string style)
        => TwoDConvertLineParameterDefinitions.TryGetValue(style, out var definitions)
            ? definitions
            : TwoDConvertLineParameterDefinitions["dashed"];

    private static string BuildTwoDConvertLineParameterMapKey(string style, string parameterKey)
        => $"{style}:{parameterKey}";

    private IReadOnlyList<string> GetSelectedTwoDConvertiblePathIds()
    {
        if (TwoDDocument is null || TwoDSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(TwoDSelectedPathIds, StringComparer.Ordinal);
        return TwoDDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsConvertibleLinePath(path))
            .Select(static path => path.Id)
            .ToArray();
    }

    private int GetTwoDConvertibleSelectionCount() => GetSelectedTwoDConvertiblePathIds().Count;

    private IReadOnlyList<string> GetSelectedTwoDCurveOffsetPathIds()
    {
        if (TwoDDocument is null || TwoDSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(TwoDSelectedPathIds, StringComparer.Ordinal);
        return TwoDDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsCurveOffsettablePath(path))
            .Select(static path => path.Id)
            .ToArray();
    }

    private int GetTwoDCurveOffsetSelectionCount()
        => IsTwoDBBoxOffsetMode ? TwoDSelectionCount : GetSelectedTwoDCurveOffsetPathIds().Count;

    private IReadOnlyList<Editor2DPreviewPath> GetTwoDThicknessCandidatePaths()
    {
        if (TwoDDocument is null)
            return [];

        if (TwoDSelectedPathIds.Count == 0)
        {
            return TwoDDocument.Paths
                .Where(Editor2DGeometry.IsThicknessSourcePath)
                .ToArray();
        }

        var selectedIds = new HashSet<string>(TwoDSelectedPathIds, StringComparer.Ordinal);
        return TwoDDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsThicknessSourcePath(path))
            .ToArray();
    }

    private int GetTwoDThicknessSourcePathCount() => GetTwoDThicknessCandidatePaths().Count;

    private IReadOnlyList<Editor2DPreviewPath> GetTwoDCleanupCandidatePaths()
        => TwoDDocument is null
            ? []
            : TwoDDocument.Paths
                .Where(Editor2DGeometry.IsCleanupSourcePath)
                .ToArray();

    private IReadOnlyList<Editor2DPreviewPath> GetSelectedTwoDPaths()
    {
        if (TwoDDocument is null || TwoDSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(TwoDSelectedPathIds, StringComparer.Ordinal);
        return TwoDDocument.Paths
            .Where(path => selectedIds.Contains(path.Id))
            .ToArray();
    }

    private IReadOnlyList<Editor2DPreviewPath> GetSelectedTwoDGlueTabPaths()
    {
        if (TwoDDocument is null || TwoDSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(TwoDSelectedPathIds, StringComparer.Ordinal);
        return TwoDDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsGlueTabSourcePath(path))
            .ToArray();
    }

    private string GetTwoDConvertLineParameterLabel(int index)
    {
        var definitions = GetTwoDConvertLineParameterDefinitions(TwoDConvertLineStyle);
        return index >= 0 && index < definitions.Length
            ? definitions[index].Label
            : string.Empty;
    }

    private string GetTwoDConvertLineParameterText(int index)
    {
        var definitions = GetTwoDConvertLineParameterDefinitions(TwoDConvertLineStyle);
        if (index < 0 || index >= definitions.Length)
            return string.Empty;

        var key = BuildTwoDConvertLineParameterMapKey(TwoDConvertLineStyle, definitions[index].Key);
        return _twoDConvertLineParameterText.TryGetValue(key, out var value)
            ? value
            : FormatTwoDConvertLineValue(definitions[index].DefaultValue, definitions[index].IsInteger);
    }

    private void SetTwoDConvertLineParameterText(int index, string? value)
    {
        var definitions = GetTwoDConvertLineParameterDefinitions(TwoDConvertLineStyle);
        if (index < 0 || index >= definitions.Length)
            return;

        var key = BuildTwoDConvertLineParameterMapKey(TwoDConvertLineStyle, definitions[index].Key);
        var normalized = value ?? string.Empty;
        if (_twoDConvertLineParameterText.TryGetValue(key, out var existing)
            && string.Equals(existing, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _twoDConvertLineParameterText[key] = normalized;
        NotifyTwoDConvertLineParameterStateChanged();
    }

    private void NotifyTwoDConvertLineParameterStateChanged()
    {
        OnPropertyChanged(nameof(TwoDConvertLineStyle));
        OnPropertyChanged(nameof(TwoDConvertLineSummary));
        OnPropertyChanged(nameof(HasTwoDConvertLineFirstParameter));
        OnPropertyChanged(nameof(HasTwoDConvertLineSecondParameter));
        OnPropertyChanged(nameof(HasTwoDConvertLineThirdParameter));
        OnPropertyChanged(nameof(TwoDConvertLineFirstParameterLabel));
        OnPropertyChanged(nameof(TwoDConvertLineSecondParameterLabel));
        OnPropertyChanged(nameof(TwoDConvertLineThirdParameterLabel));
        OnPropertyChanged(nameof(TwoDConvertLineFirstParameterText));
        OnPropertyChanged(nameof(TwoDConvertLineSecondParameterText));
        OnPropertyChanged(nameof(TwoDConvertLineThirdParameterText));
    }

    private bool TryResolveTwoDConvertLineSettings(
        out IReadOnlyDictionary<string, double> settings,
        out string errorMessage)
    {
        var definitions = GetTwoDConvertLineParameterDefinitions(TwoDConvertLineStyle);
        var resolved = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            var rawText = GetTwoDConvertLineParameterText(index);
            if (!TryParseTwoDSelectedTextHeight(rawText, out var value))
            {
                settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                errorMessage = $"Enter a valid value for {definition.Label}";
                return false;
            }

            value = Math.Max(value, definition.MinimumValue);
            if (definition.IsInteger)
                value = Math.Round(value);

            resolved[definition.Key] = value;
            _twoDConvertLineParameterText[BuildTwoDConvertLineParameterMapKey(TwoDConvertLineStyle, definition.Key)] =
                FormatTwoDConvertLineValue(value, definition.IsInteger);
        }

        settings = resolved;
        errorMessage = string.Empty;
        NotifyTwoDConvertLineParameterStateChanged();
        return true;
    }

    private static string FormatTwoDConvertLineValue(double value, bool isInteger)
        => isInteger
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryParseTwoDOffsetDistance(
        string rawText,
        string label,
        double minimumValue,
        out double value,
        out string errorMessage)
    {
        if (!TryParseTwoDSelectedTextHeight(rawText, out value))
        {
            errorMessage = $"Enter a valid {label}";
            return false;
        }

        value = Math.Max(value, minimumValue);
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryParseTwoDPatternCount(
        string rawText,
        string label,
        int minimumValue,
        out int value,
        out string errorMessage)
    {
        if (!TryParseTwoDSelectedTextHeight(rawText, out var parsedValue))
        {
            value = 0;
            errorMessage = $"Enter a valid {label}";
            return false;
        }

        value = Math.Max((int)Math.Round(parsedValue), minimumValue);
        errorMessage = string.Empty;
        return true;
    }

    private bool ApplyTwoDRectangularPattern()
    {
        if (!TryParseTwoDPatternCount(TwoDPatternCopiesXText, "pattern X count", 1, out var copiesX, out var copiesXErrorMessage))
        {
            StatusText = copiesXErrorMessage;
            return false;
        }

        if (!TryParseTwoDPatternCount(TwoDPatternCopiesYText, "pattern Y count", 1, out var copiesY, out var copiesYErrorMessage))
        {
            StatusText = copiesYErrorMessage;
            return false;
        }

        if (!TryParseTwoDOffsetDistance(TwoDPatternSpacingXText, "pattern X spacing", 0.0, out var spacingX, out var spacingXErrorMessage))
        {
            StatusText = spacingXErrorMessage;
            return false;
        }

        if (!TryParseTwoDOffsetDistance(TwoDPatternSpacingYText, "pattern Y spacing", 0.0, out var spacingY, out var spacingYErrorMessage))
        {
            StatusText = spacingYErrorMessage;
            return false;
        }

        return CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyRectangularPattern(copiesX, copiesY, spacingX, spacingY));
    }

    private bool ApplyTwoDCircularPattern()
    {
        if (!TryParseTwoDPatternCount(TwoDPatternCircularCountText, "pattern count", 1, out var totalCount, out var countErrorMessage))
        {
            StatusText = countErrorMessage;
            return false;
        }

        if (!TryParseTwoDSelectedTextHeight(TwoDPatternCircularAngleText, out var totalAngle))
        {
            StatusText = "Enter a valid pattern angle";
            return false;
        }

        var completed = CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyCircularPattern(totalCount, totalAngle, TwoDPatternPivot));
        if (completed)
            ClearTwoDCircularPatternPivot();
        return completed;
    }

    private bool ApplyTwoDPathPattern()
    {
        if (TwoDPatternGuidePathId is null)
        {
            StatusText = "Click a guide path on canvas before applying path Pattern";
            return false;
        }
        if (!TryParseTwoDPatternCount(TwoDPatternPathCopiesText, "path pattern copies", 2, out var copyCount, out var countErrorMessage))
        {
            StatusText = countErrorMessage;
            return false;
        }
        if (!TryParseTwoDOffsetDistance(TwoDPatternPathSpacingText, "path pattern spacing", 0.01, out var spacing, out var spacingErrorMessage))
        {
            StatusText = spacingErrorMessage;
            return false;
        }

        var completed = CompleteTwoDWorkspaceOperation(_twoDWorkspace.ApplyPathPattern(TwoDPatternGuidePathId, copyCount, spacing));
        if (completed)
            TwoDPatternGuidePathId = null;
        return completed;
    }

    private void SyncTwoDSelectedTextEditorState()
    {
        if (!TryGetSingleSelectedTwoDTextPath(out var selectedTextPath))
        {
            _twoDSelectedTextDraft = string.Empty;
            _twoDSelectedTextHeightText = "5";
            _twoDSelectedTextFontFamily = "Inter";
            _twoDSelectedTextCharacterSpacingText = "0";
            _twoDSelectedTextBold = false;
            _twoDSelectedTextItalic = false;
            _twoDSelectedTextUnderline = false;
            SetTwoDSelectedTextHeightValidity(true);
            NotifyTwoDSelectedTextEditorStateChanged();
            return;
        }

        _twoDSelectedTextDraft = selectedTextPath.Text ?? string.Empty;
        _twoDSelectedTextHeightText = Math.Max(selectedTextPath.TextHeight ?? 5.0, 0.1)
            .ToString("0.###", CultureInfo.InvariantCulture);
        _twoDSelectedTextFontFamily = string.IsNullOrWhiteSpace(selectedTextPath.FontFamily) ? "Inter" : selectedTextPath.FontFamily;
        _twoDSelectedTextCharacterSpacingText = selectedTextPath.CharacterSpacing.ToString("0.###", CultureInfo.InvariantCulture);
        _twoDSelectedTextBold = selectedTextPath.IsBold;
        _twoDSelectedTextItalic = selectedTextPath.IsItalic;
        _twoDSelectedTextUnderline = selectedTextPath.IsUnderline;
        SetTwoDSelectedTextHeightValidity(true);
        NotifyTwoDSelectedTextEditorStateChanged();
    }

    private void NotifyTwoDSelectedTextEditorStateChanged()
    {
        OnPropertyChanged(nameof(HasSingleTwoDTextSelection));
        OnPropertyChanged(nameof(TwoDSelectedTextDraft));
        OnPropertyChanged(nameof(TwoDSelectedTextHeightText));
        OnPropertyChanged(nameof(TwoDSelectedTextFontFamily));
        OnPropertyChanged(nameof(TwoDSelectedTextCharacterSpacingText));
        OnPropertyChanged(nameof(TwoDSelectedTextBold));
        OnPropertyChanged(nameof(TwoDSelectedTextItalic));
        OnPropertyChanged(nameof(TwoDSelectedTextUnderline));
        OnPropertyChanged(nameof(TwoDSelectedTextFitMode));
        OnPropertyChanged(nameof(TwoDSelectedTextFitModeOptions));
        OnPropertyChanged(nameof(CanApplyTwoDSelectedText));
    }

    private void SetTwoDSelectedTextHeightValidity(bool isValid)
    {
        if (_isTwoDSelectedTextHeightValid == isValid)
            return;

        _isTwoDSelectedTextHeightValid = isValid;
        OnPropertyChanged(nameof(IsTwoDSelectedTextHeightValid));
        OnPropertyChanged(nameof(IsTwoDSelectedTextHeightInvalid));
    }

    private bool TryGetSingleSelectedTwoDTextPath(out Editor2DPreviewPath selectedTextPath)
    {
        selectedTextPath = null!;
        if (TwoDDocument is null || TwoDSelectedPathIds.Count != 1)
            return false;

        var candidate = TwoDDocument.Paths.FirstOrDefault(path =>
            string.Equals(path.Id, TwoDSelectedPathIds[0], StringComparison.Ordinal)
            && path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
            return false;

        selectedTextPath = candidate;
        return true;
    }

    private static bool TryParseTwoDSelectedTextHeight(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
            return true;

        return double.TryParse(raw, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value);
    }

    private static string BuildEntityMixSummary(EditorGeneratedOutputSummary summary)
    {
        var parts = new List<string>();
        if (summary.LineEntityCount > 0)
            parts.Add($"{summary.LineEntityCount} line(s)");
        if (summary.PolylineEntityCount > 0)
            parts.Add($"{summary.PolylineEntityCount} polyline(s)");
        if (summary.ArcEntityCount > 0)
            parts.Add($"{summary.ArcEntityCount} arc(s)");
        if (summary.CircleEntityCount > 0)
            parts.Add($"{summary.CircleEntityCount} circle(s)");
        if (summary.EllipseEntityCount > 0)
            parts.Add($"{summary.EllipseEntityCount} ellipse(s)");
        if (summary.TextEntityCount > 0)
            parts.Add($"{summary.TextEntityCount} text");

        return parts.Count == 0
            ? "Entity mix: no supported preview entities detected."
            : $"Entity mix: {string.Join(", ", parts)}";
    }

    private static string FormatFileSize(long byteCount)
    {
        var size = byteCount;
        var units = new[] { "B", "KB", "MB", "GB" };
        double displaySize = size;
        var unitIndex = 0;

        while (displaySize >= 1024.0 && unitIndex < units.Length - 1)
        {
            displaySize /= 1024.0;
            unitIndex++;
        }

        return $"{displaySize:0.##} {units[unitIndex]}";
    }

    private static IReadOnlyList<Editor2DMeasurement> BuildAutoTwoDMeasurements(Editor2DPreviewDocument document)
    {
        const double minimumSize = 1e-6;
        var measurements = new List<Editor2DMeasurement>();

        foreach (var path in document.Paths.Where(static path => path.IsAxisAlignedRectangle))
        {
            var minX = path.Points.Min(static point => point.X);
            var minY = path.Points.Min(static point => point.Y);
            var maxX = path.Points.Max(static point => point.X);
            var maxY = path.Points.Max(static point => point.Y);
            var width = maxX - minX;
            var height = maxY - minY;
            if (width <= minimumSize || height <= minimumSize)
                continue;

            var offset = Math.Max(Math.Min(width, height) * 0.15, 8.0);
            var rectP1 = new Editor2DPoint(minX, minY);
            var rectP2 = new Editor2DPoint(maxX, maxY);

            measurements.Add(new Editor2DMeasurement(
                Id: $"{path.Id}:width",
                Start: new Editor2DPoint(minX, minY - offset),
                End: new Editor2DPoint(maxX, minY - offset),
                IsAutoDimension: true,
                EntityPathId: path.Id,
                DimensionType: "width",
                RectP1: rectP1,
                RectP2: rectP2));
            measurements.Add(new Editor2DMeasurement(
                Id: $"{path.Id}:height",
                Start: new Editor2DPoint(minX - offset, minY),
                End: new Editor2DPoint(minX - offset, maxY),
                IsAutoDimension: true,
                EntityPathId: path.Id,
                DimensionType: "height",
                RectP1: rectP1,
                RectP2: rectP2));
        }

        foreach (var path in document.Paths.Where(static path =>
                     path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
                     && path.Center is not null
                     && path.Radius is not null))
        {
            var center = path.Center!;
            var radius = path.Radius!.Value;
            if (radius <= minimumSize)
                continue;

            measurements.Add(new Editor2DMeasurement(
                Id: $"{path.Id}:radius",
                Start: center,
                End: new Editor2DPoint(center.X + radius, center.Y),
                IsAutoDimension: true,
                EntityPathId: path.Id,
                DimensionType: "radius"));
        }

        return measurements;
    }

    private static Editor2DPreviewDocument CreateUpdatedTwoDDocument(
        Editor2DPreviewDocument document,
        IReadOnlyList<Editor2DPreviewPath> nextPaths)
    {
        var entityCounts = nextPaths
            .GroupBy(static path => path.EntityType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return document with
        {
            Paths = nextPaths,
            Bounds = MeasureTwoDBounds(nextPaths),
            EntityCounts = entityCounts,
        };
    }

    private static Editor2DBounds MeasureTwoDBounds(IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var points = paths.SelectMany(static path => path.Points).ToArray();
        if (points.Length == 0)
            return new Editor2DBounds(0.0, 0.0, 0.0, 0.0);

        var minX = points.Min(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxX = points.Max(static point => point.X);
        var maxY = points.Max(static point => point.Y);
        return new Editor2DBounds(minX, minY, maxX, maxY);
    }
}
