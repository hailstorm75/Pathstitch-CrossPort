using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;

namespace Pathstitch.App.Controls;

internal readonly record struct DxfCanvasTransformPrecisionRequest(
    DxfCanvasTransformPrecisionKind Kind,
    string Text,
    string Unit,
    string AutomationName,
    Point Anchor,
    bool Focus);

internal sealed class DxfCanvasSelectionTransformEventArgs(
    Editor2DAffineTransform transform,
    bool createCopy)
{
    public Editor2DAffineTransform Transform { get; } = transform;
    public bool CreateCopy { get; } = createCopy;
    public bool Committed { get; private set; }
    public Editor2DPreviewDocument? Document { get; private set; }
    public IReadOnlyList<string> SelectedPathIds { get; private set; } = [];

    public void Complete(Editor2DPreviewDocument document, IReadOnlyList<string> selectedPathIds)
    {
        Committed = true;
        Document = document;
        SelectedPathIds = selectedPathIds;
    }
}

internal sealed class DxfCanvasPathReplacementEventArgs(
    string sourcePathId,
    IReadOnlyList<Editor2DPreviewPath> replacements)
{
    public string SourcePathId { get; } = sourcePathId;
    public IReadOnlyList<Editor2DPreviewPath> Replacements { get; } = replacements;
    public Editor2DPreviewDocument? Document { get; private set; }

    public void Complete(Editor2DPreviewDocument document) => Document = document;
}

internal readonly record struct DxfCanvasReferenceCalibrationRequest(
    Editor2DPoint Start,
    Editor2DPoint End,
    Point Anchor,
    double MeasuredDistance);

public sealed class DxfPreviewCanvas : Control
{
    /// <summary>Flips vertical wheel/trackpad panning to match the user's preference.</summary>
    public static bool ReversePanDirection { get; set; }

    private const double PointerDragThreshold = 4.0;
    private const double PenCloseHitTolerance = 10.0;
    private const double ScaleHandleHitTolerance = 12.0;
    private const double RotationHandleHitTolerance = 12.0;
    private const double RotationCommitThresholdDegrees = 0.05;
    private const double TranslationHandleHitTolerance = 12.0;
    private const double EmptyWorkspaceSpan = 200.0;
    private const double DefaultTextHeight = 10.0;
    private const string DefaultTextValue = "Label";

    public static readonly StyledProperty<Editor2DPreviewDocument?> DocumentProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DPreviewDocument?>(nameof(Document));

    public static readonly StyledProperty<string?> TextFontPreviewProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string?>(nameof(TextFontPreview));

    public static readonly StyledProperty<string> TextEntryProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string>(
            nameof(TextEntry),
            defaultValue: string.Empty,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Editor2DTool> ActiveToolProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DTool>(
            nameof(ActiveTool),
            defaultValue: Editor2DTool.Select,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> SnapEnabledProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(
            nameof(SnapEnabled),
            defaultValue: true,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> GridVisibleProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(
            nameof(GridVisible),
            defaultValue: true,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ChainSelectionEnabledProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(
            nameof(ChainSelectionEnabled),
            defaultValue: false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> TwoDMoveCreateCopyProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(
            nameof(TwoDMoveCreateCopy),
            defaultValue: false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> TwoDMovePointToPointActiveProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(
            nameof(TwoDMovePointToPointActive),
            defaultValue: false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Editor2DPoint?> TwoDMovePointToPointSourceProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DPoint?>(
            nameof(TwoDMovePointToPointSource),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> PatternModeProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string>(nameof(PatternMode), defaultValue: "Rectangular");

    public static readonly StyledProperty<string?> PatternGuidePathIdProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string?>(nameof(PatternGuidePathId), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Editor2DPoint?> PatternPivotProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DPoint?>(nameof(PatternPivot), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> PatternPivotPickingProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(nameof(PatternPivotPicking), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Editor2DPoint?> ScalePivotProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DPoint?>(nameof(ScalePivot), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ScalePivotPickingProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(nameof(ScalePivotPicking), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ScaleFromCenterProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(nameof(ScaleFromCenter), defaultValue: true);

    public static readonly StyledProperty<string> ScaleFactorTextProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string>(nameof(ScaleFactorText), defaultValue: "1", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> ScaleFactorProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, double>(nameof(ScaleFactor), defaultValue: 1.0);

    public static readonly StyledProperty<bool> TwoDMirrorLineModeProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(nameof(TwoDMirrorLineMode), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> TwoDMirrorFlipCopyProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(nameof(TwoDMirrorFlipCopy), defaultValue: true);

    public static readonly StyledProperty<Editor2DPoint?> TwoDMirrorAxisStartProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DPoint?>(nameof(TwoDMirrorAxisStart), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Editor2DPoint?> TwoDMirrorAxisEndProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DPoint?>(nameof(TwoDMirrorAxisEnd), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<string>> SelectedPathIdsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<string>>(
            nameof(SelectedPathIds),
            defaultValue: Array.Empty<string>(),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<string>> HiddenPathIdsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<string>>(
            nameof(HiddenPathIds),
            defaultValue: Array.Empty<string>());

    public static readonly StyledProperty<IReadOnlyList<Editor2DPreviewPath>> PreviewPathsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DPreviewPath>>(
            nameof(PreviewPaths),
            defaultValue: Array.Empty<Editor2DPreviewPath>());

    public static readonly StyledProperty<double> SewingHoleMarginProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, double>(
            nameof(SewingHoleMargin),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> OffsetDistanceTextProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string>(nameof(OffsetDistanceText), defaultValue: "12", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> OffsetSideProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string>(nameof(OffsetSide), defaultValue: "Outward", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<Editor2DPreviewPath>> OffsetPreviewPathsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DPreviewPath>>(
            nameof(OffsetPreviewPaths),
            defaultValue: Array.Empty<Editor2DPreviewPath>());

    public static readonly StyledProperty<IReadOnlyList<Editor2DPreviewPath>> PatternPreviewPathsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DPreviewPath>>(
            nameof(PatternPreviewPaths),
            defaultValue: Array.Empty<Editor2DPreviewPath>());

    public static readonly StyledProperty<IReadOnlyList<Editor2DPreviewPath>> GlueTabPreviewPathsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DPreviewPath>>(
            nameof(GlueTabPreviewPaths),
            defaultValue: Array.Empty<Editor2DPreviewPath>());

    public static readonly StyledProperty<string> GlueTabStartOffsetTextProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string>(
            nameof(GlueTabStartOffsetText), defaultValue: "0", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> GlueTabEndOffsetTextProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string>(
            nameof(GlueTabEndOffsetText), defaultValue: "0", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<Editor2DReferenceImage>> ReferenceImagesProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DReferenceImage>>(
            nameof(ReferenceImages),
            defaultValue: Array.Empty<Editor2DReferenceImage>());

    public static readonly StyledProperty<Editor2DReferenceImage?> ActiveReferenceImageProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DReferenceImage?>(nameof(ActiveReferenceImage));

    public static readonly StyledProperty<bool> ActiveReferenceImageLockedProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(nameof(ActiveReferenceImageLocked));

    public static readonly StyledProperty<bool> ReferenceCalibrationActiveProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, bool>(
            nameof(ReferenceCalibrationActive), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<Editor2DPoint>> ReferenceCalibrationPointsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DPoint>>(
            nameof(ReferenceCalibrationPoints),
            defaultValue: Array.Empty<Editor2DPoint>(),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<Editor2DCornerParameter>> CornerParametersProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DCornerParameter>>(
            nameof(CornerParameters),
            defaultValue: Array.Empty<Editor2DCornerParameter>(),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> SelectedCornerParameterIdProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string?>(
            nameof(SelectedCornerParameterId),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Editor2DFilletContinuity> FilletContinuityProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DFilletContinuity>(
            nameof(FilletContinuity),
            defaultValue: Editor2DFilletContinuity.G1);

    public static readonly StyledProperty<IReadOnlyList<Editor2DMeasurement>> MeasurementsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<Editor2DMeasurement>>(
            nameof(Measurements),
            defaultValue: Array.Empty<Editor2DMeasurement>(),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> SelectedMeasurementIdProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, string?>(
            nameof(SelectedMeasurementId),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, double>(
            nameof(Zoom),
            defaultValue: 0.0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> OffsetXProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, double>(
            nameof(OffsetX),
            defaultValue: 0.0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> OffsetYProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, double>(
            nameof(OffsetY),
            defaultValue: 0.0,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> FrameRequestTokenProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, int>(nameof(FrameRequestToken));

    public static readonly StyledProperty<int> PolygonSidesProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, int>(
            nameof(PolygonSides),
            defaultValue: 6,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> RectangleFilletRadiusProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, double>(
            nameof(RectangleFilletRadius),
            defaultValue: 0.0);

    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.Parse("#13161D")), 1);
    private static readonly Pen MajorGridPen = new(new SolidColorBrush(Color.Parse("#1D2430")), 1);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.Parse("#2D3B55")), 1.25);
    private static readonly Pen PaperBorderPen = new(new SolidColorBrush(Color.Parse("#243042")), 1);
    private static readonly Pen ClosedPathPen = new(new SolidColorBrush(Color.Parse("#E8ECF6")), 1.4);
    private static readonly Pen OpenPathPen = new(new SolidColorBrush(Color.Parse("#F5B35C")), 1.4);
    private static readonly Pen ConstructionPathPen = new(new SolidColorBrush(Color.Parse("#8A909B")), 1.2, dashStyle: new DashStyle([6, 4], 0));
    private static readonly Pen HoverPathPen = new(new SolidColorBrush(Color.Parse("#8EB3FF")), 2.0);
    private static readonly Pen SelectedPathPen = new(new SolidColorBrush(Color.Parse("#4D7FFF")), 2.4);
    private static readonly Pen RotationGizmoPen = new(new SolidColorBrush(Color.Parse("#3B82F6")), 2.0);
    private static readonly Pen TranslationXGizmoPen = new(new SolidColorBrush(Color.Parse("#EF4444")), 2.0);
    private static readonly Pen TranslationYGizmoPen = new(new SolidColorBrush(Color.Parse("#22C55E")), 2.0);
    private static readonly Pen PreviewPathPen = new(new SolidColorBrush(Color.Parse("#62E6A7")), 1.8, dashStyle: new DashStyle([4, 3], 0));
    private static readonly Pen OffsetPreviewPathPen = new(new SolidColorBrush(Color.Parse("#F59E0B")), 1.2, dashStyle: new DashStyle([4, 4], 0));
    private static readonly Pen GlueTabPreviewPathPen = new(new SolidColorBrush(Color.Parse("#A855F7")), 1.5, dashStyle: new DashStyle([4, 3], 0));
    private static readonly Pen ReferenceCalibrationPen = new(new SolidColorBrush(Color.Parse("#EF4444")), 1.5, dashStyle: new DashStyle([4, 3], 0));
    private static readonly IBrush ReferenceCalibrationBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly Pen AutoDimensionPen = new(new SolidColorBrush(Color.Parse("#63D2FF")), 1.2);
    private static readonly Pen ConstrainedRectangleHandlePen = new(new SolidColorBrush(Color.Parse("#8ED7FF")), 1.2);
    private static readonly Pen EditableVertexHandlePen = new(new SolidColorBrush(Color.Parse("#FFFFFF")), 1.0);
    private static readonly Pen MeasurementPen = new(new SolidColorBrush(Color.Parse("#7BDCB5")), 1.4);
    private static readonly Pen SelectedMeasurementPen = new(new SolidColorBrush(Color.Parse("#D6FFF1")), 2.0);
    private static readonly Pen LiveMeasurementPen = new(new SolidColorBrush(Color.Parse("#B0F5DA")), 1.2, dashStyle: new DashStyle([6, 4], 0));
    private static readonly Pen TrimPreviewPen = new(new SolidColorBrush(Color.Parse("#FF6B6B")), 3.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly Pen CornerToolHandlePen = new(new SolidColorBrush(Color.Parse("#FFFFFF")), 1.2);
    private static readonly Pen MarqueePen = new(new SolidColorBrush(Color.Parse("#6F96FF")), 1.2, dashStyle: new DashStyle([4, 4], 0));
    private static readonly Typeface MeasurementLabelTypeface = new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.Medium, FontStretch.Normal);
    private static readonly IBrush PaperFillBrush = new SolidColorBrush(Color.Parse("#11161F"));
    private static readonly IBrush FilledPathBrush = new SolidColorBrush(Color.Parse("#334D7FFF"));
    private static readonly IBrush AutoDimensionTextBrush = new SolidColorBrush(Color.Parse("#D8F5FF"));
    private static readonly IBrush AutoDimensionLabelFillBrush = new SolidColorBrush(Color.Parse("#C0121F2B"));
    private static readonly IBrush ConstrainedRectangleHandleFillBrush = new SolidColorBrush(Color.Parse("#CC10151F"));
    private static readonly IBrush EditableVertexHandleFillBrush = new SolidColorBrush(Color.Parse("#4D7FFF"));
    private static readonly IBrush RotationGizmoBrush = new SolidColorBrush(Color.Parse("#3B82F6"));
    private static readonly IBrush TranslationXGizmoBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush TranslationYGizmoBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush TranslationFreeGizmoBrush = new SolidColorBrush(Color.Parse("#FACC15"));
    private static readonly IBrush MeasurementPointBrush = new SolidColorBrush(Color.Parse("#7BDCB5"));
    private static readonly IBrush MeasurementTextBrush = new SolidColorBrush(Color.Parse("#DDF9EE"));
    private static readonly IBrush MeasurementLabelFillBrush = new SolidColorBrush(Color.Parse("#C010241D"));
    private static readonly IBrush LiveMeasurementPointBrush = new SolidColorBrush(Color.Parse("#B0F5DA"));
    private static readonly IBrush CornerToolHandleBrush = new SolidColorBrush(Color.Parse("#F5B35C"));
    private static readonly IBrush SewingHoleHandleBrush = new SolidColorBrush(Color.Parse("#C084FC"));
    private static readonly IBrush GlueTabHandleBrush = new SolidColorBrush(Color.Parse("#A855F7"));
    private static readonly IBrush MarqueeFillBrush = new SolidColorBrush(Color.Parse("#224D7FFF"));
    private static readonly Pen SnapIndicatorPen = new(new SolidColorBrush(Color.Parse("#FF9F43")), 1.5);
    private static readonly IBrush SnapIndicatorBrush = new SolidColorBrush(Color.Parse("#FF9F43"));
    private static readonly IBrush SnapLabelFillBrush = new SolidColorBrush(Color.Parse("#DD17120C"));

    private readonly ContextMenu _contextMenu;
    private readonly DxfCanvasRenderer _renderer = new();
    private readonly DxfCanvasInteractionSession _interaction = new();
    private readonly DxfCanvasInteractionController _interactionController;
    private readonly DxfCanvasToolCommitter _toolCommitter = new();
    private readonly DxfCanvasSnapResolver _snapResolver = new();
    private readonly Dictionary<string, Bitmap> _referenceImageBitmaps = new(StringComparer.Ordinal);
    private bool _isCommittingSelectionTransform;
    private double _scaleStartFactor = 1.0;
    private DxfCanvasTransformPrecisionState _transformPrecisionState = DxfCanvasTransformPrecisionState.Empty;
    private DxfCanvasTransformPrecisionKind _transformPrecisionKind;
    private string _transformPrecisionSelectionKey = string.Empty;
    private readonly MenuItem _expandRectanglesMenuItem;
    private readonly MenuItem _explodeCompoundMenuItem;
    private readonly MenuItem _duplicateSelectionMenuItem;
    private readonly MenuItem _flipHorizontalMenuItem;
    private readonly MenuItem _flipVerticalMenuItem;
    private readonly MenuItem _unionMenuItem;
    private readonly MenuItem _subtractMenuItem;
    private readonly MenuItem _intersectMenuItem;
    private readonly MenuItem _convertLinesMenuItem;
    private readonly MenuItem _reloadFromDiskMenuItem;
    private readonly MenuItem _strokeToFillMenuItem;
    private readonly MenuItem _fillToStrokeMenuItem;
    private readonly MenuItem _breakMirrorLinkMenuItem;
    private readonly MenuItem _deleteSelectionMenuItem;
    private ref bool _isMovingSelection => ref _interaction.IsMovingSelection;
    private ref bool _isScalingSelection => ref _interaction.IsScalingSelection;
    private ref bool _isRotatingSelection => ref _interaction.IsRotatingSelection;
    private ref bool _isTranslatingSelection => ref _interaction.IsTranslatingSelection;
    private ref bool _isAwaitingSecondaryContextClick => ref _interaction.IsAwaitingSecondaryContextClick;
    private ref bool _isEditingVertex => ref _interaction.IsEditingVertex;
    private ref bool _isDraggingCorner => ref _interaction.IsDraggingCorner;
    private ref bool _isDraggingSewingHoleMargin => ref _interaction.IsDraggingSewingHoleMargin;
    private ref bool _isDraggingOffsetHandle => ref _interaction.IsDraggingOffsetHandle;
    private ref DxfCanvasGlueTabHandle _glueTabDragHandle => ref _interaction.GlueTabDragHandle;
    private ref string? _cornerDragPathId => ref _interaction.CornerDragPathId;
    private ref int _cornerDragIndex => ref _interaction.CornerDragIndex;
    private ref Editor2DCornerKind _cornerDragKind => ref _interaction.CornerDragKind;
    private ref Editor2DPreviewDocument? _moveDocumentSnapshot => ref _interaction.MoveDocumentSnapshot;
    private ref Editor2DPreviewDocument? _scaleDocumentSnapshot => ref _interaction.ScaleDocumentSnapshot;
    private ref Editor2DPreviewDocument? _rotateDocumentSnapshot => ref _interaction.RotateDocumentSnapshot;
    private ref Editor2DPreviewDocument? _translateDocumentSnapshot => ref _interaction.TranslateDocumentSnapshot;
    private ref IReadOnlyList<string> _moveSelectionIds => ref _interaction.MoveSelectionIds;
    private ref IReadOnlyList<string> _scaleSelectionIds => ref _interaction.ScaleSelectionIds;
    private ref IReadOnlyList<string> _rotateSelectionIds => ref _interaction.RotateSelectionIds;
    private ref IReadOnlyList<string> _translateSelectionIds => ref _interaction.TranslateSelectionIds;
    private ref Editor2DPoint? _moveStartPoint => ref _interaction.MoveStartPoint;
    private ref Editor2DPoint _movePreviewDelta => ref _interaction.MovePreviewDelta;
    private ref Editor2DPoint? _scaleCenterPoint => ref _interaction.ScaleCenterPoint;
    private ref double _scaleStartDistance => ref _interaction.ScaleStartDistance;
    private ref double _scalePreviewFactor => ref _interaction.ScalePreviewFactor;
    private ref Editor2DPoint? _rotatePivot => ref _interaction.RotatePivot;
    private ref Point? _rotateGrabPoint => ref _interaction.RotateGrabPoint;
    private ref double _rotatePreviewDegrees => ref _interaction.RotatePreviewDegrees;
    private ref double _rotateDragDistancePixels => ref _interaction.RotateDragDistancePixels;
    private ref Editor2DPoint? _translatePivot => ref _interaction.TranslatePivot;
    private ref Point? _translateGrabPoint => ref _interaction.TranslateGrabPoint;
    private ref Editor2DPoint _translatePreviewDelta => ref _interaction.TranslatePreviewDelta;
    private ref DxfCanvasTranslationHandle _translateHandle => ref _interaction.TranslateHandle;
    private ref bool _translateCreateCopy => ref _interaction.TranslateCreateCopy;
    private ref double _translateDragDistancePixels => ref _interaction.TranslateDragDistancePixels;
    private ref string? _editingVertexPathId => ref _interaction.EditingVertexPathId;
    private ref int _editingVertexIndex => ref _interaction.EditingVertexIndex;
    private ref bool _editingVertexIsConstrainedRectangle => ref _interaction.EditingVertexIsConstrainedRectangle;
    private ref string? _editingPenPathId => ref _interaction.EditingPenPathId;
    private ref bool _editingPenClosed => ref _interaction.EditingPenClosed;
    private ref Editor2DPoint? _pendingLineStart => ref _interaction.PendingLineStart;
    private ref Editor2DPoint? _pendingLineEnd => ref _interaction.PendingLineEnd;
    private ref Editor2DPoint? _pendingRectangleStart => ref _interaction.PendingRectangleStart;
    private ref Editor2DPoint? _pendingRectangleEnd => ref _interaction.PendingRectangleEnd;
    private ref Editor2DPoint? _pendingCircleCenter => ref _interaction.PendingCircleCenter;
    private ref Editor2DPoint? _pendingCircleEdge => ref _interaction.PendingCircleEdge;
    private ref Editor2DPoint? _pendingPolygonCenter => ref _interaction.PendingPolygonCenter;
    private ref Editor2DPoint? _pendingPolygonEdge => ref _interaction.PendingPolygonEdge;
    private ref Editor2DPoint? _pendingTextStart => ref _interaction.PendingTextStart;
    private ref Editor2DPoint? _pendingTextEnd => ref _interaction.PendingTextEnd;
    private bool _isTextEntryActive;
    private string? _pendingTextInitialEntry;
    private ref IReadOnlyList<Editor2DBezierAnchor> _pendingPenAnchors => ref _interaction.PendingPenAnchors;
    private ref Editor2DPoint? _pendingPenHoverPoint => ref _interaction.PendingPenHoverPoint;
    private ref int? _pendingPenDragAnchorIndex => ref _interaction.PendingPenDragAnchorIndex;
    private ref DxfCanvasInteractionSession.PenDragControl _pendingPenDragControl => ref _interaction.PendingPenDragControl;
    private ref string? _editingMeasurementId => ref _interaction.EditingMeasurementId;
    private ref bool _editingMeasurementStart => ref _interaction.EditingMeasurementStart;
    private ref Editor2DPoint? _pendingMeasurementStart => ref _interaction.PendingMeasurementStart;
    private ref Editor2DPoint? _pendingMeasurementEnd => ref _interaction.PendingMeasurementEnd;
    private ref Editor2DPoint? _pendingDimensionStart => ref _interaction.PendingDimensionStart;
    private ref Editor2DPoint? _pendingDimensionEnd => ref _interaction.PendingDimensionEnd;
    private ref bool _pendingFrameToDocument => ref _interaction.PendingFrameToDocument;
    private ref double? _cornerToolSessionValue => ref _interaction.CornerToolSessionValue;
    private DxfCanvasSnapResult? _activeSnapResult;
    private bool _shiftSnapHeld;
    private Editor2DReferenceImage? _referenceImageDragStart;
    private Point _referenceImageDragStartPoint;
    private ReferenceImageDragMode _referenceImageDragMode;
    private double? _pinchLastScale;

    private enum ReferenceImageDragMode
    {
        Move,
        Scale,
        ScaleWidth,
        ScaleHeight,
        Rotate,
    }

    static DxfPreviewCanvas()
    {
        AffectsRender<DxfPreviewCanvas>(
            DocumentProperty,
            TextFontPreviewProperty,
            ActiveToolProperty,
            SnapEnabledProperty,
            GridVisibleProperty,
            ChainSelectionEnabledProperty,
            TwoDMoveCreateCopyProperty,
            TwoDMovePointToPointActiveProperty,
            TwoDMovePointToPointSourceProperty,
            TwoDMirrorLineModeProperty,
            TwoDMirrorFlipCopyProperty,
            TwoDMirrorAxisStartProperty,
            TwoDMirrorAxisEndProperty,
            SelectedPathIdsProperty,
            HiddenPathIdsProperty,
            PreviewPathsProperty,
            SewingHoleMarginProperty,
            OffsetDistanceTextProperty,
            OffsetSideProperty,
            OffsetPreviewPathsProperty,
            PatternPreviewPathsProperty,
            GlueTabPreviewPathsProperty,
            GlueTabStartOffsetTextProperty,
            GlueTabEndOffsetTextProperty,
            ReferenceImagesProperty,
            ActiveReferenceImageProperty,
            ActiveReferenceImageLockedProperty,
            ReferenceCalibrationActiveProperty,
            ReferenceCalibrationPointsProperty,
            CornerParametersProperty,
            MeasurementsProperty,
            SelectedMeasurementIdProperty,
            ZoomProperty,
            OffsetXProperty,
            OffsetYProperty,
            PolygonSidesProperty,
            RectangleFilletRadiusProperty);
        ClipToBoundsProperty.OverrideDefaultValue<DxfPreviewCanvas>(true);
        FocusableProperty.OverrideDefaultValue<DxfPreviewCanvas>(true);
    }

    public DxfPreviewCanvas()
    {
        _interactionController = new DxfCanvasInteractionController(_interaction);
        GestureRecognizers.Add(new ScrollGestureRecognizer
        {
            CanHorizontallyScroll = true,
            CanVerticallyScroll = true,
            IsScrollInertiaEnabled = false,
        });
        GestureRecognizers.Add(new PinchGestureRecognizer());
        ScrollGesture += OnScrollGesture;
        Pinch += OnPinch;
        PinchEnded += OnPinchEnded;
        _expandRectanglesMenuItem = new MenuItem
        {
            Header = "Expand",
            Command = new RelayCommand(ExecuteExpandRectanglesCommand),
        };

        _duplicateSelectionMenuItem = new MenuItem
        {
            Header = "Duplicate",
            Command = new RelayCommand(ExecuteDuplicateSelectionCommand),
        };
        _flipHorizontalMenuItem = new MenuItem
        {
            Header = "Flip Horizontal",
            Command = new RelayCommand(() => ExecuteFlipSelectionCommand(horizontal: true)),
        };
        _flipVerticalMenuItem = new MenuItem
        {
            Header = "Flip Vertical",
            Command = new RelayCommand(() => ExecuteFlipSelectionCommand(horizontal: false)),
        };
        _unionMenuItem = CreateBooleanMenuItem("Union", "Union");
        _subtractMenuItem = CreateBooleanMenuItem("Subtract", "Subtract");
        _intersectMenuItem = CreateBooleanMenuItem("Intersect", "Intersect");
        _convertLinesMenuItem = new MenuItem
        {
            Header = "Convert to Dashed",
            Command = new RelayCommand(ExecuteConvertToDashedCommand),
        };
        _reloadFromDiskMenuItem = new MenuItem
        {
            Header = "Reload from Disk",
            Command = new AsyncRelayCommand(ExecuteReloadFromDiskCommandAsync),
        };
        AutomationProperties.SetAutomationId(_reloadFromDiskMenuItem, "editor.canvas.2d.reload-import");

        _explodeCompoundMenuItem = new MenuItem
        {
            Header = "Explode Compound",
            Command = new RelayCommand(ExecuteExplodeCompoundCommand),
        };

        _strokeToFillMenuItem = new MenuItem
        {
            Header = "Stroke to Fill",
            Command = new RelayCommand(() => ExecuteFillConversion(toFill: true)),
        };
        _fillToStrokeMenuItem = new MenuItem
        {
            Header = "Fill to Stroke",
            Command = new RelayCommand(() => ExecuteFillConversion(toFill: false)),
        };

        _breakMirrorLinkMenuItem = new MenuItem
        {
            Header = "Break Mirror Link",
            Command = new RelayCommand(ExecuteBreakMirrorLinkCommand),
        };
        AutomationProperties.SetAutomationId(_breakMirrorLinkMenuItem, "editor.canvas.2d.break-mirror-link");

        _deleteSelectionMenuItem = new MenuItem
        {
            Header = "Delete",
            Command = new RelayCommand(ExecuteDeleteSelectionCommand),
        };

        _contextMenu = new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = new Control[]
            {
                _duplicateSelectionMenuItem,
                _flipHorizontalMenuItem,
                _flipVerticalMenuItem,
                _unionMenuItem,
                _subtractMenuItem,
                _intersectMenuItem,
                _convertLinesMenuItem,
                _reloadFromDiskMenuItem,
                _expandRectanglesMenuItem,
                _explodeCompoundMenuItem,
                _strokeToFillMenuItem,
                _fillToStrokeMenuItem,
                _breakMirrorLinkMenuItem,
                _deleteSelectionMenuItem,
            },
        };
    }

    private void ExecuteDuplicateSelectionCommand()
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.DuplicateTwoDSelection();
        _contextMenu.Close();
    }

    private void ExecuteBreakMirrorLinkCommand()
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.BreakTwoDMirrorLinks();
        _contextMenu.Close();
    }

    private void ExecuteFlipSelectionCommand(bool horizontal)
    {
        if (DataContext is EditorPageViewModel viewModel)
            viewModel.FlipTwoDSelection(horizontal);
        _contextMenu.Close();
    }

    private MenuItem CreateBooleanMenuItem(string header, string operation)
        => new()
        {
            Header = header,
            Command = new AsyncRelayCommand(() => ExecuteBooleanSelectionCommandAsync(operation)),
        };

    private async Task ExecuteBooleanSelectionCommandAsync(string operation)
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ApplyTwoDBooleanAsync(operation).ConfigureAwait(true);
        _contextMenu.Close();
    }

    private void ExecuteConvertToDashedCommand()
    {
        if (DataContext is EditorPageViewModel viewModel)
        {
            viewModel.TwoDConvertLineStyle = "dashed";
            viewModel.ApplyTwoDConvertLines();
        }
        _contextMenu.Close();
    }

    private async Task ExecuteReloadFromDiskCommandAsync()
    {
        if (DataContext is EditorPageViewModel viewModel)
            await viewModel.ReloadSelectedTwoDImportsFromDiskAsync().ConfigureAwait(true);
        _contextMenu.Close();
    }

    private void ExecuteExpandRectanglesCommand()
    {
        ExpandSelectedRectangles();
        _contextMenu.Close();
    }

    private void ExecuteExplodeCompoundCommand()
    {
        if (Document is null || SelectedPathIds.Count == 0)
        {
            _contextMenu.Close();
            return;
        }

        var selectedIds = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        var replacements = new Dictionary<string, IReadOnlyList<Editor2DPreviewPath>>(StringComparer.Ordinal);
        foreach (var path in Document.Paths.Where(path => selectedIds.Contains(path.Id)))
        {
            var loops = Editor2DGeometry.ExplodeCompoundPath(path);
            if (loops.Count > 1)
                replacements[path.Id] = loops;
        }

        if (replacements.Count > 0)
        {
            var nextPaths = new List<Editor2DPreviewPath>();
            var nextSelection = new List<string>();
            foreach (var path in Document.Paths)
            {
                if (!replacements.TryGetValue(path.Id, out var loops))
                {
                    nextPaths.Add(path);
                    if (selectedIds.Contains(path.Id))
                        nextSelection.Add(path.Id);
                    continue;
                }

                nextPaths.AddRange(loops);
                nextSelection.AddRange(loops.Select(loop => loop.Id));
            }

            SetCurrentValue(DocumentProperty, CreateUpdatedDocument(Document, nextPaths));
            SetCurrentValue(SelectedPathIdsProperty, nextSelection);
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            InvalidateVisual();
        }

        _contextMenu.Close();
    }

    private void ExecuteFillConversion(bool toFill)
    {
        if (Document is null || SelectedPathIds.Count == 0)
        {
            _contextMenu.Close();
            return;
        }

        var selectedIds = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var eligible = Document.Paths
            .Where(path => selectedIds.Contains(path.Id)
                && path.IsClosed
                && path.IsFilled != toFill)
            .ToArray();
        if (eligible.Length > 0)
        {
            var eligibleIds = eligible.Select(path => path.Id).ToHashSet(StringComparer.Ordinal);
            SetCurrentValue(DocumentProperty, CreateUpdatedDocument(Document, Document.Paths
                .Select(path => eligibleIds.Contains(path.Id) ? path with { IsFilled = toFill } : path)
                .ToArray()));
            InvalidateVisual();
        }

        _contextMenu.Close();
    }

    private void ExecuteDeleteSelectionCommand()
    {
        if (!DeleteSelectedMeasurement())
            DeleteSelectedPaths();
        _contextMenu.Close();
    }

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        if (Document is null)
            return;

        var update = _interactionController.ApplyPan(
            Zoom,
            OffsetX,
            OffsetY,
            e.Delta,
            ReversePanDirection);
        SetCurrentValue(OffsetXProperty, update.OffsetX);
        SetCurrentValue(OffsetYProperty, update.OffsetY);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (Document is null || e.Scale <= 0.0)
            return;

        var previousScale = _pinchLastScale ?? e.Scale;
        var factor = e.Scale / previousScale;
        _pinchLastScale = e.Scale;
        if (!double.IsFinite(factor) || factor <= 0.0 || Math.Abs(factor - 1.0) <= 1e-6)
            return;

        var update = _interactionController.ApplyZoom(
            e.ScaleOrigin,
            Bounds.Size,
            Zoom,
            OffsetX,
            OffsetY,
            factor);
        SetCurrentValue(ZoomProperty, update.Zoom);
        SetCurrentValue(OffsetXProperty, update.OffsetX);
        SetCurrentValue(OffsetYProperty, update.OffsetY);
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchLastScale = null;
        e.Handled = true;
    }

    private ref bool _isPanning => ref _interaction.IsPanning;
    private ref bool _isMarqueeSelecting => ref _interaction.IsMarqueeSelecting;
    private ref Point _lastPointerPosition => ref _interaction.LastPointerPosition;
    private ref Point _pointerPressPosition => ref _interaction.PointerPressPosition;
    private ref string? _hoveredPathId => ref _interaction.HoveredPathId;
    private ref string? _pressedPathId => ref _interaction.PressedPathId;
    private ref Point? _marqueeStartPoint => ref _interaction.MarqueeStartPoint;
    private ref Point? _marqueeCurrentPoint => ref _interaction.MarqueeCurrentPoint;
    private ref Point _hoverPointerPosition => ref _interaction.HoverPointerPosition;
    private ref bool _hasHoverPointerPosition => ref _interaction.HasHoverPointerPosition;
    private ref bool _cancelInteractionOnPointerRelease => ref _interaction.CancelInteractionOnPointerRelease;

    public Editor2DPreviewDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public string? TextFontPreview
    {
        get => GetValue(TextFontPreviewProperty);
        set => SetValue(TextFontPreviewProperty, value);
    }

    public string TextEntry
    {
        get => GetValue(TextEntryProperty);
        set => SetValue(TextEntryProperty, value ?? string.Empty);
    }

    public Editor2DTool ActiveTool
    {
        get => GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    public bool SnapEnabled
    {
        get => GetValue(SnapEnabledProperty);
        set => SetValue(SnapEnabledProperty, value);
    }

    public bool GridVisible
    {
        get => GetValue(GridVisibleProperty);
        set => SetValue(GridVisibleProperty, value);
    }

    public bool ChainSelectionEnabled
    {
        get => GetValue(ChainSelectionEnabledProperty);
        set => SetValue(ChainSelectionEnabledProperty, value);
    }

    public bool TwoDMoveCreateCopy
    {
        get => GetValue(TwoDMoveCreateCopyProperty);
        set => SetValue(TwoDMoveCreateCopyProperty, value);
    }

    public bool TwoDMovePointToPointActive
    {
        get => GetValue(TwoDMovePointToPointActiveProperty);
        set => SetValue(TwoDMovePointToPointActiveProperty, value);
    }

    public Editor2DPoint? TwoDMovePointToPointSource
    {
        get => GetValue(TwoDMovePointToPointSourceProperty);
        set => SetValue(TwoDMovePointToPointSourceProperty, value);
    }

    public string PatternMode
    {
        get => GetValue(PatternModeProperty);
        set => SetValue(PatternModeProperty, value);
    }

    public string? PatternGuidePathId
    {
        get => GetValue(PatternGuidePathIdProperty);
        set => SetValue(PatternGuidePathIdProperty, value);
    }

    public Editor2DPoint? PatternPivot
    {
        get => GetValue(PatternPivotProperty);
        set => SetValue(PatternPivotProperty, value);
    }

    public bool PatternPivotPicking
    {
        get => GetValue(PatternPivotPickingProperty);
        set => SetValue(PatternPivotPickingProperty, value);
    }

    public Editor2DPoint? ScalePivot
    {
        get => GetValue(ScalePivotProperty);
        set => SetValue(ScalePivotProperty, value);
    }

    public bool ScalePivotPicking
    {
        get => GetValue(ScalePivotPickingProperty);
        set => SetValue(ScalePivotPickingProperty, value);
    }

    public bool ScaleFromCenter
    {
        get => GetValue(ScaleFromCenterProperty);
        set => SetValue(ScaleFromCenterProperty, value);
    }

    public string ScaleFactorText
    {
        get => GetValue(ScaleFactorTextProperty);
        set => SetValue(ScaleFactorTextProperty, value);
    }

    public double ScaleFactor
    {
        get => GetValue(ScaleFactorProperty);
        set => SetValue(ScaleFactorProperty, value);
    }

    public bool TwoDMirrorLineMode
    {
        get => GetValue(TwoDMirrorLineModeProperty);
        set => SetValue(TwoDMirrorLineModeProperty, value);
    }

    public bool TwoDMirrorFlipCopy
    {
        get => GetValue(TwoDMirrorFlipCopyProperty);
        set => SetValue(TwoDMirrorFlipCopyProperty, value);
    }

    public Editor2DPoint? TwoDMirrorAxisStart
    {
        get => GetValue(TwoDMirrorAxisStartProperty);
        set => SetValue(TwoDMirrorAxisStartProperty, value);
    }

    public Editor2DPoint? TwoDMirrorAxisEnd
    {
        get => GetValue(TwoDMirrorAxisEndProperty);
        set => SetValue(TwoDMirrorAxisEndProperty, value);
    }

    public IReadOnlyList<string> SelectedPathIds
    {
        get => GetValue(SelectedPathIdsProperty);
        set => SetValue(SelectedPathIdsProperty, value);
    }

    public IReadOnlyList<string> HiddenPathIds
    {
        get => GetValue(HiddenPathIdsProperty);
        set => SetValue(HiddenPathIdsProperty, value);
    }

    public IReadOnlyList<Editor2DPreviewPath> PreviewPaths
    {
        get => GetValue(PreviewPathsProperty);
        set => SetValue(PreviewPathsProperty, value);
    }

    public double SewingHoleMargin
    {
        get => GetValue(SewingHoleMarginProperty);
        set => SetValue(SewingHoleMarginProperty, value);
    }

    public string OffsetDistanceText
    {
        get => GetValue(OffsetDistanceTextProperty);
        set => SetValue(OffsetDistanceTextProperty, value);
    }

    public string OffsetSide
    {
        get => GetValue(OffsetSideProperty);
        set => SetValue(OffsetSideProperty, value);
    }

    public IReadOnlyList<Editor2DPreviewPath> OffsetPreviewPaths
    {
        get => GetValue(OffsetPreviewPathsProperty);
        set => SetValue(OffsetPreviewPathsProperty, value);
    }

    public IReadOnlyList<Editor2DPreviewPath> PatternPreviewPaths
    {
        get => GetValue(PatternPreviewPathsProperty);
        set => SetValue(PatternPreviewPathsProperty, value);
    }

    public IReadOnlyList<Editor2DPreviewPath> GlueTabPreviewPaths
    {
        get => GetValue(GlueTabPreviewPathsProperty);
        set => SetValue(GlueTabPreviewPathsProperty, value);
    }

    public string GlueTabStartOffsetText
    {
        get => GetValue(GlueTabStartOffsetTextProperty);
        set => SetValue(GlueTabStartOffsetTextProperty, value);
    }

    public string GlueTabEndOffsetText
    {
        get => GetValue(GlueTabEndOffsetTextProperty);
        set => SetValue(GlueTabEndOffsetTextProperty, value);
    }

    public IReadOnlyList<Editor2DReferenceImage> ReferenceImages
    {
        get => GetValue(ReferenceImagesProperty);
        set => SetValue(ReferenceImagesProperty, value ?? Array.Empty<Editor2DReferenceImage>());
    }

    public Editor2DReferenceImage? ActiveReferenceImage
    {
        get => GetValue(ActiveReferenceImageProperty);
        set => SetValue(ActiveReferenceImageProperty, value);
    }

    public bool ActiveReferenceImageLocked
    {
        get => GetValue(ActiveReferenceImageLockedProperty);
        set => SetValue(ActiveReferenceImageLockedProperty, value);
    }

    public bool ReferenceCalibrationActive
    {
        get => GetValue(ReferenceCalibrationActiveProperty);
        set => SetValue(ReferenceCalibrationActiveProperty, value);
    }

    public IReadOnlyList<Editor2DPoint> ReferenceCalibrationPoints
    {
        get => GetValue(ReferenceCalibrationPointsProperty);
        set => SetValue(ReferenceCalibrationPointsProperty, value ?? Array.Empty<Editor2DPoint>());
    }

    public event Action<string, double, double, double, double, double>? ReferenceImageTransformChanged;
    internal event Action<DxfCanvasTransformPrecisionRequest>? TransformPrecisionRequested;
    internal event Action? TransformPrecisionDismissed;
    internal event Action<DxfCanvasSelectionTransformEventArgs>? SelectionTransformRequested;
    internal event Action<DxfCanvasPathReplacementEventArgs>? PathReplacementRequested;
    internal event Action<DxfCanvasReferenceCalibrationRequest>? ReferenceCalibrationRequested;

    public IReadOnlyList<Editor2DCornerParameter> CornerParameters
    {
        get => GetValue(CornerParametersProperty);
        set => SetValue(CornerParametersProperty, value);
    }

    public string? SelectedCornerParameterId
    {
        get => GetValue(SelectedCornerParameterIdProperty);
        set => SetValue(SelectedCornerParameterIdProperty, value);
    }

    public Editor2DFilletContinuity FilletContinuity
    {
        get => GetValue(FilletContinuityProperty);
        set => SetValue(FilletContinuityProperty, value);
    }

    public IReadOnlyList<Editor2DMeasurement> Measurements
    {
        get => GetValue(MeasurementsProperty);
        set => SetValue(MeasurementsProperty, value);
    }

    public string? SelectedMeasurementId
    {
        get => GetValue(SelectedMeasurementIdProperty);
        set => SetValue(SelectedMeasurementIdProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double OffsetX
    {
        get => GetValue(OffsetXProperty);
        set => SetValue(OffsetXProperty, value);
    }

    public double OffsetY
    {
        get => GetValue(OffsetYProperty);
        set => SetValue(OffsetYProperty, value);
    }

    public int FrameRequestToken
    {
        get => GetValue(FrameRequestTokenProperty);
        set => SetValue(FrameRequestTokenProperty, value);
    }

    public int PolygonSides
    {
        get => GetValue(PolygonSidesProperty);
        set => SetValue(PolygonSidesProperty, value);
    }

    public double RectangleFilletRadius
    {
        get => GetValue(RectangleFilletRadiusProperty);
        set => SetValue(RectangleFilletRadiusProperty, value);
    }

    public void FrameToDocument()
    {
        if (Document is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            _pendingFrameToDocument = true;
            return;
        }

        var availableWidth = Math.Max(Bounds.Width - 48.0, 32.0);
        var availableHeight = Math.Max(Bounds.Height - 48.0, 32.0);
        if (Document.Paths.Count == 0)
        {
            if (ReferenceImages.Count > 0)
            {
                var referenceBounds = MeasureReferenceImageBounds(ReferenceImages);
                var referenceScale = Math.Min(
                    availableWidth / Math.Max(referenceBounds.Width, 1.0),
                    availableHeight / Math.Max(referenceBounds.Height, 1.0));
                SetCurrentValue(ZoomProperty, referenceScale);
                SetCurrentValue(OffsetXProperty, -referenceBounds.CenterX * referenceScale);
                SetCurrentValue(OffsetYProperty, referenceBounds.CenterY * referenceScale);
                _pendingFrameToDocument = false;
                InvalidateVisual();
                return;
            }

            var emptyScale = Math.Min(availableWidth / EmptyWorkspaceSpan, availableHeight / EmptyWorkspaceSpan);
            if (!double.IsFinite(emptyScale) || emptyScale <= 0.0)
                emptyScale = 1.0;

            SetCurrentValue(ZoomProperty, emptyScale);
            SetCurrentValue(OffsetXProperty, 0.0);
            SetCurrentValue(OffsetYProperty, 0.0);
            _pendingFrameToDocument = false;
            InvalidateVisual();
            return;
        }

        var documentBounds = Document.Bounds;
        var width = Math.Max(documentBounds.Width, 1.0);
        var height = Math.Max(documentBounds.Height, 1.0);
        var scale = Math.Min(availableWidth / width, availableHeight / height);
        if (!double.IsFinite(scale) || scale <= 0.0)
            scale = 1.0;

        SetCurrentValue(ZoomProperty, scale);
        SetCurrentValue(OffsetXProperty, -documentBounds.CenterX * scale);
        SetCurrentValue(OffsetYProperty, documentBounds.CenterY * scale);
        _pendingFrameToDocument = false;
        InvalidateVisual();
    }

    public void CancelActiveInteraction()
    {
        DismissTransformPrecisionInput();
        _contextMenu.Close();
        _cancelInteractionOnPointerRelease = true;
        _isAwaitingSecondaryContextClick = false;
        _isEditingVertex = false;
        _isDraggingCorner = false;
        _isDraggingSewingHoleMargin = false;
        _isDraggingOffsetHandle = false;
        _glueTabDragHandle = DxfCanvasGlueTabHandle.None;
        _cornerDragPathId = null;
        _cornerDragIndex = 0;
        _isPanning = false;
        _isMovingSelection = false;
        _isScalingSelection = false;
        _isRotatingSelection = false;
        _isTranslatingSelection = false;
        _moveDocumentSnapshot = null;
        _scaleDocumentSnapshot = null;
        _rotateDocumentSnapshot = null;
        _translateDocumentSnapshot = null;
        _moveSelectionIds = Array.Empty<string>();
        _scaleSelectionIds = Array.Empty<string>();
        _rotateSelectionIds = Array.Empty<string>();
        _translateSelectionIds = Array.Empty<string>();
        _moveStartPoint = null;
        _movePreviewDelta = new Editor2DPoint(0, 0);
        _scaleCenterPoint = null;
        _scaleStartDistance = 0.0;
        _scaleStartFactor = 1.0;
        _scalePreviewFactor = 1.0;
        _rotatePivot = null;
        _rotateGrabPoint = null;
        _rotatePreviewDegrees = 0.0;
        _rotateDragDistancePixels = 0.0;
        _translatePivot = null;
        _translateGrabPoint = null;
        _translatePreviewDelta = new Editor2DPoint(0, 0);
        _translateHandle = DxfCanvasTranslationHandle.None;
        _translateCreateCopy = false;
        _translateDragDistancePixels = 0.0;
        _editingVertexPathId = null;
        _editingVertexIndex = 0;
        _editingVertexIsConstrainedRectangle = false;
        _referenceImageDragStart = null;
        _referenceImageDragMode = default;
        _editingMeasurementId = null;
        _editingMeasurementStart = false;
        _pressedPathId = null;
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        SetCurrentValue(TwoDMovePointToPointActiveProperty, false);
        SetCurrentValue(TwoDMovePointToPointSourceProperty, null);
        CancelMarqueeSelection();
        CancelPendingLine();
        CancelPendingRectangle();
        CancelPendingCircle();
        CancelPendingPolygon();
        CancelPendingText();
        CancelPendingPen();
        CancelPendingMirror();
        CancelPendingMeasurement();
        CancelPendingDimension();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedPathIdsProperty)
            HandleTransformPrecisionSelectionChanged();
        else if (change.Property == ActiveToolProperty || change.Property == ActiveReferenceImageProperty)
            DismissTransformPrecisionInput();
        else if (change.Property == DocumentProperty && !_isCommittingSelectionTransform)
            DismissTransformPrecisionInput();

        if (!_isCommittingSelectionTransform
            && (change.Property == DocumentProperty
                || change.Property == SelectedPathIdsProperty
                || change.Property == ActiveReferenceImageProperty
                || change.Property == ZoomProperty
                || change.Property == OffsetXProperty
                || change.Property == OffsetYProperty)
            && (_isRotatingSelection || _isTranslatingSelection))
        {
            _cancelInteractionOnPointerRelease = true;
            ResetRotateSelection();
            ResetTranslateSelection();
        }

        if (change.Property == DocumentProperty)
        {
            _activeSnapResult = null;
            _hoveredPathId = null;
            _pressedPathId = null;
            CancelMarqueeSelection();
            CancelPendingLine();
            CancelPendingRectangle();
            CancelPendingCircle();
            CancelPendingPolygon();
            CancelPendingText();
            CancelPendingPen();
            CancelPendingMirror();
            CancelPendingMeasurement();
            CancelPendingDimension();

            if (Document is null)
            {
                _pendingFrameToDocument = false;
                return;
            }

            if (Zoom <= 0.0)
                FrameToDocument();

            InvalidateVisual();
            return;
        }

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.Measure)
            CancelPendingMeasurement();

        if (change.Property == ActiveToolProperty)
        {
            if (_isRotatingSelection || _isTranslatingSelection)
            {
                _cancelInteractionOnPointerRelease = true;
                ResetRotateSelection();
                ResetTranslateSelection();
            }
            _cornerToolSessionValue = null;
            if (ActiveTool == Editor2DTool.Move)
                SetCurrentValue(TwoDMoveCreateCopyProperty, false);
        }

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.Dimension)
            CancelPendingDimension();

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.SketchLine)
            CancelPendingLine();

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.SketchRectangle)
            CancelPendingRectangle();

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.SketchCircle)
            CancelPendingCircle();

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.SketchPolygon)
            CancelPendingPolygon();

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.SketchText)
            CancelPendingText();

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.Pen)
            CancelPendingPen();

        if (change.Property == ActiveToolProperty && ActiveTool != Editor2DTool.Mirror)
            CancelPendingMirror();

        if (change.Property == FrameRequestTokenProperty)
            FrameToDocument();

        if (change.Property == SnapEnabledProperty)
        {
            _activeSnapResult = null;
            InvalidateVisual();
        }

        if (change.Property == ScaleFactorTextProperty
            || change.Property == ScaleFactorProperty
            || change.Property == ScalePivotProperty
            || change.Property == ScaleFromCenterProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (_pendingFrameToDocument && Document is not null)
            FrameToDocument();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var size = Bounds.Size;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#0D0D10")), new Rect(size));

        if (Document is null || Zoom <= 0.0)
            return;

        if (GridVisible)
            DrawGrid(context, size);
        DrawReferenceImages(context, size);
        DrawReferenceImageGizmo(context, size);
        var visiblePaths = GetVisiblePaths();
        if (_isMovingSelection
            && (Math.Abs(_movePreviewDelta.X) > 1e-12 || Math.Abs(_movePreviewDelta.Y) > 1e-12))
        {
            var movedPaths = DxfCanvasGeometryEditor.Translate(
                Document with { Paths = visiblePaths },
                _moveSelectionIds,
                _movePreviewDelta.X,
                _movePreviewDelta.Y).Paths;
            visiblePaths = _interaction.MoveSelectionCreateCopy
                ? [
                    .. visiblePaths,
                    .. movedPaths.Where(path => _moveSelectionIds.Contains(path.Id, StringComparer.Ordinal)),
                ]
                : movedPaths;
        }
        if (_isTranslatingSelection
            && (Math.Abs(_translatePreviewDelta.X) > 1e-12 || Math.Abs(_translatePreviewDelta.Y) > 1e-12))
        {
            var translatedPaths = DxfCanvasGeometryEditor.Translate(
                Document with { Paths = visiblePaths },
                _translateSelectionIds,
                _translatePreviewDelta.X,
                _translatePreviewDelta.Y).Paths;
            visiblePaths = _translateCreateCopy
                ? [
                    .. visiblePaths,
                    .. translatedPaths.Where(path => _translateSelectionIds.Contains(path.Id, StringComparer.Ordinal)),
                ]
                : translatedPaths;
        }
        if (_isRotatingSelection && _rotatePivot is not null && Math.Abs(_rotatePreviewDegrees) > 1e-12)
        {
            visiblePaths = DxfCanvasGeometryEditor.Rotate(
                Document with { Paths = visiblePaths },
                _rotateSelectionIds,
                _rotatePivot,
                -_rotatePreviewDegrees).Paths;
        }
        var scalePreviewFactor = GetScalePreviewFactor();
        if (ActiveTool == Editor2DTool.Scale && Math.Abs(scalePreviewFactor - 1.0) > 1e-12)
        {
            var pivot = GetSelectionScalePivot(Document.Paths);
            if (pivot is not null)
                visiblePaths = ScalePaths(Document with { Paths = visiblePaths }, SelectedPathIds, pivot, scalePreviewFactor).Paths;
        }
        if (visiblePaths.Count > 0)
            DrawPaperBounds(context, size, Document.Bounds);
        DrawPaths(context, size, visiblePaths);
        DrawTranslationGizmo(context, size);
        DrawRotationGizmo(context, size);
        DrawPatternPivot(context, size);
        DrawScalePivot(context, size);
        DrawPreviewPaths(context, size);
        DrawPreviewPaths(context, size, OffsetPreviewPaths, OffsetPreviewPathPen);
        DrawPreviewPaths(context, size, PatternPreviewPaths);
        DrawPreviewPaths(context, size, GlueTabPreviewPaths, GlueTabPreviewPathPen);
        DrawEditableVertexHandles(context, size, visiblePaths);
        DrawConstrainedRectangleHandles(context, size, visiblePaths);
        DrawCornerToolHandles(context, size, visiblePaths);
        DrawSewingHoleMarginHandle(context, size, visiblePaths);
        DrawOffsetHandle(context, size, visiblePaths);
        DrawGlueTabHandles(context, size);
        DrawLiveSketchLine(context, size);
        DrawLiveSketchRectangle(context, size);
        DrawLiveSketchCircle(context, size);
        DrawLiveSketchPolygon(context, size);
        DrawLiveSketchText(context, size);
        DrawLivePenPath(context, size);
        DrawScaleGizmo(context, size, visiblePaths);
        DrawLiveMirrorAxis(context, size);
        DrawTrimPreview(context, size, Document with { Paths = visiblePaths });
        DrawMeasurements(context, size);
        DrawLiveMeasurement(context, size);
        DrawReferenceCalibration(context, size);
        DrawSnapIndicator(context);
        DrawMarquee(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetCurrentPoint(this);
        UpdateShiftSnapModifier(e.KeyModifiers);
        _lastPointerPosition = point.Position;
        _pointerPressPosition = point.Position;

        if (point.Properties.IsMiddleButtonPressed
            || (point.Properties.IsLeftButtonPressed && ActiveTool == Editor2DTool.Pan))
        {
            _contextMenu.Close();
            _interactionController.BeginPan(point.Position);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            _contextMenu.Close();
            _cancelInteractionOnPointerRelease = false;
            _isAwaitingSecondaryContextClick = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || Document is null)
            return;

        if (ReferenceCalibrationActive)
        {
            var worldPoint = ResolvePlacementPoint(point.Position);
            if (ReferenceCalibrationPoints.Count == 1
                && DxfCanvasReferenceCalibration.Distance(ReferenceCalibrationPoints[0], worldPoint) <= 1e-6)
            {
                e.Handled = true;
                return;
            }
            var points = ReferenceCalibrationPoints.Take(1).Append(worldPoint).ToArray();
            SetCurrentValue(ReferenceCalibrationPointsProperty, points);
            if (points.Length == 2)
            {
                SetCurrentValue(ReferenceCalibrationActiveProperty, false);
                var midpoint = DxfCanvasReferenceCalibration.Midpoint(points[0], points[1]);
                ReferenceCalibrationRequested?.Invoke(new DxfCanvasReferenceCalibrationRequest(
                    points[0], points[1], WorldToScreen(midpoint, Bounds.Size),
                    DxfCanvasReferenceCalibration.Distance(points[0], points[1])));
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Select
            && TryHitManualMeasurementEndpoint(point.Position, out var measurementId, out var editingStart))
        {
            _editingMeasurementId = measurementId;
            _editingMeasurementStart = editingStart;
            SetCurrentValue(SelectedMeasurementIdProperty, measurementId);
            SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Patterning
            && string.Equals(PatternMode, "Circular", StringComparison.Ordinal)
            && PatternPivotPicking)
        {
            SetCurrentValue(PatternPivotProperty, ScreenToWorld(point.Position));
            SetCurrentValue(PatternPivotPickingProperty, false);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Scale && ScalePivotPicking)
        {
            SetCurrentValue(ScalePivotProperty, ScreenToWorld(point.Position));
            SetCurrentValue(ScalePivotPickingProperty, false);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Patterning
            && string.Equals(PatternMode, "Path", StringComparison.Ordinal)
            && PatternGuidePathId is null
            && SelectedPathIds.Count > 0)
        {
            var guidePathId = HitTestPathId(point.Position);
            if (guidePathId is not null && !SelectedPathIds.Contains(guidePathId, StringComparer.Ordinal))
            {
                SetCurrentValue(PatternGuidePathIdProperty, guidePathId);
                SetCurrentValue(SelectedMeasurementIdProperty, null);
                e.Handled = true;
                return;
            }
        }

        if (ActiveTool == Editor2DTool.Select
            && !ActiveReferenceImageLocked
            && ActiveReferenceImage is { } activeImage
            && TryHitReferenceImageGizmo(point.Position, activeImage, out var dragMode))
        {
            _referenceImageDragStart = activeImage;
            _referenceImageDragMode = dragMode;
            _referenceImageDragStartPoint = point.Position;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (TryBeginRotateSelection(point.Position, e.Pointer))
        {
            e.Handled = true;
            return;
        }

        if (TryBeginTranslateSelection(point.Position, e.Pointer))
        {
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Select
            || ActiveTool == Editor2DTool.Scale
            || ActiveTool == Editor2DTool.Mirror
            || ActiveTool == Editor2DTool.ConvertLines
            || ActiveTool == Editor2DTool.Offset
            || ActiveTool == Editor2DTool.AddThickness
            || ActiveTool == Editor2DTool.Cleanup
            || ActiveTool == Editor2DTool.Patterning
            || ActiveTool == Editor2DTool.PaperFolding
            || ActiveTool == Editor2DTool.AddSewingHoles)
        {
            if (ActiveTool == Editor2DTool.Scale && TryBeginScaleSelection(point.Position, e.Pointer))
            {
                e.Handled = true;
                return;
            }

            if (ActiveTool == Editor2DTool.AddSewingHoles && TryBeginSewingHoleMarginDrag(point.Position, e.Pointer))
            {
                e.Handled = true;
                return;
            }

            if (ActiveTool == Editor2DTool.Offset && TryBeginOffsetHandleDrag(point.Position, e.Pointer))
            {
                e.Handled = true;
                return;
            }

            if (ActiveTool == Editor2DTool.PaperFolding && TryBeginGlueTabHandleDrag(point.Position, e.Pointer))
            {
                e.Handled = true;
                return;
            }

            if (ActiveTool == Editor2DTool.Mirror)
            {
                _cancelInteractionOnPointerRelease = false;
                HandleMirrorClick(point.Position, e.KeyModifiers);
                e.Handled = true;
                return;
            }

            if (ActiveTool == Editor2DTool.Select && TryBeginVertexEdit(point.Position, e.Pointer))
            {
                e.Handled = true;
                return;
            }

            var hitManualMeasurementId = HitTestManualMeasurementId(point.Position);
            if (!string.IsNullOrWhiteSpace(hitManualMeasurementId))
            {
                SetCurrentValue(SelectedMeasurementIdProperty, hitManualMeasurementId);
                SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
                e.Handled = true;
                return;
            }
        }

        var pressRoute = _interactionController.RoutePrimaryPress(ActiveTool);
        if (pressRoute is not DxfCanvasPressRoute.Selection)
        {
            _cancelInteractionOnPointerRelease = false;
            switch (pressRoute)
            {
                case DxfCanvasPressRoute.Move:
                    if (TwoDMovePointToPointActive)
                        HandleMovePointToPointClick(point.Position);
                    else
                        StartMoveSelection(point.Position, e.KeyModifiers, e.Pointer);
                    break;
                case DxfCanvasPressRoute.Measure: HandleMeasurementClick(point.Position); break;
                case DxfCanvasPressRoute.Dimension: HandleDimensionClick(point.Position); break;
                case DxfCanvasPressRoute.Trim: HandleTrimClick(point.Position); break;
                case DxfCanvasPressRoute.Corner: HandleCornerToolClick(point.Position, ActiveTool == Editor2DTool.Chamfer ? "chamfer" : "fillet", e.Pointer); break;
                case DxfCanvasPressRoute.SketchLine: HandleSketchLineClick(point.Position); break;
                case DxfCanvasPressRoute.SketchRectangle: HandleSketchRectangleClick(point.Position); break;
                case DxfCanvasPressRoute.SketchCircle: HandleSketchCircleClick(point.Position); break;
                case DxfCanvasPressRoute.SketchPolygon: HandleSketchPolygonClick(point.Position); break;
                case DxfCanvasPressRoute.SketchText: HandleSketchTextClick(point.Position); break;
                case DxfCanvasPressRoute.Pen: HandlePenPress(point.Position, e.Pointer); break;
            }
            e.Handled = pressRoute is not DxfCanvasPressRoute.None;
            return;
        }

        _pressedPathId = HitTestPathId(point.Position);
        _marqueeStartPoint = point.Position;
        _marqueeCurrentPoint = point.Position;
        _isMarqueeSelecting = false;
        _cancelInteractionOnPointerRelease = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var position = e.GetPosition(this);
        UpdateShiftSnapModifier(e.KeyModifiers);
        _hoverPointerPosition = position;
        _hasHoverPointerPosition = true;
        if (_editingMeasurementId is { } editingMeasurementId)
        {
            var measurement = Measurements.FirstOrDefault(item => item.Id == editingMeasurementId && !item.IsAutoDimension);
            if (measurement is not null)
            {
                var next = DxfCanvasMeasurementEditing.MoveEndpoint(
                    measurement,
                    ScreenToWorld(position),
                    _editingMeasurementStart);
                SetCurrentValue(
                    MeasurementsProperty,
                    Measurements.Select(item => item.Id == editingMeasurementId ? next : item).ToArray());
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
        if (_referenceImageDragStart is { } image)
        {
            var currentWorld = ScreenToWorld(position);
            var x = image.X;
            var y = image.Y;
            var width = image.Width;
            var height = image.Height;
            var rotation = image.RotationDegrees;
            switch (_referenceImageDragMode)
            {
                case ReferenceImageDragMode.Move:
                    var startWorld = ScreenToWorld(_referenceImageDragStartPoint);
                    x += currentWorld.X - startWorld.X;
                    y += currentWorld.Y - startWorld.Y;
                    break;
                case ReferenceImageDragMode.Scale:
                    var local = ReferenceImageLocalPoint(position, image);
                    var widthFactor = Math.Abs(local.X) / Math.Max(image.Width / 2.0, 0.0001);
                    var heightFactor = Math.Abs(local.Y) / Math.Max(image.Height / 2.0, 0.0001);
                    var factor = Math.Max(0.01, Math.Max(widthFactor, heightFactor));
                    width *= factor;
                    height *= factor;
                    break;
                case ReferenceImageDragMode.ScaleWidth:
                    var edgeWidthFactor = Math.Max(0.01, Math.Abs(ReferenceImageLocalPoint(position, image).X) / Math.Max(image.Width / 2.0, 0.0001));
                    width *= edgeWidthFactor;
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        height *= edgeWidthFactor;
                    break;
                case ReferenceImageDragMode.ScaleHeight:
                    var edgeHeightFactor = Math.Max(0.01, Math.Abs(ReferenceImageLocalPoint(position, image).Y) / Math.Max(image.Height / 2.0, 0.0001));
                    height *= edgeHeightFactor;
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                        width *= edgeHeightFactor;
                    break;
                case ReferenceImageDragMode.Rotate:
                    rotation = Math.Atan2(currentWorld.Y - image.Y, currentWorld.X - image.X) * 180.0 / Math.PI - 90.0;
                    break;
            }
            ReferenceImageTransformChanged?.Invoke(
                image.Id,
                x, y, width, height, rotation);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (_isAwaitingSecondaryContextClick)
        {
            var secondaryDelta = position - _pointerPressPosition;
            if (Math.Abs(secondaryDelta.X) > PointerDragThreshold || Math.Abs(secondaryDelta.Y) > PointerDragThreshold)
            {
                _isAwaitingSecondaryContextClick = false;
                _isPanning = true;
            }
        }

        var moveRoute = _interactionController.RouteMove(ActiveTool, e.Pointer.Captured == this);
        switch (moveRoute)
        {
            case DxfCanvasMoveRoute.Pan:
                var pan = _interactionController.ContinuePan(position);
                SetCurrentValue(OffsetXProperty, OffsetX + pan.X); SetCurrentValue(OffsetYProperty, OffsetY + pan.Y); e.Handled = true; return;
            case DxfCanvasMoveRoute.MoveSelection: ApplyMoveSelection(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.ScaleSelection: ApplyScaleSelection(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.RotateSelection: ApplyRotateSelection(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.TranslateSelection: ApplyTranslateSelection(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.Corner: ApplyCornerDrag(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.SewingHoleMargin: ApplySewingHoleMarginDrag(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.OffsetHandle: ApplyOffsetHandleDrag(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.GlueTabHandle: ApplyGlueTabHandleDrag(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.EditVertex:
                if (!_editingVertexIsConstrainedRectangle) ApplyVertexEdit(position); e.Handled = true; return;
            case DxfCanvasMoveRoute.LineDraft: _pendingLineEnd = ResolvePlacementPoint(position, _pendingLineStart, allowOrthogonal: true); break;
            case DxfCanvasMoveRoute.RectangleDraft: _pendingRectangleEnd = ResolvePlacementPoint(position); break;
            case DxfCanvasMoveRoute.CircleDraft: _pendingCircleEdge = ResolvePlacementPoint(position); break;
            case DxfCanvasMoveRoute.PolygonDraft: _pendingPolygonEdge = ResolvePlacementPoint(position); break;
            case DxfCanvasMoveRoute.TextDraft:
                if (!_isTextEntryActive) _pendingTextEnd = ResolvePlacementPoint(position);
                break;
            case DxfCanvasMoveRoute.PenHandleDrag: UpdatePendingPenHandle(position); e.Handled = true; break;
            case DxfCanvasMoveRoute.PenDraft: _pendingPenHoverPoint = ResolvePlacementPoint(position, _pendingPenAnchors.LastOrDefault()?.Point, allowOrthogonal: _pendingPenAnchors.Count > 0); break;
            case DxfCanvasMoveRoute.MeasurementDraft: _pendingMeasurementEnd = ResolvePlacementPoint(position, _pendingMeasurementStart, allowOrthogonal: true); break;
            case DxfCanvasMoveRoute.DimensionDraft: _pendingDimensionEnd = ResolvePlacementPoint(position, _pendingDimensionStart, allowOrthogonal: true); break;
            case DxfCanvasMoveRoute.ToolPreview: InvalidateVisual(); return;
            case DxfCanvasMoveRoute.Marquee:
                var drag = position - _pointerPressPosition;
                if (!_isMarqueeSelecting && (Math.Abs(drag.X) > PointerDragThreshold || Math.Abs(drag.Y) > PointerDragThreshold)) _isMarqueeSelecting = true;
                if (!_isMarqueeSelecting) break;
                _marqueeCurrentPoint = position;
                InvalidateVisual();
                e.Handled = true;
                return;
            case DxfCanvasMoveRoute.Hover: goto Hover;
        }
        InvalidateVisual();
        return;

Hover:
        UpdateSnapHover(position);
        var hoveredPathId = HitTestPathId(position);
        if (!string.Equals(_hoveredPathId, hoveredPathId, StringComparison.Ordinal))
        {
            _hoveredPathId = hoveredPathId;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_referenceImageDragStart is not null)
        {
            _referenceImageDragStart = null;
            _referenceImageDragMode = default;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_editingMeasurementId is not null)
        {
            _editingMeasurementId = null;
            _editingMeasurementStart = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        switch (_interactionController.RouteRelease(ActiveTool, e.Pointer.Captured == this))
        {
            case DxfCanvasReleaseRoute.Cancel:
                _cancelInteractionOnPointerRelease = false; _pressedPathId = null; CancelMarqueeSelection(); break;
            case DxfCanvasReleaseRoute.Pan:
                _interactionController.EndPan(); e.Pointer.Capture(null); e.Handled = true; return;
            case DxfCanvasReleaseRoute.Context:
                _isAwaitingSecondaryContextClick = false; e.Pointer.Capture(null); HandleContextClick(e.GetPosition(this)); e.Handled = true; return;
            case DxfCanvasReleaseRoute.MoveSelection:
                ApplyMoveSelection(e.GetPosition(this));
                if (Math.Abs(_movePreviewDelta.X) > 1e-12 || Math.Abs(_movePreviewDelta.Y) > 1e-12)
                {
                    RequestSelectionTransform(
                        Editor2DAffineTransform.CreateTranslation(_movePreviewDelta.X, _movePreviewDelta.Y),
                        _interaction.MoveSelectionCreateCopy);
                }
                _isMovingSelection = false; _moveDocumentSnapshot = null; _moveSelectionIds = Array.Empty<string>(); _moveStartPoint = null; _movePreviewDelta = new Editor2DPoint(0, 0); break;
            case DxfCanvasReleaseRoute.ScaleSelection:
                _isScalingSelection = false; _scaleDocumentSnapshot = null; _scaleSelectionIds = Array.Empty<string>(); _scaleCenterPoint = null; _scaleStartDistance = 0; _scaleStartFactor = 1; _scalePreviewFactor = 1; break;
            case DxfCanvasReleaseRoute.RotateSelection:
                ApplyRotateSelection(e.GetPosition(this));
                var rotationCommitted = CommitRotateSelection();
                if (rotationCommitted)
                {
                    _transformPrecisionState = _transformPrecisionState.RecordRotationDrag(-_rotatePreviewDegrees);
                    ShowTransformPrecision(
                        DxfCanvasTransformPrecisionKind.Rotation,
                        DxfCanvasTransformPrecisionState.FormatRotation(_transformPrecisionState.CumulativeRotation),
                        _rotatePivot,
                        focus: true);
                }
                else
                {
                    DismissTransformPrecisionInput();
                }
                ResetRotateSelection();
                break;
            case DxfCanvasReleaseRoute.TranslateSelection:
                ApplyTranslateSelection(e.GetPosition(this));
                var translationCommitted = CommitTranslateSelection();
                if (translationCommitted
                    && (_translateHandle is DxfCanvasTranslationHandle.X or DxfCanvasTranslationHandle.Y))
                {
                    var axis = _translateHandle == DxfCanvasTranslationHandle.X
                        ? DxfCanvasPrecisionAxis.X
                        : DxfCanvasPrecisionAxis.Y;
                    var applied = axis == DxfCanvasPrecisionAxis.X
                        ? _translatePreviewDelta.X
                        : _translatePreviewDelta.Y;
                    _transformPrecisionState = _transformPrecisionState.RecordTranslationDrag(axis, applied);
                    ShowTransformPrecision(
                        axis == DxfCanvasPrecisionAxis.X
                            ? DxfCanvasTransformPrecisionKind.X
                            : DxfCanvasTransformPrecisionKind.Y,
                        DxfCanvasTransformPrecisionState.FormatTranslation(applied),
                        GetCurrentSelectionTransformPivot(),
                        focus: true);
                }
                else if (!translationCommitted)
                {
                    DismissTransformPrecisionInput();
                }
                ResetTranslateSelection();
                break;
            case DxfCanvasReleaseRoute.Corner:
                _isDraggingCorner = false; _cornerDragPathId = null; _cornerDragIndex = 0; break;
            case DxfCanvasReleaseRoute.SewingHoleMargin:
                _isDraggingSewingHoleMargin = false; break;
            case DxfCanvasReleaseRoute.OffsetHandle:
                _isDraggingOffsetHandle = false; break;
            case DxfCanvasReleaseRoute.GlueTabHandle:
                _glueTabDragHandle = DxfCanvasGlueTabHandle.None; break;
            case DxfCanvasReleaseRoute.EditVertex:
                _isEditingVertex = false; _editingVertexPathId = null; _editingVertexIndex = 0; _editingVertexIsConstrainedRectangle = false; break;
            case DxfCanvasReleaseRoute.PenHandleDrag:
                _pendingPenDragAnchorIndex = null; break;
            case DxfCanvasReleaseRoute.None: return;
            case DxfCanvasReleaseRoute.Selection: goto Selection;
        }
        e.Pointer.Capture(null); InvalidateVisual(); e.Handled = true; return;

Selection:
        var isShiftSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (_isMarqueeSelecting && _marqueeStartPoint is not null && _marqueeCurrentPoint is not null)
        {
            ApplyMarqueeSelection(GetSelectionRect(_marqueeStartPoint.Value, _marqueeCurrentPoint.Value), isShiftSelection);
        }
        else
        {
            ApplyClickSelection(_pressedPathId, isShiftSelection);
        }

        e.Pointer.Capture(null);
        CancelMarqueeSelection();
        _pressedPathId = null;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!_isRotatingSelection && !_isTranslatingSelection)
            return;

        DismissTransformPrecisionInput();
        ResetRotateSelection();
        ResetTranslateSelection();
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        _hasHoverPointerPosition = false;
        _activeSnapResult = null;

        if (_hoveredPathId is not null)
            _hoveredPathId = null;

        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (Document is null)
            return;

        if (_isRotatingSelection || _isTranslatingSelection)
        {
            CancelActiveInteraction();
            e.Handled = true;
            return;
        }

        var screenPoint = e.GetPosition(this);
        var wheelDelta = ReversePanDirection ? -e.Delta.Y : e.Delta.Y;
        var update = _interactionController.ApplyWheel(
            screenPoint, Bounds.Size, Zoom, OffsetX, OffsetY, wheelDelta);
        SetCurrentValue(ZoomProperty, update.Zoom);
        SetCurrentValue(OffsetXProperty, update.OffsetX);
        SetCurrentValue(OffsetYProperty, update.OffsetY);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);

        if (Document is null)
            return;

        if (ActiveTool == Editor2DTool.Pen)
        {
            CommitPendingPenPath(_editingPenPathId is not null && _editingPenClosed);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Select
            && TryBeginTextEditing(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        if (ActiveTool != Editor2DTool.Select
            || !TryBeginPenEdit(e.GetPosition(this)))
            return;

        e.Handled = true;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            _shiftSnapHeld = true;
            UpdateSnapHover(_hoverPointerPosition);
            InvalidateVisual();
            return;
        }

        if (e.Key == Key.N && e.KeyModifiers is KeyModifiers.None)
        {
            SetCurrentValue(SnapEnabledProperty, !SnapEnabled);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (ReferenceCalibrationActive)
            {
                if (DataContext is EditorPageViewModel calibrationViewModel)
                    calibrationViewModel.CancelTwoDReferencePointCalibration();
                SetCurrentValue(ReferenceCalibrationActiveProperty, false);
                SetCurrentValue(ReferenceCalibrationPointsProperty, Array.Empty<Editor2DPoint>());
                e.Handled = true;
                return;
            }

            if (_isTextEntryActive)
            {
                if (_pendingTextInitialEntry is not null)
                    SetCurrentValue(TextEntryProperty, _pendingTextInitialEntry);
                CancelPendingText();
                e.Handled = true;
                return;
            }

            if (ActiveTool == Editor2DTool.Offset && DataContext is EditorPageViewModel offsetViewModel)
                offsetViewModel.CancelTwoDOffset(exitTool: true);
            else if (ActiveTool == Editor2DTool.Scale && DataContext is EditorPageViewModel scaleViewModel)
                scaleViewModel.CancelTwoDScaleAndExit();
            else if (ActiveTool == Editor2DTool.Mirror && DataContext is EditorPageViewModel mirrorViewModel)
                mirrorViewModel.CancelTwoDMirror(exitTool: true);
            else if ((ActiveTool is Editor2DTool.Fillet or Editor2DTool.Chamfer)
                     && DataContext is EditorPageViewModel cornerViewModel)
                cornerViewModel.CancelTwoDCornerToolSession(exitTool: true);
            else
                CancelActiveInteraction();
            e.Handled = true;
            return;
        }

        if (_isTextEntryActive && e.Key == Key.Enter)
        {
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                TextEntry += Environment.NewLine;
                e.Handled = true;
                return;
            }

            if (_pendingTextStart is { } textStart
                && _pendingTextEnd is { } textEnd
                && DataContext is EditorPageViewModel viewModel)
            {
                viewModel.TwoDSelectedTextDraft = TextEntry;
                var newPathId = viewModel.CreateTwoDText(textStart, textEnd);
                if (newPathId is not null)
                {
                    SetCurrentValue(DocumentProperty, viewModel.TwoDDocument);
                    SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
                    SetCurrentValue(SelectedMeasurementIdProperty, null);
                    SetCurrentValue(TextEntryProperty, "Label");
                }
                _pendingTextStart = null;
                _pendingTextEnd = null;
                _pendingTextInitialEntry = null;
            }
            else if (DataContext is EditorPageViewModel selectedTextViewModel)
            {
                selectedTextViewModel.ApplyTwoDSelectedText();
            }
            _isTextEntryActive = false;
            e.Handled = true;
            return;
        }

        if (_isTextEntryActive && e.Key == Key.Back)
        {
            if (TextEntry.Length > 0)
                TextEntry = TextEntry[..^1];
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && ActiveTool == Editor2DTool.Pen)
        {
            CommitPendingPenPath(isClosed: false);
            e.Handled = true;
        }

        else if (e.Key == Key.Enter
            && e.KeyModifiers == KeyModifiers.None
            && ActiveTool == Editor2DTool.Scale
            && DataContext is EditorPageViewModel scaleViewModel)
        {
            scaleViewModel.ConfirmTwoDScaleAndExit();
            e.Handled = true;
        }

        else if (e.Key == Key.Enter
            && ActiveTool == Editor2DTool.Mirror
            && DataContext is EditorPageViewModel mirrorViewModel
            && mirrorViewModel.ConfirmTwoDMirror())
        {
            mirrorViewModel.TwoDActiveTool = Editor2DTool.Select;
            e.Handled = true;
        }

        else if (e.Key == Key.Enter
            && ActiveTool == Editor2DTool.Offset
            && DataContext is EditorPageViewModel offsetViewModel)
        {
            await offsetViewModel.ConfirmTwoDOffsetAsync();
            e.Handled = true;
        }

        else if (e.Key == Key.Enter
                 && (ActiveTool is Editor2DTool.Fillet or Editor2DTool.Chamfer)
                 && DataContext is EditorPageViewModel cornerViewModel)
        {
            cornerViewModel.ConfirmTwoDCornerToolSession(exitTool: true);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!_isTextEntryActive || string.IsNullOrEmpty(e.Text))
            return;

        TextEntry += e.Text;
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is not (Key.LeftShift or Key.RightShift))
            return;

        _shiftSnapHeld = false;
        UpdateSnapHover(_hoverPointerPosition);
        InvalidateVisual();
    }

    private void DrawGrid(DrawingContext context, Size size)
    {
        var majorStep = CalculateWorldStep(targetPixels: 96.0);
        var minorStep = majorStep / 4.0;
        var visibleBounds = GetVisibleWorldBounds(size);

        DrawGridLines(context, size, visibleBounds, minorStep, MinorGridPen);
        DrawGridLines(context, size, visibleBounds, majorStep, MajorGridPen);

        if (visibleBounds.Left <= 0 && visibleBounds.Right >= 0)
        {
            var x = WorldToScreen(new Editor2DPoint(0, 0), size).X;
            context.DrawLine(AxisPen, new Point(x, 0), new Point(x, size.Height));
        }

        if (visibleBounds.Bottom <= 0 && visibleBounds.Top >= 0)
        {
            var y = WorldToScreen(new Editor2DPoint(0, 0), size).Y;
            context.DrawLine(AxisPen, new Point(0, y), new Point(size.Width, y));
        }
    }

    private void DrawSnapIndicator(DrawingContext context)
    {
        if (_activeSnapResult is not { } snap || !IsSnappingActive)
            return;

        const double markerSize = 8.0;
        var marker = new Rect(
            snap.ScreenPoint.X - (markerSize / 2.0),
            snap.ScreenPoint.Y - (markerSize / 2.0),
            markerSize,
            markerSize);
        context.DrawRectangle(null, SnapIndicatorPen, marker);

        var text = new FormattedText(
            snap.Label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MeasurementLabelTypeface,
            10.0,
            SnapIndicatorBrush);
        var labelPosition = new Point(snap.ScreenPoint.X + 9.0, snap.ScreenPoint.Y - text.Height - 5.0);
        context.FillRectangle(
            SnapLabelFillBrush,
            new Rect(labelPosition.X - 3.0, labelPosition.Y - 2.0, text.Width + 6.0, text.Height + 4.0));
        context.DrawText(text, labelPosition);
    }

    private void DrawPaperBounds(DrawingContext context, Size size, Editor2DBounds bounds)
    {
        const double padding = 18.0;
        var topLeft = WorldToScreen(new Editor2DPoint(bounds.MinX - padding, bounds.MaxY + padding), size);
        var bottomRight = WorldToScreen(new Editor2DPoint(bounds.MaxX + padding, bounds.MinY - padding), size);
        var rect = new Rect(topLeft, bottomRight);
        context.DrawRectangle(PaperFillBrush, PaperBorderPen, rect);
    }

    internal static Rect GetReferenceImageLocalRect(Editor2DReferenceImage image, double zoom)
        => new(
            -image.Width * zoom / 2.0,
            -image.Height * zoom / 2.0,
            image.Width * zoom,
            image.Height * zoom);

    internal static Editor2DBounds MeasureReferenceImageBounds(IReadOnlyList<Editor2DReferenceImage> images)
    {
        var extents = images.Select(image =>
        {
            var radians = image.RotationDegrees * Math.PI / 180.0;
            var halfWidth = Math.Abs(Math.Cos(radians)) * image.Width / 2.0
                + Math.Abs(Math.Sin(radians)) * image.Height / 2.0;
            var halfHeight = Math.Abs(Math.Sin(radians)) * image.Width / 2.0
                + Math.Abs(Math.Cos(radians)) * image.Height / 2.0;
            return new Editor2DBounds(
                image.X - halfWidth,
                image.Y - halfHeight,
                image.X + halfWidth,
                image.Y + halfHeight);
        }).ToArray();
        return new Editor2DBounds(
            extents.Min(bounds => bounds.MinX),
            extents.Min(bounds => bounds.MinY),
            extents.Max(bounds => bounds.MaxX),
            extents.Max(bounds => bounds.MaxY));
    }

    private void DrawReferenceImages(DrawingContext context, Size size)
    {
        foreach (var image in ReferenceImages)
        {
            var bitmap = GetReferenceImageBitmap(image);
            if (bitmap is null || image.Opacity <= 0.0 || image.Width <= 0.0 || image.Height <= 0.0)
                continue;

            var center = WorldToScreen(new Editor2DPoint(image.X, image.Y), size);
            var destination = GetReferenceImageLocalRect(image, Zoom);
            using var transform = context.PushTransform(
                Matrix.CreateTranslation(center.X, center.Y)
                * Matrix.CreateRotation(-image.RotationDegrees * Math.PI / 180.0));
            using var opacity = context.PushOpacity(Math.Clamp(image.Opacity, 0.0, 1.0));
            context.DrawImage(
                bitmap,
                new Rect(bitmap.Size),
                destination);
        }
    }

    private void DrawReferenceImageGizmo(DrawingContext context, Size size)
    {
        if (ActiveTool != Editor2DTool.Select || ActiveReferenceImage is not { } image || image.Width <= 0.0 || image.Height <= 0.0)
            return;

        var center = WorldToScreen(new Editor2DPoint(image.X, image.Y), size);
        var rect = GetReferenceImageLocalRect(image, Zoom);
        using var transform = context.PushTransform(
            Matrix.CreateTranslation(center.X, center.Y)
            * Matrix.CreateRotation(-image.RotationDegrees * Math.PI / 180.0));
        context.DrawRectangle(null, SelectedPathPen, rect);
        const double handle = 7.0;
        foreach (var point in new[]
        {
            new Point(rect.Left, rect.Top),
            new Point(rect.Right, rect.Top),
            new Point(rect.Right, rect.Bottom),
            new Point(rect.Left, rect.Bottom),
        })
        {
            context.FillRectangle(EditableVertexHandleFillBrush, new Rect(point.X - handle / 2.0, point.Y - handle / 2.0, handle, handle));
        }
        foreach (var point in new[]
        {
            new Point(rect.Center.X, rect.Top), new Point(rect.Right, rect.Center.Y),
            new Point(rect.Center.X, rect.Bottom), new Point(rect.Left, rect.Center.Y),
        })
        {
            context.FillRectangle(EditableVertexHandleFillBrush, new Rect(point.X - handle / 2.0, point.Y - handle / 2.0, handle, handle));
        }
        var rotationHandle = new Point(rect.Center.X, rect.Top - 24.0);
        context.DrawLine(SelectedPathPen, new Point(rect.Center.X, rect.Top), rotationHandle);
        context.DrawEllipse(EditableVertexHandleFillBrush, SelectedPathPen, rotationHandle, handle / 2.0, handle / 2.0);
    }

    private Bitmap? GetReferenceImageBitmap(Editor2DReferenceImage image)
    {
        if (_referenceImageBitmaps.TryGetValue(image.Id, out var bitmap))
            return bitmap;

        try
        {
            var bytes = Convert.FromBase64String(image.DataBase64);
            using var stream = new MemoryStream(bytes, writable: false);
            bitmap = new Bitmap(stream);
            _referenceImageBitmaps[image.Id] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    private void DrawPaths(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
        => _renderer.DrawPaths(
            context,
            paths,
            SelectedPathIds,
            _hoveredPathId,
            point => WorldToScreen(point, size),
            ResolvePathPen,
            path => path.IsConstruction ? null : path.IsFilled ? FilledPathBrush : null,
            (path, pen) => TryDrawSemanticPrimitive(context, size, path, pen));

    internal static Pen ResolvePathPen(DxfCanvasPathVisualRole role)
        => role switch
        {
            DxfCanvasPathVisualRole.Selected => SelectedPathPen,
            DxfCanvasPathVisualRole.Hovered => HoverPathPen,
            DxfCanvasPathVisualRole.Construction => ConstructionPathPen,
            DxfCanvasPathVisualRole.Closed => ClosedPathPen,
            _ => OpenPathPen,
        };

    private void DrawPreviewPaths(DrawingContext context, Size size)
        => DrawPreviewPaths(context, size, PreviewPaths);

    private void DrawPreviewPaths(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
        => DrawPreviewPaths(context, size, paths, PreviewPathPen);

    private void DrawPreviewPaths(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths, Pen pen)
    {
        foreach (var path in paths)
        {
            if (path.Center is Editor2DPoint center && path.Radius is > 0)
            {
                context.DrawEllipse(null, pen, WorldToScreen(center, size), path.Radius.Value * Zoom, path.Radius.Value * Zoom);
                continue;
            }
            if (path.Points.Count < 2)
                continue;
            var geometry = new StreamGeometry();
            using var geometryContext = geometry.Open();
            geometryContext.BeginFigure(WorldToScreen(path.Points[0], size), false);
            for (var index = 1; index < path.Points.Count; index++)
                geometryContext.LineTo(WorldToScreen(path.Points[index], size));
            if (path.IsClosed)
                geometryContext.EndFigure(true);
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawPatternPivot(DrawingContext context, Size size)
    {
        if (!string.Equals(PatternMode, "Circular", StringComparison.Ordinal)
            || PatternPivot is not { } pivot)
            return;

        var point = WorldToScreen(pivot, size);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#FFB84D")), 1.5);
        context.DrawEllipse(null, pen, point, 6, 6);
        context.DrawLine(pen, new Point(point.X - 9, point.Y), new Point(point.X + 9, point.Y));
        context.DrawLine(pen, new Point(point.X, point.Y - 9), new Point(point.X, point.Y + 9));
    }

    private void DrawScalePivot(DrawingContext context, Size size)
    {
        if (ActiveTool != Editor2DTool.Scale || ScalePivot is not { } pivot)
            return;
        var point = WorldToScreen(pivot, size);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#63D6A2")), 1.5);
        context.DrawEllipse(null, pen, point, 6, 6);
        context.DrawLine(pen, new Point(point.X - 9, point.Y), new Point(point.X + 9, point.Y));
        context.DrawLine(pen, new Point(point.X, point.Y - 9), new Point(point.X, point.Y + 9));
    }

    private void DrawMeasurements(DrawingContext context, Size size)
    {
        foreach (var measurement in Measurements)
        {
            var previewMeasurement = GetTransformPreviewMeasurement(measurement);
            if (previewMeasurement is null)
                continue;

            if (previewMeasurement.IsAutoDimension
                && !string.IsNullOrWhiteSpace(previewMeasurement.EntityPathId)
                && !SelectedPathIds.Contains(previewMeasurement.EntityPathId, StringComparer.Ordinal))
            {
                continue;
            }

            var start = WorldToScreen(previewMeasurement.Start, size);
            var end = WorldToScreen(previewMeasurement.End, size);
            var isSelectedManualMeasurement = !previewMeasurement.IsAutoDimension
                && string.Equals(previewMeasurement.Id, SelectedMeasurementId, StringComparison.Ordinal);
            context.DrawLine(
                previewMeasurement.IsAutoDimension
                    ? AutoDimensionPen
                    : isSelectedManualMeasurement
                        ? SelectedMeasurementPen
                        : MeasurementPen,
                start,
                end);

            if (!previewMeasurement.IsAutoDimension)
            {
                context.DrawEllipse(MeasurementPointBrush, null, start, 3.0, 3.0);
                context.DrawEllipse(MeasurementPointBrush, null, end, 3.0, 3.0);
            }

            DrawMeasurementLabel(context, previewMeasurement, start, end);
        }
    }

    private Editor2DMeasurement? GetTransformPreviewMeasurement(Editor2DMeasurement measurement)
    {
        if (string.IsNullOrWhiteSpace(measurement.EntityPathId)
            || !SelectedPathIds.Contains(measurement.EntityPathId, StringComparer.Ordinal))
        {
            return measurement;
        }

        if (_isRotatingSelection && _rotatePivot is not null)
        {
            if (measurement.IsAutoDimension)
                return null;

            Editor2DPoint Rotate(Editor2DPoint point)
            {
                var radians = -_rotatePreviewDegrees * Math.PI / 180.0;
                var deltaX = point.X - _rotatePivot.X;
                var deltaY = point.Y - _rotatePivot.Y;
                return new Editor2DPoint(
                    _rotatePivot.X + (deltaX * Math.Cos(radians)) - (deltaY * Math.Sin(radians)),
                    _rotatePivot.Y + (deltaX * Math.Sin(radians)) + (deltaY * Math.Cos(radians)));
            }

            return measurement with
            {
                Start = Rotate(measurement.Start),
                End = Rotate(measurement.End),
                RectP1 = measurement.RectP1 is { } rectP1 ? Rotate(rectP1) : null,
                RectP2 = measurement.RectP2 is { } rectP2 ? Rotate(rectP2) : null,
            };
        }

        if (!_isTranslatingSelection || _translateCreateCopy)
            return measurement;

        Editor2DPoint Translate(Editor2DPoint point)
            => new(point.X + _translatePreviewDelta.X, point.Y + _translatePreviewDelta.Y);
        return measurement with
        {
            Start = Translate(measurement.Start),
            End = Translate(measurement.End),
            RectP1 = measurement.RectP1 is { } translateRectP1 ? Translate(translateRectP1) : null,
            RectP2 = measurement.RectP2 is { } translateRectP2 ? Translate(translateRectP2) : null,
        };
    }

    private void DrawCornerToolHandles(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (ActiveTool != Editor2DTool.Fillet && ActiveTool != Editor2DTool.Chamfer)
            return;

        foreach (var path in paths)
        {
            if (!CanCornerEditPath(path))
                continue;

            foreach (var (cornerIndex, point) in EnumerateCornerHandles(path))
            {
                var screenPoint = WorldToScreen(point, size);
                var radius = IsCornerHandleHovered(path.Id, cornerIndex) ? 5.0 : 4.0;
                context.DrawEllipse(CornerToolHandleBrush, CornerToolHandlePen, screenPoint, radius, radius);

                var parameter = CornerParameters.FirstOrDefault(item =>
                    item.PathId == path.Id && item.CornerIndex == cornerIndex);
                if (parameter is not null && parameter.SourcePoints.Count >= 3)
                {
                    var arrowPoint = CornerArrowPoint(parameter);
                    var arrowScreen = WorldToScreen(arrowPoint, size);
                    context.DrawLine(CornerToolHandlePen, screenPoint, arrowScreen);
                    context.DrawEllipse(CornerToolHandleBrush, CornerToolHandlePen, arrowScreen, 5.0, 5.0);
                }
            }
        }
    }

    private void DrawSewingHoleMarginHandle(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (ActiveTool != Editor2DTool.AddSewingHoles || SelectedPathIds.Count == 0)
            return;

        var selected = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!selected.Contains(path.Id) || !TryGetSewingHoleHandle(path, out var anchor, out var handle))
                continue;

            var anchorScreen = WorldToScreen(anchor, size);
            var handleScreen = WorldToScreen(handle, size);
            context.DrawLine(CornerToolHandlePen, anchorScreen, handleScreen);
            context.DrawEllipse(SewingHoleHandleBrush, CornerToolHandlePen, handleScreen, 5.5, 5.5);
            break;
        }
    }

    private void DrawOffsetHandle(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (ActiveTool != Editor2DTool.Offset || !TryGetSelectedOffsetHandle(out var anchor, out var handle))
            return;

        var anchorScreen = WorldToScreen(anchor, size);
        var handleScreen = WorldToScreen(handle, size);
        context.DrawLine(CornerToolHandlePen, anchorScreen, handleScreen);
        context.DrawEllipse(CornerToolHandleBrush, CornerToolHandlePen, handleScreen, 6.0, 6.0);
    }

    private void DrawEditableVertexHandles(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (!DxfCanvasSelectionInteraction.ShouldDrawHandles(ActiveTool) || SelectedPathIds.Count == 0)
            return;

        var selectedIds = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!selectedIds.Contains(path.Id) || path.IsAxisAlignedRectangle || !CanEditVertices(path))
                continue;

            foreach (var point in path.Points)
            {
                var screenPoint = WorldToScreen(point, size);
                var rect = new Rect(screenPoint.X - 3.5, screenPoint.Y - 3.5, 7.0, 7.0);
                context.DrawRectangle(EditableVertexHandleFillBrush, EditableVertexHandlePen, rect);
            }
        }
    }

    private void DrawConstrainedRectangleHandles(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (!DxfCanvasSelectionInteraction.ShouldDrawHandles(ActiveTool) || SelectedPathIds.Count == 0)
            return;

        var selectedIds = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!path.IsAxisAlignedRectangle || !selectedIds.Contains(path.Id))
                continue;

            foreach (var point in path.Points)
            {
                var screenPoint = WorldToScreen(point, size);
                var rect = new Rect(screenPoint.X - 4.0, screenPoint.Y - 4.0, 8.0, 8.0);
                context.DrawRectangle(ConstrainedRectangleHandleFillBrush, ConstrainedRectangleHandlePen, rect);
            }
        }
    }

    private void DrawMeasurementLabel(
        DrawingContext context,
        Editor2DMeasurement measurement,
        Point start,
        Point end)
    {
        var expression = measurement.Expression?.Trim();
        var labelValue = measurement.IsParametric && !string.IsNullOrWhiteSpace(expression)
            ? measurement.Driven
                ? $"({expression})"
                : expression.Any(character => char.IsLetter(character) || character is '+' or '-' or '*' or '/')
                    ? $"fx: {expression}"
                    : expression
            : measurement.Distance.ToString("0.###", CultureInfo.InvariantCulture);
        var labelText = $"{labelValue} mm";
        var text = new FormattedText(
            labelText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MeasurementLabelTypeface,
            11.0,
            measurement.IsAutoDimension ? AutoDimensionTextBrush : MeasurementTextBrush);
        var midpoint = new Point((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0);
        var paddingX = 6.0;
        var paddingY = 3.0;
        var rect = new Rect(
            midpoint.X - (text.Width / 2.0) - paddingX,
            midpoint.Y - (text.Height / 2.0) - paddingY,
            text.Width + (paddingX * 2.0),
            text.Height + (paddingY * 2.0));
        context.FillRectangle(measurement.IsAutoDimension ? AutoDimensionLabelFillBrush : MeasurementLabelFillBrush, rect);
        context.DrawText(text, new Point(rect.X + paddingX, rect.Y + paddingY));
    }

    private void DrawLiveSketchLine(DrawingContext context, Size size)
    {
        if (_pendingLineStart is not Editor2DPoint startModel || _pendingLineEnd is not Editor2DPoint endModel)
            return;

        var start = WorldToScreen(startModel, size);
        var end = WorldToScreen(endModel, size);
        context.DrawLine(HoverPathPen, start, end);
        context.DrawEllipse(LiveMeasurementPointBrush, null, start, 3.0, 3.0);
        context.DrawEllipse(LiveMeasurementPointBrush, null, end, 3.0, 3.0);
    }

    private void DrawLiveSketchRectangle(DrawingContext context, Size size)
    {
        if (_pendingRectangleStart is not Editor2DPoint startModel || _pendingRectangleEnd is not Editor2DPoint endModel)
            return;

        var preview = Editor2DRectangleCreationService.Create(
            "rectangle-preview", startModel, endModel, RectangleFilletRadius);
        if (preview is null)
            return;
        var rectanglePoints = preview.Path.Points;
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(WorldToScreen(rectanglePoints[0], size), false);
            for (var pointIndex = 1; pointIndex < rectanglePoints.Count; pointIndex++)
                geometryContext.LineTo(WorldToScreen(rectanglePoints[pointIndex], size));

            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(null, HoverPathPen, geometry);
        foreach (var point in rectanglePoints)
            context.DrawEllipse(LiveMeasurementPointBrush, null, WorldToScreen(point, size), 3.0, 3.0);
    }

    private void DrawLiveSketchCircle(DrawingContext context, Size size)
    {
        if (_pendingCircleCenter is not Editor2DPoint centerModel || _pendingCircleEdge is not Editor2DPoint edgeModel)
            return;

        var radius = Math.Sqrt(Math.Pow(edgeModel.X - centerModel.X, 2) + Math.Pow(edgeModel.Y - centerModel.Y, 2));
        if (radius <= 1e-6)
            return;

        var center = WorldToScreen(centerModel, size);
        var screenRadius = radius * Zoom;
        var rect = new Rect(
            center.X - screenRadius,
            center.Y - screenRadius,
            screenRadius * 2.0,
            screenRadius * 2.0);
        context.DrawEllipse(null, HoverPathPen, rect);
        context.DrawEllipse(LiveMeasurementPointBrush, null, center, 3.0, 3.0);
        context.DrawEllipse(LiveMeasurementPointBrush, null, WorldToScreen(edgeModel, size), 3.0, 3.0);
    }

    private void DrawLiveSketchPolygon(DrawingContext context, Size size)
    {
        if (_pendingPolygonCenter is not Editor2DPoint centerModel || _pendingPolygonEdge is not Editor2DPoint edgeModel)
            return;

        var radius = Math.Sqrt(Math.Pow(edgeModel.X - centerModel.X, 2) + Math.Pow(edgeModel.Y - centerModel.Y, 2));
        var points = BuildPolygonPoints(centerModel, edgeModel, PolygonSides);
        if (radius <= 1e-6 || points.Length < 3)
            return;

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(WorldToScreen(points[0], size), false);
            for (var pointIndex = 1; pointIndex < points.Length; pointIndex++)
                geometryContext.LineTo(WorldToScreen(points[pointIndex], size));

            geometryContext.EndFigure(true);
        }

        context.DrawGeometry(null, HoverPathPen, geometry);
        foreach (var point in points)
            context.DrawEllipse(LiveMeasurementPointBrush, null, WorldToScreen(point, size), 3.0, 3.0);
    }

    private void DrawLiveMeasurement(DrawingContext context, Size size)
    {
        var startModel = _pendingMeasurementStart ?? _pendingDimensionStart;
        var endModel = _pendingMeasurementStart is not null
            ? _pendingMeasurementEnd
            : _pendingDimensionEnd;
        if (startModel is not Editor2DPoint resolvedStartModel || endModel is not Editor2DPoint resolvedEndModel)
            return;

        var start = WorldToScreen(resolvedStartModel, size);
        var end = WorldToScreen(resolvedEndModel, size);
        context.DrawLine(LiveMeasurementPen, start, end);
        context.DrawEllipse(LiveMeasurementPointBrush, null, start, 3.0, 3.0);
        context.DrawEllipse(LiveMeasurementPointBrush, null, end, 3.0, 3.0);
    }

    private void DrawReferenceCalibration(DrawingContext context, Size size)
    {
        if (ReferenceCalibrationPoints.Count == 0)
            return;
        var points = ReferenceCalibrationPoints.Take(2).Select(point => WorldToScreen(point, size)).ToArray();
        if (points.Length == 2)
            context.DrawLine(ReferenceCalibrationPen, points[0], points[1]);
        for (var index = 0; index < points.Length; index++)
        {
            context.DrawEllipse(ReferenceCalibrationBrush, null, points[index], 5.0, 5.0);
            var label = new FormattedText(
                $"Point {index + 1}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Inter, Segoe UI, Arial"), 11, ReferenceCalibrationBrush);
            context.DrawText(label, points[index] + new Vector(8, -18));
        }
    }

    private void DrawTrimPreview(DrawingContext context, Size size, Editor2DPreviewDocument document)
    {
        if (ActiveTool != Editor2DTool.Trim || !_hasHoverPointerPosition)
        {
            return;
        }

        var hoverWorld = ScreenToWorld(_hoverPointerPosition, Zoom);
        var hitToleranceWorld = 12.0 / Math.Max(Zoom, 0.0001);
        if (DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(
                document, hoverWorld, hitToleranceWorld, out var wholeCurveTarget))
        {
            for (var index = 0; index < wholeCurveTarget.PreviewPoints.Count - 1; index++)
            {
                context.DrawLine(
                    TrimPreviewPen,
                    WorldToScreen(wholeCurveTarget.PreviewPoints[index], size),
                    WorldToScreen(wholeCurveTarget.PreviewPoints[index + 1], size));
            }
            return;
        }

        if (DxfCanvasCircularTrimGeometry.TryBuildTarget(
                document, hoverWorld, hitToleranceWorld, out var circularTarget))
        {
            for (var index = 0; index < circularTarget.PreviewPoints.Count - 1; index++)
            {
                context.DrawLine(
                    TrimPreviewPen,
                    WorldToScreen(circularTarget.PreviewPoints[index], size),
                    WorldToScreen(circularTarget.PreviewPoints[index + 1], size));
            }
            return;
        }

        if (!TryBuildTrimTarget(document, _hoverPointerPosition, out var trimTarget))
            return;

        var start = WorldToScreen(trimTarget.KillStart, size);
        var end = WorldToScreen(trimTarget.KillEnd, size);
        context.DrawLine(TrimPreviewPen, start, end);
    }

    private void DrawLivePenPath(DrawingContext context, Size size)
    {
        if (_pendingPenAnchors.Count == 0)
            return;

        var previewAnchors = _pendingPenAnchors;
        if (_pendingPenHoverPoint is Editor2DPoint hoverPoint
            && DistanceBetween(previewAnchors[^1].Point, hoverPoint) > 1e-7)
            previewAnchors = [.. previewAnchors, new Editor2DBezierAnchor(hoverPoint)];
        var previewPoints = previewAnchors.Count >= 2
            ? Editor2DBezierGeometry.Flatten(previewAnchors, closed: false)
            : [previewAnchors[0].Point];
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(WorldToScreen(previewPoints[0], size), false);
            for (var pointIndex = 1; pointIndex < previewPoints.Count; pointIndex++)
                geometryContext.LineTo(WorldToScreen(previewPoints[pointIndex], size));
        }

        context.DrawGeometry(null, HoverPathPen, geometry);
        for (var pointIndex = 0; pointIndex < _pendingPenAnchors.Count; pointIndex++)
        {
            var anchor = _pendingPenAnchors[pointIndex];
            var point = anchor.Point;
            var screenPoint = WorldToScreen(point, size);
            if (anchor.HandleIn is Editor2DPoint handleIn)
            {
                var handleScreen = WorldToScreen(handleIn, size);
                context.DrawLine(LiveMeasurementPen, screenPoint, handleScreen);
                context.DrawEllipse(LiveMeasurementPointBrush, null, handleScreen, 3.0, 3.0);
            }
            if (anchor.HandleOut is Editor2DPoint handleOut)
            {
                var handleScreen = WorldToScreen(handleOut, size);
                context.DrawLine(LiveMeasurementPen, screenPoint, handleScreen);
                context.DrawEllipse(LiveMeasurementPointBrush, null, handleScreen, 3.0, 3.0);
            }
            var isTerminalAnchor = _pendingPenAnchors.Count >= 2
                && (pointIndex == 0 || pointIndex == _pendingPenAnchors.Count - 1);
            var radius = isTerminalAnchor ? 4.0 : 3.0;
            context.DrawEllipse(
                pointIndex == 0 && _pendingPenAnchors.Count >= 2 ? EditableVertexHandleFillBrush : LiveMeasurementPointBrush,
                null,
                screenPoint,
                radius,
                radius);
        }
    }

    private void DrawLiveMirrorAxis(DrawingContext context, Size size)
    {
        if (TwoDMirrorAxisStart is not Editor2DPoint start)
            return;

        var startScreen = WorldToScreen(start, size);
        context.DrawEllipse(LiveMeasurementPointBrush, null, startScreen, 4.0, 4.0);
        if (TwoDMirrorAxisEnd is not Editor2DPoint end)
            return;

        var endScreen = WorldToScreen(end, size);
        var delta = endScreen - startScreen;
        var length = Math.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));
        if (length <= 1e-6)
            return;
        var extension = Math.Sqrt((size.Width * size.Width) + (size.Height * size.Height));
        var unit = delta / length;
        context.DrawLine(HoverPathPen, startScreen - (unit * extension), endScreen + (unit * extension));
        context.DrawEllipse(LiveMeasurementPointBrush, null, endScreen, 4.0, 4.0);

        if (Document is null || SelectedPathIds.Count == 0)
            return;
        var selected = SelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var mirrored = Document.Paths
            .Where(path => selected.Contains(path.Id))
            .Select(path => Editor2DGeometry.CreateMirrorCopy(path, start, end, TwoDMirrorFlipCopy))
            .ToArray();
        DrawPreviewPaths(context, size, mirrored);
    }

    private void DrawScaleGizmo(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (ActiveTool != Editor2DTool.Scale
            || !TryGetSelectedPathBounds(paths, out var minX, out var minY, out var maxX, out var maxY))
        {
            return;
        }

        var center = new Editor2DPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
        var pivot = ScalePivot ?? (ScaleFromCenter ? center : new Editor2DPoint(minX, minY));
        var corner = new Editor2DPoint(maxX, maxY);
        var pivotScreen = WorldToScreen(pivot, size);
        var cornerScreen = WorldToScreen(corner, size);
        var handleScreen = cornerScreen;

        context.DrawLine(HoverPathPen, pivotScreen, handleScreen);
        context.DrawEllipse(null, HoverPathPen, pivotScreen, 6.0, 6.0);
        var handleRect = new Rect(handleScreen.X - 7.0, handleScreen.Y - 7.0, 14.0, 14.0);
        context.DrawRectangle(EditableVertexHandleFillBrush, EditableVertexHandlePen, handleRect);

        var factorText = new FormattedText(
            $"x{GetScalePreviewFactor():0.###}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MeasurementLabelTypeface,
            11.0,
            MeasurementTextBrush);
        var labelOrigin = new Point(handleScreen.X + 12.0, handleScreen.Y - factorText.Height - 4.0);
        var labelRect = new Rect(
            labelOrigin.X - 6.0,
            labelOrigin.Y - 3.0,
            factorText.Width + 12.0,
            factorText.Height + 6.0);
        context.FillRectangle(MeasurementLabelFillBrush, labelRect);
        context.DrawText(factorText, labelOrigin);
    }

    private void DrawTranslationGizmo(DrawingContext context, Size size)
    {
        if (!ShouldShowSelectionTransformGizmo()
            || Document is null
            || !DxfCanvasTranslationInteraction.TryGetSelectionPivot(Document.Paths, SelectedPathIds, out var selectionPivot))
        {
            return;
        }

        var basePivot = _isTranslatingSelection && _translatePivot is not null
            ? _translatePivot
            : selectionPivot;
        var livePivot = new Editor2DPoint(
            basePivot.X + (_isTranslatingSelection ? _translatePreviewDelta.X : 0.0),
            basePivot.Y + (_isTranslatingSelection ? _translatePreviewDelta.Y : 0.0));
        var pivotScreen = WorldToScreen(livePivot, size);
        var handles = DxfCanvasTranslationInteraction.GetHandleGeometry(pivotScreen);

        context.DrawLine(TranslationXGizmoPen, handles.Free, handles.X);
        context.DrawLine(TranslationYGizmoPen, handles.Free, handles.Y);
        DrawTranslationArrow(context, handles.X, DxfCanvasTranslationHandle.X);
        DrawTranslationArrow(context, handles.Y, DxfCanvasTranslationHandle.Y);
        context.DrawRectangle(
            TranslationFreeGizmoBrush,
            EditableVertexHandlePen,
            new Rect(handles.Free.X - 8.0, handles.Free.Y - 8.0, 16.0, 16.0));
    }

    private static void DrawTranslationArrow(
        DrawingContext context,
        Point tip,
        DxfCanvasTranslationHandle handle)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            if (handle == DxfCanvasTranslationHandle.X)
            {
                path.BeginFigure(new Point(tip.X + 7.0, tip.Y), true);
                path.LineTo(new Point(tip.X - 7.0, tip.Y - 7.0));
                path.LineTo(new Point(tip.X - 7.0, tip.Y + 7.0));
            }
            else
            {
                path.BeginFigure(new Point(tip.X, tip.Y - 7.0), true);
                path.LineTo(new Point(tip.X - 7.0, tip.Y + 7.0));
                path.LineTo(new Point(tip.X + 7.0, tip.Y + 7.0));
            }
            path.EndFigure(true);
        }

        context.DrawGeometry(
            handle == DxfCanvasTranslationHandle.X ? TranslationXGizmoBrush : TranslationYGizmoBrush,
            null,
            geometry);
    }

    private void DrawRotationGizmo(DrawingContext context, Size size)
    {
        if (!ShouldShowSelectionTransformGizmo()
            || Document is null
            || !DxfCanvasTranslationInteraction.TryGetSelectionPivot(Document.Paths, SelectedPathIds, out var selectionPivot))
        {
            return;
        }

        var pivot = _isTranslatingSelection && _translatePivot is not null
            ? new Editor2DPoint(
                _translatePivot.X + _translatePreviewDelta.X,
                _translatePivot.Y + _translatePreviewDelta.Y)
            : _isRotatingSelection && _rotatePivot is not null
                ? _rotatePivot
                : selectionPivot;
        var pivotScreen = WorldToScreen(pivot, size);
        var handleScreen = DxfCanvasRotationInteraction.GetHandlePosition(pivotScreen, _rotatePreviewDegrees);
        context.DrawLine(RotationGizmoPen, pivotScreen, handleScreen);
        context.DrawEllipse(RotationGizmoBrush, EditableVertexHandlePen, handleScreen, 8.0, 8.0);
    }

    private void DrawLiveSketchText(DrawingContext context, Size size)
    {
        if (_pendingTextStart is not Editor2DPoint startModel || _pendingTextEnd is not Editor2DPoint endModel)
            return;

        var start = WorldToScreen(startModel, size);
        var end = WorldToScreen(endModel, size);
        var rect = new Rect(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
        context.DrawRectangle(null, HoverPathPen, rect);

        var width = Math.Abs(endModel.X - startModel.X);
        var height = Math.Abs(endModel.Y - startModel.Y);
        var labelText = new FormattedText(
            $"W: {width:0.##} | H: {height:0.##} mm",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MeasurementLabelTypeface,
            11.0,
            MeasurementTextBrush);
        var labelOrigin = new Point(rect.Right - labelText.Width, rect.Top - labelText.Height - 8.0);
        var backgroundRect = new Rect(
            labelOrigin.X - 6.0,
            labelOrigin.Y - 3.0,
            labelText.Width + 12.0,
            labelText.Height + 6.0);
        context.FillRectangle(MeasurementLabelFillBrush, backgroundRect);
        context.DrawText(labelText, labelOrigin);

        if (_isTextEntryActive
            && !string.IsNullOrEmpty(TextEntry)
            && DataContext is EditorPageViewModel textViewModel)
        {
            var spacing = double.TryParse(
                textViewModel.TwoDSelectedTextCharacterSpacingText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedSpacing) && double.IsFinite(parsedSpacing)
                ? parsedSpacing
                : 0.0;
            var preview = Editor2DTextCreationService.Create(
                "text-preview", startModel, endModel, TextEntry, height,
                TextFontPreview ?? textViewModel.TwoDSelectedTextFontFamily,
                spacing,
                textViewModel.TwoDSelectedTextBold,
                textViewModel.TwoDSelectedTextItalic,
                textViewModel.TwoDSelectedTextUnderline,
                textViewModel.TwoDSelectedTextFitMode);
            if (preview is not null)
                TryDrawSemanticPrimitive(context, size, preview, HoverPathPen);
        }
    }

    private void DrawMarquee(DrawingContext context)
    {
        if (!_isMarqueeSelecting || _marqueeStartPoint is not Point start || _marqueeCurrentPoint is not Point end)
            return;

        var rect = GetSelectionRect(start, end);
        context.DrawRectangle(MarqueeFillBrush, MarqueePen, rect);
    }

    private void DrawGridLines(DrawingContext context, Size size, WorldBounds visibleBounds, double step, Pen pen)
    {
        if (!double.IsFinite(step) || step <= 0.0)
            return;

        var startX = Math.Floor(visibleBounds.Left / step) * step;
        for (var x = startX; x <= visibleBounds.Right; x += step)
        {
            var screenX = WorldToScreen(new Editor2DPoint(x, 0), size).X;
            context.DrawLine(pen, new Point(screenX, 0), new Point(screenX, size.Height));
        }

        var startY = Math.Floor(visibleBounds.Bottom / step) * step;
        for (var y = startY; y <= visibleBounds.Top; y += step)
        {
            var screenY = WorldToScreen(new Editor2DPoint(0, y), size).Y;
            context.DrawLine(pen, new Point(0, screenY), new Point(size.Width, screenY));
        }
    }

    private void HandleMeasurementClick(Point screenPoint)
    {
        var worldPoint = ResolvePlacementPoint(screenPoint, _pendingMeasurementStart, allowOrthogonal: _pendingMeasurementStart is not null);
        if (_pendingMeasurementStart is null)
        {
            _pendingMeasurementStart = worldPoint;
            _pendingMeasurementEnd = worldPoint;
            InvalidateVisual();
            return;
        }

        var nextMeasurements = Measurements.ToList();
        nextMeasurements.Add(new Editor2DMeasurement(
            Guid.NewGuid().ToString("N"),
            _pendingMeasurementStart,
            worldPoint));
        SetCurrentValue(MeasurementsProperty, nextMeasurements.ToArray());
        CancelPendingMeasurement();
        InvalidateVisual();
    }

    private void HandleDimensionClick(Point screenPoint)
    {
        if (Document is null)
            return;

        var worldPoint = ResolvePlacementPoint(screenPoint, _pendingDimensionStart, allowOrthogonal: _pendingDimensionStart is not null);
        if (_pendingDimensionStart is not null)
        {
            if (DistanceBetween(_pendingDimensionStart, worldPoint) <= 1e-6)
            {
                _pendingDimensionEnd = worldPoint;
                InvalidateVisual();
                return;
            }

            var referenceMeasurement = new Editor2DMeasurement(
                Id: Guid.NewGuid().ToString("N"),
                Start: _pendingDimensionStart,
                End: worldPoint,
                IsAutoDimension: false,
                DimensionType: "reference");
            var nextMeasurements = Measurements.ToList();
            nextMeasurements.Add(referenceMeasurement);
            SetCurrentValue(MeasurementsProperty, nextMeasurements.ToArray());
            SetCurrentValue(SelectedMeasurementIdProperty, referenceMeasurement.Id);
            SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
            CancelPendingDimension();
            InvalidateVisual();
            return;
        }

        var hitPathId = HitTestPathId(screenPoint);
        if (!string.IsNullOrWhiteSpace(hitPathId))
        {
            var path = Document.Paths.FirstOrDefault(candidate => string.Equals(candidate.Id, hitPathId, StringComparison.Ordinal));
            if (path is not null && TryCreateAttachedDimensionMeasurement(path, worldPoint, out var attachedMeasurement))
            {
                var nextMeasurements = Measurements.ToList();
                nextMeasurements.Add(attachedMeasurement);
                SetCurrentValue(MeasurementsProperty, nextMeasurements.ToArray());
                SetCurrentValue(SelectedMeasurementIdProperty, attachedMeasurement.Id);
                SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
                InvalidateVisual();
                return;
            }
        }

        _pendingDimensionStart = worldPoint;
        _pendingDimensionEnd = worldPoint;
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
    }

    private void HandleTrimClick(Point screenPoint)
    {
        if (Document is null)
            return;

        var worldPoint = ScreenToWorld(screenPoint, Zoom);
        var hitToleranceWorld = 12.0 / Math.Max(Zoom, 0.0001);
        if (DxfCanvasWholeCurveTrimGeometry.TryBuildTarget(
                Document, worldPoint, hitToleranceWorld, out var wholeCurveTarget))
        {
            ApplyPathReplacement(wholeCurveTarget.PathId, wholeCurveTarget.ReplacementPaths);
            return;
        }

        if (DxfCanvasCircularTrimGeometry.TryBuildTarget(
                Document, worldPoint, hitToleranceWorld, out var circularTarget))
        {
            ApplyPathReplacement(circularTarget.PathId, circularTarget.ReplacementPaths);
            return;
        }

        if (!TryBuildTrimTarget(Document, screenPoint, out var trimTarget))
            return;

        var nextDocument = ApplyTrim(Document, trimTarget);
        if (nextDocument is null)
            return;

        SetCurrentValue(DocumentProperty, nextDocument);
        SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
    }

    private void ApplyPathReplacement(string sourcePathId, IReadOnlyList<Editor2DPreviewPath> replacements)
    {
        var request = new DxfCanvasPathReplacementEventArgs(sourcePathId, replacements);
        PathReplacementRequested?.Invoke(request);
        if (request.Document is null)
            return;
        SetCurrentValue(DocumentProperty, request.Document);
        SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
    }

    private void HandleCornerToolClick(Point screenPoint, string kind, IPointer pointer)
    {
        if (Document is null
            || (!TryHitTestCornerArrow(screenPoint, out var hit)
                && !TryHitTestCornerHandle(GetVisiblePaths(), screenPoint, out hit)))
            return;

        var path = Document.Paths.FirstOrDefault(candidate => candidate.Id == hit.PathId);
        if (path is null)
            return;

        var existingForPath = CornerParameters.Where(parameter => parameter.PathId == hit.PathId).ToArray();
        var sourcePoints = existingForPath.FirstOrDefault()?.SourcePoints ?? path.Points;
        if (hit.CornerIndex < 0 || hit.CornerIndex >= sourcePoints.Count)
            return;

        var previousIndex = hit.CornerIndex == 0 ? sourcePoints.Count - 1 : hit.CornerIndex - 1;
        var nextIndex = hit.CornerIndex == sourcePoints.Count - 1 ? 0 : hit.CornerIndex + 1;
        var cornerKind = kind.Equals("chamfer", StringComparison.OrdinalIgnoreCase)
            ? Editor2DCornerKind.Chamfer
            : Editor2DCornerKind.Fillet;
        _cornerToolSessionValue = existingForPath.FirstOrDefault(item => item.CornerIndex == hit.CornerIndex)?.Value
            ?? Editor2DCornerGeometry.DefaultValue(
            sourcePoints[previousIndex],
            sourcePoints[hit.CornerIndex],
            sourcePoints[nextIndex],
            cornerKind);
        var parameter = new Editor2DCornerParameter(
            $"{hit.PathId}:{hit.CornerIndex}",
            hit.PathId,
            hit.CornerIndex,
            cornerKind,
            _cornerToolSessionValue.Value,
            sourcePoints.ToArray(),
            FilletContinuity);
        if (DataContext is EditorPageViewModel cornerViewModel
            && cornerViewModel.UpsertTwoDCornerParameter(parameter))
        {
            _isDraggingCorner = true;
            _cornerDragPathId = hit.PathId;
            _cornerDragIndex = hit.CornerIndex;
            _cornerDragKind = cornerKind;
            pointer.Capture(this);
            InvalidateVisual();
            return;
        }
        var nextParameters = CornerParameters
            .Where(item => item.Id != parameter.Id)
            .Append(parameter)
            .ToArray();
        var sourcePath = path with { Points = sourcePoints };
        var nextPath = Editor2DCornerGeometry.Apply(sourcePath, nextParameters);
        var nextDocument = CreateUpdatedDocument(
            Document,
            Document.Paths.Select(item => item.Id == path.Id ? nextPath : item).ToArray());

        SetCurrentValue(CornerParametersProperty, nextParameters);
        SetCurrentValue(SelectedCornerParameterIdProperty, parameter.Id);
        SetCurrentValue(DocumentProperty, nextDocument);
        SetCurrentValue(SelectedPathIdsProperty, new[] { hit.PathId });
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        _isDraggingCorner = true;
        _cornerDragPathId = hit.PathId;
        _cornerDragIndex = hit.CornerIndex;
        _cornerDragKind = cornerKind;
        pointer.Capture(this);
        InvalidateVisual();
    }

    private bool TryBeginSewingHoleMarginDrag(Point screenPoint, IPointer pointer)
    {
        if (!TryGetSelectedSewingHoleHandle(out _, out var handle)
            || ScreenDistance(screenPoint, WorldToScreen(handle, Bounds.Size)) > 12.0)
            return false;

        _isDraggingSewingHoleMargin = true;
        pointer.Capture(this);
        return true;
    }

    private void ApplySewingHoleMarginDrag(Point screenPoint)
    {
        if (!TryGetSelectedSewingHoleHandle(out var anchor, out var handle))
            return;

        var axisX = handle.X - anchor.X;
        var axisY = handle.Y - anchor.Y;
        var length = Math.Sqrt(axisX * axisX + axisY * axisY);
        if (length <= 0.0001)
            return;

        var point = ScreenToWorld(screenPoint);
        var dx = point.X - anchor.X;
        var dy = point.Y - anchor.Y;
        var margin = Math.Max(0.0, (dx * axisX + dy * axisY) / length);
        SetCurrentValue(SewingHoleMarginProperty, margin);
        InvalidateVisual();
    }

    private bool TryBeginOffsetHandleDrag(Point screenPoint, IPointer pointer)
    {
        if (!TryGetSelectedOffsetHandle(out _, out var handle)
            || ScreenDistance(screenPoint, WorldToScreen(handle, Bounds.Size)) > 12.0)
            return false;

        _isDraggingOffsetHandle = true;
        pointer.Capture(this);
        return true;
    }

    private void ApplyOffsetHandleDrag(Point screenPoint)
    {
        if (!TryGetSelectedOffsetHandle(out var anchor, out var handle))
            return;

        var axisX = handle.X - anchor.X;
        var axisY = handle.Y - anchor.Y;
        var length = Math.Sqrt(axisX * axisX + axisY * axisY);
        if (length <= 0.0001)
            return;

        var point = ScreenToWorld(screenPoint);
        var dx = point.X - anchor.X;
        var dy = point.Y - anchor.Y;
        var projection = (dx * axisX + dy * axisY) / length;
        SetCurrentValue(OffsetDistanceTextProperty, Math.Max(0.1, Math.Abs(projection)).ToString("0.###", CultureInfo.InvariantCulture));
        SetCurrentValue(OffsetSideProperty, projection >= 0.0 ? "Outward" : "Inward");
        InvalidateVisual();
    }

    private void DrawGlueTabHandles(DrawingContext context, Size size)
    {
        if (!TryGetGlueTabHandles(out _, out var handles))
            return;

        const double radius = 8.0;
        context.DrawEllipse(GlueTabHandleBrush, EditableVertexHandlePen, WorldToScreen(handles.Start, size), radius, radius);
        context.DrawEllipse(GlueTabHandleBrush, EditableVertexHandlePen, WorldToScreen(handles.End, size), radius, radius);
    }

    private bool TryBeginGlueTabHandleDrag(Point screenPoint, IPointer pointer)
    {
        if (!TryGetGlueTabHandles(out _, out var handles))
            return false;
        _glueTabDragHandle = DxfCanvasGlueTabInteraction.HitTest(
            screenPoint,
            WorldToScreen(handles.Start, Bounds.Size),
            WorldToScreen(handles.End, Bounds.Size),
            12.0);
        if (_glueTabDragHandle == DxfCanvasGlueTabHandle.None)
            return false;
        pointer.Capture(this);
        return true;
    }

    private void ApplyGlueTabHandleDrag(Point screenPoint)
    {
        if (_glueTabDragHandle == DxfCanvasGlueTabHandle.None
            || !TryGetGlueTabHandles(out var source, out _))
            return;

        ParseGlueTabOffsets(out var startOffset, out var endOffset);
        var otherOffset = _glueTabDragHandle == DxfCanvasGlueTabHandle.Start ? endOffset : startOffset;
        var offset = DxfCanvasGlueTabInteraction.ProjectOffset(
            source, ScreenToWorld(screenPoint), _glueTabDragHandle, otherOffset);
        var text = offset.ToString("0.###", CultureInfo.InvariantCulture);
        if (_glueTabDragHandle == DxfCanvasGlueTabHandle.Start)
            SetCurrentValue(GlueTabStartOffsetTextProperty, text);
        else
            SetCurrentValue(GlueTabEndOffsetTextProperty, text);
        InvalidateVisual();
    }

    private bool TryGetGlueTabHandles(
        out Editor2DPreviewPath source,
        out DxfCanvasGlueTabHandles handles)
    {
        source = default!;
        handles = default;
        if (ActiveTool != Editor2DTool.PaperFolding
            || SelectedPathIds.Count != 1
            || GlueTabPreviewPaths.Count != 1)
            return false;
        source = GetVisiblePaths().FirstOrDefault(path => path.Id == SelectedPathIds[0])!;
        if (source is null)
            return false;
        ParseGlueTabOffsets(out var startOffset, out var endOffset);
        return DxfCanvasGlueTabInteraction.TryGetHandles(source, startOffset, endOffset, out handles);
    }

    private void ParseGlueTabOffsets(out double startOffset, out double endOffset)
    {
        if (!double.TryParse(GlueTabStartOffsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out startOffset)
            || !double.IsFinite(startOffset))
            startOffset = 0.0;
        if (!double.TryParse(GlueTabEndOffsetText, NumberStyles.Float, CultureInfo.InvariantCulture, out endOffset)
            || !double.IsFinite(endOffset))
            endOffset = 0.0;
        startOffset = Math.Max(0.0, startOffset);
        endOffset = Math.Max(0.0, endOffset);
    }

    private bool TryGetSelectedOffsetHandle(out Editor2DPoint anchor, out Editor2DPoint handle)
    {
        anchor = default;
        handle = default;
        if (!double.TryParse(OffsetDistanceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var distance)
            || !double.IsFinite(distance))
            distance = 12.0;
        distance = Math.Max(0.1, Math.Abs(distance));
        var side = string.Equals(OffsetSide, "Inward", StringComparison.OrdinalIgnoreCase) ? -1.0 : 1.0;
        var selected = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        var path = GetVisiblePaths()
            .FirstOrDefault(candidate => selected.Contains(candidate.Id) && Editor2DGeometry.IsCurveOffsettablePath(candidate));
        if (path is null)
            return false;

        if ((path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
                || path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase))
            && path.Center is Editor2DPoint center)
        {
            anchor = center;
            handle = new Editor2DPoint(center.X + (distance * side), center.Y);
            return true;
        }

        if (path.Points.Count < 2)
            return false;
        var segmentCount = path.IsClosed ? path.Points.Count : path.Points.Count - 1;
        var bestLength = 0.0;
        Editor2DPoint bestStart = default;
        Editor2DPoint bestEnd = default;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = path.Points[index];
            var end = path.Points[(index + 1) % path.Points.Count];
            var segmentLength = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (segmentLength > bestLength)
            {
                bestLength = segmentLength;
                bestStart = start;
                bestEnd = end;
            }
        }

        if (bestLength <= 0.0001)
            return false;
        anchor = new Editor2DPoint((bestStart.X + bestEnd.X) / 2.0, (bestStart.Y + bestEnd.Y) / 2.0);
        var normalX = -(bestEnd.Y - bestStart.Y) / bestLength;
        var normalY = (bestEnd.X - bestStart.X) / bestLength;
        handle = new Editor2DPoint(
            anchor.X + (normalX * distance * side),
            anchor.Y + (normalY * distance * side));
        return true;
    }

    private bool TryGetSelectedSewingHoleHandle(out Editor2DPoint anchor, out Editor2DPoint handle)
    {
        anchor = default;
        handle = default;
        var selected = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        var path = GetVisiblePaths().FirstOrDefault(candidate => selected.Contains(candidate.Id));
        return path is not null && TryGetSewingHoleHandle(path, out anchor, out handle);
    }

    private bool TryGetSewingHoleHandle(Editor2DPreviewPath path, out Editor2DPoint anchor, out Editor2DPoint handle)
    {
        anchor = default;
        handle = default;
        if (path.Points.Count < 2)
            return false;

        var segmentCount = path.IsClosed ? path.Points.Count : path.Points.Count - 1;
        var bestLength = 0.0;
        Editor2DPoint bestStart = default;
        Editor2DPoint bestEnd = default;
        for (var index = 0; index < segmentCount; index++)
        {
            var start = path.Points[index];
            var end = path.Points[(index + 1) % path.Points.Count];
            var length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            if (length > bestLength)
            {
                bestLength = length;
                bestStart = start;
                bestEnd = end;
            }
        }

        if (bestLength <= 0.0001)
            return false;

        anchor = new Editor2DPoint((bestStart.X + bestEnd.X) / 2.0, (bestStart.Y + bestEnd.Y) / 2.0);
        var normalX = -(bestEnd.Y - bestStart.Y) / bestLength;
        var normalY = (bestEnd.X - bestStart.X) / bestLength;
        handle = new Editor2DPoint(
            anchor.X + normalX * SewingHoleMargin,
            anchor.Y + normalY * SewingHoleMargin);
        return true;
    }

    private void ApplyCornerDrag(Point screenPoint)
    {
        if (Document is null || _cornerDragPathId is null)
            return;

        var parameter = CornerParameters.FirstOrDefault(item =>
            item.PathId == _cornerDragPathId && item.CornerIndex == _cornerDragIndex);
        if (parameter is null)
            return;
        var path = Document.Paths.FirstOrDefault(item => item.Id == _cornerDragPathId);
        if (path is null)
            return;
        var source = parameter.SourcePoints;
        if (_cornerDragIndex <= 0 && !path.IsClosed)
            return;

        var previousIndex = _cornerDragIndex == 0 ? source.Count - 1 : _cornerDragIndex - 1;
        var nextIndex = _cornerDragIndex == source.Count - 1 ? 0 : _cornerDragIndex + 1;
        var value = Editor2DCornerGeometry.ValueFromPoint(
            source[previousIndex],
            source[_cornerDragIndex],
            source[nextIndex],
            ScreenToWorld(screenPoint),
            _cornerDragKind);
        var nextParameters = CornerParameters
            .Select(item => item.Id == parameter.Id ? item with { Value = value } : item)
            .ToArray();
        var updatedParameter = nextParameters.First(item => item.Id == parameter.Id);
        if (DataContext is EditorPageViewModel cornerViewModel
            && cornerViewModel.UpsertTwoDCornerParameter(updatedParameter))
        {
            InvalidateVisual();
            return;
        }
        var nextPath = Editor2DCornerGeometry.Apply(path with { Points = source }, nextParameters);
        SetCurrentValue(CornerParametersProperty, nextParameters);
        SetCurrentValue(DocumentProperty, Document with
        {
            Paths = Document.Paths.Select(item => item.Id == path.Id ? nextPath : item).ToArray(),
        });
        InvalidateVisual();
    }

    private bool TryHitTestCornerArrow(Point screenPoint, out CornerHandleHit hit)
    {
        const double tolerance = 10.0;
        var bestDistance = double.PositiveInfinity;
        CornerHandleHit? best = null;
        foreach (var parameter in CornerParameters)
        {
            var arrowScreen = WorldToScreen(CornerArrowPoint(parameter), Bounds.Size);
            var distance = ScreenDistance(screenPoint, arrowScreen);
            if (distance <= tolerance && distance < bestDistance)
            {
                bestDistance = distance;
                best = new CornerHandleHit(parameter.PathId, parameter.CornerIndex);
            }
        }
        if (best is { } resolved)
        {
            hit = resolved;
            return true;
        }
        hit = default;
        return false;
    }

    private static Editor2DPoint CornerArrowPoint(Editor2DCornerParameter parameter)
    {
        var source = parameter.SourcePoints;
        var index = parameter.CornerIndex;
        var previous = source[index == 0 ? source.Count - 1 : index - 1];
        var corner = source[index];
        var next = source[index == source.Count - 1 ? 0 : index + 1];
        var incoming = NormalizeVector(previous.X - corner.X, previous.Y - corner.Y);
        var outgoing = NormalizeVector(next.X - corner.X, next.Y - corner.Y);
        var bisector = NormalizeVector(incoming.X + outgoing.X, incoming.Y + outgoing.Y);
        var dot = Math.Clamp((incoming.X * outgoing.X) + (incoming.Y * outgoing.Y), -1, 1);
        var angle = Math.Acos(dot);
        var setback = parameter.Kind == Editor2DCornerKind.Chamfer
            ? parameter.Value
            : parameter.Value / Math.Max(Math.Tan(angle / 2), 1e-6);
        return new Editor2DPoint(corner.X + (bisector.X * setback), corner.Y + (bisector.Y * setback));
    }

    private static bool TryCreateAttachedDimensionMeasurement(
        Editor2DPreviewPath path,
        Editor2DPoint referencePoint,
        out Editor2DMeasurement measurement)
    {
        if (path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase) && path.Points.Count >= 2)
        {
            var lineStart = path.Points[0];
            var lineEnd = path.Points[^1];
            var length = DistanceBetween(lineStart, lineEnd);
            if (length <= 1e-6)
            {
                measurement = default!;
                return false;
            }

            var offsetDistance = Editor2DGeometry.CalculateSignedLineDimensionOffset(
                lineStart,
                lineEnd,
                referencePoint,
                Math.Max(length * 0.12, 8.0));
            if (!Editor2DGeometry.TryBuildAttachedMeasurement(path, "length", offsetDistance, null, out var start, out var end))
            {
                measurement = default!;
                return false;
            }

            measurement = new Editor2DMeasurement(
                Id: Guid.NewGuid().ToString("N"),
                Start: start,
                End: end,
                IsAutoDimension: false,
                EntityPathId: path.Id,
                DimensionType: "length",
                OffsetDistance: offsetDistance);
            return true;
        }

        if ((path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
             || path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase))
            && path.Center is Editor2DPoint center
            && path.Radius is double)
        {
            var placementAngleDegrees = NormalizeAngleDegrees(Math.Atan2(
                referencePoint.Y - center.Y,
                referencePoint.X - center.X) * 180.0 / Math.PI);
            if (!Editor2DGeometry.TryBuildAttachedMeasurement(path, "radius", 0.0, placementAngleDegrees, out var start, out var end))
            {
                measurement = default!;
                return false;
            }

            measurement = new Editor2DMeasurement(
                Id: Guid.NewGuid().ToString("N"),
                Start: start,
                End: end,
                IsAutoDimension: false,
                EntityPathId: path.Id,
                DimensionType: "radius",
                PlacementAngleDegrees: placementAngleDegrees);
            return true;
        }

        measurement = default!;
        return false;
    }

    private bool TryHitTestCornerHandle(
        IReadOnlyList<Editor2DPreviewPath> paths,
        Point screenPoint,
        out CornerHandleHit hit)
    {
        const double hitTolerance = 10.0;
        var bestDistance = double.PositiveInfinity;
        CornerHandleHit? bestHit = null;

        foreach (var path in paths)
        {
            if (!CanCornerEditPath(path))
                continue;

            foreach (var (cornerIndex, point) in EnumerateCornerHandles(path))
            {
                var cornerScreenPoint = WorldToScreen(point, Bounds.Size);
                var distance = Math.Sqrt(Math.Pow(screenPoint.X - cornerScreenPoint.X, 2) + Math.Pow(screenPoint.Y - cornerScreenPoint.Y, 2));
                if (distance <= hitTolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestHit = new CornerHandleHit(path.Id, cornerIndex);
                }
            }
        }

        if (bestHit is CornerHandleHit resolvedHit)
        {
            hit = resolvedHit;
            return true;
        }

        hit = default;
        return false;
    }

    private bool TryBuildTrimTarget(Editor2DPreviewDocument document, Point screenPoint, out TrimTarget trimTarget)
    {
        const double hitTolerancePixels = 12.0;
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
        var hitToleranceWorld = hitTolerancePixels / Math.Max(Zoom, 0.0001);
        var bestDistance = hitToleranceWorld;
        TrimTarget? bestTarget = null;

        foreach (var path in document.Paths)
        {
            if (!CanTrimPath(path))
                continue;

            foreach (var (segmentIndex, start, end) in EnumerateTrimSegments(path))
            {
                var distance = DistancePointToSegment(worldPoint, start, end);
                if (distance > bestDistance)
                    continue;

                if (!TryCreateTrimTargetForSegment(document, path, segmentIndex, start, end, worldPoint, out var candidate))
                    continue;

                bestDistance = distance;
                bestTarget = candidate;
            }
        }

        if (bestTarget is TrimTarget resolvedTarget)
        {
            trimTarget = resolvedTarget;
            return true;
        }

        trimTarget = default;
        return false;
    }

    private static bool TryCreateTrimTargetForSegment(
        Editor2DPreviewDocument document,
        Editor2DPreviewPath sourcePath,
        int segmentIndex,
        Editor2DPoint segmentStart,
        Editor2DPoint segmentEnd,
        Editor2DPoint worldPoint,
        out TrimTarget trimTarget)
    {
        const double epsilon = 1e-6;
        var clickParameter = ProjectPointToSegmentParameter(worldPoint, segmentStart, segmentEnd);
        var cutParameters = new List<double>();

        foreach (var cutterPath in document.Paths)
        {
            if (CanTrimPath(cutterPath))
            {
                foreach (var (cutterSegmentIndex, cutterStart, cutterEnd) in EnumerateTrimSegments(cutterPath))
                {
                    if (string.Equals(cutterPath.Id, sourcePath.Id, StringComparison.Ordinal)
                        && cutterSegmentIndex == segmentIndex
                        && PointsEqual(cutterStart, segmentStart)
                        && PointsEqual(cutterEnd, segmentEnd))
                    {
                        continue;
                    }

                    if (TryGetSegmentIntersectionParameter(segmentStart, segmentEnd, cutterStart, cutterEnd, out var parameter))
                        cutParameters.Add(parameter);
                }
            }

            if (TryGetCircleGeometry(cutterPath, out var circleCenter, out var circleRadius))
            {
                cutParameters.AddRange(GetCircleIntersectionParameters(segmentStart, segmentEnd, circleCenter, circleRadius));
                continue;
            }

            if (TryGetArcGeometry(cutterPath, out var arcCenter, out var arcRadius, out var startAngleDegrees, out var endAngleDegrees))
            {
                foreach (var parameter in GetCircleIntersectionParameters(segmentStart, segmentEnd, arcCenter, arcRadius))
                {
                    var intersectionPoint = InterpolatePoint(segmentStart, segmentEnd, parameter);
                    var angleDegrees = NormalizeAngleDegrees(Math.Atan2(
                        intersectionPoint.Y - arcCenter.Y,
                        intersectionPoint.X - arcCenter.X) * 180.0 / Math.PI);
                    if (IsAngleWithinArcSweep(angleDegrees, startAngleDegrees, endAngleDegrees))
                        cutParameters.Add(parameter);
                }
            }
        }

        var interiorCuts = cutParameters
            .Where(parameter => parameter > epsilon && parameter < 1.0 - epsilon)
            .OrderBy(static parameter => parameter)
            .ToArray();

        var lowParameter = 0.0;
        var highParameter = 1.0;
        foreach (var parameter in interiorCuts)
        {
            if (parameter <= clickParameter && parameter > lowParameter)
                lowParameter = parameter;

            if (parameter >= clickParameter && parameter < highParameter)
                highParameter = parameter;
        }

        if (highParameter - lowParameter <= epsilon)
        {
            trimTarget = default;
            return false;
        }

        trimTarget = new TrimTarget(
            sourcePath.Id,
            segmentIndex,
            lowParameter,
            highParameter,
            segmentStart,
            segmentEnd,
            InterpolatePoint(segmentStart, segmentEnd, lowParameter),
            InterpolatePoint(segmentStart, segmentEnd, highParameter));
        return true;
    }

    private static Editor2DPreviewDocument? ApplyTrim(Editor2DPreviewDocument document, TrimTarget trimTarget)
    {
        var pathIndex = -1;
        for (var index = 0; index < document.Paths.Count; index++)
        {
            if (string.Equals(document.Paths[index].Id, trimTarget.PathId, StringComparison.Ordinal))
            {
                pathIndex = index;
                break;
            }
        }

        if (pathIndex < 0)
            return null;

        var path = document.Paths[pathIndex];
        var replacementPaths = BuildTrimmedPathFragments(path, trimTarget).ToArray();
        var nextPaths = new List<Editor2DPreviewPath>(document.Paths.Count - 1 + replacementPaths.Length);
        for (var index = 0; index < document.Paths.Count; index++)
        {
            if (index == pathIndex)
            {
                nextPaths.AddRange(replacementPaths);
                continue;
            }

            nextPaths.Add(document.Paths[index]);
        }

        return CreateUpdatedDocument(document, nextPaths);
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildTrimmedPathFragments(Editor2DPreviewPath path, TrimTarget trimTarget)
    {
        var points = path.Points.ToArray();
        if (points.Length < 2)
            return [];

        return path.IsClosed
            ? BuildClosedTrimmedPathFragment(path, points, trimTarget)
            : BuildOpenTrimmedPathFragments(path, points, trimTarget);
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildOpenTrimmedPathFragments(
        Editor2DPreviewPath path,
        IReadOnlyList<Editor2DPoint> points,
        TrimTarget trimTarget)
    {
        if (trimTarget.SegmentIndex < 0 || trimTarget.SegmentIndex >= points.Count - 1)
            return [];

        var leftPoints = new List<Editor2DPoint>(points.Take(trimTarget.SegmentIndex + 1));
        if (trimTarget.LowParameter > 1e-6)
            AppendUniquePoint(leftPoints, trimTarget.KillStart);

        var rightPoints = new List<Editor2DPoint>();
        if (trimTarget.HighParameter < 1.0 - 1e-6)
            AppendUniquePoint(rightPoints, trimTarget.KillEnd);

        foreach (var point in points.Skip(trimTarget.SegmentIndex + 1))
            AppendUniquePoint(rightPoints, point);

        var fragments = new List<Editor2DPreviewPath>(2);
        if (HasDrawableSegments(leftPoints))
            fragments.Add(CreateTrimmedPath(path, path.Id, leftPoints, isClosed: false));

        if (HasDrawableSegments(rightPoints))
            fragments.Add(CreateTrimmedPath(path, $"{path.Id}-trim-{Guid.NewGuid():N}", rightPoints, isClosed: false));

        return fragments;
    }

    private static IReadOnlyList<Editor2DPreviewPath> BuildClosedTrimmedPathFragment(
        Editor2DPreviewPath path,
        IReadOnlyList<Editor2DPoint> points,
        TrimTarget trimTarget)
    {
        if (points.Count < 3)
            return [];

        var nextIndex = (trimTarget.SegmentIndex + 1) % points.Count;
        var trimmedPoints = new List<Editor2DPoint> { trimTarget.KillEnd };
        for (var step = 0; step < points.Count; step++)
        {
            var index = (nextIndex + step) % points.Count;
            AppendUniquePoint(trimmedPoints, points[index]);
        }

        AppendUniquePoint(trimmedPoints, trimTarget.KillStart);
        if (!HasDrawableSegments(trimmedPoints))
            return [];

        return [CreateTrimmedPath(path, path.Id, trimmedPoints, isClosed: false)];
    }

    private static Editor2DPreviewPath CreateTrimmedPath(
        Editor2DPreviewPath sourcePath,
        string id,
        IReadOnlyList<Editor2DPoint> points,
        bool isClosed)
        => sourcePath with
        {
            Id = id,
            Points = points.ToArray(),
            IsClosed = isClosed,
            IsAxisAlignedRectangle = Editor2DGeometry.IsAxisAlignedRectangle(points, isClosed),
        };

    private static IEnumerable<(int SegmentIndex, Editor2DPoint Start, Editor2DPoint End)> EnumerateTrimSegments(Editor2DPreviewPath path)
    {
        if (!CanTrimPath(path))
            yield break;

        for (var index = 0; index < path.Points.Count - 1; index++)
            yield return (index, path.Points[index], path.Points[index + 1]);

        if (path.IsClosed && path.Points.Count > 2)
            yield return (path.Points.Count - 1, path.Points[^1], path.Points[0]);
    }

    private static bool CanTrimPath(Editor2DPreviewPath path)
        => path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetSegmentIntersectionParameter(
        Editor2DPoint leftStart,
        Editor2DPoint leftEnd,
        Editor2DPoint rightStart,
        Editor2DPoint rightEnd,
        out double parameter)
    {
        var leftDeltaX = leftEnd.X - leftStart.X;
        var leftDeltaY = leftEnd.Y - leftStart.Y;
        var rightDeltaX = rightEnd.X - rightStart.X;
        var rightDeltaY = rightEnd.Y - rightStart.Y;
        var denominator = (leftDeltaX * rightDeltaY) - (leftDeltaY * rightDeltaX);
        if (Math.Abs(denominator) <= 1e-12)
        {
            parameter = 0.0;
            return false;
        }

        var offsetX = rightStart.X - leftStart.X;
        var offsetY = rightStart.Y - leftStart.Y;
        var leftParameter = ((offsetX * rightDeltaY) - (offsetY * rightDeltaX)) / denominator;
        var rightParameter = ((offsetX * leftDeltaY) - (offsetY * leftDeltaX)) / denominator;
        if (leftParameter < -1e-9
            || leftParameter > 1.0 + 1e-9
            || rightParameter < -1e-9
            || rightParameter > 1.0 + 1e-9)
        {
            parameter = 0.0;
            return false;
        }

        parameter = leftParameter;
        return true;
    }

    private static IEnumerable<double> GetCircleIntersectionParameters(
        Editor2DPoint segmentStart,
        Editor2DPoint segmentEnd,
        Editor2DPoint center,
        double radius)
    {
        var deltaX = segmentEnd.X - segmentStart.X;
        var deltaY = segmentEnd.Y - segmentStart.Y;
        var a = (deltaX * deltaX) + (deltaY * deltaY);
        if (a <= 1e-12)
            yield break;

        var fx = segmentStart.X - center.X;
        var fy = segmentStart.Y - center.Y;
        var b = 2.0 * ((fx * deltaX) + (fy * deltaY));
        var c = (fx * fx) + (fy * fy) - (radius * radius);
        var discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < 0.0)
            yield break;

        var squareRoot = Math.Sqrt(discriminant);
        var low = (-b - squareRoot) / (2.0 * a);
        var high = (-b + squareRoot) / (2.0 * a);
        if (low > 1e-9 && low < 1.0 - 1e-9)
            yield return low;

        if (high > 1e-9 && high < 1.0 - 1e-9 && Math.Abs(high - low) > 1e-9)
            yield return high;
    }

    private static double ProjectPointToSegmentParameter(Editor2DPoint point, Editor2DPoint start, Editor2DPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= 1e-12)
            return 0.0;

        return Math.Clamp(
            (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) / lengthSquared,
            0.0,
            1.0);
    }

    private static double DistancePointToSegment(Editor2DPoint point, Editor2DPoint start, Editor2DPoint end)
    {
        var parameter = ProjectPointToSegmentParameter(point, start, end);
        return DistanceBetween(point, InterpolatePoint(start, end, parameter));
    }

    private static Editor2DPoint InterpolatePoint(Editor2DPoint start, Editor2DPoint end, double parameter)
        => new(
            start.X + ((end.X - start.X) * parameter),
            start.Y + ((end.Y - start.Y) * parameter));

    private static bool HasDrawableSegments(IReadOnlyList<Editor2DPoint> points)
    {
        if (points.Count < 2)
            return false;

        for (var index = 1; index < points.Count; index++)
        {
            if (DistanceBetween(points[index - 1], points[index]) > 1e-6)
                return true;
        }

        return false;
    }

    private static void AppendUniquePoint(List<Editor2DPoint> points, Editor2DPoint point)
    {
        if (points.Count == 0 || !PointsEqual(points[^1], point))
            points.Add(point);
    }

    private static bool PointsEqual(Editor2DPoint left, Editor2DPoint right)
        => DistanceBetween(left, right) <= 1e-6;

    private readonly record struct TrimTarget(
        string PathId,
        int SegmentIndex,
        double LowParameter,
        double HighParameter,
        Editor2DPoint SegmentStart,
        Editor2DPoint SegmentEnd,
        Editor2DPoint KillStart,
        Editor2DPoint KillEnd);

    private static Editor2DPreviewDocument? ApplyCornerEdit(
        Editor2DPreviewDocument document,
        string pathId,
        int cornerIndex,
        string kind)
    {
        var path = document.Paths.FirstOrDefault(candidate => string.Equals(candidate.Id, pathId, StringComparison.Ordinal));
        if (path is null)
            return null;

        var nextPath = BuildCornerEditedPath(path, cornerIndex, kind);
        if (nextPath is null)
            return null;

        var nextPaths = document.Paths
            .Select(candidate => string.Equals(candidate.Id, pathId, StringComparison.Ordinal)
                ? nextPath
                : candidate)
            .ToArray();
        return CreateUpdatedDocument(document, nextPaths);
    }

    private static Editor2DPreviewPath? BuildCornerEditedPath(
        Editor2DPreviewPath path,
        int cornerIndex,
        string kind)
    {
        if (!CanCornerEditPath(path))
            return null;

        var points = path.Points.ToArray();
        var pointCount = points.Length;
        if (cornerIndex < 0 || cornerIndex >= pointCount)
            return null;

        var previousIndex = cornerIndex == 0 ? pointCount - 1 : cornerIndex - 1;
        var nextIndex = cornerIndex == pointCount - 1 ? 0 : cornerIndex + 1;
        if (!path.IsClosed && (cornerIndex == 0 || cornerIndex == pointCount - 1))
            return null;

        var previousPoint = points[previousIndex];
        var cornerPoint = points[cornerIndex];
        var nextPoint = points[nextIndex];
        var firstEdgeLength = DistanceBetween(previousPoint, cornerPoint);
        var secondEdgeLength = DistanceBetween(cornerPoint, nextPoint);
        if (firstEdgeLength <= 1e-6 || secondEdgeLength <= 1e-6)
            return null;

        var defaultValue = CalculateDefaultCornerValue(previousPoint, cornerPoint, nextPoint, kind);
        if (defaultValue <= 1e-6)
            return null;

        var cornerPoints = BuildCornerGeometry(previousPoint, cornerPoint, nextPoint, kind, defaultValue);
        if (cornerPoints.Length == 0)
            return null;

        var nextPoints = new List<Editor2DPoint>(pointCount + cornerPoints.Length - 1);
        for (var index = 0; index < pointCount; index++)
        {
            if (index == cornerIndex)
            {
                nextPoints.AddRange(cornerPoints);
                continue;
            }

            nextPoints.Add(points[index]);
        }

        return path with
        {
            Points = nextPoints.ToArray(),
            IsAxisAlignedRectangle = false,
        };
    }

    private static Editor2DPoint[] BuildCornerGeometry(
        Editor2DPoint previousPoint,
        Editor2DPoint cornerPoint,
        Editor2DPoint nextPoint,
        string kind,
        double requestedValue)
    {
        var towardPrevious = NormalizeVector(previousPoint.X - cornerPoint.X, previousPoint.Y - cornerPoint.Y);
        var towardNext = NormalizeVector(nextPoint.X - cornerPoint.X, nextPoint.Y - cornerPoint.Y);
        var dot = Math.Clamp((towardPrevious.X * towardNext.X) + (towardPrevious.Y * towardNext.Y), -1.0, 1.0);
        var interiorAngle = Math.Acos(dot);
        if (interiorAngle <= 1e-3 || interiorAngle >= Math.PI - 1e-3)
            return [];

        var maximumSetback = Math.Min(DistanceBetween(previousPoint, cornerPoint), DistanceBetween(cornerPoint, nextPoint)) * 0.5;
        if (maximumSetback <= 1e-6)
            return [];

        var tangentSetback = kind.Equals("chamfer", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(requestedValue, maximumSetback)
            : Math.Min(requestedValue / Math.Tan(interiorAngle / 2.0), maximumSetback);
        if (tangentSetback <= 1e-6)
            return [];

        var start = new Editor2DPoint(
            cornerPoint.X + (towardPrevious.X * tangentSetback),
            cornerPoint.Y + (towardPrevious.Y * tangentSetback));
        var end = new Editor2DPoint(
            cornerPoint.X + (towardNext.X * tangentSetback),
            cornerPoint.Y + (towardNext.Y * tangentSetback));
        if (kind.Equals("chamfer", StringComparison.OrdinalIgnoreCase))
            return [start, end];

        var bisector = NormalizeVector(towardPrevious.X + towardNext.X, towardPrevious.Y + towardNext.Y);
        if (Math.Abs(bisector.X) <= 1e-6 && Math.Abs(bisector.Y) <= 1e-6)
            return [start, end];

        var radius = tangentSetback * Math.Tan(interiorAngle / 2.0);
        var centerDistance = radius / Math.Sin(interiorAngle / 2.0);
        var center = new Editor2DPoint(
            cornerPoint.X + (bisector.X * centerDistance),
            cornerPoint.Y + (bisector.Y * centerDistance));
        var startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
        var endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
        var sweep = endAngle - startAngle;
        while (sweep <= -Math.PI)
            sweep += Math.PI * 2.0;

        while (sweep > Math.PI)
            sweep -= Math.PI * 2.0;

        const int segmentCount = 10;
        var points = new Editor2DPoint[segmentCount + 1];
        for (var segmentIndex = 0; segmentIndex <= segmentCount; segmentIndex++)
        {
            var angle = startAngle + (sweep * segmentIndex / segmentCount);
            points[segmentIndex] = new Editor2DPoint(
                center.X + (radius * Math.Cos(angle)),
                center.Y + (radius * Math.Sin(angle)));
        }

        return points;
    }

    private static double CalculateDefaultCornerValue(
        Editor2DPoint previousPoint,
        Editor2DPoint cornerPoint,
        Editor2DPoint nextPoint,
        string kind)
    {
        var firstEdgeLength = DistanceBetween(previousPoint, cornerPoint);
        var secondEdgeLength = DistanceBetween(cornerPoint, nextPoint);
        var maximumSetback = Math.Min(firstEdgeLength, secondEdgeLength) * 0.5;
        if (maximumSetback <= 1e-6)
            return 0.0;

        var presets = new[] { 10.0, 5.0, 2.0 };
        var defaultSetback = presets.FirstOrDefault(preset => preset <= maximumSetback);
        if (defaultSetback <= 1e-6)
            defaultSetback = Math.Max(Math.Min(maximumSetback, 1.0), 0.0);

        if (kind.Equals("chamfer", StringComparison.OrdinalIgnoreCase))
            return defaultSetback;

        var towardPrevious = NormalizeVector(previousPoint.X - cornerPoint.X, previousPoint.Y - cornerPoint.Y);
        var towardNext = NormalizeVector(nextPoint.X - cornerPoint.X, nextPoint.Y - cornerPoint.Y);
        var dot = Math.Clamp((towardPrevious.X * towardNext.X) + (towardPrevious.Y * towardNext.Y), -1.0, 1.0);
        var interiorAngle = Math.Acos(dot);
        if (interiorAngle <= 1e-3 || interiorAngle >= Math.PI - 1e-3)
            return 0.0;

        return defaultSetback * Math.Tan(interiorAngle / 2.0);
    }

    private static (double X, double Y) NormalizeVector(double x, double y)
    {
        var length = Math.Sqrt((x * x) + (y * y));
        return length > 1e-9 ? (x / length, y / length) : (0.0, 0.0);
    }

    private bool IsCornerHandleHovered(string pathId, int cornerIndex)
        => _hasHoverPointerPosition
           && TryHitTestCornerHandle(
               Document?.Paths ?? Array.Empty<Editor2DPreviewPath>(),
               _hoverPointerPosition,
               out var hit)
           && string.Equals(hit.PathId, pathId, StringComparison.Ordinal)
           && hit.CornerIndex == cornerIndex;

    private static IEnumerable<(int CornerIndex, Editor2DPoint Point)> EnumerateCornerHandles(Editor2DPreviewPath path)
    {
        if (!CanCornerEditPath(path))
            yield break;

        if (path.IsClosed)
        {
            for (var index = 0; index < path.Points.Count; index++)
                yield return (index, path.Points[index]);

            yield break;
        }

        for (var index = 1; index < path.Points.Count - 1; index++)
            yield return (index, path.Points[index]);
    }

    private static bool CanCornerEditPath(Editor2DPreviewPath path)
        => path.Points.Count >= 3
           && (path.IsClosed
               || path.Points.Count >= 3)
           && (path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
               || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase)
               || path.IsAxisAlignedRectangle);

    private readonly record struct CornerHandleHit(string PathId, int CornerIndex);

    private void HandleSketchLineClick(Point screenPoint)
    {
        var worldPoint = ResolvePlacementPoint(screenPoint, _pendingLineStart, allowOrthogonal: _pendingLineStart is not null);
        if (_pendingLineStart is null)
        {
            _pendingLineStart = worldPoint;
            _pendingLineEnd = worldPoint;
            InvalidateVisual();
            return;
        }

        var startPoint = _pendingLineStart;
        var nextDocument = AddLineToDocument(startPoint, worldPoint);
        if (nextDocument is not null)
        {
            SetCurrentValue(DocumentProperty, nextDocument);
            var newPathId = nextDocument.Paths[^1].Id;
            SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
        }

        CancelPendingLine();
        InvalidateVisual();
    }

    private void HandleSketchRectangleClick(Point screenPoint)
    {
        var worldPoint = ResolvePlacementPoint(screenPoint);
        if (_pendingRectangleStart is null)
        {
            _pendingRectangleStart = worldPoint;
            _pendingRectangleEnd = worldPoint;
            InvalidateVisual();
            return;
        }

        var startPoint = _pendingRectangleStart;
        if (DataContext is EditorPageViewModel viewModel)
        {
            var newPathId = viewModel.CreateTwoDRectangle(startPoint, worldPoint);
            if (newPathId is not null)
            {
                SetCurrentValue(DocumentProperty, viewModel.TwoDDocument);
                SetCurrentValue(SelectedPathIdsProperty, viewModel.TwoDSelectedPathIds);
                SetCurrentValue(CornerParametersProperty, viewModel.TwoDCornerParameters);
                SetCurrentValue(MeasurementsProperty, viewModel.TwoDMeasurements);
            }
        }
        else
        {
            var nextDocument = AddRectangleToDocument(startPoint, worldPoint);
            if (nextDocument is not null)
            {
                SetCurrentValue(DocumentProperty, nextDocument);
                var newPathId = nextDocument.Paths[^1].Id;
                SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
            }
        }

        CancelPendingRectangle();
        InvalidateVisual();
    }

    private void HandleSketchCircleClick(Point screenPoint)
    {
        var worldPoint = ResolvePlacementPoint(screenPoint);
        if (_pendingCircleCenter is null)
        {
            _pendingCircleCenter = worldPoint;
            _pendingCircleEdge = worldPoint;
            InvalidateVisual();
            return;
        }

        var centerPoint = _pendingCircleCenter;
        var nextDocument = AddCircleToDocument(centerPoint, worldPoint);
        if (nextDocument is not null)
        {
            SetCurrentValue(DocumentProperty, nextDocument);
            var newPathId = nextDocument.Paths[^1].Id;
            SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
        }

        CancelPendingCircle();
        InvalidateVisual();
    }

    private void HandleSketchPolygonClick(Point screenPoint)
    {
        var worldPoint = ResolvePlacementPoint(screenPoint);
        if (_pendingPolygonCenter is null)
        {
            _pendingPolygonCenter = worldPoint;
            _pendingPolygonEdge = worldPoint;
            InvalidateVisual();
            return;
        }

        var centerPoint = _pendingPolygonCenter;
        var nextDocument = AddPolygonToDocument(centerPoint, worldPoint);
        if (nextDocument is not null)
        {
            SetCurrentValue(DocumentProperty, nextDocument);
            var newPathId = nextDocument.Paths[^1].Id;
            SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
        }

        CancelPendingPolygon();
        InvalidateVisual();
    }

    private void HandleSketchTextClick(Point screenPoint)
    {
        if (_isTextEntryActive)
            return;

        var worldPoint = ResolvePlacementPoint(screenPoint);
        if (_pendingTextStart is null)
        {
            _pendingTextStart = worldPoint;
            _pendingTextEnd = worldPoint;
            InvalidateVisual();
            return;
        }

        _pendingTextEnd = worldPoint;
        _pendingTextInitialEntry = TextEntry;
        SetCurrentValue(TextEntryProperty, string.Empty);
        SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        _isTextEntryActive = true;
        Focus();
        InvalidateVisual();
    }

    private bool TryBeginTextEditing(Point screenPoint)
    {
        if (Document is null)
            return false;

        var hitPathId = HitTestPathId(screenPoint);
        var path = Document.Paths.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, hitPathId, StringComparison.Ordinal)
            && candidate.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase));
        if (path is null)
            return false;

        SetCurrentValue(SelectedPathIdsProperty, new[] { path.Id });
        SetCurrentValue(TextEntryProperty, path.Text ?? string.Empty);
        _isTextEntryActive = true;
        Focus();
        return true;
    }

    private void HandlePenPress(Point screenPoint, IPointer pointer)
    {
        var completion = DxfCanvasPenInteraction.GetCompletionForClick(
            _pendingPenAnchors.Select(static anchor => anchor.Point).ToArray(),
            screenPoint,
            point => WorldToScreen(point, Bounds.Size),
            PenCloseHitTolerance);
        if (completion is not null)
        {
            CommitPendingPenPath(isClosed: completion == DxfPenCompletion.Closed);
            return;
        }

        if (_editingPenPathId is not null
            && TryFindPendingPenControl(screenPoint, out var anchorIndex, out var dragControl))
        {
            _pendingPenDragAnchorIndex = anchorIndex;
            _pendingPenDragControl = dragControl;
            _pointerPressPosition = screenPoint;
            pointer.Capture(this);
            return;
        }

        if (_editingPenPathId is not null
            && DxfCanvasPenEditing.TryInsertAnchorAt(
                _pendingPenAnchors,
                ScreenToWorld(screenPoint, Zoom),
                _editingPenClosed,
                12.0 / Math.Max(Zoom, 1e-6),
                out var insertedAnchors,
                out var insertedIndex))
        {
            _pendingPenAnchors = insertedAnchors.ToArray();
            _pendingPenDragAnchorIndex = insertedIndex;
            _pendingPenDragControl = DxfCanvasInteractionSession.PenDragControl.Anchor;
            _pointerPressPosition = screenPoint;
            pointer.Capture(this);
            InvalidateVisual();
            return;
        }

        var worldPoint = ResolvePlacementPoint(
            screenPoint,
            _pendingPenAnchors.LastOrDefault()?.Point,
            allowOrthogonal: _pendingPenAnchors.Count > 0);

        if (_pendingPenAnchors.Count > 0 && DistanceBetween(_pendingPenAnchors[^1].Point, worldPoint) <= 1e-6)
            return;

        _pendingPenAnchors = [.. _pendingPenAnchors, new Editor2DBezierAnchor(worldPoint)];
        _pendingPenDragAnchorIndex = _pendingPenAnchors.Count - 1;
        _pendingPenDragControl = DxfCanvasInteractionSession.PenDragControl.Anchor;
        _pendingPenHoverPoint = worldPoint;
        pointer.Capture(this);
        InvalidateVisual();
    }

    private void UpdatePendingPenHandle(Point screenPoint)
    {
        if (_pendingPenDragAnchorIndex is not int index || index < 0 || index >= _pendingPenAnchors.Count)
            return;
        var screenDelta = screenPoint - _pointerPressPosition;
        if (Math.Abs(screenDelta.X) <= PointerDragThreshold && Math.Abs(screenDelta.Y) <= PointerDragThreshold)
            return;
        var anchors = _pendingPenAnchors.ToArray();
        var nextPoint = ScreenToWorld(screenPoint, Zoom);
        if (_editingPenPathId is not null)
        {
            var previous = anchors[index];
            anchors[index] = _pendingPenDragControl switch
            {
                DxfCanvasInteractionSession.PenDragControl.HandleIn => DxfCanvasPenEditing.MoveHandleIn(previous, nextPoint),
                DxfCanvasInteractionSession.PenDragControl.HandleOut => DxfCanvasPenEditing.MoveHandleOut(previous, nextPoint),
                _ => DxfCanvasPenEditing.MoveAnchor(
                    previous,
                    new Editor2DPoint(nextPoint.X - previous.Point.X, nextPoint.Y - previous.Point.Y)),
            };
        }
        else
        {
            anchors[index] = Editor2DBezierGeometry.CreateSmoothAnchor(anchors[index].Point, nextPoint);
        }
        _pendingPenAnchors = anchors;
        _pendingPenHoverPoint = anchors[^1].Point;
    }

    private void HandleMirrorClick(Point screenPoint, KeyModifiers keyModifiers)
    {
        if (Document is null)
            return;

        if (!TwoDMirrorLineMode)
        {
            ApplyClickSelection(HitTestPathId(screenPoint), keyModifiers.HasFlag(KeyModifiers.Shift));
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            InvalidateVisual();
            return;
        }

        var hitPathId = HitTestPathId(screenPoint);
        var hitPath = Document.Paths.FirstOrDefault(path => string.Equals(path.Id, hitPathId, StringComparison.Ordinal));
        if (hitPath is not null
            && !SelectedPathIds.Contains(hitPath.Id, StringComparer.Ordinal)
            && hitPath.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
            && hitPath.Points.Count >= 2)
        {
            SetCurrentValue(TwoDMirrorAxisStartProperty, hitPath.Start ?? hitPath.Points[0]);
            SetCurrentValue(TwoDMirrorAxisEndProperty, hitPath.Points[^1]);
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            InvalidateVisual();
            return;
        }

        var point = ResolvePlacementPoint(screenPoint);
        if (TwoDMirrorAxisStart is null || TwoDMirrorAxisEnd is not null)
        {
            SetCurrentValue(TwoDMirrorAxisStartProperty, point);
            SetCurrentValue(TwoDMirrorAxisEndProperty, null);
        }
        else if (DistanceBetween(TwoDMirrorAxisStart, point) > 1e-6)
        {
            SetCurrentValue(TwoDMirrorAxisEndProperty, point);
        }
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
    }

    private void HandleContextClick(Point screenPoint)
    {
        var hitManualMeasurementId = HitTestManualMeasurementId(screenPoint);
        if (!string.IsNullOrWhiteSpace(hitManualMeasurementId))
        {
            SetCurrentValue(SelectedMeasurementIdProperty, hitManualMeasurementId);
            SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
            RefreshContextMenuState();
            _contextMenu.Open(this);
            return;
        }

        if (Document is null)
            return;

        var hitPathId = HitTestPathId(screenPoint);
        if (string.IsNullOrWhiteSpace(hitPathId))
        {
            _contextMenu.Close();
            return;
        }

        if (!SelectedPathIds.Contains(hitPathId, StringComparer.Ordinal))
            SetCurrentValue(SelectedPathIdsProperty, new[] { hitPathId });

        SetCurrentValue(SelectedMeasurementIdProperty, null);

        RefreshContextMenuState();
        if (SelectedPathIds.Count == 0)
            return;

        _contextMenu.Open(this);
    }

    private bool TryBeginVertexEdit(Point screenPoint, IPointer pointer)
    {
        if (Document is null || SelectedPathIds.Count == 0)
            return false;

        foreach (var pathId in SelectedPathIds)
        {
            var path = Document.Paths.FirstOrDefault(candidate => string.Equals(candidate.Id, pathId, StringComparison.Ordinal));
            if (path is null)
                continue;

            var vertexIndex = HitTestVertexIndex(path, screenPoint);
            if (vertexIndex is null)
                continue;

            _isEditingVertex = true;
            _editingVertexPathId = path.Id;
            _editingVertexIndex = vertexIndex.Value;
            _editingVertexIsConstrainedRectangle = path.IsAxisAlignedRectangle;
            _cancelInteractionOnPointerRelease = false;
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            pointer.Capture(this);
            return true;
        }

        return false;
    }

    private bool TryBeginScaleSelection(Point screenPoint, IPointer pointer)
    {
        if (Document is null
            || SelectedPathIds.Count == 0
            || !TryGetSelectedPathBounds(Document.Paths, out var minX, out var minY, out var maxX, out var maxY))
        {
            return false;
        }

        var factor = GetScalePreviewFactor();
        var center = new Editor2DPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
        var pivot = ScalePivot ?? (ScaleFromCenter ? center : new Editor2DPoint(minX, minY));
        var handle = new Editor2DPoint(
            pivot.X + ((maxX - pivot.X) * factor),
            pivot.Y + ((maxY - pivot.Y) * factor));
        var handleScreen = WorldToScreen(handle, Bounds.Size);
        var distanceToHandle = Math.Sqrt(Math.Pow(screenPoint.X - handleScreen.X, 2) + Math.Pow(screenPoint.Y - handleScreen.Y, 2));
        if (distanceToHandle > ScaleHandleHitTolerance)
            return false;

        _isScalingSelection = true;
        _scaleDocumentSnapshot = Document;
        _scaleSelectionIds = SelectedPathIds.ToArray();
        _scaleCenterPoint = pivot;
        var startWorldPoint = ScreenToWorld(screenPoint, Zoom);
        _scaleStartDistance = Math.Max(DistanceBetween(pivot, startWorldPoint), 1e-6);
        _scaleStartFactor = factor;
        _scalePreviewFactor = factor;
        _cancelInteractionOnPointerRelease = false;
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        pointer.Capture(this);
        return true;
    }

    private bool TryBeginRotateSelection(Point screenPoint, IPointer pointer)
    {
        if (!ShouldShowSelectionTransformGizmo()
            || Document is null
            || !DxfCanvasTranslationInteraction.TryGetSelectionPivot(Document.Paths, SelectedPathIds, out var pivot))
        {
            return false;
        }

        var pivotScreen = WorldToScreen(pivot, Bounds.Size);
        var handleScreen = DxfCanvasRotationInteraction.GetHandlePosition(pivotScreen);
        if (!DxfCanvasRotationInteraction.IsHandleHit(screenPoint, handleScreen, RotationHandleHitTolerance))
            return false;

        DismissTransformPrecisionInput();
        _isRotatingSelection = true;
        _rotateDocumentSnapshot = Document;
        _rotateSelectionIds = SelectedPathIds.ToArray();
        _rotatePivot = pivot;
        _rotateGrabPoint = screenPoint;
        _rotatePreviewDegrees = 0.0;
        _rotateDragDistancePixels = 0.0;
        _cancelInteractionOnPointerRelease = false;
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        pointer.Capture(this);
        InvalidateVisual();
        return true;
    }

    private bool ShouldShowSelectionTransformGizmo()
        => DxfCanvasSelectionInteraction.ShouldShowTransformGizmo(
            ActiveTool,
            SelectedPathIds.Count > 0,
            ActiveReferenceImage is not null)
            && !(ActiveTool == Editor2DTool.Move && TwoDMovePointToPointActive);

    private bool TryBeginTranslateSelection(Point screenPoint, IPointer pointer)
    {
        if (!ShouldShowSelectionTransformGizmo()
            || Document is null
            || !DxfCanvasTranslationInteraction.TryGetSelectionPivot(Document.Paths, SelectedPathIds, out var pivot))
        {
            return false;
        }

        var pivotScreen = WorldToScreen(pivot, Bounds.Size);
        var handles = DxfCanvasTranslationInteraction.GetHandleGeometry(pivotScreen);
        var handle = DxfCanvasTranslationInteraction.HitTest(
            screenPoint,
            handles,
            TranslationHandleHitTolerance);
        if (handle == DxfCanvasTranslationHandle.None)
            return false;

        DismissTransformPrecisionInput();
        _isTranslatingSelection = true;
        _translateDocumentSnapshot = Document;
        _translateSelectionIds = SelectedPathIds.ToArray();
        _translatePivot = pivot;
        _translateGrabPoint = screenPoint;
        _translatePreviewDelta = new Editor2DPoint(0, 0);
        _translateHandle = handle;
        _translateCreateCopy = ActiveTool == Editor2DTool.Move && TwoDMoveCreateCopy;
        _translateDragDistancePixels = 0.0;
        _cancelInteractionOnPointerRelease = false;
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        pointer.Capture(this);
        InvalidateVisual();
        return true;
    }

    private void StartMoveSelection(Point pointerPosition, KeyModifiers keyModifiers, IPointer pointer)
    {
        if (Document is null)
            return;

        var hitPathId = HitTestPathId(pointerPosition);
        if (string.IsNullOrWhiteSpace(hitPathId))
        {
            if (!keyModifiers.HasFlag(KeyModifiers.Shift))
                SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());

            return;
        }

        IReadOnlyList<string> selectionIds;
        if (SelectedPathIds.Contains(hitPathId, StringComparer.Ordinal))
        {
            selectionIds = SelectedPathIds;
        }
        else
        {
            selectionIds = [hitPathId];
            SetCurrentValue(SelectedPathIdsProperty, selectionIds);
        }

        _isMovingSelection = true;
        _moveDocumentSnapshot = Document;
        _moveSelectionIds = selectionIds.ToArray();
        _moveStartPoint = ScreenToWorld(pointerPosition, Zoom);
        _movePreviewDelta = new Editor2DPoint(0, 0);
        _interaction.MoveSelectionCreateCopy = TwoDMoveCreateCopy;
        _cancelInteractionOnPointerRelease = false;
        pointer.Capture(this);
    }

    private void HandleMovePointToPointClick(Point pointerPosition)
    {
        if (Document is null || SelectedPathIds.Count == 0)
            return;

        var point = ResolvePlacementPoint(pointerPosition);
        if (TwoDMovePointToPointSource is not { } source)
        {
            SetCurrentValue(TwoDMovePointToPointSourceProperty, point);
            InvalidateVisual();
            return;
        }

        RequestSelectionTransform(
            Editor2DAffineTransform.CreateTranslation(point.X - source.X, point.Y - source.Y),
            TwoDMoveCreateCopy);
        SetCurrentValue(TwoDMovePointToPointSourceProperty, null);
        SetCurrentValue(TwoDMovePointToPointActiveProperty, false);
        InvalidateVisual();
    }

    private void ApplyMoveSelection(Point pointerPosition)
    {
        if (_moveDocumentSnapshot is null || _moveStartPoint is null)
            return;

        var currentPoint = ScreenToWorld(pointerPosition, Zoom);
        var deltaX = currentPoint.X - _moveStartPoint.X;
        var deltaY = currentPoint.Y - _moveStartPoint.Y;
        _movePreviewDelta = new Editor2DPoint(deltaX, deltaY);
        InvalidateVisual();
    }

    private void ApplyScaleSelection(Point pointerPosition)
    {
        if (_scaleDocumentSnapshot is null || _scaleCenterPoint is null)
            return;

        var currentPoint = ScreenToWorld(pointerPosition, Zoom);
        var currentDistance = Math.Max(DistanceBetween(_scaleCenterPoint, currentPoint), 1e-6);
        var factor = Math.Max(_scaleStartFactor * currentDistance / Math.Max(_scaleStartDistance, 1e-6), 0.05);
        _scalePreviewFactor = factor;
        SetCurrentValue(ScaleFactorTextProperty, factor.ToString("0.###", CultureInfo.InvariantCulture));
        InvalidateVisual();
    }

    private double GetScalePreviewFactor()
        => _isScalingSelection
            ? _scalePreviewFactor
            : double.IsFinite(ScaleFactor) && ScaleFactor > 0.0 ? ScaleFactor : 1.0;

    private Editor2DPoint? GetSelectionScalePivot(IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (!TryGetSelectedPathBounds(paths, out var minX, out var minY, out var maxX, out var maxY))
            return null;
        return ScalePivot ?? (ScaleFromCenter
            ? new Editor2DPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0)
            : new Editor2DPoint(minX, minY));
    }

    private void ApplyRotateSelection(Point pointerPosition)
    {
        if (_rotatePivot is null || _rotateGrabPoint is null)
            return;

        var pivotScreen = WorldToScreen(_rotatePivot, Bounds.Size);
        _rotatePreviewDegrees = DxfCanvasRotationInteraction.GetGrabRelativeDelta(
            pivotScreen,
            _rotateGrabPoint.Value,
            pointerPosition);
        _rotateDragDistancePixels = ScreenDistance(_rotateGrabPoint.Value, pointerPosition);
        var liveRotation = DxfCanvasTransformPrecisionState.WrapRotation(
            _transformPrecisionState.CumulativeRotation - _rotatePreviewDegrees);
        ShowTransformPrecision(
            DxfCanvasTransformPrecisionKind.Rotation,
            DxfCanvasTransformPrecisionState.FormatRotation(liveRotation),
            _rotatePivot,
            focus: false);
        InvalidateVisual();
    }

    private void ApplyTranslateSelection(Point pointerPosition)
    {
        if (_translateGrabPoint is null)
            return;

        _translatePreviewDelta = DxfCanvasTranslationInteraction.ScreenToWorldDelta(
            pointerPosition - _translateGrabPoint.Value,
            Zoom,
            _translateHandle);
        _translateDragDistancePixels = ScreenDistance(_translateGrabPoint.Value, pointerPosition);
        if (_translateHandle is DxfCanvasTranslationHandle.X or DxfCanvasTranslationHandle.Y)
        {
            var value = _translateHandle == DxfCanvasTranslationHandle.X
                ? _translatePreviewDelta.X
                : _translatePreviewDelta.Y;
            ShowTransformPrecision(
                _translateHandle == DxfCanvasTranslationHandle.X
                    ? DxfCanvasTransformPrecisionKind.X
                    : DxfCanvasTransformPrecisionKind.Y,
                DxfCanvasTransformPrecisionState.FormatTranslation(value),
                _translatePivot,
                focus: false);
        }
        InvalidateVisual();
    }

    private bool CommitTranslateSelection()
    {
        if (_translateDocumentSnapshot is null
            || !DxfCanvasTranslationInteraction.ShouldCommit(
                _translateDragDistancePixels,
                PointerDragThreshold,
                _translatePreviewDelta))
        {
            return false;
        }

        return RequestSelectionTransform(
            Editor2DAffineTransform.CreateTranslation(
                _translatePreviewDelta.X,
                _translatePreviewDelta.Y),
            _translateCreateCopy);
    }

    private void ResetTranslateSelection()
    {
        _isTranslatingSelection = false;
        _translateDocumentSnapshot = null;
        _translateSelectionIds = Array.Empty<string>();
        _translatePivot = null;
        _translateGrabPoint = null;
        _translatePreviewDelta = new Editor2DPoint(0, 0);
        _translateHandle = DxfCanvasTranslationHandle.None;
        _translateCreateCopy = false;
        _translateDragDistancePixels = 0.0;
    }

    private bool CommitRotateSelection()
    {
        if (_rotateDocumentSnapshot is null
            || _rotatePivot is null
            || !DxfCanvasRotationInteraction.ShouldCommit(
                _rotateDragDistancePixels,
                PointerDragThreshold,
                _rotatePreviewDegrees,
                RotationCommitThresholdDegrees))
        {
            return false;
        }

        return RequestSelectionTransform(
            Editor2DAffineTransform.CreateRotation(_rotatePivot, -_rotatePreviewDegrees));
    }

    private bool RequestSelectionTransform(Editor2DAffineTransform transform, bool createCopy = false)
    {
        _isCommittingSelectionTransform = true;
        try
        {
            var request = new DxfCanvasSelectionTransformEventArgs(transform, createCopy);
            SelectionTransformRequested?.Invoke(request);
            if (!request.Committed || request.Document is null)
                return false;

            SetCurrentValue(DocumentProperty, request.Document);
            SetCurrentValue(SelectedPathIdsProperty, request.SelectedPathIds);
            return true;
        }
        finally
        {
            _isCommittingSelectionTransform = false;
        }
    }

    internal bool TryApplyTransformPrecisionInput(string? rawValue)
    {
        if (Document is null
            || _transformPrecisionKind == DxfCanvasTransformPrecisionKind.None
            || GetCurrentSelectionTransformPivot() is not { } pivot)
        {
            return false;
        }

        var nextState = _transformPrecisionState;
        double correction;
        switch (_transformPrecisionKind)
        {
            case DxfCanvasTransformPrecisionKind.X:
            case DxfCanvasTransformPrecisionKind.Y:
                var axis = _transformPrecisionKind == DxfCanvasTransformPrecisionKind.X
                    ? DxfCanvasPrecisionAxis.X
                    : DxfCanvasPrecisionAxis.Y;
                if (!_transformPrecisionState.TryApplyTranslationTotal(axis, rawValue, out nextState, out correction))
                    return false;
                if (Math.Abs(correction) > 1e-12)
                {
                    if (!RequestSelectionTransform(Editor2DAffineTransform.CreateTranslation(
                            axis == DxfCanvasPrecisionAxis.X ? correction : 0.0,
                            axis == DxfCanvasPrecisionAxis.Y ? correction : 0.0)))
                    {
                        return false;
                    }
                }
                _transformPrecisionState = nextState;
                ShowTransformPrecision(
                    _transformPrecisionKind,
                    DxfCanvasTransformPrecisionState.FormatTranslation(
                        axis == DxfCanvasPrecisionAxis.X ? nextState.AppliedX : nextState.AppliedY),
                    GetCurrentSelectionTransformPivot(),
                    focus: true);
                return true;

            case DxfCanvasTransformPrecisionKind.Rotation:
                if (!_transformPrecisionState.TryApplyAbsoluteRotation(rawValue, out nextState, out correction))
                    return false;
                if (Math.Abs(correction) > 1e-12)
                {
                    if (!RequestSelectionTransform(
                            Editor2DAffineTransform.CreateRotation(pivot, correction)))
                    {
                        return false;
                    }
                }
                _transformPrecisionState = nextState;
                ShowTransformPrecision(
                    DxfCanvasTransformPrecisionKind.Rotation,
                    DxfCanvasTransformPrecisionState.FormatRotation(nextState.CumulativeRotation),
                    GetCurrentSelectionTransformPivot(),
                    focus: true);
                return true;

            default:
                return false;
        }
    }

    internal void DismissTransformPrecisionInput(bool exitToSelect = false)
    {
        if (_transformPrecisionKind != DxfCanvasTransformPrecisionKind.None)
        {
            _transformPrecisionKind = DxfCanvasTransformPrecisionKind.None;
            TransformPrecisionDismissed?.Invoke();
        }

        if (exitToSelect)
            SetCurrentValue(ActiveToolProperty, Editor2DTool.Select);
    }

    private void ShowTransformPrecision(
        DxfCanvasTransformPrecisionKind kind,
        string text,
        Editor2DPoint? pivot,
        bool focus)
    {
        if (pivot is null)
            return;

        _transformPrecisionKind = kind;
        var (unit, automationName) = kind switch
        {
            DxfCanvasTransformPrecisionKind.X => ("mm", "X translation"),
            DxfCanvasTransformPrecisionKind.Y => ("mm", "Y translation"),
            DxfCanvasTransformPrecisionKind.Rotation => ("°", "Rotation"),
            _ => (string.Empty, "Transform"),
        };
        TransformPrecisionRequested?.Invoke(new DxfCanvasTransformPrecisionRequest(
            kind,
            text,
            unit,
            automationName,
            WorldToScreen(pivot, Bounds.Size),
            focus));
    }

    private Editor2DPoint? GetCurrentSelectionTransformPivot()
        => Document is not null
           && DxfCanvasTranslationInteraction.TryGetSelectionPivot(
               Document.Paths,
               SelectedPathIds,
               out var pivot)
            ? pivot
            : null;

    private void HandleTransformPrecisionSelectionChanged()
    {
        var selectionKey = string.Join(
            "\u001f",
            SelectedPathIds.Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal));
        if (string.Equals(selectionKey, _transformPrecisionSelectionKey, StringComparison.Ordinal))
            return;

        _transformPrecisionSelectionKey = selectionKey;
        _transformPrecisionState = _transformPrecisionState.ResetForSelection();
        DismissTransformPrecisionInput();
    }

    private void ResetRotateSelection()
    {
        _isRotatingSelection = false;
        _rotateDocumentSnapshot = null;
        _rotateSelectionIds = Array.Empty<string>();
        _rotatePivot = null;
        _rotateGrabPoint = null;
        _rotatePreviewDegrees = 0.0;
        _rotateDragDistancePixels = 0.0;
    }

    private void ApplyVertexEdit(Point pointerPosition)
    {
        if (Document is null || string.IsNullOrWhiteSpace(_editingVertexPathId))
            return;

        var nextPoint = ResolvePlacementPoint(pointerPosition);
        var nextPaths = Document.Paths
            .Select(path =>
            {
                if (!string.Equals(path.Id, _editingVertexPathId, StringComparison.Ordinal))
                    return path;

                var nextPoints = path.Points.ToArray();
                if (_editingVertexIndex < 0 || _editingVertexIndex >= nextPoints.Length)
                    return path;

                nextPoints[_editingVertexIndex] = nextPoint;
                return path with { Points = nextPoints };
            })
            .ToArray();

        SetCurrentValue(DocumentProperty, CreateUpdatedDocument(Document, nextPaths));
        InvalidateVisual();
    }

    private Editor2DPreviewDocument? AddLineToDocument(Editor2DPoint startPoint, Editor2DPoint endPoint)
        => _toolCommitter.Line(Document, startPoint, endPoint);

    private Editor2DPreviewDocument? AddRectangleToDocument(Editor2DPoint startPoint, Editor2DPoint endPoint)
        => _toolCommitter.Rectangle(Document, startPoint, endPoint);

    private Editor2DPreviewDocument? AddCircleToDocument(Editor2DPoint centerPoint, Editor2DPoint edgePoint)
        => _toolCommitter.Circle(Document, centerPoint, edgePoint);

    private Editor2DPreviewDocument? AddPolygonToDocument(Editor2DPoint centerPoint, Editor2DPoint edgePoint)
        => _toolCommitter.Polygon(Document, centerPoint, edgePoint, PolygonSides);

    private Editor2DPreviewDocument? AddTextToDocument(Editor2DPoint startPoint, Editor2DPoint endPoint)
        => _toolCommitter.Text(Document, startPoint, endPoint, DefaultTextValue);

    private Editor2DPreviewDocument? AddPenPathToDocument(IReadOnlyList<Editor2DBezierAnchor> anchors, bool isClosed)
        => _toolCommitter.Pen(Document, anchors, isClosed);

    private void CommitPendingPenPath(bool isClosed)
    {
        if (_pendingPenAnchors.Count < 2)
        {
            CancelPendingPen();
            InvalidateVisual();
            return;
        }

        var resolvedClosed = isClosed && _pendingPenAnchors.Count >= 3;
        var nextDocument = _editingPenPathId is string editingPathId && Document is not null
            ? DxfCanvasPenEditing.ReplacePath(Document, editingPathId, _pendingPenAnchors, resolvedClosed)
            : AddPenPathToDocument(_pendingPenAnchors, resolvedClosed);
        if (nextDocument is not null)
        {
            SetCurrentValue(DocumentProperty, nextDocument);
            var newPathId = nextDocument.Paths[^1].Id;
            SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
            SetCurrentValue(SelectedMeasurementIdProperty, null);
        }

        CancelPendingPen();
        InvalidateVisual();
    }

    private bool TryBeginPenEdit(Point screenPoint)
    {
        var pathId = HitTestPathId(screenPoint);
        if (string.IsNullOrWhiteSpace(pathId) || Document is null)
            return false;

        var path = Document.Paths.FirstOrDefault(candidate => string.Equals(candidate.Id, pathId, StringComparison.Ordinal));
        if (path?.BezierAnchors is not { Count: >= 2 } anchors)
            return false;

        _pendingPenAnchors = anchors.ToArray();
        _editingPenPathId = path.Id;
        _editingPenClosed = path.IsClosed;
        _pendingPenHoverPoint = null;
        _pendingPenDragAnchorIndex = null;
        SetCurrentValue(SelectedPathIdsProperty, new[] { path.Id });
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        SetCurrentValue(ActiveToolProperty, Editor2DTool.Pen);
        InvalidateVisual();
        return true;
    }

    private bool TryFindPendingPenControl(
        Point screenPoint,
        out int index,
        out DxfCanvasInteractionSession.PenDragControl control)
    {
        index = -1;
        control = DxfCanvasInteractionSession.PenDragControl.Anchor;
        var nearestDistance = PenCloseHitTolerance;
        for (var candidate = 0; candidate < _pendingPenAnchors.Count; candidate++)
        {
            var anchor = _pendingPenAnchors[candidate];
            foreach (var handle in new[]
            {
                (Point: DxfCanvasPenEditing.GetHandleInForHit(anchor), Control: DxfCanvasInteractionSession.PenDragControl.HandleIn),
                (Point: anchor.HandleOut, Control: DxfCanvasInteractionSession.PenDragControl.HandleOut),
            })
            {
                if (handle.Point is not Editor2DPoint handlePoint)
                    continue;
                var handleScreen = WorldToScreen(handlePoint, Bounds.Size);
                var handleDistance = ScreenDistance(screenPoint, handleScreen);
                if (handleDistance <= nearestDistance)
                {
                    nearestDistance = handleDistance;
                    index = candidate;
                    control = handle.Control;
                }
            }
        }

        if (index >= 0)
            return true;

        for (var candidate = 0; candidate < _pendingPenAnchors.Count; candidate++)
        {
            var anchorScreen = WorldToScreen(_pendingPenAnchors[candidate].Point, Bounds.Size);
            var distance = ScreenDistance(screenPoint, anchorScreen);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                index = candidate;
                control = DxfCanvasInteractionSession.PenDragControl.Anchor;
            }
        }

        return index >= 0;
    }

    private void ApplyClickSelection(string? pathId, bool isShiftSelection)
    {
        if (string.IsNullOrWhiteSpace(pathId))
        {
            if (!isShiftSelection)
            {
                SetCurrentValue(SelectedMeasurementIdProperty, null);
                SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
            }

            return;
        }

        if (!isShiftSelection)
        {
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            SetCurrentValue(
                SelectedPathIdsProperty,
                ChainSelectionEnabled ? FindConnectedPathIds(pathId) : new[] { pathId });
            return;
        }

        SetCurrentValue(SelectedMeasurementIdProperty, null);
        var selected = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        if (!selected.Add(pathId))
            selected.Remove(pathId);

        SetCurrentValue(SelectedPathIdsProperty, selected.ToArray());
    }

    private IReadOnlyList<string> FindConnectedPathIds(string seedPathId)
    {
        if (Document is null)
            return [seedPathId];

        var paths = Document.Paths
            .Where(path => path.Points.Count >= 2 && !path.IsClosed)
            .ToArray();
        var connected = new HashSet<string>(StringComparer.Ordinal) { seedPathId };
        var frontier = new Queue<string>([seedPathId]);
        while (frontier.Count > 0)
        {
            var currentId = frontier.Dequeue();
            var current = paths.FirstOrDefault(path => path.Id == currentId);
            if (current is null)
                continue;

            var endpoints = new[] { current.Points[0], current.Points[^1] };
            foreach (var candidate in paths)
            {
                if (connected.Contains(candidate.Id))
                    continue;

                var candidateEndpoints = new[] { candidate.Points[0], candidate.Points[^1] };
                if (endpoints.Any(endpoint => candidateEndpoints.Any(other => DistanceBetween(endpoint, other) <= 1e-5)))
                {
                    connected.Add(candidate.Id);
                    frontier.Enqueue(candidate.Id);
                }
            }
        }

        return Document.Paths
            .Where(path => connected.Contains(path.Id))
            .Select(path => path.Id)
            .ToArray();
    }

    private void ApplyMarqueeSelection(Rect selectionRect, bool isShiftSelection)
    {
        if (Document is null)
            return;

        var matchedIds = Document.Paths
            .Where(path => GetScreenBounds(path, Bounds.Size).Intersects(selectionRect))
            .Select(path => path.Id)
            .ToArray();

        if (!isShiftSelection)
        {
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            SetCurrentValue(SelectedPathIdsProperty, matchedIds);
            return;
        }

        SetCurrentValue(SelectedMeasurementIdProperty, null);
        var selected = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        foreach (var id in matchedIds)
            selected.Add(id);

        SetCurrentValue(SelectedPathIdsProperty, selected.ToArray());
    }

    private double CalculateWorldStep(double targetPixels)
        => DxfCanvasViewportTransform.CalculateWorldStep(targetPixels, Zoom);

    private WorldBounds GetVisibleWorldBounds(Size size)
    {
        var bounds = DxfCanvasViewportTransform.VisibleWorldBounds(size, Zoom, OffsetX, OffsetY);
        return new WorldBounds(bounds.Left, bounds.Right, bounds.Bottom, bounds.Top);
    }

    private Point WorldToScreen(Editor2DPoint point, Size size)
        => DxfCanvasViewportTransform.WorldToScreen(point, size, Zoom, OffsetX, OffsetY);

    private Editor2DPoint ScreenToWorld(Point point)
        => ScreenToWorld(point, Zoom);

    private Editor2DPoint ScreenToWorld(Point point, double zoom)
        => DxfCanvasViewportTransform.ScreenToWorld(point, Bounds.Size, zoom, OffsetX, OffsetY);

    private bool IsInsideReferenceImage(Point screenPoint, Editor2DReferenceImage image)
    {
        var local = ReferenceImageLocalPoint(screenPoint, image);
        return Math.Abs(local.X) <= image.Width / 2.0
            && Math.Abs(local.Y) <= image.Height / 2.0;
    }

    private Point ReferenceImageLocalPoint(Point screenPoint, Editor2DReferenceImage image)
    {
        var center = WorldToScreen(new Editor2DPoint(image.X, image.Y), Bounds.Size);
        var dx = screenPoint.X - center.X;
        var dy = screenPoint.Y - center.Y;
        var radians = image.RotationDegrees * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var localX = (cosine * dx - sine * dy) / Math.Max(Zoom, 0.0001);
        var localY = (sine * dx + cosine * dy) / Math.Max(Zoom, 0.0001);
        return new Point(localX, localY);
    }

    private bool TryHitReferenceImageGizmo(Point screenPoint, Editor2DReferenceImage image, out ReferenceImageDragMode mode)
    {
        var local = ReferenceImageLocalPoint(screenPoint, image);
        var tolerance = 10.0 / Math.Max(Zoom, 0.0001);
        var halfWidth = image.Width / 2.0;
        var halfHeight = image.Height / 2.0;
        var rotationHandle = new Point(0, -halfHeight - 24.0 / Math.Max(Zoom, 0.0001));
        if (Math.Sqrt(Math.Pow(local.X - rotationHandle.X, 2) + Math.Pow(local.Y - rotationHandle.Y, 2)) <= tolerance)
        {
            mode = ReferenceImageDragMode.Rotate;
            return true;
        }

        foreach (var corner in new[]
        {
            new Point(-halfWidth, -halfHeight), new Point(halfWidth, -halfHeight),
            new Point(halfWidth, halfHeight), new Point(-halfWidth, halfHeight),
        })
        {
            if (Math.Sqrt(Math.Pow(local.X - corner.X, 2) + Math.Pow(local.Y - corner.Y, 2)) <= tolerance)
            {
                mode = ReferenceImageDragMode.Scale;
                return true;
            }
        }

        foreach (var edge in new[]
        {
            (Point)new Point(0, -halfHeight),
            new Point(halfWidth, 0),
            new Point(0, halfHeight),
            new Point(-halfWidth, 0),
        })
        {
            if (Math.Sqrt(Math.Pow(local.X - edge.X, 2) + Math.Pow(local.Y - edge.Y, 2)) <= tolerance)
            {
                mode = Math.Abs(edge.X) > 0 ? ReferenceImageDragMode.ScaleWidth : ReferenceImageDragMode.ScaleHeight;
                return true;
            }
        }

        mode = ReferenceImageDragMode.Move;
        return IsInsideReferenceImage(screenPoint, image);
    }

    private bool IsSnappingActive => SnapEnabled != _shiftSnapHeld;

    private Editor2DPoint ResolvePlacementPoint(
        Point screenPoint,
        Editor2DPoint? orthogonalReference = null,
        bool allowOrthogonal = false)
    {
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
        if (!IsSnappingActive)
        {
            _activeSnapResult = null;
            return worldPoint;
        }

        _activeSnapResult = _snapResolver.Resolve(
            Document,
            HiddenPathIds,
            worldPoint,
            screenPoint,
            point => WorldToScreen(point, Bounds.Size),
            cornerParameters: CornerParameters);
        if (_activeSnapResult is { } snap)
            return snap.ModelPoint;

        return allowOrthogonal && orthogonalReference is not null
            ? DxfCanvasSnapResolver.ApplyOrthogonalConstraint(orthogonalReference, worldPoint)
            : worldPoint;
    }

    private void UpdateSnapHover(Point screenPoint)
    {
        if (!_hasHoverPointerPosition || Document is null || Zoom <= 0.0)
        {
            _activeSnapResult = null;
            InvalidateVisual();
            return;
        }

        _ = ResolvePlacementPoint(screenPoint);
        InvalidateVisual();
    }

    private void UpdateShiftSnapModifier(KeyModifiers modifiers)
    {
        var shiftHeld = modifiers.HasFlag(KeyModifiers.Shift);
        if (_shiftSnapHeld == shiftHeld)
            return;

        _shiftSnapHeld = shiftHeld;
        _activeSnapResult = null;
    }

    private string? HitTestPathId(Point pointerPosition)
    {
        if (Document is null || Document.Paths.Count == 0 || Zoom <= 0.0)
            return null;

        const double hitTolerance = 8.0;
        var bestDistance = double.PositiveInfinity;
        string? bestPathId = null;
        var worldPoint = ScreenToWorld(pointerPosition, Zoom);

        foreach (var path in Document.Paths)
        {
            if (HiddenPathIds.Contains(path.Id, StringComparer.Ordinal))
                continue;

            if (TryHitTestSemanticPrimitive(path, worldPoint, hitTolerance, out var semanticDistance, out var containsPoint))
            {
                if (containsPoint)
                {
                    bestDistance = 0.0;
                    bestPathId = path.Id;
                    continue;
                }

                if (semanticDistance <= hitTolerance && semanticDistance < bestDistance)
                {
                    bestDistance = semanticDistance;
                    bestPathId = path.Id;
                    continue;
                }
            }

            var screenPoints = path.Points.Select(point => WorldToScreen(point, Bounds.Size)).ToArray();
            var pathDistance = DxfCanvasHitTester.DistanceToPath(pointerPosition, screenPoints, path.IsClosed);
            if (pathDistance <= hitTolerance && pathDistance < bestDistance)
            {
                bestDistance = pathDistance;
                bestPathId = path.Id;
                continue;
            }

            if (path.IsClosed && screenPoints.Length >= 3 && DxfCanvasHitTester.PointInPolygon(pointerPosition, screenPoints))
            {
                bestDistance = 0.0;
                bestPathId = path.Id;
            }
        }

        return bestPathId;
    }

    private IReadOnlyList<Editor2DPreviewPath> GetVisiblePaths()
    {
        return _renderer.VisiblePaths(Document, HiddenPathIds);
    }

    private Rect GetScreenBounds(Editor2DPreviewPath path, Size size)
    {
        if (TryGetCircleGeometry(path, out var circleCenter, out var circleRadius))
        {
            var screenCenter = WorldToScreen(circleCenter, size);
            var screenRadius = circleRadius * Zoom;
            return new Rect(
                screenCenter.X - screenRadius,
                screenCenter.Y - screenRadius,
                screenRadius * 2.0,
                screenRadius * 2.0);
        }

        var points = path.Points.Select(point => WorldToScreen(point, size)).ToArray();
        var minX = points.Min(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxX = points.Max(static point => point.X);
        var maxY = points.Max(static point => point.Y);
        return new Rect(new Point(minX, minY), new Point(maxX, maxY));
    }

    private bool TryDrawSemanticPrimitive(DrawingContext context, Size size, Editor2DPreviewPath path, Pen pen)
    {
        if (TryGetCircleGeometry(path, out var circleCenter, out var circleRadius))
        {
            var screenCenter = WorldToScreen(circleCenter, size);
            var screenRadius = circleRadius * Zoom;
            var rect = new Rect(
                screenCenter.X - screenRadius,
                screenCenter.Y - screenRadius,
                screenRadius * 2.0,
                screenRadius * 2.0);
            context.DrawEllipse(null, pen, rect);
            return true;
        }

        if (TryGetArcGeometry(path, out var arcCenter, out var arcRadius, out var startAngleDegrees, out var endAngleDegrees))
        {
            var startPoint = WorldToScreen(PointOnCircle(arcCenter, arcRadius, startAngleDegrees), size);
            var endPoint = WorldToScreen(PointOnCircle(arcCenter, arcRadius, endAngleDegrees), size);
            var sweep = NormalizeAngleSweepDegrees(startAngleDegrees, endAngleDegrees);
            var geometry = new StreamGeometry();
            using (var geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(startPoint, false);
                geometryContext.ArcTo(
                    endPoint,
                    new Size(arcRadius * Zoom, arcRadius * Zoom),
                    0.0,
                    sweep > 180.0,
                    SweepDirection.CounterClockwise);
            }

            context.DrawGeometry(null, pen, geometry);
            return true;
        }

        if (TryGetTextGeometry(path, out var textStart, out var textValue, out var textHeight, out var rotationDegrees, out var widthFactor))
        {
            var screenStart = WorldToScreen(textStart, size);
            var fontSize = Math.Max(textHeight * Zoom, 8.0);
            var typeface = new Typeface(
                SelectedPathIds.Contains(path.Id, StringComparer.Ordinal)
                    && !string.IsNullOrWhiteSpace(TextFontPreview)
                    ? TextFontPreview!
                    : string.IsNullOrWhiteSpace(path.FontFamily) ? "Inter, Segoe UI, Arial" : path.FontFamily,
                path.IsItalic ? FontStyle.Italic : FontStyle.Normal,
                path.IsBold ? FontWeight.Bold : FontWeight.Normal,
                FontStretch.Normal);
            var formattedText = new FormattedText(
                textValue,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                pen.Brush ?? OpenPathPen.Brush);
            var characterCount = textValue.Count(character => character is not '\r' and not '\n');
            var spacingPixels = Math.Max(characterCount - 1, 0) * path.CharacterSpacing * Zoom;
            var spacingScale = formattedText.Width > 0
                ? Math.Max((formattedText.Width + spacingPixels) / formattedText.Width, 0.1)
                : 1.0;

            using var transform = context.PushTransform(
                Matrix.CreateTranslation(screenStart.X, screenStart.Y)
                * Matrix.CreateRotation(-rotationDegrees * Math.PI / 180.0)
                * Matrix.CreateScale(widthFactor * spacingScale, 1.0));
            context.DrawText(formattedText, new Point(0.0, -formattedText.Height));
            if (path.IsUnderline)
            {
                context.DrawLine(
                    new Pen(pen.Brush ?? OpenPathPen.Brush, Math.Max(1, fontSize / 14)),
                    new Point(0, 1),
                    new Point(formattedText.Width, 1));
            }
            return true;
        }

        return false;
    }

    private bool TryHitTestSemanticPrimitive(
        Editor2DPreviewPath path,
        Editor2DPoint worldPoint,
        double hitTolerance,
        out double distance,
        out bool containsPoint)
    {
        if (TryGetCircleGeometry(path, out var circleCenter, out var circleRadius))
        {
            var centerDistance = DistanceBetween(worldPoint, circleCenter);
            containsPoint = centerDistance <= circleRadius;
            distance = Math.Abs(centerDistance - circleRadius) * Zoom;
            return true;
        }

        if (TryGetArcGeometry(path, out var arcCenter, out var arcRadius, out var startAngleDegrees, out var endAngleDegrees))
        {
            containsPoint = false;
            var deltaX = worldPoint.X - arcCenter.X;
            var deltaY = worldPoint.Y - arcCenter.Y;
            var radialDistance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            var pointAngleDegrees = NormalizeAngleDegrees(Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI);
            if (IsAngleWithinArcSweep(pointAngleDegrees, startAngleDegrees, endAngleDegrees))
            {
                distance = Math.Abs(radialDistance - arcRadius) * Zoom;
                return true;
            }

            var startPoint = PointOnCircle(arcCenter, arcRadius, startAngleDegrees);
            var endPoint = PointOnCircle(arcCenter, arcRadius, endAngleDegrees);
            distance = Math.Min(
                DistanceBetween(worldPoint, startPoint) * Zoom,
                DistanceBetween(worldPoint, endPoint) * Zoom);
            return true;
        }

        if (TryGetTextGeometry(path, out _, out _, out _, out _, out _))
        {
            var polygon = path.Points
                .Select(static point => new Point(point.X, point.Y))
                .ToArray();
            var hitPoint = new Point(worldPoint.X, worldPoint.Y);
            containsPoint = polygon.Length >= 3 && DxfCanvasHitTester.PointInPolygon(hitPoint, polygon);
            distance = polygon.Length >= 2
                ? DxfCanvasHitTester.DistanceToPath(hitPoint, polygon, isClosed: true) * Zoom
                : double.PositiveInfinity;
            return true;
        }

        distance = double.PositiveInfinity;
        containsPoint = false;
        return false;
    }

    private static bool TryGetCircleGeometry(Editor2DPreviewPath path, out Editor2DPoint center, out double radius)
    {
        if (path.EntityType.Equals("CIRCLE", StringComparison.OrdinalIgnoreCase)
            && path.Center is Editor2DPoint resolvedCenter
            && path.Radius is double resolvedRadius
            && resolvedRadius > 1e-9)
        {
            center = resolvedCenter;
            radius = resolvedRadius;
            return true;
        }

        center = default!;
        radius = 0.0;
        return false;
    }

    private static bool TryGetTextGeometry(
        Editor2DPreviewPath path,
        out Editor2DPoint start,
        out string text,
        out double textHeight,
        out double rotationDegrees,
        out double widthFactor)
    {
        if (path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
            && path.Start is Editor2DPoint resolvedStart
            && !string.IsNullOrWhiteSpace(path.Text))
        {
            start = resolvedStart;
            text = path.Text!;
            textHeight = Math.Max(path.TextHeight ?? 5.0, 0.1);
            rotationDegrees = path.RotationDegrees ?? 0.0;
            var resolvedWidthFactor = path.WidthFactor ?? 1.0;
            widthFactor = resolvedWidthFactor < 0.0
                ? -Math.Max(Math.Abs(resolvedWidthFactor), 0.1)
                : Math.Max(resolvedWidthFactor, 0.1);
            return true;
        }

        start = default!;
        text = string.Empty;
        textHeight = 0.0;
        rotationDegrees = 0.0;
        widthFactor = 1.0;
        return false;
    }

    private static bool TryGetArcGeometry(
        Editor2DPreviewPath path,
        out Editor2DPoint center,
        out double radius,
        out double startAngleDegrees,
        out double endAngleDegrees)
    {
        if (path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
            && path.Center is Editor2DPoint resolvedCenter
            && path.Radius is double resolvedRadius
            && path.StartAngleDegrees is double resolvedStartAngle
            && path.EndAngleDegrees is double resolvedEndAngle
            && resolvedRadius > 1e-9)
        {
            center = resolvedCenter;
            radius = resolvedRadius;
            startAngleDegrees = resolvedStartAngle;
            endAngleDegrees = resolvedEndAngle;
            return true;
        }

        center = default!;
        radius = 0.0;
        startAngleDegrees = 0.0;
        endAngleDegrees = 0.0;
        return false;
    }

    private static Editor2DPoint PointOnCircle(Editor2DPoint center, double radius, double angleDegrees)
    {
        var angleRadians = angleDegrees * Math.PI / 180.0;
        return new Editor2DPoint(
            center.X + (radius * Math.Cos(angleRadians)),
            center.Y + (radius * Math.Sin(angleRadians)));
    }

    private static double NormalizeAngleDegrees(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        return normalized < 0.0 ? normalized + 360.0 : normalized;
    }

    private static double NormalizeAngleSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = NormalizeAngleDegrees(endAngleDegrees) - NormalizeAngleDegrees(startAngleDegrees);
        while (sweep <= 0.0)
            sweep += 360.0;

        return sweep;
    }

    private static bool IsAngleWithinArcSweep(double angleDegrees, double startAngleDegrees, double endAngleDegrees)
    {
        var normalizedStart = NormalizeAngleDegrees(startAngleDegrees);
        var normalizedAngle = NormalizeAngleDegrees(angleDegrees);
        var sweep = NormalizeAngleSweepDegrees(startAngleDegrees, endAngleDegrees);
        var relative = normalizedAngle - normalizedStart;
        while (relative < 0.0)
            relative += 360.0;

        return relative <= sweep + 1e-6;
    }

    private void CancelPendingMeasurement()
    {
        _pendingMeasurementStart = null;
        _pendingMeasurementEnd = null;
    }

    private void CancelPendingDimension()
    {
        _pendingDimensionStart = null;
        _pendingDimensionEnd = null;
    }

    private void CancelPendingLine()
    {
        _pendingLineStart = null;
        _pendingLineEnd = null;
    }

    private void CancelPendingRectangle()
    {
        _pendingRectangleStart = null;
        _pendingRectangleEnd = null;
    }

    private void CancelPendingCircle()
    {
        _pendingCircleCenter = null;
        _pendingCircleEdge = null;
    }

    private void CancelPendingPolygon()
    {
        _pendingPolygonCenter = null;
        _pendingPolygonEdge = null;
    }

    private void CancelPendingText()
    {
        _pendingTextStart = null;
        _pendingTextEnd = null;
        _pendingTextInitialEntry = null;
        _isTextEntryActive = false;
    }

    private void CancelPendingPen()
    {
        _pendingPenAnchors = Array.Empty<Editor2DBezierAnchor>();
        _pendingPenHoverPoint = null;
        _pendingPenDragAnchorIndex = null;
        _editingPenPathId = null;
        _editingPenClosed = false;
    }

    private void CancelPendingMirror()
    {
        SetCurrentValue(TwoDMirrorAxisStartProperty, null);
        SetCurrentValue(TwoDMirrorAxisEndProperty, null);
    }

    private void CancelMarqueeSelection()
    {
        _isMarqueeSelecting = false;
        _marqueeStartPoint = null;
        _marqueeCurrentPoint = null;
    }

    private void RefreshContextMenuState()
    {
        var hasMeasurementSelection = !string.IsNullOrWhiteSpace(SelectedMeasurementId);
        var hasSelection = SelectedPathIds.Count > 0;
        var selectedIds = hasSelection
            ? new HashSet<string>(SelectedPathIds, StringComparer.Ordinal)
            : null;
        var canExpandRectangles = Document is not null
            && selectedIds is not null
            && Document.Paths.Any(path => path.IsAxisAlignedRectangle && selectedIds.Contains(path.Id));
        var canExplodeCompound = Document is not null
            && selectedIds is not null
            && Document.Paths.Any(path => selectedIds.Contains(path.Id)
                && path.IsClosed
                && (path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
                    || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase)));
        var canStrokeToFill = Document is not null
            && selectedIds is not null
            && Document.Paths.Any(path => selectedIds.Contains(path.Id) && path.IsClosed && !path.IsFilled);
        var canFillToStroke = Document is not null
            && selectedIds is not null
            && Document.Paths.Any(path => selectedIds.Contains(path.Id) && path.IsClosed && path.IsFilled);

        _expandRectanglesMenuItem.IsVisible = !hasMeasurementSelection && canExpandRectangles;
        _duplicateSelectionMenuItem.IsVisible = !hasMeasurementSelection && hasSelection;
        _flipHorizontalMenuItem.IsVisible = !hasMeasurementSelection && hasSelection;
        _flipVerticalMenuItem.IsVisible = !hasMeasurementSelection && hasSelection;
        var canBoolean = !hasMeasurementSelection
            && Document is not null
            && selectedIds is not null
            && Document.Paths.Count(path => selectedIds.Contains(path.Id) && path.IsClosed && path.Points.Count >= 3) >= 2;
        _unionMenuItem.IsVisible = canBoolean;
        _subtractMenuItem.IsVisible = canBoolean;
        _intersectMenuItem.IsVisible = canBoolean;
        var canConvertLines = !hasMeasurementSelection
            && DataContext is EditorPageViewModel { CanApplyTwoDConvertLines: true };
        _convertLinesMenuItem.IsVisible = canConvertLines;
        _reloadFromDiskMenuItem.IsVisible = !hasMeasurementSelection
            && DataContext is EditorPageViewModel { HasSelectedTwoDImportGroup: true };
        _explodeCompoundMenuItem.IsVisible = !hasMeasurementSelection && canExplodeCompound;
        _strokeToFillMenuItem.IsVisible = !hasMeasurementSelection && canStrokeToFill;
        _fillToStrokeMenuItem.IsVisible = !hasMeasurementSelection && canFillToStroke;
        _breakMirrorLinkMenuItem.IsVisible = !hasMeasurementSelection
            && DataContext is EditorPageViewModel { HasTwoDMirrorLinkSelection: true };
        _deleteSelectionMenuItem.IsVisible = hasMeasurementSelection || hasSelection;
    }

    private string? HitTestManualMeasurementId(Point screenPoint)
    {
        const double hitTolerance = 10.0;
        var bestDistance = double.PositiveInfinity;
        string? bestMeasurementId = null;

        foreach (var measurement in Measurements)
        {
            if (measurement.IsAutoDimension)
                continue;

            var start = WorldToScreen(measurement.Start, Bounds.Size);
            var end = WorldToScreen(measurement.End, Bounds.Size);
            var distance = DxfCanvasHitTester.DistanceToSegment(screenPoint, start, end);
            distance = Math.Min(distance, Math.Sqrt(Math.Pow(screenPoint.X - start.X, 2) + Math.Pow(screenPoint.Y - start.Y, 2)));
            distance = Math.Min(distance, Math.Sqrt(Math.Pow(screenPoint.X - end.X, 2) + Math.Pow(screenPoint.Y - end.Y, 2)));
            if (distance <= hitTolerance && distance < bestDistance)
            {
                bestDistance = distance;
                bestMeasurementId = measurement.Id;
            }
        }

        return bestMeasurementId;
    }

    private bool TryHitManualMeasurementEndpoint(Point screenPoint, out string measurementId, out bool start)
    {
        const double hitTolerance = 10.0;
        var bestDistance = double.PositiveInfinity;
        measurementId = string.Empty;
        start = false;
        foreach (var measurement in Measurements)
        {
            if (measurement.IsAutoDimension)
                continue;

            var startScreen = WorldToScreen(measurement.Start, Bounds.Size);
            var endScreen = WorldToScreen(measurement.End, Bounds.Size);
            var startDistance = ScreenDistance(screenPoint, startScreen);
            var endDistance = ScreenDistance(screenPoint, endScreen);
            if (startDistance <= hitTolerance && startDistance < bestDistance)
            {
                bestDistance = startDistance;
                measurementId = measurement.Id;
                start = true;
            }
            if (endDistance <= hitTolerance && endDistance < bestDistance)
            {
                bestDistance = endDistance;
                measurementId = measurement.Id;
                start = false;
            }
        }

        return measurementId.Length > 0;
    }

    private int? HitTestVertexIndex(Editor2DPreviewPath path, Point screenPoint)
    {
        if (path.IsAxisAlignedRectangle)
            return HitTestVertexIndex(path.Points, screenPoint);

        if (!CanEditVertices(path))
            return null;

        return HitTestVertexIndex(path.Points, screenPoint);
    }

    private int? HitTestVertexIndex(IReadOnlyList<Editor2DPoint> points, Point screenPoint)
    {
        const double hitTolerance = 7.0;
        for (var index = 0; index < points.Count; index++)
        {
            var vertexScreenPoint = WorldToScreen(points[index], Bounds.Size);
            if (Math.Sqrt(Math.Pow(screenPoint.X - vertexScreenPoint.X, 2) + Math.Pow(screenPoint.Y - vertexScreenPoint.Y, 2)) <= hitTolerance)
                return index;
        }

        return null;
    }

    private static Rect GetSelectionRect(Point start, Point end)
        => new(
            new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)),
            new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));

    private static Editor2DPoint[] BuildRectanglePoints(Editor2DPoint startPoint, Editor2DPoint endPoint)
        => DxfCanvasGeometryEditor.BuildRectangle(startPoint, endPoint);

    private static Editor2DPoint[] BuildCirclePoints(Editor2DPoint centerPoint, double radius)
        => DxfCanvasGeometryEditor.BuildCircle(centerPoint, radius);

    private static Editor2DPoint[] BuildPolygonPoints(Editor2DPoint centerPoint, Editor2DPoint edgePoint, int sides)
        => DxfCanvasGeometryEditor.BuildPolygon(centerPoint, edgePoint, sides);

    private static bool CanEditVertices(Editor2DPreviewPath path)
        => path.EntityType.Equals("LINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase)
           || path.EntityType.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase);

    private void DeleteSelectedPaths()
    {
        if (Document is null || SelectedPathIds.Count == 0)
            return;

        var selectedIds = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        var nextPaths = Document.Paths
            .Where(path => !selectedIds.Contains(path.Id))
            .ToArray();

        SetCurrentValue(DocumentProperty, CreateUpdatedDocument(Document, nextPaths));
        SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
    }

    private bool DeleteSelectedMeasurement()
    {
        if (string.IsNullOrWhiteSpace(SelectedMeasurementId))
            return false;

        var nextMeasurements = Measurements
            .Where(measurement => !string.Equals(measurement.Id, SelectedMeasurementId, StringComparison.Ordinal))
            .ToArray();
        SetCurrentValue(MeasurementsProperty, nextMeasurements);
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
        return true;
    }

    private void ExpandSelectedRectangles()
    {
        if (Document is null || SelectedPathIds.Count == 0)
            return;

        var selectedIds = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        var nextPaths = Document.Paths
            .Select(path => !selectedIds.Contains(path.Id) || !path.IsAxisAlignedRectangle
                ? path
                : path with { IsAxisAlignedRectangle = false })
            .ToArray();

        SetCurrentValue(DocumentProperty, CreateUpdatedDocument(Document, nextPaths));
        InvalidateVisual();
    }

    private static Editor2DPreviewDocument TranslatePaths(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedIds,
        double deltaX,
        double deltaY)
        => DxfCanvasGeometryEditor.Translate(document, selectedIds, deltaX, deltaY);

    private static Editor2DPreviewDocument? DuplicateSelectedPaths(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedIds,
        out IReadOnlyList<string> copySelectionIds)
    {
        var selected = selectedIds.ToHashSet(StringComparer.Ordinal);
        var copies = document.Paths
            .Where(path => selected.Contains(path.Id))
            .Select(path => CopyPath(path, $"{path.Id}:copy:{Guid.NewGuid():N}"))
            .ToArray();
        copySelectionIds = copies.Select(path => path.Id).ToArray();
        return copies.Length == 0
            ? null
            : CreateUpdatedDocument(document, [.. document.Paths, .. copies]);
    }

    private static Editor2DPreviewPath CopyPath(Editor2DPreviewPath path, string id)
        => path with { Id = id };

    private static Editor2DPreviewDocument ScalePaths(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedIds,
        Editor2DPoint center,
        double factor)
        => DxfCanvasGeometryEditor.Scale(document, selectedIds, center, factor);

    private static Editor2DPreviewDocument CreateUpdatedDocument(
        Editor2DPreviewDocument document,
        IReadOnlyList<Editor2DPreviewPath> nextPaths)
        => DxfCanvasGeometryEditor.Update(document, nextPaths);

    private static double DistanceBetween(Editor2DPoint left, Editor2DPoint right)
        => DxfCanvasGeometryEditor.Distance(left, right);

    private static double ScreenDistance(Point left, Point right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2));

    private bool TryGetSelectedPathBounds(
        IReadOnlyList<Editor2DPreviewPath> paths,
        out double minX,
        out double minY,
        out double maxX,
        out double maxY)
    {
        var selectedIds = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        var points = paths
            .Where(path => selectedIds.Contains(path.Id))
            .SelectMany(static path => path.Points)
            .ToArray();
        if (points.Length == 0)
        {
            minX = minY = maxX = maxY = 0.0;
            return false;
        }

        minX = points.Min(static point => point.X);
        minY = points.Min(static point => point.Y);
        maxX = points.Max(static point => point.X);
        maxY = points.Max(static point => point.Y);
        return true;
    }

    private static Editor2DBounds MeasureBounds(IReadOnlyList<Editor2DPreviewPath> paths)
        => DxfCanvasGeometryEditor.MeasureBounds(paths);

    private readonly record struct WorldBounds(double Left, double Right, double Bottom, double Top);
}
