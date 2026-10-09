using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ContextGuard.App;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel workspace;
    private SettingsWindow? settingsWindow;
    public MainWindow() : this(new WorkspaceViewModel()) { }
    public MainWindow(WorkspaceViewModel workspace)
    {
        InitializeComponent();
        this.workspace = workspace; DataContext = workspace;
        workspace.PickFolderAsync = async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = workspace.Text["FolderPickerTitle"], AllowMultiple = false });
            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        };
        SizeChanged += (_, e) => workspace.CompactLayout = e.NewSize.Width < 1200;
        workspace.ScrollChat += ScrollToEnd;
        workspace.RequestSettings += OpenSettings;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            var command = workspace.Active?.Session.PreviewPromptCommand;
            if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0 && command?.CanExecute(null) == true)
            { command.Execute(null); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            workspace.ScrollChat -= ScrollToEnd; workspace.RequestSettings -= OpenSettings;
            workspace.Dispose();
        };
    }
    private void ScrollToEnd() => Dispatcher.UIThread.Post(() =>
        this.FindControl<ScrollViewer>("ChatScroll")?.ScrollToEnd(), DispatcherPriority.Background);

    private void CloseChatMenu(object? sender, RoutedEventArgs e) => this.FindControl<Button>("ChatActions")?.Flyout?.Hide();

    private async void OpenSettings()
    {
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        settingsWindow = new SettingsWindow { DataContext = workspace };
        try { await settingsWindow.ShowDialog(this); }
        finally { settingsWindow = null; }
    }
}
