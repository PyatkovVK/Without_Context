using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ContextGuard.App;
using ContextGuard.Core;

namespace ContextGuard.UiTests;

internal static class WorkspaceSetupChecks
{
    public static void Run(string sourceFolder)
    {
        bool reject = false;
        int generations = 0;
        TaskCompletionSource<HttpResponseMessage>? pending = null;
        using var workspace = new WorkspaceViewModel(() => new ModelClient(new StubHandler(async (request, ct) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path == "/api/tags") return Response("{\"models\":[{\"name\":\"gemma3:4b\"},{\"name\":\"assistant\"}]}");
            if (path == "/api/show")
            {
                if (pending is not null) return await pending.Task;
                return reject ? new(HttpStatusCode.BadRequest) : Response("{\"model_info\":{\"architecture\":\"gemma\"},\"capabilities\":[\"vision\"]}");
            }
            string body = await request.Content!.ReadAsStringAsync(ct);
            if (body.Contains("You are a local privacy detector")) return Response("{\"message\":{\"content\":\"{\\\"entities\\\":[]}\"}}");
            if (body.Contains("Create a short chat title")) return Response("{\"message\":{\"content\":\"A safe task\"}}");
            generations++; return Response("{\"message\":{\"content\":\"A safe answer\"},\"done\":true}\n");
        })));
        var window = new MainWindow(workspace); window.Show(); Flush();
        var vm = workspace.Active!.Session;
        try
        {
            Check(vm.SetupRequired && !vm.WorkspaceReady && window.FindControl<ScrollViewer>("SetupGate")!.IsVisible &&
                !window.FindControl<Grid>("MainWorkArea")!.IsVisible, "Main workspace is gated until setup is verified");
            vm.LocalModel = "assistant"; vm.AgentModel = "gemma3:4b"; vm.UseAgent = true;
            vm.Folder = sourceFolder;
            Check(!vm.WorkspaceReady && !vm.AssistantConnected && !vm.CleanerConnected, "Names and folder alone never imply a verified connection");
            workspace.LanguageIndex = 1; Save(window, "workspace-setup-required");
            vm.CheckChatCommand.Execute(null); Wait(vm);
            Check(vm.ShowAssistantSummary && !vm.ShowAssistantEditor && vm.WorkspaceReady && vm.Ready && !vm.ContextApproved &&
                window.FindControl<Border>("AssistantConnectedCard")!.IsVisible, "One verified assistant unlocks the workspace and prepares the selected folder automatically");
            vm.CheckCleanerCommand.Execute(null); Wait(vm);
            Check(vm.WorkspaceReady && vm.ShowCleanerSummary && !vm.CleanerModelExpanded &&
                window.FindControl<Border>("CleanerConnectedCard")!.IsVisible && window.FindControl<Grid>("MainWorkArea")!.IsVisible,
                "Both verified models and folder unlock the workspace and show two compact summaries");
            Check(vm.CanCompose && !vm.CanSendReviewedMessage && window.FindControl<Button>("StartWorkButton")!.Command!.CanExecute(null), "The message can be drafted while prepared folder context still requires review");
            Save(window, "workspace-connected");
            vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true; vm.Prompt = "A draft to retain";
            string context = vm.ContextText;
            window.FindControl<Button>("EditAssistantButton")!.Command!.Execute(null); Flush();
            Check(vm.ShowAssistantEditor && vm.ChatModelExpanded && vm.ContextApproved && vm.ContextText == context && vm.WorkspaceReady,
                "Assistant edit opens its connection form without discarding approval or connection");
            vm.CheckChatCommand.Execute(null); Wait(vm);
            window.FindControl<Button>("EditCleanerButton")!.Command!.Execute(null); Wait(vm);
            Check(vm.ShowCleanerEditor && vm.CleanerModelExpanded && vm.ContextApproved && vm.Prompt == "A draft to retain",
                "Cleaner edit opens its model picker and preserves reviewed data");
            vm.CheckCleanerCommand.Execute(null); Wait(vm);
            Check(vm.CanCompose, "Reconfirmed settings retain approval");
            vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            Check(generations == 1 && vm.Chat.Count == 2 && vm.WorkspaceReady, "A prepared and reviewed workspace completes a request");
            vm.LocalModel = "assistant-changed";
            Check(vm.SetupRequired && !vm.AssistantConnected && !vm.CanCompose, "Changing the assistant immediately revokes workspace readiness");
            vm.CheckChatCommand.Execute(null); Wait(vm); vm.AgentModel = "cleaner-changed";
            Check(vm.WorkspaceReady && !vm.Ready && !vm.ContextApproved, "Changing the optional cleaner revokes prepared context while keeping the assistant available");
            vm.CheckCleanerCommand.Execute(null); Wait(vm);
            string folder = vm.Folder; vm.Folder = "";
            Check(vm.WorkspaceReady && vm.CanCompose && vm.CleanerConnected && vm.AssistantConnected, "Removing the optional folder keeps ordinary chat available");
            vm.Folder = folder; reject = true; vm.CheckChatCommand.Execute(null); Wait(vm, true);
            Check(!vm.AssistantConnected && vm.SetupRequired && vm.ShowAssistantEditor, "Failed recheck cannot retain a connected card or ready workspace");
            reject = false; pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.CheckChatCommand.Execute(null); Flush();
            vm.LocalModel = "new-settings";
            pending.SetResult(Response("{\"model_info\":{\"architecture\":\"gemma\"}}")); Wait(vm);
            Check(!vm.AssistantConnected && !vm.WorkspaceReady, "A late connection result cannot verify changed settings");
        }
        finally { window.Close(); Flush(); }
        CheckSingleModelFlows(sourceFolder);
        Console.WriteLine("PASS workspace readiness, connected model cards, editing, review and stale connection results");
    }

    private static void CheckSingleModelFlows(string sourceFolder)
    {
        int localCalls = 0, remoteCalls = 0;
        bool anonymous = false;
        using var workspace = new WorkspaceViewModel(() => new ModelClient(new StubHandler(async (request, ct) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.RequestUri.Host == "example.com")
            {
                Check(request.Headers.Authorization is null, "No-key requests omit the authorization header"); anonymous = true;
                if (path == "/v1/models") return Response("{\"data\":[{\"id\":\"public-model\"}]}");
                string payload = await request.Content!.ReadAsStringAsync(ct);
                Check(!payload.Contains("user@example.com") && !payload.Contains("temporary-key"), "Anonymous API still receives anonymized text only");
                if (payload.Contains("Create a short chat title")) return Response("{\"choices\":[{\"message\":{\"content\":\"Safe request\"}}]}");
                remoteCalls++; return Response("{\"choices\":[{\"message\":{\"content\":\"Safe remote reply\"}}]}");
            }
            if (path == "/api/tags") return Response("{\"models\":[{\"name\":\"single-local\"}]}");
            if (path == "/api/show") return Response("{\"model_info\":{\"architecture\":\"gemma\"}}");
            string body = await request.Content!.ReadAsStringAsync(ct); localCalls++;
            if (body.Contains("You are a local privacy detector")) return Response("{\"message\":{\"content\":\"{\\\"entities\\\":[]}\"}}");
            if (body.Contains("Create a short chat title")) return Response("{\"message\":{\"content\":\"Safe local request\"}}");
            return Response("{\"message\":{\"content\":\"Safe local reply\"},\"done\":true}\n");
        })));
        var window = new MainWindow(workspace); window.Show(); Flush();
        var vm = workspace.Active!.Session;
        try
        {
            vm.AgentModel = "single-local"; vm.CheckCleanerCommand.Execute(null); Wait(vm);
            Check(vm.WorkspaceReady && vm.IsOllama && vm.LocalModel == "single-local" && vm.UseAgent && vm.CanCompose && !vm.ContextApproved,
                "Connecting Ollama alone configures the assistant and local processing without folder or false approval");
            vm.Prompt = "A local task"; vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            Check(vm.Chat.Count == 2, "Ollama alone completes an ordinary chat");
            vm.NewChatCommand.Execute(null); workspace.LanguageIndex = 1; Save(window, "onboarding-local-ready");
            vm.ProviderIndex = 1; vm.ApiEndpoint = "https://example.com/v1/chat/completions"; vm.ApiModel = "public-model";
            vm.CheckChatCommand.Execute(null); Wait(vm);
            Check(anonymous && vm.WorkspaceReady && !vm.UseAgent && !vm.UseApiKey && vm.CanCompose,
                "External connection works alone and releases automatically linked local processing");
            int beforeRemote = localCalls;
            vm.CustomTrimming = true; vm.TrimmingInstructions = "Keep goals";
            vm.Prompt = "Contact user@example.com"; vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            Check(vm.Chat.Count == 2 && remoteCalls == 1 && localCalls == beforeRemote && vm.ShowAutomaticTrimmingHint,
                "External-only chat uses local rules and automatic trimming without a missing-Ollama error");
            workspace.PickFolderAsync = () => Task.FromResult<string?>(sourceFolder);
            vm.BrowseCommand.Execute(null); Wait(vm);
            Check(vm.Ready && !vm.ContextApproved && vm.ContextText.Length > 0 && localCalls == beforeRemote && !vm.HasError,
                "Choosing a folder automatically prepares it with rules and still requires review");
            vm.Prompt = "Use the folder"; vm.PreviewPromptCommand.Execute(null); Wait(vm);
            Check(!vm.HasPending && !vm.SendCommand.CanExecute(null), "Folder context cannot be sent without review");
            vm.ContextApproved = true; vm.ApiKey = "temporary-key";
            Check(vm.UseApiKey, "Entering a credential enables authenticated mode");
            vm.UseApiKey = false; Check(vm.ApiKey == "" && !vm.AssistantConnected, "Anonymous mode clears the old key and revokes prior verification");
            vm.CheckChatCommand.Execute(null); Wait(vm);
            Check(vm.WorkspaceReady && !window.FindControl<TextBox>("ApiKeyBox")!.IsVisible && window.FindControl<TextBlock>("AnonymousApiHelp")!.IsVisible,
                "No-key mode is clear in the external model editor");
            vm.EditAssistantCommand.Execute(null); Flush(); Save(window, "onboarding-external-no-key");
            vm.CustomCodeCleanup = true; vm.CustomTrimming = true;
            workspace.NewChatCommand.Execute(null);
            var next = workspace.Active!.Session;
            Check(next.CustomTrimming && next.CustomCodeCleanup && next.TrimmingInstructions == vm.TrimmingInstructions &&
                !next.Ready && !next.ContextApproved && next.ContextText.Length == 0,
                "New chats copy the cleanup choice and description without copying prepared context or approval");
        }
        finally { window.Close(); Flush(); }
    }

    private static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static void Flush() => Dispatcher.UIThread.RunJobs();
    private static void Wait(MainViewModel vm, bool error = false)
    {
        var until = DateTime.UtcNow.AddSeconds(15);
        while (vm.Busy && DateTime.UtcNow < until) { Flush(); Thread.Sleep(10); }
        Flush(); Check(!vm.Busy && vm.HasError == error, "Setup completed: " + vm.Error);
    }
    private static void Save(Window window, string name)
    {
        var until = DateTime.UtcNow.AddMilliseconds(650);
        while (DateTime.UtcNow < until) { Flush(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
        window.UpdateLayout();
        string folder = Path.Combine(Environment.CurrentDirectory, "artifacts", "screenshots"); Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, name + ".png"));
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
