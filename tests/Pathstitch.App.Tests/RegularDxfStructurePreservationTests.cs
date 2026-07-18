using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Domain.App.Models;
using Domain.App.Navigation;
using Domain.App.Services;
using Domain.App.ViewModels;
using Domain.MVVM.Navigation;
using Pathstitch.App.Services;

namespace Pathstitch.App.Tests;

public sealed class RegularDxfStructurePreservationTests
{
    [Fact]
    public async Task UnchangedFullExportAndProjectSave_PreserveOpaqueDxfAcrossReopen()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.dxf");
        var projectPath = Path.Combine(directory, "project.stch");
        var firstExport = Path.Combine(directory, "first-export.dxf");
        var reopenedExport = Path.Combine(directory, "reopened-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OpaqueDxf(), Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);

            using (var editor = await OpenEditorAsync(projectPath, firstExport, outputService))
            {
                editor.TwoDExportMeasurementLines = true;
                await File.WriteAllTextAsync(editor.LastGeneratedOutputPath!, MinimalDxf(), Encoding.ASCII);
                await editor.ExportTwoDDxfAsync();
                AssertOpaqueStructureFile(firstExport);

                await editor.SaveDocumentAsync();
                Assert.False(editor.IsDirty);
                AssertOpaqueStructure(await ReadEmbeddedDxfAsync(projectPath));
            }

            using (var reopened = await OpenEditorAsync(projectPath, reopenedExport, outputService))
            {
                await reopened.ExportTwoDDxfAsync();
                AssertOpaqueStructureFile(reopenedExport);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("text")]
    [InlineData("layer-name")]
    [InlineData("layer-color")]
    [InlineData("layer-reassignment")]
    [InlineData("selected")]
    [InlineData("measurements")]
    [InlineData("version")]
    [InlineData("deletion")]
    [InlineData("new")]
    public async Task ChangedOrFilteredExport_UsesSafeMergeOrGeometrySerializer(string variation)
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.dxf");
        var projectPath = Path.Combine(directory, "project.stch");
        var exportPath = Path.Combine(directory, $"{variation}.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = variation == "text"
                ? OpaqueDxf().Replace(
                    "1\nOriginal label\n0\nINSERT",
                    "1\nOriginal label\n1001\nTEXT_VENDOR\n1000\ntext-payload\n0\nINSERT",
                    StringComparison.Ordinal)
                : OpaqueDxf();
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            using var editor = await OpenEditorAsync(projectPath, exportPath, outputService);
            var sourceLine = Assert.Single(editor.TwoDDocument!.Paths, path => path.EntityType == "LINE");

            switch (variation)
            {
                case "geometry":
                    editor.TwoDDocument = editor.TwoDDocument with
                    {
                        Paths = editor.TwoDDocument.Paths.Select(path => path.Id == sourceLine.Id
                            ? path with { Points = [new(0, 0), new(25, 0)] }
                            : path).ToArray(),
                    };
                    break;
                case "text":
                    editor.TwoDDocument = editor.TwoDDocument with
                    {
                        Paths = editor.TwoDDocument.Paths.Select(path => path.EntityType == "TEXT"
                            ? path with { Text = "Changed label" }
                            : path).ToArray(),
                    };
                    break;
                case "layer-name":
                    editor.RenameTwoDLayer(Assert.Single(editor.TwoDLayers).Id, "RENAMED");
                    break;
                case "layer-color":
                    Assert.True(editor.SetTwoDLayerColor(Assert.Single(editor.TwoDLayers).Id, "#123456"));
                    break;
                case "layer-reassignment":
                    editor.CreateTwoDLayer();
                    editor.TwoDSelectedPathIds = [sourceLine.Id];
                    editor.AssignTwoDSelectionToLayer(editor.TwoDLayers.OrderBy(layer => layer.Order).Last().Id);
                    break;
                case "selected":
                    editor.TwoDSelectedPathIds = [sourceLine.Id];
                    editor.TwoDExportSelectedOnly = true;
                    break;
                case "measurements":
                    editor.TwoDMeasurements = [new Editor2DMeasurement("dimension", new(0, -2), new(10, -2))];
                    editor.TwoDExportMeasurementLines = true;
                    break;
                case "version":
                    editor.TwoDDxfVersion = "R2018";
                    break;
                case "deletion":
                    editor.TwoDDocument = editor.TwoDDocument with
                    {
                        Paths = editor.TwoDDocument.Paths.Where(path => path.Id != sourceLine.Id).ToArray(),
                    };
                    break;
                case "new":
                    editor.TwoDDocument = editor.TwoDDocument with
                    {
                        Paths = editor.TwoDDocument.Paths.Append(new Editor2DPreviewPath(
                            "new-line", "LINE", [new(0, 5), new(10, 5)], false)).ToArray(),
                    };
                    break;
            }

            await editor.ExportTwoDDxfAsync();

            var exported = Normalize(await File.ReadAllTextAsync(exportPath));
            if (variation is "geometry" or "text")
            {
                AssertOpaqueStructure(exported);
                if (variation == "geometry")
                {
                    Assert.Contains("\n25\n", exported, StringComparison.Ordinal);
                    Assert.Contains("\n5\n10\n", exported, StringComparison.Ordinal);
                    Assert.Contains("\nTEXT\n5\n12\n8\nCUT\n10\n2\n", exported, StringComparison.Ordinal);
                }
                else
                {
                    Assert.Contains("\n1\nChanged label\n", exported, StringComparison.Ordinal);
                    Assert.Contains("\n5\n12\n", exported, StringComparison.Ordinal);
                    Assert.Contains("\n1001\nTEXT_VENDOR\n1000\ntext-payload\n", exported, StringComparison.Ordinal);
                }
            }
            else if (variation == "deletion")
            {
                Assert.Contains("\nBLOCKS\n", exported, StringComparison.Ordinal);
                Assert.Contains("\nINSERT\n", exported, StringComparison.Ordinal);
                Assert.Contains("\nDASHED_VENDOR\n", exported, StringComparison.Ordinal);
                Assert.DoesNotContain("\nLINE\n5\n10\n", exported, StringComparison.Ordinal);
                Assert.DoesNotContain("\nVENDOR_APP\n", exported, StringComparison.Ordinal);
            }
            else
            {
                Assert.DoesNotContain("\nBLOCKS\n", exported, StringComparison.Ordinal);
                Assert.DoesNotContain("\nINSERT\n", exported, StringComparison.Ordinal);
                Assert.DoesNotContain("\nVENDOR_APP\n", exported, StringComparison.Ordinal);
            }
            if (variation == "version")
                Assert.Contains("\nAC1032\n", exported, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ModifiedSave_MergesCurrentGeometryAndRetainsOpaqueSourceAcrossReopen()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.dxf");
        var projectPath = Path.Combine(directory, "project.stch");
        var exportPath = Path.Combine(directory, "reopened-modified.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OpaqueDxf(), Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            using (var editor = await OpenEditorAsync(projectPath, exportPath, outputService))
            {
                var line = Assert.Single(editor.TwoDDocument!.Paths, path => path.EntityType == "LINE");
                editor.TwoDDocument = editor.TwoDDocument with
                {
                    Paths = editor.TwoDDocument.Paths.Select(path => path.Id == line.Id
                        ? path with { Points = [new(0, 0), new(25, 0)] }
                        : path).ToArray(),
                };
                await editor.SaveDocumentAsync();
            }

            var embedded = Normalize(await ReadEmbeddedDxfAsync(projectPath));
            AssertOpaqueStructure(embedded);
            Assert.Contains("\n25\n", embedded, StringComparison.Ordinal);

            using var reopened = await OpenEditorAsync(projectPath, exportPath, outputService);
            await reopened.ExportTwoDDxfAsync();
            var exported = Normalize(await File.ReadAllTextAsync(exportPath));
            AssertOpaqueStructure(exported);
            Assert.Contains("\n25\n", exported, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PlanarHandledEntities_MergeAllSupportedGeometryTypesAndPreserveOpaqueRecords()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "planar-source.dxf");
        var exportPath = Path.Combine(directory, "planar-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, PlanarGeometryDxf(), Encoding.UTF8);
            var document = Assert.IsType<Editor2DPreviewDocument>(await outputService.LoadPreviewDocumentAsync(sourcePath));
            var changedPaths = document.Paths.Select(path => path.SourceEntityHandle?.ToUpperInvariant() switch
            {
                "10" => path with { Points = [new(1, 2), new(21, 2)] },
                "20" => path with { Points = path.Points.Select(point => new Editor2DPoint(point.X + 3, point.Y + 4)).ToArray() },
                "30" => path with { Center = new(7, 8), Radius = 5 },
                "40" => path with { Center = new(12, 3), Radius = 6, StartAngleDegrees = 45, EndAngleDegrees = 180 },
                _ => path,
            }).ToArray();
            var changedDocument = document with { Paths = changedPaths };
            var metadata = changedPaths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\nBLOCKS\n", raw, StringComparison.Ordinal);
            Assert.Contains("\nINSERT\n", raw, StringComparison.Ordinal);
            Assert.Contains("\nVENDOR_APP\n1000\npolyline-payload\n", raw, StringComparison.Ordinal);
            Assert.Contains("\nTEXT\n5\n50\n8\nCUT\n10\n2\n20\n2\n40\n2\n1\nUntouched\n", raw, StringComparison.Ordinal);

            var reopened = Assert.IsType<Editor2DPreviewDocument>(await outputService.LoadPreviewDocumentAsync(exportPath));
            var line = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "10");
            Assert.Equal(new Editor2DPoint(21, 2), line.Points[^1]);
            var polyline = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "20");
            Assert.Equal(new Editor2DPoint(3, 4), polyline.Points[0]);
            var circle = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "30");
            Assert.Equal(new Editor2DPoint(7, 8), circle.Center);
            Assert.Equal(5, circle.Radius);
            var arc = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "40");
            Assert.Equal(new Editor2DPoint(12, 3), arc.Center);
            Assert.Equal(6, arc.Radius);
            Assert.Equal(45, arc.StartAngleDegrees);
            Assert.Equal(180, arc.EndAngleDegrees);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task NewEntities_AllocateGlobalHandlesUpdateHandseedAndSetModelSpaceOwner()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "owned-source.dxf");
        var exportPath = Path.Combine(directory, "owned-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newLine = new Editor2DPreviewPath(
                "new-line", "LINE", [new(0, 5), new(10, 5)], false);
            var newPolyline = new Editor2DPreviewPath(
                "new-polyline", "LWPOLYLINE", [new(0, 8), new(5, 8), new(5, 12)], false);
            var paths = sourceDocument.Paths.Concat([newLine, newPolyline]).ToArray();
            var document = sourceDocument with { Paths = paths };
            var metadata = paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(document, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            Assert.Equal("100", merged.ProvenanceByPathId[newLine.Id].Handle);
            Assert.Equal("101", merged.ProvenanceByPathId[newPolyline.Id].Handle);
            Assert.Equal("CUT", merged.ProvenanceByPathId[newLine.Id].LayerName);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n9\n$HANDSEED\n5\n102\n", raw, StringComparison.Ordinal);

            Assert.Contains("\n5\nFF\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n5\n100\n330\n1F\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n5\n101\n330\n1F\n", raw, StringComparison.Ordinal);

            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "100" && path.EntityType == "LINE");
            Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "101" && path.EntityType == "LWPOLYLINE");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SolidNonAssociativePolylineHatch_MergesLoopTransformAndPreservesEnvelope()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "hatch-source.dxf");
        var exportPath = Path.Combine(directory, "hatch-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceHatchDxf(associative: false), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            var movedLoops = hatch.FillLoops!
                .Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                    .Select(point => new Editor2DPoint(point.X + 1, point.Y + 2))
                    .ToArray())
                .ToArray();
            var movedHatch = hatch with
            {
                Points = hatch.Points.Select(point => new Editor2DPoint(point.X + 1, point.Y + 2)).ToArray(),
                FillLoops = movedLoops,
            };
            var changedDocument = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? movedHatch : path).ToArray(),
            };
            var metadata = changedDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\nHATCH\n5\n13\n330\n1F\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n5\nFF\n", raw, StringComparison.Ordinal);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedHatch = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "13");
            Assert.Equal(new Editor2DPoint(1, 2), reopenedHatch.Points[0]);
            Assert.Equal(new Editor2DPoint(11, 7), reopenedHatch.Points[2]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AssociativeHatchEdit_RejectsMergeWithoutTouchingTarget()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "associative-hatch-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceHatchDxf(associative: true), Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            var changedDocument = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id
                    ? path with
                    {
                        Points = path.Points.Select(point => new Editor2DPoint(point.X + 1, point.Y)).ToArray(),
                        FillLoops = path.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                            .Select(point => new Editor2DPoint(point.X + 1, point.Y)).ToArray()).ToArray(),
                    }
                    : path).ToArray(),
            };
            var metadata = changedDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.False(merged.Succeeded);
            Assert.Equal("sentinel", await File.ReadAllTextAsync(exportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BulgedPolylineHatchEdit_PreservesBulgeAndTransformsRawVertices()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "bulged-hatch-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceHatchDxf(associative: false)
                .Replace("92\n3\n72\n0", "92\n3\n72\n1", StringComparison.Ordinal)
                .Replace("10\n0\n20\n0\n10\n10", "10\n0\n20\n0\n42\n1\n10\n10", StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            var changedDocument = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id
                    ? path with
                    {
                        Points = path.Points.Select(point => new Editor2DPoint(point.X + 1, point.Y)).ToArray(),
                        FillLoops = path.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                            .Select(point => new Editor2DPoint(point.X + 1, point.Y)).ToArray()).ToArray(),
                    }
                    : path).ToArray(),
            };
            var metadata = changedDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n10\n1\n20\n0\n42\n1\n10\n11\n", raw, StringComparison.Ordinal);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedHatch = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "13");
            var expectedHatch = changedDocument.Paths.Single(path => path.Id == hatch.Id);
            Assert.True(reopenedHatch.Points.Zip(expectedHatch.Points).All(pair =>
                Math.Abs(pair.First.X - pair.Second.X) < 1e-8
                && Math.Abs(pair.First.Y - pair.Second.Y) < 1e-8));        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task BulgedPolylineHatchNonUniformScaling_RejectsWithoutTouchingTarget()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "bulged-hatch-nonuniform-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceHatchDxf(associative: false)
                .Replace("92\n3\n72\n0", "92\n3\n72\n1", StringComparison.Ordinal)
                .Replace("10\n0\n20\n0\n10\n10", "10\n0\n20\n0\n42\n1\n10\n10", StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            var changedDocument = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id
                    ? path with
                    {
                        Points = path.Points.Select(point => new Editor2DPoint(point.X * 2.0, point.Y)).ToArray(),
                        FillLoops = path.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                            .Select(point => new Editor2DPoint(point.X * 2.0, point.Y)).ToArray()).ToArray(),
                    }
                    : path).ToArray(),
            };
            var metadata = changedDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.False(merged.Succeeded);
            Assert.Equal("sentinel", await File.ReadAllTextAsync(exportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BulgedPolylineHatchUniformScale_PromotesCanonicalGeometryAndSecondMergeIsStable()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "bulged-hatch-scale-source.dxf");
        var firstPath = Path.Combine(directory, "bulged-hatch-scale-first.dxf");
        var secondPath = Path.Combine(directory, "bulged-hatch-scale-second.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceHatchDxf(associative: false)
                .Replace("92\n3\n72\n0", "92\n3\n72\n1", StringComparison.Ordinal)
                .Replace("10\n0\n20\n0\n10\n10", "10\n0\n20\n0\n42\n1\n10\n10", StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            var scaledHatch = hatch with
            {
                Points = hatch.Points.Select(point => new Editor2DPoint(point.X * 2.0, point.Y * 2.0)).ToArray(),
                FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                    .Select(point => new Editor2DPoint(point.X * 2.0, point.Y * 2.0)).ToArray()).ToArray(),
            };
            var changed = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? scaledHatch : path).ToArray(),
            };
            var metadata = changed.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var first = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changed, metadata),
                firstPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(first.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(firstPath));
            Assert.Contains("\n10\n0\n20\n0\n42\n1\n10\n20\n", raw, StringComparison.Ordinal);
            var canonicalHatch = Assert.IsType<Editor2DPreviewPath>(first.CanonicalPathsByPathId![hatch.Id]);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(firstPath));
            Assert.Equal(
                Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "13").Points.Count,
                canonicalHatch.Points.Count);
            var canonicalDocument = changed with
            {
                Paths = changed.Paths.Select(path => path.Id == hatch.Id ? canonicalHatch : path).ToArray(),
            };
            var second = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(firstPath),
                new Editor2DExportDocument(canonicalDocument, metadata),
                secondPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(second.Succeeded);
            Assert.Equal(await File.ReadAllBytesAsync(firstPath), await File.ReadAllBytesAsync(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProjectSave_UniformlyScaledBulgedHatchPromotesCanonicalGeometryAcrossReopen()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "scaled-hatch-project-source.dxf");
        var projectPath = Path.Combine(directory, "scaled-hatch-project.stch");
        var exportPath = Path.Combine(directory, "scaled-hatch-project-export.dxf");
        var embeddedPath = Path.Combine(directory, "scaled-hatch-project-embedded.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceHatchDxf(associative: false)
                .Replace("92\n3\n72\n0", "92\n3\n72\n1", StringComparison.Ordinal)
                .Replace("10\n0\n20\n0\n10\n10", "10\n0\n20\n0\n42\n1\n10\n10", StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            int promotedPointCount;
            using (var editor = await OpenEditorAsync(projectPath, exportPath, outputService))
            {
                var hatch = Assert.Single(editor.TwoDDocument!.Paths, path => path.EntityType == "HATCH");
                static Editor2DPoint Transform(Editor2DPoint point) => new(point.X * 2.0, point.Y * 2.0);
                var scaledHatch = hatch with
                {
                    Points = hatch.Points.Select(Transform).ToArray(),
                    FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                        .Select(Transform).ToArray()).ToArray(),
                };
                editor.TwoDDocument = editor.TwoDDocument with
                {
                    Paths = editor.TwoDDocument.Paths.Select(path => path.Id == hatch.Id ? scaledHatch : path).ToArray(),
                };

                await editor.SaveDocumentAsync();

                var promoted = Assert.Single(editor.TwoDDocument.Paths, path => path.Id == hatch.Id);
                promotedPointCount = promoted.Points.Count;
                Assert.False(editor.IsDirty);
            }

            var savedState = await new Project3DStateService(outputService).LoadAsync(projectPath);
            var persisted = Assert.Single(
                savedState.TwoDWorkspaceState!.Document.Paths,
                path => path.SourceEntityHandle == "13");
            Assert.Equal(promotedPointCount, persisted.Points.Count);
            var embeddedBytes = Convert.FromBase64String(savedState.GeneratedOutputDataBase64!);
            Assert.Contains(
                "\n10\n0\n20\n0\n42\n1\n10\n20\n",
                Normalize(Encoding.UTF8.GetString(embeddedBytes)),
                StringComparison.Ordinal);
            await File.WriteAllBytesAsync(embeddedPath, embeddedBytes);
            var canonicalPreview = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(embeddedPath));
            var canonicalHatch = Assert.Single(canonicalPreview.Paths, path => path.SourceEntityHandle == "13");
            Assert.Equal(canonicalHatch.Points.Count, persisted.Points.Count);
            Assert.True(canonicalHatch.Points.Zip(persisted.Points).All(pair =>
                Math.Abs(pair.First.X - pair.Second.X) < 1e-12
                && Math.Abs(pair.First.Y - pair.Second.Y) < 1e-12));

            using var reopened = await OpenEditorAsync(projectPath, exportPath, outputService);
            Assert.Equal(
                promotedPointCount,
                Assert.Single(reopened.TwoDDocument!.Paths, path => path.SourceEntityHandle == "13").Points.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task MixedLineAndArcEdgeHatch_RotatesWithoutFlatteningNativeEdges()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "edge-hatch-source.dxf");
        var exportPath = Path.Combine(directory, "edge-hatch-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceEdgeHatchDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            static Editor2DPoint Transform(Editor2DPoint point) => new(20 - point.Y, 3 + point.X);
            var transformedHatch = hatch with
            {
                Points = hatch.Points.Select(Transform).ToArray(),
                FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                    .Select(Transform).ToArray()).ToArray(),
            };
            var changedDocument = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? transformedHatch : path).ToArray(),
            };
            var metadata = changedDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n72\n1\n10\n20\n20\n3\n11\n20\n21\n13\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n72\n2\n10\n15\n20\n13\n40\n5\n50\n0\n51\n180\n73\n1\n", raw, StringComparison.Ordinal);
            Assert.Equal(1, raw.Split("\n72\n2\n", StringSplitOptions.None).Length - 1);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedHatch = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "13");
            Assert.Equal(transformedHatch.Points.Count, reopenedHatch.Points.Count);
            Assert.True(reopenedHatch.Points.Zip(transformedHatch.Points).All(pair =>
                Math.Abs(pair.First.X - pair.Second.X) < 1e-8
                && Math.Abs(pair.First.Y - pair.Second.Y) < 1e-8));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ArcEdgeHatchUniformScale_ScalesRadiusAndSecondMergeIsStable()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "scaled-edge-hatch-source.dxf");
        var firstPath = Path.Combine(directory, "scaled-edge-hatch-first.dxf");
        var secondPath = Path.Combine(directory, "scaled-edge-hatch-second.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceEdgeHatchDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            static Editor2DPoint Transform(Editor2DPoint point) => new(point.X * 2.0, point.Y * 2.0);
            var transformedHatch = hatch with
            {
                Points = hatch.Points.Select(Transform).ToArray(),
                FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                    .Select(Transform).ToArray()).ToArray(),
            };
            var changed = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? transformedHatch : path).ToArray(),
            };
            var metadata = changed.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var first = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changed, metadata),
                firstPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(first.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(firstPath));
            Assert.Contains("\n72\n2\n10\n20\n20\n10\n40\n10\n50\n-90\n51\n90\n73\n1\n", raw, StringComparison.Ordinal);
            var canonicalHatch = Assert.IsType<Editor2DPreviewPath>(first.CanonicalPathsByPathId![hatch.Id]);
            var canonicalDocument = changed with
            {
                Paths = changed.Paths.Select(path => path.Id == hatch.Id ? canonicalHatch : path).ToArray(),
            };
            var second = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(firstPath),
                new Editor2DExportDocument(canonicalDocument, metadata),
                secondPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(second.Succeeded);
            Assert.Equal(await File.ReadAllBytesAsync(firstPath), await File.ReadAllBytesAsync(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task EllipseEdgeHatch_RotatesMajorAxisWithoutFlattening()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "ellipse-edge-hatch-source.dxf");
        var exportPath = Path.Combine(directory, "ellipse-edge-hatch-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceEllipseEdgeHatchDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            static Editor2DPoint Transform(Editor2DPoint point) => new(20 - point.Y, 3 + point.X);
            var transformedHatch = hatch with
            {
                Points = hatch.Points.Select(Transform).ToArray(),
                FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop.Select(Transform).ToArray()).ToArray(),
            };
            var changed = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? transformedHatch : path).ToArray(),
            };
            var metadata = changed.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changed, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains(
                "\n72\n3\n10\n15\n20\n8\n11\n0\n21\n5\n40\n0.5\n50\n0\n51\n360\n73\n1\n",
                raw,
                StringComparison.Ordinal);
            Assert.Equal(1, raw.Split("\n72\n3\n", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RationalSplineEdgeHatch_RotatesControlPointsAndPreservesCurveData()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "spline-edge-hatch-source.dxf");
        var exportPath = Path.Combine(directory, "spline-edge-hatch-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceSplineEdgeHatchDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            static Editor2DPoint Transform(Editor2DPoint point) => new(20 - point.Y, 3 + point.X);
            var transformedHatch = hatch with
            {
                Points = hatch.Points.Select(Transform).ToArray(),
                FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop.Select(Transform).ToArray()).ToArray(),
            };
            var changed = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? transformedHatch : path).ToArray(),
            };
            var metadata = changed.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changed, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n72\n4\n94\n2\n73\n1\n74\n0\n95\n6\n96\n3\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n42\n1\n42\n1\n42\n1\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n10\n20\n20\n3\n10\n10\n20\n8\n10\n20\n20\n13\n", raw, StringComparison.Ordinal);
            Assert.Equal(1, raw.Split("\n72\n4\n", StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ArcEdgeHatch_ReflectionTransformsAnglesAndTogglesDirection()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "reflected-edge-hatch-source.dxf");
        var exportPath = Path.Combine(directory, "reflected-edge-hatch-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceEdgeHatchDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            static Editor2DPoint Transform(Editor2DPoint point) => new(point.X, 20 - point.Y);
            var transformedHatch = hatch with
            {
                Points = hatch.Points.Select(Transform).ToArray(),
                FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop.Select(Transform).ToArray()).ToArray(),
            };
            var changed = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? transformedHatch : path).ToArray(),
            };
            var metadata = changed.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changed, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n72\n2\n10\n10\n20\n15\n40\n5\n50\n90\n51\n-90\n73\n0\n", raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedEdgeCount_RejectsWithoutTouchingTarget()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "malformed-edge-count-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceEdgeHatchDxf().Replace("92\n1\n93\n4", "92\n1\n93\n3", StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var hatch = Assert.Single(sourceDocument.Paths, path => path.EntityType == "HATCH");
            var movedHatch = hatch with
            {
                Points = hatch.Points.Select(point => new Editor2DPoint(point.X + 1, point.Y + 2)).ToArray(),
                FillLoops = hatch.FillLoops!.Select(loop => (IReadOnlyList<Editor2DPoint>)loop
                    .Select(point => new Editor2DPoint(point.X + 1, point.Y + 2)).ToArray()).ToArray(),
            };
            var changed = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == hatch.Id ? movedHatch : path).ToArray(),
            };
            var metadata = changed.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changed, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.False(merged.Succeeded);
            Assert.Equal("sentinel", await File.ReadAllTextAsync(exportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BasicNewText_InsertsWithHandleOwnerAndStableTextGeometry()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "owned-source.dxf");
        var exportPath = Path.Combine(directory, "new-text-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newText = new Editor2DPreviewPath(
                "new-text",
                "TEXT",
                [new(2, 6)],
                false,
                Start: new(2, 6),
                Text: "New note",
                TextHeight: 2,
                RotationDegrees: 15,
                WidthFactor: 1.25);
            var paths = sourceDocument.Paths.Append(newText).ToArray();
            var metadata = paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            Assert.Equal("100", merged.ProvenanceByPathId[newText.Id].Handle);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\nTEXT\n5\n100\n330\n1F\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n1\nNew note\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n50\n15\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n7\nSTANDARD\n", raw, StringComparison.Ordinal);
            Assert.Contains(
                "\n0\nTABLE\n2\nSTYLE\n5\n101\n330\n0\n100\nAcDbSymbolTable\n70\n1\n0\nSTYLE\n5\n102\n330\n101\n",
                raw,
                StringComparison.Ordinal);
            Assert.Contains("\n9\n$HANDSEED\n5\n103\n", raw, StringComparison.Ordinal);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedText = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "100");
            Assert.Equal("New note", reopenedText.Text);
            Assert.Equal(15, reopenedText.RotationDegrees!.Value, 8);
            Assert.Equal(1.25, reopenedText.WidthFactor);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BasicNewRichText_UsesExistingPathstitchAppIdDependency()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "registered-rich-source.dxf");
        var exportPath = Path.Combine(directory, "new-rich-text-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceRichTextDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newText = new Editor2DPreviewPath(
                "new-rich-text",
                "TEXT",
                [new(4, 7)],
                false,
                Start: new(4, 7),
                Text: "New\nrich",
                TextHeight: 2,
                FontFamily: "Inter",
                IsUnderline: true);
            var paths = sourceDocument.Paths.Append(newText).ToArray();
            var metadata = paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            Assert.Equal("100", merged.ProvenanceByPathId[newText.Id].Handle);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Equal(2, raw.Split("\n1001\nPATHSTITCH\n", StringSplitOptions.None).Length - 1);
            Assert.Contains("\nTEXT\n5\n100\n330\n1F\n", raw, StringComparison.Ordinal);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedText = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "100");
            Assert.Equal("New\nrich", reopenedText.Text);
            Assert.Equal("Inter", reopenedText.FontFamily);
            Assert.True(reopenedText.IsUnderline);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewRichText_InsertsMissingAppIdAndStyleTablesAndSecondMergeIsStable()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "missing-dependencies-source.dxf");
        var firstPath = Path.Combine(directory, "first-merge.dxf");
        var secondPath = Path.Combine(directory, "second-merge.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newText = new Editor2DPreviewPath(
                "new-rich-text-missing-dependencies",
                "TEXT",
                [new(4, 7)],
                false,
                Start: new(4, 7),
                Text: "New\nrich",
                TextHeight: 2,
                FontFamily: "Inter",
                IsUnderline: true);
            var paths = sourceDocument.Paths.Append(newText).ToArray();
            var metadata = paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var first = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                firstPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(first.Succeeded);
            Assert.Equal("100", first.ProvenanceByPathId[newText.Id].Handle);
            var raw = Normalize(await File.ReadAllTextAsync(firstPath));
            Assert.Contains("\n0\nTABLE\n2\nAPPID\n5\n101\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nAPPID\n5\n102\n330\n101\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n2\nPATHSTITCH\n70\n0\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nTABLE\n2\nSTYLE\n5\n103\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nSTYLE\n5\n104\n330\n103\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n2\nSTANDARD\n70\n0\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n9\n$HANDSEED\n5\n105\n", raw, StringComparison.Ordinal);

            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(firstPath));
            var reopenedText = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "100");
            Assert.Equal("New\nrich", reopenedText.Text);
            Assert.Equal("Inter", reopenedText.FontFamily);
            Assert.True(reopenedText.IsUnderline);
            var reopenedMetadata = reopened.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);
            var second = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(firstPath),
                new Editor2DExportDocument(reopened, reopenedMetadata),
                secondPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(second.Succeeded);
            Assert.Equal(await File.ReadAllBytesAsync(firstPath), await File.ReadAllBytesAsync(secondPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingSymbolTables_InsertRecordsAndRespectDeclaredCapacity()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "vendor-tables-source.dxf");
        var exportPath = Path.Combine(directory, "vendor-tables-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceVendorTablesDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newText = new Editor2DPreviewPath(
                "new-rich-text-existing-tables",
                "TEXT",
                [new(4, 7)],
                false,
                Start: new(4, 7),
                Text: "Existing tables",
                TextHeight: 2,
                FontFamily: "Inter");
            var paths = sourceDocument.Paths.Append(newText).ToArray();
            var metadata = paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            Assert.Equal("100", merged.ProvenanceByPathId[newText.Id].Handle);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Equal(1, raw.Split("\n0\nTABLE\n2\nAPPID\n", StringSplitOptions.None).Length - 1);
            Assert.Equal(1, raw.Split("\n0\nTABLE\n2\nSTYLE\n", StringSplitOptions.None).Length - 1);
            Assert.Contains("\n2\nAPPID\n5\n2F\n330\n0\n100\nAcDbSymbolTable\n70\n2\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nAPPID\n5\n101\n330\n2F\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n2\nSTYLE\n5\n2D\n330\n0\n100\nAcDbSymbolTable\n70\n2\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nSTYLE\n5\n102\n330\n2D\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n9\n$HANDSEED\n5\n103\n", raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedText_PreservesRegisteredCustomNativeStyle()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "custom-style-source.dxf");
        var exportPath = Path.Combine(directory, "custom-style-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceVendorTablesDxf().Replace(
                "40\n2\n1\nUntouched",
                "40\n2\n7\nVENDOR_STYLE\n1\nUntouched",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var textPath = Assert.Single(sourceDocument.Paths, path => path.EntityType == "TEXT");
            var changed = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == textPath.Id
                    ? path with { Text = "Styled update" }
                    : path).ToArray(),
            };
            var metadata = changed.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changed, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n7\nVENDOR_STYLE\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n1\nStyled update\n", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\n7\nSTANDARD\n", raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedAppIdRecordOwner_RejectsWithoutTouchingTarget()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "malformed-appid-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceVendorTablesDxf().Replace(
                "0\nAPPID\n5\n30\n330\n2F",
                "0\nAPPID\n5\n30\n330\n29",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newText = new Editor2DPreviewPath(
                "new-rich-text-malformed-appid",
                "TEXT",
                [new(4, 7)],
                false,
                Start: new(4, 7),
                Text: "Rich",
                TextHeight: 2,
                FontFamily: "Inter");
            var paths = sourceDocument.Paths.Append(newText).ToArray();
            var metadata = paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.False(merged.Succeeded);
            Assert.Equal("sentinel", await File.ReadAllTextAsync(exportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task RegisteredRichTextXDataEdit_ReplacesOwnedBlockAndPreservesVendorXData()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "registered-rich-text-source.dxf");
        var exportPath = Path.Combine(directory, "registered-rich-text-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceRichTextDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var textPath = Assert.Single(sourceDocument.Paths, path => path.EntityType == "TEXT");
            var changedDocument = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == textPath.Id
                    ? path with
                    {
                        Text = "Updated\nrich",
                        FontFamily = "Inter",
                        CharacterSpacing = 0.5,
                        IsBold = false,
                        IsItalic = true,
                    }
                    : path).ToArray(),
            };
            var metadata = changedDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Equal(1, raw.Split("\n1001\nPATHSTITCH\n", StringSplitOptions.None).Length - 1);
            Assert.Contains("\n1000\nInter\n1070\n0\n1070\n1\n1070\n0\n1040\n0.5\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n1000\nUpdated\u0001NL\u0001rich\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n1001\nTEXT_VENDOR\n1000\nvendor-payload\n", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\n1000\nArial\n", raw, StringComparison.Ordinal);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedText = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "12");
            Assert.Equal("Updated\nrich", reopenedText.Text);
            Assert.Equal("Inter", reopenedText.FontFamily);
            Assert.False(reopenedText.IsBold);
            Assert.True(reopenedText.IsItalic);
            Assert.Equal(0.5, reopenedText.CharacterSpacing, 8);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RichTextXDataEdit_InsertsMissingAppIdAndStyleTables()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "rich-text-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OpaqueDxf().Replace(
                "1\nOriginal label\n0\nINSERT",
                "1\nOriginal label\n1001\nPATHSTITCH\n1000\nArial\n1070\n1\n1070\n0\n1070\n0\n1040\n0\n1000\nOriginal label\n0\nINSERT",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var textPath = Assert.Single(sourceDocument.Paths, path => path.EntityType == "TEXT");
            var changedDocument = sourceDocument with
            {
                Paths = sourceDocument.Paths.Select(path => path.Id == textPath.Id
                    ? path with { Text = "Changed rich label" }
                    : path).ToArray(),
            };
            var metadata = changedDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(changedDocument, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n0\nTABLE\n2\nAPPID\n5\n13\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nAPPID\n5\n14\n330\n13\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n2\nPATHSTITCH\n70\n0\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nTABLE\n2\nSTYLE\n5\n15\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nSTYLE\n5\n16\n330\n15\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n2\nSTANDARD\n70\n0\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n9\n$HANDSEED\n5\n17\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n1001\nPATHSTITCH\n", raw, StringComparison.Ordinal);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedText = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "12");
            Assert.Equal("Changed rich label", reopenedText.Text);
            Assert.Equal("Arial", reopenedText.FontFamily);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewEntityOnNewLayer_InsertsLayerTableDependencyBeforeEntity()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "owned-source.dxf");
        var exportPath = Path.Combine(directory, "new-layer-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newLine = new Editor2DPreviewPath(
                "new-layer-line", "LINE", [new(0, 5), new(10, 5)], false);
            var paths = sourceDocument.Paths.Append(newLine).ToArray();
            var metadata = sourceDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);
            metadata[newLine.Id] = new Editor2DExportPathMetadata("LASER", "#123456");

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            Assert.Equal("101", merged.ProvenanceByPathId[newLine.Id].Handle);
            Assert.Equal("LASER", merged.ProvenanceByPathId[newLine.Id].LayerName);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains("\n2\nLAYER\n5\n29\n70\n2\n", raw, StringComparison.Ordinal);
            Assert.Contains(
                "\n0\nLAYER\n5\n100\n330\n29\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\nLASER\n70\n0\n62\n7\n420\n1193046\n6\nCONTINUOUS\n",
                raw,
                StringComparison.Ordinal);
            Assert.Contains("\n5\n101\n330\n1F\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n8\nLASER\n", raw, StringComparison.Ordinal);
            var ltypeTableIndex = raw.IndexOf("\n0\nTABLE\n2\nLTYPE\n5\n102\n", StringComparison.Ordinal);
            var layerTableIndex = raw.IndexOf("\n0\nTABLE\n2\nLAYER\n5\n29\n", StringComparison.Ordinal);
            Assert.True(ltypeTableIndex > 0 && ltypeTableIndex < layerTableIndex);
            Assert.Contains(
                "\n0\nLTYPE\n5\n103\n330\n102\n100\nAcDbSymbolTableRecord\n100\nAcDbLinetypeTableRecord\n2\nCONTINUOUS\n",
                raw,
                StringComparison.Ordinal);
            Assert.Contains("\n9\n$HANDSEED\n5\n104\n", raw, StringComparison.Ordinal);

            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedLine = Assert.Single(
                reopened.Paths,
                path => path.SourceEntityHandle == "101" && path.EntityType == "LINE");
            Assert.Equal("LASER", reopenedLine.SourceLayerName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewConstructionLine_InsertsDashedDependencyLayerAndCanonicalEntityStyle()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "construction-source.dxf");
        var exportPath = Path.Combine(directory, "construction-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var constructionLine = new Editor2DPreviewPath(
                "new-construction-line",
                "LINE",
                [new(0, 7), new(10, 7)],
                false,
                IsConstruction: true);
            var paths = sourceDocument.Paths.Append(constructionLine).ToArray();
            var metadata = sourceDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);
            metadata[constructionLine.Id] = new Editor2DExportPathMetadata("CONSTRUCTION", "#808080");

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            Assert.Equal("101", merged.ProvenanceByPathId[constructionLine.Id].Handle);
            Assert.Equal("CONSTRUCTION", merged.ProvenanceByPathId[constructionLine.Id].LayerName);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Contains(
                "\n0\nLAYER\n5\n100\n330\n29\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\nCONSTRUCTION\n70\n0\n62\n8\n6\nDASHED\n",
                raw,
                StringComparison.Ordinal);
            Assert.Contains("\n0\nLINE\n5\n101\n330\n1F\n100\nAcDbEntity\n8\nCONSTRUCTION\n6\nDASHED\n62\n8\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nTABLE\n2\nLTYPE\n5\n102\n", raw, StringComparison.Ordinal);
            Assert.Contains(
                "\n0\nLTYPE\n5\n103\n330\n102\n100\nAcDbSymbolTableRecord\n100\nAcDbLinetypeTableRecord\n2\nDASHED\n70\n0\n3\nDashed __ __ __\n72\n65\n73\n2\n40\n0.75\n49\n0.5\n74\n0\n49\n-0.25\n74\n0\n",
                raw,
                StringComparison.Ordinal);
            Assert.Contains("\n9\n$HANDSEED\n5\n104\n", raw, StringComparison.Ordinal);
            var reopened = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(exportPath));
            var reopenedLine = Assert.Single(reopened.Paths, path => path.SourceEntityHandle == "101");
            Assert.Equal("CONSTRUCTION", reopenedLine.SourceLayerName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MixedNormalAndConstructionLayers_ShareOneNewLinetypeTable()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "mixed-layers-source.dxf");
        var exportPath = Path.Combine(directory, "mixed-layers-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var regularLine = new Editor2DPreviewPath("mixed-regular", "LINE", [new(0, 7), new(10, 7)], false);
            var constructionLine = new Editor2DPreviewPath(
                "mixed-construction", "LINE", [new(0, 9), new(10, 9)], false, IsConstruction: true);
            var paths = sourceDocument.Paths.Append(regularLine).Append(constructionLine).ToArray();
            var metadata = sourceDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);
            metadata[regularLine.Id] = new Editor2DExportPathMetadata("LASER", "#123456");
            metadata[constructionLine.Id] = new Editor2DExportPathMetadata("CONSTRUCTION", "#808080");

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.True(merged.Succeeded);
            var raw = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.Equal(1, raw.Split("\n0\nTABLE\n2\nLTYPE\n", StringSplitOptions.None).Length - 1);
            Assert.Contains("\n2\nLTYPE\n5\n104\n330\n0\n100\nAcDbSymbolTable\n70\n2\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nLTYPE\n5\n105\n330\n104\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n2\nCONTINUOUS\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n0\nLTYPE\n5\n106\n330\n104\n", raw, StringComparison.Ordinal);
            Assert.Contains("\n2\nDASHED\n", raw, StringComparison.Ordinal);
            Assert.Equal("102", merged.ProvenanceByPathId[regularLine.Id].Handle);
            Assert.Equal("103", merged.ProvenanceByPathId[constructionLine.Id].Handle);
            Assert.Contains("\n9\n$HANDSEED\n5\n107\n", raw, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SolidRecordNamedDashed_RejectsConstructionMergeWithoutTouchingTarget()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "invalid-dashed-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = OwnedModelSpaceVendorTablesDxf().Replace(
                "2\nVENDOR_LTYPE\n70\n0\n3\nVendor\n72\n65\n73\n0\n40\n0",
                "2\nDASHED\n70\n0\n3\nFake dashed\n72\n65\n73\n0\n40\n0",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var constructionLine = new Editor2DPreviewPath(
                "invalid-dashed-construction", "LINE", [new(0, 7), new(10, 7)], false, IsConstruction: true);
            var paths = sourceDocument.Paths.Append(constructionLine).ToArray();
            var metadata = sourceDocument.Paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);
            metadata[constructionLine.Id] = new Editor2DExportPathMetadata("CONSTRUCTION", "#808080");

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.False(merged.Succeeded);
            Assert.Equal("sentinel", await File.ReadAllTextAsync(exportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAs_PersistsNewEntityProvenanceAndMergedDxfBytes()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "owned-source.dxf");
        var projectPath = Path.Combine(directory, "owned-project.stch");
        var saveAsPath = Path.Combine(directory, "owned-copy.stch");
        var exportPath = Path.Combine(directory, "owned-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            using var editor = await OpenEditorAsync(projectPath, exportPath, outputService, saveAsPath);
            var newPathId = editor.CreateTwoDLine(new(0, 5), new(10, 5))!;

            await editor.SaveDocumentAsAsync();

            Assert.Equal(Path.GetFullPath(saveAsPath), editor.ProjectSession!.ProjectFilePath);
            Assert.Equal(
                "100",
                Assert.Single(editor.TwoDDocument!.Paths, path => path.Id == newPathId).SourceEntityHandle);
            var savedState = await new Project3DStateService(outputService).LoadAsync(saveAsPath);
            var persisted = Assert.Single(
                savedState.TwoDWorkspaceState!.Document.Paths,
                path => path.Id == newPathId);
            Assert.Equal("100", persisted.SourceEntityHandle);
            Assert.Equal("CUT", persisted.SourceLayerName);
            var embedded = Normalize(Encoding.UTF8.GetString(
                Convert.FromBase64String(savedState.GeneratedOutputDataBase64!)));
            Assert.Contains("\n9\n$HANDSEED\n5\n101\n", embedded, StringComparison.Ordinal);
            Assert.Contains("\n5\n100\n330\n1F\n", embedded, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("ambiguous-owner")]
    [InlineData("duplicate-definition")]
    [InlineData("layer-capacity-too-small")]
    public async Task InvalidNewEntityOwnershipOrHandleGraph_RejectsWithoutTouchingTarget(string variation)
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "invalid-source.dxf");
        var exportPath = Path.Combine(directory, "sentinel.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var source = variation switch
            {
                "ambiguous-owner" => OwnedModelSpaceDxf().Replace(
                    "0\nTEXT\n5\n12\n330\n1F",
                    "0\nTEXT\n5\n12\n330\n2F",
                    StringComparison.Ordinal),
                "duplicate-definition" => OwnedModelSpaceDxf().Replace(
                    "0\nLINE\n5\nFF\n330\n1F",
                    "0\nLINE\n5\n10\n330\n1F",
                    StringComparison.Ordinal),
                "layer-capacity-too-small" => OwnedModelSpaceDxf().Replace(
                    "2\nLAYER\n5\n29\n70\n1",
                    "2\nLAYER\n5\n29\n70\n0",
                    StringComparison.Ordinal),
                _ => throw new InvalidOperationException(),
            };
            await File.WriteAllTextAsync(sourcePath, source, Encoding.UTF8);
            await File.WriteAllTextAsync(exportPath, "sentinel", Encoding.ASCII);
            var sourceDocument = Assert.IsType<Editor2DPreviewDocument>(
                await outputService.LoadPreviewDocumentAsync(sourcePath));
            var newLine = new Editor2DPreviewPath(
                "new-line", "LINE", [new(0, 5), new(10, 5)], false);
            var paths = sourceDocument.Paths.Append(newLine).ToArray();
            var metadata = paths.ToDictionary(
                path => path.Id,
                path => new Editor2DExportPathMetadata(path.SourceLayerName ?? "CUT", "#4D7FFF"),
                StringComparer.Ordinal);

            var merged = await outputService.TrySaveMergedDxfDocumentAsync(
                await File.ReadAllBytesAsync(sourcePath),
                new Editor2DExportDocument(sourceDocument with { Paths = paths }, metadata),
                exportPath,
                new Editor2DExportOptions(DxfVersion: "R2010"));

            Assert.False(merged.Succeeded);
            Assert.Equal("sentinel", await File.ReadAllTextAsync(exportPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NewEntityHandle_PersistsAcrossProjectSaveReopenAndSecondMerge()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "owned-source.dxf");
        var projectPath = Path.Combine(directory, "owned-project.stch");
        var exportPath = Path.Combine(directory, "owned-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OwnedModelSpaceDxf(), Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            string newPathId;
            using (var editor = await OpenEditorAsync(projectPath, exportPath, outputService))
            {
                newPathId = editor.CreateTwoDLine(new(0, 5), new(10, 5))!;
                await editor.SaveDocumentAsync();

                var promoted = Assert.Single(editor.TwoDDocument!.Paths, path => path.Id == newPathId);
                Assert.Equal("100", promoted.SourceEntityHandle);
                Assert.Equal("CUT", promoted.SourceLayerName);
                Assert.False(editor.IsDirty);
            }

            var stateService = new Project3DStateService(outputService);
            var firstSavedState = await stateService.LoadAsync(projectPath);
            var firstPersisted = Assert.Single(
                firstSavedState.TwoDWorkspaceState!.Document.Paths,
                path => path.Id == newPathId);
            Assert.Equal("100", firstPersisted.SourceEntityHandle);
            Assert.Equal("CUT", firstPersisted.SourceLayerName);
            var firstEmbedded = Normalize(Encoding.UTF8.GetString(
                Convert.FromBase64String(firstSavedState.GeneratedOutputDataBase64!)));
            Assert.Contains("\n9\n$HANDSEED\n5\n101\n", firstEmbedded, StringComparison.Ordinal);
            Assert.Contains("\n5\n100\n330\n1F\n", firstEmbedded, StringComparison.Ordinal);

            using (var reopened = await OpenEditorAsync(projectPath, exportPath, outputService))
            {
                var reopenedPath = Assert.Single(reopened.TwoDDocument!.Paths, path => path.Id == newPathId);
                Assert.Equal("100", reopenedPath.SourceEntityHandle);
                reopened.TwoDDocument = reopened.TwoDDocument with
                {
                    Paths = reopened.TwoDDocument.Paths.Select(path => path.Id == newPathId
                        ? path with { Points = [new(0, 5), new(20, 5)] }
                        : path).ToArray(),
                };
                await reopened.SaveDocumentAsync();
                Assert.Equal(
                    "100",
                    Assert.Single(reopened.TwoDDocument.Paths, path => path.Id == newPathId).SourceEntityHandle);
            }

            var secondSavedState = await stateService.LoadAsync(projectPath);
            var secondPersisted = Assert.Single(
                secondSavedState.TwoDWorkspaceState!.Document.Paths,
                path => path.Id == newPathId);
            Assert.Equal("100", secondPersisted.SourceEntityHandle);
            var secondEmbedded = Normalize(Encoding.UTF8.GetString(
                Convert.FromBase64String(secondSavedState.GeneratedOutputDataBase64!)));
            Assert.Contains("\n9\n$HANDSEED\n5\n101\n", secondEmbedded, StringComparison.Ordinal);
            Assert.Contains("\n5\n100\n330\n1F\n", secondEmbedded, StringComparison.Ordinal);
            Assert.Contains("\n20\n", secondEmbedded, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReferencedDeletion_RejectsMergeAndUsesSerializer()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "referenced-deletion.dxf");
        var projectPath = Path.Combine(directory, "project.stch");
        var exportPath = Path.Combine(directory, "referenced-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var referencedSource = OpaqueDxf().Replace(
                "40\n2\n1\nOriginal label\n0\nINSERT",
                "40\n2\n1\nOriginal label\n1001\nREF_APP\n1005\n10\n0\nINSERT",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, referencedSource, Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            using var editor = await OpenEditorAsync(projectPath, exportPath, outputService);
            var line = Assert.Single(editor.TwoDDocument!.Paths, path => path.EntityType == "LINE");
            editor.TwoDDocument = editor.TwoDDocument with
            {
                Paths = editor.TwoDDocument.Paths.Where(path => path.Id != line.Id).ToArray(),
            };

            await editor.ExportTwoDDxfAsync();

            var exported = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.DoesNotContain("\nBLOCKS\n", exported, StringComparison.Ordinal);
            Assert.DoesNotContain("\nREF_APP\n", exported, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task DuplicateSourceHandles_RejectMergeBeforeWritingTarget()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "duplicate-handles.dxf");
        var projectPath = Path.Combine(directory, "project.stch");
        var exportPath = Path.Combine(directory, "duplicate-export.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            var duplicateSource = OpaqueDxf().Replace(
                "0\nTEXT\n5\n12\n8\nCUT",
                "0\nTEXT\n5\n10\n8\nCUT",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(sourcePath, duplicateSource, Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            using var editor = await OpenEditorAsync(projectPath, exportPath, outputService);
            var line = Assert.Single(editor.TwoDDocument!.Paths, path => path.EntityType == "LINE");
            editor.TwoDDocument = editor.TwoDDocument with
            {
                Paths = editor.TwoDDocument.Paths.Select(path => path.Id == line.Id
                    ? path with { Points = [new(0, 0), new(30, 0)] }
                    : path).ToArray(),
            };

            await editor.ExportTwoDDxfAsync();

            var exported = Normalize(await File.ReadAllTextAsync(exportPath));
            Assert.DoesNotContain("\nBLOCKS\n", exported, StringComparison.Ordinal);
            Assert.Contains("\n30\n", exported, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public async Task UndoBackToBaseline_RestoresOpaquePreservation()
    {
        var directory = TemporaryDirectory();
        var sourcePath = Path.Combine(directory, "source.dxf");
        var projectPath = Path.Combine(directory, "project.stch");
        var exportPath = Path.Combine(directory, "undo.dxf");
        var outputService = new DxfOutputPreviewService();
        try
        {
            await File.WriteAllTextAsync(sourcePath, OpaqueDxf(), Encoding.UTF8);
            await CreateProjectAsync(projectPath, sourcePath, outputService);
            using var editor = await OpenEditorAsync(projectPath, exportPath, outputService);
            var original = editor.TwoDDocument!;
            var line = Assert.Single(original.Paths, path => path.EntityType == "LINE");
            editor.TwoDDocument = original with
            {
                Paths = original.Paths.Select(path => path.Id == line.Id
                    ? path with { Points = [new(0, 0), new(25, 0)] }
                    : path).ToArray(),
            };

            await editor.ExportTwoDDxfAsync();
            AssertOpaqueStructureFile(exportPath);
            Assert.True(editor.UndoTwoDWorkspace());

            await editor.ExportTwoDDxfAsync();
            AssertOpaqueStructureFile(exportPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
    private static async Task CreateProjectAsync(
        string projectPath,
        string sourcePath,
        IEditorOutputPreviewService outputService)
    {
        var document = (await outputService.LoadPreviewDocumentAsync(sourcePath))!;
        var workspace = new Editor2DWorkspaceState(
            document,
            IsInitialized: true,
            ExportPreferences: new Editor2DExportPreferences(DxfVersion: "R2010"));
        var state = Project3DState.Empty with
        {
            GeneratedOutputPath = sourcePath,
            GeneratedOutputDataBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(sourcePath)),
            TwoDWorkspaceState = workspace,
        };
        await new Project3DStateService(outputService).SaveAsync(projectPath, state);
    }

    private static async Task<EditorPageViewModel> OpenEditorAsync(
        string projectPath,
        string exportPath,
        IEditorOutputPreviewService outputService,
        string? saveAsPath = null)
    {
        var stateService = new Project3DStateService(outputService);
        var editor = EditorPageViewModelModeTests.CreateViewModelForTests(
            projectFileDialogService: new FixedDxfExportDialogService(exportPath, saveAsPath),
            outputPreviewService: outputService,
            project3DStateService: stateService);
        var session = new ProjectSession(
            Guid.NewGuid(),
            "Opaque DXF",
            projectPath,
            new ProjectTemplateDefinition("blank", "Blank", "Untitled"),
            ProjectSessionOrigin.Opened,
            DateTimeOffset.UtcNow);
        Assert.True(await editor.ConfigureParametersAsync(
            new Dictionary<string, object> { [EditorNavigationParameterKeys.ProjectSession] = session },
            CancellationToken.None));
        await ((INavigablePageViewModel)editor).LoadAsync(CancellationToken.None);
        Assert.NotNull(editor.TwoDDocument);
        return editor;
    }

    private static async Task<string> ReadEmbeddedDxfAsync(string projectPath)
    {
        using var archive = ZipFile.OpenRead(projectPath);
        await using var stream = archive.GetEntry("project.json")!.Open();
        using var json = await JsonDocument.ParseAsync(stream);
        var encoded = json.RootElement.GetProperty("dxfDataBase64").GetString();
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded!));
    }

    private static void AssertOpaqueStructureFile(string path)
        => AssertOpaqueStructure(File.ReadAllText(path));

    private static void AssertOpaqueStructure(string raw)
    {
        var normalized = Normalize(raw);
        Assert.Contains("\nBLOCKS\n", normalized, StringComparison.Ordinal);
        Assert.Contains("\nINSERT\n", normalized, StringComparison.Ordinal);
        Assert.Contains("\nVENDOR_APP\n", normalized, StringComparison.Ordinal);
        Assert.Contains("\nDASHED_VENDOR\n", normalized, StringComparison.Ordinal);
        Assert.Contains("\n$INSUNITS\n70\n4\n", normalized, StringComparison.Ordinal);
        Assert.Contains("\n$MEASUREMENT\n70\n1\n", normalized, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
        => "\n" + value.Replace("\r", string.Empty, StringComparison.Ordinal).Trim('\n') + "\n";

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pathstitch-regular-structure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string MinimalDxf()
        => string.Join("\n",
        [
            "0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1024",
            "9", "$INSUNITS", "70", "4", "9", "$MEASUREMENT", "70", "1", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES", "0", "ENDSEC", "0", "EOF", string.Empty,
        ]);
    private static string PlanarGeometryDxf()
        => string.Join("\n",
        [
            "0", "SECTION", "2", "HEADER", "9", "$ACADVER", "1", "AC1024",
            "9", "$INSUNITS", "70", "4", "9", "$MEASUREMENT", "70", "1", "0", "ENDSEC",
            "0", "SECTION", "2", "BLOCKS",
            "0", "BLOCK", "5", "70", "2", "VENDOR_BLOCK", "70", "0", "10", "0", "20", "0",
            "0", "LINE", "5", "71", "8", "CUT", "10", "0", "20", "0", "11", "1", "21", "1",
            "0", "ENDBLK", "5", "72", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LINE", "5", "10", "8", "CUT", "10", "0", "20", "0", "11", "10", "21", "0",
            "0", "LWPOLYLINE", "5", "20", "8", "CUT", "90", "3", "70", "0",
            "10", "0", "20", "0", "10", "5", "20", "0", "10", "5", "20", "5",
            "1001", "VENDOR_APP", "1000", "polyline-payload",
            "0", "CIRCLE", "5", "30", "8", "CUT", "10", "5", "20", "5", "40", "3",
            "0", "ARC", "5", "40", "8", "CUT", "10", "10", "20", "0", "40", "4", "50", "0", "51", "90",
            "0", "TEXT", "5", "50", "8", "CUT", "10", "2", "20", "2", "40", "2", "1", "Untouched",
            "0", "INSERT", "5", "60", "8", "CUT", "2", "VENDOR_BLOCK", "10", "20", "20", "30",
            "0", "ENDSEC", "0", "EOF", string.Empty,
        ]);
    private static string OwnedModelSpaceRichTextDxf()
    {
        var source = OwnedModelSpaceDxf().Replace(
            "0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nBLOCKS",
            string.Join("\n",
            [
                "0", "ENDTAB",
                "0", "TABLE", "2", "APPID", "5", "2B", "330", "0", "100", "AcDbSymbolTable", "70", "1",
                "0", "APPID", "5", "2C", "330", "2B", "100", "AcDbSymbolTableRecord",
                "100", "AcDbRegAppTableRecord", "2", "PATHSTITCH", "70", "0",
                "0", "ENDTAB", "0", "ENDSEC", "0", "SECTION", "2", "BLOCKS",
            ]),
            StringComparison.Ordinal);
        return source.Replace(
            "1\nUntouched\n0\nENDSEC",
            string.Join("\n",
            [
                "1", "Untouched",
                "1001", "PATHSTITCH", "1000", "Arial", "1070", "1", "1070", "0", "1070", "0",
                "1040", "0", "1000", "Untouched",
                "1001", "TEXT_VENDOR", "1000", "vendor-payload",
                "0", "ENDSEC",
            ]),
            StringComparison.Ordinal);
    }

    private static string OwnedModelSpaceVendorTablesDxf()
        => OwnedModelSpaceDxf().Replace(
            "0\nENDTAB\n0\nTABLE\n2\nLAYER",
            string.Join("\n",
            [
                "0", "ENDTAB",
                "0", "TABLE", "2", "LTYPE", "5", "2B", "330", "0", "100", "AcDbSymbolTable", "70", "2",
                "0", "LTYPE", "5", "2C", "330", "2B", "100", "AcDbSymbolTableRecord",
                "100", "AcDbLinetypeTableRecord", "2", "VENDOR_LTYPE", "70", "0", "3", "Vendor", "72", "65", "73", "0", "40", "0",
                "0", "ENDTAB",
                "0", "TABLE", "2", "STYLE", "5", "2D", "330", "0", "100", "AcDbSymbolTable", "70", "2",
                "0", "STYLE", "5", "2E", "330", "2D", "100", "AcDbSymbolTableRecord",
                "100", "AcDbTextStyleTableRecord", "2", "VENDOR_STYLE", "70", "0", "40", "0", "41", "1", "50", "0", "71", "0", "42", "2.5", "3", "vendor.ttf", "4", "",
                "0", "ENDTAB",
                "0", "TABLE", "2", "APPID", "5", "2F", "330", "0", "100", "AcDbSymbolTable", "70", "1",
                "0", "APPID", "5", "30", "330", "2F", "100", "AcDbSymbolTableRecord",
                "100", "AcDbRegAppTableRecord", "2", "VENDOR_APP", "70", "0",
                "0", "ENDTAB",
                "0", "TABLE", "2", "LAYER",
            ]),
            StringComparison.Ordinal);
    private static string OwnedModelSpaceEllipseEdgeHatchDxf()
        => OwnedModelSpaceCustomEdgeHatchDxf(
        [
            "91", "1", "92", "1", "93", "1",
            "72", "3", "10", "5", "20", "5", "11", "5", "21", "0", "40", "0.5",
            "50", "0", "51", "360", "73", "1",
            "97", "0", "75", "0", "76", "1", "98", "0",
        ]);

    private static string OwnedModelSpaceSplineEdgeHatchDxf()
        => OwnedModelSpaceCustomEdgeHatchDxf(
        [
            "91", "1", "92", "1", "93", "2",
            "72", "4", "94", "2", "73", "1", "74", "0", "95", "6", "96", "3",
            "40", "0", "40", "0", "40", "0", "40", "1", "40", "1", "40", "1",
            "42", "1", "42", "1", "42", "1",
            "10", "0", "20", "0", "10", "5", "20", "10", "10", "10", "20", "0",
            "97", "0",
            "72", "1", "10", "10", "20", "0", "11", "0", "21", "0",
            "97", "0", "75", "0", "76", "1", "98", "0",
        ]);

    private static string OwnedModelSpaceCustomEdgeHatchDxf(IReadOnlyList<string> boundaryPairs)
    {
        var hatch = string.Join("\n",
            new[]
            {
                "0", "HATCH", "5", "13", "330", "1F", "8", "CUT",
                "100", "AcDbEntity", "100", "AcDbHatch",
                "10", "0", "20", "0", "30", "0",
                "210", "0", "220", "0", "230", "1",
                "2", "SOLID", "70", "1", "71", "0",
            }.Concat(boundaryPairs));
        return OwnedModelSpaceDxf().Replace(
            "0\nENDSEC\n0\nEOF\n",
            hatch + "\n0\nENDSEC\n0\nEOF\n",
            StringComparison.Ordinal);
    }

    private static string OwnedModelSpaceEdgeHatchDxf()
    {
        var hatch = string.Join("\n",
        [
            "0", "HATCH", "5", "13", "330", "1F", "8", "CUT",
            "100", "AcDbEntity", "100", "AcDbHatch",
            "10", "0", "20", "0", "30", "0",
            "210", "0", "220", "0", "230", "1",
            "2", "SOLID", "70", "1", "71", "0", "91", "1",
            "92", "1", "93", "4",
            "72", "1", "10", "0", "20", "0", "11", "10", "21", "0",
            "72", "2", "10", "10", "20", "5", "40", "5", "50", "270", "51", "90", "73", "1",
            "72", "1", "10", "10", "20", "10", "11", "0", "21", "10",
            "72", "1", "10", "0", "20", "10", "11", "0", "21", "0",
            "97", "0", "75", "0", "76", "1", "98", "0",
        ]);
        return OwnedModelSpaceDxf().Replace(
            "0\nENDSEC\n0\nEOF\n",
            hatch + "\n0\nENDSEC\n0\nEOF\n",
            StringComparison.Ordinal);
    }

    private static string OwnedModelSpaceHatchDxf(bool associative)
    {
        var hatch = string.Join("\n",
        [
            "0", "HATCH", "5", "13", "330", "1F", "8", "CUT",
            "100", "AcDbEntity", "100", "AcDbHatch",
            "10", "0", "20", "0", "30", "0",
            "210", "0", "220", "0", "230", "1",
            "2", "SOLID", "70", "1", "71", associative ? "1" : "0", "91", "1",
            "92", "3", "72", "0", "73", "1", "93", "4",
            "10", "0", "20", "0", "10", "10", "20", "0",
            "10", "10", "20", "5", "10", "0", "20", "5",
            "97", "0", "75", "0", "76", "1", "98", "0",
        ]);
        return OwnedModelSpaceDxf().Replace(
            "0\nENDSEC\n0\nEOF\n",
            hatch + "\n0\nENDSEC\n0\nEOF\n",
            StringComparison.Ordinal);
    }

    private static string OwnedModelSpaceDxf()
        => string.Join("\n",
        [
            "0", "SECTION", "2", "HEADER",
            "9", "$ACADVER", "1", "AC1024",
            "9", "$HANDSEED", "5", "20",
            "9", "$INSUNITS", "70", "4",
            "9", "$MEASUREMENT", "70", "1",
            "0", "ENDSEC",
            "0", "SECTION", "2", "TABLES",
            "0", "TABLE", "2", "BLOCK_RECORD", "5", "1A", "70", "1",
            "0", "BLOCK_RECORD", "5", "1F", "330", "1A", "2", "*Model_Space", "70", "0",
            "0", "ENDTAB",
            "0", "TABLE", "2", "LAYER", "5", "29", "70", "1",
            "0", "LAYER", "5", "2A", "330", "29", "2", "CUT", "70", "0", "62", "7", "6", "CONTINUOUS",
            "0", "ENDTAB", "0", "ENDSEC",
            "0", "SECTION", "2", "BLOCKS",
            "0", "BLOCK", "5", "FE", "330", "1F", "2", "OPAQUE_BLOCK", "70", "0", "10", "0", "20", "0",
            "0", "LINE", "5", "FF", "330", "1F", "8", "CUT", "10", "0", "20", "0", "11", "1", "21", "1",
            "0", "ENDBLK", "5", "FD", "330", "1F", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LINE", "5", "10", "330", "1F", "8", "CUT", "10", "0", "20", "0", "11", "10", "21", "0",
            "0", "TEXT", "5", "12", "330", "1F", "8", "CUT", "10", "2", "20", "2", "40", "2", "1", "Untouched",
            "0", "ENDSEC", "0", "EOF", string.Empty,
        ]);

    private static string OpaqueDxf()
        => string.Join("\n",
        [
            "0", "SECTION", "2", "HEADER",
            "9", "$ACADVER", "1", "AC1024",
            "9", "$INSUNITS", "70", "0",
            "9", "$MEASUREMENT", "70", "0",
            "0", "ENDSEC",
            "0", "SECTION", "2", "TABLES",
            "0", "TABLE", "2", "LTYPE", "70", "1",
            "0", "LTYPE", "2", "DASHED_VENDOR", "70", "0", "3", "Vendor pattern", "72", "65", "73", "0", "40", "0",
            "0", "ENDTAB", "0", "ENDSEC",
            "0", "SECTION", "2", "BLOCKS",
            "0", "BLOCK", "2", "VENDOR_BLOCK", "70", "0", "10", "0", "20", "0",
            "0", "LINE", "8", "CUT", "10", "0", "20", "0", "11", "5", "21", "5",
            "0", "ENDBLK", "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES",
            "0", "LINE", "5", "10", "8", "CUT", "10", "0", "20", "0", "11", "10", "21", "0",
            "1001", "VENDOR_APP", "1000", "opaque-payload",
            "0", "TEXT", "5", "12", "8", "CUT", "10", "2", "20", "2", "40", "2", "1", "Original label",
            "0", "INSERT", "5", "11", "8", "CUT", "2", "VENDOR_BLOCK", "10", "20", "20", "30",
            "0", "ENDSEC", "0", "EOF", string.Empty,
        ]);

    private sealed class FixedDxfExportDialogService(string outputPath, string? saveAsPath = null) : IProjectFileDialogService
    {
        public Task<string?> PickDxfExportFileAsync(string suggestedFileName, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(outputPath);

        public Task<string?> PickProjectSaveAsFileAsync(
            string suggestedFileName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(saveAsPath);

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
}
