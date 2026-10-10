using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Animation;
using Avalonia.Animation.Easings;

namespace ContextGuard.App;

public partial class SettingsWindow : Window
{
    private bool closing, allowClose;
    public SettingsWindow()
    {
        InitializeComponent();
        var content = this.FindControl<Grid>("SettingsContent")!;
        // Avoid opening an opaque window with completely invisible content.
        content.Opacity = 0.94;
        content.Transitions = new Transitions { new DoubleTransition
            { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(400), Easing = new CubicEaseInOut() } };
        Opened += (_, _) => content.Opacity = 1;
        Closing += async (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true; if (closing) return;
            closing = true; content.IsHitTestVisible = false; content.Opacity = 0.94;
            await Task.Delay(400); allowClose = true; Close();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        { if (e.Key == Key.Escape) { Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private void Done(object? sender, RoutedEventArgs e) => Close();
}
