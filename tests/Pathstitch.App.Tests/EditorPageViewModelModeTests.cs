using System.Text.Json;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pathstitch.App.Tests;

public sealed class EditorPageViewModelModeTests
{
    public static IEnumerable<object[]> CatalogShortcutDescriptors()
        => EditorToolCatalog.All
            .Where(descriptor => descriptor.ShortcutText is not null)
            .Select(descriptor => new object[] { descriptor });

    [Fact]
    public void EditorMode_HasStableValuesIncludingReservedBatchMode()
    {
        Assert.Equal(0, (int)EditorMode.TwoD);
        Assert.Equal(1, (int)EditorMode.ThreeD);
        Assert.Equal(2, (int)EditorMode.Batch);
        Assert.Equal([EditorMode.TwoD, EditorMode.ThreeD, EditorMode.Batch], Enum.GetValues<EditorMode>());
    }

    [Fact]
    public void PatternGuide_IsClearedWhenPatternSessionChanges()
    {
        var viewModel = CreateViewModel();
        viewModel.TwoDPatternGuidePathId = "stale-guide";

        viewModel.TwoDActiveTool = Editor2DTool.Patterning;

        Assert.Null(viewModel.TwoDPatternGuidePathId);
    }

    [Fact]
    public async Task ThreeDBodyMove_CanUndoAndRedoOffsetChanges()
    {
        var viewModel = CreateViewModelForTests();
        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        viewModel.ThreeDWorkspace.ReplaceBodies([new Body3D(0, "body", [])], "{}", "body.obj");
        viewModel.ActivateMoveTool();
        viewModel.SelectBodyFromPanel(0);

        viewModel.NudgeSelectedBody(axis: 0, direction: 1);

        Assert.Equal(1.0, Assert.Single(viewModel.BodyOffsets).X);
        Assert.True(viewModel.CanUndoThreeDBodyMove);
        Assert.True(viewModel.UndoThreeDBodyMove());
        Assert.Empty(viewModel.BodyOffsets);
        Assert.True(viewModel.CanRedoThreeDBodyMove);
        Assert.True(viewModel.RedoThreeDBodyMove());
        Assert.Equal(1.0, Assert.Single(viewModel.BodyOffsets).X);
    }

    [Fact]
    public void PatternPreview_IsComputedWithoutMutatingDocument()
    {
        var viewModel = CreateViewModel();
        var source = new Editor2DPreviewPath("source", "LINE", [new(0, 0), new(1, 0)], false);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with { Paths = [source] };
        viewModel.TwoDSelectedPathIds = [source.Id];
        viewModel.TwoDActiveTool = Editor2DTool.Patterning;
        viewModel.TwoDPatternMode = "Circular";
        viewModel.TwoDPatternCircularCountText = "3";
        viewModel.TwoDPatternCircularAngleText = "180";
        viewModel.TwoDPatternPivot = new Editor2DPoint(0, 0);

        var preview = viewModel.TwoDPatternPreviewPaths;

        Assert.Equal(2, preview.Count);
        Assert.Single(viewModel.TwoDDocument.Paths);
        Assert.Equal(new Editor2DPoint(0, 0), preview[0].Points[0]);
        Assert.Equal(-1, preview[1].Points[1].X, 6);
        Assert.Equal(0, preview[1].Points[1].Y, 6);
    }

    [Fact]
    public async Task StrokeAndFillConversionAvailability_FollowsTheCurrentSelection()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with
        {
            Paths =
            [
                new Editor2DPreviewPath("stroke", "LWPOLYLINE", [new(0, 0), new(5, 0), new(5, 5), new(0, 5)], true),
                new Editor2DPreviewPath("fill", "LWPOLYLINE", [new(10, 0), new(15, 0), new(15, 5), new(10, 5)], true, IsFilled: true),
            ],
        };

        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.TwoDSelectedPathIds = ["stroke"];

        Assert.True(viewModel.CanApplyTwoDStrokeToFill);
        Assert.False(viewModel.CanApplyTwoDFillToStroke);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDStrokeToFill), changes);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDFillToStroke), changes);

        changes.Clear();
        viewModel.TwoDSelectedPathIds = ["fill"];

        Assert.False(viewModel.CanApplyTwoDStrokeToFill);
        Assert.True(viewModel.CanApplyTwoDFillToStroke);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDStrokeToFill), changes);
        Assert.Contains(nameof(EditorPageViewModel.CanApplyTwoDFillToStroke), changes);
    }

    [Fact]
    public async Task StrokeAndFillConversions_UseTheWorkspaceOperationAndUpdateDocumentState()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDDocument = Editor2DWorkspaceState.Empty.Document with
        {
            Paths =
            [
                new Editor2DPreviewPath("stroke", "LWPOLYLINE", [new(0, 0), new(5, 0), new(5, 5), new(0, 5)], true),
                new Editor2DPreviewPath("fill", "LWPOLYLINE", [new(10, 0), new(15, 0), new(15, 5), new(10, 5)], true, IsFilled: true),
            ],
        };

        viewModel.TwoDSelectedPathIds = ["stroke"];

        Assert.True(viewModel.ApplyTwoDStrokeToFill());
        Assert.True(viewModel.TwoDDocument!.Paths.Single(path => path.Id == "stroke").IsFilled);

        viewModel.TwoDSelectedPathIds = ["fill"];

        Assert.True(viewModel.ApplyTwoDFillToStroke());
        Assert.False(viewModel.TwoDDocument!.Paths.Single(path => path.Id == "fill").IsFilled);
    }

    [Fact]
    public void ScalePivot_IsTransientAcrossToolSessions()
    {
        var viewModel = CreateViewModel();
        viewModel.TwoDActiveTool = Editor2DTool.Scale;
        viewModel.TwoDScalePivot = new Editor2DPoint(12, 7);

        viewModel.TwoDActiveTool = Editor2DTool.Select;

        Assert.Null(viewModel.TwoDScalePivot);
        Assert.False(viewModel.TwoDScalePivotPicking);
    }

    [Fact]
    public void MoveCopyMode_ResetsWhenMoveToolActivates()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.TwoDMoveCreateCopy);

        viewModel.TwoDMoveCreateCopy = true;
        viewModel.ActivateTwoDMoveTool();

        Assert.False(viewModel.TwoDMoveCreateCopy);

        viewModel.TwoDMoveCreateCopy = true;
        viewModel.TwoDActiveTool = Editor2DTool.Move;

        Assert.False(viewModel.TwoDMoveCreateCopy);
    }

    [Theory]
    [InlineData(EditorMode.TwoD, true, false, false)]
    [InlineData(EditorMode.ThreeD, false, true, false)]
    [InlineData(EditorMode.Batch, false, false, true)]
    public async Task ActiveEditorMode_RaisesObservableStateAndShowsExactlyOneWorkspace(
        EditorMode mode,
        bool showsTwoD,
        bool showsThreeD,
        bool showsBatch)
    {
        var viewModel = CreateViewModel();
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.SetActiveEditorModeAsync(mode);

        Assert.Equal(mode, viewModel.ActiveEditorMode);
        Assert.Equal(showsTwoD, viewModel.IsShowingTwoDWorkspace);
        Assert.Equal(showsThreeD, viewModel.IsShowing3DWorkspace);
        Assert.Equal(showsBatch, viewModel.IsShowingBatchWorkspace);
        Assert.Equal(1, new[]
        {
            viewModel.IsShowingTwoDWorkspace,
            viewModel.IsShowing3DWorkspace,
            viewModel.IsShowingBatchWorkspace,
        }.Count(static isVisible => isVisible));

        if (mode != EditorMode.ThreeD)
        {
            Assert.Contains(nameof(EditorPageViewModel.ActiveEditorMode), changes);
            Assert.Contains(nameof(EditorPageViewModel.IsShowingTwoDWorkspace), changes);
            Assert.Contains(nameof(EditorPageViewModel.IsShowing3DWorkspace), changes);
            Assert.Contains(nameof(EditorPageViewModel.IsShowingBatchWorkspace), changes);
        }
    }

    [Fact]
    public async Task SwitchingBetweenTwoDAndThreeD_PreservesEachModesActiveTool()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateTwoDCircleTool();

        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        viewModel.ActivateMoveTool();
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);

        viewModel.ActivateTwoDRectangleTool();
        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);
        Assert.Equal(Editor2DTool.SketchRectangle, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task OpeningBlankTwoDWorkspace_DoesNotCreateATwoDArtifact()
    {
        var viewModel = CreateViewModel();

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.True(viewModel.HasTwoDWorkspaceDocument);
        Assert.Empty(viewModel.TwoDDocument!.Paths);
        Assert.False(viewModel.HasGeneratedOutput);
        Assert.Null(viewModel.LastGeneratedOutputPath);
        Assert.Same(viewModel.TwoDDocument, viewModel.TwoDWorkspace.Document);
    }

    [Fact]
    public async Task TwoDUndoRedoCommands_TrackWorkspaceHistory()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.TwoDWorkspace.ClearHistory();
        var path = new Editor2DPreviewPath("undo-line", "LINE", [new(0, 0), new(5, 0)], false);

        Assert.False(viewModel.CanUndoTwoDWorkspace);
        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };

        Assert.True(viewModel.CanUndoTwoDWorkspace);
        Assert.True(viewModel.UndoTwoDCommand.CanExecute(null));
        viewModel.UndoTwoDCommand.Execute(null);
        Assert.Empty(viewModel.TwoDDocument!.Paths);
        Assert.True(viewModel.CanRedoTwoDWorkspace);
        viewModel.RedoTwoDCommand.Execute(null);
        Assert.Equal(path.Id, Assert.Single(viewModel.TwoDDocument!.Paths).Id);
    }

    [Fact]
    public async Task EditingTwoDDocument_NotifiesLayersPanelWithUpdatedMembership()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        var path = new Editor2DPreviewPath(
            "line-1",
            "LINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0)],
            IsClosed: false);

        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };

        var layer = Assert.Single(viewModel.TwoDLayers);
        Assert.Equal([path.Id], layer.PathIds);
        Assert.Equal("1 entities", layer.ContentSummary);
        Assert.Contains(nameof(EditorPageViewModel.TwoDLayers), changes);
    }

    [Fact]
    public async Task FillStrokeActions_ExposeAndApplyExistingWorkspaceOperations()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        var path = new Editor2DPreviewPath(
            "fill-toggle", "LWPOLYLINE",
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10)], true);
        viewModel.TwoDDocument = viewModel.TwoDDocument! with { Paths = [path] };
        viewModel.TwoDSelectedPathIds = [path.Id];

        Assert.True(viewModel.CanApplyTwoDStrokeToFill);
        Assert.True(viewModel.ApplyTwoDStrokeToFill());
        Assert.True(viewModel.TwoDDocument!.Paths.Single().IsFilled);
        Assert.True(viewModel.CanApplyTwoDFillToStroke);
        Assert.True(viewModel.ApplyTwoDFillToStroke());
        Assert.False(viewModel.TwoDDocument!.Paths.Single().IsFilled);
    }

    [Fact]
    public async Task LoadingSavedTwoDProject_RestoresDocumentAndActiveMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-2d-mode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "saved-2d.stch");
        try
        {
            var path = new Editor2DPreviewPath(
                "rectangle-1",
                "LWPOLYLINE",
                [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0), new Editor2DPoint(10, 5), new Editor2DPoint(0, 5)],
                IsClosed: true);
            var twoDState = Editor2DWorkspaceState.Empty with
            {
                IsInitialized = true,
                Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            };
            var shellState = new EditorWorkspaceState(
                Editor3DTool.Select,
                ThreeDOrthographic: false,
                ShowTwoDWorkspace: true,
                ActiveEditorMode: EditorMode.TwoD);
            await new Project3DStateService().SaveAsync(
                projectPath,
                new Project3DState(null, [], [], WorkspaceState: shellState, TwoDWorkspaceState: twoDState));
            var session = new ProjectSession(
                Guid.NewGuid(),
                "Saved 2D",
                projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Opened,
                DateTimeOffset.UtcNow);
            var viewModel = CreateViewModel();

            Assert.True(await viewModel.ConfigureParametersAsync(
                new Dictionary<string, object> { [EditorNavigationParameterKeys.ProjectSession] = session },
                CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);

            Assert.Equal(EditorMode.TwoD, viewModel.ActiveEditorMode);
            Assert.True(viewModel.IsShowingTwoDWorkspace);
            Assert.Equal(path.Id, Assert.Single(viewModel.TwoDDocument!.Paths).Id);
            Assert.Equal([path.Id], Assert.Single(viewModel.TwoDLayers).PathIds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadingPendingTwoDDocuments_ImportsAllDrawingsSideBySide()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-2d-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "imported.stch");
        var firstPath = Path.Combine(directory, "first.dxf");
        var secondPath = Path.Combine(directory, "second.svg");
        try
        {
            await new Project3DStateService().SaveAsync(projectPath, new Project3DState(null, [], []));
            File.WriteAllText(firstPath, "first");
            File.WriteAllText(secondPath, "second");
            var session = new ProjectSession(
                Guid.NewGuid(),
                "Imported drawings",
                projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Imported,
                DateTimeOffset.UtcNow);
            var previewService = new MappingOutputPreviewService(new Dictionary<string, Editor2DPreviewDocument>
            {
                [Path.GetFullPath(firstPath)] = Document("first", 0),
                [Path.GetFullPath(secondPath)] = Document("second", 100),
            });
            var viewModel = CreateViewModel(outputPreviewService: previewService);

            Assert.True(await viewModel.ConfigureParametersAsync(
                new Dictionary<string, object>
                {
                    [EditorNavigationParameterKeys.ProjectSession] = session,
                    [EditorNavigationParameterKeys.PendingTwoDFilePaths] = new[] { firstPath, secondPath },
                },
                CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);

            Assert.Equal(2, viewModel.TwoDDocument!.Paths.Count);
            Assert.Equal("import-1-1-first", viewModel.TwoDDocument.Paths[0].Id);
            Assert.Equal("import-2-1-second", viewModel.TwoDDocument.Paths[1].Id);
            Assert.True(viewModel.TwoDDocument.Paths[1].Points[0].X > viewModel.TwoDDocument.Paths[0].Points[0].X);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static Editor2DPreviewDocument Document(string id, double x)
        {
            var path = new Editor2DPreviewPath(id, "LINE", [new Editor2DPoint(x, 0), new Editor2DPoint(x + 10, 0)], false);
            return new Editor2DPreviewDocument(
                [path],
                new Editor2DBounds(x, 0, x + 10, 0),
                new Dictionary<string, int> { ["LINE"] = 1 },
                []);
        }
    }

    [Fact]
    public async Task LoadingPendingDxf_AppliesConfirmedImportUnitCorrection()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pathstitch-2d-units-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, "imported.stch");
        var dxfPath = Path.Combine(directory, "inch-drawing.dxf");
        try
        {
            await new Project3DStateService().SaveAsync(projectPath, new Project3DState(null, [], []));
            File.WriteAllText(dxfPath, "placeholder");
            var previewPath = new Editor2DPreviewPath("inch-line", "LINE", [new(0, 0), new(10, 0)], false);
            var preview = new Editor2DPreviewDocument(
                [previewPath],
                new Editor2DBounds(0, 0, 10, 0),
                new Dictionary<string, int> { ["LINE"] = 1 },
                []);
            var previewService = new MappingOutputPreviewService(
                new Dictionary<string, Editor2DPreviewDocument> { [Path.GetFullPath(dxfPath)] = preview },
                new Dictionary<string, Editor2DImportUnitsInfo>
                {
                    [Path.GetFullPath(dxfPath)] = new(dxfPath, 1, 25.4, 10, 1),
                });
            var prompt = new RecordingImportUnitsPrompt(25.4);
            var session = new ProjectSession(
                Guid.NewGuid(), "Imported inches", projectPath,
                new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
                ProjectSessionOrigin.Imported, DateTimeOffset.UtcNow);
            var viewModel = CreateViewModelForTests(
                outputPreviewService: previewService,
                importUnitsPromptService: prompt);

            Assert.True(await viewModel.ConfigureParametersAsync(new Dictionary<string, object>
            {
                [EditorNavigationParameterKeys.ProjectSession] = session,
                [EditorNavigationParameterKeys.PendingTwoDFilePaths] = new[] { dxfPath },
            }, CancellationToken.None));
            await ((INavigablePageViewModel)viewModel).LoadAsync(CancellationToken.None);

            var path = Assert.Single(viewModel.TwoDDocument!.Paths);
            Assert.Equal(254, path.Points[1].X, 6);
            Assert.Equal(Path.GetFullPath(dxfPath), prompt.LastInfo?.SourcePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchMode_PreservesBothToolsAndRejectsEditorShortcuts()
    {
        var viewModel = CreateViewModel();
        viewModel.ActivateMoveTool();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateTwoDCircleTool();

        await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        var handled = viewModel.TryActivateEditorShortcut("select");

        Assert.False(handled);
        Assert.Equal(EditorMode.Batch, viewModel.ActiveEditorMode);
        Assert.Equal(Editor3DTool.Move, viewModel.ActiveTool);
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
    }

    [Theory]
    [MemberData(nameof(CatalogShortcutDescriptors))]
    public async Task CatalogShortcut_ActivatesEveryDescriptor(EditorToolDescriptor descriptor)
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(descriptor.Mode);
        var wasOrthographic = viewModel.ThreeDOrthographic;

        var handled = viewModel.TryActivateEditorShortcut(descriptor.ShortcutText!);

        Assert.True(handled);
        if (descriptor.TwoDTool is Editor2DTool twoDTool)
            Assert.Equal(twoDTool, viewModel.TwoDActiveTool);
        if (descriptor.ThreeDTool is Editor3DTool threeDTool)
            Assert.Equal(threeDTool, viewModel.ActiveTool);
        if (descriptor.Action == EditorSidebarAction.ToggleOrthographic)
            Assert.NotEqual(wasOrthographic, viewModel.ThreeDOrthographic);
    }

    [Fact]
    public async Task IdenticalShortcut_ResolvesDifferentlyAcrossModes()
    {
        var viewModel = CreateViewModel();

        Assert.True(viewModel.TryActivateEditorShortcut("3"));
        Assert.Equal(Editor3DTool.Plane, viewModel.ActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        Assert.True(viewModel.TryActivateEditorShortcut("3"));
        Assert.Equal(Editor2DTool.Pan, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task SemanticCommandNames_AreNotRemappedToUnrelatedTwoDTools()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        viewModel.ActivateTwoDCircleTool();

        Assert.False(viewModel.TryActivateEditorShortcut("project"));
        Assert.False(viewModel.TryActivateEditorShortcut("unfold"));
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);
    }

    [Fact]
    public async Task CustomizedCatalog_FeedsRailSearchShortcutsAndPersistedStateFromTheSameSet()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        viewModel.CustomizeTool("2d.circle", order: -10, shortcutText: "G");

        var railIdentifiers = viewModel.SidebarTools.Select(item => item.Identifier).ToArray();
        var searchIdentifiers = viewModel.CommandSearchResults.Select(item => item.Identifier).ToArray();
        var persistedIdentifiers = viewModel.ToolCustomizations
            .Where(customization => customization.Identifier.StartsWith("2d.", StringComparison.Ordinal))
            .Select(customization => customization.Identifier)
            .Order()
            .ToArray();
        Assert.Equal("2d.circle", railIdentifiers[0]);
        Assert.Equal(railIdentifiers, searchIdentifiers);
        Assert.Equal(railIdentifiers.Order(), persistedIdentifiers);
        Assert.Equal(
            EditorToolCatalog.ForMode(EditorMode.TwoD).Select(descriptor => descriptor.Identifier).Order(),
            railIdentifiers.Order());

        viewModel.ActivateTwoDSelectTool();
        Assert.False(viewModel.TryActivateEditorShortcut("C"));
        Assert.True(viewModel.TryActivateEditorShortcut("G"));
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);

        viewModel.CommandSearchQuery = "circle";
        var result = Assert.Single(viewModel.CommandSearchResults);
        Assert.Equal("2d.circle", result.Identifier);
        Assert.Equal("G", result.ShortcutText);
        viewModel.ActivateCommandSearchItem(result.Identifier);
        Assert.Equal(string.Empty, viewModel.CommandSearchQuery);
    }

    [Fact]
    public async Task CommandSearch_ReportsAnEmptyStateForUnmatchedQueries()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.False(viewModel.IsCommandSearchOpen);
        Assert.False(viewModel.IsCommandSearchEmpty);

        viewModel.CommandSearchQuery = "definitely-not-a-tool";

        Assert.True(viewModel.IsCommandSearchOpen);
        Assert.True(viewModel.IsCommandSearchEmpty);
        Assert.Empty(viewModel.CommandSearchResults);
    }

    [Fact]
    public void WorkspaceState_RoundTripsToolCustomizationByStableIdentifier()
    {
        var state = new EditorWorkspaceState(
            Editor3DTool.Select,
            ThreeDOrthographic: false,
            ShowTwoDWorkspace: false,
            ToolCustomizations:
            [
                new EditorToolCustomization("2d.circle", -10, "G"),
                new EditorToolCustomization("3d.project", 2, "P"),
            ]);

        var restored = JsonSerializer.Deserialize<EditorWorkspaceState>(JsonSerializer.Serialize(state));

        Assert.NotNull(restored);
        Assert.Equal(state.ToolCustomizations, restored.ToolCustomizations);
        Assert.Contains(restored.ToolCustomizations!, item => item.Identifier == "2d.circle" && item.ShortcutText == "G");
    }

    [Fact]
    public async Task SidebarTools_AreScopedToTheActiveEditorMode()
    {
        var viewModel = CreateViewModel();

        Assert.NotEmpty(viewModel.SidebarTools);
        Assert.All(viewModel.SidebarTools, item => Assert.Equal(EditorMode.ThreeD, item.Mode));
        Assert.Contains(viewModel.SidebarTools, item => item.Tool == Editor3DTool.Select);
        Assert.Contains(viewModel.SidebarTools, item => item.Tool == Editor3DTool.Move);
        Assert.Contains(viewModel.SidebarTools, item => item.Tool == Editor3DTool.Plane);
        Assert.DoesNotContain(viewModel.SidebarTools, item => item.TwoDTool is not null);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.Equal(EditorToolCatalog.ForMode(EditorMode.TwoD).Count, viewModel.SidebarTools.Count);
        Assert.All(viewModel.SidebarTools, item => Assert.Equal(EditorMode.TwoD, item.Mode));
        Assert.DoesNotContain(viewModel.SidebarTools, item => item.Tool is not null);

        viewModel.ActivateSidebarItem("circle");
        Assert.Equal(Editor2DTool.SketchCircle, viewModel.TwoDActiveTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.Batch);
        Assert.Empty(viewModel.SidebarTools);
    }

    [Fact]
    public async Task ChangingMode_NotifiesTheSharedSidebarCollection()
    {
        var viewModel = CreateViewModel();
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.Contains(nameof(EditorPageViewModel.SidebarTools), changes);
    }

    [Theory]
    [InlineData(Editor3DTool.Select)]
    [InlineData(Editor3DTool.Move)]
    [InlineData(Editor3DTool.Plane)]
    [InlineData(Editor3DTool.Measure)]
    [InlineData(Editor3DTool.Unfold)]
    [InlineData(Editor3DTool.Output)]
    public async Task TwoDMode_HidesEveryThreeDInspectorPanel(Editor3DTool threeDTool)
    {
        var viewModel = CreateViewModel();
        viewModel.ActivateTool(threeDTool);

        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);

        Assert.False(viewModel.ShowSelectionPanel);
        Assert.False(viewModel.ShowMoveBodiesPanel);
        Assert.False(viewModel.ShowProjectionPanel);
        Assert.False(viewModel.ShowMeasurePanel);
        Assert.False(viewModel.ShowUnfoldPanel);
        Assert.False(viewModel.ShowOutputPanel);
    }

    [Fact]
    public async Task ThreeDMode_HidesTheTwoDInspectorContext()
    {
        var viewModel = CreateViewModel();
        await viewModel.SetActiveEditorModeAsync(EditorMode.TwoD);
        await viewModel.SetActiveEditorModeAsync(EditorMode.ThreeD);

        Assert.True(viewModel.IsShowing3DWorkspace);
        Assert.False(viewModel.IsShowingTwoDWorkspace);
    }

    [Fact]
    public void WorkspaceState_LegacyJsonWithoutModeRemainsCompatible()
    {
        const string legacyJson = """
            {
              "activeTool": 1,
              "threeDOrthographic": true,
              "showGeneratedOutputWorkspace": true,
              "generatedOutputActiveTool": 6
            }
            """;

        var state = JsonSerializer.Deserialize<EditorWorkspaceState>(legacyJson);

        Assert.NotNull(state);
        Assert.Null(state.ActiveEditorMode);
        Assert.True(state.ShowTwoDWorkspace);
        Assert.Equal(Editor3DTool.Move, state.ActiveTool);
        Assert.Equal(Editor2DTool.SketchCircle, state.TwoDActiveTool);
    }

    [Fact]
    public void WorkspaceState_TwoDPropertiesRetainLegacyStchJsonFieldNames()
    {
        var state = new EditorWorkspaceState(
            Editor3DTool.Select,
            ThreeDOrthographic: false,
            ShowTwoDWorkspace: true,
            TwoDActiveTool: Editor2DTool.SketchRectangle,
            TwoDPolygonSides: 9,
            TwoDViewportZoom: 2.5,
            TwoDViewportOffsetX: 4,
            TwoDViewportOffsetY: -3,
            TwoDExpandedRectanglePathIds: ["rectangle-1"]);

        var json = JsonSerializer.Serialize(state);

        Assert.Contains("\"showGeneratedOutputWorkspace\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"generatedOutputActiveTool\":5", json, StringComparison.Ordinal);
        Assert.Contains("\"generatedOutputPolygonSides\":9", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"twoDActiveTool\"", json, StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<EditorWorkspaceState>(json);
        Assert.NotNull(restored);
        Assert.Equal(Editor2DTool.SketchRectangle, restored.TwoDActiveTool);
        Assert.Equal(["rectangle-1"], restored.TwoDExpandedRectanglePathIds);
    }

    [Fact]
    public void WorkspaceState_RoundTripsExplicitBatchMode()
    {
        var state = new EditorWorkspaceState(
            Editor3DTool.Move,
            ThreeDOrthographic: true,
            ShowTwoDWorkspace: false,
            Editor2DTool.SketchCircle,
            ActiveEditorMode: EditorMode.Batch);

        var restored = JsonSerializer.Deserialize<EditorWorkspaceState>(JsonSerializer.Serialize(state));

        Assert.NotNull(restored);
        Assert.Equal(EditorMode.Batch, restored.ActiveEditorMode);
        Assert.Equal(Editor3DTool.Move, restored.ActiveTool);
        Assert.Equal(Editor2DTool.SketchCircle, restored.TwoDActiveTool);
    }

    public static EditorPageViewModel CreateViewModelForTests(
        IProjectFileDialogService? projectFileDialogService = null,
        IEditorOutputPreviewService? outputPreviewService = null,
        IUnsavedChangesPromptService? unsavedChangesPromptService = null,
        IEditorImportUnitsPromptService? importUnitsPromptService = null)
        => CreateViewModel(projectFileDialogService, outputPreviewService, unsavedChangesPromptService, importUnitsPromptService);

    private static EditorPageViewModel CreateViewModel(
        IProjectFileDialogService? projectFileDialogService = null,
        IEditorOutputPreviewService? outputPreviewService = null,
        IUnsavedChangesPromptService? unsavedChangesPromptService = null,
        IEditorImportUnitsPromptService? importUnitsPromptService = null)
        => new(
            NullLogger<EditorPageViewModel>.Instance,
            new StubViewportAssetLocator(),
            new Project3DStateService(),
            projectFileDialogService ?? new StubProjectFileDialogService(),
            new StubOutputLauncherService(),
            outputPreviewService ?? new StubOutputPreviewService(),
            new Stub2DGeometryKernelService(),
            new Stub3DOperationService(),
            new StubGeometryKernelDescriptorProvider(),
            unsavedChangesPromptService: unsavedChangesPromptService,
            importUnitsPromptService: importUnitsPromptService);

    private sealed class StubViewportAssetLocator : IEditorViewportAssetLocator
    {
        public string GetViewportHtml() => string.Empty;

        public Uri GetViewportBaseUri() => new("file:///viewport/");
    }

    private sealed class StubProjectFileDialogService : IProjectFileDialogService
    {
        public Task<string?> PickExistingProjectFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<string?> PickNewProjectFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickWorkspaceFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> PickSourceModelFilesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSourceModelFileAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StubOutputLauncherService : IEditorOutputLauncherService
    {
        public Task OpenOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RevealOutputAsync(string outputPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubOutputPreviewService : IEditorOutputPreviewService
    {
        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<Editor2DPreviewDocument?>(null);

        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }

    private sealed class MappingOutputPreviewService(
        IReadOnlyDictionary<string, Editor2DPreviewDocument> documents,
        IReadOnlyDictionary<string, Editor2DImportUnitsInfo>? importUnits = null) : IEditorOutputPreviewService
    {
        public Task<Editor2DPreviewDocument?> LoadPreviewDocumentAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.TryGetValue(Path.GetFullPath(outputPath), out var document);
            return Task.FromResult(document);
        }

        public Task SavePreviewDocumentAsync(Editor2DPreviewDocument document, string outputPath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<Editor2DImportUnitsInfo?> InspectImportUnitsAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            Editor2DImportUnitsInfo? info = null;
            importUnits?.TryGetValue(Path.GetFullPath(outputPath), out info);
            return Task.FromResult(info);
        }

        public Task<EditorGeneratedOutputSummary?> InspectOutputAsync(string outputPath, CancellationToken cancellationToken = default)
            => Task.FromResult<EditorGeneratedOutputSummary?>(null);
    }

    private sealed class RecordingImportUnitsPrompt(double factor) : IEditorImportUnitsPromptService
    {
        public Editor2DImportUnitsInfo? LastInfo { get; private set; }

        public Task<double?> PromptAsync(Editor2DImportUnitsInfo info, CancellationToken cancellationToken = default)
        {
            LastInfo = info;
            return Task.FromResult<double?>(factor);
        }
    }

    private sealed class Stub2DGeometryKernelService : IEditor2DGeometryKernelService
    {
        public Task<Editor2DGeometryKernelResult> BuildCurveOffsetPathsAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double offsetDistance,
            bool offsetOutward,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success([]));

        public Task<Editor2DGeometryKernelResult> BuildThicknessOutlinesAsync(
            IReadOnlyList<Editor2DPreviewPath> sourcePaths,
            double thickness,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Editor2DGeometryKernelResult.Success([]));
    }

    private sealed class Stub3DOperationService : IEditor3DOperationService
    {
        public Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorModelLoadResult(false, "Not used."));

        public Task<EditorModelLoadResult> LoadModelsAsync(
            IReadOnlyList<string> sourceModelPaths,
            string? existingSourceModelPath = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorModelLoadResult(false, "Not used."));

        public Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(
            string? sourceModelPath,
            SelectedFace3D selectedFace,
            string distortionMode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorFaceDistortionResult(false, "Not used."));

        public Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorOperationResult(false, "Not used."));

        public Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new EditorOperationResult(false, "Not used."));
    }

    private sealed class StubGeometryKernelDescriptorProvider : IGeometryKernelDescriptorProvider
    {
        public GeometryKernelDescriptor Current { get; } = new(
            "Test kernel",
            "Test implementation",
            "Test runtime",
            "Test capabilities",
            "Test requirements",
            "Test source models");
    }
}
