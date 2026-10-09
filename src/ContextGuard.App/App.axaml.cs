using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ContextGuard.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            if (Program.SmokeStartup)
            {
                desktop.MainWindow.Opacity = 0;
                desktop.MainWindow.ShowInTaskbar = false;
                DispatcherTimer.RunOnce(() => desktop.Shutdown(0), TimeSpan.FromSeconds(2));
            }
            desktop.Exit += (_, _) => (desktop.MainWindow?.DataContext as WorkspaceViewModel)?.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
