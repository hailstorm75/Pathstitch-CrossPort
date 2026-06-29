using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private sealed record GeneratedOutputConvertLineParameterDefinition(
        string Key,
        string Label,
        double DefaultValue,
        double MinimumValue,
        bool IsInteger = false);

    private static readonly IReadOnlyList<string> GeneratedOutputConvertLineStyleOrder =
    [
        "dashed",
        "dotted",
        "zigzag",
        "wave",
        "striped",
        "square",
        "triangle",
    ];

    private static readonly IReadOnlyDictionary<string, GeneratedOutputConvertLineParameterDefinition[]> GeneratedOutputConvertLineParameterDefinitions =
        new Dictionary<string, GeneratedOutputConvertLineParameterDefinition[]>(StringComparer.OrdinalIgnoreCase)
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

    private static readonly IReadOnlyList<string> GeneratedOutputOffsetModeOptions =
    [
        "Curve",
        "BBox",
    ];

    private static readonly IReadOnlyList<string> GeneratedOutputOffsetSideOptions =
    [
        "Outward",
        "Inward",
    ];

    private static readonly IReadOnlyList<string> GeneratedOutputPatternModeOptions =
    [
        "Rectangular",
        "Circular",
    ];

    private static readonly IReadOnlyList<string> GeneratedOutputGlueTabTypeOptions =
    [
        "Trapezoid",
        "Triangle",
    ];

    private static readonly IReadOnlyList<string> GeneratedOutputGlueTabSideOptions =
    [
        "Left",
        "Right",
    ];

    private EditorGeneratedOutputContext? _generatedOutputContext;
    private string _generatedOutputConvertLineStyle = "dashed";
    private readonly Dictionary<string, string> _generatedOutputConvertLineParameterText = CreateGeneratedOutputConvertLineParameterText();
    private string _generatedOutputOffsetMode = "Curve";
    private string _generatedOutputOffsetSide = "Outward";
    private string _generatedOutputOffsetDistanceText = "12";
    private string _generatedOutputOffsetBBoxDistanceText = "12";
    private string _generatedOutputOffsetBBoxFilletText = "0";
    private string _generatedOutputAddThicknessWidthText = "3";
    private string _generatedOutputCleanupToleranceText = "0.1";
    private string _generatedOutputPatternMode = "Rectangular";
    private string _generatedOutputPatternCopiesXText = "3";
    private string _generatedOutputPatternCopiesYText = "1";
    private string _generatedOutputPatternSpacingXText = "10";
    private string _generatedOutputPatternSpacingYText = "10";
    private string _generatedOutputPatternCircularCountText = "6";
    private string _generatedOutputPatternCircularAngleText = "360";
    private string _generatedOutputGlueTabHeightText = "5";
    private string _generatedOutputGlueTabType = "Trapezoid";
    private string _generatedOutputGlueTabSide = "Left";
    private string _generatedOutputGlueTabStartOffsetText = "0";
    private string _generatedOutputGlueTabEndOffsetText = "0";

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

    public Editor2DPreviewDocument? GeneratedOutputPreviewDocument
    {
        get => _generatedOutputPreviewDocument;
        set
        {
            var normalizedDocument = ApplyExpandedRectangleOverrides(value);
            var documentChanged = SetProperty(ref _generatedOutputPreviewDocument, normalizedDocument);
            SyncGeneratedOutputExpandedRectanglePathIds(normalizedDocument);
            if (!documentChanged)
                return;

            OnPropertyChanged(nameof(HasGeneratedOutputPreview));
            OnPropertyChanged(nameof(HasNoGeneratedOutputPreview));
            OnPropertyChanged(nameof(GeneratedOutputViewportSummary));
            OnPropertyChanged(nameof(GeneratedOutputSelectionSummary));
            OnPropertyChanged(nameof(OutputStatusSummary));
            OnPropertyChanged(nameof(OutputPreviewButtonLabel));
            OnPropertyChanged(nameof(CanFrameHome));
            OnPropertyChanged(nameof(HasGeneratedOutputConvertibleLineSelection));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputConvertLines));
            OnPropertyChanged(nameof(GeneratedOutputConvertLineSummary));
            OnPropertyChanged(nameof(HasGeneratedOutputCurveOffsetSelection));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputOffset));
            OnPropertyChanged(nameof(GeneratedOutputOffsetSummary));
            OnPropertyChanged(nameof(HasGeneratedOutputThicknessSourceSelection));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputAddThickness));
            OnPropertyChanged(nameof(GeneratedOutputAddThicknessSummary));
            OnPropertyChanged(nameof(HasGeneratedOutputCleanupCandidates));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputCleanup));
            OnPropertyChanged(nameof(GeneratedOutputCleanupSummary));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputPattern));
            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputPaperFoldingCreases));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputGlueTabs));
            OnPropertyChanged(nameof(GeneratedOutputPaperFoldingSummary));
            RefreshDerivedGeneratedOutputMeasurements(normalizedDocument);
            SyncGeneratedOutputSelectedTextEditorState();

            if (!_suppressGeneratedOutputDocumentPersistence)
                RequestGeneratedOutputDocumentPersistence(TimeSpan.FromMilliseconds(80));
        }
    }

    public Editor2DTool GeneratedOutputActiveTool
    {
        get => _generatedOutputActiveTool;
        set
        {
            if (!SetProperty(ref _generatedOutputActiveTool, value))
                return;

            OnPropertyChanged(nameof(IsGeneratedOutputSelectToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputMoveToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputPanToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputMeasureToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputDimensionToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputLineToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputRectangleToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputCircleToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputPolygonToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputTextToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputPenToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputScaleToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputMirrorToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputTrimToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputFilletToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputChamferToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputConvertLinesToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputOffsetToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputAddThicknessToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputCleanupToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputPatternToolActive));
            OnPropertyChanged(nameof(IsGeneratedOutputPaperFoldingToolActive));
            OnPropertyChanged(nameof(GeneratedOutputToolHint));
        }
    }

    public int GeneratedOutputPolygonSides
    {
        get => _generatedOutputPolygonSides;
        set
        {
            var normalized = Math.Clamp(value, 3, 64);
            if (!SetProperty(ref _generatedOutputPolygonSides, normalized))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPolygonSidesSummary));
            if (!_suppressGeneratedOutputViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public IReadOnlyList<string> GeneratedOutputSelectedPathIds
    {
        get => _generatedOutputSelectedPathIds;
        set
        {
            var normalized = value
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (!SetProperty(ref _generatedOutputSelectedPathIds, normalized))
                return;

            OnPropertyChanged(nameof(HasGeneratedOutputSelection));
            OnPropertyChanged(nameof(GeneratedOutputSelectionCount));
            OnPropertyChanged(nameof(GeneratedOutputSelectedRectangleCount));
            OnPropertyChanged(nameof(CanExpandGeneratedOutputRectangles));
            OnPropertyChanged(nameof(GeneratedOutputSelectionSummary));
            OnPropertyChanged(nameof(GeneratedOutputToolHint));
            OnPropertyChanged(nameof(HasGeneratedOutputConvertibleLineSelection));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputConvertLines));
            OnPropertyChanged(nameof(GeneratedOutputConvertLineSummary));
            OnPropertyChanged(nameof(HasGeneratedOutputCurveOffsetSelection));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputOffset));
            OnPropertyChanged(nameof(GeneratedOutputOffsetSummary));
            OnPropertyChanged(nameof(HasGeneratedOutputThicknessSourceSelection));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputAddThickness));
            OnPropertyChanged(nameof(GeneratedOutputAddThicknessSummary));
            OnPropertyChanged(nameof(HasGeneratedOutputCleanupCandidates));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputCleanup));
            OnPropertyChanged(nameof(GeneratedOutputCleanupSummary));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputPattern));
            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputPaperFoldingCreases));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputGlueTabs));
            OnPropertyChanged(nameof(GeneratedOutputPaperFoldingSummary));
            SyncGeneratedOutputSelectedTextEditorState();
        }
    }

    public IReadOnlyList<Editor2DMeasurement> GeneratedOutputMeasurements
    {
        get => _generatedOutputMeasurements;
        set
        {
            var normalized = value
                .DistinctBy(static measurement => measurement.Id)
                .ToArray();
            if (!SetProperty(ref _generatedOutputMeasurements, normalized))
                return;

            if (!string.IsNullOrWhiteSpace(GeneratedOutputSelectedMeasurementId)
                && normalized.All(measurement => !string.Equals(measurement.Id, GeneratedOutputSelectedMeasurementId, StringComparison.Ordinal)))
            {
                GeneratedOutputSelectedMeasurementId = null;
            }

            OnPropertyChanged(nameof(HasGeneratedOutputMeasurements));
            OnPropertyChanged(nameof(GeneratedOutputAutoDimensionCount));
            OnPropertyChanged(nameof(GeneratedOutputMeasurementSummary));
            OnPropertyChanged(nameof(GeneratedOutputToolHint));
        }
    }

    public string? GeneratedOutputSelectedMeasurementId
    {
        get => _generatedOutputSelectedMeasurementId;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (!SetProperty(ref _generatedOutputSelectedMeasurementId, normalized))
                return;

            OnPropertyChanged(nameof(HasGeneratedOutputSelectedMeasurement));
            OnPropertyChanged(nameof(GeneratedOutputMeasurementSummary));
        }
    }

    public double GeneratedOutputViewportZoom
    {
        get => _generatedOutputViewportZoom;
        set
        {
            if (!SetProperty(ref _generatedOutputViewportZoom, value))
                return;

            OnPropertyChanged(nameof(GeneratedOutputViewportSummary));
            if (!_suppressGeneratedOutputViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public double GeneratedOutputViewportOffsetX
    {
        get => _generatedOutputViewportOffsetX;
        set
        {
            if (!SetProperty(ref _generatedOutputViewportOffsetX, value))
                return;

            if (!_suppressGeneratedOutputViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public double GeneratedOutputViewportOffsetY
    {
        get => _generatedOutputViewportOffsetY;
        set
        {
            if (!SetProperty(ref _generatedOutputViewportOffsetY, value))
                return;

            if (!_suppressGeneratedOutputViewportPersistence)
                Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public int GeneratedOutputFrameRequestToken
    {
        get => _generatedOutputFrameRequestToken;
        private set => SetProperty(ref _generatedOutputFrameRequestToken, value);
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

    public bool HasGeneratedOutputPreview => GeneratedOutputPreviewDocument is { Paths.Count: > 0 };

    public bool HasNoGeneratedOutputPreview => !HasGeneratedOutputPreview;

    public bool IsGeneratedOutputSelectToolActive => GeneratedOutputActiveTool == Editor2DTool.Select;

    public bool IsGeneratedOutputMoveToolActive => GeneratedOutputActiveTool == Editor2DTool.Move;

    public bool IsGeneratedOutputPanToolActive => GeneratedOutputActiveTool == Editor2DTool.Pan;

    public bool IsGeneratedOutputMeasureToolActive => GeneratedOutputActiveTool == Editor2DTool.Measure;

    public bool IsGeneratedOutputDimensionToolActive => GeneratedOutputActiveTool == Editor2DTool.Dimension;

    public bool IsGeneratedOutputLineToolActive => GeneratedOutputActiveTool == Editor2DTool.SketchLine;

    public bool IsGeneratedOutputRectangleToolActive => GeneratedOutputActiveTool == Editor2DTool.SketchRectangle;

    public bool IsGeneratedOutputCircleToolActive => GeneratedOutputActiveTool == Editor2DTool.SketchCircle;

    public bool IsGeneratedOutputPolygonToolActive => GeneratedOutputActiveTool == Editor2DTool.SketchPolygon;

    public bool IsGeneratedOutputTextToolActive => GeneratedOutputActiveTool == Editor2DTool.SketchText;

    public bool IsGeneratedOutputPenToolActive => GeneratedOutputActiveTool == Editor2DTool.Pen;

    public bool IsGeneratedOutputScaleToolActive => GeneratedOutputActiveTool == Editor2DTool.Scale;

    public bool IsGeneratedOutputMirrorToolActive => GeneratedOutputActiveTool == Editor2DTool.Mirror;

    public bool IsGeneratedOutputTrimToolActive => GeneratedOutputActiveTool == Editor2DTool.Trim;

    public bool IsGeneratedOutputFilletToolActive => GeneratedOutputActiveTool == Editor2DTool.Fillet;

    public bool IsGeneratedOutputChamferToolActive => GeneratedOutputActiveTool == Editor2DTool.Chamfer;

    public bool IsGeneratedOutputConvertLinesToolActive => GeneratedOutputActiveTool == Editor2DTool.ConvertLines;

    public bool IsGeneratedOutputOffsetToolActive => GeneratedOutputActiveTool == Editor2DTool.Offset;

    public bool IsGeneratedOutputAddThicknessToolActive => GeneratedOutputActiveTool == Editor2DTool.AddThickness;

    public bool IsGeneratedOutputCleanupToolActive => GeneratedOutputActiveTool == Editor2DTool.Cleanup;

    public bool IsGeneratedOutputPatternToolActive => GeneratedOutputActiveTool == Editor2DTool.Patterning;

    public bool IsGeneratedOutputPaperFoldingToolActive => GeneratedOutputActiveTool == Editor2DTool.PaperFolding;

    public bool HasGeneratedOutputSelection => GeneratedOutputSelectedPathIds.Count > 0;

    public int GeneratedOutputSelectionCount => GeneratedOutputSelectedPathIds.Count;

    public int GeneratedOutputSelectedRectangleCount
        => GeneratedOutputPreviewDocument is null
            ? 0
            : GeneratedOutputPreviewDocument.Paths.Count(path =>
                path.IsAxisAlignedRectangle
                && GeneratedOutputSelectedPathIds.Contains(path.Id, StringComparer.Ordinal));

    public bool CanExpandGeneratedOutputRectangles => GeneratedOutputSelectedRectangleCount > 0;

    public bool HasGeneratedOutputMeasurements => GeneratedOutputMeasurements.Any(static measurement => !measurement.IsAutoDimension);

    public bool HasGeneratedOutputSelectedMeasurement => !string.IsNullOrWhiteSpace(GeneratedOutputSelectedMeasurementId);

    public int GeneratedOutputAutoDimensionCount => GeneratedOutputMeasurements.Count(static measurement => measurement.IsAutoDimension);

    public bool HasSingleGeneratedOutputTextSelection => TryGetSingleSelectedGeneratedOutputTextPath(out _);

    public IReadOnlyList<string> GeneratedOutputConvertLineStyleOptions => GeneratedOutputConvertLineStyleOrder;

    public string GeneratedOutputConvertLineStyle
    {
        get => _generatedOutputConvertLineStyle;
        set
        {
            var normalized = NormalizeGeneratedOutputConvertLineStyle(value);
            if (!SetProperty(ref _generatedOutputConvertLineStyle, normalized))
                return;

            NotifyGeneratedOutputConvertLineParameterStateChanged();
        }
    }

    public bool HasGeneratedOutputConvertibleLineSelection => GetGeneratedOutputConvertibleSelectionCount() > 0;

    public bool CanApplyGeneratedOutputConvertLines => HasGeneratedOutputConvertibleLineSelection;

    public string GeneratedOutputConvertLineSummary
    {
        get
        {
            var convertibleSelectionCount = GetGeneratedOutputConvertibleSelectionCount();
            return convertibleSelectionCount == 0
                ? "Select LINE, LWPOLYLINE, or POLYLINE geometry to apply a native pattern conversion."
                : $"{convertibleSelectionCount} convertible entit{(convertibleSelectionCount == 1 ? "y" : "ies")} selected. Apply {GeneratedOutputConvertLineStyle} geometry to replace the current linework.";
        }
    }

    public bool HasGeneratedOutputConvertLineFirstParameter => GetGeneratedOutputConvertLineParameterDefinitions(GeneratedOutputConvertLineStyle).Length >= 1;

    public bool HasGeneratedOutputConvertLineSecondParameter => GetGeneratedOutputConvertLineParameterDefinitions(GeneratedOutputConvertLineStyle).Length >= 2;

    public bool HasGeneratedOutputConvertLineThirdParameter => GetGeneratedOutputConvertLineParameterDefinitions(GeneratedOutputConvertLineStyle).Length >= 3;

    public string GeneratedOutputConvertLineFirstParameterLabel => GetGeneratedOutputConvertLineParameterLabel(0);

    public string GeneratedOutputConvertLineSecondParameterLabel => GetGeneratedOutputConvertLineParameterLabel(1);

    public string GeneratedOutputConvertLineThirdParameterLabel => GetGeneratedOutputConvertLineParameterLabel(2);

    public string GeneratedOutputConvertLineFirstParameterText
    {
        get => GetGeneratedOutputConvertLineParameterText(0);
        set => SetGeneratedOutputConvertLineParameterText(0, value);
    }

    public string GeneratedOutputConvertLineSecondParameterText
    {
        get => GetGeneratedOutputConvertLineParameterText(1);
        set => SetGeneratedOutputConvertLineParameterText(1, value);
    }

    public string GeneratedOutputConvertLineThirdParameterText
    {
        get => GetGeneratedOutputConvertLineParameterText(2);
        set => SetGeneratedOutputConvertLineParameterText(2, value);
    }

    public IReadOnlyList<string> GeneratedOutputOffsetModeOptionItems => GeneratedOutputOffsetModeOptions;

    public IReadOnlyList<string> GeneratedOutputOffsetSideOptionItems => GeneratedOutputOffsetSideOptions;

    public string GeneratedOutputOffsetMode
    {
        get => _generatedOutputOffsetMode;
        set
        {
            var normalized = NormalizeGeneratedOutputOffsetMode(value);
            if (!SetProperty(ref _generatedOutputOffsetMode, normalized))
                return;

            OnPropertyChanged(nameof(IsGeneratedOutputCurveOffsetMode));
            OnPropertyChanged(nameof(IsGeneratedOutputBBoxOffsetMode));
            OnPropertyChanged(nameof(HasGeneratedOutputCurveOffsetSelection));
            OnPropertyChanged(nameof(CanApplyGeneratedOutputOffset));
            OnPropertyChanged(nameof(GeneratedOutputOffsetSummary));
        }
    }

    public string GeneratedOutputOffsetSide
    {
        get => _generatedOutputOffsetSide;
        set
        {
            var normalized = NormalizeGeneratedOutputOffsetSide(value);
            if (!SetProperty(ref _generatedOutputOffsetSide, normalized))
                return;

            OnPropertyChanged(nameof(GeneratedOutputOffsetSummary));
        }
    }

    public string GeneratedOutputOffsetDistanceText
    {
        get => _generatedOutputOffsetDistanceText;
        set => SetProperty(ref _generatedOutputOffsetDistanceText, value ?? string.Empty);
    }

    public string GeneratedOutputOffsetBBoxDistanceText
    {
        get => _generatedOutputOffsetBBoxDistanceText;
        set => SetProperty(ref _generatedOutputOffsetBBoxDistanceText, value ?? string.Empty);
    }

    public string GeneratedOutputOffsetBBoxFilletText
    {
        get => _generatedOutputOffsetBBoxFilletText;
        set => SetProperty(ref _generatedOutputOffsetBBoxFilletText, value ?? string.Empty);
    }

    public bool IsGeneratedOutputCurveOffsetMode => string.Equals(GeneratedOutputOffsetMode, "Curve", StringComparison.Ordinal);

    public bool IsGeneratedOutputBBoxOffsetMode => string.Equals(GeneratedOutputOffsetMode, "BBox", StringComparison.Ordinal);

    public bool HasGeneratedOutputCurveOffsetSelection => GetGeneratedOutputCurveOffsetSelectionCount() > 0;

    public bool CanApplyGeneratedOutputOffset
        => IsGeneratedOutputBBoxOffsetMode
            ? HasGeneratedOutputSelection
            : HasGeneratedOutputCurveOffsetSelection;

    public string GeneratedOutputOffsetSummary
    {
        get
        {
            if (IsGeneratedOutputBBoxOffsetMode)
            {
                return HasGeneratedOutputSelection
                    ? $"BBox offset will wrap the current selection in a new rectangular profile. Rounded corners use the current fillet value."
                    : "Select one or more 2D entities to create a bounding-box offset profile.";
            }

            var curveOffsetSelectionCount = GetGeneratedOutputCurveOffsetSelectionCount();
            return curveOffsetSelectionCount == 0
                ? "Select LINE, LWPOLYLINE, POLYLINE, CIRCLE, or ARC geometry to add a native offset copy."
                : $"{curveOffsetSelectionCount} offsettable entit{(curveOffsetSelectionCount == 1 ? "y" : "ies")} selected. Open-path offsets follow path direction; closed paths expand or shrink.";
        }
    }

    public string GeneratedOutputAddThicknessWidthText
    {
        get => _generatedOutputAddThicknessWidthText;
        set => SetProperty(ref _generatedOutputAddThicknessWidthText, value ?? string.Empty);
    }

    public bool HasGeneratedOutputThicknessSourceSelection => GetGeneratedOutputThicknessSourcePathCount() > 0;

    public bool CanApplyGeneratedOutputAddThickness => GetGeneratedOutputThicknessCandidatePaths().Count > 0;

    public string GeneratedOutputAddThicknessSummary
    {
        get
        {
            var selectedSourceCount = GetGeneratedOutputThicknessSourcePathCount();
            if (GeneratedOutputSelectedPathIds.Count > 0)
            {
                return selectedSourceCount == 0
                    ? "The current selection does not contain open line or polyline centerlines that can be thickened."
                    : $"{selectedSourceCount} open line/polyline centerline{(selectedSourceCount == 1 ? string.Empty : "s")} selected. Apply Thickness to create closed outline geometry.";
            }

            var fallbackCount = GetGeneratedOutputThicknessCandidatePaths().Count;
            return fallbackCount == 0
                ? "No eligible open line/polyline centerlines are loaded in the 2D workspace."
                : $"No selection is active. Applying Thickness will process all {fallbackCount} eligible open line/polyline centerlines in the current 2D workspace.";
        }
    }

    public string GeneratedOutputCleanupToleranceText
    {
        get => _generatedOutputCleanupToleranceText;
        set => SetProperty(ref _generatedOutputCleanupToleranceText, value ?? string.Empty);
    }

    public bool HasGeneratedOutputCleanupCandidates => GetGeneratedOutputCleanupCandidatePaths().Count > 0;

    public bool CanApplyGeneratedOutputCleanup => HasGeneratedOutputCleanupCandidates;

    public string GeneratedOutputCleanupSummary
    {
        get
        {
            var candidateCount = GetGeneratedOutputCleanupCandidatePaths().Count;
            return candidateCount == 0
                ? "No open line or polyline chains are available for Join/Cleanup."
                : $"Join/Cleanup will process {candidateCount} open line/polyline entit{(candidateCount == 1 ? "y" : "ies")} in the current 2D workspace. This first native pass joins nearby endpoints and removes degenerate open segments.";
        }
    }

    public IReadOnlyList<string> GeneratedOutputPatternModeOptionItems => GeneratedOutputPatternModeOptions;

    public string GeneratedOutputPatternMode
    {
        get => _generatedOutputPatternMode;
        set
        {
            var normalized = NormalizeGeneratedOutputPatternMode(value);
            if (!SetProperty(ref _generatedOutputPatternMode, normalized))
                return;

            OnPropertyChanged(nameof(IsGeneratedOutputRectangularPatternMode));
            OnPropertyChanged(nameof(IsGeneratedOutputCircularPatternMode));
            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
        }
    }

    public bool IsGeneratedOutputRectangularPatternMode => string.Equals(GeneratedOutputPatternMode, "Rectangular", StringComparison.Ordinal);

    public bool IsGeneratedOutputCircularPatternMode => string.Equals(GeneratedOutputPatternMode, "Circular", StringComparison.Ordinal);

    public string GeneratedOutputPatternCopiesXText
    {
        get => _generatedOutputPatternCopiesXText;
        set
        {
            if (!SetProperty(ref _generatedOutputPatternCopiesXText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
        }
    }

    public string GeneratedOutputPatternCopiesYText
    {
        get => _generatedOutputPatternCopiesYText;
        set
        {
            if (!SetProperty(ref _generatedOutputPatternCopiesYText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
        }
    }

    public string GeneratedOutputPatternSpacingXText
    {
        get => _generatedOutputPatternSpacingXText;
        set
        {
            if (!SetProperty(ref _generatedOutputPatternSpacingXText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
        }
    }

    public string GeneratedOutputPatternSpacingYText
    {
        get => _generatedOutputPatternSpacingYText;
        set
        {
            if (!SetProperty(ref _generatedOutputPatternSpacingYText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
        }
    }

    public string GeneratedOutputPatternCircularCountText
    {
        get => _generatedOutputPatternCircularCountText;
        set
        {
            if (!SetProperty(ref _generatedOutputPatternCircularCountText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
        }
    }

    public string GeneratedOutputPatternCircularAngleText
    {
        get => _generatedOutputPatternCircularAngleText;
        set
        {
            if (!SetProperty(ref _generatedOutputPatternCircularAngleText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPatternSummary));
        }
    }

    public bool CanApplyGeneratedOutputPattern => GeneratedOutputPreviewDocument is not null && HasGeneratedOutputSelection;

    public string GeneratedOutputPatternSummary
    {
        get
        {
            if (!HasGeneratedOutputSelection)
                return "Select one or more 2D entities before applying Pattern. This first native pass supports rectangular and circular duplication only.";

            if (IsGeneratedOutputCircularPatternMode)
            {
                return $"Circular pattern will create {GeneratedOutputPatternCircularCountText} total instance(s) across {GeneratedOutputPatternCircularAngleText} deg around the current selection center. Count includes the source selection.";
            }

            return $"Rectangular pattern will create a {GeneratedOutputPatternCopiesXText} x {GeneratedOutputPatternCopiesYText} layout using {GeneratedOutputPatternSpacingXText} mm / {GeneratedOutputPatternSpacingYText} mm spacing. Counts include the source selection.";
        }
    }

    public IReadOnlyList<string> GeneratedOutputGlueTabTypeOptionItems => GeneratedOutputGlueTabTypeOptions;

    public IReadOnlyList<string> GeneratedOutputGlueTabSideOptionItems => GeneratedOutputGlueTabSideOptions;

    public string GeneratedOutputGlueTabType
    {
        get => _generatedOutputGlueTabType;
        set
        {
            var normalized = NormalizeGeneratedOutputGlueTabType(value);
            if (!SetProperty(ref _generatedOutputGlueTabType, normalized))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPaperFoldingSummary));
        }
    }

    public string GeneratedOutputGlueTabSide
    {
        get => _generatedOutputGlueTabSide;
        set
        {
            var normalized = NormalizeGeneratedOutputGlueTabSide(value);
            if (!SetProperty(ref _generatedOutputGlueTabSide, normalized))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPaperFoldingSummary));
        }
    }

    public string GeneratedOutputGlueTabHeightText
    {
        get => _generatedOutputGlueTabHeightText;
        set
        {
            if (!SetProperty(ref _generatedOutputGlueTabHeightText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPaperFoldingSummary));
        }
    }

    public string GeneratedOutputGlueTabStartOffsetText
    {
        get => _generatedOutputGlueTabStartOffsetText;
        set
        {
            if (!SetProperty(ref _generatedOutputGlueTabStartOffsetText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPaperFoldingSummary));
        }
    }

    public string GeneratedOutputGlueTabEndOffsetText
    {
        get => _generatedOutputGlueTabEndOffsetText;
        set
        {
            if (!SetProperty(ref _generatedOutputGlueTabEndOffsetText, value ?? string.Empty))
                return;

            OnPropertyChanged(nameof(GeneratedOutputPaperFoldingSummary));
        }
    }

    public bool CanApplyGeneratedOutputPaperFoldingCreases => HasGeneratedOutputConvertibleLineSelection;

    public bool CanApplyGeneratedOutputGlueTabs => GetSelectedGeneratedOutputGlueTabPaths().Count > 0;

    public string GeneratedOutputPaperFoldingSummary
    {
        get
        {
            var creaseCount = GetGeneratedOutputConvertibleSelectionCount();
            var glueTabCount = GetSelectedGeneratedOutputGlueTabPaths().Count;
            if (!HasGeneratedOutputSelection)
                return "Select line or polyline geometry to convert it into dashed crease geometry, or select LINE entities to add glue tabs.";

            if (glueTabCount > 0)
            {
                return $"{glueTabCount} LINE entit{(glueTabCount == 1 ? "y is" : "ies are")} ready for {GeneratedOutputGlueTabType.ToLowerInvariant()} glue tabs on the {GeneratedOutputGlueTabSide.ToLowerInvariant()} side. Dashed creases can use {creaseCount} selected line/polyline entit{(creaseCount == 1 ? "y" : "ies")}.";
            }

            return creaseCount == 0
                ? "The current selection can be used for Paper Folding only if it contains line or polyline geometry."
                : $"{creaseCount} selected line/polyline entit{(creaseCount == 1 ? "y is" : "ies are")} ready for dashed crease conversion. Select LINE entities to enable glue tabs.";
        }
    }

    public string GeneratedOutputSelectedTextDraft
    {
        get => _generatedOutputSelectedTextDraft;
        set => SetProperty(ref _generatedOutputSelectedTextDraft, value ?? string.Empty);
    }

    public string GeneratedOutputSelectedTextHeightText
    {
        get => _generatedOutputSelectedTextHeightText;
        set
        {
            if (!SetProperty(ref _generatedOutputSelectedTextHeightText, value ?? string.Empty))
                return;

            SetGeneratedOutputSelectedTextHeightValidity(true);
            OnPropertyChanged(nameof(CanApplyGeneratedOutputSelectedText));
        }
    }

    public bool IsGeneratedOutputSelectedTextHeightValid => _isGeneratedOutputSelectedTextHeightValid;

    public bool IsGeneratedOutputSelectedTextHeightInvalid => !_isGeneratedOutputSelectedTextHeightValid;

    public bool CanApplyGeneratedOutputSelectedText
        => HasSingleGeneratedOutputTextSelection && !string.IsNullOrWhiteSpace(GeneratedOutputSelectedTextHeightText);

    public bool IsShowingGeneratedOutputWorkspace
    {
        get => _isShowingGeneratedOutputWorkspace;
        private set
        {
            if (!SetProperty(ref _isShowingGeneratedOutputWorkspace, value))
                return;

            SyncSidebarToolStates();
            OnPropertyChanged(nameof(IsShowing3DWorkspace));
            OnPropertyChanged(nameof(WorkspaceSurfaceTitle));
            OnPropertyChanged(nameof(WorkspaceModeHint));
            OnPropertyChanged(nameof(OutputPreviewButtonLabel));
            OnPropertyChanged(nameof(OutputStatusSummary));
            OnPropertyChanged(nameof(ShowViewportEmptyState));
            OnPropertyChanged(nameof(CanFrameHome));
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
    }

    public bool IsShowing3DWorkspace => !IsShowingGeneratedOutputWorkspace;

    public string WorkspaceSurfaceTitle => IsShowingGeneratedOutputWorkspace ? "2D Workspace" : "3D Workspace";

    public string OutputStatusSummary
    {
        get
        {
            if (!HasGeneratedOutput)
                return "Run Projection or Unfold to create a 2D result.";

            if (GeneratedOutputSummary is null)
                return "Generated output saved. Refresh the inspector to load DXF details.";

            if (!GeneratedOutputSummary.FileExists)
                return "The saved DXF path no longer exists on disk. Regenerate or restore the file.";

            if (HasGeneratedOutputPreview)
            {
                return IsShowingGeneratedOutputWorkspace
                    ? "Native 2D workspace active."
                    : "2D workspace ready.";
            }

            return GeneratedOutputSummary.PreviewPathCount == 0
                ? GeneratedOutputSummary.UnsupportedEntityCount > 0
                    ? "DXF found, but it only contains entities the native preview does not render yet."
                    : "DXF found, but no previewable linework was detected."
                : "DXF inspected. Open or reveal the file for a closer check.";
        }
    }

    public string OutputPreviewButtonLabel
        => IsShowingGeneratedOutputWorkspace
            ? "2D Workspace Visible"
            : !HasGeneratedOutputFileOnDisk
                ? "File Missing"
                : HasGeneratedOutputPreview
                    ? "Show 2D Workspace"
                    : "Preview Unavailable";

    public string GeneratedOutputViewportSummary => GeneratedOutputPreviewDocument is { Paths.Count: > 0 } document
        ? GeneratedOutputViewportZoom > 0.0
            ? $"Native viewport · {document.Paths.Count} path(s) · {GeneratedOutputViewportZoom * 100.0:0}%"
            : $"Native viewport · {document.Paths.Count} path(s)"
        : "No native 2D viewport loaded.";

    public string GeneratedOutputSelectionSummary => !HasGeneratedOutputPreview
        ? "No 2D geometry loaded."
        : GeneratedOutputSelectionCount switch
        {
            0 => "No 2D entities selected.",
            1 when GeneratedOutputSelectedRectangleCount == 1 => "1 constrained rectangle selected.",
            1 => "1 2D entity selected.",
            _ when GeneratedOutputSelectedRectangleCount == 0 => $"{GeneratedOutputSelectionCount} 2D entities selected.",
            _ => $"{GeneratedOutputSelectionCount} 2D entities selected ({GeneratedOutputSelectedRectangleCount} constrained rectangle{(GeneratedOutputSelectedRectangleCount == 1 ? string.Empty : "s")}).",
        };

    public string GeneratedOutputMeasurementSummary
    {
        get
        {
            var manualMeasurements = GeneratedOutputMeasurements
                .Where(static measurement => !measurement.IsAutoDimension)
                .ToArray();
            var autoDimensionCount = GeneratedOutputAutoDimensionCount;
            var selectedMeasurement = !string.IsNullOrWhiteSpace(GeneratedOutputSelectedMeasurementId)
                ? manualMeasurements.FirstOrDefault(measurement => string.Equals(measurement.Id, GeneratedOutputSelectedMeasurementId, StringComparison.Ordinal))
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

    public string WorkspaceModeHint => IsShowingGeneratedOutputWorkspace
        ? "Viewing the native 2D workspace. Use the 2D tool chips for selection, moving, scaling, mirroring, offsetting, thickening, cleanup, patterning, paper folding, dimensioning, trimming, filleting, chamfering, line conversion, drawing, pen paths, text, panning, and manual measurements. Rectangles and circles now generate attached auto dimensions."
        : !HasLoadedModel
            ? "Load a model to begin the 3D workspace."
            : !HasUsableSourceModelAsset
                ? "Viewing a restored 3D workspace. Native operations stay disabled until the source asset is restored."
                : "Viewing the interactive 3D workspace.";

    public string GeneratedOutputPolygonSidesSummary => $"Polygon sides: {GeneratedOutputPolygonSides}";

    public string GeneratedOutputToolHint => GeneratedOutputActiveTool switch
    {
        Editor2DTool.Select => "Select tool: click linework to select it, or drag a marquee to select multiple paths. Shift-click adds or removes from the selection.",
        Editor2DTool.Move => "Move tool: drag a selected entity set to reposition it directly in the 2D workspace.",
        Editor2DTool.Pan => "Pan tool: left-drag to move the 2D workspace. Mouse wheel zoom stays available on every tool.",
        Editor2DTool.Measure => "Measure tool: click once to place the start point, click again to place the end point, and press Escape to cancel the in-progress measurement.",
        Editor2DTool.Dimension => "Dimension tool: click a line to place an attached length dimension, click a circle or arc to place an attached radius dimension, or click empty space twice for a reference distance. Press Escape to cancel an in-progress reference dimension.",
        Editor2DTool.Scale => "Scale tool: select entities, then drag the corner scale handle to scale them uniformly around the selection center.",
        Editor2DTool.Mirror => "Mirror tool: select entities, click once to place the mirror axis start, then click again to place the axis end and mirror the selection.",
        Editor2DTool.Offset => "Offset tool: keep geometry selected, choose Curve or BBox mode in the lower 2D panel, and apply a native offset copy. Open paths offset relative to their point order.",
        Editor2DTool.AddThickness => "Add Thickness tool: thicken selected open line or polyline centerlines into closed outlines, or process every eligible open centerline when nothing is selected. This first native pass does not handle already-closed regions.",
        Editor2DTool.Cleanup => "Join/Cleanup tool: apply endpoint cleanup across open line and polyline geometry using the current tolerance. This first native pass joins nearby chain endpoints and removes degenerate open segments.",
        Editor2DTool.Patterning => "Pattern tool: keep geometry selected, choose Rectangular or Circular mode in the lower 2D panel, and apply native duplicates. This first native pass uses the current selection center as the circular pivot.",
        Editor2DTool.PaperFolding => "Paper Folding tool: convert selected linework to dashed crease geometry or add glue-tab outlines to selected LINE entities. This first native pass writes explicit geometry instead of DXF linetype metadata.",
        Editor2DTool.Trim => "Trim tool: click a hovered straight segment to remove the piece under the cursor between the nearest intersections. This first native pass trims lines and polylines.",
        Editor2DTool.Fillet => "Fillet tool: click a polyline-style corner handle to round that corner with a default radius. This first native pass applies local geometry directly and does not yet support parametric re-editing.",
        Editor2DTool.Chamfer => "Chamfer tool: click a polyline-style corner handle to bevel that corner with a default setback. This first native pass applies local geometry directly and does not yet support parametric re-editing.",
        Editor2DTool.ConvertLines => "Convert Lines tool: keep line or polyline geometry selected, pick a style in the lower 2D panel, and apply it to replace the selected source paths with native patterned geometry.",
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

    public void Show3DWorkspace()
    {
        IsShowingGeneratedOutputWorkspace = false;
        StatusText = "3D workspace active";
    }

    public void ShowGeneratedOutputWorkspace()
    {
        if (!HasGeneratedOutputPreview)
            return;

        ActivateOutputTool();
        IsShowingGeneratedOutputWorkspace = true;
        StatusText = "2D workspace active";
    }

    public void ActivateGeneratedOutputSelectTool() => GeneratedOutputActiveTool = Editor2DTool.Select;

    public void ActivateGeneratedOutputMoveTool() => GeneratedOutputActiveTool = Editor2DTool.Move;

    public void ActivateGeneratedOutputPanTool() => GeneratedOutputActiveTool = Editor2DTool.Pan;

    public void ActivateGeneratedOutputMeasureTool() => GeneratedOutputActiveTool = Editor2DTool.Measure;

    public void ActivateGeneratedOutputDimensionTool() => GeneratedOutputActiveTool = Editor2DTool.Dimension;

    public void ActivateGeneratedOutputScaleTool() => GeneratedOutputActiveTool = Editor2DTool.Scale;

    public void ActivateGeneratedOutputMirrorTool() => GeneratedOutputActiveTool = Editor2DTool.Mirror;

    public void ActivateGeneratedOutputOffsetTool() => GeneratedOutputActiveTool = Editor2DTool.Offset;

    public void ActivateGeneratedOutputAddThicknessTool() => GeneratedOutputActiveTool = Editor2DTool.AddThickness;

    public void ActivateGeneratedOutputCleanupTool() => GeneratedOutputActiveTool = Editor2DTool.Cleanup;

    public void ActivateGeneratedOutputPatternTool() => GeneratedOutputActiveTool = Editor2DTool.Patterning;

    public void ActivateGeneratedOutputPaperFoldingTool() => GeneratedOutputActiveTool = Editor2DTool.PaperFolding;

    public void ActivateGeneratedOutputTrimTool() => GeneratedOutputActiveTool = Editor2DTool.Trim;

    public void ActivateGeneratedOutputFilletTool() => GeneratedOutputActiveTool = Editor2DTool.Fillet;

    public void ActivateGeneratedOutputChamferTool() => GeneratedOutputActiveTool = Editor2DTool.Chamfer;

    public void ActivateGeneratedOutputConvertLinesTool() => GeneratedOutputActiveTool = Editor2DTool.ConvertLines;

    public void ActivateGeneratedOutputLineTool() => GeneratedOutputActiveTool = Editor2DTool.SketchLine;

    public void ActivateGeneratedOutputRectangleTool() => GeneratedOutputActiveTool = Editor2DTool.SketchRectangle;

    public void ActivateGeneratedOutputCircleTool() => GeneratedOutputActiveTool = Editor2DTool.SketchCircle;

    public void ActivateGeneratedOutputPolygonTool() => GeneratedOutputActiveTool = Editor2DTool.SketchPolygon;

    public void ActivateGeneratedOutputTextTool() => GeneratedOutputActiveTool = Editor2DTool.SketchText;

    public void ActivateGeneratedOutputPenTool() => GeneratedOutputActiveTool = Editor2DTool.Pen;

    public void IncrementGeneratedOutputPolygonSides() => GeneratedOutputPolygonSides++;

    public void DecrementGeneratedOutputPolygonSides() => GeneratedOutputPolygonSides--;

    public void ClearGeneratedOutputSelection() => GeneratedOutputSelectedPathIds = [];

    public void ClearGeneratedOutputSelectedMeasurement() => GeneratedOutputSelectedMeasurementId = null;

    public void ClearGeneratedOutputMeasurements()
        => GeneratedOutputMeasurements = GeneratedOutputMeasurements
            .Where(static measurement => measurement.IsAutoDimension)
            .ToArray();

    public bool ApplyGeneratedOutputOffset()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        if (IsGeneratedOutputBBoxOffsetMode)
            return ApplyGeneratedOutputBoundingBoxOffset();

        var curveOffsetPathIds = GetSelectedGeneratedOutputCurveOffsetPathIds();
        if (curveOffsetPathIds.Count == 0)
        {
            StatusText = "Select line, polyline, circle, or arc geometry before applying Offset";
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputOffsetDistanceText, "offset distance", 0.1, out var offsetDistance, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        var offsettableIds = new HashSet<string>(curveOffsetPathIds, StringComparer.Ordinal);
        var nextPaths = GeneratedOutputPreviewDocument.Paths.ToList();
        var nextSelectedIds = new List<string>();
        var createdCount = 0;
        var offsetOutward = string.Equals(GeneratedOutputOffsetSide, "Outward", StringComparison.Ordinal);

        foreach (var path in GeneratedOutputPreviewDocument.Paths)
        {
            if (!selectedIds.Contains(path.Id) || !offsettableIds.Contains(path.Id))
                continue;

            if (!Editor2DGeometry.TryBuildCurveOffsetPath(path, offsetDistance, offsetOutward, out var offsetPath))
                continue;

            nextPaths.Add(offsetPath);
            nextSelectedIds.Add(offsetPath.Id);
            createdCount++;
        }

        if (createdCount == 0)
        {
            StatusText = "The selected geometry could not be offset";
            return false;
        }

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = nextSelectedIds;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = createdCount == 1
            ? $"Created 1 {GeneratedOutputOffsetSide.ToLowerInvariant()} offset path"
            : $"Created {createdCount} {GeneratedOutputOffsetSide.ToLowerInvariant()} offset paths";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ApplyGeneratedOutputAddThickness()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputAddThicknessWidthText, "thickness width", 0.1, out var thickness, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        var sourcePaths = GetGeneratedOutputThicknessCandidatePaths();
        if (sourcePaths.Count == 0)
        {
            StatusText = "No eligible open line or polyline centerlines are available for Add Thickness";
            return false;
        }

        var nextPaths = GeneratedOutputPreviewDocument.Paths.ToList();
        var nextSelectedIds = new List<string>();
        var createdCount = 0;

        foreach (var path in sourcePaths)
        {
            if (!Editor2DGeometry.TryBuildThicknessPath(path, thickness, out var thickenedPath))
                continue;

            nextPaths.Add(thickenedPath);
            nextSelectedIds.Add(thickenedPath.Id);
            createdCount++;
        }

        if (createdCount == 0)
        {
            StatusText = "The selected geometry could not be thickened";
            return false;
        }

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = nextSelectedIds;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = createdCount == 1
            ? "Created 1 thickened outline"
            : $"Created {createdCount} thickened outlines";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ApplyGeneratedOutputCleanup()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputCleanupToleranceText, "cleanup tolerance", 0.0001, out var tolerance, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        var sourcePaths = GetGeneratedOutputCleanupCandidatePaths();
        if (sourcePaths.Count == 0)
        {
            StatusText = "No open line or polyline geometry is available for Join/Cleanup";
            return false;
        }

        var sourceIdSet = sourcePaths
            .Select(static path => path.Id)
            .ToHashSet(StringComparer.Ordinal);
        var cleanedPaths = Editor2DGeometry.BuildCleanupPaths(sourcePaths, tolerance);
        if (cleanedPaths.Count == 0)
        {
            StatusText = "Join/Cleanup could not build any cleaned paths";
            return false;
        }

        var nextPaths = GeneratedOutputPreviewDocument.Paths
            .Where(path => !sourceIdSet.Contains(path.Id))
            .Concat(cleanedPaths)
            .ToArray();
        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = cleanedPaths.Select(static path => path.Id).ToArray();
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = cleanedPaths.Count == 1
            ? "Join/Cleanup produced 1 cleaned path"
            : $"Join/Cleanup produced {cleanedPaths.Count} cleaned paths";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ApplyGeneratedOutputPattern()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        if (!CanApplyGeneratedOutputPattern)
        {
            StatusText = "Select one or more 2D entities before applying Pattern";
            return false;
        }

        return IsGeneratedOutputCircularPatternMode
            ? ApplyGeneratedOutputCircularPattern()
            : ApplyGeneratedOutputRectangularPattern();
    }

    public bool ApplyGeneratedOutputPaperFoldingCreases()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        var convertiblePathIds = GetSelectedGeneratedOutputConvertiblePathIds();
        if (convertiblePathIds.Count == 0)
        {
            StatusText = "Select line or polyline geometry before applying dashed creases";
            return false;
        }

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        var convertibleIdSet = new HashSet<string>(convertiblePathIds, StringComparer.Ordinal);
        var nextPaths = new List<Editor2DPreviewPath>(GeneratedOutputPreviewDocument.Paths.Count);
        var nextSelectedIds = new List<string>();
        var settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["dash_length"] = 2.0,
            ["gap"] = 1.0,
        };
        var convertedEntityCount = 0;

        foreach (var path in GeneratedOutputPreviewDocument.Paths)
        {
            if (!selectedIds.Contains(path.Id) || !convertibleIdSet.Contains(path.Id))
            {
                nextPaths.Add(path);
                continue;
            }

            var creasePaths = Editor2DGeometry.BuildConvertedLinePaths(path, "dashed", settings);
            if (creasePaths.Count == 0)
            {
                nextPaths.Add(path);
                continue;
            }

            nextPaths.AddRange(creasePaths);
            nextSelectedIds.AddRange(creasePaths.Select(static candidate => candidate.Id));
            convertedEntityCount++;
        }

        if (convertedEntityCount == 0)
        {
            StatusText = "The selected geometry could not be converted to crease geometry";
            return false;
        }

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = nextSelectedIds;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = convertedEntityCount == 1
            ? "Converted 1 selected entity to dashed crease geometry"
            : $"Converted {convertedEntityCount} selected entities to dashed crease geometry";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ApplyGeneratedOutputGlueTabs()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        var sourcePaths = GetSelectedGeneratedOutputGlueTabPaths();
        if (sourcePaths.Count == 0)
        {
            StatusText = "Select LINE entities before applying glue tabs";
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputGlueTabHeightText, "glue tab height", 0.1, out var height, out var heightErrorMessage))
        {
            StatusText = heightErrorMessage;
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputGlueTabStartOffsetText, "glue tab start offset", 0.0, out var startOffset, out var startErrorMessage))
        {
            StatusText = startErrorMessage;
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputGlueTabEndOffsetText, "glue tab end offset", 0.0, out var endOffset, out var endErrorMessage))
        {
            StatusText = endErrorMessage;
            return false;
        }

        var nextPaths = GeneratedOutputPreviewDocument.Paths.ToList();
        var nextSelectedIds = new List<string>();

        foreach (var path in sourcePaths)
        {
            if (!Editor2DGeometry.TryBuildGlueTabPath(
                    path,
                    height,
                    GeneratedOutputGlueTabType,
                    GeneratedOutputGlueTabSide,
                    startOffset,
                    endOffset,
                    out var glueTabPath))
            {
                continue;
            }

            nextPaths.Add(glueTabPath);
            nextSelectedIds.Add(glueTabPath.Id);
        }

        if (nextSelectedIds.Count == 0)
        {
            StatusText = "The selected LINE entities could not produce glue tabs";
            return false;
        }

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = nextSelectedIds;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = nextSelectedIds.Count == 1
            ? "Created 1 glue tab outline"
            : $"Created {nextSelectedIds.Count} glue tab outlines";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ApplyGeneratedOutputConvertLines()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        var convertiblePathIds = GetSelectedGeneratedOutputConvertiblePathIds();
        if (convertiblePathIds.Count == 0)
        {
            StatusText = "Select line or polyline geometry before applying Convert Lines";
            return false;
        }

        if (!TryResolveGeneratedOutputConvertLineSettings(out var settings, out var errorMessage))
        {
            StatusText = errorMessage;
            return false;
        }

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        var convertibleIdSet = new HashSet<string>(convertiblePathIds, StringComparer.Ordinal);
        var nextPaths = new List<Editor2DPreviewPath>(GeneratedOutputPreviewDocument.Paths.Count);
        var nextSelectedIds = new List<string>();
        var convertedEntityCount = 0;

        foreach (var path in GeneratedOutputPreviewDocument.Paths)
        {
            if (!selectedIds.Contains(path.Id) || !convertibleIdSet.Contains(path.Id))
            {
                nextPaths.Add(path);
                continue;
            }

            var convertedPaths = Editor2DGeometry.BuildConvertedLinePaths(path, GeneratedOutputConvertLineStyle, settings);
            if (convertedPaths.Count == 0)
            {
                nextPaths.Add(path);
                continue;
            }

            nextPaths.AddRange(convertedPaths);
            nextSelectedIds.AddRange(convertedPaths.Select(static candidate => candidate.Id));
            convertedEntityCount++;
        }

        if (convertedEntityCount == 0)
        {
            StatusText = "The selected geometry could not be converted";
            return false;
        }

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = nextSelectedIds;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = convertedEntityCount == 1
            ? $"Converted 1 selected entity to {GeneratedOutputConvertLineStyle} geometry"
            : $"Converted {convertedEntityCount} selected entities to {GeneratedOutputConvertLineStyle} geometry";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    private bool ApplyGeneratedOutputBoundingBoxOffset()
    {
        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count == 0)
        {
            StatusText = "Select one or more 2D entities before applying BBox Offset";
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputOffsetBBoxDistanceText, "BBox offset distance", 0.1, out var offsetDistance, out var distanceErrorMessage))
        {
            StatusText = distanceErrorMessage;
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputOffsetBBoxFilletText, "BBox fillet radius", 0.0, out var cornerRadius, out var filletErrorMessage))
        {
            StatusText = filletErrorMessage;
            return false;
        }

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        var selectedPaths = GeneratedOutputPreviewDocument.Paths
            .Where(path => selectedIds.Contains(path.Id))
            .ToArray();
        var bboxPath = Editor2DGeometry.BuildBoundingBoxOffsetPath(selectedPaths, offsetDistance, cornerRadius);
        if (bboxPath is null)
        {
            StatusText = "The current selection could not produce a BBox offset";
            return false;
        }

        var nextPaths = GeneratedOutputPreviewDocument.Paths
            .Concat([bboxPath])
            .ToArray();
        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = [bboxPath.Id];
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = "Created a new BBox offset profile";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ApplyGeneratedOutputSelectedText()
    {
        if (GeneratedOutputPreviewDocument is null
            || !TryGetSingleSelectedGeneratedOutputTextPath(out var selectedTextPath))
        {
            return false;
        }

        if (!TryParseGeneratedOutputSelectedTextHeight(GeneratedOutputSelectedTextHeightText, out var textHeight))
        {
            SetGeneratedOutputSelectedTextHeightValidity(false);
            StatusText = "Enter a valid text height in millimeters";
            return false;
        }

        var start = selectedTextPath.Start
            ?? selectedTextPath.Points.FirstOrDefault()
            ?? new Editor2DPoint(0.0, 0.0);
        var normalizedText = string.IsNullOrWhiteSpace(GeneratedOutputSelectedTextDraft)
            ? "Label"
            : GeneratedOutputSelectedTextDraft.Replace("\r\n", "\n");
        var normalizedHeight = Math.Max(textHeight, 0.1);
        var nextTextPath = selectedTextPath with
        {
            Start = start,
            Text = normalizedText,
            TextHeight = normalizedHeight,
            Points = Editor2DGeometry.BuildTextBoundsPoints(
                start,
                normalizedText,
                normalizedHeight,
                selectedTextPath.RotationDegrees ?? 0.0,
                selectedTextPath.WidthFactor ?? 1.0),
        };
        var nextPaths = GeneratedOutputPreviewDocument.Paths
            .Select(path => string.Equals(path.Id, selectedTextPath.Id, StringComparison.Ordinal)
                ? nextTextPath
                : path)
            .ToArray();

        SetGeneratedOutputSelectedTextHeightValidity(true);
        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedTextHeightText = normalizedHeight.ToString("0.###", CultureInfo.InvariantCulture);
        GeneratedOutputSelectedTextDraft = normalizedText;
        StatusText = "Updated the selected text entity";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool DeleteGeneratedOutputSelection()
    {
        if (DeleteGeneratedOutputSelectedMeasurement())
            return true;

        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count == 0)
            return false;

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        var nextPaths = GeneratedOutputPreviewDocument.Paths
            .Where(path => !selectedIds.Contains(path.Id))
            .ToArray();

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = [];
        StatusText = nextPaths.Length == 0
            ? "Deleted the selected 2D entities"
            : $"Deleted {selectedIds.Count} selected 2D entit{(selectedIds.Count == 1 ? "y" : "ies")}";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool DeleteGeneratedOutputSelectedMeasurement()
    {
        if (string.IsNullOrWhiteSpace(GeneratedOutputSelectedMeasurementId))
            return false;

        var nextMeasurements = GeneratedOutputMeasurements
            .Where(measurement => !string.Equals(measurement.Id, GeneratedOutputSelectedMeasurementId, StringComparison.Ordinal))
            .ToArray();
        GeneratedOutputMeasurements = nextMeasurements;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = "Deleted the selected 2D measurement";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ExpandGeneratedOutputRectangles()
    {
        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count == 0)
            return false;

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        var expandedRectangleCount = 0;
        var nextPaths = GeneratedOutputPreviewDocument.Paths
            .Select(path =>
            {
                if (!path.IsAxisAlignedRectangle || !selectedIds.Contains(path.Id))
                    return path;

                expandedRectangleCount++;
                return path with { IsAxisAlignedRectangle = false };
            })
            .ToArray();

        if (expandedRectangleCount == 0)
            return false;

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        StatusText = expandedRectangleCount == 1
            ? "Expanded the selected constrained rectangle"
            : $"Expanded {expandedRectangleCount} selected constrained rectangles";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public void FrameGeneratedOutputToContent()
    {
        if (!HasGeneratedOutputPreview)
            return;

        GeneratedOutputFrameRequestToken++;
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
            : $"{StatusText} and prepared native 2D workspace";
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
            SetGeneratedOutputPreviewDocument(null, activatePreviewWorkspace);
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
        if (summary is { FileExists: true, PreviewPathCount: > 0 })
        {
            previewDocument = await _editorOutputPreviewService
                .LoadPreviewDocumentAsync(outputPath, cancellationToken)
                .ConfigureAwait(true);
        }

        SetGeneratedOutputPreviewDocument(previewDocument, activatePreviewWorkspace);

        if (persistState)
            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }

    private void ClearGeneratedOutputState()
    {
        LastGeneratedOutputPath = null;
        GeneratedOutputSummary = null;
        GeneratedOutputContext = null;
        SetGeneratedOutputPreviewDocument(null, activatePreviewWorkspace: false);
    }

    private void SetGeneratedOutputPreviewDocument(
        Editor2DPreviewDocument? document,
        bool activatePreviewWorkspace = true)
    {
        ApplyGeneratedOutputPreviewDocument(document, requestPersistence: false);
        ResetGeneratedOutputEditorState();
        ResetGeneratedOutputViewportState(document is not null);

        if (document is null)
            IsShowingGeneratedOutputWorkspace = false;
        else if (activatePreviewWorkspace)
            IsShowingGeneratedOutputWorkspace = true;
    }

    private void ResetGeneratedOutputViewportState(bool requestFrame)
    {
        ApplyGeneratedOutputViewportState(0.0, 0.0, 0.0, requestPersistence: false);

        if (requestFrame)
            GeneratedOutputFrameRequestToken++;
    }

    private void ResetGeneratedOutputEditorState()
    {
        GeneratedOutputActiveTool = Editor2DTool.Select;
        GeneratedOutputSelectedPathIds = [];
        GeneratedOutputSelectedMeasurementId = null;
        RefreshDerivedGeneratedOutputMeasurements(GeneratedOutputPreviewDocument);
    }

    private void ApplyGeneratedOutputPreviewDocument(
        Editor2DPreviewDocument? document,
        bool requestPersistence)
    {
        var previousSuppression = _suppressGeneratedOutputDocumentPersistence;
        _suppressGeneratedOutputDocumentPersistence = !requestPersistence;

        try
        {
            GeneratedOutputPreviewDocument = document;
        }
        finally
        {
            _suppressGeneratedOutputDocumentPersistence = previousSuppression;
        }
    }

    private void ApplyGeneratedOutputViewportState(
        double zoom,
        double offsetX,
        double offsetY,
        bool requestPersistence)
    {
        var previousSuppression = _suppressGeneratedOutputViewportPersistence;
        _suppressGeneratedOutputViewportPersistence = true;

        try
        {
            GeneratedOutputViewportZoom = zoom;
            GeneratedOutputViewportOffsetX = offsetX;
            GeneratedOutputViewportOffsetY = offsetY;
        }
        finally
        {
            _suppressGeneratedOutputViewportPersistence = previousSuppression;
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

    private void RefreshDerivedGeneratedOutputMeasurements(Editor2DPreviewDocument? document)
    {
        var manualMeasurements = document is null
            ? _generatedOutputMeasurements
                .Where(static measurement => !measurement.IsAutoDimension)
                .ToArray()
            : RebuildAttachedGeneratedOutputMeasurements(
                document,
                _generatedOutputMeasurements.Where(static measurement => !measurement.IsAutoDimension));
        var autoMeasurements = document is null
            ? []
            : BuildAutoGeneratedOutputMeasurements(document);
        GeneratedOutputMeasurements = autoMeasurements
            .Concat(manualMeasurements)
            .ToArray();
    }

    private static IReadOnlyList<Editor2DMeasurement> RebuildAttachedGeneratedOutputMeasurements(
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
        if (document is null || _generatedOutputExpandedRectanglePathIds.Count == 0)
            return document;

        var expandedRectangleIds = new HashSet<string>(_generatedOutputExpandedRectanglePathIds, StringComparer.Ordinal);
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

    private void SyncGeneratedOutputExpandedRectanglePathIds(Editor2DPreviewDocument? document)
    {
        _generatedOutputExpandedRectanglePathIds = document is null
            ? []
            : document.Paths
                .Where(path => !path.IsAxisAlignedRectangle && Editor2DGeometry.IsAxisAlignedRectangle(path.Points, path.IsClosed))
                .Select(static path => path.Id)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }

    private static IReadOnlyList<string> NormalizeGeneratedOutputExpandedRectanglePathIds(IReadOnlyList<string>? pathIds)
        => pathIds is null
            ? []
            : pathIds
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private void RequestGeneratedOutputDocumentPersistence(TimeSpan? delay = null)
    {
        if (string.IsNullOrWhiteSpace(LastGeneratedOutputPath)
            || GeneratedOutputPreviewDocument is null)
        {
            return;
        }

        var nextCancellationTokenSource = new CancellationTokenSource();
        var previousCancellationTokenSource = _persistGeneratedOutputDocumentCancellationTokenSource;
        _persistGeneratedOutputDocumentCancellationTokenSource = nextCancellationTokenSource;
        previousCancellationTokenSource?.Cancel();
        previousCancellationTokenSource?.Dispose();

        _ = PersistGeneratedOutputDocumentAsync(nextCancellationTokenSource, delay ?? TimeSpan.Zero);
    }

    private async Task PersistGeneratedOutputDocumentAsync(CancellationTokenSource cancellationTokenSource, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationTokenSource.Token).ConfigureAwait(true);

            if (GeneratedOutputPreviewDocument is null || string.IsNullOrWhiteSpace(LastGeneratedOutputPath))
                return;

            await _editorOutputPreviewService
                .SavePreviewDocumentAsync(GeneratedOutputPreviewDocument, LastGeneratedOutputPath, cancellationTokenSource.Token)
                .ConfigureAwait(true);

            _generatedOutputDataBase64 = await TryReadGeneratedOutputDataBase64Async(LastGeneratedOutputPath, cancellationTokenSource.Token)
                .ConfigureAwait(true);

            GeneratedOutputSummary = await _editorOutputPreviewService
                .InspectOutputAsync(LastGeneratedOutputPath, cancellationTokenSource.Token)
                .ConfigureAwait(true);

            Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
        }
        catch (OperationCanceledException)
        {
            // A newer 2D document edit superseded this write.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist editable 2D output for {OutputPath}", LastGeneratedOutputPath);
        }
        finally
        {
            if (ReferenceEquals(_persistGeneratedOutputDocumentCancellationTokenSource, cancellationTokenSource))
                _persistGeneratedOutputDocumentCancellationTokenSource = null;

            cancellationTokenSource.Dispose();
        }
    }

    private static Dictionary<string, string> CreateGeneratedOutputConvertLineParameterText()
    {
        var parameterText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (style, definitions) in GeneratedOutputConvertLineParameterDefinitions)
        {
            foreach (var definition in definitions)
                parameterText[BuildGeneratedOutputConvertLineParameterMapKey(style, definition.Key)] = FormatGeneratedOutputConvertLineValue(definition.DefaultValue, definition.IsInteger);
        }

        return parameterText;
    }

    private static string NormalizeGeneratedOutputConvertLineStyle(string? style)
        => !string.IsNullOrWhiteSpace(style)
           && GeneratedOutputConvertLineParameterDefinitions.ContainsKey(style)
            ? style
            : "dashed";

    private static string NormalizeGeneratedOutputOffsetMode(string? mode)
        => string.Equals(mode, "BBox", StringComparison.OrdinalIgnoreCase)
            ? "BBox"
            : "Curve";

    private static string NormalizeGeneratedOutputPatternMode(string? mode)
        => string.Equals(mode, "Circular", StringComparison.OrdinalIgnoreCase)
            ? "Circular"
            : "Rectangular";

    private static string NormalizeGeneratedOutputGlueTabType(string? tabType)
        => string.Equals(tabType, "Triangle", StringComparison.OrdinalIgnoreCase)
            ? "Triangle"
            : "Trapezoid";

    private static string NormalizeGeneratedOutputGlueTabSide(string? side)
        => string.Equals(side, "Right", StringComparison.OrdinalIgnoreCase)
            ? "Right"
            : "Left";

    private static string NormalizeGeneratedOutputOffsetSide(string? side)
        => string.Equals(side, "Inward", StringComparison.OrdinalIgnoreCase)
            ? "Inward"
            : "Outward";

    private static GeneratedOutputConvertLineParameterDefinition[] GetGeneratedOutputConvertLineParameterDefinitions(string style)
        => GeneratedOutputConvertLineParameterDefinitions.TryGetValue(style, out var definitions)
            ? definitions
            : GeneratedOutputConvertLineParameterDefinitions["dashed"];

    private static string BuildGeneratedOutputConvertLineParameterMapKey(string style, string parameterKey)
        => $"{style}:{parameterKey}";

    private IReadOnlyList<string> GetSelectedGeneratedOutputConvertiblePathIds()
    {
        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        return GeneratedOutputPreviewDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsConvertibleLinePath(path))
            .Select(static path => path.Id)
            .ToArray();
    }

    private int GetGeneratedOutputConvertibleSelectionCount() => GetSelectedGeneratedOutputConvertiblePathIds().Count;

    private IReadOnlyList<string> GetSelectedGeneratedOutputCurveOffsetPathIds()
    {
        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        return GeneratedOutputPreviewDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsCurveOffsettablePath(path))
            .Select(static path => path.Id)
            .ToArray();
    }

    private int GetGeneratedOutputCurveOffsetSelectionCount()
        => IsGeneratedOutputBBoxOffsetMode ? GeneratedOutputSelectionCount : GetSelectedGeneratedOutputCurveOffsetPathIds().Count;

    private IReadOnlyList<Editor2DPreviewPath> GetGeneratedOutputThicknessCandidatePaths()
    {
        if (GeneratedOutputPreviewDocument is null)
            return [];

        if (GeneratedOutputSelectedPathIds.Count == 0)
        {
            return GeneratedOutputPreviewDocument.Paths
                .Where(Editor2DGeometry.IsThicknessSourcePath)
                .ToArray();
        }

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        return GeneratedOutputPreviewDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsThicknessSourcePath(path))
            .ToArray();
    }

    private int GetGeneratedOutputThicknessSourcePathCount() => GetGeneratedOutputThicknessCandidatePaths().Count;

    private IReadOnlyList<Editor2DPreviewPath> GetGeneratedOutputCleanupCandidatePaths()
        => GeneratedOutputPreviewDocument is null
            ? []
            : GeneratedOutputPreviewDocument.Paths
                .Where(Editor2DGeometry.IsCleanupSourcePath)
                .ToArray();

    private IReadOnlyList<Editor2DPreviewPath> GetSelectedGeneratedOutputPaths()
    {
        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        return GeneratedOutputPreviewDocument.Paths
            .Where(path => selectedIds.Contains(path.Id))
            .ToArray();
    }

    private IReadOnlyList<Editor2DPreviewPath> GetSelectedGeneratedOutputGlueTabPaths()
    {
        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count == 0)
            return [];

        var selectedIds = new HashSet<string>(GeneratedOutputSelectedPathIds, StringComparer.Ordinal);
        return GeneratedOutputPreviewDocument.Paths
            .Where(path => selectedIds.Contains(path.Id) && Editor2DGeometry.IsGlueTabSourcePath(path))
            .ToArray();
    }

    private string GetGeneratedOutputConvertLineParameterLabel(int index)
    {
        var definitions = GetGeneratedOutputConvertLineParameterDefinitions(GeneratedOutputConvertLineStyle);
        return index >= 0 && index < definitions.Length
            ? definitions[index].Label
            : string.Empty;
    }

    private string GetGeneratedOutputConvertLineParameterText(int index)
    {
        var definitions = GetGeneratedOutputConvertLineParameterDefinitions(GeneratedOutputConvertLineStyle);
        if (index < 0 || index >= definitions.Length)
            return string.Empty;

        var key = BuildGeneratedOutputConvertLineParameterMapKey(GeneratedOutputConvertLineStyle, definitions[index].Key);
        return _generatedOutputConvertLineParameterText.TryGetValue(key, out var value)
            ? value
            : FormatGeneratedOutputConvertLineValue(definitions[index].DefaultValue, definitions[index].IsInteger);
    }

    private void SetGeneratedOutputConvertLineParameterText(int index, string? value)
    {
        var definitions = GetGeneratedOutputConvertLineParameterDefinitions(GeneratedOutputConvertLineStyle);
        if (index < 0 || index >= definitions.Length)
            return;

        var key = BuildGeneratedOutputConvertLineParameterMapKey(GeneratedOutputConvertLineStyle, definitions[index].Key);
        var normalized = value ?? string.Empty;
        if (_generatedOutputConvertLineParameterText.TryGetValue(key, out var existing)
            && string.Equals(existing, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _generatedOutputConvertLineParameterText[key] = normalized;
        NotifyGeneratedOutputConvertLineParameterStateChanged();
    }

    private void NotifyGeneratedOutputConvertLineParameterStateChanged()
    {
        OnPropertyChanged(nameof(GeneratedOutputConvertLineStyle));
        OnPropertyChanged(nameof(GeneratedOutputConvertLineSummary));
        OnPropertyChanged(nameof(HasGeneratedOutputConvertLineFirstParameter));
        OnPropertyChanged(nameof(HasGeneratedOutputConvertLineSecondParameter));
        OnPropertyChanged(nameof(HasGeneratedOutputConvertLineThirdParameter));
        OnPropertyChanged(nameof(GeneratedOutputConvertLineFirstParameterLabel));
        OnPropertyChanged(nameof(GeneratedOutputConvertLineSecondParameterLabel));
        OnPropertyChanged(nameof(GeneratedOutputConvertLineThirdParameterLabel));
        OnPropertyChanged(nameof(GeneratedOutputConvertLineFirstParameterText));
        OnPropertyChanged(nameof(GeneratedOutputConvertLineSecondParameterText));
        OnPropertyChanged(nameof(GeneratedOutputConvertLineThirdParameterText));
    }

    private bool TryResolveGeneratedOutputConvertLineSettings(
        out IReadOnlyDictionary<string, double> settings,
        out string errorMessage)
    {
        var definitions = GetGeneratedOutputConvertLineParameterDefinitions(GeneratedOutputConvertLineStyle);
        var resolved = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            var rawText = GetGeneratedOutputConvertLineParameterText(index);
            if (!TryParseGeneratedOutputSelectedTextHeight(rawText, out var value))
            {
                settings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                errorMessage = $"Enter a valid value for {definition.Label}";
                return false;
            }

            value = Math.Max(value, definition.MinimumValue);
            if (definition.IsInteger)
                value = Math.Round(value);

            resolved[definition.Key] = value;
            _generatedOutputConvertLineParameterText[BuildGeneratedOutputConvertLineParameterMapKey(GeneratedOutputConvertLineStyle, definition.Key)] =
                FormatGeneratedOutputConvertLineValue(value, definition.IsInteger);
        }

        settings = resolved;
        errorMessage = string.Empty;
        NotifyGeneratedOutputConvertLineParameterStateChanged();
        return true;
    }

    private static string FormatGeneratedOutputConvertLineValue(double value, bool isInteger)
        => isInteger
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryParseGeneratedOutputOffsetDistance(
        string rawText,
        string label,
        double minimumValue,
        out double value,
        out string errorMessage)
    {
        if (!TryParseGeneratedOutputSelectedTextHeight(rawText, out value))
        {
            errorMessage = $"Enter a valid {label}";
            return false;
        }

        value = Math.Max(value, minimumValue);
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryParseGeneratedOutputPatternCount(
        string rawText,
        string label,
        int minimumValue,
        out int value,
        out string errorMessage)
    {
        if (!TryParseGeneratedOutputSelectedTextHeight(rawText, out var parsedValue))
        {
            value = 0;
            errorMessage = $"Enter a valid {label}";
            return false;
        }

        value = Math.Max((int)Math.Round(parsedValue), minimumValue);
        errorMessage = string.Empty;
        return true;
    }

    private bool ApplyGeneratedOutputRectangularPattern()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        var selectedPaths = GetSelectedGeneratedOutputPaths();
        if (selectedPaths.Count == 0)
        {
            StatusText = "Select one or more 2D entities before applying a rectangular pattern";
            return false;
        }

        if (!TryParseGeneratedOutputPatternCount(GeneratedOutputPatternCopiesXText, "pattern X count", 1, out var copiesX, out var copiesXErrorMessage))
        {
            StatusText = copiesXErrorMessage;
            return false;
        }

        if (!TryParseGeneratedOutputPatternCount(GeneratedOutputPatternCopiesYText, "pattern Y count", 1, out var copiesY, out var copiesYErrorMessage))
        {
            StatusText = copiesYErrorMessage;
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputPatternSpacingXText, "pattern X spacing", 0.0, out var spacingX, out var spacingXErrorMessage))
        {
            StatusText = spacingXErrorMessage;
            return false;
        }

        if (!TryParseGeneratedOutputOffsetDistance(GeneratedOutputPatternSpacingYText, "pattern Y spacing", 0.0, out var spacingY, out var spacingYErrorMessage))
        {
            StatusText = spacingYErrorMessage;
            return false;
        }

        if (copiesX == 1 && copiesY == 1)
        {
            StatusText = "Increase the rectangular pattern counts to create at least one duplicate";
            return false;
        }

        var nextPaths = GeneratedOutputPreviewDocument.Paths.ToList();
        var nextSelectedIds = new List<string>();

        for (var row = 0; row < copiesY; row++)
        {
            for (var column = 0; column < copiesX; column++)
            {
                if (row == 0 && column == 0)
                    continue;

                var deltaX = column * spacingX;
                var deltaY = row * spacingY;
                foreach (var path in selectedPaths)
                {
                    var patternedPath = Editor2DGeometry.TranslatePath(
                        path,
                        deltaX,
                        deltaY,
                        $"{path.Id}:pattern:{row}:{column}:{Guid.NewGuid():N}");
                    nextPaths.Add(patternedPath);
                    nextSelectedIds.Add(patternedPath.Id);
                }
            }
        }

        if (nextSelectedIds.Count == 0)
        {
            StatusText = "The selected geometry could not be patterned";
            return false;
        }

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = nextSelectedIds;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = nextSelectedIds.Count == 1
            ? "Created 1 rectangular pattern duplicate"
            : $"Created {nextSelectedIds.Count} rectangular pattern duplicates";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    private bool ApplyGeneratedOutputCircularPattern()
    {
        if (GeneratedOutputPreviewDocument is null)
            return false;

        var selectedPaths = GetSelectedGeneratedOutputPaths();
        if (selectedPaths.Count == 0)
        {
            StatusText = "Select one or more 2D entities before applying a circular pattern";
            return false;
        }

        if (!TryParseGeneratedOutputPatternCount(GeneratedOutputPatternCircularCountText, "pattern count", 1, out var totalCount, out var countErrorMessage))
        {
            StatusText = countErrorMessage;
            return false;
        }

        if (!TryParseGeneratedOutputSelectedTextHeight(GeneratedOutputPatternCircularAngleText, out var totalAngle))
        {
            StatusText = "Enter a valid pattern angle";
            return false;
        }

        if (totalCount <= 1)
        {
            StatusText = "Increase the circular pattern count to create at least one duplicate";
            return false;
        }

        if (!TryGetGeneratedOutputPatternPivot(selectedPaths, out var pivot))
        {
            StatusText = "The current selection does not have enough geometry to compute a circular pattern pivot";
            return false;
        }

        var fullCircle = Math.Abs(Math.Abs(totalAngle) - 360.0) <= 1e-6;
        var angleStep = fullCircle
            ? totalAngle / totalCount
            : totalAngle / Math.Max(totalCount - 1, 1);
        var nextPaths = GeneratedOutputPreviewDocument.Paths.ToList();
        var nextSelectedIds = new List<string>();

        for (var index = 1; index < totalCount; index++)
        {
            var angle = angleStep * index;
            foreach (var path in selectedPaths)
            {
                var patternedPath = Editor2DGeometry.RotatePath(
                    path,
                    pivot,
                    angle,
                    $"{path.Id}:pattern:circular:{index}:{Guid.NewGuid():N}");
                nextPaths.Add(patternedPath);
                nextSelectedIds.Add(patternedPath.Id);
            }
        }

        if (nextSelectedIds.Count == 0)
        {
            StatusText = "The selected geometry could not be patterned";
            return false;
        }

        GeneratedOutputPreviewDocument = CreateUpdatedGeneratedOutputDocument(GeneratedOutputPreviewDocument, nextPaths);
        GeneratedOutputSelectedPathIds = nextSelectedIds;
        GeneratedOutputSelectedMeasurementId = null;
        StatusText = nextSelectedIds.Count == 1
            ? "Created 1 circular pattern duplicate"
            : $"Created {nextSelectedIds.Count} circular pattern duplicates";
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    private static bool TryGetGeneratedOutputPatternPivot(
        IReadOnlyList<Editor2DPreviewPath> selectedPaths,
        out Editor2DPoint pivot)
    {
        var points = selectedPaths
            .SelectMany(static path => path.Points)
            .ToArray();
        if (points.Length == 0)
        {
            pivot = default!;
            return false;
        }

        var minX = points.Min(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxX = points.Max(static point => point.X);
        var maxY = points.Max(static point => point.Y);
        pivot = new Editor2DPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
        return true;
    }

    private void SyncGeneratedOutputSelectedTextEditorState()
    {
        if (!TryGetSingleSelectedGeneratedOutputTextPath(out var selectedTextPath))
        {
            _generatedOutputSelectedTextDraft = string.Empty;
            _generatedOutputSelectedTextHeightText = "5";
            SetGeneratedOutputSelectedTextHeightValidity(true);
            NotifyGeneratedOutputSelectedTextEditorStateChanged();
            return;
        }

        _generatedOutputSelectedTextDraft = selectedTextPath.Text ?? string.Empty;
        _generatedOutputSelectedTextHeightText = Math.Max(selectedTextPath.TextHeight ?? 5.0, 0.1)
            .ToString("0.###", CultureInfo.InvariantCulture);
        SetGeneratedOutputSelectedTextHeightValidity(true);
        NotifyGeneratedOutputSelectedTextEditorStateChanged();
    }

    private void NotifyGeneratedOutputSelectedTextEditorStateChanged()
    {
        OnPropertyChanged(nameof(HasSingleGeneratedOutputTextSelection));
        OnPropertyChanged(nameof(GeneratedOutputSelectedTextDraft));
        OnPropertyChanged(nameof(GeneratedOutputSelectedTextHeightText));
        OnPropertyChanged(nameof(CanApplyGeneratedOutputSelectedText));
    }

    private void SetGeneratedOutputSelectedTextHeightValidity(bool isValid)
    {
        if (_isGeneratedOutputSelectedTextHeightValid == isValid)
            return;

        _isGeneratedOutputSelectedTextHeightValid = isValid;
        OnPropertyChanged(nameof(IsGeneratedOutputSelectedTextHeightValid));
        OnPropertyChanged(nameof(IsGeneratedOutputSelectedTextHeightInvalid));
    }

    private bool TryGetSingleSelectedGeneratedOutputTextPath(out Editor2DPreviewPath selectedTextPath)
    {
        selectedTextPath = null!;
        if (GeneratedOutputPreviewDocument is null || GeneratedOutputSelectedPathIds.Count != 1)
            return false;

        var candidate = GeneratedOutputPreviewDocument.Paths.FirstOrDefault(path =>
            string.Equals(path.Id, GeneratedOutputSelectedPathIds[0], StringComparison.Ordinal)
            && path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
            return false;

        selectedTextPath = candidate;
        return true;
    }

    private static bool TryParseGeneratedOutputSelectedTextHeight(string raw, out double value)
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

    private static IReadOnlyList<Editor2DMeasurement> BuildAutoGeneratedOutputMeasurements(Editor2DPreviewDocument document)
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

    private static Editor2DPreviewDocument CreateUpdatedGeneratedOutputDocument(
        Editor2DPreviewDocument document,
        IReadOnlyList<Editor2DPreviewPath> nextPaths)
    {
        var entityCounts = nextPaths
            .GroupBy(static path => path.EntityType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return document with
        {
            Paths = nextPaths,
            Bounds = MeasureGeneratedOutputBounds(nextPaths),
            EntityCounts = entityCounts,
        };
    }

    private static Editor2DBounds MeasureGeneratedOutputBounds(IReadOnlyList<Editor2DPreviewPath> paths)
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
