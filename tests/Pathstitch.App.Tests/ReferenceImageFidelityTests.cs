using Domain.App.Models;
using Domain.App.Services;
using Pathstitch.App.Services;
using SkiaSharp;

namespace Pathstitch.App.Tests;

public sealed class ReferenceImageFidelityTests
{
    [Theory]
    [InlineData("logo-clean", false, 100.0, 0.98, 1.5, true)]
    [InlineData("antialiased-line-art", false, 100.0, 0.93, 3.0, false)]
    [InlineData("holes-and-islands", false, 100.0, 0.98, 1.5, true)]
    [InlineData("noisy-scan", false, 50.0, 0.93, 3.0, false)]
    [InlineData("border-touching", false, 100.0, 0.98, 1.5, true)]
    [InlineData("transparency", true, 100.0, 0.98, 1.5, true)]
    public void TechnicalTraceFixtures_MeetMaskAndTopologyTargets(
        string fixtureName,
        bool silhouetteOnly,
        double tolerance,
        double minimumIou,
        double maximumHausdorff,
        bool requireExactTopology)
    {
        using var source = Load($"{fixtureName}.png");
        using var truthBitmap = Load($"{fixtureName}-mask.png");
        var truth = MaskFromLuminance(truthBitmap);
        var contours = new AvaloniaReferenceImageTraceService().TraceContours(
            Convert.ToBase64String(File.ReadAllBytes(Fixture($"{fixtureName}.png"))),
            new Editor2DReferenceImageTraceOptions(
                Threshold: 0.5,
                Tolerance: tolerance,
                CornerSmoothness: 0,
                PathOptimization: 0,
                SilhouetteOnly: silhouetteOnly));
        var actual = Rasterize(contours, source.Width, source.Height);

        Assert.True(Iou(actual, truth) >= minimumIou,
            $"{fixtureName} IoU {Iou(actual, truth):F4} was below {minimumIou:F2}.");
        Assert.True(Hausdorff(actual, truth, source.Width, source.Height) <= maximumHausdorff,
            $"{fixtureName} boundary Hausdorff exceeded {maximumHausdorff:F1}px.");
        Assert.InRange(contours.Sum(contour => contour.Count), 3, 500);
        if (requireExactTopology)
        {
            Assert.Equal(CountComponents(truth, source.Width, source.Height), CountComponents(actual, source.Width, source.Height));
            Assert.Equal(CountHoles(truth, source.Width, source.Height), CountHoles(actual, source.Width, source.Height));
        }
    }

    [Theory]
    [InlineData("flat-background-object", 0.98)]
    [InlineData("gradient-background-object", 0.98)]
    public void FlatBackgroundFixtures_MeetAlphaMaskTarget(string fixtureName, double minimumIou)
    {
        using var truthBitmap = Load($"{fixtureName}-mask.png");
        var truth = MaskFromLuminance(truthBitmap);
        var output = new AvaloniaReferenceImageBackgroundRemovalService().RemoveBackground(
            Convert.ToBase64String(File.ReadAllBytes(Fixture($"{fixtureName}.png"))));

        Assert.NotNull(output);
        using var actualBitmap = SKBitmap.Decode(Convert.FromBase64String(output!));
        var actual = MaskFromAlpha(actualBitmap);
        var iou = Iou(actual, truth);
        Assert.True(iou >= minimumIou, $"{fixtureName} alpha IoU {iou:F4} was below {minimumIou:F2}.");
        Assert.True(FalsePositiveRate(actual, truth) <= 0.01);
        Assert.True(FalseNegativeRate(actual, truth) <= 0.01);
    }

    [Fact]
    public void PhotoHairFixture_IsDecisionGateAndUiDoesNotClaimAiRemoval()
    {
        var input = Fixture("photo-hair-synthetic-decision-gate.png");
        var output = new AvaloniaReferenceImageBackgroundRemovalService().RemoveBackground(
            Convert.ToBase64String(File.ReadAllBytes(input)));

        Assert.NotNull(output);
        using var decoded = SKBitmap.Decode(Convert.FromBase64String(output!));
        Assert.Equal(64, decoded.Width);
        Assert.Equal(64, decoded.Height);
        var panel = File.ReadAllText(RepositoryFile(
            "src", "Pathstitch.App", "Pages", "Editor2DLayersPanel.axaml"));
        Assert.Contains("Remove Flat Background", panel, StringComparison.Ordinal);
        Assert.Contains("not AI subject cutout", panel, StringComparison.OrdinalIgnoreCase);
    }

    private static SKBitmap Load(string fileName)
        => SKBitmap.Decode(File.ReadAllBytes(Fixture(fileName)))
            ?? throw new InvalidDataException($"Could not decode fixture {fileName}.");

    private static string Fixture(string fileName)
        => RepositoryFile(
            "tests", "Pathstitch.App.Tests", "Fixtures", "reference-images", fileName);

    private static bool[] Rasterize(
        IReadOnlyList<IReadOnlyList<Editor2DPoint>> contours,
        int width,
        int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.Black);
        using var canvas = new SKCanvas(bitmap);
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var contour in contours)
        {
            if (contour.Count < 3)
                continue;
            path.MoveTo((float)contour[0].X, (float)contour[0].Y);
            foreach (var point in contour.Skip(1))
                path.LineTo((float)point.X, (float)point.Y);
            path.Close();
        }
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };
        canvas.DrawPath(path, paint);
        canvas.Flush();
        return MaskFromLuminance(bitmap);
    }

    private static bool[] MaskFromLuminance(SKBitmap bitmap)
    {
        var mask = new bool[checked(bitmap.Width * bitmap.Height)];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                mask[(y * bitmap.Width) + x] = pixel.Red >= 128;
            }
        }
        return mask;
    }

    private static bool[] MaskFromAlpha(SKBitmap bitmap)
    {
        var mask = new bool[checked(bitmap.Width * bitmap.Height)];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
                mask[(y * bitmap.Width) + x] = bitmap.GetPixel(x, y).Alpha >= 128;
        }
        return mask;
    }

    private static double Iou(bool[] actual, bool[] truth)
    {
        var intersection = 0;
        var union = 0;
        for (var index = 0; index < actual.Length; index++)
        {
            if (actual[index] && truth[index])
                intersection++;
            if (actual[index] || truth[index])
                union++;
        }
        return union == 0 ? 1.0 : intersection / (double)union;
    }

    private static double FalsePositiveRate(bool[] actual, bool[] truth)
    {
        var background = 0;
        var falsePositive = 0;
        for (var index = 0; index < actual.Length; index++)
        {
            if (truth[index])
                continue;
            background++;
            if (actual[index])
                falsePositive++;
        }
        return background == 0 ? 0.0 : falsePositive / (double)background;
    }

    private static double FalseNegativeRate(bool[] actual, bool[] truth)
    {
        var foreground = 0;
        var falseNegative = 0;
        for (var index = 0; index < actual.Length; index++)
        {
            if (!truth[index])
                continue;
            foreground++;
            if (!actual[index])
                falseNegative++;
        }
        return foreground == 0 ? 0.0 : falseNegative / (double)foreground;
    }

    private static double Hausdorff(bool[] left, bool[] right, int width, int height)
    {
        var leftBoundary = Boundary(left, width, height);
        var rightBoundary = Boundary(right, width, height);
        if (leftBoundary.Count == 0 || rightBoundary.Count == 0)
            return leftBoundary.Count == rightBoundary.Count ? 0.0 : double.PositiveInfinity;
        return Math.Max(Directed(leftBoundary, rightBoundary), Directed(rightBoundary, leftBoundary));
    }

    private static double Directed(
        IReadOnlyList<(int X, int Y)> source,
        IReadOnlyList<(int X, int Y)> target)
        => source.Max(point => Math.Sqrt(target.Min(candidate =>
            Math.Pow(point.X - candidate.X, 2) + Math.Pow(point.Y - candidate.Y, 2))));

    private static List<(int X, int Y)> Boundary(bool[] mask, int width, int height)
    {
        var result = new List<(int X, int Y)>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                if (!mask[index])
                    continue;
                if (Neighbors(x, y, width, height).Any(neighbor => !mask[neighbor])
                    || x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    result.Add((x, y));
            }
        }
        return result;
    }

    private static int CountComponents(bool[] mask, int width, int height)
        => CountRegions(mask, width, height, target: true, excludeBorder: false);

    private static int CountHoles(bool[] mask, int width, int height)
        => CountRegions(mask, width, height, target: false, excludeBorder: true);

    private static int CountRegions(bool[] mask, int width, int height, bool target, bool excludeBorder)
    {
        var visited = new bool[mask.Length];
        var count = 0;
        for (var index = 0; index < mask.Length; index++)
        {
            if (visited[index] || mask[index] != target)
                continue;
            var queue = new Queue<int>();
            queue.Enqueue(index);
            visited[index] = true;
            var touchesBorder = false;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var x = current % width;
                var y = current / width;
                touchesBorder |= x == 0 || y == 0 || x == width - 1 || y == height - 1;
                foreach (var neighbor in Neighbors(x, y, width, height))
                {
                    if (visited[neighbor] || mask[neighbor] != target)
                        continue;
                    visited[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            if (!excludeBorder || !touchesBorder)
                count++;
        }
        return count;
    }

    private static IEnumerable<int> Neighbors(int x, int y, int width, int height)
    {
        if (x > 0) yield return (y * width) + x - 1;
        if (x + 1 < width) yield return (y * width) + x + 1;
        if (y > 0) yield return ((y - 1) * width) + x;
        if (y + 1 < height) yield return ((y + 1) * width) + x;
    }

    private static string RepositoryFile(params string[] pathParts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException(Path.Combine(pathParts));
    }
}
