using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.App.Models;
using Microsoft.Extensions.Logging;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    private void FlushPendingScripts()
    {
        while (_pendingViewportScripts.Count > 0)
            _threeDWorkspace.RequestViewportScript(_pendingViewportScripts.Dequeue());

        if (SelectedFaces.Count > 0)
            _threeDWorkspace.RequestViewportScript(BuildSetSelectedFacesScript(SelectedFaces));

        _threeDWorkspace.RequestViewportScript(BuildSetOrthographicModeScript(ThreeDOrthographic));
        _threeDWorkspace.RequestViewportScript(BuildSetPlaneSelectionStateScript());
        RequestBodyVisibilityStateSync();
        _threeDWorkspace.RequestViewportScript(BuildSetBodyMoveStateScript());
        _threeDWorkspace.RequestViewportScript(BuildSetFaceDistortionScript(_distortionDataJson));
    }

    private void RequestViewportScript(string script)
    {
        if (ViewportReady)
        {
            _threeDWorkspace.RequestViewportScript(script);
            return;
        }

        _pendingViewportScripts.Enqueue(script);
    }

    private static string BuildLoadModelScript(string viewportJson)
    {
        var escaped = EscapeForJavaScriptString(viewportJson);
        return $"loadModel(\"{escaped}\\n\");";
    }

    private static string BuildSetSelectedFacesScript(IReadOnlyList<SelectedFace3D> faces)
    {
        var payload = JsonSerializer.Serialize(faces.Select(x => new SelectedFacePayload(x.BodyIndex, x.FaceIndex)));
        var escaped = EscapeForJavaScriptString(payload);
        return $"setSelectedFaces(\"{escaped}\");";
    }

    private static string BuildSetBodyVisibilityScript(int bodyIndex, bool isVisible)
        => $"setBodyVisibility({bodyIndex}, {(isVisible ? "true" : "false")});";

    private static string BuildSetOrthographicModeScript(bool isEnabled)
        => $"setOrthographicMode({(isEnabled ? "true" : "false")});";

    private string BuildSetBodyMoveStateScript()
    {
        var active = IsMoveToolActive ? "true" : "false";
        var selectedBody = SelectedBodyIndex ?? -1;
        var offsets = BodyOffsets.ToDictionary(
            keySelector: x => x.BodyIndex.ToString(CultureInfo.InvariantCulture),
            elementSelector: x => new[] { x.X, x.Y, x.Z });
        var payload = JsonSerializer.Serialize(offsets);
        var escaped = EscapeForJavaScriptString(payload);
        return $"setBodyMoveState({active}, {selectedBody}, \"{escaped}\");";
    }

    private void RequestBodyVisibilityStateSync()
    {
        foreach (var body in Bodies)
            RequestViewportScript(BuildSetBodyVisibilityScript(body.BodyIndex, body.Visible));
    }

    private static string BuildSetFaceDistortionScript(string payload)
        => $"setFaceDistortion(\"{EscapeForJavaScriptString(payload)}\");";

    private static string EscapeForJavaScriptString(string raw)
        => raw
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);

    private EditorViewportMessage ParseViewportMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var op = root.TryGetProperty("op", out var opElement)
                ? opElement.GetString() ?? "unknown"
                : "unknown";

            int? ReadInt(string name)
                => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                    ? value.GetInt32()
                    : null;

            double? ReadDouble(string name)
                => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                    ? value.GetDouble()
                    : null;

            string? error = root.TryGetProperty("error", out var errorValue)
                ? errorValue.GetString()
                : null;
            string? plane = root.TryGetProperty("plane", out var planeValue)
                ? planeValue.GetString()
                : null;
            double? offset = root.TryGetProperty("offset", out var offsetValue) && offsetValue.ValueKind == JsonValueKind.Number
                ? offsetValue.GetDouble()
                : null;
            bool isShiftKey = root.TryGetProperty("isShiftKey", out var shiftValue)
                && (shiftValue.ValueKind == JsonValueKind.True || shiftValue.ValueKind == JsonValueKind.False)
                && shiftValue.GetBoolean();

            return new EditorViewportMessage(
                Operation: op,
                BodyIndex: ReadInt("bodyIndex"),
                FaceIndex: ReadInt("faceIndex"),
                EdgeIndex: ReadInt("edgeIndex"),
                Plane: plane,
                Offset: offset,
                X: ReadDouble("x"),
                Y: ReadDouble("y"),
                Z: ReadDouble("z"),
                RawBody: body,
                Error: error,
                IsShiftKey: isShiftKey);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse viewport message: {Body}", body);
            return new EditorViewportMessage("invalid", RawBody: body, Error: ex.Message);
        }
    }

    private sealed record SelectedFacePayload(
        [property: JsonPropertyName("bodyIndex")] int BodyIndex,
        [property: JsonPropertyName("faceIndex")] int FaceIndex);

}
