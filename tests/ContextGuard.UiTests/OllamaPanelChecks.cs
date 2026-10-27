using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ContextGuard.App;
using ContextGuard.Core;

namespace ContextGuard.UiTests;

internal static class OllamaPanelChecks
{
    public static void Run(string fixture)
    {
        var host = new Host();
        bool running = false;
        bool rejectList = false;
        int listRequests = 0;
        string models = "{\"models\":[]}";
        TaskCompletionSource<HttpResponseMessage>? late = null;
        using var workspace = new WorkspaceViewModel(() => new ModelClient(new StubHandler((request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/chat")
                return Task.FromResult(Response("{\"message\":{\"content\":\"{\\\"entities\\\":[]}\"}}"));
            if (request.RequestUri!.AbsolutePath == "/api/show")
                return Task.FromResult(Response("{\"model_info\":{\"general.architecture\":\"llama\"}}"));
            Check(request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/tags", "Setup requests metadata only");
            listRequests++;
            if (late is not null) return late.Task;
            if (!running) throw new HttpRequestException();
            if (rejectList) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(Response(models));
        })), ollamaHost: host);
        var window = new MainWindow(workspace); window.Show(); Flush();
        var vm = workspace.Active!.Session;
        try
        {
            var actionsButton = window.FindControl<Button>("ModelChatActionsButton")!;
            var actionsMenu = (Flyout)actionsButton.Flyout!;
            actionsMenu.ShowAt(actionsButton); Flush();
            Check(window.FindControl<Button>("ArchiveChatButton")!.Bounds.Height == 34 &&
                window.FindControl<Button>("DeleteChatButton")!.Bounds.Height == 34, "Header-menu chat actions keep compact single rows");
            Check(window.FindControl<Button>("ExportChatButton")!.Command == workspace.ExportChatCommand &&
                window.FindControl<Button>("ArchiveChatButton")!.Command == workspace.ArchiveChatCommand &&
                window.FindControl<Button>("DeleteChatButton")!.Command == workspace.DeleteChatCommand,
                "Popup actions retain workspace command bindings and active-chat targeting");
            Save(window, "chat-actions-menu");
            workspace.ToggleModelsCommand.Execute(null); Flush();
            Check(!actionsMenu.IsOpen && !actionsButton.IsEffectivelyEnabled, "Collapsing settings dismisses the menu and disables its hidden trigger");
            workspace.ToggleModelsCommand.Execute(null); SettleAnimations();
            Check(window.FindControl<Border>("ContextTrimmingOptions")!.Bounds.Height < 220 &&
                !window.FindControl<StackPanel>("CustomTrimmingEditor")!.IsVisible, "Preset trimming has no empty instruction editor");
            vm.CustomTrimming = true; Flush();
            Check(window.FindControl<StackPanel>("CustomTrimmingEditor")!.IsVisible, "Custom mode reveals instruction editor");
            vm.TrimmingInstructions = "Keep the task description";
            var codeCleanup = window.FindControl<RadioButton>("CodeCleanupRadio")!;
            codeCleanup.IsChecked = true; Flush();
            Check(vm.CustomCodeCleanup && vm.EffectiveTrimmingMode == ContextTrimmingMode.Code &&
                !window.FindControl<StackPanel>("CustomTrimmingEditor")!.IsVisible && !vm.ShowAutomaticTrimmingHint,
                "Code cleanup selects a real local policy without an instruction editor or missing-model hint");
            vm.Folder = Path.Combine(fixture, "a"); vm.PrepareCommand.Execute(null); Wait(vm);
            Check(vm.Ready && listRequests == 0 && !vm.ContextText.Contains("user@example.com") &&
                !vm.ContextText.Contains("demo-sensitive-password"), "Code cleanup prepares protected context without a model");
            vm.ContextApproved = true; vm.TrimmingInstructions = "Keep the task description and key facts";
            Check(vm.ContextApproved, "Editing inactive text instructions does not invalidate code cleanup");
            codeCleanup.BringIntoView(); Save(window, "custom-cleanup-code");
            vm.ModerateTrimming = true; Flush();
            Check(!window.FindControl<StackPanel>("CustomCleanupOptions")!.IsVisible, "Presets hide both custom choices");
            vm.CustomTrimming = true; Flush();
            Check(vm.CustomCodeCleanup && !vm.ShowTrimmingInstructions, "Returning to Custom preserves the code choice");
            window.FindControl<RadioButton>("TextCleanupRadio")!.IsChecked = true; Flush();
            Check(vm.CustomTextCleanup && !vm.Ready && !vm.ContextApproved && vm.TrimmingInstructions.EndsWith("key facts") &&
                window.FindControl<StackPanel>("CustomTrimmingEditor")!.IsVisible && vm.ShowAutomaticTrimmingHint,
                "Switching to description revokes approval, restores the text editor and preserves its instructions");
            var instructionsBox = window.FindControl<TextBox>("TrimmingInstructionsBox")!;
            instructionsBox.BringIntoView(); Save(window, "custom-cleanup-text");
            vm.ModerateTrimming = true;
            vm.Folder = Path.Combine(fixture, "a"); vm.PrepareCommand.Execute(null); Wait(vm);
            string reviewedContext = vm.ContextText;
            vm.ContextApproved = true; vm.Prompt = "Keep this draft";
            vm.CleanerModelExpanded = true; Wait(vm);
            Check(vm.OllamaAvailability == OllamaAvailability.Missing && window.FindControl<Button>("DownloadOllamaButton")!.IsVisible,
                "Opening cleaner detects missing Ollama and offers official download");
            Check(vm.ContextApproved && vm.ContextText == reviewedContext && vm.Prompt == "Keep this draft" && !vm.UseAgent,
                "Automatic discovery preserves review, draft and disabled cleaner");
            vm.DownloadOllamaCommand.Execute(null); Wait(vm); Check(host.Downloads == 1, "Download uses explicit browser action");
            workspace.PickOllamaExecutableAsync = () => Task.FromResult<string?>(null);
            vm.FindOllamaCommand.Execute(null); Wait(vm);
            Check(vm.OllamaAvailability == OllamaAvailability.Missing, "Cancelling picker preserves current setup state");
            workspace.PickOllamaExecutableAsync = () => Task.FromResult<string?>("chosen-ollama.exe");
            host.Executable = "chosen-ollama.exe";
            vm.FindOllamaCommand.Execute(null); Wait(vm);
            Check(host.Selected == "chosen-ollama.exe" && vm.CanStartOllama && window.FindControl<Button>("StartOllamaButton")!.IsVisible,
                "Manual selection shows installed path and start action");
            host.OnStart = () => running = true;
            vm.StartOllamaCommand.Execute(null); Wait(vm);
            Check(host.Starts == 1 && vm.CanInstallOllamaModel && !vm.HasAgentModels && vm.OllamaAvailability == OllamaAvailability.Running,
                "Starting an empty local server offers model installation");
            var installedSelector = window.FindControl<ComboBox>("AgentModelSelector")!;
            var downloadSelector = window.FindControl<ComboBox>("DownloadableAgentModelSelector")!;
            var refreshButton = window.FindControl<Button>("RefreshInstalledModelsButton")!;
            Check(window.FindControl<StackPanel>("InstalledAgentModels")!.IsVisible && !installedSelector.IsEnabled &&
                window.FindControl<TextBlock>("NoInstalledModelsHint")!.IsVisible && refreshButton.IsEffectivelyEnabled,
                "Empty installed list keeps its selector, explanation and refresh reachable with cleaner disabled");
            Check(downloadSelector.Items.Count > 1 && vm.DownloadableAgentModels.All(option => OllamaSetupService.IsValidPullModel(option.Model)),
                "Download selector offers several valid local model tags");
            downloadSelector.SelectedItem = vm.DownloadableAgentModels.Single(option => option.Model == "gemma3:1b"); Flush();
            Check(vm.ModelToInstall == "gemma3:1b" && vm.ModelDownloadHint.Contains("806") && vm.AgentModel == "" &&
                vm.ContextApproved && vm.ContextText == reviewedContext, "Download choice updates size without selecting a cleaner or invalidating review");
            vm.ModelToInstall = "qwen3:4b; calc";
            Check(!vm.InstallOllamaModelCommand.CanExecute(null), "Shell fragments cannot enable model installation");
            downloadSelector.SelectedItem = vm.DownloadableAgentModels.Single(option => option.Model == "qwen3:4b"); Flush();
            vm.InstallOllamaModelCommand.Execute(null); Wait(vm);
            Check(host.Pulls == 1 && host.Model == "qwen3:4b" && !vm.HasAgentModels && vm.AgentModel == "", "Download action passes chosen tag without claiming completion or selecting a cleaner");
            models = "{\"models\":[{\"name\":\"qwen3:4b\"},{\"name\":\"llama3.2:3b\"},{\"name\":\"demo:cloud\"}]}";
            int beforeRefresh = listRequests;
            refreshButton.Command!.Execute(null); Wait(vm);
            Check(listRequests == beforeRefresh + 1 && vm.AgentModels.SequenceEqual(["llama3.2:3b", "qwen3:4b"]) && vm.AgentModel == "",
                "Selector refresh reaches metadata and lists installed local models without automatic selection");
            Check(vm.ContextApproved && vm.ContextText == reviewedContext && vm.Prompt == "Keep this draft" && !vm.UseAgent,
                "Fresh installed list preserves review, draft and cleaner enablement");
            Check(vm.IsInstallModelPresent && !vm.InstallOllamaModelCommand.CanExecute(null) && vm.ModelDownloadHint.Contains("Already installed"),
                "Completed download is marked installed and does not invite another download");
            installedSelector.SelectedItem = "qwen3:4b"; Flush();
            Check(vm.AgentModel == "qwen3:4b" && !vm.ContextApproved && vm.ContextText == "", "Explicit installed selection revokes context review");
            vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true;
            string selectedContext = vm.ContextText;
            vm.RefreshAgentModelsCommand.Execute(null); Wait(vm);
            Check(vm.AgentModel == "qwen3:4b" && (string?)installedSelector.SelectedItem == "qwen3:4b" &&
                vm.ContextApproved && vm.ContextText == selectedContext, "Refreshing an unchanged selection preserves its model and reviewed context");
            models = "{\"models\":[{\"name\":\"qwen3:4b\"},{\"name\":\"gemma3:1b\"}]}";
            int beforeReopen = listRequests;
            vm.CleanerModelExpanded = false; vm.CleanerModelExpanded = true; Wait(vm);
            Check(listRequests == beforeReopen + 1 && vm.AgentModels.Contains("gemma3:1b") && vm.ContextApproved,
                "Reopening the cleaner rechecks downloads made outside the application without losing approval");
            workspace.LanguageIndex = 1; Flush();
            Check(((TextBlock)codeCleanup.Content!).Text == "Очищать в коде" &&
                ((TextBlock)window.FindControl<RadioButton>("TextCleanupRadio")!.Content!).Text == "Очищать текст по описанию",
                "Both custom cleanup choices follow Russian localization");
            Check(vm.OllamaSetupMessage.Contains("Ollama запущена") && (string?)window.FindControl<Button>("InstallOllamaModelButton")!.Content == "Установить через терминал ↗",
                "Setup status and actions follow Russian localization");
            Check(window.FindControl<TextBlock>("DownloadableModelsLabel")!.Text == "Бесплатные модели для скачивания" &&
                vm.ModelDownloadHint.Contains("Уже установлена"), "Catalog labels and installed status follow Russian localization");
            downloadSelector.SelectedItem = vm.DownloadableAgentModels.Single(option => option.Model == "llama3.2:3b"); Flush();
            Check(vm.ModelDownloadHint.Contains("МБ") && vm.ContextApproved, "Download size follows language without changing review");
            window.Height = 1086; Flush();
            Save(window, "right-panel-ollama");
            downloadSelector.IsDropDownOpen = true; Flush(); Save(window, "right-panel-ollama-catalog");
            downloadSelector.IsDropDownOpen = false;
            window.Width = 1100; window.Height = 720; window.UpdateLayout(); SettleAnimations();
            actionsMenu.ShowAt(actionsButton); Flush(); Save(window, "chat-actions-menu-compact"); actionsMenu.Hide();
            vm.CustomTrimming = true; Flush(); instructionsBox.BringIntoView(); Save(window, "custom-cleanup-text-compact-ru");
            var settingsScroll = window.FindControl<ScrollViewer>("ModelSettingsScroll")!;
            var instructionsPosition = instructionsBox.TranslatePoint(new Avalonia.Point(), settingsScroll);
            Check(instructionsPosition is { Y: >= 0 } editor && editor.Y + instructionsBox.Bounds.Height <= settingsScroll.Bounds.Height,
                "The description text area remains reachable at compact size in Russian");
            vm.ModerateTrimming = true; Flush();
            downloadSelector.BringIntoView(); Save(window, "right-panel-ollama-compact");
            var scroll = window.FindControl<ScrollViewer>("ModelSettingsScroll")!;
            var selectorPosition = downloadSelector.TranslatePoint(new Avalonia.Point(), scroll);
            Check(selectorPosition is { Y: >= 0 } position && position.Y + downloadSelector.Bounds.Height <= scroll.Bounds.Height,
                "Compact settings can scroll the download selector into view");
            vm.UseAgent = true; vm.CheckCleanerCommand.Execute(null); Wait(vm);
            Check(vm.CleanerConnectionState == ConnectionState.Connected, "Selected downloaded model can be verified");
            models = "{\"models\":[{\"name\":\"gemma3:1b\"}]}";
            vm.RefreshAgentModelsCommand.Execute(null); Wait(vm);
            Check(vm.AgentModel == "qwen3:4b" && installedSelector.SelectedItem is null && vm.SelectedAgentModelUnavailable &&
                vm.CleanerConnectionState == ConnectionState.Unchecked, "Removed model clears the selector and connection signal without silently replacing settings");
            host.Executable = null; vm.DetectOllamaCommand.Execute(null); Wait(vm);
            Check(vm.IsOllamaRunning && vm.NeedsOllamaExecutable && installedSelector.IsEnabled &&
                window.FindControl<StackPanel>("DownloadableAgentModels")!.IsVisible && !vm.InstallOllamaModelCommand.CanExecute(null),
                "Running server without CLI keeps both lists visible and explains installation requirement");
            host.Executable = "chosen-ollama.exe";
            rejectList = true; vm.RefreshAgentModelsCommand.Execute(null); Wait(vm, expectError: true);
            Check(!vm.HasAgentModels && !vm.IsOllamaRunning, "A failed refresh cannot present the old list as fresh");
            rejectList = false; vm.RefreshAgentModelsCommand.Execute(null); Wait(vm);
            Check(vm.HasAgentModels, "Refresh retries after a metadata error");
            late = new(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.DetectOllamaCommand.Execute(null); Flush();
            Check(vm.Busy && !workspace.NewChatCommand.CanExecute(null), "Discovery owns an operation and blocks navigation");
            var pending = late; vm.AgentEndpoint = "http://localhost:11435";
            pending.SetResult(Response("{\"models\":[{\"name\":\"late-model\"}]}")); Wait(vm);
            Check(!vm.HasAgentModels && vm.OllamaAvailability == OllamaAvailability.Unchecked, "Old endpoint response cannot repopulate setup");
            late = null; vm.DetectOllamaCommand.Execute(null); Wait(vm);
            Check(vm.HasAgentModels, "A fresh check works after endpoint cancellation");
            late = new(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.DetectOllamaCommand.Execute(null); Flush(); var closing = late;
            vm.Dispose(); closing.SetResult(Response("{\"models\":[{\"name\":\"late-model\"}]}")); Wait(vm);
            Check(!vm.HasAgentModels && vm.OllamaExecutable == "", "Disposal prevents late model and path publication");
        }
        finally { window.Close(); Flush(); }

        // No process is launched by these adapter checks.
        string adapterFolder = Path.Combine(fixture, "Ollama's $folder"); Directory.CreateDirectory(adapterFolder);
        string executable = Path.Combine(adapterFolder, OperatingSystem.IsWindows() ? "ollama.exe" : "ollama");
        File.WriteAllText(executable, "synthetic executable fixture");
        var adapter = new DesktopOllamaHost();
        Check(WaitTask(adapter.FindExecutableAsync(executable, default)) == executable, "Manual adapter discovery accepts absolute path with spaces and quotes");
        if (OperatingSystem.IsWindows())
        {
            var start = DesktopOllamaHost.CreatePullStartInfo(executable, new Uri("http://127.0.0.1:11435"), "qwen3:4b");
            string script = Encoding.Unicode.GetString(Convert.FromBase64String(start.ArgumentList.Last()));
            Check(start.UseShellExecute && start.ArgumentList.Contains("-NoExit") && start.ArgumentList.Contains("-NoProfile") &&
                script == "$env:OLLAMA_HOST = 'http://127.0.0.1:11435'; & '" + executable.Replace("'", "''") + "' pull 'qwen3:4b'",
                "Visible terminal uses encoded literal arguments, selected server and no profile");
        }
        Console.WriteLine("PASS Ollama installed/download selectors, fresh metadata, actions, localization and stale results");
    }

    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static void Flush() { Dispatcher.UIThread.RunJobs(); }
    private static void Wait(MainViewModel vm, bool expectError = false)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.Busy && DateTime.UtcNow < until) { Flush(); Thread.Sleep(10); }
        Flush(); Check(!vm.Busy && vm.HasError == expectError, "Setup operation finished: " + vm.Error);
    }
    private static T WaitTask<T>(Task<T> task)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < until) { Flush(); Thread.Sleep(10); }
        Check(task.IsCompleted, "Adapter operation deadline"); return task.GetAwaiter().GetResult();
    }
    private static void SettleAnimations()
    {
        var until = DateTime.UtcNow.AddMilliseconds(650);
        while (DateTime.UtcNow < until) { Flush(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
    }
    private static void Save(Window window, string name)
    {
        SettleAnimations();
        Flush(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        string folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "screenshots"); Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, name + ".png"));
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Host : IOllamaHost
    {
        public string? Executable, Selected, Model;
        public int Starts, Downloads, Pulls;
        public Action? OnStart;
        public Task<string?> FindExecutableAsync(string? selectedPath, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Selected = selectedPath; return Task.FromResult(Executable); }
        public Task StartAsync(string executable, Uri endpoint, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Starts++; OnStart?.Invoke(); return Task.CompletedTask; }
        public void OpenDownloadPage() => Downloads++;
        public void OpenPullTerminal(string executable, Uri endpoint, string model) { Pulls++; Model = model; }
    }
}
