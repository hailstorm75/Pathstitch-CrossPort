namespace Pathstitch.App.Tests;

public sealed class DesktopStartupTests
{
    [Fact]
    public void NormalizeStartupFileArgumentsKeepsExistingUniqueFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pathstitch-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "sample.stch");
            var drawing = Path.Combine(root, "drawing.dxf");
            File.WriteAllText(project, "{}");
            File.WriteAllText(drawing, "0\nEOF\n");

            var normalized = App.NormalizeStartupFileArguments([
                project,
                project.ToUpperInvariant(),
                drawing,
                Path.Combine(root, "missing.step"),
                "",
            ]);

            Assert.Equal([Path.GetFullPath(project), Path.GetFullPath(drawing)], normalized);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
