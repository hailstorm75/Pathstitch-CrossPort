using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;

namespace Pathstitch.App.Tests;

public sealed class DrawingImportRoutingTests
{
    [Fact]
    public async Task FourDrawings_DistributeIntoTwoDGroups()
    {
        await using var fixture = await Fixture.CreateAsync(4, EditorMode.TwoD);

        await fixture.LoadAsync();

        Assert.Equal(EditorMode.TwoD, fixture.ViewModel.ActiveEditorMode);
        Assert.Equal(4, fixture.ViewModel.TwoDWorkspace.ImportGroups.Count);
        Assert.Equal(4, fixture.ViewModel.TwoDWorkspace.ImportGroups
            .Select(group => group.OwningLayerId).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(fixture.ViewModel.BatchWorkspace.Items);
        Assert.Equal(4, fixture.Preview.LoadCount);
    }

    [Fact]
    public async Task FiveDrawings_QueueSelectedBatchCardsWithoutTouchingTwoDWorkspace()
    {
        var existing = new Editor2DPreviewPath("existing", "LINE", [new(1, 2), new(3, 4)], false);
        var existingState = new Editor2DWorkspaceState(
            Document(existing),
            IsInitialized: true,
            Layers: [new("existing-layer", "Existing", [existing.Id])],
            ActiveLayerId: "existing-layer");
        await using var fixture = await Fixture.CreateAsync(5, EditorMode.TwoD, existingState);

        await fixture.LoadAsync();

        Assert.Equal(EditorMode.Batch, fixture.ViewModel.ActiveEditorMode);
        Assert.Equal(fixture.Paths, fixture.ViewModel.BatchWorkspace.Items.Select(item => item.FilePath));
        Assert.All(fixture.ViewModel.BatchWorkspace.Items, item => Assert.True(item.IsSelected));
        var restored = Assert.Single(fixture.ViewModel.TwoDDocument!.Paths);
        Assert.Equal(existing.Id, restored.Id);
        Assert.Equal(existing.Points, restored.Points);
        Assert.Empty(fixture.ViewModel.TwoDWorkspace.ImportGroups);
        Assert.Equal(0, fixture.Preview.LoadCount);
        Assert.Equal(0, fixture.Preview.InspectUnitsCount);
        Assert.Equal(0, fixture.Prompt.CallCount);
        Assert.False(fixture.ViewModel.TwoDWorkspace.CanUndo);
        Assert.Equal("Queued 5 drawings for batch processing", fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task ExistingBatchMode_QueuesSingleDrawing()
    {
        await using var fixture = await Fixture.CreateAsync(1, EditorMode.Batch);

        await fixture.LoadAsync();

        Assert.Equal(EditorMode.Batch, fixture.ViewModel.ActiveEditorMode);
        Assert.Equal(fixture.Paths, fixture.ViewModel.BatchWorkspace.Items.Select(item => item.FilePath));
        Assert.Equal(0, fixture.Preview.LoadCount);
    }

    [Fact]
    public async Task FourInvalidDrawings_PreservePreviousModeAndWorkspace()
    {
        await using var fixture = await Fixture.CreateAsync(4, EditorMode.ThreeD, includeDocuments: false);

        await fixture.LoadAsync();

        Assert.Equal(EditorMode.ThreeD, fixture.ViewModel.ActiveEditorMode);
        Assert.Empty(fixture.ViewModel.TwoDWorkspace.ImportGroups);
        Assert.Empty(fixture.ViewModel.BatchWorkspace.Items);
        Assert.False(fixture.ViewModel.IsDirty);
        Assert.False(fixture.ViewModel.TwoDWorkspace.CanUndo);
        Assert.Contains(Path.GetFileName(fixture.Paths[0]), fixture.ViewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("no importable geometry", fixture.ViewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedPdf_AbortsWholeDrawingImportAndReportsFailure()
    {
        await using var fixture = await Fixture.CreateAsync(2, EditorMode.ThreeD);
        fixture.Preview.FailingPath = fixture.Paths[1];
        fixture.Preview.LoadException = new InvalidDataException("malformed PDF payload");

        await fixture.LoadAsync();

        Assert.Equal(EditorMode.ThreeD, fixture.ViewModel.ActiveEditorMode);
        Assert.Empty(fixture.ViewModel.TwoDWorkspace.ImportGroups);
        Assert.False(fixture.ViewModel.IsDirty);
        Assert.False(fixture.ViewModel.TwoDWorkspace.CanUndo);
        Assert.Contains(Path.GetFileName(fixture.Paths[1]), fixture.ViewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("malformed PDF payload", fixture.ViewModel.ErrorMessage, StringComparison.Ordinal);
    }

    private static Editor2DPreviewDocument Document(params Editor2DPreviewPath[] paths)
    {
        var points = paths.SelectMany(path => path.Points).ToArray();
        return new(
            paths,
            points.Length == 0 ? new(0, 0, 0, 0) : new(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y)),
            paths.GroupBy(path => path.EntityType).ToDictionary(group => group.Key, group => group.Count()),
            []);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _directory;

        private Fixture(
            string directory,
            IReadOnlyList<string> paths,
            EditorPageViewModel viewModel,
            RecordingPreviewService preview,
            RecordingPrompt prompt)
        {
            _directory = directory;
            Paths = paths;
            ViewModel = viewModel;
            Preview = preview;
            Prompt = prompt;
        }

        public IReadOnlyList<string> Paths { get; }
        public EditorPageViewModel ViewModel { get; }
        public RecordingPreviewService Preview { get; }
        public RecordingPrompt Prompt { get; }

        public static async Task<Fixture> CreateAsync(
            int drawingCount,
            EditorMode mode,
            Editor2DWorkspaceState? twoDState = null,
            bool includeDocuments = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-routing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var projectPath = Path.Combine(directory, "routing.stch");
            var shellState = new EditorWorkspaceState(
                Editor3DTool.Select,
                ThreeDOrthographic: false,
                ShowTwoDWorkspace: mode == EditorMode.TwoD,
                ActiveEditorMode: mode);
            await new Project3DStateService().SaveAsync(projectPath,
                new Project3DState(null, [], [], WorkspaceState: shellState, TwoDWorkspaceState: twoDState));
            var paths = Enumerable.Range(0, drawingCount)
                .Select(index => Path.Combine(directory, index % 2 == 0 ? $"drawing-{index}.dxf" : $"drawing-{index}.pdf"))
                .Select(Path.GetFullPath)
                .ToArray();
            foreach (var path in paths)
                await File.WriteAllTextAsync(path, "drawing");
            var documents = paths.ToDictionary(
                path => path,
                path => includeDocuments
                    ? Document(new Editor2DPreviewPath(
                        Path.GetFileNameWithoutExtension(path), "LINE", [new(0, 0), new(5, 0)], false))
                    : Document(),
                StringComparer.OrdinalIgnoreCase);
            var preview = new RecordingPreviewService(documents);
            var prompt = new RecordingPrompt();
            var viewModel = EditorPageViewModelModeTests.CreateViewModelForTests(
                outputPreviewService: preview,
                importUnitsPromptService: prompt);
            var session = new ProjectSession(
                Guid.NewGuid(), "Routing", projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Imported, DateTimeOffset.UtcNow);
            Assert.True(await viewModel.ConfigureParametersAsync(new Dictionary<string, object>
            {
                [EditorNavigationParameterKeys.ProjectSession] = session,
                [EditorNavigationParameterKeys.PendingTwoDFilePaths] = paths,
            }, CancellationToken.None));
            return new(directory, paths, viewModel, preview, prompt);
        }

        public async Task LoadAsync()
            => await ((INavigablePageViewModel)ViewModel).LoadAsync(CancellationToken.None);

        public ValueTask DisposeAsync()
        {
            Directory.Delete(_directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingPreviewService(IReadOnlyDictionary<string, Editor2DPreviewDocument> documents)
        : IEditorOutputPreviewService
    {
        public int LoadCount { get; private set; }
        public int InspectUnitsCount { get; private set; }
        public string? FailingPath { get; set; }
        public Exception? LoadException { get; set; }

        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadCount++;
            if (LoadException is not null
                && FailingPath is not null
                && string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(FailingPath), StringComparison.OrdinalIgnoreCase))
            {
                throw LoadException;
            }
            documents.TryGetValue(Path.GetFullPath(outputPath), out var document);
            return Task.FromResult(document);
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

    private sealed class RecordingPrompt : IEditorImportUnitsPromptService
    {
        public int CallCount { get; private set; }

        public Task<double?> PromptAsync(Editor2DImportUnitsInfo info, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult<double?>(1);
        }
    }
}
