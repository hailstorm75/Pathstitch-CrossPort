namespace Domain.App.Models;

public sealed record SelectedFaceDetails(
    int BodyIndex,
    string BodyName,
    int FaceIndex,
    string FaceType,
    double Area)
{
    public string QueueLabel => $"B{BodyIndex + 1} : F{FaceIndex}";

    public string AreaLabel => $"{Area:0.###} mm²";

    public string SelectionKey => $"{BodyIndex}:{FaceIndex}";
}
