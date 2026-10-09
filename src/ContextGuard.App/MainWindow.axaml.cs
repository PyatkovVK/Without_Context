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
    private bool allowClose, closing;
    public MainWindow() : this(new WorkspaceViewModel(archive: new Core.ChatArchiveService(new Core.JsonDirectoryChatStore(
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WithoutContext", "chats"))))) { }
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
        workspace.PickExportFolderAsync = async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = workspace.Text["ExportFolderTitle"], AllowMultiple = false });
            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        };
        Opened += async (_, _) => await workspace.InitializeAsync();
        Closing += async (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true;
            if (closing) return;
            closing = true; IsEnabled = false; workspace.CancelOperations();
            // Even a completed save must resume after the original Closing event has returned.
            await Task.Yield();
            try { await workspace.FlushStorageAsync(); allowClose = true; Close(); }
            catch
            {
                closing = false; IsEnabled = true;
                var message = new Window { Title = workspace.Text["Settings"], Width = 470, Height = 180,
                    CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
                var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
                var retry = new Button { Content = workspace.Text["KeepOpen"] };
                var discard = new Button { Content = workspace.Text["CloseWithoutSaving"] };
                retry.Click += (_, _) => message.Close();
                discard.Click += (_, _) => { message.Close(); allowClose = true; Close(); };
                buttons.Children.Add(retry); buttons.Children.Add(discard);
                message.Content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 18,
                    Children = { new TextBlock { Text = workspace.Text["ChatStorageCloseFailed"], TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons } };
                await message.ShowDialog(this);
            }
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
        workspace.SettingsOpen = true;
        try { await settingsWindow.ShowDialog(this); }
        finally { workspace.SettingsOpen = false; settingsWindow = null; }
    }
}
