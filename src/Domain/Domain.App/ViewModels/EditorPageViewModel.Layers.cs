using Domain.App.Models;
using System.Globalization;

namespace Domain.App.ViewModels;

public sealed partial class EditorPageViewModel
{
    public IReadOnlyList<Editor2DLayer> TwoDLayers => _twoDWorkspace.Layers;

    public string? TwoDActiveLayerId => _twoDWorkspace.ActiveLayerId;

    public IReadOnlyList<Editor2DReferenceImage> TwoDReferenceImages => TwoDLayers
        .Where(layer => layer.IsVisible && layer.ReferenceImage is not null)
        .Select(layer => layer.ReferenceImage!)
        .ToArray();

    public Editor2DReferenceImage? TwoDActiveReferenceImage
        => _twoDWorkspace.ActiveLayer?.ReferenceImage;

    public bool HasTwoDActiveReferenceImage => TwoDActiveReferenceImage is not null;

    public bool TwoDActiveReferenceImageLocked
        => _twoDWorkspace.ActiveLayer?.IsLocked == true;

    private string _twoDReferenceCalibrationWidthText = "100";

    public string TwoDReferenceCalibrationWidthText
    {
        get => _twoDReferenceCalibrationWidthText;
        set => SetProperty(ref _twoDReferenceCalibrationWidthText, value ?? string.Empty);
    }

    public IReadOnlyList<string> TwoDHiddenPathIds => TwoDLayers
        .Where(layer => !layer.IsVisible)
        .SelectMany(layer => layer.PathIds)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public void CreateTwoDLayer()
    {
        _twoDWorkspace.CreateLayer();
        RefreshTwoDLayerFacade();
    }

    public async Task ImportTwoDReferenceImageAsync(CancellationToken cancellationToken = default)
    {
        var imagePath = await _projectFileDialogService
            .PickReferenceImageFileAsync(cancellationToken)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(imagePath))
            return;

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken).ConfigureAwait(true);
            if (!Editor2DReferenceImageMetadata.TryReadPixelSize(bytes, out var pixelWidth, out var pixelHeight))
            {
                StatusText = "Unsupported reference image format";
                return;
            }

            if (!TwoDWorkspace.IsInitialized)
                TwoDDocument = Editor2DWorkspaceState.Empty.Document;
            _twoDWorkspace.ImportReferenceImage(
                Path.GetFileName(imagePath),
                Convert.ToBase64String(bytes),
                pixelWidth,
                pixelHeight);
            RefreshTwoDLayerFacade();
            StatusText = $"Imported reference image: {Path.GetFileName(imagePath)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            StatusText = $"Could not import reference image: {ex.Message}";
        }
    }

    public void MoveTwoDReferenceImage(string layerId, double deltaX, double deltaY)
    {
        var image = GetReferenceImage(layerId);
        if (image is null)
            return;
        if (_twoDWorkspace.UpdateReferenceImageTransform(
                layerId,
                image.X + deltaX,
                image.Y + deltaY,
                image.Width,
                image.Height,
                image.RotationDegrees))
        {
            RefreshTwoDLayerFacade();
        }
    }

    public void UpdateTwoDReferenceImageTransform(
        string layerId,
        double x,
        double y,
        double width,
        double height,
        double rotationDegrees)
    {
        if (_twoDWorkspace.UpdateReferenceImageTransform(layerId, x, y, width, height, rotationDegrees))
            RefreshTwoDLayerFacade();
    }

    public void ScaleTwoDReferenceImage(string layerId, double factor)
    {
        var image = GetReferenceImage(layerId);
        if (image is null || factor <= 0.0)
            return;
        if (_twoDWorkspace.UpdateReferenceImageTransform(
                layerId,
                image.X,
                image.Y,
                image.Width * factor,
                image.Height * factor,
                image.RotationDegrees))
        {
            RefreshTwoDLayerFacade();
        }
    }

    public void RotateTwoDReferenceImage(string layerId, double deltaDegrees)
    {
        var image = GetReferenceImage(layerId);
        if (image is null)
            return;
        if (_twoDWorkspace.UpdateReferenceImageTransform(
                layerId,
                image.X,
                image.Y,
                image.Width,
                image.Height,
                image.RotationDegrees + deltaDegrees))
        {
            RefreshTwoDLayerFacade();
        }
    }

    public void AdjustTwoDReferenceImageOpacity(string layerId, double delta)
    {
        var image = GetReferenceImage(layerId);
        if (image is not null && _twoDWorkspace.SetReferenceImageOpacity(layerId, image.Opacity + delta))
            RefreshTwoDLayerFacade();
    }

    public void AdjustTwoDReferenceTraceThreshold(string layerId, double delta)
    {
        var image = GetReferenceImage(layerId);
        if (image is not null && _twoDWorkspace.SetReferenceImageTraceThreshold(layerId, image.TraceThreshold + delta))
            RefreshTwoDLayerFacade();
    }

    public void CalibrateTwoDReferenceImage(string layerId)
    {
        if (!double.TryParse(
                TwoDReferenceCalibrationWidthText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var width))
        {
            StatusText = "Calibration width must be a positive number";
            return;
        }

        if (_twoDWorkspace.CalibrateReferenceImage(layerId, width))
        {
            RefreshTwoDLayerFacade();
            StatusText = $"Reference image calibrated to {width:0.###} units wide";
        }
    }

    public void TraceTwoDReferenceImage(string layerId)
    {
        var trace = _twoDWorkspace.TraceReferenceImageBounds(layerId);
        if (trace is null)
            return;

        RefreshTwoDLayerFacade();
        StatusText = "Reference bounds traced to editable geometry";
    }

    public void SelectTwoDLayer(string layerId)
    {
        if (_twoDWorkspace.SelectLayer(layerId))
            RefreshTwoDLayerFacade();
    }

    public void ToggleTwoDLayerVisibility(string layerId)
    {
        if (_twoDWorkspace.ToggleLayerVisibility(layerId))
            RefreshTwoDLayerFacade();
    }

    public void ToggleTwoDLayerLock(string layerId)
    {
        if (_twoDWorkspace.ToggleLayerLock(layerId))
            RefreshTwoDLayerFacade();
    }

    public void MoveTwoDLayer(string layerId, int direction)
    {
        if (_twoDWorkspace.MoveLayer(layerId, direction))
            RefreshTwoDLayerFacade();
    }

    public void AssignTwoDSelectionToLayer(string layerId)
    {
        if (_twoDWorkspace.AssignPathsToLayer(layerId, TwoDSelectedPathIds))
            RefreshTwoDLayerFacade();
    }

    private void RefreshTwoDLayerFacade()
    {
        NotifyTwoDWorkspaceFacadeProperties();
        OnPropertyChanged(nameof(TwoDLayers));
        OnPropertyChanged(nameof(TwoDActiveLayerId));
        OnPropertyChanged(nameof(TwoDHiddenPathIds));
        OnPropertyChanged(nameof(TwoDReferenceImages));
        OnPropertyChanged(nameof(TwoDActiveReferenceImage));
        OnPropertyChanged(nameof(HasTwoDActiveReferenceImage));
        OnPropertyChanged(nameof(TwoDActiveReferenceImageLocked));
        Request3DStatePersistence(TimeSpan.FromMilliseconds(150));
    }

    private Editor2DReferenceImage? GetReferenceImage(string layerId)
        => TwoDLayers.FirstOrDefault(layer => layer.Id == layerId)?.ReferenceImage;
}
