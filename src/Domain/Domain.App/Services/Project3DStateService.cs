using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Domain.App.Models;

namespace Domain.App.Services;

public sealed class Project3DStateService(IEditorOutputPreviewService? outputPreviewService = null)
{
    private static readonly DateTimeOffset AppleReferenceDate = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string GetEditableGeneratedOutputPath(string projectFilePath)
        => BuildEditableGeneratedOutputPath(projectFilePath);

    public async Task<Project3DState> LoadAsync(string projectFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
            return Project3DState.Empty;

        try
        {
            var payload = await ReadProjectPayloadAsync(projectFilePath, cancellationToken).ConfigureAwait(false);
            if (payload is null)
                return Project3DState.Empty;

            var bodies = (payload.SavedBodies3D ?? [])
                .Select(body => body with
                {
                    Visible = body.Visible,
                    Faces = body.Faces.Select(face => face with { BodyIndex = body.BodyIndex }).ToArray(),
                })
                .ToArray();
            var offsets = payload.SavedBodyOffsets?
                .Select(x => new BodyOffset3D(x.BodyIndex, x.X, x.Y, x.Z))
                .ToArray() ?? [];
            var generatedOutputPath = await TryExtractGeneratedOutputAsync(payload, projectFilePath, cancellationToken)
                .ConfigureAwait(false);
            var twoDWorkspaceState = payload.SavedTwoDWorkspaceState;
            if (twoDWorkspaceState is null)
            {
                var legacyDocument = generatedOutputPath is not null && outputPreviewService is not null
                    ? await outputPreviewService.LoadPreviewDocumentAsync(generatedOutputPath, cancellationToken).ConfigureAwait(false)
                    : null;
                twoDWorkspaceState = ConvertLegacyTwoDWorkspace(payload, legacyDocument);
            }

            return new Project3DState(
                payload.SavedViewportJson ?? payload.SavedStepJson,
                bodies,
                offsets,
                payload.SourceModelPath,
                generatedOutputPath,
                payload.DxfDataBase64,
                payload.SavedGeneratedOutputContext,
                payload.SavedUnfoldWorkspaceState,
                payload.SavedProjectionWorkspaceState,
                payload.SavedEditorWorkspaceState,
                twoDWorkspaceState,
                payload.SavedThreeDWorkspaceState,
                payload.SavedStepTopology,
                payload.SavedBatchWorkspaceState ?? ConvertLegacyBatchWorkspace(payload.BatchItems),
                payload.SavedActivityLog ?? ConvertLegacyActivityLog(payload.LogEntries),
                payload.SavedLearnModeEnabled ?? payload.IsLearnModeEnabled ?? true,
                LegacyExportMeasurementLines: payload.ExportMeasurementLines);
        }
        catch
        {
            return Project3DState.Empty;
        }
    }

    public Task SaveAsync(string projectFilePath, Project3DState state, CancellationToken cancellationToken = default)
        => SaveCoreAsync(projectFilePath, state, projectNameOverride: null, cancellationToken);

    private async Task SaveCoreAsync(
        string projectFilePath,
        Project3DState state,
        string? projectNameOverride,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
            return;

        var preserveExistingSourceModel = string.IsNullOrWhiteSpace(state.SourceModelPath) || !File.Exists(state.SourceModelPath);
        var payload = await ReadProjectPayloadNodeAsync(projectFilePath, cancellationToken).ConfigureAwait(false)
            ?? new JsonObject();
        var preservedEntries = await ReadPreservedArchiveEntriesAsync(projectFilePath, preserveExistingSourceModel, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(projectNameOverride))
            payload["projectName"] = projectNameOverride.Trim();

        payload["savedViewportJson"] = state.ViewportJson is null
            ? null
            : JsonValue.Create(state.ViewportJson);
        payload["savedStepJson"] = state.ViewportJson is null
            ? null
            : JsonValue.Create(state.ViewportJson);
        payload["savedBodies3D"] = state.Bodies.Count == 0
            ? null
            : JsonSerializer.SerializeToNode(state.Bodies, SerializerOptions);
        payload["savedBodyOffsets"] = state.BodyOffsets.Count == 0
            ? null
            : JsonSerializer.SerializeToNode(
                state.BodyOffsets.Select(x => new BodyOffsetPayload(x.BodyIndex, x.X, x.Y, x.Z)),
                SerializerOptions);
        payload["savedGeneratedOutputContext"] = state.GeneratedOutputContext is null
            ? null
            : JsonSerializer.SerializeToNode(state.GeneratedOutputContext, SerializerOptions);
        payload["savedUnfoldWorkspaceState"] = state.UnfoldWorkspaceState is null
            ? null
            : JsonSerializer.SerializeToNode(state.UnfoldWorkspaceState, SerializerOptions);
        payload["savedProjectionWorkspaceState"] = state.ProjectionWorkspaceState is null
            ? null
            : JsonSerializer.SerializeToNode(state.ProjectionWorkspaceState, SerializerOptions);
        payload["savedEditorWorkspaceState"] = state.WorkspaceState is null
            ? null
            : JsonSerializer.SerializeToNode(state.WorkspaceState, SerializerOptions);
        payload["savedTwoDWorkspaceState"] = state.TwoDWorkspaceState is null
            ? null
            : JsonSerializer.SerializeToNode(state.TwoDWorkspaceState, SerializerOptions);
        payload["savedThreeDWorkspaceState"] = state.ThreeDWorkspaceState is null
            ? null
            : JsonSerializer.SerializeToNode(state.ThreeDWorkspaceState, SerializerOptions);
        payload["savedBatchWorkspaceState"] = state.BatchWorkspaceState is null
            ? null
            : JsonSerializer.SerializeToNode(state.BatchWorkspaceState, SerializerOptions);
        payload["savedStepTopology"] = state.StepTopology is null
            ? null
            : JsonSerializer.SerializeToNode(state.StepTopology, SerializerOptions);
        payload["savedActivityLog"] = state.ActivityLog is not { Count: > 0 }
            ? null
            : JsonSerializer.SerializeToNode(state.ActivityLog, SerializerOptions);
        payload["savedLearnModeEnabled"] = state.LearnModeEnabled;
        var generatedOutputDataBase64 = await TryReadGeneratedOutputBase64Async(state.GeneratedOutputPath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(generatedOutputDataBase64))
        {
            payload["dxfDataBase64"] = generatedOutputDataBase64;
        }
        else if (!state.HasGeneratedOutput)
        {
            payload["dxfDataBase64"] = null;
        }
        else if (!string.IsNullOrWhiteSpace(state.GeneratedOutputDataBase64))
        {
            payload["dxfDataBase64"] = state.GeneratedOutputDataBase64;
        }

        var jsonData = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (!string.IsNullOrWhiteSpace(projectDirectory))
            Directory.CreateDirectory(projectDirectory);

        var tempArchivePath = Path.Combine(
            projectDirectory ?? Path.GetTempPath(),
            $"{Path.GetFileName(projectFilePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using var fileStream = File.Create(tempArchivePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

            var projectEntry = archive.CreateEntry("project.json", CompressionLevel.Optimal);
            await using (var projectEntryStream = projectEntry.Open())
            {
                await projectEntryStream.WriteAsync(jsonData, cancellationToken).ConfigureAwait(false);
            }

            foreach (var preservedEntry in preservedEntries)
            {
                var entry = archive.CreateEntry(preservedEntry.FullName, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(preservedEntry.Content, cancellationToken).ConfigureAwait(false);
            }

            if (IsValidPreviewImage(state.PreviewImageData))
            {
                var previewEntry = archive.CreateEntry("preview.png", CompressionLevel.Optimal);
                await using var previewStream = previewEntry.Open();
                await previewStream.WriteAsync(state.PreviewImageData, cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(state.SourceModelPath) && File.Exists(state.SourceModelPath))
            {
                var extension = Path.GetExtension(state.SourceModelPath);
                var sourceEntry = archive.CreateEntry($"active{extension}", CompressionLevel.NoCompression);
                await using var sourceFileStream = File.OpenRead(state.SourceModelPath);
                await using var sourceEntryStream = sourceEntry.Open();
                await sourceFileStream.CopyToAsync(sourceEntryStream, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            TryDeleteFile(tempArchivePath);
            throw;
        }

        try
        {
            File.Move(tempArchivePath, projectFilePath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempArchivePath);
        }
    }

    public async Task SaveAsAsync(
        string sourceProjectFilePath,
        string targetProjectFilePath,
        Project3DState state,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceProjectFilePath))
            throw new ArgumentException("A source project path is required.", nameof(sourceProjectFilePath));
        if (string.IsNullOrWhiteSpace(targetProjectFilePath))
            throw new ArgumentException("A target project path is required.", nameof(targetProjectFilePath));

        var sourcePath = Path.GetFullPath(sourceProjectFilePath);
        var targetPath = Path.GetFullPath(targetProjectFilePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The source project file does not exist.", sourcePath);
        if (!Path.GetExtension(targetPath).Equals(".stch", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The target project path must use the .stch extension.", nameof(targetProjectFilePath));

        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            await SaveCoreAsync(
                targetPath,
                state,
                Path.GetFileNameWithoutExtension(targetPath),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        var stagingPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileNameWithoutExtension(targetPath)}.{Guid.NewGuid():N}.saveas.stch");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(sourcePath, stagingPath, overwrite: false);
            await SaveCoreAsync(
                stagingPath,
                state,
                Path.GetFileNameWithoutExtension(targetPath),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stagingPath, targetPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(stagingPath);
        }
    }

    private static async Task<Project3DStatePayload?> ReadProjectPayloadAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(projectFilePath);

        if (extension.Equals(".stch", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var fileStream = File.OpenRead(projectFilePath);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
                var entry = archive.GetEntry("project.json");
                var sourceModelPath = await TryExtractSourceModelAsync(archive, projectFilePath, cancellationToken).ConfigureAwait(false);
                if (entry is not null)
                {
                    await using var entryStream = entry.Open();
                    var payload = await JsonSerializer.DeserializeAsync<Project3DStatePayload>(entryStream, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false);
                    return payload is null ? null : payload with { SourceModelPath = sourceModelPath };
                }
            }
            catch (InvalidDataException)
            {
                // Historic and newly seeded projects may store JSON directly in the .stch file.
            }
        }

        await using var jsonStream = File.OpenRead(projectFilePath);
        return await JsonSerializer.DeserializeAsync<Project3DStatePayload>(jsonStream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<JsonObject?> ReadProjectPayloadNodeAsync(string projectFilePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
            return null;

        var extension = Path.GetExtension(projectFilePath);
        if (extension.Equals(".stch", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await using var fileStream = File.OpenRead(projectFilePath);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
                var entry = archive.GetEntry("project.json");
                if (entry is not null)
                {
                    await using var entryStream = entry.Open();
                    return await JsonNode.ParseAsync(entryStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
                }
            }
            catch (InvalidDataException)
            {
                // The project may still be the initial plain JSON seed written by the Avalonia home page.
            }
        }

        await using var jsonStream = File.OpenRead(projectFilePath);
        return await JsonNode.ParseAsync(jsonStream, cancellationToken: cancellationToken).ConfigureAwait(false) as JsonObject;
    }

    private static async Task<IReadOnlyList<PreservedArchiveEntry>> ReadPreservedArchiveEntriesAsync(
        string projectFilePath,
        bool preserveExistingSourceModel,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath)
            || !File.Exists(projectFilePath)
            || !Path.GetExtension(projectFilePath).Equals(".stch", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            var preservedEntries = new List<PreservedArchiveEntry>();
            await using var fileStream = File.OpenRead(projectFilePath);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

            foreach (var entry in archive.Entries)
            {
                if (string.Equals(entry.FullName, "project.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(entry.FullName, "preview.png", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!preserveExistingSourceModel && IsSourceModelEntry(entry.FullName))
                    continue;

                await using var entryStream = entry.Open();
                using var memoryStream = new MemoryStream();
                await entryStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
                preservedEntries.Add(new PreservedArchiveEntry(entry.FullName, memoryStream.ToArray()));
            }

            return preservedEntries;
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }

    private static bool IsValidPreviewImage(byte[]? data)
        => data is { Length: > 8 and <= 4 * 1024 * 1024 }
            && data[0] == 0x89
            && data[1] == 0x50
            && data[2] == 0x4E
            && data[3] == 0x47
            && data[4] == 0x0D
            && data[5] == 0x0A
            && data[6] == 0x1A
            && data[7] == 0x0A;

    private static async Task<string?> TryExtractSourceModelAsync(ZipArchive archive, string projectFilePath, CancellationToken cancellationToken)
    {
        var sourceEntry = archive.Entries.FirstOrDefault(entry =>
        {
            var fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            return IsSourceModelEntry(fileName);
        });

        if (sourceEntry is null)
            return null;

        var extractionRoot = Path.Combine(
            Path.GetTempPath(),
            "Pathstitch-CrossPort",
            "Recovered3DAssets",
            GetProjectCacheFolderName(projectFilePath));

        Directory.CreateDirectory(extractionRoot);
        var targetPath = Path.Combine(extractionRoot, Path.GetFileName(sourceEntry.FullName));

        await using var entryStream = sourceEntry.Open();
        await using var fileStream = File.Create(targetPath);
        await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }

    private static async Task<string?> TryExtractGeneratedOutputAsync(
        Project3DStatePayload payload,
        string projectFilePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.DxfDataBase64))
            return null;

        try
        {
            var dxfData = Convert.FromBase64String(payload.DxfDataBase64);
            var targetPath = BuildEditableGeneratedOutputPath(projectFilePath);
            await File.WriteAllBytesAsync(targetPath, dxfData, cancellationToken).ConfigureAwait(false);
            return targetPath;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<EditorActivityEntry> ConvertLegacyActivityLog(
        IReadOnlyList<LegacyActivityEntryPayload>? entries)
    {
        if (entries is null || entries.Count == 0)
            return [];

        var converted = new List<EditorActivityEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id)
                || entry.Action is null
                || entry.Details is null
                || !TryParseLegacyActivityTimestamp(entry.Timestamp, out var timestamp))
            {
                continue;
            }

            converted.Add(new EditorActivityEntry(
                entry.Id,
                timestamp,
                entry.Action,
                entry.Details,
                entry.LayerAffected));
        }

        return converted;
    }

    private static EditorBatchWorkspaceState? ConvertLegacyBatchWorkspace(
        IReadOnlyList<LegacyBatchItemPayload>? entries)
    {
        if (entries is null)
            return null;

        var items = new List<EditorBatchItemState>(entries.Count);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.OriginalName)
                || string.IsNullOrWhiteSpace(entry.DxfDataBase64))
            {
                continue;
            }

            string fileName;
            try
            {
                fileName = Path.GetFileName(entry.OriginalName);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                _ = Convert.FromBase64String(entry.DxfDataBase64);
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            {
                continue;
            }

            items.Add(new EditorBatchItemState(
                fileName,
                SourceDataBase64: entry.DxfDataBase64,
                IsSelected: entry.IsSelected ?? true,
                SourceFileExtension: ".dxf"));
        }

        return new EditorBatchWorkspaceState(items);
    }

    private static Editor2DWorkspaceState? ConvertLegacyTwoDWorkspace(
        Project3DStatePayload payload,
        Editor2DPreviewDocument? document)
    {
        if (string.IsNullOrWhiteSpace(payload.DxfDataBase64)
            && string.IsNullOrWhiteSpace(payload.RefImageBase64)
            && payload.SavedLayers is not { Count: > 0 }
            && payload.Measurements is not { Count: > 0 }
            && payload.CanvasScale == 0.0
            && payload.CanvasOffsetX == 0.0
            && payload.CanvasOffsetY == 0.0)
            return null;

        var workspaceDocument = document ?? Editor2DWorkspaceState.Empty.Document;
        var layers = new List<Editor2DLayer>();
        foreach (var legacyLayer in payload.SavedLayers ?? [])
        {
            if (string.IsNullOrWhiteSpace(legacyLayer.Id))
                continue;

            Editor2DReferenceImage? referenceImage = null;
            if (legacyLayer.IsReferenceImageLayer)
                referenceImage = ConvertLegacyReferenceImage(legacyLayer);
            if (legacyLayer.IsReferenceImageLayer && referenceImage is null)
                continue;

            layers.Add(new Editor2DLayer(
                legacyLayer.Id,
                string.IsNullOrWhiteSpace(legacyLayer.Name) ? $"Layer {layers.Count + 1}" : legacyLayer.Name,
                legacyLayer.IsReferenceImageLayer
                    ? []
                    : workspaceDocument.Paths
                        .Where(path => string.Equals(path.SourceLayerName, legacyLayer.Name, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(path.SourceLayerName, legacyLayer.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(path => path.Id)
                        .ToArray(),
                IsVisible: legacyLayer.Visible,
                IsLocked: legacyLayer.Locked,
                Order: layers.Count,
                Kind: legacyLayer.IsReferenceImageLayer ? Editor2DLayerKind.ReferenceImage : Editor2DLayerKind.Geometry,
                ReferenceImage: referenceImage,
                ColorHex: legacyLayer.ColorHex ?? "#4D7FFF",
                ParentFolderId: legacyLayer.ParentFolderId));
        }

        if (!layers.Any(layer => layer.IsReferenceImage) && !string.IsNullOrWhiteSpace(payload.RefImageBase64))
        {
            var topLevelReference = ConvertLegacyReferenceImage(new LegacyLayerPayload(
                "legacy-reference-image",
                "Reference image",
                "#4D7FFF",
                true,
                null,
                true,
                payload.RefImageBase64,
                payload.RefImageOffsetX,
                payload.RefImageOffsetY,
                payload.RefImageScale,
                payload.RefImageScale,
                0.0,
                0.0,
                0.0,
                "back",
                payload.RefImageOpacity,
                false));
            if (topLevelReference is not null)
            {
                layers.Add(new Editor2DLayer(
                    topLevelReference.Id,
                    topLevelReference.FileName,
                    [],
                    Order: layers.Count,
                    Kind: Editor2DLayerKind.ReferenceImage,
                    ReferenceImage: topLevelReference));
            }
        }

        if (!layers.Any(layer => layer.Kind == Editor2DLayerKind.Geometry))
            layers.Insert(0, new Editor2DLayer("layer-1", "Layer 1", [], Order: 0));
        var assignedPathIds = layers.SelectMany(layer => layer.PathIds).ToHashSet(StringComparer.Ordinal);
        var unassignedPathIds = workspaceDocument.Paths.Select(path => path.Id).Where(id => !assignedPathIds.Contains(id)).ToArray();
        if (unassignedPathIds.Length > 0)
        {
            var targetIndex = layers.FindIndex(layer => layer.Kind == Editor2DLayerKind.Geometry
                && string.Equals(layer.Id, payload.SavedActiveLayerId, StringComparison.Ordinal));
            if (targetIndex < 0)
                targetIndex = layers.FindIndex(layer => layer.Kind == Editor2DLayerKind.Geometry);
            layers[targetIndex] = layers[targetIndex] with { PathIds = layers[targetIndex].PathIds.Concat(unassignedPathIds).ToArray() };
        }
        layers = layers.Select((layer, order) => layer with { Order = order }).ToList();

        var pathIdsByHandle = workspaceDocument.Paths
            .Where(path => !string.IsNullOrWhiteSpace(path.SourceEntityHandle))
            .GroupBy(path => path.SourceEntityHandle!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
        var measurements = (payload.Measurements ?? [])
            .Where(item => item.Start is not null && item.End is not null
                && item.Start.IsFinite && item.End.IsFinite)
            .Select(item => new Editor2DMeasurement(
                string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                new Editor2DPoint(item.Start!.X, item.Start.Y),
                new Editor2DPoint(item.End!.X, item.End.Y),
                item.IsAutoDimension,
                item.EntityHandle is not null && pathIdsByHandle.TryGetValue(item.EntityHandle, out var pathId) ? pathId : null,
                item.DimensionType,
                item.RectP1 is { IsFinite: true } rectP1 ? new Editor2DPoint(rectP1.X, rectP1.Y) : null,
                item.RectP2 is { IsFinite: true } rectP2 ? new Editor2DPoint(rectP2.X, rectP2.Y) : null,
                item.FilletRadius,
                item.OffsetDistance,
                VarName: item.VarName,
                Expression: item.Expression,
                Driven: item.Driven,
                IsParametric: item.IsParametric,
                EvaluatedValue: double.IsFinite(item.DistanceMm) && item.DistanceMm > 0 ? item.DistanceMm : null))
            .ToArray();
        var zoom = double.IsFinite(payload.CanvasScale) && payload.CanvasScale > 0.0
            ? payload.CanvasScale
            : 0.0;
        return new Editor2DWorkspaceState(
            workspaceDocument,
            ViewportZoom: zoom,
            ViewportOffsetX: double.IsFinite(payload.CanvasOffsetX) ? payload.CanvasOffsetX : 0.0,
            ViewportOffsetY: double.IsFinite(payload.CanvasOffsetY) ? payload.CanvasOffsetY : 0.0,
            IsInitialized: true,
            Layers: layers,
            ActiveLayerId: layers.Any(layer => layer.Id == payload.SavedActiveLayerId)
                ? payload.SavedActiveLayerId
                : layers.First(layer => layer.Kind == Editor2DLayerKind.Geometry).Id,
            Measurements: measurements,
            Folders: (payload.SavedLayerFolders ?? [])
                .Where(folder => !string.IsNullOrWhiteSpace(folder.Id))
                .Select(folder => new Editor2DLayerFolder(folder.Id, folder.Name ?? "Folder", folder.ParentFolderId))
                .ToArray());
    }

    private static Editor2DReferenceImage? ConvertLegacyReferenceImage(LegacyLayerPayload layer)
    {
        if (string.IsNullOrWhiteSpace(layer.RefImageBase64))
            return null;
        try
        {
            var imageData = Convert.FromBase64String(layer.RefImageBase64);
            var hasMetadata = Editor2DReferenceImageMetadata.TryReadPixelSize(imageData, out var metadataWidth, out var metadataHeight);
            var pixelWidth = layer.RefImagePixelWidth > 0 ? (int)Math.Round(layer.RefImagePixelWidth) : hasMetadata ? metadataWidth : 0;
            var pixelHeight = layer.RefImagePixelHeight > 0 ? (int)Math.Round(layer.RefImagePixelHeight) : hasMetadata ? metadataHeight : 0;
            if (pixelWidth <= 0 || pixelHeight <= 0)
                return null;
            var baseWidth = layer.RefImageWidth > 0 ? layer.RefImageWidth : pixelWidth;
            var baseHeight = layer.RefImageHeight > 0 ? layer.RefImageHeight : pixelHeight;
            var scaleX = double.IsFinite(layer.RefImageScaleX) && layer.RefImageScaleX > 0 ? layer.RefImageScaleX : 1.0;
            var scaleY = double.IsFinite(layer.RefImageScaleY) && layer.RefImageScaleY > 0 ? layer.RefImageScaleY : 1.0;
            var width = baseWidth * scaleX;
            var height = baseHeight * scaleY;
            return new Editor2DReferenceImage(
                layer.Id,
                string.IsNullOrWhiteSpace(layer.Name) ? "Reference image" : layer.Name,
                layer.RefImageBase64,
                pixelWidth,
                pixelHeight,
                double.IsFinite(layer.RefImageOffsetX) ? layer.RefImageOffsetX : 0.0,
                double.IsFinite(layer.RefImageOffsetY) ? layer.RefImageOffsetY : 0.0,
                width,
                height,
                double.IsFinite(layer.RefImageRotation) ? layer.RefImageRotation : 0.0,
                double.IsFinite(layer.RefImageOpacity) ? Math.Clamp(layer.RefImageOpacity, 0.0, 1.0) : 0.5,
                Math.Abs(width / pixelWidth),
                OriginalDataBase64: layer.RefImageOriginalBase64,
                BackgroundRemoved: layer.BackgroundRemoved,
                Depth: string.Equals(layer.RefImageDepth, "front", StringComparison.OrdinalIgnoreCase)
                    ? Editor2DReferenceImageDepth.Front
                    : Editor2DReferenceImageDepth.Back);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool TryParseLegacyActivityTimestamp(JsonElement value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        try
        {
            if (value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var seconds)
                && double.IsFinite(seconds))
            {
                timestamp = AppleReferenceDate.AddSeconds(seconds);
                return true;
            }

            return value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(
                    value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out timestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string BuildEditableGeneratedOutputPath(string projectFilePath)
    {
        var cacheFolderName = GetProjectCacheFolderName(projectFilePath);
        var extractionRoot = Path.Combine(
            Path.GetTempPath(),
            "Pathstitch-CrossPort",
            "Recovered2DOutputs",
            cacheFolderName);

        Directory.CreateDirectory(extractionRoot);
        return Path.Combine(extractionRoot, $"{cacheFolderName}-generated-output.dxf");
    }

    private static async Task<string?> TryReadGeneratedOutputBase64Async(string? generatedOutputPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(generatedOutputPath) || !File.Exists(generatedOutputPath))
            return null;

        var dxfData = await File.ReadAllBytesAsync(generatedOutputPath, cancellationToken).ConfigureAwait(false);
        return Convert.ToBase64String(dxfData);
    }

    private static bool IsSourceModelEntry(string entryName)
    {
        var fileName = Path.GetFileName(entryName);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.StartsWith("active.", StringComparison.OrdinalIgnoreCase))
            return false;

        var extension = Path.GetExtension(fileName);
        return extension.Equals(".obj", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".stl", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".step", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".stp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProjectCacheFolderName(string projectFilePath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        var fullPath = Path.GetFullPath(projectFilePath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return $"{projectName}-{hash[..12]}";
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the caller will surface the actual write failure if replacement fails.
        }
    }

    private sealed record Project3DStatePayload(
        [property: JsonPropertyName("dxfDataBase64")] string? DxfDataBase64,
        [property: JsonPropertyName("savedViewportJson")] string? SavedViewportJson,
        [property: JsonPropertyName("savedStepJson")] string? SavedStepJson,
        [property: JsonPropertyName("savedBodies3D")] IReadOnlyList<Body3D>? SavedBodies3D,
        [property: JsonPropertyName("savedBodyOffsets")] IReadOnlyList<BodyOffsetPayload>? SavedBodyOffsets,
        [property: JsonPropertyName("savedGeneratedOutputContext")] EditorGeneratedOutputContext? SavedGeneratedOutputContext,
        [property: JsonPropertyName("savedUnfoldWorkspaceState")] EditorUnfoldWorkspaceState? SavedUnfoldWorkspaceState,
        [property: JsonPropertyName("savedProjectionWorkspaceState")] EditorProjectionWorkspaceState? SavedProjectionWorkspaceState,
        [property: JsonPropertyName("savedEditorWorkspaceState")] EditorWorkspaceState? SavedEditorWorkspaceState,
        [property: JsonPropertyName("savedTwoDWorkspaceState")] Editor2DWorkspaceState? SavedTwoDWorkspaceState,
        [property: JsonPropertyName("savedThreeDWorkspaceState")] Editor3DWorkspaceState? SavedThreeDWorkspaceState,
        [property: JsonPropertyName("savedBatchWorkspaceState")] EditorBatchWorkspaceState? SavedBatchWorkspaceState,
        [property: JsonPropertyName("batchItems")] IReadOnlyList<LegacyBatchItemPayload>? BatchItems,
        [property: JsonPropertyName("savedStepTopology")] StepGeometryDocument? SavedStepTopology,
        [property: JsonPropertyName("savedActivityLog")] IReadOnlyList<EditorActivityEntry>? SavedActivityLog,
        [property: JsonPropertyName("logEntries")] IReadOnlyList<LegacyActivityEntryPayload>? LogEntries,
        [property: JsonPropertyName("savedLearnModeEnabled")] bool? SavedLearnModeEnabled,
        [property: JsonPropertyName("isLearnModeEnabled")] bool? IsLearnModeEnabled,
        [property: JsonPropertyName("exportMeasurementLines")] bool? ExportMeasurementLines,
        [property: JsonPropertyName("measurements")] IReadOnlyList<LegacyMeasurementPayload>? Measurements = null,
        [property: JsonPropertyName("savedLayers")] IReadOnlyList<LegacyLayerPayload>? SavedLayers = null,
        [property: JsonPropertyName("savedLayerFolders")] IReadOnlyList<LegacyLayerFolderPayload>? SavedLayerFolders = null,
        [property: JsonPropertyName("savedActiveLayerId")] string? SavedActiveLayerId = null,
        [property: JsonPropertyName("canvasScale")] double CanvasScale = 0.0,
        [property: JsonPropertyName("canvasOffsetX")] double CanvasOffsetX = 0.0,
        [property: JsonPropertyName("canvasOffsetY")] double CanvasOffsetY = 0.0,
        [property: JsonPropertyName("refImageBase64")] string? RefImageBase64 = null,
        [property: JsonPropertyName("refImageOffsetX")] double RefImageOffsetX = 0.0,
        [property: JsonPropertyName("refImageOffsetY")] double RefImageOffsetY = 0.0,
        [property: JsonPropertyName("refImageScale")] double RefImageScale = 1.0,
        [property: JsonPropertyName("refImageOpacity")] double RefImageOpacity = 0.5,
        [property: JsonPropertyName("refImageCalibrationDistance")] double RefImageCalibrationDistance = 100.0,
        [property: JsonPropertyName("refImageCalibrationStartX")] double? RefImageCalibrationStartX = null,
        [property: JsonPropertyName("refImageCalibrationStartY")] double? RefImageCalibrationStartY = null,
        [property: JsonPropertyName("refImageCalibrationEndX")] double? RefImageCalibrationEndX = null,
        [property: JsonPropertyName("refImageCalibrationEndY")] double? RefImageCalibrationEndY = null,
        string? SourceModelPath = null);

    private sealed record LegacyLayerPayload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("colorHex")] string? ColorHex,
        [property: JsonPropertyName("visible")] bool Visible = true,
        [property: JsonPropertyName("parentFolderId")] string? ParentFolderId = null,
        [property: JsonPropertyName("isReferenceImageLayer")] bool IsReferenceImageLayer = false,
        [property: JsonPropertyName("refImageBase64")] string? RefImageBase64 = null,
        [property: JsonPropertyName("refImageOffsetX")] double RefImageOffsetX = 0.0,
        [property: JsonPropertyName("refImageOffsetY")] double RefImageOffsetY = 0.0,
        [property: JsonPropertyName("refImageScaleX")] double RefImageScaleX = 1.0,
        [property: JsonPropertyName("refImageScaleY")] double RefImageScaleY = 1.0,
        [property: JsonPropertyName("refImageWidth")] double RefImageWidth = 0.0,
        [property: JsonPropertyName("refImageHeight")] double RefImageHeight = 0.0,
        [property: JsonPropertyName("refImageRotation")] double RefImageRotation = 0.0,
        [property: JsonPropertyName("refImageDepth")] string? RefImageDepth = "back",
        [property: JsonPropertyName("refImageOpacity")] double RefImageOpacity = 0.5,
        [property: JsonPropertyName("locked")] bool Locked = false,
        [property: JsonPropertyName("refImagePixelWidth")] double RefImagePixelWidth = 0.0,
        [property: JsonPropertyName("refImagePixelHeight")] double RefImagePixelHeight = 0.0,
        [property: JsonPropertyName("refImageOriginalBase64")] string? RefImageOriginalBase64 = null,
        [property: JsonPropertyName("backgroundRemoved")] bool BackgroundRemoved = false);

    private sealed record LegacyLayerFolderPayload(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("parentFolderId")] string? ParentFolderId = null);

    private sealed record LegacyPointPayload(
        [property: JsonPropertyName("x")] double X,
        [property: JsonPropertyName("y")] double Y)
    {
        public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    }

    private sealed record LegacyMeasurementPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("start")] LegacyPointPayload? Start,
        [property: JsonPropertyName("end")] LegacyPointPayload? End,
        [property: JsonPropertyName("distanceMm")] double DistanceMm,
        [property: JsonPropertyName("isAutoDimension")] bool IsAutoDimension,
        [property: JsonPropertyName("entityHandle")] string? EntityHandle = null,
        [property: JsonPropertyName("dimensionType")] string? DimensionType = null,
        [property: JsonPropertyName("rectP1")] LegacyPointPayload? RectP1 = null,
        [property: JsonPropertyName("rectP2")] LegacyPointPayload? RectP2 = null,
        [property: JsonPropertyName("filletRadius")] double FilletRadius = 0.0,
        [property: JsonPropertyName("varName")] string? VarName = null,
        [property: JsonPropertyName("expression")] string? Expression = null,
        [property: JsonPropertyName("driven")] bool Driven = false,
        [property: JsonPropertyName("isParametric")] bool IsParametric = false,
        [property: JsonPropertyName("offsetDistance")] double OffsetDistance = 0.0);

    private sealed record LegacyActivityEntryPayload(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("timestamp")] JsonElement Timestamp,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("details")] string? Details,
        [property: JsonPropertyName("layerAffected")] string? LayerAffected);

    private sealed record LegacyBatchItemPayload(
        [property: JsonPropertyName("originalName")] string? OriginalName,
        [property: JsonPropertyName("dxfDataBase64")] string? DxfDataBase64,
        [property: JsonPropertyName("isSelected")] bool? IsSelected);

    private sealed record BodyOffsetPayload(
        [property: JsonPropertyName("bodyIndex")] int BodyIndex,
        [property: JsonPropertyName("x")] double X,
        [property: JsonPropertyName("y")] double Y,
        [property: JsonPropertyName("z")] double Z);

    private sealed record PreservedArchiveEntry(string FullName, byte[] Content);
}
