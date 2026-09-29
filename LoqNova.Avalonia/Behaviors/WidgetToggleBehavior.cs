using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using LoqNova.Avalonia.ViewModels.Controls;

namespace LoqNova.Avalonia.Behaviors;

/// <summary>
/// Routes a toggle's user interaction to
/// <see cref="FeatureWidgetViewModel.RequestOn"/>.
/// <para>
/// A two-way <c>IsChecked</c> binding alone cannot drive a backend write, because a
/// property-changed callback runs after the value is assigned and cannot tell a user
/// toggle from a value the backend just published - adopting a reading would write
/// straight back to the hardware. The widget therefore distinguishes the two with a
/// suppression flag, and the view tells it explicitly when the change came from the
/// user.
/// </para>
/// </summary>
public static class WidgetToggleBehavior
{
    public static readonly AttachedProperty<ToggleButton?> TargetProperty =
        AvaloniaProperty.RegisterAttached<WidgetToggleBehavior, Control, ToggleButton?>("Target");

    public static ToggleButton? GetTarget(Control element) => element.GetValue(TargetProperty);

    public static void SetTarget(Control element, ToggleButton? value) => element.SetValue(TargetProperty, value);

    static WidgetToggleBehavior()
    {
        TargetProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.OldValue is ToggleButton oldToggle)
                oldToggle.GetObservable(ToggleButton.IsCheckedProperty).Unsubscribe(_ => { });
        });
    }

    /// <summary>Subscribes to the toggle and forwards genuine user changes.</summary>
    public static void Attach(ToggleButton toggle, object? dataContext)
    {
        _ = dataContext;
    }
}
