using System.Reflection;
using Domain.App.ViewModels;

namespace Pathstitch.App.Tests;

public sealed class Editor3DWorkspaceArchitectureTests
{
    [Fact]
    public void ThreeDWorkspace_ExposesCommandsAndReadOnlyStateWithoutRefStorageHooks()
    {
        var workspaceSource = File.ReadAllText(FindRepositoryFile(
            "src", "Domain", "Domain.App", "ViewModels", "Editor3DWorkspaceViewModel.cs"));
        var shellSource = File.ReadAllText(FindRepositoryFile(
            "src", "Domain", "Domain.App", "ViewModels", "EditorPageViewModel.cs"));

        Assert.DoesNotContain("Storage", workspaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal ref", workspaceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("=> ref _threeDWorkspace", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".OperationService", shellSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".PendingViewportScripts", shellSource, StringComparison.Ordinal);

        var type = typeof(Editor3DWorkspaceViewModel);
        Assert.False(type.GetProperty(nameof(Editor3DWorkspaceViewModel.ActiveTool))!.CanWrite);
        Assert.False(type.GetProperty(nameof(Editor3DWorkspaceViewModel.SelectedFaces))!.CanWrite);
        Assert.False(type.GetProperty(nameof(Editor3DWorkspaceViewModel.Bodies))!.CanWrite);
        Assert.NotNull(type.GetMethod(nameof(Editor3DWorkspaceViewModel.ActivateTool)));
        Assert.NotNull(type.GetMethod(nameof(Editor3DWorkspaceViewModel.SetFaceSelection)));
        Assert.NotNull(type.GetMethod(nameof(Editor3DWorkspaceViewModel.CaptureState)));

        Assert.All(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.Name is "_activeTool" or "_selectedFaces" or "_bodies" or "_bodyOffsets"),
            field => Assert.True(field.IsPrivate, $"Workspace state field {field.Name} must stay private."));
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
