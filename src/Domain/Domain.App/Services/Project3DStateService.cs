using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Domain.App.Models;

namespace Domain.App.Services;

public sealed class Project3DStateService
{
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
                payload.SavedTwoDWorkspaceState,
                payload.SavedThreeDWorkspaceState,
                payload.SavedStepTopology,
                payload.SavedBatchWorkspaceState,
                payload.SavedActivityLog ?? [],
                payload.SavedLearnModeEnabled ?? true);
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
        [property: JsonPropertyName("savedStepTopology")] StepGeometryDocument? SavedStepTopology,
        [property: JsonPropertyName("savedActivityLog")] IReadOnlyList<EditorActivityEntry>? SavedActivityLog,
        [property: JsonPropertyName("savedLearnModeEnabled")] bool? SavedLearnModeEnabled,
        string? SourceModelPath = null);

    private sealed record BodyOffsetPayload(
        [property: JsonPropertyName("bodyIndex")] int BodyIndex,
        [property: JsonPropertyName("x")] double X,
        [property: JsonPropertyName("y")] double Y,
        [property: JsonPropertyName("z")] double Z);

    private sealed record PreservedArchiveEntry(string FullName, byte[] Content);
}
