using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ContextGuard.App;
using ContextGuard.Core;

namespace ContextGuard.UiTests;

internal static class SidebarStateChecks
{
    public static void Run(string fixture)
    {
        string folder = Path.Combine(fixture, "sidebar-preferences");
        string file = Path.Combine(folder, "sidebar-state.local.json");
        var service = new SidebarStateService(folder);
        Check(Read(service) == new SidebarState(), "Missing sidebar file defaults to both panels open");
        Check(!Directory.Exists(folder), "Loading preferences does not create files");

        foreach (bool left in new[] { false, true })
        foreach (bool right in new[] { false, true })
        {
            using (var original = new WorkspaceViewModel(sidebarState: service))
            {
                Wait(original.InitializeAsync());
                // Normal closure, not a test-only direct save, persists the final state.
                for (int i = 0; i < 10; i++) { original.ToggleChatsCommand.Execute(null); original.ToggleModelsCommand.Execute(null); }
                original.ChatsSidebarOpen = left; original.ModelsSidebarOpen = right;
                original.LanguageIndex = 1;
                original.Active!.Session.ApiKey = "sidebar-test-private-key";
                original.Active.Session.CustomTerms = "sidebar-test-private-value";
                original.Active.Session.Prompt = "sidebar-test-unsent-draft";
                original.NewChatCommand.Execute(null); original.ClearAllCommand.Execute(null);
                Check(original.ChatsSidebarOpen == left && original.ModelsSidebarOpen == right, "Chat changes do not reset sidebar choices");
                var window = new MainWindow(original); window.Show(); Flush(); window.Close();
                Until(() => !window.IsVisible);
            }
            using (var json = JsonDocument.Parse(File.ReadAllText(file)))
            {
                var root = json.RootElement;
                Check(root.EnumerateObject().Select(p => p.Name).ToHashSet().SetEquals(["Version", "ChatsSidebarOpen", "ModelsSidebarOpen"]),
                    "Sidebar service writes only version and two flags");
                Check(root.GetProperty("Version").GetInt32() == 1 && root.GetProperty("ChatsSidebarOpen").GetBoolean() == left &&
                    root.GetProperty("ModelsSidebarOpen").GetBoolean() == right, "Closure persists each independent panel's final choice");
            }
            Check(Directory.GetFiles(folder).Length == 1, "Atomic save leaves no temporary files");
            using var reopened = new WorkspaceViewModel(sidebarState: new SidebarStateService(folder));
            Wait(reopened.InitializeAsync());
            Check(reopened.ChatsSidebarOpen == left && reopened.ModelsSidebarOpen == right, "All four combinations survive restart");
            Check(reopened.LanguageIndex == 0 && reopened.Active!.Session.ApiKey == "" && reopened.Active.Session.Prompt == "" &&
                reopened.Active.Session.CustomTerms == "" && !reopened.Active.Session.ContextApproved,
                "Restoring sidebar state does not restore other preferences, credentials, drafts or approval");
            var reopenedWindow = new MainWindow(reopened); reopenedWindow.Show(); Flush();
            Check(reopenedWindow.FindControl<Border>("ChatsSidebar")!.Bounds.Width == (left ? 310 : 72) &&
                reopenedWindow.FindControl<Border>("ModelsSidebar")!.Bounds.Width == (right ? 350 : 72), "Restored flags determine actual sidebar layout");
            if (!left) Check(reopenedWindow.FindControl<Button>("CollapsedSettingsButton")!.IsVisible,
                "Restored collapsed sidebar retains settings access");
            reopenedWindow.Close(); Until(() => !reopenedWindow.IsVisible);
        }

        foreach (string invalid in new[]
        {
            "", "{", "null", "{}", "{\"Version\":1,\"ChatsSidebarOpen\":false}",
            "{\"Version\":2,\"ChatsSidebarOpen\":false,\"ModelsSidebarOpen\":false}",
            "{\"Version\":1,\"ChatsSidebarOpen\":null,\"ModelsSidebarOpen\":true}",
            "{\"Version\":1,\"ChatsSidebarOpen\":false,\"ModelsSidebarOpen\":false,\"ApiKey\":\"fictional\"}",
            new string(' ', SidebarStateService.MaxFileBytes + 1)
        })
        {
            File.WriteAllText(file, invalid);
            Check(Read(service) == new SidebarState(), "Invalid/incomplete/oversized sidebar state defaults safely");
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            ExpectCancelled(service.LoadAsync(cancelled.Token));
            string before = File.ReadAllText(file);
            ExpectCancelled(service.SaveAsync(new(false, false), cancelled.Token));
            Check(File.ReadAllText(file) == before, "Cancelled save leaves the existing file unchanged");
        }
        Wait(service.SaveAsync(new(false, true), default));
        Check(Read(service) == new SidebarState(false, true), "A fresh save recovers from malformed state");
        File.Delete(file); Check(Read(service) == new SidebarState(), "Deleting the sidebar file resets only sidebar defaults");

        CheckDelayedLoading();
        CheckCloseFailure();
        Console.WriteLine("PASS sidebar persistence: four restart combinations, strict bounded file, sidebar-only schema, cancellation, late loads and close retry.");
    }

    private static void CheckDelayedLoading()
    {
        var delayed = new ControlledService();
        using (var workspace = new WorkspaceViewModel(sidebarState: delayed))
        {
            Task firstLoad = workspace.InitializeAsync(), secondLoad = workspace.InitializeAsync();
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleChatsCommand.Execute(null);
            delayed.Loading.SetResult(new(false, false));
            Wait(firstLoad); Wait(secondLoad);
            Check(delayed.LoadCalls == 1 && workspace.ChatsSidebarOpen && !workspace.ModelsSidebarOpen,
                "One startup load preserves newer left choice and independently restores untouched right panel");
        }
        delayed = new ControlledService();
        using (var workspace = new WorkspaceViewModel(sidebarState: delayed))
        {
            Task load = workspace.InitializeAsync();
            workspace.OpenModelOptionsCommand.Execute(null);
            delayed.Loading.SetResult(new(false, false)); Wait(load);
            Check(!workspace.ChatsSidebarOpen && workspace.ModelsSidebarOpen, "Explicit model options keep right panel open despite a late saved state");
        }
        delayed = new ControlledService();
        var disposed = new WorkspaceViewModel(sidebarState: delayed);
        Task pending = disposed.InitializeAsync(); disposed.Dispose();
        delayed.Loading.SetResult(new(false, false)); Wait(pending);
        Check(delayed.LoadToken.IsCancellationRequested && disposed.ChatsSidebarOpen && disposed.ModelsSidebarOpen,
            "Disposal cancels loading and prevents late state from publishing");
    }

    private static void CheckCloseFailure()
    {
        var service = new ControlledService { FailSave = true };
        service.Loading.SetResult(new());
        using var workspace = new WorkspaceViewModel(sidebarState: service);
        Wait(workspace.InitializeAsync()); workspace.ChatsSidebarOpen = false;
        var window = new MainWindow(workspace); window.Show(); Flush(); window.Close();
        Until(() => window.OwnedWindows.Any());
        Check(window.IsVisible && workspace.StorageFailed && service.SaveCalls == 1, "Failed sidebar save keeps main window open and reports a safe error");
        var dialog = window.OwnedWindows.Single();
        var keepOpen = ((StackPanel)dialog.Content!).Children.OfType<StackPanel>().Single().Children.OfType<Button>().First();
        // The existing close dialog uses click handlers, so activate its actual button.
        keepOpen.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Flush();
        Check(!window.OwnedWindows.Any() && window.IsEnabled, "Keep open restores the workspace after sidebar save failure");
        service.FailSave = false; window.Close(); Until(() => !window.IsVisible);
        Check(service.Saved == new SidebarState(false, true) && service.SaveCalls == 2, "Retrying normal closure saves the final state");
    }

    private sealed class ControlledService : ISidebarStateService
    {
        public TaskCompletionSource<SidebarState> Loading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LoadCalls, SaveCalls;
        public bool FailSave;
        public CancellationToken LoadToken;
        public SidebarState? Saved;
        public Task<SidebarState> LoadAsync(CancellationToken ct) { LoadCalls++; LoadToken = ct; return Loading.Task; }
        public Task SaveAsync(SidebarState state, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); SaveCalls++;
            if (FailSave) throw new SafeOperationException("SidebarStateWriteFailed");
            Saved = state; return Task.CompletedTask;
        }
    }
    private static SidebarState Read(ISidebarStateService service)
    { var task = service.LoadAsync(default); Wait(task); return task.GetAwaiter().GetResult(); }
    private static void ExpectCancelled(Task task)
    { try { Wait(task); throw new InvalidOperationException("Expected cancellation"); } catch (OperationCanceledException) { } }
    private static void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); Flush(); }
    private static void Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!done() && DateTime.UtcNow < deadline) { Flush(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
        Check(done(), "Sidebar persistence check timed out"); Flush();
    }
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
