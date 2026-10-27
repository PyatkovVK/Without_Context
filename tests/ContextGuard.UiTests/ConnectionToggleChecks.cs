using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using ContextGuard.App;
using ContextGuard.Core;

namespace ContextGuard.UiTests;

internal static class ConnectionToggleChecks
{
    public static void Run(string folder)
    {
        int metadata = 0, requests = 0, rawRequests = 0;
        var host = new Host();
        TaskCompletionSource<HttpResponseMessage>? delayedMetadata = null, delayedReply = null;
        using var workspace = new WorkspaceViewModel(() => new ModelClient(new StubHandler(async (request, ct) =>
        {
            requests++;
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/show")
            {
                metadata++;
                if (delayedMetadata is not null) return await delayedMetadata.Task;
                return LocalMetadata();
            }
            if (path == "/api/tags") return Response("{\"models\":[{\"name\":\"toggle-local\"}]}");
            if (path == "/v1/models") { metadata++; return Response("{\"data\":[{\"id\":\"toggle-remote\"}]}"); }
            string body = await request.Content!.ReadAsStringAsync(ct);
            Check(!body.Contains("keep_alive") && !path.Contains("stop"), "Toggles never unload or stop a model");
            if (body.Contains("You are a local privacy detector"))
            { rawRequests++; return Response("{\"message\":{\"content\":\"{\\\"entities\\\":[]}\"}}"); }
            if (body.Contains("Create a short chat title")) return Response("{\"message\":{\"content\":\"Safe task\"}}");
            if (delayedReply is not null) return await delayedReply.Task;
            return Response("{\"message\":{\"content\":\"Safe answer\"},\"done\":true}\n");
        })), ollamaHost: host);
        var window = new MainWindow(workspace); window.Show(); Flush();
        var first = workspace.Active!; var vm = first.Session;
        var cleaner = window.FindControl<Button>("ToggleCleanerButton")!;
        var assistant = window.FindControl<Button>("ToggleAssistantButton")!;
        try
        {
            Check(cleaner.Command == vm.ToggleCleanerCommand && assistant.Command == vm.ToggleAssistantCommand,
                "Header indicators are actual bound buttons");
            vm.AgentModel = "toggle-local"; vm.CheckCleanerCommand.Execute(null); Wait(vm);
            vm.Folder = folder; vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true;
            vm.Prompt = "Complete a safe task"; vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            WaitTitle(vm);
            int completed = vm.Chat.Count;
            Check(completed == 2 && vm.CleanerConnected && vm.AssistantConnected, "Both roles start connected with a completed conversation");
            vm.Prompt = "Keep this draft";
            string context = vm.ContextText;
            int beforeDisable = requests;
            Click(window, cleaner);
            Check(!vm.UseAgent && vm.AssistantConnected && vm.ContextText == context && vm.Chat.Count == completed &&
                vm.Prompt == "Keep this draft" && !vm.ContextApproved && requests == beforeDisable,
                "Cleaner off keeps assistant, transcript, draft and context without sending stop requests; review is revoked");
            Click(window, assistant);
            Check(vm.ChatConnectionState == ConnectionState.Disabled && !vm.CanCompose && vm.Chat.Count == completed &&
                vm.LocalModel == "toggle-local" && vm.AgentModel == "toggle-local" && requests == beforeDisable,
                "Assistant off preserves its settings and chat without any server request");
            Check(window.FindControl<Grid>("MainWorkArea")!.IsVisible && !window.FindControl<ScrollViewer>("SetupGate")!.IsVisible &&
                !window.FindControl<TextBox>("PromptBox")!.IsEnabled, "Paused conversations remain readable while the composer is disabled");
            int beforeResume = metadata;
            Click(window, cleaner); Wait(vm);
            Check(vm.CleanerConnected && !vm.AssistantConnected && metadata > beforeResume && vm.Chat.Count == completed,
                "Resuming cleaner validates metadata and leaves the disabled assistant off");
            Click(window, cleaner);
            beforeResume = metadata;
            Click(window, assistant); Wait(vm);
            Check(vm.AssistantConnected && !vm.UseAgent && metadata > beforeResume && vm.Chat.Count == completed &&
                vm.ContextText == context && vm.Prompt == "Keep this draft", "Resuming the assistant does not re-enable a manually disabled cleaner");
            int rawBefore = rawRequests;
            vm.PreviewPromptCommand.Execute(null); Wait(vm);
            Check(!vm.HasPending && !vm.SendCommand.CanExecute(null), "Changed cleaning mode still requires context review");
            vm.ContextApproved = true;
            vm.PreviewPromptCommand.Execute(null); Wait(vm);
            Check(vm.HasPending && rawRequests == rawBefore, "Disabled cleaner receives no raw-text requests during preview");
            delayedMetadata = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Click(window, cleaner); Flush();
            Check(vm.Busy && cleaner.Command!.CanExecute(null), "Cleaner can be turned off while its reconnection is pending");
            Click(window, cleaner);
            delayedMetadata.SetResult(LocalMetadata()); Wait(vm); delayedMetadata = null;
            Check(!vm.UseAgent && !vm.CleanerConnected && rawRequests == rawBefore, "A late reconnect cannot re-enable the disabled cleaner");
            vm.ContextApproved = true; vm.PreviewPromptCommand.Execute(null); Wait(vm);
            delayedReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.SendCommand.Execute(null); Flush();
            Check(vm.Busy && assistant.Command!.CanExecute(null), "Header assistant off stays available during an active request");
            Click(window, assistant);
            delayedReply.SetResult(Response("{\"message\":{\"content\":\"Late answer\"},\"done\":true}\n")); Wait(vm); delayedReply = null;
            Check(!vm.AssistantConnected && vm.ChatConnectionState == ConnectionState.Disabled && vm.Chat.Count == completed &&
                vm.Prompt == "Keep this draft", "Assistant off cancels an unfinished reply and rejects late reconnection/results");
            workspace.LanguageIndex = 1; Flush();
            Check(vm.ChatConnectionLabel == "Отключён" && vm.AssistantToggleTip.StartsWith("Включить помощника") &&
                (ToolTip.GetTip(cleaner) as string)?.StartsWith("Включить локальный") == true, "Disabled labels and toggle actions use Russian localization");
            Save(window, "header-models-disabled");
            workspace.NewChatCommand.Execute(null);
            var other = workspace.Active!.Session;
            other.Folder = ""; other.ProviderIndex = 1; other.ApiEndpoint = "https://example.com/v1/chat/completions"; other.ApiModel = "toggle-remote";
            other.CheckChatCommand.Execute(null); Wait(other);
            int remoteReads = metadata;
            other.ToggleAssistantCommand.Execute(null);
            Check(!other.AssistantConnected && other.ApiModel == "toggle-remote", "External assistant can also be disabled without erasing configuration");
            other.ToggleAssistantCommand.Execute(null); Wait(other);
            Check(other.AssistantConnected && metadata > remoteReads && !vm.AssistantConnected && !vm.UseAgent,
                "External assistant reconnects with fresh validation without changing the first chat's disabled roles");
            Check(host.SideEffects == 0, "All switches leave Ollama processes, browser and terminals untouched");
        }
        finally { window.Close(); Flush(); }
        Console.WriteLine("PASS header role toggles, fresh reconnect, independent chats, preserved transcript and late cancellation without process control");
    }

    private static void Click(Window window, Button button)
    {
        Flush(); window.UpdateLayout();
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None); Flush();
    }
    private static HttpResponseMessage LocalMetadata() => Response("{\"model_info\":{\"architecture\":\"llama\"}}");
    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static void Wait(MainViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (vm.Busy && DateTime.UtcNow < until) { Flush(); Thread.Sleep(10); }
        Flush(); Check(!vm.Busy && !vm.HasError, "Toggle operation completed: " + vm.Error);
    }
    private static void WaitTitle(MainViewModel vm)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!vm.TitleUpdate.IsCompleted && DateTime.UtcNow < until) { Flush(); Thread.Sleep(10); }
        Check(vm.TitleUpdate.IsCompleted, "Optional title finished before toggle fixture"); Flush();
    }
    private static void Save(Window window, string name)
    {
        var until = DateTime.UtcNow.AddMilliseconds(650);
        while (DateTime.UtcNow < until) { Flush(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
        string folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "screenshots"); Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, name + ".png"));
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Host : IOllamaHost
    {
        public int SideEffects;
        public Task<string?> FindExecutableAsync(string? path, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task StartAsync(string executable, Uri endpoint, CancellationToken ct) { SideEffects++; return Task.CompletedTask; }
        public void OpenDownloadPage() => SideEffects++;
        public void OpenPullTerminal(string executable, Uri endpoint, string model) => SideEffects++;
    }
}
