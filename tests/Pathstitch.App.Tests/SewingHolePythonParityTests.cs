using System.Text.Json;
using Domain.App.Models;
using Domain.App.Services;

namespace Pathstitch.App.Tests;

public sealed class SewingHolePythonParityTests
{
    public static TheoryData<string> CaseNames => new()
    {
        "count-open-line",
        "line-proximity-threshold",
        "nearly-parallel-line",
        "tagged-keepout",
        "hole-radius-obstacle",
        "existing-circle",
        "acute-mitre",
        "acute-round",
        "self-near-proximity",
        "outer-sharp-count",
        "outer-round-count",
    };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void GeneratedCenters_MatchPythonGolden(string caseName)
    {
        var fixture = LoadFixture();
        var parityCase = Assert.Single(fixture.Cases, candidate => candidate.Name == caseName);
        var document = BuildDocument(parityCase.Entities);
        var selectedIds = parityCase.Entities
            .Where(entity => entity.Selected)
            .Select(entity => entity.Id)
            .ToArray();
        var actual = Editor2DSewingHoleGeometry.BuildPreview(
                document,
                selectedIds,
                BuildParameters(parityCase.Parameters),
                $"python-parity-{caseName}")
            .Select(path => new GoldenCenter(path.Center!.X, path.Center.Y, path.Radius!.Value))
            .ToList();

        Assert.Equal(parityCase.Expected.Count, actual.Count);
        foreach (var expected in parityCase.Expected)
        {
            var nearest = actual
                .Select((center, index) => new
                {
                    Center = center,
                    Index = index,
                    Distance = Math.Sqrt(Math.Pow(center.X - expected.X, 2) + Math.Pow(center.Y - expected.Y, 2)),
                })
                .OrderBy(candidate => candidate.Distance)
                .First();
            Assert.True(
                nearest.Distance <= parityCase.CoordinateTolerance,
                $"{caseName}: expected ({expected.X:G17}, {expected.Y:G17}), nearest "
                + $"({nearest.Center.X:G17}, {nearest.Center.Y:G17}), distance {nearest.Distance:G17}, "
                + $"tolerance {parityCase.CoordinateTolerance:G17}.");
            Assert.InRange(
                Math.Abs(nearest.Center.Radius - expected.Radius),
                0,
                parityCase.RadiusTolerance);
            actual.RemoveAt(nearest.Index);
        }
        Assert.Empty(actual);
    }

    [Fact]
    public void ClosedObstacle_EnforcesDocumentedContainmentContractBeyondPythonBug()
    {
        var parityCase = Assert.Single(LoadFixture().Cases, candidate => candidate.Name == "closed-obstacle");
        var document = BuildDocument(parityCase.Entities);
        var selectedIds = parityCase.Entities.Where(entity => entity.Selected).Select(entity => entity.Id).ToArray();
        var actual = Editor2DSewingHoleGeometry.BuildPreview(
            document,
            selectedIds,
            BuildParameters(parityCase.Parameters),
            "python-parity-closed-obstacle");

        Assert.Equal(5, parityCase.Expected.Count);
        Assert.Equal(4, actual.Count);
        Assert.DoesNotContain(actual, hole => Math.Abs(hole.Center!.X - 10) < 1e-8);
    }

    private static Editor2DSewingHoleParameters BuildParameters(GoldenParameters parameters)
        => new(
            Diameter: parameters.Diameter,
            Pitch: parameters.Pitch,
            Margin: parameters.Margin,
            CornerMode: Enum.Parse<Editor2DSewingCornerMode>(parameters.CornerMode),
            CornerClearance: parameters.CornerClearance,
            AvoidanceEnabled: parameters.AvoidanceEnabled,
            AvoidanceClearance: parameters.AvoidanceClearance,
            AvoidPathIds: parameters.AvoidPathIds,
            SymmetricDistribution: parameters.SymmetricDistribution,
            DistributionMode: Enum.Parse<Editor2DSewingDistributionMode>(parameters.DistributionMode),
            Count: parameters.Count,
            VariableSpacingEnabled: parameters.VariableSpacingEnabled,
            VariableSpacingMin: parameters.VariableSpacingMin,
            VariableSpacingMax: parameters.VariableSpacingMax,
            Pattern: Enum.Parse<Editor2DSewingPattern>(parameters.Pattern),
            Side: Enum.Parse<Editor2DSewingSide>(parameters.Side),
            SaddleSpacing: parameters.SaddleSpacing,
            OffsetCornerFillet: parameters.OffsetCornerFillet,
            ProximityFilterEnabled: parameters.ProximityFilterEnabled,
            CornerInterpolationEnabled: parameters.CornerInterpolationEnabled,
            LineProximityFilterEnabled: parameters.LineProximityFilterEnabled,
            LineProximityThreshold: parameters.LineProximityThreshold,
            ProximityFilterDistance: parameters.ProximityFilterDistance);

    private static Editor2DPreviewDocument BuildDocument(IReadOnlyList<GoldenEntity> entities)
    {
        var paths = entities.Select(entity => entity.Type switch
        {
            "LINE" or "LWPOLYLINE" => new Editor2DPreviewPath(
                entity.Id,
                entity.Type,
                entity.Points.Select(point => new Editor2DPoint(point[0], point[1])).ToArray(),
                entity.Closed),
            "CIRCLE" => new Editor2DPreviewPath(
                entity.Id,
                entity.Type,
                [],
                true,
                Center: new Editor2DPoint(entity.Center![0], entity.Center[1]),
                Radius: entity.Radius),
            _ => throw new InvalidDataException($"Unsupported golden entity type '{entity.Type}'."),
        }).ToArray();
        var points = paths.SelectMany(path => path.Points).Concat(paths
            .Where(path => path.Center is not null)
            .Select(path => path.Center!)).ToArray();
        var bounds = points.Length == 0
            ? new Editor2DBounds(0, 0, 0, 0)
            : new Editor2DBounds(
                points.Min(point => point.X),
                points.Min(point => point.Y),
                points.Max(point => point.X),
                points.Max(point => point.Y));
        return new Editor2DPreviewDocument(paths, bounds, new Dictionary<string, int>(), []);
    }

    private static GoldenFixture LoadFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tests",
                "Pathstitch.App.Tests",
                "Fixtures",
                "sewing-hole-parity-cases.json");
            if (File.Exists(candidate))
            {
                return JsonSerializer.Deserialize<GoldenFixture>(
                    File.ReadAllText(candidate),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("Sewing-hole parity fixture is empty.");
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("sewing-hole-parity-cases.json");
    }

    private sealed record GoldenFixture(int SchemaVersion, string Source, IReadOnlyList<GoldenCase> Cases);

    private sealed record GoldenCase(
        string Name,
        double CoordinateTolerance,
        double RadiusTolerance,
        IReadOnlyList<GoldenEntity> Entities,
        GoldenParameters Parameters,
        IReadOnlyList<GoldenCenter> Expected);

    private sealed record GoldenEntity(
        string Id,
        string Type,
        double[][] Points,
        bool Closed,
        bool Selected,
        double[]? Center,
        double? Radius);

    private sealed record GoldenParameters(
        double Diameter,
        double Pitch,
        double Margin,
        string CornerMode,
        double CornerClearance,
        bool AvoidanceEnabled,
        double AvoidanceClearance,
        IReadOnlyList<string> AvoidPathIds,
        bool SymmetricDistribution,
        string DistributionMode,
        int Count,
        bool VariableSpacingEnabled,
        double VariableSpacingMin,
        double VariableSpacingMax,
        string Pattern,
        string Side,
        double SaddleSpacing,
        bool OffsetCornerFillet,
        bool ProximityFilterEnabled,
        bool CornerInterpolationEnabled,
        bool LineProximityFilterEnabled,
        double LineProximityThreshold,
        double ProximityFilterDistance);

    private sealed record GoldenCenter(double X, double Y, double Radius);
}
