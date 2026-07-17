using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Domain.App.Models;
using Domain.App.Services;
using Domain.App.ViewModels;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class Project3DStateServiceTests
{
    [Fact]
    public async Task LoadAsync_MigratesLegacyEditableCornersAndPenAnchors()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("legacy-editable-geometry.stch");
        const string dxf =
            "0\nSECTION\n2\nENTITIES\n" +
            "0\nLWPOLYLINE\n5\nC1\n8\nCut\n90\n4\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n10\n20\n10\n10\n0\n20\n10\n" +
            "0\nLWPOLYLINE\n5\nP1\n8\nPen\n90\n3\n70\n0\n10\n20\n20\n0\n10\n25\n20\n5\n10\n30\n20\n0\n" +
            "0\nENDSEC\n0\nEOF\n";
        var payload = JsonSerializer.Serialize(new
        {
            dxfDataBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(dxf)),
            parametricShapes = new Dictionary<string, object>
            {
                ["C1"] = new
                {
                    @base = new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 10.0 }, new[] { 0.0, 10.0 } },
                    closed = true,
                    corners = new[] { new { index = 1, kind = "fillet", value = 2.5, continuity = "G2" } },
                },
            },
            penPaths = new Dictionary<string, object>
            {
                ["P1"] = new
                {
                    closed = false,
                    anchors = new[]
                    {
                        new { point = new[] { 20.0, 0.0 }, handleIn = (double[]?)null, handleOut = (double[]?)new[] { 22.0, 3.0 } },
                        new { point = new[] { 25.0, 5.0 }, handleIn = (double[]?)new[] { 23.0, 5.0 }, handleOut = (double[]?)new[] { 27.0, 5.0 } },
                        new { point = new[] { 30.0, 0.0 }, handleIn = (double[]?)new[] { 28.0, 3.0 }, handleOut = (double[]?)null },
                    },
                },
            },
        });
        await File.WriteAllTextAsync(projectPath, payload);
        var service = new Project3DStateService(new DxfOutputPreviewService());

        var restored = await service.LoadAsync(projectPath);

        var state = Assert.IsType<Editor2DWorkspaceState>(restored.TwoDWorkspaceState);
        var cornerPath = Assert.Single(state.Document.Paths, path => path.SourceEntityHandle == "C1");
        var corner = Assert.Single(state.CornerParameters!);
        Assert.Equal(cornerPath.Id, corner.PathId);
        Assert.Equal(1, corner.CornerIndex);
        Assert.Equal(Editor2DCornerKind.Fillet, corner.Kind);
        Assert.Equal(Editor2DFilletContinuity.G2, corner.Continuity);
        Assert.Equal(2.5, corner.Value);
        Assert.Equal([new Editor2DPoint(0, 0), new Editor2DPoint(10, 0), new Editor2DPoint(10, 10), new Editor2DPoint(0, 10)], corner.SourcePoints);
        var penPath = Assert.Single(state.Document.Paths, path => path.SourceEntityHandle == "P1");
        Assert.False(penPath.IsClosed);
        Assert.Equal(3, penPath.BezierAnchors!.Count);
        Assert.Equal(new Editor2DPoint(22, 3), penPath.BezierAnchors[0].HandleOut);
        Assert.Equal(new Editor2DPoint(23, 5), penPath.BezierAnchors[1].HandleIn);
        Assert.Equal(new Editor2DPoint(30, 0), penPath.BezierAnchors[2].Point);

        await service.SaveAsync(projectPath, restored);
        var reopened = Assert.IsType<Editor2DWorkspaceState>((await service.LoadAsync(projectPath)).TwoDWorkspaceState);
        var reopenedCorner = Assert.Single(reopened.CornerParameters!);
        Assert.Equal(corner.PathId, reopenedCorner.PathId);
        Assert.Equal(corner.SourcePoints, reopenedCorner.SourcePoints);
        var reopenedPen = Assert.Single(reopened.Document.Paths, path => path.Id == penPath.Id);
        Assert.Equal(penPath.BezierAnchors, reopenedPen.BezierAnchors);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyMacLayerStackFoldersAndMeasurements()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("legacy-layers.stch");
        const string dxf =
            "0\nSECTION\n2\nENTITIES\n" +
            "0\nLINE\n5\nA1\n8\nCut\n10\n0\n20\n0\n11\n10\n21\n0\n" +
            "0\nLINE\n5\nB2\n8\nScore\n10\n0\n20\n5\n11\n10\n21\n5\n" +
            "0\nENDSEC\n0\nEOF\n";
        const string onePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        var payload = JsonSerializer.Serialize(new
        {
            dxfDataBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(dxf)),
            savedLayers = new object[]
            {
                new { id = "cut-id", name = "Cut", colorHex = "#FF0000", visible = true, locked = false, parentFolderId = "folder-child" },
                new { id = "score-id", name = "Score", colorHex = "#0000FF", visible = false, locked = true, parentFolderId = "folder-child" },
                new
                {
                    id = "reference-id", name = "Pattern", colorHex = "#00FF00", visible = true, locked = true,
                    parentFolderId = "folder-root", isReferenceImageLayer = true, refImageBase64 = onePixelPng,
                    refImageOffsetX = 8.0, refImageOffsetY = -3.0, refImageScaleX = 2.0, refImageScaleY = 3.0,
                    refImageWidth = 10.0, refImageHeight = 20.0, refImagePixelWidth = 1.0, refImagePixelHeight = 1.0,
                    refImageRotation = 15.0, refImageDepth = "front", refImageOpacity = 0.35,
                    refImageOriginalBase64 = onePixelPng, backgroundRemoved = true,
                },
            },
            savedLayerFolders = new[]
            {
                new { id = "folder-root", name = "References", parentFolderId = (string?)null },
                new { id = "folder-child", name = "Geometry", parentFolderId = (string?)"folder-root" },
            },
            savedActiveLayerId = "score-id",
            measurements = new[]
            {
                new
                {
                    id = "11111111-1111-1111-1111-111111111111",
                    start = new { x = 0.0, y = 0.0 }, end = new { x = 10.0, y = 0.0 }, distanceMm = 10.0,
                    isAutoDimension = true, entityHandle = "A1", dimensionType = "length", varName = "d1",
                    expression = "10", driven = false, isParametric = true, offsetDistance = 2.0,
                },
            },
        });
        await File.WriteAllTextAsync(projectPath, payload);
        var service = new Project3DStateService(new DxfOutputPreviewService());

        var restored = await service.LoadAsync(projectPath);

        var state = Assert.IsType<Editor2DWorkspaceState>(restored.TwoDWorkspaceState);
        Assert.Equal("score-id", state.ActiveLayerId);
        var cut = Assert.Single(state.Layers!, layer => layer.Id == "cut-id");
        var score = Assert.Single(state.Layers!, layer => layer.Id == "score-id");
        Assert.Equal("#FF0000", cut.ColorHex);
        Assert.True(cut.IsVisible);
        Assert.False(cut.IsLocked);
        Assert.Equal("folder-child", cut.ParentFolderId);
        Assert.Single(cut.PathIds);
        Assert.False(score.IsVisible);
        Assert.True(score.IsLocked);
        Assert.Single(score.PathIds);
        Assert.NotEqual(cut.PathIds[0], score.PathIds[0]);
        var reference = Assert.Single(state.Layers!, layer => layer.Id == "reference-id").ReferenceImage!;
        Assert.Equal(20.0, reference.Width);
        Assert.Equal(60.0, reference.Height);
        Assert.Equal(15.0, reference.RotationDegrees);
        Assert.Equal(Editor2DReferenceImageDepth.Front, reference.Depth);
        Assert.True(reference.BackgroundRemoved);
        Assert.Equal(2, state.Folders!.Count);
        Assert.Equal("folder-root", state.Folders.Single(folder => folder.Id == "folder-child").ParentFolderId);
        var measurement = Assert.Single(state.Measurements!);
        Assert.Equal(cut.PathIds[0], measurement.EntityPathId);
        Assert.Equal("d1", measurement.VarName);
        Assert.Equal("10", measurement.Expression);

        await service.SaveAsync(projectPath, restored);
        var reopened = Assert.IsType<Editor2DWorkspaceState>((await service.LoadAsync(projectPath)).TwoDWorkspaceState);
        Assert.Equal(state.ActiveLayerId, reopened.ActiveLayerId);
        Assert.Equal(state.Layers!.Select(layer => layer.Id), reopened.Layers!.Select(layer => layer.Id));
        Assert.Equal(measurement.EntityPathId, Assert.Single(reopened.Measurements!).EntityPathId);
        Assert.Equal(reference, Assert.Single(reopened.Layers!, layer => layer.Id == "reference-id").ReferenceImage);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyMacDrawingViewportAndReferenceImage()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("legacy-2d.stch");
        const string dxf = "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n2\n20\n3\n11\n8\n21\n3\n0\nENDSEC\n0\nEOF\n";
        const string onePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        var payload = JsonSerializer.Serialize(new
        {
            dxfDataBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(dxf)),
            canvasScale = 1.75,
            canvasOffsetX = 42.5,
            canvasOffsetY = -18.25,
            refImageBase64 = onePixelPng,
            refImageOffsetX = 12.0,
            refImageOffsetY = -6.0,
            refImageScale = 2.5,
            refImageOpacity = 0.4,
        });
        await File.WriteAllTextAsync(projectPath, payload);
        var service = new Project3DStateService(new DxfOutputPreviewService());

        var restored = await service.LoadAsync(projectPath);

        var state = Assert.IsType<Editor2DWorkspaceState>(restored.TwoDWorkspaceState);
        Assert.Equal(1.75, state.ViewportZoom);
        Assert.Equal(42.5, state.ViewportOffsetX);
        Assert.Equal(-18.25, state.ViewportOffsetY);
        var line = Assert.Single(state.Document.Paths);
        Assert.Equal(new Editor2DPoint(2, 3), line.Points[0]);
        Assert.Equal(new Editor2DPoint(8, 3), line.Points[1]);
        var reference = Assert.Single(state.Layers!, layer => layer.IsReferenceImage).ReferenceImage!;
        Assert.Equal(12.0, reference.X);
        Assert.Equal(-6.0, reference.Y);
        Assert.Equal(2.5, reference.Width);
        Assert.Equal(2.5, reference.Height);
        Assert.Equal(0.4, reference.Opacity);
        Assert.Equal(2.5, reference.CalibrationUnitsPerPixel);

        await service.SaveAsync(projectPath, restored);
        var reopened = await service.LoadAsync(projectPath);
        var reopenedState = Assert.IsType<Editor2DWorkspaceState>(reopened.TwoDWorkspaceState);
        Assert.Equal(state.ViewportZoom, reopenedState.ViewportZoom);
        Assert.Equal(state.ViewportOffsetX, reopenedState.ViewportOffsetX);
        Assert.Equal(state.ViewportOffsetY, reopenedState.ViewportOffsetY);
        var reopenedLine = Assert.Single(reopenedState.Document.Paths);
        Assert.Equal(line.Id, reopenedLine.Id);
        Assert.Equal(line.EntityType, reopenedLine.EntityType);
        Assert.Equal(line.Points, reopenedLine.Points);
        Assert.Equal(reference, Assert.Single(reopenedState.Layers!, layer => layer.IsReferenceImage).ReferenceImage);
    }

    [Fact]
    public async Task SaveAsync_WritesAndReplacesProjectPreviewEntry()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("preview.stch");
        var service = new Project3DStateService();
        var preview = await new ProjectPreviewRenderer().RenderAsync(null);
        Assert.NotNull(preview);

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], PreviewImageData: preview));

        await using (var file = File.OpenRead(projectPath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
        {
            var entry = archive.GetEntry("preview.png");
            Assert.NotNull(entry);
            await using var stream = entry.Open();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            Assert.Equal(preview, memory.ToArray());
        }

        await service.SaveAsync(projectPath, Project3DState.Empty);

        await using var replacedFile = File.OpenRead(projectPath);
        using var replacedArchive = new ZipArchive(replacedFile, ZipArchiveMode.Read);
        Assert.Null(replacedArchive.GetEntry("preview.png"));
    }

    [Fact]
    public async Task SaveAsync_OmitsInvalidAndOversizedPreviewData()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("invalid-preview.stch");
        var service = new Project3DStateService();
        byte[][] invalidPreviews =
        [
            [1, 2, 3, 4, 5, 6, 7, 8, 9],
            new byte[4 * 1024 * 1024 + 1],
        ];

        foreach (var preview in invalidPreviews)
        {
            await service.SaveAsync(
                projectPath,
                new Project3DState(null, [], [], PreviewImageData: preview));
            await using var file = File.OpenRead(projectPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            Assert.Null(archive.GetEntry("preview.png"));
        }
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsActivityLog()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("activity-log.stch");
        var service = new Project3DStateService();
        var timestamp = new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero);
        var activity = new EditorActivityEntry("entry-1", timestamp, "Import Reference Images", "2 images", "layer-1");

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], ActivityLog: [activity]));

        var restored = await service.LoadAsync(projectPath);

        Assert.Equal(activity, Assert.Single(restored.ActivityLog!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_RestoresLegacyMacActivityLog(bool zipContainer)
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath(zipContainer ? "legacy-log-zip.stch" : "legacy-log-json.stch");
        const string payload =
            """
            {
              "logEntries": [
                {
                  "id": "legacy-entry",
                  "timestamp": 0,
                  "action": "Import Reference Image",
                  "details": "pattern.png",
                  "layerAffected": "layer-7"
                }
              ]
            }
            """;
        if (zipContainer)
        {
            await using var file = File.Create(projectPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("project.json");
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync(payload);
        }
        else
        {
            File.WriteAllText(projectPath, payload);
        }
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        var activity = Assert.Single(state.ActivityLog!);
        Assert.Equal("legacy-entry", activity.Id);
        Assert.Equal(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero), activity.TimestampUtc);
        Assert.Equal("Import Reference Image", activity.Action);
        Assert.Equal("pattern.png", activity.Details);
        Assert.Equal("layer-7", activity.LayerId);
    }

    [Fact]
    public async Task LoadAsync_SkipsMalformedLegacyActivityEntries()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.WriteText(
            "partially-malformed-log.stch",
            """
            {
              "logEntries": [
                { "id": "bad-date", "timestamp": "not-a-date", "action": "Bad", "details": "Ignored" },
                { "timestamp": 0, "action": "Missing id", "details": "Ignored" },
                {
                  "id": "valid-entry",
                  "timestamp": "2026-07-17T10:30:00Z",
                  "action": "Valid",
                  "details": "Restored"
                }
              ]
            }
            """);
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        var activity = Assert.Single(state.ActivityLog!);
        Assert.Equal("valid-entry", activity.Id);
        Assert.Equal(new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero), activity.TimestampUtc);
    }

    [Fact]
    public async Task LoadAsync_PrefersCanonicalActivityLog()
    {
        using var workspace = TestWorkspace.Create();
        var canonical = new EditorActivityEntry(
            "canonical-entry",
            new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero),
            "Canonical",
            "Preferred");
        var projectPath = workspace.WriteText(
            "conflicting-activity-log.stch",
            JsonSerializer.Serialize(new
            {
                savedActivityLog = new[] { canonical },
                logEntries = new[]
                {
                    new
                    {
                        id = "legacy-entry",
                        timestamp = 0,
                        action = "Legacy",
                        details = "Ignored",
                    },
                },
            }));
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.Equal(canonical, Assert.Single(state.ActivityLog!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_RestoresLegacyMacBatchItems(bool zipContainer)
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath(zipContainer ? "legacy-batch-zip.stch" : "legacy-batch-json.stch");
        var firstData = Convert.ToBase64String([1, 2, 3]);
        var secondData = Convert.ToBase64String([4, 5, 6]);
        var payload = JsonSerializer.Serialize(new
        {
            batchItems = new object[]
            {
                new { originalName = "pattern.png", dxfDataBase64 = firstData, isSelected = true },
                new { originalName = "SECOND", dxfDataBase64 = secondData, isSelected = false },
                new { originalName = "broken.dxf", dxfDataBase64 = "not-base64", isSelected = true },
                new { originalName = "", dxfDataBase64 = firstData, isSelected = true },
            },
        });
        if (zipContainer)
        {
            await using var file = File.Create(projectPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("project.json");
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync(payload);
        }
        else
        {
            File.WriteAllText(projectPath, payload);
        }
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.NotNull(state.BatchWorkspaceState);
        Assert.NotNull(state.BatchWorkspaceState.Items);
        Assert.Collection(
            state.BatchWorkspaceState.Items,
            item =>
            {
                Assert.Equal("pattern.png", item.FileName);
                Assert.Equal(firstData, item.SourceDataBase64);
                Assert.True(item.IsSelected);
                Assert.Equal(".dxf", item.SourceFileExtension);
            },
            item =>
            {
                Assert.Equal("SECOND", item.FileName);
                Assert.Equal(secondData, item.SourceDataBase64);
                Assert.False(item.IsSelected);
                Assert.Equal(".dxf", item.SourceFileExtension);
            });
    }

    [Fact]
    public async Task LoadAsync_PrefersCanonicalBatchWorkspaceState()
    {
        using var workspace = TestWorkspace.Create();
        var canonical = new EditorBatchWorkspaceState(
            [new EditorBatchItemState("canonical.dxf", SourceDataBase64: Convert.ToBase64String([7]))]);
        var projectPath = workspace.WriteText(
            "conflicting-batch-state.stch",
            JsonSerializer.Serialize(new
            {
                savedBatchWorkspaceState = canonical,
                batchItems = new[]
                {
                    new
                    {
                        originalName = "legacy.dxf",
                        dxfDataBase64 = Convert.ToBase64String(new byte[] { 8 }),
                        isSelected = false,
                    },
                },
            }));
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.NotNull(state.BatchWorkspaceState);
        Assert.Equal("canonical.dxf", Assert.Single(state.BatchWorkspaceState.Items!).FileName);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsDisabledLearnMode()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("learn-mode.stch");
        var service = new Project3DStateService();

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], LearnModeEnabled: false));

        var restored = await service.LoadAsync(projectPath);

        Assert.False(restored.LearnModeEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_RestoresLegacyMacLearnModeSetting(bool zipContainer)
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath(zipContainer ? "legacy-learn-zip.stch" : "legacy-learn-json.stch");
        const string payload = "{\"isLearnModeEnabled\":false}";
        if (zipContainer)
        {
            await using var file = File.Create(projectPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("project.json");
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync(payload);
        }
        else
        {
            File.WriteAllText(projectPath, payload);
        }
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.False(state.LearnModeEnabled);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task LoadAsync_PrefersCanonicalLearnModeSetting(bool canonical, bool legacy)
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.WriteText(
            "conflicting-learn-mode.stch",
            JsonSerializer.Serialize(new
            {
                savedLearnModeEnabled = canonical,
                isLearnModeEnabled = legacy,
            }));
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.Equal(canonical, state.LearnModeEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_ExposesLegacyMacMeasurementLineExportSetting(bool zipContainer)
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath(zipContainer ? "legacy-export-zip.stch" : "legacy-export-json.stch");
        const string payload = "{\"exportMeasurementLines\":true}";
        if (zipContainer)
        {
            await using var file = File.Create(projectPath);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create);
            var entry = archive.CreateEntry("project.json");
            await using var entryStream = entry.Open();
            await using var writer = new StreamWriter(entryStream);
            await writer.WriteAsync(payload);
        }
        else
        {
            File.WriteAllText(projectPath, payload);
        }
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.True(state.LegacyExportMeasurementLines);
    }

    [Fact]
    public async Task LoadAsync_MapsLegacySavedStepJsonToViewportJson()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.WriteText(
            "legacy-project.json",
            """
            {
              "savedStepJson": "{\"bodies\":[],\"bbox\":{}}",
              "savedBodies3D": []
            }
            """);
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.Equal("{\"bodies\":[],\"bbox\":{}}", state.ViewportJson);
        Assert.True(state.HasModel);
        Assert.Empty(state.ActivityLog!);
        Assert.True(state.LearnModeEnabled);
    }

    [Fact]
    public async Task LoadAsync_RestoresPlainJsonStchState()
    {
        using var workspace = TestWorkspace.Create();
        var body = new Body3D(7, "Shell", [new Face3D(3, "Planar", 42.5) { BodyIndex = 7 }]);
        var activity = new EditorActivityEntry(
            "entry-plain-json",
            new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero),
            "Unfold",
            "1 body",
            "layer-7");
        var twoDWorkspace = Editor2DWorkspaceState.Empty with
        {
            ActiveTool = Editor2DTool.SketchLine,
            IsInitialized = true,
            ViewportZoom = 2.5,
        };
        var projectPath = workspace.WriteText(
            "plain-json.stch",
            JsonSerializer.Serialize(new
            {
                savedViewportJson = "{\"bodies\":[{\"body_index\":7}],\"bbox\":{}}",
                savedBodies3D = new[] { body },
                savedBodyOffsets = new[] { new { bodyIndex = 7, x = 1.5, y = -2.0, z = 3.25 } },
                savedActivityLog = new[] { activity },
                savedLearnModeEnabled = false,
                savedTwoDWorkspaceState = twoDWorkspace,
            }));
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.Equal("{\"bodies\":[{\"body_index\":7}],\"bbox\":{}}", state.ViewportJson);
        var restoredBody = Assert.Single(state.Bodies);
        Assert.Equal(body.BodyIndex, restoredBody.BodyIndex);
        Assert.Equal(body.Name, restoredBody.Name);
        Assert.Equal(Assert.Single(body.Faces), Assert.Single(restoredBody.Faces));
        Assert.Equal(new BodyOffset3D(7, 1.5, -2.0, 3.25), Assert.Single(state.BodyOffsets));
        Assert.Equal(activity, Assert.Single(state.ActivityLog!));
        Assert.False(state.LearnModeEnabled);
        Assert.NotNull(state.TwoDWorkspaceState);
        Assert.Equal(Editor2DTool.SketchLine, state.TwoDWorkspaceState.ActiveTool);
        Assert.True(state.TwoDWorkspaceState.IsInitialized);
        Assert.Equal(2.5, state.TwoDWorkspaceState.ViewportZoom);
    }

    [Fact]
    public async Task LoadAsync_RestoresGeneratedOutputFromPlainJsonStch()
    {
        using var workspace = TestWorkspace.Create();
        byte[] expected = [0, 1, 2, 3, 10, 13, 255];
        var projectPath = workspace.WriteText(
            "plain-json-output.stch",
            JsonSerializer.Serialize(new { dxfDataBase64 = Convert.ToBase64String(expected) }));
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.NotNull(state.GeneratedOutputPath);
        Assert.True(File.Exists(state.GeneratedOutputPath));
        Assert.Equal(expected, await File.ReadAllBytesAsync(state.GeneratedOutputPath));
        File.Delete(state.GeneratedOutputPath);
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmptyForMalformedPlainJsonStch()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.WriteText("malformed.stch", "{ not valid json");
        var service = new Project3DStateService();

        var state = await service.LoadAsync(projectPath);

        Assert.Same(Project3DState.Empty, state);
    }

    [Fact]
    public async Task SaveAsync_WritesViewportJsonAndLegacyCompatibilityAlias()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("project.stch");
        var service = new Project3DStateService();
        var body = new Body3D(0, "Body", [new Face3D(0, "Mesh", 50.0) { BodyIndex = 0 }]);

        await service.SaveAsync(
            projectPath,
            new Project3DState(
                ViewportJson: "{\"bodies\":[{\"body_index\":0}],\"bbox\":{}}",
                Bodies: [body],
                BodyOffsets: []));

        await using var fileStream = File.OpenRead(projectPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("project.json");
        Assert.NotNull(entry);
        await using var entryStream = entry.Open();
        using var document = await JsonDocument.ParseAsync(entryStream);
        var root = document.RootElement;

        Assert.Equal("{\"bodies\":[{\"body_index\":0}],\"bbox\":{}}", root.GetProperty("savedViewportJson").GetString());
        Assert.Equal("{\"bodies\":[{\"body_index\":0}],\"bbox\":{}}", root.GetProperty("savedStepJson").GetString());
    }

    [Fact]
    public async Task SaveAndLoadAsync_RestoresJsonMeshWorkspaceAsActiveSourceAsset()
    {
        using var workspace = TestWorkspace.Create();
        var sourcePath = workspace.WriteText(
            "mesh_workspace.json",
            """
            {"bodies":[]}
            """);
        var projectPath = workspace.GetPath("combined.stch");
        var service = new Project3DStateService();

        await service.SaveAsync(
            projectPath,
            new Project3DState(
                ViewportJson: "{\"bodies\":[],\"bbox\":{}}",
                Bodies: [],
                BodyOffsets: [],
                SourceModelPath: sourcePath));

        var restored = await service.LoadAsync(projectPath);

        Assert.NotNull(restored.SourceModelPath);
        Assert.True(File.Exists(restored.SourceModelPath));
        Assert.Equal(".json", Path.GetExtension(restored.SourceModelPath));
        Assert.Equal("{\"bodies\":[]}", await File.ReadAllTextAsync(restored.SourceModelPath));
    }

    [Fact]
    public async Task LoadAsync_RestoresEmbeddedStepAsActiveSourceAsset()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("legacy-step.stch");
        await using (var fileStream = File.Create(projectPath))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var projectEntry = archive.CreateEntry("project.json");
            await using (var projectStream = projectEntry.Open())
            await using (var writer = new StreamWriter(projectStream))
            {
                await writer.WriteAsync(
                    """
                    {
                      "savedViewportJson": "{\"bodies\":[],\"bbox\":{}}",
                      "savedBodies3D": []
                    }
                    """);
            }

            var sourceEntry = archive.CreateEntry("active.step");
            await using var sourceStream = sourceEntry.Open();
            await using var sourceWriter = new StreamWriter(sourceStream);
            await sourceWriter.WriteAsync("ISO-10303-21;");
        }
        var service = new Project3DStateService();

        var restored = await service.LoadAsync(projectPath);

        Assert.NotNull(restored.SourceModelPath);
        Assert.True(File.Exists(restored.SourceModelPath));
        Assert.Equal(".step", Path.GetExtension(restored.SourceModelPath), ignoreCase: true);
        Assert.Equal("ISO-10303-21;", await File.ReadAllTextAsync(restored.SourceModelPath));
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsBlankIndependentTwoDWorkspace()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("blank-2d.stch");
        var service = new Project3DStateService();
        var line = new Editor2DPreviewPath(
            "line-1",
            "LINE",
            [new Editor2DPoint(1, 2), new Editor2DPoint(3, 4)],
            IsClosed: false);
        var document = Editor2DWorkspaceState.Empty.Document with
        {
            Paths = [line],
            Bounds = new Editor2DBounds(1, 2, 3, 4),
            EntityCounts = new Dictionary<string, int> { ["LINE"] = 1 },
        };
        var twoDState = new Editor2DWorkspaceState(
            document,
            ActiveTool: Editor2DTool.Move,
            SelectedPathIds: [line.Id],
            PolygonSides: 8,
            ViewportZoom: 2.5,
            ViewportOffsetX: 12,
            ViewportOffsetY: -4);

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: twoDState));

        var restored = await service.LoadAsync(projectPath);

        Assert.False(restored.HasGeneratedOutput);
        Assert.NotNull(restored.TwoDWorkspaceState);
        Assert.Equal(Editor2DTool.Move, restored.TwoDWorkspaceState.ActiveTool);
        var restoredLine = Assert.Single(restored.TwoDWorkspaceState.Document.Paths);
        Assert.Equal(line.Id, restoredLine.Id);
        Assert.Equal(line.EntityType, restoredLine.EntityType);
        Assert.Equal(line.Points, restoredLine.Points);
        Assert.Equal([line.Id], restored.TwoDWorkspaceState.SelectedPathIds);
        Assert.Equal(2.5, restored.TwoDWorkspaceState.ViewportZoom);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsTwoDLayersAndMembership()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("layers.stch");
        var service = new Project3DStateService();
        var path = new Editor2DPreviewPath("path", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)], false);
        var layer = new Editor2DLayer("cut", "Cut", [path.Id], IsVisible: false, IsLocked: true, ColorHex: "#FF8800");
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            Layers = [layer],
            ActiveLayerId = layer.Id,
        };

        await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: state));
        var restored = await service.LoadAsync(projectPath);

        var restoredLayer = Assert.Single(restored.TwoDWorkspaceState!.Layers!);
        Assert.Equal(layer.Id, restoredLayer.Id);
        Assert.Equal(layer.Name, restoredLayer.Name);
        Assert.Equal(layer.PathIds, restoredLayer.PathIds);
        Assert.False(restoredLayer.IsVisible);
        Assert.True(restoredLayer.IsLocked);
        Assert.Equal("#FF8800", restoredLayer.ColorHex);
        Assert.Equal(layer.Id, restored.TwoDWorkspaceState.ActiveLayerId);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsNestedTwoDFoldersAndLayerMembership()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("nested-layer-folders.stch");
        var service = new Project3DStateService();
        var path = new Editor2DPreviewPath("path", "LINE", [new Editor2DPoint(0, 0), new Editor2DPoint(5, 0)], false);
        var parent = new Editor2DLayerFolder("production", "Production");
        var child = new Editor2DLayerFolder("cut-folder", "Cut", parent.Id);
        var layer = new Editor2DLayer("cut", "Cut lines", [path.Id], ParentFolderId: child.Id);
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            Layers = [layer],
            ActiveLayerId = layer.Id,
            Folders = [parent, child],
        };

        await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: state));
        var restored = await service.LoadAsync(projectPath);

        Assert.Equal([parent, child], restored.TwoDWorkspaceState!.Folders);
        Assert.Equal(child.Id, Assert.Single(restored.TwoDWorkspaceState.Layers!).ParentFolderId);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsReferenceImageLayerWithoutCreatingGeometry()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("reference-image.stch");
        var service = new Project3DStateService();
        var referenceImage = new Editor2DReferenceImage(
            "reference-1",
            "pattern.png",
            Convert.ToBase64String([1, 2, 3, 4]),
            800,
            600,
            X: 12.5,
            Y: -8.5,
            Width: 200,
            Height: 150,
            RotationDegrees: 17,
            Opacity: 0.35,
            CalibrationUnitsPerPixel: 0.25,
            TraceThreshold: 0.72,
            Depth: Editor2DReferenceImageDepth.Front);
        var referenceLayer = new Editor2DLayer(
            referenceImage.Id,
            "Pattern reference",
            [],
            IsLocked: true,
            Kind: Editor2DLayerKind.ReferenceImage,
            ReferenceImage: referenceImage);
        var twoDState = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Layers = [new Editor2DLayer("geometry", "Geometry", []), referenceLayer],
            ActiveLayerId = referenceLayer.Id,
        };

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: twoDState));
        var restored = await service.LoadAsync(projectPath);

        Assert.False(restored.HasGeneratedOutput);
        Assert.NotNull(restored.TwoDWorkspaceState);
        Assert.Empty(restored.TwoDWorkspaceState.Document.Paths);
        var restoredLayer = Assert.Single(
            restored.TwoDWorkspaceState.Layers!,
            layer => layer.Kind == Editor2DLayerKind.ReferenceImage);
        Assert.Empty(restoredLayer.PathIds);
        Assert.True(restoredLayer.IsLocked);
        Assert.Equal(referenceImage, restoredLayer.ReferenceImage);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsEditableCornerSourceAndValue()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("corners.stch");
        var service = new Project3DStateService();
        var path = new Editor2DPreviewPath(
            "shape",
            "LWPOLYLINE",
            [new Editor2DPoint(0, 0), new Editor2DPoint(10, 0), new Editor2DPoint(10, 10)],
            true);
        var parameter = new Editor2DCornerParameter("shape:1", path.Id, 1, Editor2DCornerKind.Chamfer, 2.5, path.Points);
        var state = Editor2DWorkspaceState.Empty with
        {
            IsInitialized = true,
            Document = Editor2DWorkspaceState.Empty.Document with { Paths = [path] },
            CornerParameters = [parameter],
        };

        await service.SaveAsync(projectPath, new Project3DState(null, [], [], TwoDWorkspaceState: state));
        var restored = await service.LoadAsync(projectPath);

        var restoredParameter = Assert.Single(restored.TwoDWorkspaceState!.CornerParameters!);
        Assert.Equal(parameter.Id, restoredParameter.Id);
        Assert.Equal(2.5, restoredParameter.Value);
        Assert.Equal(parameter.SourcePoints, restoredParameter.SourcePoints);
        var reopenedWorkspace = new Domain.App.ViewModels.Editor2DWorkspaceViewModel();
        reopenedWorkspace.Apply(restored.TwoDWorkspaceState, recordHistory: false);
        Assert.True(reopenedWorkspace.UpdateCornerParameter(restoredParameter.Id, 4));
        Assert.Equal(4, reopenedWorkspace.CornerParameters.Single().Value);
        Assert.NotEqual(parameter.SourcePoints.Count, reopenedWorkspace.Document.Paths.Single().Points.Count);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsDrivenRectangleDimensionsAndCornerSource()
    {
        using var files = TestWorkspace.Create();
        var projectPath = files.GetPath("driven-rectangle.stch");
        var service = new Project3DStateService();
        var editor = new Editor2DWorkspaceViewModel();
        editor.SetDocument(Editor2DWorkspaceState.Empty.Document);
        var pathId = editor.CreateRectangle(new(30, 40), new(10, 20), initialFilletRadius: 2)!;
        var widthId = $"{pathId}:width";
        Assert.True(editor.TrySetMeasurementValue(widthId, 35, out var error), error);

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: editor.State));
        var restored = await service.LoadAsync(projectPath);
        var reopened = new Editor2DWorkspaceViewModel();
        reopened.Apply(restored.TwoDWorkspaceState!, recordHistory: false);

        Assert.Equal(35, reopened.Measurements.Single(item => item.Id == widthId).Distance, 8);
        Assert.All(
            reopened.CornerParameters.Where(parameter => parameter.PathId == pathId),
            parameter =>
            {
                Assert.Equal(2, parameter.Value);
                Assert.Equal(new Editor2DPoint(30, 40), parameter.SourcePoints[0]);
                Assert.Equal(new Editor2DPoint(-5, 20), parameter.SourcePoints[2]);
            });
        Assert.Equal(pathId, reopened.Document.Paths.Single(path => path.Id == pathId).Id);
        Assert.Contains(pathId, reopened.Layers.Single(layer => layer.PathIds.Contains(pathId)).PathIds);
        Assert.True(reopened.TrySetMeasurementValue($"{pathId}:height", 30, out error), error);
        Assert.Equal(30, reopened.Measurements.Single(item => item.Id == $"{pathId}:height").Distance, 8);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsCreationLineAndCircleAutoDimensions()
    {
        using var files = TestWorkspace.Create();
        var projectPath = files.GetPath("creation-precision.stch");
        var service = new Project3DStateService();
        var editor = new Editor2DWorkspaceViewModel();
        var lineId = editor.CreateLine(new(10, 10), new(7, 6), "saved-line")!;
        var circleId = editor.CreateCircle(new(3, 4), new(3, 9), "saved-circle")!;

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: editor.State));
        var restored = await service.LoadAsync(projectPath);
        var reopened = new Editor2DWorkspaceViewModel();
        reopened.Apply(restored.TwoDWorkspaceState!, recordHistory: false);

        Assert.Equal(2, reopened.Document.Paths.Count);
        Assert.Equal(2, reopened.Measurements.Count);
        var line = reopened.Measurements.Single(item => item.Id == $"{lineId}:length");
        Assert.Equal(new Editor2DPoint(10, 10), line.Start);
        Assert.Equal(new Editor2DPoint(7, 6), line.End);
        var radius = reopened.Measurements.Single(item => item.Id == $"{circleId}:radius");
        Assert.Equal(new Editor2DPoint(3, 4), radius.Start);
        Assert.Equal(new Editor2DPoint(8, 4), radius.End);
        Assert.Single(reopened.Layers, layer => layer.PathIds.Contains(lineId) && layer.PathIds.Contains(circleId));
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsCopiedCreationAutoDimensions()
    {
        using var files = TestWorkspace.Create();
        var projectPath = files.GetPath("copied-creation-precision.stch");
        var service = new Project3DStateService();
        var editor = new Editor2DWorkspaceViewModel();
        var lineId = editor.CreateLine(new(0, 0), new(4, 0), "line")!;
        var circleId = editor.CreateCircle(new(10, 0), new(12, 0), "circle")!;
        editor.SetMeasurements(editor.Measurements.Select(item => item.Id == $"{lineId}:length"
            ? item with { VarName = "d1", Expression = "4", IsParametric = true, EvaluatedValue = 4 }
            : item).ToArray());
        editor.SetSelection([lineId, circleId]);
        Assert.True(editor.ApplySelectionTransform(
            Editor2DAffineTransform.CreateTranslation(5, 3),
            createCopy: true));
        var copiedPathIds = editor.SelectedPathIds.ToArray();

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], TwoDWorkspaceState: editor.State));
        var restored = await service.LoadAsync(projectPath);
        var reopened = new Editor2DWorkspaceViewModel();
        reopened.Apply(restored.TwoDWorkspaceState!, recordHistory: false);

        Assert.Equal(4, reopened.Document.Paths.Count);
        Assert.Equal(4, reopened.Measurements.Count);
        Assert.Equal(4, reopened.Measurements.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(copiedPathIds, copiedPathId =>
        {
            var measurement = Assert.Single(reopened.Measurements, item => item.EntityPathId == copiedPathId);
            Assert.StartsWith($"{copiedPathId}:", measurement.Id, StringComparison.Ordinal);
            Assert.Null(measurement.VarName);
            Assert.Null(measurement.Expression);
            Assert.False(measurement.IsParametric);
            Assert.Null(measurement.EvaluatedValue);
        });
        var originalLine = reopened.Measurements.Single(item => item.Id == $"{lineId}:length");
        Assert.Equal("d1", originalLine.VarName);
        Assert.Equal("4", originalLine.Expression);
        Assert.True(originalLine.IsParametric);
        Assert.Single(reopened.Layers, layer => copiedPathIds.All(layer.PathIds.Contains));
    }

    [Fact]
    public async Task BlankTwoDProject_CanCreateEditSaveCloseAndReopenWithoutTwoDState()
    {
        using var files = TestWorkspace.Create();
        var projectPath = files.GetPath("blank-editable-2d.stch");
        var service = new Project3DStateService();
        var workspace = new Editor2DWorkspaceViewModel();
        var line = new Editor2DPreviewPath(
            "blank-line",
            "LINE",
            [new Editor2DPoint(2, 3), new Editor2DPoint(14, 3)],
            IsClosed: false);
        var measurement = new Editor2DMeasurement(
            "blank-measurement",
            line.Points[0],
            line.Points[1]);
        workspace.Edit(state => state with
        {
            Document = state.Document with { Paths = [line] },
            IsInitialized = true,
            ActiveTool = Editor2DTool.Move,
            SelectedPathIds = [line.Id],
            Measurements = [measurement],
        });

        await service.SaveAsync(
            projectPath,
            new Project3DState(
                ViewportJson: null,
                Bodies: [],
                BodyOffsets: [],
                GeneratedOutputPath: null,
                GeneratedOutputDataBase64: null,
                TwoDWorkspaceState: workspace.State));

        workspace = null!; // Close the editing session; only the project archive remains.
        var restoredProject = await service.LoadAsync(projectPath);
        var reopenedWorkspace = new Editor2DWorkspaceViewModel();
        reopenedWorkspace.Apply(restoredProject.TwoDWorkspaceState!, recordHistory: false);

        Assert.False(restoredProject.HasGeneratedOutput);
        Assert.Null(restoredProject.GeneratedOutputPath);
        Assert.Null(restoredProject.GeneratedOutputDataBase64);
        Assert.True(reopenedWorkspace.IsInitialized);
        Assert.Equal(Editor2DTool.Move, reopenedWorkspace.ActiveTool);
        var reopenedLine = Assert.Single(reopenedWorkspace.Document.Paths);
        Assert.Equal(line.Id, reopenedLine.Id);
        Assert.Equal(line.EntityType, reopenedLine.EntityType);
        Assert.Equal(line.Points, reopenedLine.Points);
        Assert.Equal([line.Id], reopenedWorkspace.SelectedPathIds);
        Assert.Equal(measurement, Assert.Single(reopenedWorkspace.Measurements));
        Assert.False(reopenedWorkspace.CanUndo);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsToolCustomizationInEditorWorkspaceState()
    {
        using var workspace = TestWorkspace.Create();
        var projectPath = workspace.GetPath("customized-tools.stch");
        var service = new Project3DStateService();
        var editorState = new EditorWorkspaceState(
            Editor3DTool.Select,
            ThreeDOrthographic: false,
            ShowTwoDWorkspace: false,
            ToolCustomizations:
            [
                new EditorToolCustomization("2d.circle", -10, "G"),
                new EditorToolCustomization("3d.project", 2, "P"),
            ]);

        await service.SaveAsync(
            projectPath,
            new Project3DState(null, [], [], WorkspaceState: editorState));

        var restored = await service.LoadAsync(projectPath);

        Assert.NotNull(restored.WorkspaceState);
        Assert.Equal(editorState.ToolCustomizations, restored.WorkspaceState.ToolCustomizations);
    }

    [Fact]
    public async Task SaveAsAsync_PreservesSourceArchiveEntriesAndReplacesProjectName()
    {
        using var workspace = TestWorkspace.Create();
        var source = workspace.WriteText(
            "source.stch",
            "{\"projectName\":\"Source\",\"templateId\":\"blank\",\"customMetadata\":42}");
        var target = workspace.GetPath("renamed.stch");
        var service = new Project3DStateService();
        await service.SaveAsync(source, Project3DState.Empty);
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Update))
        {
            var entry = archive.CreateEntry("custom/data.bin");
            await using var stream = entry.Open();
            await stream.WriteAsync(new byte[] { 1, 2, 3, 4 });
        }
        var state = Project3DState.Empty with
        {
            WorkspaceState = new EditorWorkspaceState(
                Editor3DTool.Select,
                ThreeDOrthographic: false,
                ShowTwoDWorkspace: false,
                ActiveEditorMode: EditorMode.Batch),
        };

        await service.SaveAsAsync(source, target, state);

        using var targetArchive = ZipFile.OpenRead(target);
        Assert.NotNull(targetArchive.GetEntry("custom/data.bin"));
        await using var projectStream = targetArchive.GetEntry("project.json")!.Open();
        using var projectJson = await JsonDocument.ParseAsync(projectStream);
        Assert.Equal("renamed", projectJson.RootElement.GetProperty("projectName").GetString());
        Assert.Equal(42, projectJson.RootElement.GetProperty("customMetadata").GetInt32());
        Assert.Equal(EditorMode.Batch, (await service.LoadAsync(target)).WorkspaceState!.ActiveEditorMode);
        Assert.Null((await service.LoadAsync(source)).WorkspaceState);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(string directory)
        {
            Directory = directory;
        }

        private string Directory { get; }

        public static TestWorkspace Create()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "Pathstitch-CrossPort-ProjectStateTests",
                Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(directory);
            return new TestWorkspace(directory);
        }

        public string GetPath(string fileName)
            => Path.Combine(Directory, fileName);

        public string WriteText(string fileName, string contents)
        {
            var path = GetPath(fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Test cleanup is best-effort; stale temp files do not affect assertions.
            }
        }
    }
}
