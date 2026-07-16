using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class ImportedDrawingReloadTests
{
    [Fact]
    public async Task ReloadSelectedImport_ReappliesStoredScalePreservesCenterAndIsOneUndoStep()
    {
        var sourcePath = Path.GetFullPath("source.dxf");
        var service = new MutablePreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(outputPreviewService: service);
        Assert.True(viewModel.TwoDWorkspace.AddImportedDrawings(
            [new Editor2DImportedDrawing(sourcePath, 2, Document(Line("old", 0, 8)))]).IsSuccess);
        var originalGroup = Assert.Single(viewModel.TwoDWorkspace.ImportGroups);
        var originalCenter = Bounds(viewModel.TwoDWorkspace, originalGroup).CenterX;
        viewModel.TwoDSelectedPathIds = [originalGroup.GeneratedPathIds[0]];
        viewModel.TwoDWorkspace.ClearHistory();
        service.Documents[sourcePath] = Document(Line("fresh", -2.5, 2.5));

        Assert.True(viewModel.HasSelectedTwoDImportGroup);
        Assert.True(await viewModel.ReloadSelectedTwoDImportsFromDiskAsync());

        var reloaded = Assert.Single(viewModel.TwoDWorkspace.ImportGroups);
        Assert.Equal(originalGroup.Id, reloaded.Id);
        Assert.Equal(2, reloaded.AppliedUnitScale);
        Assert.Equal(originalCenter, Bounds(viewModel.TwoDWorkspace, reloaded).CenterX, 8);
        Assert.Equal(10, Bounds(viewModel.TwoDWorkspace, reloaded).Width, 8);
        Assert.Equal(reloaded.GeneratedPathIds, viewModel.TwoDSelectedPathIds);
        Assert.Equal(1, service.LoadCount);
        Assert.Equal(0, service.InspectUnitsCount);
        Assert.True(viewModel.TwoDWorkspace.Undo());
        Assert.Equal(8, Bounds(viewModel.TwoDWorkspace, originalGroup).Width, 8);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public async Task ReloadSelectedImports_MissingGroupIsAtomicNoOp()
    {
        var firstPath = Path.GetFullPath("first.dxf");
        var missingPath = Path.GetFullPath("missing.svg");
        var service = new MutablePreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(outputPreviewService: service);
        Assert.True(viewModel.TwoDWorkspace.AddImportedDrawings(
        [
            new Editor2DImportedDrawing(firstPath, 1, Document(Line("first", 0, 5))),
            new Editor2DImportedDrawing(missingPath, 1, Document(Line("second", 0, 5))),
        ]).IsSuccess);
        var groups = viewModel.TwoDWorkspace.ImportGroups.ToArray();
        viewModel.TwoDSelectedPathIds = groups.Select(group => group.GeneratedPathIds[0]).ToArray();
        viewModel.TwoDWorkspace.ClearHistory();
        var before = viewModel.TwoDWorkspace.State;
        service.Documents[firstPath] = Document(Line("new-first", 0, 20));

        Assert.False(await viewModel.ReloadSelectedTwoDImportsFromDiskAsync());

        Assert.Same(before, viewModel.TwoDWorkspace.State);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
        Assert.Contains("missing.svg", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReloadSelection_IgnoresUngroupedGeometryAndLoadsEachGroupOnce()
    {
        var firstPath = Path.GetFullPath("first.dxf");
        var secondPath = Path.GetFullPath("second.svg");
        var service = new MutablePreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(outputPreviewService: service);
        var ordinary = Line("ordinary", -10, -5);
        viewModel.TwoDDocument = Document(ordinary);
        Assert.True(viewModel.TwoDWorkspace.AddImportedDrawings(
        [
            new Editor2DImportedDrawing(firstPath, 1, Document(Line("first", 0, 5))),
            new Editor2DImportedDrawing(secondPath, 1, Document(Line("second", 0, 5))),
        ]).IsSuccess);
        var groups = viewModel.TwoDWorkspace.ImportGroups.ToArray();
        service.Documents[firstPath] = Document(Line("new-first", 0, 6));
        service.Documents[secondPath] = Document(Line("new-second", 0, 7));
        viewModel.TwoDSelectedPathIds = [ordinary.Id];
        Assert.False(viewModel.HasSelectedTwoDImportGroup);
        viewModel.TwoDSelectedPathIds =
            [ordinary.Id, groups[0].GeneratedPathIds[0], groups[1].GeneratedPathIds[0]];

        Assert.True(await viewModel.ReloadSelectedTwoDImportsFromDiskAsync());

        Assert.Equal(2, service.LoadCount);
        Assert.Contains(viewModel.TwoDDocument!.Paths, path => path.Id == ordinary.Id);
        Assert.Equal(2, viewModel.TwoDWorkspace.ImportGroups.Count);
    }

    [Fact]
    public async Task ReloadSelectedImport_CancellationLeavesWorkspaceUntouched()
    {
        var sourcePath = Path.GetFullPath("cancelled.dxf");
        var service = new MutablePreviewService();
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(outputPreviewService: service);
        Assert.True(viewModel.TwoDWorkspace.AddImportedDrawings(
            [new Editor2DImportedDrawing(sourcePath, 1, Document(Line("old", 0, 5)))]).IsSuccess);
        var group = Assert.Single(viewModel.TwoDWorkspace.ImportGroups);
        viewModel.TwoDSelectedPathIds = [group.GeneratedPathIds[0]];
        viewModel.TwoDWorkspace.ClearHistory();
        var before = viewModel.TwoDWorkspace.State;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            viewModel.ReloadSelectedTwoDImportsFromDiskAsync(cancellation.Token));

        Assert.Same(before, viewModel.TwoDWorkspace.State);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public async Task ReloadSelectedImport_GenericParserFailureIsReportedWithoutMutation()
    {
        var sourcePath = Path.GetFullPath("broken.svg");
        var service = new MutablePreviewService { LoadException = new InvalidOperationException("bad XML") };
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(outputPreviewService: service);
        Assert.True(viewModel.TwoDWorkspace.AddImportedDrawings(
            [new Editor2DImportedDrawing(sourcePath, 1, Document(Line("old", 0, 5)))]).IsSuccess);
        var group = Assert.Single(viewModel.TwoDWorkspace.ImportGroups);
        viewModel.TwoDSelectedPathIds = [group.GeneratedPathIds[0]];
        viewModel.TwoDWorkspace.ClearHistory();
        var before = viewModel.TwoDWorkspace.State;

        Assert.False(await viewModel.ReloadSelectedTwoDImportsFromDiskAsync());

        Assert.Same(before, viewModel.TwoDWorkspace.State);
        Assert.Contains("bad XML", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    [Fact]
    public async Task ReloadSelectedImport_RejectsConcurrentReloadAndAbortsWhenWorkspaceChanges()
    {
        var sourcePath = Path.GetFullPath("slow.dxf");
        var service = new MutablePreviewService
        {
            LoadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ReleaseLoad = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        service.Documents[sourcePath] = Document(Line("fresh", 0, 7));
        var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(outputPreviewService: service);
        Assert.True(viewModel.TwoDWorkspace.AddImportedDrawings(
            [new Editor2DImportedDrawing(sourcePath, 1, Document(Line("old", 0, 5)))]).IsSuccess);
        var group = Assert.Single(viewModel.TwoDWorkspace.ImportGroups);
        viewModel.TwoDSelectedPathIds = [group.GeneratedPathIds[0]];
        viewModel.TwoDWorkspace.ClearHistory();
        var firstReload = viewModel.ReloadSelectedTwoDImportsFromDiskAsync();
        await service.LoadStarted.Task;

        Assert.False(await viewModel.ReloadSelectedTwoDImportsFromDiskAsync());
        viewModel.TwoDSelectedPathIds = [];
        viewModel.TwoDWorkspace.ClearHistory();
        service.ReleaseLoad.SetResult(true);
        Assert.False(await firstReload);

        Assert.Contains("Workspace changed", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(5, viewModel.TwoDDocument!.Bounds.Width, 8);
        Assert.False(viewModel.TwoDWorkspace.CanUndo);
    }

    private static Editor2DPreviewPath Line(string id, double start, double end)
        => new(id, "LINE", [new(start, 0), new(end, 0)], false);

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return new(
            paths,
            points.Length == 0 ? new(0, 0, 0, 0) : new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y)),
            paths.GroupBy(path => path.EntityType).ToDictionary(group => group.Key, group => group.Count()),
            []);
    }

    private static Editor2DBounds Bounds(Domain.App.ViewModels.Editor2DWorkspaceViewModel workspace, Editor2DImportGroup group)
    {
        var points = workspace.Document.Paths
            .Where(path => group.GeneratedPathIds.Contains(path.Id, StringComparer.Ordinal))
            .SelectMany(path => path.Points)
            .ToArray();
        return new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }

    private sealed class MutablePreviewService : IEditorOutputPreviewService
    {
        public Dictionary<string, Editor2DPreviewDocument> Documents { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int LoadCount { get; private set; }
        public int InspectUnitsCount { get; private set; }
        public Exception? LoadException { get; init; }
        public TaskCompletionSource<bool>? LoadStarted { get; init; }
        public TaskCompletionSource<bool>? ReleaseLoad { get; init; }

        public async Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            if (LoadException is not null)
                throw LoadException;
            LoadStarted?.TrySetResult(true);
            if (ReleaseLoad is not null)
                await ReleaseLoad.Task.WaitAsync(cancellationToken);
            Documents.TryGetValue(Path.GetFullPath(outputPath), out var document);
            return document;
        }

        public Task<Editor2DImportUnitsInfo?> InspectImportUnitsAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            InspectUnitsCount++;
            return Task.FromResult<Editor2DImportUnitsInfo?>(null);
        }

        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }
}
