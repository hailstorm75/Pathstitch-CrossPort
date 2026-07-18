using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.Services;

namespace Pathstitch.App;

internal static class MacOSPackagedAcceptance
{
    public static async Task<int> RunAsync(IServiceProvider services, Window window, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var evidencePath = Path.Combine(outputDirectory, "packaged-app-acceptance.json");
        var evidence = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 4,
            ["platform"] = Environment.OSVersion.Platform.ToString(),
        };

        try
        {
            var dialogService = services.GetRequiredService<IProjectFileDialogService>();
            if (dialogService is not ProjectFileDialogService)
                throw new InvalidOperationException("The packaged app did not resolve the native project file-dialog service.");
            var storageProvider = window.StorageProvider
                ?? throw new InvalidOperationException("The packaged window has no platform storage provider.");
            if (!storageProvider.CanOpen || !storageProvider.CanSave)
                throw new InvalidOperationException("The native storage provider cannot open and save files.");
            evidence["fileDialogs"] = new
            {
                service = dialogService.GetType().FullName,
                provider = storageProvider.GetType().FullName,
                storageProvider.CanOpen,
                storageProvider.CanSave,
            };

            var webView = new NativeWebView();
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.Loaded += (_, _) => loaded.TrySetResult();
            webView.NavigationCompleted += (_, args) => navigation.TrySetResult(args.IsSuccess);
            window.Content = webView;
            if (await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(true) != loaded.Task)
                throw new InvalidOperationException("The packaged native WebView was not attached to the window.");
            webView.NavigateToString("<!doctype html><html><body><canvas id='viewport'></canvas><script>document.title='Pathstitch acceptance';</script></body></html>");
            var completed = await Task.WhenAny(navigation.Task, Task.Delay(TimeSpan.FromSeconds(25))).ConfigureAwait(true);
            if (completed != navigation.Task || !await navigation.Task.ConfigureAwait(true))
                throw new InvalidOperationException("The packaged native WebView did not initialize and navigate successfully.");
            evidence["webView"] = new { control = webView.GetType().FullName, navigationCompleted = true };

            var finderPatternApplied = MacOSIntegrationService.TrySetQuickLookPreferences(
                dxfEnabled: false,
                stepEnabled: true,
                stchEnabled: false);
            var finderPatternReadBack =
                MacOSIntegrationService.TryGetQuickLookPreference(
                    MacOSFinderPreviewFormat.Dxf, out var dxfEnabled) && !dxfEnabled
                && MacOSIntegrationService.TryGetQuickLookPreference(
                    MacOSFinderPreviewFormat.Step, out var stepEnabled) && stepEnabled
                && MacOSIntegrationService.TryGetQuickLookPreference(
                    MacOSFinderPreviewFormat.Stch, out var stchEnabled) && !stchEnabled;
            var finderDefaultsRestored = MacOSIntegrationService.TrySetQuickLookPreferences(
                dxfEnabled: true,
                stepEnabled: true,
                stchEnabled: true);
            var finderDefaultsReadBack =
                MacOSIntegrationService.TryGetQuickLookPreference(
                    MacOSFinderPreviewFormat.Dxf, out dxfEnabled) && dxfEnabled
                && MacOSIntegrationService.TryGetQuickLookPreference(
                    MacOSFinderPreviewFormat.Step, out stepEnabled) && stepEnabled
                && MacOSIntegrationService.TryGetQuickLookPreference(
                    MacOSFinderPreviewFormat.Stch, out stchEnabled) && stchEnabled;
            var lightIconApplied = MacOSIntegrationService.TryApplyDockIcon("Light");
            var darkIconApplied = MacOSIntegrationService.TryApplyDockIcon("Dark");
            var automaticIconApplied = MacOSIntegrationService.TryApplyDockIcon("Automatic");
            if (!finderPatternApplied
                || !finderPatternReadBack
                || !finderDefaultsRestored
                || !finderDefaultsReadBack
                || !lightIconApplied
                || !darkIconApplied
                || !automaticIconApplied)
            {
                throw new InvalidOperationException(
                    "The packaged macOS preference/icon bridge did not apply and read back every acceptance value.");
            }
            evidence["macIntegration"] = new
            {
                independentFinderPatternApplied = true,
                independentFinderPatternReadBack = true,
                finderDefaultsRestored = true,
                finderDefaultsReadBack = true,
                lightIconApplied = true,
                darkIconApplied = true,
                automaticIconApplied = true,
            };

            var stepFixture = Environment.GetEnvironmentVariable(
                "PATHSTITCH_MACOS_ACCEPTANCE_STEP_FIXTURE");
            if (string.IsNullOrWhiteSpace(stepFixture) || !File.Exists(stepFixture))
                throw new InvalidOperationException("Packaged STEP acceptance fixture was not provided.");

            stepFixture = Path.GetFullPath(stepFixture);
            var kernel = services.GetRequiredService<IStepGeometryKernelService>();
            var imported = await kernel.ImportAsync(stepFixture).ConfigureAwait(true);
            if (!imported.IsSuccess
                || imported.Document is null
                || imported.ViewportBodies is null
                || imported.ViewportBodies.Count == 0
                || string.IsNullOrWhiteSpace(imported.ViewportJson))
            {
                throw new InvalidOperationException($"Packaged STEP import failed: {imported.Message}");
            }

            var visibleBodyIndices = Enumerable.Range(0, imported.Document.Bodies.Count).ToArray();
            var visibleBodyIds = imported.Document.Bodies.Select(body => body.Id).ToArray();
            var projection = await kernel.ProjectAsync(new EditorProjectionRequest(
                stepFixture,
                PlaneType: "XY",
                Offset: 0,
                FaceIndex: null,
                FaceBodyIndex: null,
                VisibleBodyIndices: visibleBodyIndices,
                BodyOffsets: [],
                VisibleBodyIds: visibleBodyIds)).ConfigureAwait(true);
            if (!projection.IsSuccess
                || string.IsNullOrWhiteSpace(projection.OutputPath)
                || !File.Exists(projection.OutputPath))
            {
                throw new InvalidOperationException($"Packaged STEP projection failed: {projection.Message}");
            }

            var selectedBodyIndex = imported.Document.Bodies
                .ToList()
                .FindIndex(body => body.Faces.Count > 0);
            if (selectedBodyIndex < 0)
                throw new InvalidOperationException("Packaged STEP fixture contained no unfoldable face.");
            var selectedBody = imported.Document.Bodies[selectedBodyIndex];
            var selectedFace = selectedBody.Faces[0];
            var unfold = await kernel.UnfoldAsync(new EditorUnfoldRequest(
                stepFixture,
                SelectedFaces:
                [
                    new SelectedFace3D(
                        selectedBodyIndex,
                        0,
                        selectedBody.Id,
                        selectedFace.Id),
                ],
                VisibleBodyIndices: visibleBodyIndices,
                WholeBody: false,
                DistortionMode: "conformal",
                SelectedFaceIds: [selectedFace.Id],
                VisibleBodyIds: visibleBodyIds)).ConfigureAwait(true);
            if (!unfold.IsSuccess
                || string.IsNullOrWhiteSpace(unfold.OutputPath)
                || !File.Exists(unfold.OutputPath))
            {
                throw new InvalidOperationException($"Packaged STEP unfold failed: {unfold.Message}");
            }

            var projectionPath = Path.Combine(outputDirectory, "packaged-projection.dxf");
            var unfoldPath = Path.Combine(outputDirectory, "packaged-unfold.dxf");
            File.Copy(projection.OutputPath, projectionPath, overwrite: true);
            File.Copy(unfold.OutputPath, unfoldPath, overwrite: true);
            if (new FileInfo(projectionPath).Length <= 100 || new FileInfo(unfoldPath).Length <= 100)
                throw new InvalidOperationException("Packaged projection/unfold DXF output was empty or trivial.");
            ValidateCanonicalMillimeterDxf(projectionPath);
            ValidateCanonicalMillimeterDxf(unfoldPath);

            var projectPath = Path.Combine(outputDirectory, "packaged-roundtrip.stch");
            var persistence = services.GetRequiredService<Project3DStateService>();
            var expectedSourceSha256 = await HashFileAsync(stepFixture).ConfigureAwait(true);
            var expectedOutputSha256 = await HashFileAsync(unfoldPath).ConfigureAwait(true);
            var expectedTopologyJson = JsonSerializer.Serialize(imported.Document);
            await persistence.SaveAsync(
                projectPath,
                new Project3DState(
                    imported.ViewportJson,
                    imported.ViewportBodies,
                    [],
                    SourceModelPath: stepFixture,
                    GeneratedOutputPath: unfoldPath,
                    StepTopology: imported.Document)).ConfigureAwait(true);
            var reopened = await persistence.LoadAsync(projectPath).ConfigureAwait(true);
            var viewportPreserved = reopened.ViewportJson == imported.ViewportJson;
            var topologyPreserved = reopened.StepTopology is not null
                && JsonSerializer.Serialize(reopened.StepTopology) == expectedTopologyJson;
            var reopenedSourcePath = reopened.SourceModelPath;
            var reopenedOutputPath = reopened.GeneratedOutputPath;
            var sourceModelPreserved = reopenedSourcePath is not null
                && File.Exists(reopenedSourcePath)
                && await HashFileAsync(reopenedSourcePath).ConfigureAwait(true) == expectedSourceSha256;
            var generatedOutputPreserved = reopenedOutputPath is not null
                && File.Exists(reopenedOutputPath)
                && await HashFileAsync(reopenedOutputPath).ConfigureAwait(true) == expectedOutputSha256;
            if (!File.Exists(projectPath)
                || !viewportPreserved
                || !topologyPreserved
                || !sourceModelPreserved
                || !generatedOutputPreserved)
            {
                throw new InvalidOperationException(
                    "The packaged .stch save/reopen round trip did not preserve exact STEP/source/output state.");
            }

            var stepFixtureEvidence = await PreserveInputFixtureAsync(
                stepFixture,
                outputDirectory,
                "packaged-input.step").ConfigureAwait(true);
            evidence["stepWorkflow"] = new
            {
                fixture = stepFixture,
                fixtureSha256 = await HashFileAsync(stepFixture).ConfigureAwait(true),
                fixtureEvidence = stepFixtureEvidence,
                imported = true,
                documentId = imported.Document.DocumentId,
                bodyCount = imported.Document.Bodies.Count,
                faceCount = imported.Document.Bodies.Sum(body => body.Faces.Count),
                projected = true,
                projection = await DescribeDxfFileAsync(projectionPath).ConfigureAwait(true),
                unfolded = true,
                unfold = await DescribeDxfFileAsync(unfoldPath).ConfigureAwait(true),
            };
            evidence["projectRoundTrip"] = new
            {
                path = projectPath,
                bytes = new FileInfo(projectPath).Length,
                sha256 = await HashFileAsync(projectPath).ConfigureAwait(true),
                viewportPreserved,
                topologyPreserved,
                sourceModelPreserved,
                sourceSha256 = expectedSourceSha256,
                reopenedSourceSha256 = await HashFileAsync(reopenedSourcePath!).ConfigureAwait(true),
                generatedOutputPreserved,
                generatedOutputSha256 = expectedOutputSha256,
                reopenedGeneratedOutputSha256 = await HashFileAsync(reopenedOutputPath!).ConfigureAwait(true),
            };
            evidence["meshWorkflow"] = await RunMeshWorkflowAsync(
                kernel,
                stepFixture,
                outputDirectory).ConfigureAwait(true);
            evidence["status"] = "passed";
            await WriteEvidenceAsync(evidencePath, evidence).ConfigureAwait(true);
            return 0;
        }
        catch (Exception exception)
        {
            evidence["status"] = "failed";
            evidence["error"] = exception.ToString();
            await WriteEvidenceAsync(evidencePath, evidence).ConfigureAwait(true);
            return 1;
        }
    }

    private static async Task<object> RunMeshWorkflowAsync(
        IStepGeometryKernelService kernel,
        string stepFixture,
        string outputDirectory)
    {
        var objFixture = RequireAcceptanceFixture(
            "PATHSTITCH_MACOS_ACCEPTANCE_OBJ_FIXTURE",
            "OBJ");
        var stlFixture = RequireAcceptanceFixture(
            "PATHSTITCH_MACOS_ACCEPTANCE_STL_FIXTURE",
            "STL");
        var generatedCombinedPaths = new List<string>();
        try
        {
            var objFixtureEvidence = await PreserveInputFixtureAsync(
                objFixture,
                outputDirectory,
                "packaged-input.obj").ConfigureAwait(true);
            var stlFixtureEvidence = await PreserveInputFixtureAsync(
                stlFixture,
                outputDirectory,
                "packaged-input.stl").ConfigureAwait(true);
            var objImport = await kernel.ImportAsync(objFixture).ConfigureAwait(true);
            var repeatedObjImport = await kernel.ImportAsync(objFixture).ConfigureAwait(true);
            var stlImport = await kernel.ImportAsync(stlFixture).ConfigureAwait(true);
            ValidateTwoTriangleMeshImport(objImport, "OBJ");
            ValidateTwoTriangleMeshImport(stlImport, "STL");
            if (!repeatedObjImport.IsSuccess
                || repeatedObjImport.Document?.DocumentId != objImport.Document!.DocumentId)
            {
                throw new InvalidOperationException("Packaged OBJ import did not retain a stable document identity.");
            }

            var stepAndObj = await kernel.CombineAsync(stepFixture, objFixture).ConfigureAwait(true);
            if (!stepAndObj.IsSuccess
                || string.IsNullOrWhiteSpace(stepAndObj.OutputPath)
                || !File.Exists(stepAndObj.OutputPath)
                || stepAndObj.BodyCount != 3)
            {
                throw new InvalidOperationException($"Packaged STEP + OBJ combine failed: {stepAndObj.Message}");
            }
            generatedCombinedPaths.Add(stepAndObj.OutputPath);

            var allFormats = await kernel.CombineAsync(
                stepAndObj.OutputPath,
                stlFixture).ConfigureAwait(true);
            if (!allFormats.IsSuccess
                || string.IsNullOrWhiteSpace(allFormats.OutputPath)
                || !File.Exists(allFormats.OutputPath)
                || allFormats.BodyCount != 4)
            {
                throw new InvalidOperationException($"Packaged STEP + OBJ + STL combine failed: {allFormats.Message}");
            }
            generatedCombinedPaths.Add(allFormats.OutputPath);

            var combinedImport = await kernel.ImportAsync(allFormats.OutputPath).ConfigureAwait(true);
            if (!combinedImport.IsSuccess || combinedImport.Document?.Bodies.Count != 4)
            {
                throw new InvalidOperationException(
                    $"Packaged mixed 3D reimport did not preserve four bodies: {combinedImport.Message}");
            }
            var mixedPath = Path.Combine(outputDirectory, "packaged-mixed.step");
            File.Copy(allFormats.OutputPath, mixedPath, overwrite: true);
            if (new FileInfo(mixedPath).Length <= 100)
                throw new InvalidOperationException("Packaged mixed STEP evidence was empty or trivial.");

            var body = objImport.Document!.Bodies[0];
            var selectedFaces = body.Faces
                .Select((face, index) => new SelectedFace3D(0, index, body.Id, face.Id))
                .ToArray();
            var selectedFaceIds = body.Faces.Select(face => face.Id).ToArray();
            var sharedEdgeIndex = body.Edges
                .Select((edge, index) => new { Edge = edge, WorkerIndex = index + 1 })
                .Single(item => item.Edge.AdjacentFaceIds.Count == 2)
                .WorkerIndex;
            var forcedSeams = new[] { new EditorSeamEdge3D(0, sharedEdgeIndex) };

            var projection = await kernel.ProjectAsync(new EditorProjectionRequest(
                objFixture,
                PlaneType: "XY",
                Offset: 0,
                FaceIndex: null,
                FaceBodyIndex: null,
                VisibleBodyIndices: [0],
                BodyOffsets: [],
                VisibleBodyIds: [body.Id])).ConfigureAwait(true);
            var projectionEvidence = await PreserveOperationOutputAsync(
                projection,
                outputDirectory,
                "packaged-obj-projection.dxf",
                requiredMarker: null).ConfigureAwait(true);

            var connected = await kernel.UnfoldAsync(new EditorUnfoldRequest(
                objFixture,
                selectedFaces,
                [0],
                WholeBody: true,
                DistortionMode: "conformal",
                SelectedFaceIds: selectedFaceIds,
                VisibleBodyIds: [body.Id],
                NetLayout: "connected")).ConfigureAwait(true);
            var connectedEvidence = await PreserveOperationOutputAsync(
                connected,
                outputDirectory,
                "packaged-obj-connected.dxf",
                requiredMarker: "CREASE").ConfigureAwait(true);

            var separate = await kernel.UnfoldAsync(new EditorUnfoldRequest(
                objFixture,
                selectedFaces,
                [0],
                WholeBody: true,
                DistortionMode: "conformal",
                SelectedFaceIds: selectedFaceIds,
                VisibleBodyIds: [body.Id],
                NetLayout: "separate")).ConfigureAwait(true);
            var separateEvidence = await PreserveOperationOutputAsync(
                separate,
                outputDirectory,
                "packaged-obj-separate.dxf",
                requiredMarker: null).ConfigureAwait(true);

            var tabs = await kernel.UnfoldAsync(new EditorUnfoldRequest(
                objFixture,
                selectedFaces,
                [0],
                WholeBody: true,
                DistortionMode: "conformal",
                SelectedFaceIds: selectedFaceIds,
                VisibleBodyIds: [body.Id],
                SeamControlMode: "manual",
                ForcedSeams: forcedSeams,
                SeamDecoration: "tabs",
                NetLayout: "connected",
                TabHeight: 2)).ConfigureAwait(true);
            var tabsEvidence = await PreserveOperationOutputAsync(
                tabs,
                outputDirectory,
                "packaged-obj-tabs.dxf",
                requiredMarker: "GLUE_TABS").ConfigureAwait(true);

            var holes = await kernel.UnfoldAsync(new EditorUnfoldRequest(
                objFixture,
                selectedFaces,
                [0],
                WholeBody: true,
                DistortionMode: "conformal",
                SelectedFaceIds: selectedFaceIds,
                VisibleBodyIds: [body.Id],
                SeamControlMode: "manual",
                ForcedSeams: forcedSeams,
                SeamDecoration: "none",
                SeamDecorations:
                [
                    new EditorSeamDecoration3D(
                        new EditorSeamEdge3D(0, sharedEdgeIndex),
                        "holes"),
                ],
                NetLayout: "connected",
                HoleDiameter: 1,
                HoleSpacing: 3,
                HoleMargin: 1)).ConfigureAwait(true);
            var holesEvidence = await PreserveOperationOutputAsync(
                holes,
                outputDirectory,
                "packaged-obj-holes.dxf",
                requiredMarker: "SEW_HOLES").ConfigureAwait(true);

            return new
            {
                objFixture,
                objFixtureSha256 = await HashFileAsync(objFixture).ConfigureAwait(true),
                objFixtureEvidence,
                objImported = true,
                objDocumentId = objImport.Document.DocumentId,
                objBodyCount = objImport.Document.Bodies.Count,
                objFaceCount = body.Faces.Count,
                objEdgeCount = body.Edges.Count,
                objSharedEdgeIndex = sharedEdgeIndex,
                stlFixture,
                stlFixtureSha256 = await HashFileAsync(stlFixture).ConfigureAwait(true),
                stlFixtureEvidence,
                stlImported = true,
                stlBodyCount = stlImport.Document!.Bodies.Count,
                stlFaceCount = stlImport.Document.Bodies[0].Faces.Count,
                stlEdgeCount = stlImport.Document.Bodies[0].Edges.Count,
                mixedCombined = true,
                mixedBodyCount = combinedImport.Document.Bodies.Count,
                mixed = await DescribeFileAsync(mixedPath).ConfigureAwait(true),
                projected = true,
                projection = projectionEvidence,
                connectedUnfolded = true,
                connected = connectedEvidence,
                separateUnfolded = true,
                separate = separateEvidence,
                tabsDecorated = true,
                tabs = tabsEvidence,
                holesDecorated = true,
                holes = holesEvidence,
            };
        }
        finally
        {
            foreach (var generatedPath in generatedCombinedPaths)
            {
                try { File.Delete(generatedPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static string RequireAcceptanceFixture(string environmentVariable, string label)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException($"Packaged {label} acceptance fixture was not provided.");
        return Path.GetFullPath(path);
    }

    private static void ValidateTwoTriangleMeshImport(
        StepGeometryImportResult result,
        string label)
    {
        if (!result.IsSuccess
            || result.Document?.Bodies.Count != 1
            || result.Document.Bodies[0].Faces.Count != 2
            || result.Document.Bodies[0].Edges.Count != 5
            || result.Document.Bodies[0].Edges.Count(edge => edge.AdjacentFaceIds.Count == 2) != 1)
        {
            throw new InvalidOperationException(
                $"Packaged {label} import did not expose sewn two-face/five-edge topology: {result.Message}");
        }
    }

    private static async Task<object> PreserveOperationOutputAsync(
        EditorOperationResult result,
        string outputDirectory,
        string fileName,
        string? requiredMarker)
    {
        if (!result.IsSuccess
            || string.IsNullOrWhiteSpace(result.OutputPath)
            || !File.Exists(result.OutputPath))
        {
            throw new InvalidOperationException($"Packaged mesh operation failed: {result.Message}");
        }

        var destination = Path.Combine(outputDirectory, fileName);
        File.Copy(result.OutputPath, destination, overwrite: true);
        if (new FileInfo(destination).Length <= 100)
            throw new InvalidOperationException($"Packaged mesh output {fileName} was empty or trivial.");
        if (requiredMarker is not null
            && !(await File.ReadAllTextAsync(destination).ConfigureAwait(true))
                .Contains(requiredMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Packaged mesh output {fileName} did not contain required layer {requiredMarker}.");
        }

        return await DescribeDxfFileAsync(destination).ConfigureAwait(true);
    }

    private static DxfUnitEvidence ValidateCanonicalMillimeterDxf(string path)
    {
        var metadata = EditorDxfDocument.ReadUnitMetadata(path);
        var lines = File.ReadAllLines(path);
        var insUnitsValues = new List<int>();
        var measurementValues = new List<int>();
        var inHeader = false;

        for (var index = 0; index + 1 < lines.Length; index += 2)
        {
            var code = lines[index].Trim();
            var value = lines[index + 1].Trim();
            if (code == "0" && string.Equals(value, "SECTION", StringComparison.OrdinalIgnoreCase))
            {
                inHeader = index + 3 < lines.Length
                    && lines[index + 2].Trim() == "2"
                    && string.Equals(lines[index + 3].Trim(), "HEADER", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (code == "0" && string.Equals(value, "ENDSEC", StringComparison.OrdinalIgnoreCase))
            {
                inHeader = false;
                continue;
            }
            if (!inHeader || code != "9" || index + 3 >= lines.Length || lines[index + 2].Trim() != "70")
                continue;
            if (!int.TryParse(
                    lines[index + 3].Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var integerValue))
            {
                continue;
            }

            if (string.Equals(value, "$INSUNITS", StringComparison.OrdinalIgnoreCase))
                insUnitsValues.Add(integerValue);
            else if (string.Equals(value, "$MEASUREMENT", StringComparison.OrdinalIgnoreCase))
                measurementValues.Add(integerValue);
        }

        if (metadata.InsUnitsCode != EditorLengthUnits.MillimeterInsUnitsCode
            || metadata.MillimetersPerDrawingUnit != 1.0
            || insUnitsValues.Count != 1
            || insUnitsValues[0] != EditorLengthUnits.MillimeterInsUnitsCode)
        {
            throw new InvalidOperationException(
                $"Packaged DXF {Path.GetFileName(path)} must contain exactly one HEADER $INSUNITS=4 declaration.");
        }
        if (measurementValues.Count != 1
            || measurementValues[0] != EditorLengthUnits.MetricMeasurementCode)
        {
            throw new InvalidOperationException(
                $"Packaged DXF {Path.GetFileName(path)} must contain exactly one HEADER $MEASUREMENT=1 declaration.");
        }

        return new DxfUnitEvidence(insUnitsValues[0], measurementValues[0]);
    }

    private static async Task<object> PreserveInputFixtureAsync(
        string source,
        string outputDirectory,
        string fileName)
    {
        var destination = Path.Combine(outputDirectory, fileName);
        File.Copy(source, destination, overwrite: true);
        return await DescribeFileAsync(destination).ConfigureAwait(true);
    }

    private static async Task<object> DescribeFileAsync(string path)
        => new
        {
            path,
            bytes = new FileInfo(path).Length,
            sha256 = await HashFileAsync(path).ConfigureAwait(true),
        };

    private static async Task<object> DescribeDxfFileAsync(string path)
    {
        var units = ValidateCanonicalMillimeterDxf(path);
        return new
        {
            path,
            bytes = new FileInfo(path).Length,
            sha256 = await HashFileAsync(path).ConfigureAwait(true),
            insUnitsCode = units.InsUnitsCode,
            measurementCode = units.MeasurementCode,
        };
    }

    private static async Task<string> HashFileAsync(string path)
        => Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path).ConfigureAwait(true)))
            .ToLowerInvariant();

    private sealed record DxfUnitEvidence(int InsUnitsCode, int MeasurementCode);

    private static Task WriteEvidenceAsync(string path, Dictionary<string, object?> evidence)
        => File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(
                evidence,
                new JsonSerializerOptions { WriteIndented = true }));
}
