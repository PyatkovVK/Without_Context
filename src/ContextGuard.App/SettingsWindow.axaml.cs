using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ContextGuard.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) =>
        { if (e.Key == Key.Escape) { Close(); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private void Done(object? sender, RoutedEventArgs e) => Close();
}
