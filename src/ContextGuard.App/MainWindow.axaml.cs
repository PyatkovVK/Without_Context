using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace ContextGuard.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        vm.PickFolderAsync = async () =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = vm.Text["FolderPickerTitle"], AllowMultiple = false });
            return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        };
        vm.ScrollChat += () => Dispatcher.UIThread.Post(() => this.FindControl<ScrollViewer>("ChatScroll")?.ScrollToEnd(), DispatcherPriority.Background);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) != 0 && vm.PreviewPromptCommand.CanExecute(null))
            { vm.PreviewPromptCommand.Execute(null); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
        Closed += (_, _) => vm.Dispose();
    }
}
