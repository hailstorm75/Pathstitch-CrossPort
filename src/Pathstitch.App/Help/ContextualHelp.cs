using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Pathstitch.App.Help;

/// <summary>
/// Adds hover help and an optional documentation target to an Avalonia control.
/// Documentation targets are supplied by each control instead of being embedded
/// in the behavior.
/// </summary>
public sealed class ContextualHelp : AvaloniaObject
{
    public static readonly AttachedProperty<string?> TextProperty =
        AvaloniaProperty.RegisterAttached<ContextualHelp, Control, string?>("Text");

    public static readonly AttachedProperty<string?> DocumentationTargetProperty =
        AvaloniaProperty.RegisterAttached<ContextualHelp, Control, string?>("DocumentationTarget");

    static ContextualHelp()
        => TextProperty.Changed.AddClassHandler<Control>(OnTextChanged);

    private ContextualHelp()
    {
    }

    public static string? GetText(Control control)
        => control.GetValue(TextProperty);

    public static void SetText(Control control, string? value)
        => control.SetValue(TextProperty, value);

    public static string? GetDocumentationTarget(Control control)
        => control.GetValue(DocumentationTargetProperty);

    public static void SetDocumentationTarget(Control control, string? value)
        => control.SetValue(DocumentationTargetProperty, value);

    private static void OnTextChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        var oldText = args.OldValue as string;
        var newText = args.NewValue as string;
        var wasMarked = !string.IsNullOrWhiteSpace(oldText);
        var isMarked = !string.IsNullOrWhiteSpace(newText);

        // Contextual guidance remains available to assistive technology.
        AutomationProperties.SetHelpText(control, newText?.Trim());

        if (!wasMarked && isMarked)
        {
            control.PointerEntered += OnPointerEntered;
            control.PointerExited += OnPointerExited;
            control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }
        else if (wasMarked && !isMarked)
        {
            Deactivate(control);
            control.PointerEntered -= OnPointerEntered;
            control.PointerExited -= OnPointerExited;
            control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        }
        else if (isMarked && control.IsPointerOver)
        {
            Activate(control);
        }
    }

    private static void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control control)
            Activate(control);
    }

    private static void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control control)
            Deactivate(control);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control)
            return;

        var host = e.RootVisual as IContextualHelpHost ?? FindHost(control);
        host?.DeactivateContextualHelp(control);
    }

    private static void Activate(Control control)
        => FindHost(control)?.ActivateContextualHelp(control);

    private static void Deactivate(Control control)
        => FindHost(control)?.DeactivateContextualHelp(control);

    private static IContextualHelpHost? FindHost(Control control)
        => TopLevel.GetTopLevel(control) as IContextualHelpHost
           ?? control.GetLogicalAncestors().OfType<IContextualHelpHost>().FirstOrDefault();
}

internal interface IContextualHelpHost
{
    void ActivateContextualHelp(Control source);

    void DeactivateContextualHelp(Control source);
}
