using System.Reflection;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class Editor2DWorkspaceArchitectureTests
{
    [Fact]
    public void WorkspaceBoundaryHasNoRefStorageHooksAndShellHasNoGeometryAlgorithms()
    {
        var viewModelRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.cs"))!;
        var sources = Directory.GetFiles(viewModelRoot, "*.cs").ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);
        var combined = string.Join('\n', sources.Values);
        Assert.DoesNotContain("internal ref", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("private ref", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("Storage =>", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("=> ref _twoDWorkspace", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("=> ref _threeDWorkspace", combined, StringComparison.Ordinal);

        var shell = sources["EditorPageViewModel.Output.cs"];
        foreach (var algorithm in new[]
                 {
                     "Editor2DGeometry.BuildCleanupPaths", "Editor2DGeometry.BuildConvertedLinePaths",
                     "Editor2DGeometry.TryBuildGlueTabPath", "Editor2DGeometry.TranslatePath",
                     "Editor2DGeometry.RotatePath", "Editor2DGeometry.BuildTextBoundsPoints",
                     "Editor2DGeometry.BuildBoundingBoxOffsetPath",
                 })
            Assert.DoesNotContain(algorithm, shell, StringComparison.Ordinal);

        var workspaceOperations = sources["Editor2DWorkspaceViewModel.Operations.cs"];
        Assert.Contains("ApplyCurveOffsetAsync", workspaceOperations, StringComparison.Ordinal);
        Assert.Contains("ApplyThicknessAsync", workspaceOperations, StringComparison.Ordinal);
        Assert.Contains("ApplyCleanup", workspaceOperations, StringComparison.Ordinal);
        Assert.Contains("ApplyRectangularPattern", workspaceOperations, StringComparison.Ordinal);
        Assert.Contains("ApplyCreases", workspaceOperations, StringComparison.Ordinal);
        Assert.Contains("ApplyGlueTabs", workspaceOperations, StringComparison.Ordinal);
        Assert.Contains("ApplyConvertedLines", workspaceOperations, StringComparison.Ordinal);
        Assert.Contains("ApplySelectedText", workspaceOperations, StringComparison.Ordinal);

        var byRefProperties = typeof(Editor2DWorkspaceViewModel)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.GetMethod?.ReturnParameter.ParameterType.IsByRef == true)
            .ToArray();
        Assert.Empty(byRefProperties);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
