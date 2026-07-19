using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Pathstitch.App.Controls;

/// <summary>
/// Works around Avalonia 12.0.5 raising decimal UI Automation values that the
/// Windows COM variant marshaller cannot represent. The range provider contract
/// is double-based, so numeric property notifications are normalized to double.
/// </summary>
public sealed class AutomationSafeNumericUpDown : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    protected override AutomationPeer OnCreateAutomationPeer()
        => new AutomationSafeNumericUpDownAutomationPeer(this);
}

internal sealed class AutomationSafeNumericUpDownAutomationPeer : ControlAutomationPeer, IRangeValueProvider
{
    public AutomationSafeNumericUpDownAutomationPeer(AutomationSafeNumericUpDown owner)
        : base(owner)
    {
        Owner.PropertyChanged += OwnerPropertyChanged;
    }

    public new AutomationSafeNumericUpDown Owner => (AutomationSafeNumericUpDown)base.Owner;

    public bool IsReadOnly => Owner.IsReadOnly;

    public double Maximum => (double)Owner.Maximum;

    public double Minimum => (double)Owner.Minimum;

    public double Value => Owner.Value.HasValue
        ? (double)Owner.Value.Value
        : (double)Math.Clamp(0m, Owner.Minimum, Owner.Maximum);

    public double SmallChange => (double)Owner.Increment;

    public double LargeChange => (double)Owner.Increment;

    public void SetValue(double value) => Owner.Value = (decimal)value;

    protected override AutomationControlType GetAutomationControlTypeCore()
        => AutomationControlType.Spinner;

    protected override string GetClassNameCore() => nameof(NumericUpDown);

    private void OwnerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == NumericUpDown.MinimumProperty)
        {
            RaisePropertyChangedEvent(
                RangeValuePatternIdentifiers.MinimumProperty,
                NormalizeAutomationValue(e.OldValue),
                NormalizeAutomationValue(e.NewValue));
        }
        else if (e.Property == NumericUpDown.MaximumProperty)
        {
            RaisePropertyChangedEvent(
                RangeValuePatternIdentifiers.MaximumProperty,
                NormalizeAutomationValue(e.OldValue),
                NormalizeAutomationValue(e.NewValue));
        }
        else if (e.Property == NumericUpDown.ValueProperty)
        {
            RaisePropertyChangedEvent(
                RangeValuePatternIdentifiers.ValueProperty,
                NormalizeAutomationValue(e.OldValue),
                NormalizeAutomationValue(e.NewValue));
        }
        else if (e.Property == NumericUpDown.IsReadOnlyProperty)
        {
            RaisePropertyChangedEvent(
                RangeValuePatternIdentifiers.IsReadOnlyProperty,
                e.OldValue,
                e.NewValue);
        }
    }

    internal static object? NormalizeAutomationValue(object? value)
        => value is decimal decimalValue ? (double)decimalValue : value;
}
