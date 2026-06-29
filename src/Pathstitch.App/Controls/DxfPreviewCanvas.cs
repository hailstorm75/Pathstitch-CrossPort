using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Domain.App.Models;

namespace Pathstitch.App.Controls;

public sealed class DxfPreviewCanvas : Control
{
    private const double PointerDragThreshold = 4.0;
    private const double PenCloseHitTolerance = 10.0;
    private const double ScaleHandleHitTolerance = 12.0;
    private const double EmptyWorkspaceSpan = 200.0;
    private const double DefaultTextHeight = 10.0;
    private const string DefaultTextValue = "Label";

    public static readonly StyledProperty<Editor2DPreviewDocument?> DocumentProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DPreviewDocument?>(nameof(Document));

    public static readonly StyledProperty<Editor2DTool> ActiveToolProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, Editor2DTool>(
            nameof(ActiveTool),
            defaultValue: Editor2DTool.Select,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<string>> SelectedPathIdsProperty =
        AvaloniaProperty.Register<DxfPreviewCanvas, IReadOnlyList<string>>(
            nameof(SelectedPathIds),
            defaultValue: Array.Empty<string>(),
            defaultBindingMode: BindingMode.TwoWay);

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

    private static readonly Pen MinorGridPen = new(new SolidColorBrush(Color.Parse("#13161D")), 1);
    private static readonly Pen MajorGridPen = new(new SolidColorBrush(Color.Parse("#1D2430")), 1);
    private static readonly Pen AxisPen = new(new SolidColorBrush(Color.Parse("#2D3B55")), 1.25);
    private static readonly Pen PaperBorderPen = new(new SolidColorBrush(Color.Parse("#243042")), 1);
    private static readonly Pen ClosedPathPen = new(new SolidColorBrush(Color.Parse("#E8ECF6")), 1.4);
    private static readonly Pen OpenPathPen = new(new SolidColorBrush(Color.Parse("#F5B35C")), 1.4);
    private static readonly Pen HoverPathPen = new(new SolidColorBrush(Color.Parse("#8EB3FF")), 2.0);
    private static readonly Pen SelectedPathPen = new(new SolidColorBrush(Color.Parse("#4D7FFF")), 2.4);
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
    private static readonly Typeface TextEntityTypeface = new("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
    private static readonly IBrush PaperFillBrush = new SolidColorBrush(Color.Parse("#11161F"));
    private static readonly IBrush AutoDimensionTextBrush = new SolidColorBrush(Color.Parse("#D8F5FF"));
    private static readonly IBrush AutoDimensionLabelFillBrush = new SolidColorBrush(Color.Parse("#C0121F2B"));
    private static readonly IBrush ConstrainedRectangleHandleFillBrush = new SolidColorBrush(Color.Parse("#CC10151F"));
    private static readonly IBrush EditableVertexHandleFillBrush = new SolidColorBrush(Color.Parse("#4D7FFF"));
    private static readonly IBrush MeasurementPointBrush = new SolidColorBrush(Color.Parse("#7BDCB5"));
    private static readonly IBrush MeasurementTextBrush = new SolidColorBrush(Color.Parse("#DDF9EE"));
    private static readonly IBrush MeasurementLabelFillBrush = new SolidColorBrush(Color.Parse("#C010241D"));
    private static readonly IBrush LiveMeasurementPointBrush = new SolidColorBrush(Color.Parse("#B0F5DA"));
    private static readonly IBrush CornerToolHandleBrush = new SolidColorBrush(Color.Parse("#F5B35C"));
    private static readonly IBrush MarqueeFillBrush = new SolidColorBrush(Color.Parse("#224D7FFF"));

    private readonly ContextMenu _contextMenu;
    private readonly MenuItem _expandRectanglesMenuItem;
    private readonly MenuItem _deleteSelectionMenuItem;
    private bool _isPanning;
    private bool _isMarqueeSelecting;
    private bool _isMovingSelection;
    private bool _isScalingSelection;
    private bool _isAwaitingSecondaryContextClick;
    private bool _isEditingVertex;
    private Point _lastPointerPosition;
    private Point _pointerPressPosition;
    private string? _hoveredPathId;
    private string? _pressedPathId;
    private Point? _marqueeStartPoint;
    private Point? _marqueeCurrentPoint;
    private Editor2DPreviewDocument? _moveDocumentSnapshot;
    private Editor2DPreviewDocument? _scaleDocumentSnapshot;
    private IReadOnlyList<string> _moveSelectionIds = Array.Empty<string>();
    private IReadOnlyList<string> _scaleSelectionIds = Array.Empty<string>();
    private Editor2DPoint? _moveStartPoint;
    private Editor2DPoint? _scaleCenterPoint;
    private double _scaleStartDistance;
    private double _scalePreviewFactor = 1.0;
    private string? _editingVertexPathId;
    private int _editingVertexIndex;
    private bool _editingVertexIsConstrainedRectangle;
    private Editor2DPoint? _pendingLineStart;
    private Editor2DPoint? _pendingLineEnd;
    private Editor2DPoint? _pendingRectangleStart;
    private Editor2DPoint? _pendingRectangleEnd;
    private Editor2DPoint? _pendingCircleCenter;
    private Editor2DPoint? _pendingCircleEdge;
    private Editor2DPoint? _pendingPolygonCenter;
    private Editor2DPoint? _pendingPolygonEdge;
    private Editor2DPoint? _pendingTextStart;
    private Editor2DPoint? _pendingTextEnd;
    private IReadOnlyList<Editor2DPoint> _pendingPenPoints = Array.Empty<Editor2DPoint>();
    private Editor2DPoint? _pendingPenHoverPoint;
    private Editor2DPoint? _pendingMirrorAxisStart;
    private Editor2DPoint? _pendingMirrorAxisEnd;
    private Editor2DPoint? _pendingMeasurementStart;
    private Editor2DPoint? _pendingMeasurementEnd;
    private Editor2DPoint? _pendingDimensionStart;
    private Editor2DPoint? _pendingDimensionEnd;
    private Point _hoverPointerPosition;
    private bool _hasHoverPointerPosition;
    private bool _pendingFrameToDocument;
    private bool _cancelInteractionOnPointerRelease;

    static DxfPreviewCanvas()
    {
        AffectsRender<DxfPreviewCanvas>(
            DocumentProperty,
            ActiveToolProperty,
            SelectedPathIdsProperty,
            MeasurementsProperty,
            SelectedMeasurementIdProperty,
            ZoomProperty,
            OffsetXProperty,
            OffsetYProperty,
            PolygonSidesProperty);
        ClipToBoundsProperty.OverrideDefaultValue<DxfPreviewCanvas>(true);
        FocusableProperty.OverrideDefaultValue<DxfPreviewCanvas>(true);
    }

    public DxfPreviewCanvas()
    {
        _expandRectanglesMenuItem = new MenuItem
        {
            Header = "Expand",
        };
        _expandRectanglesMenuItem.Click += OnExpandRectanglesMenuItemClick;

        _deleteSelectionMenuItem = new MenuItem
        {
            Header = "Delete",
        };
        _deleteSelectionMenuItem.Click += OnDeleteSelectionMenuItemClick;

        _contextMenu = new ContextMenu
        {
            Placement = PlacementMode.Pointer,
            ItemsSource = new Control[]
            {
                _expandRectanglesMenuItem,
                _deleteSelectionMenuItem,
            },
        };
    }

    public Editor2DPreviewDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public Editor2DTool ActiveTool
    {
        get => GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    public IReadOnlyList<string> SelectedPathIds
    {
        get => GetValue(SelectedPathIdsProperty);
        set => SetValue(SelectedPathIdsProperty, value);
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
        _contextMenu.Close();
        _cancelInteractionOnPointerRelease = true;
        _isAwaitingSecondaryContextClick = false;
        _isEditingVertex = false;
        _isPanning = false;
        _isMovingSelection = false;
        _isScalingSelection = false;
        _moveDocumentSnapshot = null;
        _scaleDocumentSnapshot = null;
        _moveSelectionIds = Array.Empty<string>();
        _scaleSelectionIds = Array.Empty<string>();
        _moveStartPoint = null;
        _scaleCenterPoint = null;
        _scaleStartDistance = 0.0;
        _scalePreviewFactor = 1.0;
        _editingVertexPathId = null;
        _editingVertexIndex = 0;
        _editingVertexIsConstrainedRectangle = false;
        _pressedPathId = null;
        SetCurrentValue(SelectedMeasurementIdProperty, null);
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

        if (change.Property == DocumentProperty)
        {
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

        DrawGrid(context, size);
        if (Document.Paths.Count > 0)
            DrawPaperBounds(context, size, Document.Bounds);
        DrawPaths(context, size, Document.Paths);
        DrawEditableVertexHandles(context, size, Document.Paths);
        DrawConstrainedRectangleHandles(context, size, Document.Paths);
        DrawCornerToolHandles(context, size, Document.Paths);
        DrawLiveSketchLine(context, size);
        DrawLiveSketchRectangle(context, size);
        DrawLiveSketchCircle(context, size);
        DrawLiveSketchPolygon(context, size);
        DrawLiveSketchText(context, size);
        DrawLivePenPath(context, size);
        DrawScaleGizmo(context, size, Document.Paths);
        DrawLiveMirrorAxis(context, size);
        DrawTrimPreview(context, size, Document);
        DrawMeasurements(context, size);
        DrawLiveMeasurement(context, size);
        DrawMarquee(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetCurrentPoint(this);
        _lastPointerPosition = point.Position;
        _pointerPressPosition = point.Position;

        if (point.Properties.IsMiddleButtonPressed
            || (point.Properties.IsLeftButtonPressed && ActiveTool == Editor2DTool.Pan))
        {
            _contextMenu.Close();
            _cancelInteractionOnPointerRelease = false;
            _isPanning = true;
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

        if (ActiveTool == Editor2DTool.Select
            || ActiveTool == Editor2DTool.Scale
            || ActiveTool == Editor2DTool.Mirror
            || ActiveTool == Editor2DTool.ConvertLines
            || ActiveTool == Editor2DTool.Offset
            || ActiveTool == Editor2DTool.AddThickness
            || ActiveTool == Editor2DTool.Cleanup
            || ActiveTool == Editor2DTool.Patterning
            || ActiveTool == Editor2DTool.PaperFolding)
        {
            if (ActiveTool == Editor2DTool.Scale && TryBeginScaleSelection(point.Position, e.Pointer))
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

        if (ActiveTool == Editor2DTool.Measure)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleMeasurementClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Dimension)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleDimensionClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Trim)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleTrimClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Fillet || ActiveTool == Editor2DTool.Chamfer)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleCornerToolClick(point.Position, ActiveTool == Editor2DTool.Chamfer ? "chamfer" : "fillet");
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.SketchLine)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleSketchLineClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.SketchRectangle)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleSketchRectangleClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.SketchCircle)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleSketchCircleClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.SketchPolygon)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleSketchPolygonClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.SketchText)
        {
            _cancelInteractionOnPointerRelease = false;
            HandleSketchTextClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.Pen)
        {
            _cancelInteractionOnPointerRelease = false;
            HandlePenClick(point.Position);
            e.Handled = true;
            return;
        }

        if (ActiveTool != Editor2DTool.Select
            && ActiveTool != Editor2DTool.Scale
            && ActiveTool != Editor2DTool.Mirror
            && ActiveTool != Editor2DTool.ConvertLines
            && ActiveTool != Editor2DTool.Offset
            && ActiveTool != Editor2DTool.AddThickness
            && ActiveTool != Editor2DTool.Cleanup
            && ActiveTool != Editor2DTool.Patterning
            && ActiveTool != Editor2DTool.PaperFolding)
        {
            if (ActiveTool == Editor2DTool.Move)
            {
                StartMoveSelection(point.Position, e.KeyModifiers, e.Pointer);
                e.Handled = true;
            }

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
        _hoverPointerPosition = position;
        _hasHoverPointerPosition = true;
        if (_isAwaitingSecondaryContextClick)
        {
            var secondaryDelta = position - _pointerPressPosition;
            if (Math.Abs(secondaryDelta.X) > PointerDragThreshold || Math.Abs(secondaryDelta.Y) > PointerDragThreshold)
            {
                _isAwaitingSecondaryContextClick = false;
                _isPanning = true;
            }
        }

        if (_isPanning)
        {
            var delta = position - _lastPointerPosition;
            SetCurrentValue(OffsetXProperty, OffsetX + delta.X);
            SetCurrentValue(OffsetYProperty, OffsetY + delta.Y);
            _lastPointerPosition = position;
            e.Handled = true;
            return;
        }

        if (_isMovingSelection && _moveDocumentSnapshot is not null && _moveStartPoint is not null)
        {
            ApplyMoveSelection(position);
            e.Handled = true;
            return;
        }

        if (_isScalingSelection && _scaleDocumentSnapshot is not null && _scaleCenterPoint is not null)
        {
            ApplyScaleSelection(position);
            e.Handled = true;
            return;
        }

        if (_isEditingVertex && !string.IsNullOrWhiteSpace(_editingVertexPathId))
        {
            if (!_editingVertexIsConstrainedRectangle)
                ApplyVertexEdit(position);

            e.Handled = true;
            return;
        }

        if (ActiveTool == Editor2DTool.SketchLine && _pendingLineStart is not null)
        {
            _pendingLineEnd = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.SketchRectangle && _pendingRectangleStart is not null)
        {
            _pendingRectangleEnd = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.SketchCircle && _pendingCircleCenter is not null)
        {
            _pendingCircleEdge = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.SketchPolygon && _pendingPolygonCenter is not null)
        {
            _pendingPolygonEdge = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.SketchText && _pendingTextStart is not null)
        {
            _pendingTextEnd = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.Pen && _pendingPenPoints.Count > 0)
        {
            _pendingPenHoverPoint = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.Mirror && _pendingMirrorAxisStart is not null)
        {
            _pendingMirrorAxisEnd = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.Measure && _pendingMeasurementStart is not null)
        {
            _pendingMeasurementEnd = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.Dimension && _pendingDimensionStart is not null)
        {
            _pendingDimensionEnd = ScreenToWorld(position, Zoom);
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.Trim)
        {
            InvalidateVisual();
            return;
        }

        if (ActiveTool == Editor2DTool.Fillet || ActiveTool == Editor2DTool.Chamfer)
        {
            InvalidateVisual();
            return;
        }

        if ((ActiveTool == Editor2DTool.Select
             || ActiveTool == Editor2DTool.ConvertLines
             || ActiveTool == Editor2DTool.Offset
             || ActiveTool == Editor2DTool.AddThickness
             || ActiveTool == Editor2DTool.Cleanup
             || ActiveTool == Editor2DTool.Patterning
             || ActiveTool == Editor2DTool.PaperFolding)
            && e.Pointer.Captured == this
            && _marqueeStartPoint is not null)
        {
            var delta = position - _pointerPressPosition;
            if (!_isMarqueeSelecting && (Math.Abs(delta.X) > PointerDragThreshold || Math.Abs(delta.Y) > PointerDragThreshold))
                _isMarqueeSelecting = true;

            if (_isMarqueeSelecting)
            {
                _marqueeCurrentPoint = position;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

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

        if (e.Pointer.Captured == this && _cancelInteractionOnPointerRelease)
        {
            _cancelInteractionOnPointerRelease = false;
            e.Pointer.Capture(null);
            _pressedPathId = null;
            CancelMarqueeSelection();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isAwaitingSecondaryContextClick)
        {
            _isAwaitingSecondaryContextClick = false;
            e.Pointer.Capture(null);
            HandleContextClick(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_isMovingSelection)
        {
            _isMovingSelection = false;
            _moveDocumentSnapshot = null;
            _moveSelectionIds = Array.Empty<string>();
            _moveStartPoint = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isScalingSelection)
        {
            _isScalingSelection = false;
            _scaleDocumentSnapshot = null;
            _scaleSelectionIds = Array.Empty<string>();
            _scaleCenterPoint = null;
            _scaleStartDistance = 0.0;
            _scalePreviewFactor = 1.0;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isEditingVertex)
        {
            _isEditingVertex = false;
            _editingVertexPathId = null;
            _editingVertexIndex = 0;
            _editingVertexIsConstrainedRectangle = false;
            e.Pointer.Capture(null);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if ((ActiveTool != Editor2DTool.Select
             && ActiveTool != Editor2DTool.Scale
             && ActiveTool != Editor2DTool.Mirror
             && ActiveTool != Editor2DTool.ConvertLines
             && ActiveTool != Editor2DTool.Offset
             && ActiveTool != Editor2DTool.AddThickness
             && ActiveTool != Editor2DTool.Cleanup
             && ActiveTool != Editor2DTool.Patterning
             && ActiveTool != Editor2DTool.PaperFolding)
            || e.Pointer.Captured != this)
            return;

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

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        _hasHoverPointerPosition = false;

        if (_hoveredPathId is not null)
        {
            _hoveredPathId = null;
            InvalidateVisual();
        }
        else if (ActiveTool == Editor2DTool.Trim)
        {
            InvalidateVisual();
        }
        else if (ActiveTool == Editor2DTool.Fillet || ActiveTool == Editor2DTool.Chamfer)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (Document is null)
            return;

        var nextZoom = Zoom <= 0.0 ? 1.0 : Zoom;
        nextZoom *= e.Delta.Y >= 0 ? 1.1 : 1.0 / 1.1;
        nextZoom = Math.Clamp(nextZoom, 0.02, 2000.0);

        var screenPoint = e.GetPosition(this);
        var worldPoint = ScreenToWorld(screenPoint, Zoom <= 0.0 ? nextZoom : Zoom);
        SetCurrentValue(ZoomProperty, nextZoom);
        SetCurrentValue(OffsetXProperty, screenPoint.X - (Bounds.Width / 2.0) - (worldPoint.X * nextZoom));
        SetCurrentValue(OffsetYProperty, screenPoint.Y - (Bounds.Height / 2.0) + (worldPoint.Y * nextZoom));
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            CancelActiveInteraction();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && ActiveTool == Editor2DTool.Pen)
        {
            CommitPendingPenPath(isClosed: false);
            e.Handled = true;
        }
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

    private void DrawPaperBounds(DrawingContext context, Size size, Editor2DBounds bounds)
    {
        const double padding = 18.0;
        var topLeft = WorldToScreen(new Editor2DPoint(bounds.MinX - padding, bounds.MaxY + padding), size);
        var bottomRight = WorldToScreen(new Editor2DPoint(bounds.MaxX + padding, bounds.MinY - padding), size);
        var rect = new Rect(topLeft, bottomRight);
        context.DrawRectangle(PaperFillBrush, PaperBorderPen, rect);
    }

    private void DrawPaths(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        var selectedIds = SelectedPathIds.Count == 0
            ? null
            : new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            if (path.Points.Count < 2)
                continue;

            var geometry = new StreamGeometry();
            using (var geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(WorldToScreen(path.Points[0], size), false);
                for (var pointIndex = 1; pointIndex < path.Points.Count; pointIndex++)
                    geometryContext.LineTo(WorldToScreen(path.Points[pointIndex], size));

                if (path.IsClosed)
                    geometryContext.EndFigure(true);
            }

            var pen = selectedIds?.Contains(path.Id) == true
                ? SelectedPathPen
                : string.Equals(path.Id, _hoveredPathId, StringComparison.Ordinal)
                    ? HoverPathPen
                    : path.IsClosed
                        ? ClosedPathPen
                        : OpenPathPen;

            if (TryDrawSemanticPrimitive(context, size, path, pen))
                continue;

            context.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawMeasurements(DrawingContext context, Size size)
    {
        foreach (var measurement in Measurements)
        {
            if (measurement.IsAutoDimension
                && !string.IsNullOrWhiteSpace(measurement.EntityPathId)
                && !SelectedPathIds.Contains(measurement.EntityPathId, StringComparer.Ordinal))
            {
                continue;
            }

            var start = WorldToScreen(measurement.Start, size);
            var end = WorldToScreen(measurement.End, size);
            var isSelectedManualMeasurement = !measurement.IsAutoDimension
                && string.Equals(measurement.Id, SelectedMeasurementId, StringComparison.Ordinal);
            context.DrawLine(
                measurement.IsAutoDimension
                    ? AutoDimensionPen
                    : isSelectedManualMeasurement
                        ? SelectedMeasurementPen
                        : MeasurementPen,
                start,
                end);

            if (!measurement.IsAutoDimension)
            {
                context.DrawEllipse(MeasurementPointBrush, null, start, 3.0, 3.0);
                context.DrawEllipse(MeasurementPointBrush, null, end, 3.0, 3.0);
            }

            DrawMeasurementLabel(context, measurement, start, end);
        }
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
            }
        }
    }

    private void DrawEditableVertexHandles(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if ((ActiveTool != Editor2DTool.Select && ActiveTool != Editor2DTool.Scale) || SelectedPathIds.Count == 0)
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
        if (SelectedPathIds.Count == 0)
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
        var labelText = $"{measurement.Distance:0.###} mm";
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

        var rectanglePoints = BuildRectanglePoints(startModel, endModel);
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(WorldToScreen(rectanglePoints[0], size), false);
            for (var pointIndex = 1; pointIndex < rectanglePoints.Length; pointIndex++)
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

    private void DrawTrimPreview(DrawingContext context, Size size, Editor2DPreviewDocument document)
    {
        if (ActiveTool != Editor2DTool.Trim
            || !_hasHoverPointerPosition
            || !TryBuildTrimTarget(document, _hoverPointerPosition, out var trimTarget))
        {
            return;
        }

        var start = WorldToScreen(trimTarget.KillStart, size);
        var end = WorldToScreen(trimTarget.KillEnd, size);
        context.DrawLine(TrimPreviewPen, start, end);
    }

    private void DrawLivePenPath(DrawingContext context, Size size)
    {
        if (_pendingPenPoints.Count == 0)
            return;

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(WorldToScreen(_pendingPenPoints[0], size), false);
            for (var pointIndex = 1; pointIndex < _pendingPenPoints.Count; pointIndex++)
                geometryContext.LineTo(WorldToScreen(_pendingPenPoints[pointIndex], size));

            if (_pendingPenHoverPoint is Editor2DPoint hoverPoint)
                geometryContext.LineTo(WorldToScreen(hoverPoint, size));
        }

        context.DrawGeometry(null, HoverPathPen, geometry);
        for (var pointIndex = 0; pointIndex < _pendingPenPoints.Count; pointIndex++)
        {
            var point = _pendingPenPoints[pointIndex];
            var screenPoint = WorldToScreen(point, size);
            var radius = pointIndex == 0 && _pendingPenPoints.Count >= 2 ? 4.0 : 3.0;
            context.DrawEllipse(
                pointIndex == 0 && _pendingPenPoints.Count >= 2 ? EditableVertexHandleFillBrush : LiveMeasurementPointBrush,
                null,
                screenPoint,
                radius,
                radius);
        }
    }

    private void DrawLiveMirrorAxis(DrawingContext context, Size size)
    {
        if (_pendingMirrorAxisStart is not Editor2DPoint start || _pendingMirrorAxisEnd is not Editor2DPoint end)
            return;

        var startScreen = WorldToScreen(start, size);
        var endScreen = WorldToScreen(end, size);
        context.DrawLine(HoverPathPen, startScreen, endScreen);
        context.DrawEllipse(LiveMeasurementPointBrush, null, startScreen, 4.0, 4.0);
        context.DrawEllipse(LiveMeasurementPointBrush, null, endScreen, 4.0, 4.0);
    }

    private void DrawScaleGizmo(DrawingContext context, Size size, IReadOnlyList<Editor2DPreviewPath> paths)
    {
        if (ActiveTool != Editor2DTool.Scale
            || !TryGetSelectedPathBounds(paths, out var minX, out var minY, out var maxX, out var maxY))
        {
            return;
        }

        var center = new Editor2DPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
        var corner = new Editor2DPoint(maxX, maxY);
        var centerScreen = WorldToScreen(center, size);
        var cornerScreen = WorldToScreen(corner, size);
        var currentFactor = _isScalingSelection ? _scalePreviewFactor : 1.0;
        var handleScreen = new Point(
            centerScreen.X + ((cornerScreen.X - centerScreen.X) * currentFactor),
            centerScreen.Y + ((cornerScreen.Y - centerScreen.Y) * currentFactor));

        context.DrawLine(HoverPathPen, centerScreen, handleScreen);
        context.DrawEllipse(null, HoverPathPen, centerScreen, 6.0, 6.0);
        var handleRect = new Rect(handleScreen.X - 7.0, handleScreen.Y - 7.0, 14.0, 14.0);
        context.DrawRectangle(EditableVertexHandleFillBrush, EditableVertexHandlePen, handleRect);

        var factorText = new FormattedText(
            $"x{currentFactor:0.###}",
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
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
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

        var worldPoint = ScreenToWorld(screenPoint, Zoom);
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
        if (Document is null || !TryBuildTrimTarget(Document, screenPoint, out var trimTarget))
            return;

        var nextDocument = ApplyTrim(Document, trimTarget);
        if (nextDocument is null)
            return;

        SetCurrentValue(DocumentProperty, nextDocument);
        SetCurrentValue(SelectedPathIdsProperty, Array.Empty<string>());
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
    }

    private void HandleCornerToolClick(Point screenPoint, string kind)
    {
        if (Document is null || !TryHitTestCornerHandle(Document.Paths, screenPoint, out var hit))
            return;

        var nextDocument = ApplyCornerEdit(Document, hit.PathId, hit.CornerIndex, kind);
        if (nextDocument is null)
            return;

        SetCurrentValue(DocumentProperty, nextDocument);
        SetCurrentValue(SelectedPathIdsProperty, new[] { hit.PathId });
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        InvalidateVisual();
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
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
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
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
        if (_pendingRectangleStart is null)
        {
            _pendingRectangleStart = worldPoint;
            _pendingRectangleEnd = worldPoint;
            InvalidateVisual();
            return;
        }

        var startPoint = _pendingRectangleStart;
        var nextDocument = AddRectangleToDocument(startPoint, worldPoint);
        if (nextDocument is not null)
        {
            SetCurrentValue(DocumentProperty, nextDocument);
            var newPathId = nextDocument.Paths[^1].Id;
            SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
        }

        CancelPendingRectangle();
        InvalidateVisual();
    }

    private void HandleSketchCircleClick(Point screenPoint)
    {
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
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
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
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
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
        if (_pendingTextStart is null)
        {
            _pendingTextStart = worldPoint;
            _pendingTextEnd = worldPoint;
            InvalidateVisual();
            return;
        }

        var nextDocument = AddTextToDocument(_pendingTextStart, worldPoint);
        if (nextDocument is not null)
        {
            SetCurrentValue(DocumentProperty, nextDocument);
            var newPathId = nextDocument.Paths[^1].Id;
            SetCurrentValue(SelectedPathIdsProperty, new[] { newPathId });
            SetCurrentValue(SelectedMeasurementIdProperty, null);
        }

        CancelPendingText();
        InvalidateVisual();
    }

    private void HandlePenClick(Point screenPoint)
    {
        var worldPoint = ScreenToWorld(screenPoint, Zoom);
        if (_pendingPenPoints.Count >= 2 && IsNearFirstPendingPenPoint(screenPoint))
        {
            CommitPendingPenPath(isClosed: true);
            return;
        }

        if (_pendingPenPoints.Count > 0 && DistanceBetween(_pendingPenPoints[^1], worldPoint) <= 1e-6)
            return;

        _pendingPenPoints = _pendingPenPoints.Append(worldPoint).ToArray();
        _pendingPenHoverPoint = worldPoint;
        InvalidateVisual();
    }

    private void HandleMirrorClick(Point screenPoint, KeyModifiers keyModifiers)
    {
        if (Document is null)
            return;

        if (_pendingMirrorAxisStart is not null)
        {
            var axisEnd = ScreenToWorld(screenPoint, Zoom);
            if (DistanceBetween(_pendingMirrorAxisStart, axisEnd) <= 1e-6)
            {
                _pendingMirrorAxisEnd = axisEnd;
                InvalidateVisual();
                return;
            }

            var mirroredDocument = MirrorPaths(Document, SelectedPathIds, _pendingMirrorAxisStart, axisEnd);
            SetCurrentValue(DocumentProperty, mirroredDocument);
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            CancelPendingMirror();
            InvalidateVisual();
            return;
        }

        var hitPathId = HitTestPathId(screenPoint);
        if (SelectedPathIds.Count == 0 || keyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ApplyClickSelection(hitPathId, keyModifiers.HasFlag(KeyModifiers.Shift));
            SetCurrentValue(SelectedMeasurementIdProperty, null);
            InvalidateVisual();
            return;
        }

        var axisStart = ScreenToWorld(screenPoint, Zoom);
        _pendingMirrorAxisStart = axisStart;
        _pendingMirrorAxisEnd = axisStart;
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

        var center = new Editor2DPoint((minX + maxX) / 2.0, (minY + maxY) / 2.0);
        var handleScreen = WorldToScreen(new Editor2DPoint(maxX, maxY), Bounds.Size);
        var distanceToHandle = Math.Sqrt(Math.Pow(screenPoint.X - handleScreen.X, 2) + Math.Pow(screenPoint.Y - handleScreen.Y, 2));
        if (distanceToHandle > ScaleHandleHitTolerance)
            return false;

        _isScalingSelection = true;
        _scaleDocumentSnapshot = Document;
        _scaleSelectionIds = SelectedPathIds.ToArray();
        _scaleCenterPoint = center;
        var startWorldPoint = ScreenToWorld(screenPoint, Zoom);
        _scaleStartDistance = Math.Max(DistanceBetween(center, startWorldPoint), 1e-6);
        _scalePreviewFactor = 1.0;
        _cancelInteractionOnPointerRelease = false;
        SetCurrentValue(SelectedMeasurementIdProperty, null);
        pointer.Capture(this);
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
        _cancelInteractionOnPointerRelease = false;
        pointer.Capture(this);
    }

    private void ApplyMoveSelection(Point pointerPosition)
    {
        if (_moveDocumentSnapshot is null || _moveStartPoint is null)
            return;

        var currentPoint = ScreenToWorld(pointerPosition, Zoom);
        var deltaX = currentPoint.X - _moveStartPoint.X;
        var deltaY = currentPoint.Y - _moveStartPoint.Y;
        var movedDocument = TranslatePaths(_moveDocumentSnapshot, _moveSelectionIds, deltaX, deltaY);
        SetCurrentValue(DocumentProperty, movedDocument);
        InvalidateVisual();
    }

    private void ApplyScaleSelection(Point pointerPosition)
    {
        if (_scaleDocumentSnapshot is null || _scaleCenterPoint is null)
            return;

        var currentPoint = ScreenToWorld(pointerPosition, Zoom);
        var currentDistance = Math.Max(DistanceBetween(_scaleCenterPoint, currentPoint), 1e-6);
        var factor = Math.Max(currentDistance / Math.Max(_scaleStartDistance, 1e-6), 0.05);
        _scalePreviewFactor = factor;
        var scaledDocument = ScalePaths(_scaleDocumentSnapshot, _scaleSelectionIds, _scaleCenterPoint, factor);
        SetCurrentValue(DocumentProperty, scaledDocument);
        InvalidateVisual();
    }

    private void ApplyVertexEdit(Point pointerPosition)
    {
        if (Document is null || string.IsNullOrWhiteSpace(_editingVertexPathId))
            return;

        var nextPoint = ScreenToWorld(pointerPosition, Zoom);
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
    {
        if (Document is null)
            return null;

        var distance = Math.Sqrt(Math.Pow(endPoint.X - startPoint.X, 2) + Math.Pow(endPoint.Y - startPoint.Y, 2));
        if (distance <= 1e-6)
            return null;

        var nextPaths = Document.Paths.ToList();
        nextPaths.Add(new Editor2DPreviewPath(
            Id: $"line-{Guid.NewGuid():N}",
            EntityType: "LINE",
            Points: [startPoint, endPoint],
            IsClosed: false,
            IsAxisAlignedRectangle: false));
        return CreateUpdatedDocument(Document, nextPaths);
    }

    private Editor2DPreviewDocument? AddRectangleToDocument(Editor2DPoint startPoint, Editor2DPoint endPoint)
    {
        if (Document is null)
            return null;

        var width = Math.Abs(endPoint.X - startPoint.X);
        var height = Math.Abs(endPoint.Y - startPoint.Y);
        if (width <= 1e-6 || height <= 1e-6)
            return null;

        var nextPaths = Document.Paths.ToList();
        nextPaths.Add(new Editor2DPreviewPath(
            Id: $"rectangle-{Guid.NewGuid():N}",
            EntityType: "LWPOLYLINE",
            Points: BuildRectanglePoints(startPoint, endPoint),
            IsClosed: true,
            IsAxisAlignedRectangle: true));
        return CreateUpdatedDocument(Document, nextPaths);
    }

    private Editor2DPreviewDocument? AddCircleToDocument(Editor2DPoint centerPoint, Editor2DPoint edgePoint)
    {
        if (Document is null)
            return null;

        var radius = Math.Sqrt(Math.Pow(edgePoint.X - centerPoint.X, 2) + Math.Pow(edgePoint.Y - centerPoint.Y, 2));
        if (radius <= 1e-6)
            return null;

        var nextPaths = Document.Paths.ToList();
        nextPaths.Add(new Editor2DPreviewPath(
            Id: $"circle-{Guid.NewGuid():N}",
            EntityType: "CIRCLE",
            Points: BuildCirclePoints(centerPoint, radius),
            IsClosed: true,
            IsAxisAlignedRectangle: false,
            Center: centerPoint,
            Radius: radius,
            StartAngleDegrees: 0.0,
            EndAngleDegrees: 360.0));
        return CreateUpdatedDocument(Document, nextPaths);
    }

    private Editor2DPreviewDocument? AddPolygonToDocument(Editor2DPoint centerPoint, Editor2DPoint edgePoint)
    {
        if (Document is null)
            return null;

        var nextPoints = BuildPolygonPoints(centerPoint, edgePoint, PolygonSides);
        if (nextPoints.Length < 3)
            return null;

        var nextPaths = Document.Paths.ToList();
        nextPaths.Add(new Editor2DPreviewPath(
            Id: $"polygon-{Guid.NewGuid():N}",
            EntityType: "LWPOLYLINE",
            Points: nextPoints,
            IsClosed: true,
            IsAxisAlignedRectangle: false));
        return CreateUpdatedDocument(Document, nextPaths);
    }

    private Editor2DPreviewDocument? AddTextToDocument(Editor2DPoint startPoint, Editor2DPoint endPoint)
    {
        if (Document is null)
            return null;

        var textHeight = Math.Abs(endPoint.Y - startPoint.Y);
        var textWidth = Math.Abs(endPoint.X - startPoint.X);
        if (textHeight <= 1e-6 || textWidth <= 1e-6)
            return null;

        var insertPoint = new Editor2DPoint(
            Math.Min(startPoint.X, endPoint.X),
            Math.Min(startPoint.Y, endPoint.Y));
        var naturalWidth = Math.Max(DefaultTextValue.Length * textHeight * 0.6, textHeight * 0.6);
        var widthFactor = naturalWidth <= 1e-6
            ? 1.0
            : Math.Max(textWidth / naturalWidth, 0.1);
        var nextPaths = Document.Paths.ToList();
        nextPaths.Add(new Editor2DPreviewPath(
            Id: $"text-{Guid.NewGuid():N}",
            EntityType: "TEXT",
            Points: Editor2DGeometry.BuildTextBoundsPoints(insertPoint, DefaultTextValue, textHeight, widthFactor: widthFactor),
            IsClosed: false,
            IsAxisAlignedRectangle: false,
            Start: insertPoint,
            Text: DefaultTextValue,
            TextHeight: textHeight,
            RotationDegrees: 0.0,
            WidthFactor: widthFactor));
        return CreateUpdatedDocument(Document, nextPaths);
    }

    private Editor2DPreviewDocument? AddPenPathToDocument(IReadOnlyList<Editor2DPoint> points, bool isClosed)
    {
        if (Document is null || points.Count < 2)
            return null;

        var normalizedPoints = isClosed && points.Count >= 3
            ? points.ToArray()
            : points.ToArray();
        var nextPaths = Document.Paths.ToList();
        nextPaths.Add(new Editor2DPreviewPath(
            Id: $"pen-{Guid.NewGuid():N}",
            EntityType: "LWPOLYLINE",
            Points: normalizedPoints,
            IsClosed: isClosed,
            IsAxisAlignedRectangle: Editor2DGeometry.IsAxisAlignedRectangle(normalizedPoints, isClosed)));
        return CreateUpdatedDocument(Document, nextPaths);
    }

    private void CommitPendingPenPath(bool isClosed)
    {
        if (_pendingPenPoints.Count < 2)
        {
            CancelPendingPen();
            InvalidateVisual();
            return;
        }

        var resolvedClosed = isClosed && _pendingPenPoints.Count >= 3;
        var nextDocument = AddPenPathToDocument(_pendingPenPoints, resolvedClosed);
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

    private bool IsNearFirstPendingPenPoint(Point screenPoint)
    {
        if (_pendingPenPoints.Count == 0)
            return false;

        var firstPoint = WorldToScreen(_pendingPenPoints[0], Bounds.Size);
        return Math.Sqrt(Math.Pow(screenPoint.X - firstPoint.X, 2) + Math.Pow(screenPoint.Y - firstPoint.Y, 2)) <= PenCloseHitTolerance;
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
            SetCurrentValue(SelectedPathIdsProperty, new[] { pathId });
            return;
        }

        SetCurrentValue(SelectedMeasurementIdProperty, null);
        var selected = new HashSet<string>(SelectedPathIds, StringComparer.Ordinal);
        if (!selected.Add(pathId))
            selected.Remove(pathId);

        SetCurrentValue(SelectedPathIdsProperty, selected.ToArray());
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
    {
        var zoom = Math.Max(Zoom, 0.0001);
        var raw = targetPixels / zoom;
        var magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var factor = normalized switch
        {
            <= 1.0 => 1.0,
            <= 2.0 => 2.0,
            <= 5.0 => 5.0,
            _ => 10.0,
        };

        return factor * magnitude;
    }

    private WorldBounds GetVisibleWorldBounds(Size size)
    {
        var topLeft = ScreenToWorld(new Point(0, 0), Zoom);
        var bottomRight = ScreenToWorld(new Point(size.Width, size.Height), Zoom);
        return new WorldBounds(
            Left: Math.Min(topLeft.X, bottomRight.X),
            Right: Math.Max(topLeft.X, bottomRight.X),
            Bottom: Math.Min(topLeft.Y, bottomRight.Y),
            Top: Math.Max(topLeft.Y, bottomRight.Y));
    }

    private Point WorldToScreen(Editor2DPoint point, Size size)
        => new(
            (size.Width / 2.0) + OffsetX + (point.X * Zoom),
            (size.Height / 2.0) + OffsetY - (point.Y * Zoom));

    private Editor2DPoint ScreenToWorld(Point point, double zoom)
    {
        var resolvedZoom = Math.Max(zoom, 0.0001);
        return new Editor2DPoint(
            (point.X - (Bounds.Width / 2.0) - OffsetX) / resolvedZoom,
            ((Bounds.Height / 2.0) + OffsetY - point.Y) / resolvedZoom);
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
            var pathDistance = DistanceToPath(pointerPosition, screenPoints, path.IsClosed);
            if (pathDistance <= hitTolerance && pathDistance < bestDistance)
            {
                bestDistance = pathDistance;
                bestPathId = path.Id;
                continue;
            }

            if (path.IsClosed && screenPoints.Length >= 3 && PointInPolygon(pointerPosition, screenPoints))
            {
                bestDistance = 0.0;
                bestPathId = path.Id;
            }
        }

        return bestPathId;
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
            var formattedText = new FormattedText(
                textValue,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                TextEntityTypeface,
                fontSize,
                pen.Brush ?? OpenPathPen.Brush);

            using var transform = context.PushTransform(
                Matrix.CreateTranslation(screenStart.X, screenStart.Y)
                * Matrix.CreateRotation(-rotationDegrees * Math.PI / 180.0)
                * Matrix.CreateScale(widthFactor, 1.0));
            context.DrawText(formattedText, new Point(0.0, -formattedText.Height));
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
            containsPoint = polygon.Length >= 3 && PointInPolygon(hitPoint, polygon);
            distance = polygon.Length >= 2
                ? DistanceToPath(hitPoint, polygon, isClosed: true) * Zoom
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
    }

    private void CancelPendingPen()
    {
        _pendingPenPoints = Array.Empty<Editor2DPoint>();
        _pendingPenHoverPoint = null;
    }

    private void CancelPendingMirror()
    {
        _pendingMirrorAxisStart = null;
        _pendingMirrorAxisEnd = null;
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

        _expandRectanglesMenuItem.IsVisible = !hasMeasurementSelection && canExpandRectangles;
        _deleteSelectionMenuItem.IsVisible = hasMeasurementSelection || hasSelection;
    }

    private void OnExpandRectanglesMenuItemClick(object? sender, RoutedEventArgs e)
    {
        ExpandSelectedRectangles();
        _contextMenu.Close();
    }

    private void OnDeleteSelectionMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (!DeleteSelectedMeasurement())
            DeleteSelectedPaths();

        _contextMenu.Close();
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
            var distance = DistanceToSegment(screenPoint, start, end);
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
        =>
        [
            new Editor2DPoint(startPoint.X, startPoint.Y),
            new Editor2DPoint(endPoint.X, startPoint.Y),
            new Editor2DPoint(endPoint.X, endPoint.Y),
            new Editor2DPoint(startPoint.X, endPoint.Y),
        ];

    private static Editor2DPoint[] BuildCirclePoints(Editor2DPoint centerPoint, double radius)
    {
        const int segmentCount = 48;
        var points = new Editor2DPoint[segmentCount];
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            var angle = Math.PI * 2.0 * segmentIndex / segmentCount;
            points[segmentIndex] = new Editor2DPoint(
                centerPoint.X + (radius * Math.Cos(angle)),
                centerPoint.Y + (radius * Math.Sin(angle)));
        }

        return points;
    }

    private static Editor2DPoint[] BuildPolygonPoints(Editor2DPoint centerPoint, Editor2DPoint edgePoint, int sides)
    {
        var radius = Math.Sqrt(Math.Pow(edgePoint.X - centerPoint.X, 2) + Math.Pow(edgePoint.Y - centerPoint.Y, 2));
        var resolvedSides = Math.Clamp(sides, 3, 64);
        if (radius <= 1e-6)
            return [];

        var rotation = Math.Atan2(edgePoint.Y - centerPoint.Y, edgePoint.X - centerPoint.X);
        var points = new Editor2DPoint[resolvedSides];
        for (var index = 0; index < resolvedSides; index++)
        {
            var angle = rotation + (index * (Math.PI * 2.0 / resolvedSides));
            points[index] = new Editor2DPoint(
                centerPoint.X + (radius * Math.Cos(angle)),
                centerPoint.Y + (radius * Math.Sin(angle)));
        }

        return points;
    }

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
    {
        var selectedIdSet = new HashSet<string>(selectedIds, StringComparer.Ordinal);
        var nextPaths = document.Paths
            .Select(path => !selectedIdSet.Contains(path.Id)
                ? path
                : path with
                {
                    Start = path.Start is Editor2DPoint start
                        ? new Editor2DPoint(start.X + deltaX, start.Y + deltaY)
                        : null,
                    Center = path.Center is Editor2DPoint center
                        ? new Editor2DPoint(center.X + deltaX, center.Y + deltaY)
                        : null,
                    Points = path.Points
                        .Select(point => new Editor2DPoint(point.X + deltaX, point.Y + deltaY))
                        .ToArray(),
                })
            .ToArray();
        return CreateUpdatedDocument(document, nextPaths);
    }

    private static Editor2DPreviewDocument ScalePaths(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedIds,
        Editor2DPoint center,
        double factor)
    {
        var selectedIdSet = new HashSet<string>(selectedIds, StringComparer.Ordinal);
        var normalizedFactor = Math.Max(factor, 0.05);
        var nextPaths = document.Paths
            .Select(path => !selectedIdSet.Contains(path.Id)
                ? path
                : path with
                {
                    Start = path.Start is Editor2DPoint start
                        ? ScalePoint(start, center, normalizedFactor)
                        : null,
                    Center = path.Center is Editor2DPoint pathCenter
                        ? ScalePoint(pathCenter, center, normalizedFactor)
                        : null,
                    Radius = path.Radius is double radius ? radius * normalizedFactor : null,
                    TextHeight = path.TextHeight is double textHeight ? textHeight * normalizedFactor : null,
                    Points = path.Points
                        .Select(point => ScalePoint(point, center, normalizedFactor))
                        .ToArray(),
                })
            .ToArray();
        return CreateUpdatedDocument(document, nextPaths);
    }

    private static Editor2DPreviewDocument MirrorPaths(
        Editor2DPreviewDocument document,
        IReadOnlyList<string> selectedIds,
        Editor2DPoint axisStart,
        Editor2DPoint axisEnd)
    {
        var selectedIdSet = new HashSet<string>(selectedIds, StringComparer.Ordinal);
        var nextPaths = document.Paths
            .Select(path =>
            {
                if (!selectedIdSet.Contains(path.Id))
                    return path;

                var mirroredStart = path.Start is Editor2DPoint start
                    ? ReflectPoint(start, axisStart, axisEnd)
                    : null;
                var mirroredCenter = path.Center is Editor2DPoint center
                    ? ReflectPoint(center, axisStart, axisEnd)
                    : null;
                var mirroredRotation = path.RotationDegrees;
                var mirroredWidthFactor = path.WidthFactor;

                if (path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                    && path.Start is Editor2DPoint textStart
                    && path.RotationDegrees is double rotationDegrees)
                {
                    var directionPoint = new Editor2DPoint(
                        textStart.X + Math.Cos(rotationDegrees * Math.PI / 180.0),
                        textStart.Y + Math.Sin(rotationDegrees * Math.PI / 180.0));
                    var mirroredDirectionPoint = ReflectPoint(directionPoint, axisStart, axisEnd);
                    var resolvedMirroredStart = mirroredStart ?? ReflectPoint(textStart, axisStart, axisEnd);
                    mirroredRotation = Math.Atan2(
                        mirroredDirectionPoint.Y - resolvedMirroredStart.Y,
                        mirroredDirectionPoint.X - resolvedMirroredStart.X) * 180.0 / Math.PI;
                    mirroredWidthFactor = -(path.WidthFactor ?? 1.0);
                }

                var mirroredPoints = path.Points
                    .Select(point => ReflectPoint(point, axisStart, axisEnd))
                    .ToArray();

                if (path.EntityType.Equals("TEXT", StringComparison.OrdinalIgnoreCase)
                    && mirroredStart is Editor2DPoint resolvedTextStart)
                {
                    mirroredPoints = Editor2DGeometry.BuildTextBoundsPoints(
                        resolvedTextStart,
                        path.Text,
                        path.TextHeight ?? 5.0,
                        mirroredRotation ?? 0.0,
                        mirroredWidthFactor ?? 1.0);
                }

                var mirroredStartAngle = path.StartAngleDegrees;
                var mirroredEndAngle = path.EndAngleDegrees;
                if (path.EntityType.Equals("ARC", StringComparison.OrdinalIgnoreCase)
                    && path.Center is Editor2DPoint originalCenter
                    && path.Radius is double radius
                    && path.StartAngleDegrees is double startAngleDegrees
                    && path.EndAngleDegrees is double endAngleDegrees
                    && mirroredCenter is Editor2DPoint resolvedMirroredCenter)
                {
                    var originalArcStart = PointOnCircle(originalCenter, radius, startAngleDegrees);
                    var originalArcEnd = PointOnCircle(originalCenter, radius, endAngleDegrees);
                    var mirroredArcStart = ReflectPoint(originalArcStart, axisStart, axisEnd);
                    var mirroredArcEnd = ReflectPoint(originalArcEnd, axisStart, axisEnd);
                    mirroredStartAngle = NormalizeAngleDegrees(Math.Atan2(
                        mirroredArcEnd.Y - resolvedMirroredCenter.Y,
                        mirroredArcEnd.X - resolvedMirroredCenter.X) * 180.0 / Math.PI);
                    mirroredEndAngle = NormalizeAngleDegrees(Math.Atan2(
                        mirroredArcStart.Y - resolvedMirroredCenter.Y,
                        mirroredArcStart.X - resolvedMirroredCenter.X) * 180.0 / Math.PI);
                }

                return path with
                {
                    Start = mirroredStart,
                    Center = mirroredCenter,
                    RotationDegrees = mirroredRotation,
                    WidthFactor = mirroredWidthFactor,
                    StartAngleDegrees = mirroredStartAngle,
                    EndAngleDegrees = mirroredEndAngle,
                    Points = mirroredPoints,
                    IsAxisAlignedRectangle = Editor2DGeometry.IsAxisAlignedRectangle(mirroredPoints, path.IsClosed),
                };
            })
            .ToArray();
        return CreateUpdatedDocument(document, nextPaths);
    }

    private static Editor2DPoint ScalePoint(Editor2DPoint point, Editor2DPoint center, double factor)
        => new(
            center.X + ((point.X - center.X) * factor),
            center.Y + ((point.Y - center.Y) * factor));

    private static Editor2DPoint ReflectPoint(Editor2DPoint point, Editor2DPoint axisStart, Editor2DPoint axisEnd)
    {
        var deltaX = axisEnd.X - axisStart.X;
        var deltaY = axisEnd.Y - axisStart.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= 1e-9)
            return point;

        var projectedFactor = ((point.X - axisStart.X) * deltaX + (point.Y - axisStart.Y) * deltaY) / lengthSquared;
        var projectedX = axisStart.X + (projectedFactor * deltaX);
        var projectedY = axisStart.Y + (projectedFactor * deltaY);
        return new Editor2DPoint(
            (2.0 * projectedX) - point.X,
            (2.0 * projectedY) - point.Y);
    }

    private static Editor2DPreviewDocument CreateUpdatedDocument(
        Editor2DPreviewDocument document,
        IReadOnlyList<Editor2DPreviewPath> nextPaths)
    {
        var bounds = MeasureBounds(nextPaths);
        var entityCounts = nextPaths
            .GroupBy(static path => path.EntityType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return document with
        {
            Paths = nextPaths,
            Bounds = bounds,
            EntityCounts = entityCounts,
        };
    }

    private static double DistanceBetween(Editor2DPoint left, Editor2DPoint right)
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
    {
        var points = paths.SelectMany(static path => path.Points).ToArray();
        if (points.Length == 0)
            return new Editor2DBounds(0, 0, 0, 0);

        var minX = points.Min(static point => point.X);
        var minY = points.Min(static point => point.Y);
        var maxX = points.Max(static point => point.X);
        var maxY = points.Max(static point => point.Y);
        return new Editor2DBounds(minX, minY, maxX, maxY);
    }

    private static double DistanceToPath(Point point, IReadOnlyList<Point> points, bool isClosed)
    {
        if (points.Count < 2)
            return double.PositiveInfinity;

        var bestDistance = double.PositiveInfinity;
        for (var index = 0; index < points.Count - 1; index++)
            bestDistance = Math.Min(bestDistance, DistanceToSegment(point, points[index], points[index + 1]));

        if (isClosed)
            bestDistance = Math.Min(bestDistance, DistanceToSegment(point, points[^1], points[0]));

        return bestDistance;
    }

    private static bool PointInPolygon(Point point, IReadOnlyList<Point> polygon)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count; i++)
        {
            var j = i == 0 ? polygon.Count - 1 : i - 1;
            var pi = polygon[i];
            var pj = polygon[j];
            var intersects = ((pi.Y > point.Y) != (pj.Y > point.Y))
                             && (point.X < ((pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + double.Epsilon)) + pi.X);
            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var delta = end - start;
        var lengthSquared = (delta.X * delta.X) + (delta.Y * delta.Y);
        if (lengthSquared <= 1e-9)
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));

        var t = ((point.X - start.X) * delta.X + (point.Y - start.Y) * delta.Y) / lengthSquared;
        t = Math.Clamp(t, 0.0, 1.0);
        var projection = new Point(start.X + (delta.X * t), start.Y + (delta.Y * t));
        return Math.Sqrt(Math.Pow(point.X - projection.X, 2) + Math.Pow(point.Y - projection.Y, 2));
    }

    private readonly record struct WorldBounds(double Left, double Right, double Bottom, double Top);
}
