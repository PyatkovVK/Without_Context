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
            // Startup verification must not load, migrate or overwrite the user's real chat folder.
            WorkspaceViewModel workspace;
            if (Program.SmokeStartup) workspace = new WorkspaceViewModel();
            else
            {
                string dataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WithoutContext");
                workspace = new WorkspaceViewModel(
                    archive: new Core.ChatArchiveService(new Core.JsonDirectoryChatStore(System.IO.Path.Combine(dataFolder, "chats"))),
                    sidebarState: new SidebarStateService(dataFolder));
            }
            desktop.MainWindow = new MainWindow(workspace);
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
