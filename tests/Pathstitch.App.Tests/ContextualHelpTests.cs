using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using Domain.MVVM.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Pathstitch.App.Help;
using Pathstitch.App.Services;
using Pathstitch.App.Tests.Fixtures;
using UI.Navigation;

namespace Pathstitch.App.Tests;

public sealed class ContextualHelpTests
{
    private readonly HeadlessUiFixture _ui = new();

    [Fact]
    public async Task AttachedText_IsAlsoExposedToAssistiveTechnology()
    {
        var control = await _ui.RunAsync(() => new Button());

        await _ui.RunAsync(() =>
        {
            ContextualHelp.SetText(control, "Explains the marked control");

            Assert.Equal("Explains the marked control", ContextualHelp.GetText(control));
            Assert.Equal("Explains the marked control", AutomationProperties.GetHelpText(control));
        });
    }

    [Fact]
    public async Task PointerHover_ActivatesAndClearsMarkedControl()
    {
        var control = await _ui.RunAsync(() => new Button
        {
            Width = 100,
            Height = 40,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        });
        var window = await _ui.RunAsync(() => new TestContextualHelpWindow
        {
            Width = 300,
            Height = 200,
            Content = control,
        });

        await _ui.RunAsync(() =>
        {
            ContextualHelp.SetText(control, "Hover guidance");
            window.Show();

            var controlPoint = control.TranslatePoint(new Point(10, 10), window)!.Value;
            window.MouseMove(controlPoint);
            Assert.Same(control, window.ActiveSource);

            window.MouseMove(new Point(window.ClientSize.Width - 2, window.ClientSize.Height - 2));
            Assert.Null(window.ActiveSource);
            window.Close();
        });
    }

    [Fact]
    public async Task WindowHost_DisplaysActiveHelpAndOpensOnlyConfiguredDocumentation()
    {
        var launcher = new RecordingProcessLauncher();
        using var services = CreateServices(launcher);
        var shell = await _ui.RunAsync(() => new MainWindowShell(services));
        var control = await _ui.RunAsync(() => new Button());
        const string documentationTarget = "https://example.invalid/pathstitch/context";

        await _ui.RunAsync(() =>
        {
            ContextualHelp.SetText(control, "Context-sensitive guidance");
            ((IContextualHelpHost)shell).ActivateContextualHelp(control);

            Assert.Equal("Context-sensitive guidance", shell.DisplayedContextualHelpText);
            Assert.False(shell.IsContextualHelpDocumentationAvailable);
            Assert.False(shell.TryOpenActiveContextualDocumentation());
            Assert.Null(launcher.LastStartInfo);

            ContextualHelp.SetDocumentationTarget(control, documentationTarget);
            ((IContextualHelpHost)shell).ActivateContextualHelp(control);

            Assert.True(shell.IsContextualHelpDocumentationAvailable);
            var keyDown = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F1,
            };
            shell.RaiseEvent(keyDown);

            Assert.True(keyDown.Handled);
            Assert.Equal(documentationTarget, launcher.LastStartInfo?.FileName);
            Assert.True(launcher.LastStartInfo?.UseShellExecute);
        });
    }

    [Fact]
    public async Task WindowHost_RestoresDefaultTextWhenContextEnds()
    {
        using var services = CreateServices(new RecordingProcessLauncher());
        var shell = await _ui.RunAsync(() => new MainWindowShell(services));
        var control = await _ui.RunAsync(() => new Button());

        await _ui.RunAsync(() =>
        {
            ContextualHelp.SetText(control, "Temporary guidance");
            var host = (IContextualHelpHost)shell;
            host.ActivateContextualHelp(control);
            host.DeactivateContextualHelp(control);

            Assert.Equal("Hover over a control for help.", shell.DisplayedContextualHelpText);
            Assert.False(shell.IsContextualHelpDocumentationAvailable);
        });
    }

    private static ServiceProvider CreateServices(IProcessLauncher launcher)
        => new ServiceCollection()
            .AddSingleton<IMessenger>(new StrongReferenceMessenger())
            .AddSingleton<INavigationManager>(new EmptyNavigationManager())
            .AddSingleton(launcher)
            .BuildServiceProvider();

    private sealed class EmptyNavigationManager : INavigationManager
    {
        public bool TryGetNavigablePage(
            string identifier,
            [NotNullWhen(true)] out INavigablePageView? page)
        {
            page = null;
            return false;
        }
    }

    private sealed class TestContextualHelpWindow : Window, IContextualHelpHost
    {
        public Control? ActiveSource { get; private set; }

        public void ActivateContextualHelp(Control source)
            => ActiveSource = source;

        public void DeactivateContextualHelp(Control source)
        {
            if (ReferenceEquals(ActiveSource, source))
                ActiveSource = null;
        }
    }

    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo)
            => LastStartInfo = startInfo;
    }
}
