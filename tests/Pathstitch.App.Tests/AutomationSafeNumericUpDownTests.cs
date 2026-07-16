using Avalonia.Automation.Peers;
using Pathstitch.App.Controls;

namespace Pathstitch.App.Tests;

public sealed class AutomationSafeNumericUpDownTests
{
    [Fact]
    public void AutomationPeer_NormalizesDecimalRangeChangesToComCompatibleDoubles()
    {
        var control = new AutomationSafeNumericUpDown { Value = 1.0m };
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        object? oldValue = null;
        object? newValue = null;
        peer.PropertyChanged += (_, args) =>
        {
            oldValue = args.OldValue;
            newValue = args.NewValue;
        };

        control.Value = 1.1m;

        Assert.IsType<double>(oldValue);
        Assert.IsType<double>(newValue);
        Assert.Equal(1.0, oldValue);
        Assert.Equal(1.1, newValue);
    }

    [Fact]
    public void Inspector_UsesAutomationSafeSpinnerForEveryDecimalSetting()
    {
        var inspector = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Pathstitch.App", "Pages", "Editor2DInspector.axaml"));

        Assert.DoesNotContain("<NumericUpDown", inspector, StringComparison.Ordinal);
        Assert.Equal(10, inspector.Split("<controls:AutomationSafeNumericUpDown", StringSplitOptions.None).Length - 1);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PathstitchCross.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
