using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ContextGuard.App;

public enum VisibilityMotion { Fade, Sidebar, View }

// Presentation only: stable sidebar layout and gentle, exclusive view entry.
public sealed class SmoothVisibility : AvaloniaObject
{
    private sealed class State { public int Revision; }
    private static readonly ConditionalWeakTable<Control, State> States = new();
    public static readonly AttachedProperty<bool> IsShownProperty =
        AvaloniaProperty.RegisterAttached<SmoothVisibility, Control, bool>("IsShown", true);
    public static readonly AttachedProperty<VisibilityMotion> MotionProperty =
        AvaloniaProperty.RegisterAttached<SmoothVisibility, Control, VisibilityMotion>("Motion");
    public static VisibilityMotion GetMotion(Control control) => control.GetValue(MotionProperty);
    public static void SetMotion(Control control, VisibilityMotion value) => control.SetValue(MotionProperty, value);
    public static bool GetIsShown(Control control) => control.GetValue(IsShownProperty);
    public static void SetIsShown(Control control, bool value) => control.SetValue(IsShownProperty, value);
    static SmoothVisibility() => IsShownProperty.Changed.AddClassHandler<Control>((control, change) =>
    {
        bool show = change.GetNewValue<bool>();
        var state = States.GetOrCreateValue(control); int revision = ++state.Revision;
        var motion = GetMotion(control);
        control.IsHitTestVisible = show;
        if (control.GetVisualRoot() is null)
        { control.IsVisible = show || motion == VisibilityMotion.Sidebar; control.Opacity = show ? 1 : 0; return; }
        var duration = TimeSpan.FromMilliseconds(motion == VisibilityMotion.View ? 240 : 500);
        control.Transitions ??= new Transitions { new DoubleTransition
            { Property = Visual.OpacityProperty, Duration = duration, Easing = new CubicEaseInOut() } };
        if (motion == VisibilityMotion.View)
        {
            // Never layer two documents. A small entry fade avoids a bright/dark flash.
            if (!show) { control.IsVisible = false; return; }
            if (control.Parent is Panel parent)
                foreach (var sibling in parent.Children.Where(c => c != control && GetMotion(c) == VisibilityMotion.View))
                { sibling.IsVisible = false; sibling.IsHitTestVisible = false; }
            var transitions = control.Transitions;
            control.Transitions = null; control.Opacity = 0.94; control.Transitions = transitions;
        }
        if (show || motion == VisibilityMotion.Sidebar) control.IsVisible = true;
        if (show) Dispatcher.UIThread.Post(() =>
        { if (state.Revision == revision) control.Opacity = 1; }, DispatcherPriority.Render);
        else
        {
            control.Opacity = 0;
            if (motion != VisibilityMotion.Sidebar) DispatcherTimer.RunOnce(() =>
            { if (state.Revision == revision) control.IsVisible = false; }, duration);
        }
    });
}
