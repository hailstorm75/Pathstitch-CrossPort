using System.Text.Json.Serialization;

namespace Domain.App.Models;

public sealed record EditorUnfoldWorkspaceState(
    [property: JsonPropertyName("netLayoutIndex")] int NetLayoutIndex,
    [property: JsonPropertyName("distortionModeIndex")] int DistortionModeIndex,
    [property: JsonPropertyName("unrollModeIndex")] int UnrollModeIndex,
    [property: JsonPropertyName("globalSeamDecorationIndex")] int GlobalSeamDecorationIndex,
    [property: JsonPropertyName("seamControlModeIndex")] int SeamControlModeIndex,
    [property: JsonPropertyName("liveRecomputeEnabled")] bool LiveRecomputeEnabled,
    [property: JsonPropertyName("wholeBodyRecompute")] bool WholeBodyRecompute,
    [property: JsonPropertyName("glueTabHeightText")] string GlueTabHeightText,
    [property: JsonPropertyName("holeDiameterText")] string HoleDiameterText,
    [property: JsonPropertyName("holeSpacingText")] string HoleSpacingText,
    [property: JsonPropertyName("holeMarginText")] string HoleMarginText,
    [property: JsonPropertyName("forcedSeams")] IReadOnlyList<EditorSeamEdge3D>? ForcedSeams = null,
    [property: JsonPropertyName("forbiddenSeams")] IReadOnlyList<EditorSeamEdge3D>? ForbiddenSeams = null,
    [property: JsonPropertyName("anchorFace")] SelectedFace3D? AnchorFace = null,
    [property: JsonPropertyName("seamDecorations")] IReadOnlyList<EditorSeamDecoration3D>? SeamDecorations = null)
{
    public EditorUnfoldWorkspaceState NormalizeForOpenGeometryEditor()
        => this with
        {
            NetLayoutIndex = Math.Clamp(NetLayoutIndex, 0, 1),
            UnrollModeIndex = Math.Clamp(UnrollModeIndex, 0, 2),
            GlueTabHeightText = "5",
            HoleDiameterText = "1",
            HoleSpacingText = "4",
            HoleMarginText = "2",
            ForcedSeams = ForcedSeams ?? [],
            ForbiddenSeams = ForbiddenSeams ?? [],
            SeamDecorations = SeamDecorations ?? [],
        };
}
