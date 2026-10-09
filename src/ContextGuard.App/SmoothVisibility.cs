using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ContextGuard.App;

// Presentation only: keep an outgoing view in layout for the duration of its fade.
public sealed class SmoothVisibility : AvaloniaObject
{
    private sealed class State { public int Revision; }
    private static readonly ConditionalWeakTable<Control, State> States = new();
    public static readonly AttachedProperty<bool> IsShownProperty =
        AvaloniaProperty.RegisterAttached<SmoothVisibility, Control, bool>("IsShown", true);
    public static bool GetIsShown(Control control) => control.GetValue(IsShownProperty);
    public static void SetIsShown(Control control, bool value) => control.SetValue(IsShownProperty, value);
    static SmoothVisibility() => IsShownProperty.Changed.AddClassHandler<Control>((control, change) =>
    {
        bool show = change.GetNewValue<bool>();
        var state = States.GetOrCreateValue(control); int revision = ++state.Revision;
        control.IsHitTestVisible = show;
        if (control.GetVisualRoot() is null) { control.IsVisible = show; control.Opacity = show ? 1 : 0; return; }
        control.Transitions ??= new Transitions();
        if (!control.Transitions.OfType<DoubleTransition>().Any(t => t.Property == Visual.OpacityProperty))
            control.Transitions.Add(new DoubleTransition
            { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new CubicEaseInOut() });
        if (show) control.IsVisible = true;
        control.Opacity = show ? 1 : 0;
        if (!show) DispatcherTimer.RunOnce(() =>
        { if (state.Revision == revision) control.IsVisible = false; }, TimeSpan.FromMilliseconds(280));
    });
}
