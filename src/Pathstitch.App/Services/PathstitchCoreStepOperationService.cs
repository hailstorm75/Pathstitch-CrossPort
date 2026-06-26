using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Domain.App.Models;
using Domain.App.Services;
using Microsoft.Extensions.Logging;

namespace Pathstitch.App.Services;

public sealed class PathstitchCoreStepOperationService : IEditor3DOperationService
{
    private readonly ILogger<PathstitchCoreStepOperationService> _logger;

    public PathstitchCoreStepOperationService(ILogger<PathstitchCoreStepOperationService> logger)
    {
        _logger = logger;
    }

    public async Task<EditorModelLoadResult> LoadModelAsync(string sourceModelPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
        {
            return new EditorModelLoadResult(false, "The selected 3D source model could not be found.");
        }

        var repositoryRoot = PathstitchRuntimeResolver.ResolveRepositoryRoot();
        if (repositoryRoot is null)
        {
            return new EditorModelLoadResult(false, "Unable to locate the repository root for pathstitch_core execution.");
        }

        var pythonCommand = PathstitchRuntimeResolver.ResolvePythonCommand(repositoryRoot, "pathstitch_core.step_ops");
        if (pythonCommand is null)
        {
            return new EditorModelLoadResult(
                false,
                PathstitchRuntimeResolver.BuildMissingRuntimeMessage("3D model loading"));
        }

        var payload = new
        {
            op = "list_bodies",
            args = new
            {
                input = sourceModelPath,
            },
        };

        try
        {
            var root = await ExecuteStepOperationJsonAsync(
                pythonCommand,
                repositoryRoot,
                payload,
                "load-model",
                cancellationToken).ConfigureAwait(false);

            if (!TryReadSuccess(root, out var message))
                return new EditorModelLoadResult(false, message);

            if (!root.TryGetProperty("data", out var dataElement))
                return new EditorModelLoadResult(false, "The 3D model loader returned no viewport data.");

            if (!dataElement.TryGetProperty("bodies", out var bodiesElement))
                return new EditorModelLoadResult(false, "The 3D model loader returned no body list.");

            var stepJson = dataElement.GetRawText();
            var bodies = JsonSerializer.Deserialize<IReadOnlyList<Body3D>>(bodiesElement.GetRawText()) ?? [];
            var normalizedBodies = bodies
                .Select(body => body with
                {
                    Visible = true,
                    Faces = body.Faces.Select(face => face with { BodyIndex = body.BodyIndex }).ToArray(),
                })
                .ToArray();

            return new EditorModelLoadResult(
                true,
                $"Loaded {normalizedBodies.Length} body/bodies from {Path.GetFileName(sourceModelPath)}.",
                SourceModelPath: sourceModelPath,
                StepJson: stepJson,
                Bodies: normalizedBodies);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load 3D source model {SourceModelPath}.", sourceModelPath);
            return new EditorModelLoadResult(false, $"3D model load failed: {ex.Message}");
        }
    }

    public async Task<EditorModelLoadResult> LoadModelsAsync(
        IReadOnlyList<string> sourceModelPaths,
        string? existingSourceModelPath = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedPaths = sourceModelPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
            return new EditorModelLoadResult(false, "No valid 3D source models were selected.");

        if (normalizedPaths.Length == 1
            && (string.IsNullOrWhiteSpace(existingSourceModelPath) || !File.Exists(existingSourceModelPath)))
        {
            return await LoadModelAsync(normalizedPaths[0], cancellationToken).ConfigureAwait(false);
        }

        var repositoryRoot = PathstitchRuntimeResolver.ResolveRepositoryRoot();
        if (repositoryRoot is null)
            return new EditorModelLoadResult(false, "Unable to locate the repository root for pathstitch_core execution.");

        var pythonCommand = PathstitchRuntimeResolver.ResolvePythonCommand(repositoryRoot, "pathstitch_core.step_ops");
        if (pythonCommand is null)
        {
            return new EditorModelLoadResult(
                false,
                PathstitchRuntimeResolver.BuildMissingRuntimeMessage("3D model import"));
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(outputDirectory);

        try
        {
            var existingSourceExists = !string.IsNullOrWhiteSpace(existingSourceModelPath) && File.Exists(existingSourceModelPath);
            var currentCombinedPath = existingSourceExists
                ? existingSourceModelPath!
                : normalizedPaths[0];

            var incomingIndex = existingSourceExists ? 0 : 1;
            for (var i = incomingIndex; i < normalizedPaths.Length; i++)
            {
                var nextOutputPath = Path.Combine(outputDirectory, $"combined_{Guid.NewGuid():N}.step");
                var payload = new
                {
                    op = "combine_steps",
                    args = new
                    {
                        input = currentCombinedPath,
                        incoming = normalizedPaths[i],
                        output = nextOutputPath,
                    },
                };

                var root = await ExecuteStepOperationJsonAsync(
                    pythonCommand,
                    repositoryRoot,
                    payload,
                    "combine-models",
                    cancellationToken).ConfigureAwait(false);

                if (!TryReadSuccess(root, out var combineMessage))
                    return new EditorModelLoadResult(false, combineMessage);

                currentCombinedPath = nextOutputPath;
            }

            var loadResult = await LoadModelAsync(currentCombinedPath, cancellationToken).ConfigureAwait(false);
            if (!loadResult.IsSuccess)
                return loadResult;

            var message = existingSourceExists
                ? $"Appended {normalizedPaths.Length} model(s) into the current 3D workspace."
                : $"Loaded {normalizedPaths.Length} model(s) into the 3D workspace.";

            return loadResult with
            {
                SourceModelPath = currentCombinedPath,
                Message = message,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to combine {ModelCount} 3D source models.", normalizedPaths.Length);
            return new EditorModelLoadResult(false, $"3D model import failed: {ex.Message}");
        }
    }

    public async Task<EditorFaceDistortionResult> ComputeFaceDistortionAsync(
        string? sourceModelPath,
        SelectedFace3D selectedFace,
        string distortionMode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceModelPath) || !File.Exists(sourceModelPath))
        {
            return new EditorFaceDistortionResult(
                false,
                "No usable 3D source asset is available for distortion analysis.");
        }

        var repositoryRoot = PathstitchRuntimeResolver.ResolveRepositoryRoot();
        if (repositoryRoot is null)
            return new EditorFaceDistortionResult(false, "Unable to locate the repository root for pathstitch_core execution.");

        var pythonCommand = PathstitchRuntimeResolver.ResolvePythonCommand(repositoryRoot, "pathstitch_core.step_ops");
        if (pythonCommand is null)
        {
            return new EditorFaceDistortionResult(
                false,
                PathstitchRuntimeResolver.BuildMissingRuntimeMessage("3D distortion analysis"));
        }

        var payload = new
        {
            op = "face_distortion",
            args = new
            {
                input = sourceModelPath,
                body_index = selectedFace.BodyIndex,
                face_index = selectedFace.FaceIndex,
                distortion_mode = distortionMode,
            },
        };

        try
        {
            var root = await ExecuteStepOperationJsonAsync(
                pythonCommand,
                repositoryRoot,
                payload,
                "face-distortion",
                cancellationToken).ConfigureAwait(false);

            if (!TryReadSuccess(root, out var message))
                return new EditorFaceDistortionResult(false, message);

            if (!root.TryGetProperty("distortion", out var distortionElement))
                return new EditorFaceDistortionResult(false, "The distortion analysis returned no distortion payload.");

            var payloadJson = JsonSerializer.Serialize(new
            {
                body_index = selectedFace.BodyIndex,
                face_index = selectedFace.FaceIndex,
                distortion = JsonSerializer.Deserialize<double[]>(distortionElement.GetRawText()) ?? [],
            });

            return new EditorFaceDistortionResult(true, "Distortion analysis updated.", payloadJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute distortion for body {BodyIndex}, face {FaceIndex}.", selectedFace.BodyIndex, selectedFace.FaceIndex);
            return new EditorFaceDistortionResult(false, $"3D distortion analysis failed: {ex.Message}");
        }
    }

    public async Task<EditorOperationResult> UnfoldAsync(EditorUnfoldRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceModelPath) || !File.Exists(request.SourceModelPath))
        {
            return new EditorOperationResult(
                false,
                "No usable 3D source asset is available for unfolding. Re-import the model into this project so the .stch can embed it for later restore.");
        }

        var repositoryRoot = PathstitchRuntimeResolver.ResolveRepositoryRoot();
        if (repositoryRoot is null)
        {
            return new EditorOperationResult(false, "Unable to locate the repository root for pathstitch_core execution.");
        }

        var pythonCommand = PathstitchRuntimeResolver.ResolvePythonCommand(repositoryRoot, "pathstitch_core.step_ops");
        if (pythonCommand is null)
        {
            return new EditorOperationResult(
                false,
                PathstitchRuntimeResolver.BuildMissingRuntimeMessage("3D unfolding"));
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"unfold_{Guid.NewGuid():N}.dxf");

        var operationPayload = BuildOperationPayload(request, outputPath);
        return await ExecuteStepOperationAsync(
            pythonCommand,
            repositoryRoot,
            operationPayload,
            outputPath,
            "unfold",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<EditorOperationResult> ProjectEdgesAsync(EditorProjectionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceModelPath) || !File.Exists(request.SourceModelPath))
        {
            return new EditorOperationResult(
                false,
                "No usable 3D source asset is available for projection. Re-import the model into this project so the .stch can embed it for later restore.");
        }

        var repositoryRoot = PathstitchRuntimeResolver.ResolveRepositoryRoot();
        if (repositoryRoot is null)
        {
            return new EditorOperationResult(false, "Unable to locate the repository root for pathstitch_core execution.");
        }

        var pythonCommand = PathstitchRuntimeResolver.ResolvePythonCommand(repositoryRoot, "pathstitch_core.step_ops");
        if (pythonCommand is null)
        {
            return new EditorOperationResult(
                false,
                PathstitchRuntimeResolver.BuildMissingRuntimeMessage("3D projection"));
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Pathstitch-CrossPort", "Generated");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"projected_{Guid.NewGuid():N}.dxf");
        var operationPayload = BuildProjectionPayload(request, outputPath);

        return await ExecuteStepOperationAsync(
            pythonCommand,
            repositoryRoot,
            operationPayload,
            outputPath,
            "projection",
            cancellationToken).ConfigureAwait(false);
    }

    private static object BuildOperationPayload(EditorUnfoldRequest request, string outputPath)
    {
        if (string.Equals(request.LayoutMode, "connected", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                op = "unfold_connected",
                args = new
                {
                    input = request.SourceModelPath,
                    output = outputPath,
                    mode = request.UnrollMode,
                    decoration = request.SeamDecoration,
                    tab_height = request.GlueTabHeight,
                    hole_diameter = request.HoleDiameter,
                    hole_spacing = request.HoleSpacing,
                    hole_margin = request.HoleMargin,
                    distortion_mode = request.DistortionMode,
                    seam_control_mode = request.SeamControlMode,
                    whole_body = request.WholeBody,
                    faces = request.WholeBody
                        ? null
                        : request.SelectedFaces.Select(face => new { body_index = face.BodyIndex, face_index = face.FaceIndex }).ToArray(),
                    anchor = request.AnchorFace is null
                        ? null
                        : new { body_index = request.AnchorFace.BodyIndex, face_index = request.AnchorFace.FaceIndex },
                    forced_seams = request.ForcedSeams.Select(edge => new { body_index = edge.BodyIndex, edge_index = edge.EdgeIndex }).ToArray(),
                    forbidden_seams = request.ForbiddenSeams.Select(edge => new { body_index = edge.BodyIndex, edge_index = edge.EdgeIndex }).ToArray(),
                    seam_decorations = request.SeamDecorations.Select(edge => new
                    {
                        body_index = edge.BodyIndex,
                        edge_index = edge.EdgeIndex,
                        decoration = edge.Decoration,
                    }).ToArray(),
                },
            };
        }

        return new
        {
            op = "unfold_faces",
            args = new
            {
                input = request.SourceModelPath,
                output = outputPath,
                distortion_mode = request.DistortionMode,
                faces = request.SelectedFaces.Select(face => new { body_index = face.BodyIndex, face_index = face.FaceIndex }).ToArray(),
            },
        };
    }

    private static object BuildProjectionPayload(EditorProjectionRequest request, string outputPath)
    {
        return new
        {
            op = "project_edges",
            args = new
            {
                input = request.SourceModelPath,
                output = outputPath,
                plane_type = request.PlaneType,
                offset = request.Offset,
                visible_bodies = request.VisibleBodyIndices.ToArray(),
                body_offsets = request.BodyOffsets.ToDictionary(
                    keySelector: x => x.BodyIndex.ToString(),
                    elementSelector: x => new[] { x.X, x.Y, x.Z }),
                face_index = request.FaceIndex,
                face_body_index = request.FaceBodyIndex,
            },
        };
    }

    private async Task<EditorOperationResult> ExecuteStepOperationAsync(
        string pythonCommand,
        string repositoryRoot,
        object operationPayload,
        string outputPath,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = await ExecuteStepOperationJsonAsync(
                pythonCommand,
                repositoryRoot,
                operationPayload,
                operationName,
                cancellationToken).ConfigureAwait(false);

            if (!TryReadSuccess(root, out var message))
                return new EditorOperationResult(false, message);

            return new EditorOperationResult(true, message, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to execute pathstitch_core {OperationName} operation.", operationName);
            return new EditorOperationResult(false, $"3D {operationName} execution failed: {ex.Message}");
        }
    }

    private async Task<JsonElement> ExecuteStepOperationJsonAsync(
        string pythonCommand,
        string repositoryRoot,
        object operationPayload,
        string operationName,
        CancellationToken cancellationToken)
    {
        var requestJson = JsonSerializer.Serialize(operationPayload);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonCommand,
                Arguments = "-m pathstitch_core.step_ops",
                WorkingDirectory = repositoryRoot,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.StartInfo.Environment["PYTHONPATH"] = repositoryRoot;

        process.Start();
        await process.StandardInput.WriteAsync(requestJson).ConfigureAwait(false);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(stderr))
            _logger.LogInformation("pathstitch_core.step_ops stderr ({OperationName}): {StdErr}", operationName, stderr);

        if (string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException($"The 3D {operationName} operation produced no JSON response.");

        using var document = JsonDocument.Parse(stdout);
        return document.RootElement.Clone();
    }

    private static bool TryReadSuccess(JsonElement root, out string message)
    {
        var status = root.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString()
            : "error";
        message = root.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "3D operation finished."
            : "3D operation finished.";

        return string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);
    }

}
