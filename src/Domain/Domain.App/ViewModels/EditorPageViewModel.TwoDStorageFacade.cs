using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool ApplyTwoDSelectionTransform(Editor2DAffineTransform transform, bool createCopy = false)
    {
        var succeeded = _twoDWorkspace.ApplySelectionTransform(transform, createCopy);
        if (!CompleteTwoDWorkspaceOperation(succeeded
                ? Editor2DWorkspaceOperationResult.Success(createCopy
                    ? "Copied and transformed the selection."
                    : "Transformed the selection.")
                : Editor2DWorkspaceOperationResult.Failure("Select geometry to transform.")))
        {
            return false;
        }

        return true;
    }

    private void CommitTwoDWorkspaceEdit(Editor2DPreviewDocument document, IReadOnlyList<string> selectedPathIds)
    {
        _twoDWorkspace.CommitDocumentEdit(document, selectedPathIds);
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        RecordActivity("2D Edit", "Committed drawing edit");
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
    }

    private bool CompleteTwoDWorkspaceOperation(Editor2DWorkspaceOperationResult result)
    {
        StatusText = result.Message;
        if (!result.IsSuccess)
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        RecordActivity("2D Edit", result.Message);
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool ReplaceTwoDPath(string sourcePathId, IReadOnlyList<Editor2DPreviewPath> replacements)
    {
        if (!_twoDWorkspace.ReplacePath(sourcePathId, replacements))
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        RecordActivity("2D Edit", "Replaced path geometry");
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    public bool UpdateTwoDPathVertex(string pathId, int vertexIndex, Editor2DPoint point)
    {
        if (!_twoDWorkspace.UpdatePathVertex(pathId, vertexIndex, point))
            return false;
        ApplyTwoDWorkspaceSnapshot(_twoDWorkspace.State);
        RecordActivity("2D Edit", "Moved path vertex");
        Request3DStatePersistence(TimeSpan.FromMilliseconds(80));
        ViewportStateText = OutputStatusSummary;
        return true;
    }

    private int _twoDPolygonSides { get => _twoDWorkspace.PolygonSides; set => _twoDWorkspace.PolygonSides = value; }
    private IReadOnlyList<string> _twoDExpandedRectanglePathIds { get => _twoDWorkspace.ExpandedRectanglePathIds; set => _twoDWorkspace.ExpandedRectanglePathIds = value; }
    private string _twoDSelectedTextDraft { get => _twoDWorkspace.SelectedTextDraft; set => _twoDWorkspace.SelectedTextDraft = value; }
    private string _twoDSelectedTextHeightText { get => _twoDWorkspace.SelectedTextHeightText; set => _twoDWorkspace.SelectedTextHeightText = value; }
    private string _twoDSelectedTextFontFamily { get => _twoDWorkspace.SelectedTextFontFamily; set => _twoDWorkspace.SelectedTextFontFamily = value; }
    private string _twoDSelectedTextCharacterSpacingText { get => _twoDWorkspace.SelectedTextCharacterSpacingText; set => _twoDWorkspace.SelectedTextCharacterSpacingText = value; }
    private bool _twoDSelectedTextBold { get => _twoDWorkspace.SelectedTextBold; set => _twoDWorkspace.SelectedTextBold = value; }
    private bool _twoDSelectedTextItalic { get => _twoDWorkspace.SelectedTextItalic; set => _twoDWorkspace.SelectedTextItalic = value; }
    private bool _twoDSelectedTextUnderline { get => _twoDWorkspace.SelectedTextUnderline; set => _twoDWorkspace.SelectedTextUnderline = value; }
    private string _twoDSelectedTextFitMode { get => _twoDWorkspace.SelectedTextFitMode; set => _twoDWorkspace.SelectedTextFitMode = value; }
    private bool _isTwoDSelectedTextHeightValid { get => _twoDWorkspace.IsSelectedTextHeightValid; set => _twoDWorkspace.IsSelectedTextHeightValid = value; }
    private double _twoDViewportZoom { get => _twoDWorkspace.ViewportZoom; set => _twoDWorkspace.ViewportZoom = value; }
    private double _twoDViewportOffsetX { get => _twoDWorkspace.ViewportOffsetX; set => _twoDWorkspace.ViewportOffsetX = value; }
    private double _twoDViewportOffsetY { get => _twoDWorkspace.ViewportOffsetY; set => _twoDWorkspace.ViewportOffsetY = value; }
    private int _twoDFrameRequestToken { get => _twoDWorkspace.FrameRequestToken; set => _twoDWorkspace.FrameRequestToken = value; }
    private string _twoDConvertLineStyle { get => _twoDWorkspace.ConvertLineStyle; set => _twoDWorkspace.ConvertLineStyle = value; }
    private Dictionary<string, string> _twoDConvertLineParameterText
        => _twoDWorkspace.GetOrInitializeConvertLineParameterText(CreateTwoDConvertLineParameterText());
    private string _twoDOffsetMode { get => _twoDWorkspace.OffsetMode; set => _twoDWorkspace.OffsetMode = value; }
    private string _twoDOffsetSide { get => _twoDWorkspace.OffsetSide; set => _twoDWorkspace.OffsetSide = value; }
    private string _twoDOffsetDistanceText { get => _twoDWorkspace.OffsetDistanceText; set => _twoDWorkspace.OffsetDistanceText = value; }
    private string _twoDOffsetBBoxDistanceText { get => _twoDWorkspace.OffsetBBoxDistanceText; set => _twoDWorkspace.OffsetBBoxDistanceText = value; }
    private string _twoDOffsetBBoxFilletText { get => _twoDWorkspace.OffsetBBoxFilletText; set => _twoDWorkspace.OffsetBBoxFilletText = value; }
    private bool _twoDOffsetConstruction { get => _twoDWorkspace.OffsetConstruction; set => _twoDWorkspace.OffsetConstruction = value; }
    private string _twoDAddThicknessWidthText { get => _twoDWorkspace.AddThicknessWidthText; set => _twoDWorkspace.AddThicknessWidthText = value; }
    private string _twoDCleanupToleranceText { get => _twoDWorkspace.CleanupToleranceText; set => _twoDWorkspace.CleanupToleranceText = value; }
    private string _twoDPatternMode { get => _twoDWorkspace.PatternMode; set => _twoDWorkspace.PatternMode = value; }
    private string _twoDPatternDistanceMode { get => _twoDWorkspace.PatternDistanceMode; set => _twoDWorkspace.PatternDistanceMode = value; }
    private string _twoDPatternCopiesXText { get => _twoDWorkspace.PatternCopiesXText; set => _twoDWorkspace.PatternCopiesXText = value; }
    private string _twoDPatternCopiesYText { get => _twoDWorkspace.PatternCopiesYText; set => _twoDWorkspace.PatternCopiesYText = value; }
    private string _twoDPatternSpacingXText { get => _twoDWorkspace.PatternSpacingXText; set => _twoDWorkspace.PatternSpacingXText = value; }
    private string _twoDPatternSpacingYText { get => _twoDWorkspace.PatternSpacingYText; set => _twoDWorkspace.PatternSpacingYText = value; }
    private string _twoDPatternExtentXText { get => _twoDWorkspace.PatternExtentXText; set => _twoDWorkspace.PatternExtentXText = value; }
    private string _twoDPatternExtentYText { get => _twoDWorkspace.PatternExtentYText; set => _twoDWorkspace.PatternExtentYText = value; }
    private string _twoDPatternCircularCountText { get => _twoDWorkspace.PatternCircularCountText; set => _twoDWorkspace.PatternCircularCountText = value; }
    private string _twoDPatternCircularAngleText { get => _twoDWorkspace.PatternCircularAngleText; set => _twoDWorkspace.PatternCircularAngleText = value; }
    private string _twoDPatternPathCopiesText { get => _twoDWorkspace.PatternPathCopiesText; set => _twoDWorkspace.PatternPathCopiesText = value; }
    private string _twoDPatternPathSpacingText { get => _twoDWorkspace.PatternPathSpacingText; set => _twoDWorkspace.PatternPathSpacingText = value; }
    private string _twoDGlueTabHeightText { get => _twoDWorkspace.GlueTabHeightText; set => _twoDWorkspace.GlueTabHeightText = value; }
    private string _twoDGlueTabType { get => _twoDWorkspace.GlueTabType; set => _twoDWorkspace.GlueTabType = value; }
    private string _twoDGlueTabSide { get => _twoDWorkspace.GlueTabSide; set => _twoDWorkspace.GlueTabSide = value; }
    private string _twoDGlueTabStartOffsetText { get => _twoDWorkspace.GlueTabStartOffsetText; set => _twoDWorkspace.GlueTabStartOffsetText = value; }
    private string _twoDGlueTabEndOffsetText { get => _twoDWorkspace.GlueTabEndOffsetText; set => _twoDWorkspace.GlueTabEndOffsetText = value; }
    private double _twoDSewingHoleMargin { get => _twoDWorkspace.SewingHoleMargin; set => _twoDWorkspace.SewingHoleMargin = value; }
}
