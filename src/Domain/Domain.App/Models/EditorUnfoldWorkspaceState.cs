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
    [property: JsonPropertyName("holeMarginText")] string HoleMarginText)
{
    public EditorUnfoldWorkspaceState NormalizeForNativeEditor()
        => this with
        {
            NetLayoutIndex = 1,
            UnrollModeIndex = 0,
            GlobalSeamDecorationIndex = 0,
            SeamControlModeIndex = 0,
            GlueTabHeightText = "5",
            HoleDiameterText = "1",
            HoleSpacingText = "4",
            HoleMarginText = "2",
        };
}
