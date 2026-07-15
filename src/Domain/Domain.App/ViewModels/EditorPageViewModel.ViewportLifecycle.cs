using System.Globalization;
using Domain.App.Models;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public void OnViewportNavigationCompleted(bool isSuccess)
    {
        ViewportStateText = isSuccess ? "Viewport document loaded" : "Viewport navigation failed";

        if (!isSuccess)
        {
            StatusText = "Viewport failed to load";
            ErrorMessage = "The embedded 3D viewport did not finish loading.";
        }
    }

    public void OnViewportMessageReceived(string body)
    {
        var message = ParseViewportMessage(body);
        LastViewportEvent = message.Operation;

        switch (message.Operation)
        {
            case "ready":
                _threeDWorkspace.MarkViewportReady();
                OnPropertyChanged(nameof(ViewportReady));
                StatusText = "Viewport ready";
                ViewportStateText = "Three.js viewport initialized";
                ErrorMessage = null;
                FlushPendingScripts();
                break;

            case "selectFace":
                ApplyViewportSelection(message);
                break;

            case "clearSelection":
                SelectedFaces = [];
                RefreshSelectionState();
                break;

            case "selectBody":
                SelectedBodyIndex = message.BodyIndex;
                StatusText = SelectedBodyIndex is null ? StatusText : $"Body selected: {SelectedBodySummary}";
                RequestBodyMoveStateSync();
                break;

            case "selectEdge" when message.BodyIndex is not null && message.EdgeIndex is not null:
                ToggleSeamEdge(message.BodyIndex.Value, message.EdgeIndex.Value);
                StatusText = $"{SeamControlModeLabel}: edge B{message.BodyIndex.Value + 1}:E{message.EdgeIndex.Value}";
                break;

            case "bodyMoveBegin":
                StatusText = "Body move started";
                break;

            case "bodyMoved":
                ApplyBodyMove(message);
                break;

            case "selectProjectionPlane":
                ActiveTool = Editor3DTool.Plane;
                IsPlaneSelectionActive = true;
                PlaneSelectionModeType = Domain.App.Models.PlaneSelectionModeType.Origin;
                SelectedProjectionPlane = message.Plane;
                SelectedProjectionFaceIndex = null;
                SelectedProjectionBodyIndex = null;
                UpdateProjectionSummary();
                break;

            case "selectProjectionFace":
                ActiveTool = Editor3DTool.Plane;
                IsPlaneSelectionActive = true;
                PlaneSelectionModeType = Domain.App.Models.PlaneSelectionModeType.Face;
                SelectedProjectionPlane = "face";
                SelectedProjectionFaceIndex = message.FaceIndex;
                SelectedProjectionBodyIndex = message.BodyIndex;
                UpdateProjectionSummary();
                break;

            case "updateOffset":
                if (message.Offset is not null)
                {
                    PlaneOffset = message.Offset.Value;
                    PlaneOffsetText = PlaneOffset.ToString("0.###", CultureInfo.InvariantCulture);
                    UpdateProjectionSummary();
                }
                break;

            case "confirmProjection":
                ConfirmPlaneProjection();
                break;

            case "cameraAnimationComplete":
                _ = ExecuteProjectionAsync();
                break;

            case "consoleError":
                ErrorMessage = message.Error ?? message.RawBody ?? "Viewport script error";
                StatusText = "Viewport reported a script error";
                break;

            default:
                ViewportStateText = $"Viewport event: {message.Operation}";
                break;
        }
    }

    public string GetHomeFrameScript() => "if (window.recenterCamera) { window.recenterCamera(); }";

    public void ToggleOrthographic()
    {
        ThreeDOrthographic = !ThreeDOrthographic;
        RequestViewportScript(BuildSetOrthographicModeScript(ThreeDOrthographic));
    }
}
