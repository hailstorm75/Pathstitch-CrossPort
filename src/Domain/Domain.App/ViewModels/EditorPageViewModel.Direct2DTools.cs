using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public bool DuplicateTwoDSelection(double offsetX = 5.0, double offsetY = 5.0)
    {
        if (TwoDDocument is not { } document || TwoDSelectedPathIds.Count == 0)
            return false;

        var selectedCount = document.Paths.Count(path => TwoDSelectedPathIds.Contains(path.Id, StringComparer.Ordinal));
        if (!_twoDWorkspace.ApplySelectionTransform(
                Editor2DAffineTransform.CreateTranslation(offsetX, offsetY),
                createCopy: true))
        {
            return false;
        }

        return CompleteTwoDWorkspaceOperation(Editor2DWorkspaceOperationResult.Success(selectedCount == 1
            ? "Duplicated 1 entity"
            : $"Duplicated {selectedCount} entities"));
    }

    public bool FlipTwoDSelection(bool horizontal)
    {
        if (TwoDDocument is not { } document || TwoDSelectedPathIds.Count == 0)
            return false;

        var selectedIds = TwoDSelectedPathIds.ToHashSet(StringComparer.Ordinal);
        var selected = document.Paths.Where(path => selectedIds.Contains(path.Id)).ToArray();
        var referencePoints = selected
            .SelectMany(GetPathReferencePoints)
            .ToArray();
        if (referencePoints.Length == 0)
            return false;

        var pivotX = (referencePoints.Min(point => point.X) + referencePoints.Max(point => point.X)) / 2.0;
        var pivotY = (referencePoints.Min(point => point.Y) + referencePoints.Max(point => point.Y)) / 2.0;
        var transform = horizontal
            ? Editor2DAffineTransform.CreateReflectionAcrossVerticalAxis(pivotX)
            : Editor2DAffineTransform.CreateReflectionAcrossHorizontalAxis(pivotY);
        if (!_twoDWorkspace.ApplySelectionTransform(transform))
            return false;

        return CompleteTwoDWorkspaceOperation(Editor2DWorkspaceOperationResult.Success(horizontal
            ? "Flipped selection horizontally"
            : "Flipped selection vertically"));
    }

    private static IEnumerable<Editor2DPoint> GetPathReferencePoints(Editor2DPreviewPath path)
    {
        foreach (var point in path.Points)
            yield return point;

        if (path.Start is { } start)
            yield return start;
        if (path.Center is { } center)
            yield return center;
    }

}
