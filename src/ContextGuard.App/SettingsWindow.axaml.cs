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
        content.Opacity = 0;
        content.Transitions = new Transitions { new DoubleTransition
            { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new CubicEaseInOut() } };
        Opened += (_, _) => content.Opacity = 1;
        Closing += async (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true; if (closing) return;
            closing = true; content.Opacity = 0;
            await Task.Delay(240); allowClose = true; Close();
        };
        AddHandler(KeyDownEvent, (_, e) =>
        { if (e.Key == Key.Escape) { Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private void Done(object? sender, RoutedEventArgs e) => Close();
}
