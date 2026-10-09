using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ContextGuard.App;
using ContextGuard.Core;

namespace ContextGuard.UiTests;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        AppBuilder.Configure<ContextGuard.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia().WithInterFont().SetupWithoutStarting();
        string fixtureRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".test-work"));
        string fixture = Path.Combine(fixtureRoot, "ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string folderA = Path.Combine(fixture, "a"), folderB = Path.Combine(fixture, "b");
        Directory.CreateDirectory(folderA); Directory.CreateDirectory(folderB);
        File.WriteAllText(Path.Combine(folderA, "example.md"), "# Project\nContact user@example.com\npassword=demo-sensitive-password\n");
        File.WriteAllText(Path.Combine(folderB, "other.md"), "Second workspace: second@example.com\npassword=second-sensitive-password\n");
        int networkCalls = 0, generatedChats = 0, detectorCalls = 0;
        bool cloudCleaner = false, rejectApi = false, delayChecks = false;
        var payloads = new List<string>();
        using var workspace = new WorkspaceViewModel(() => new ModelClient(new StubHandler(async (request, ct) =>
        {
            networkCalls++;
            string path = request.RequestUri!.AbsolutePath;
            if (delayChecks && path == "/api/show") await Task.Delay(Timeout.Infinite, ct);
            if (path == "/api/tags") return Response("{\"models\":[{\"name\":\"demo-local\"},{\"name\":\"local-detector\"}]}");
            if (path == "/api/show")
            {
                string body = await request.Content!.ReadAsStringAsync(ct);
                True(!body.Contains("messages"), "Local connection check contains no document or prompt");
                if (cloudCleaner && body.Contains("local-detector"))
                    return Response("{\"remote_host\":\"https://example.com\",\"model_info\":{\"general.architecture\":\"llama\"}}");
                return Response("{\"model_info\":{\"general.architecture\":\"llama\"}}");
            }
            if (path == "/v1/models")
            {
                True(request.Method == HttpMethod.Get && request.Content is null, "API connection check does not generate a reply");
                True(request.Headers.Authorization?.Parameter == "test-key", "Probe uses only authorization header");
                if (rejectApi) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("echoed-sensitive-value") };
                return Response("{\"data\":[{\"id\":\"demo-remote\"}]}");
            }
            string data = await request.Content!.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.TryGetProperty("format", out _))
            {
                detectorCalls++;
                string text = document.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
                string[] entities = text.StartsWith("Edited:", StringComparison.Ordinal) && text.Contains("PrivateOrganization")
                    ? ["PrivateOrganization"] : [];
                return Response(JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(new { entities }) } }));
            }
            generatedChats++; payloads.Add(data);
            const string demoReply = "Here is a three-month product launch plan with key tasks and measurable milestones.\n\n## 1  Overall launch plan\nThe plan has three stages: **Preparation**, **Launch** and **Growth**.\n\n### Product launch roadmap\n| Workstream | Month 1 · Preparation | Month 2 · Launch | Month 3 · Growth |\n| --- | --- | --- | --- |\n| Product | MVP and testing | Release launch | Feedback improvements |\n| Marketing | Content preparation | Launch campaign | Scale the campaign |\n| Sales | Lead preparation | First customers | Sales growth |\n| Metrics | Launch readiness | 100 active users | Revenue growth |\n\n## 2  Detailed milestones\n### Month 1. Preparation (weeks 1–4)\nPrepare the product, team and marketing materials for launch.\n\n- Complete the MVP and run internal testing.\n- Prepare the landing page and positioning.\n- Build a content calendar and customer outreach list.\n\n**Key metrics:**\n- The product is ready to launch.\n- Core marketing materials are approved.";
            return path == "/api/chat" ? Response(JsonSerializer.Serialize(new { message = new { content = demoReply }, done = true }) + "\n")
                : Response(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = demoReply } } } }));
        })));
        var window = new MainWindow(workspace);
        window.Show();
        try
        {
            Flush();
            var first = workspace.Active!;
            var vm = first.Session;
            True(window.Title == "Without Context", "Application is renamed");
            True(!vm.CanCompose && workspace.Conversations.Count == 1, "First chat starts locked with no context");
            True(workspace.LanguageIndex == 0 && VisibleText(window, "Start with your folder"), "English is the default");
            True(vm.CleanerConnectionState == ConnectionState.Disabled && vm.ChatConnectionState == ConnectionState.Unchecked, "Signals never pretend unverified models are connected");
            True(window.FindControl<Border>("ChatsSidebar")!.Bounds.Width == 310 && window.FindControl<Border>("ModelsSidebar")!.Bounds.Width == 350, "Sidebars render expanded");
            True(window.FindControl<RadioButton>("ModerateTrimRadio")!.IsChecked == true, "Moderate trimming is selected by default");
            window.FindControl<RadioButton>("MinimalTrimRadio")!.IsChecked = true; Flush();
            True(vm.TrimmingMode == ContextTrimmingMode.Minimal, "Trimming radio changes the real setting");
            vm.ModerateTrimming = true;
            SaveFrame(window, "start");
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); Flush();
            SaveFrame(window, "collapsed");
            True(window.FindControl<Border>("ChatsSidebar")!.Bounds.Width == 56 && window.FindControl<Border>("ModelsSidebar")!.Bounds.Width == 56, "Both sidebars collapse");
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); Flush();

            var settings = OpenSettings(window, workspace);
            True(settings.IsVisible && settings.Owner == window, "Settings open as an owned modal window");
            var languageSelector = settings.FindControl<ComboBox>("LanguageSelector")!;
            True(languageSelector.SelectedIndex == 0 && settings.FindControl<ComboBox>("ProfileSelector")!.SelectedIndex == 0, "Default selections render in settings");
            SaveFrame(settings, "settings");
            languageSelector.SelectedIndex = 1; Flush();
            True(workspace.LanguageIndex == 1 && vm.LanguageIndex == 1 && VisibleText(window, "Начните с вашей папки"), "Language selection applies to the workspace");
            languageSelector.SelectedIndex = 0; Flush();
            vm.ProfileIndex = 1; vm.CustomTerms = "cleanup-only-private-value";
            settings.Close(); Flush();
            True(!window.OwnedWindows.Any(), "Closing settings releases the owned dialog");

            vm.Folder = folderA;
            vm.PrepareCommand.Execute(null); Wait(vm);
            True(vm.Ready && !vm.ContextApproved && !vm.CanCompose, "Context preparation still requires review");
            True(!vm.ContextText.Contains("user@example.com") && !vm.ContextText.Contains("demo-sensitive-password"), "Context preview is anonymized");
            True(networkCalls == 0, "Rule-based preparation stays local");
            SaveFrame(window, "context");
            vm.ContextApproved = true;
            True(!vm.CanCompose, "An assistant model is required");
            vm.LocalModel = "demo-local";
            vm.MaximumTrimming = true;
            True(!vm.Ready && !vm.ContextApproved, "Changing trimming requires preparing and reviewing context again");
            vm.ModerateTrimming = true; vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true;
            vm.Prompt = "First chat task: help me plan a product launch.\nCreate a three-month plan with milestones, owners and success metrics. My email: another@example.com";
            vm.PreviewPromptCommand.Execute(null); Wait(vm);
            True(vm.HasPending && !vm.PendingText.Contains("another@example.com") && networkCalls == 0, "Message preview stays local");
            string reviewedContext = vm.ContextText, reviewedMessage = vm.PendingText, draft = vm.Prompt;
            string[] placeholders = vm.Replacements.Select(replacement => replacement.Placeholder).ToArray();
            settings = OpenSettings(window, workspace);
            settings.FindControl<ComboBox>("LanguageSelector")!.SelectedIndex = 1; Flush();
            True(vm.ContextApproved && vm.HasPending && vm.CanCompose && vm.ContextText == reviewedContext && vm.PendingText == reviewedMessage && vm.Prompt == draft, "Language switch preserves approval and drafts");
            True(vm.ProfileIndex == 1 && vm.LocalModel == "demo-local" && vm.Replacements.Select(replacement => replacement.Placeholder).SequenceEqual(placeholders), "Language switch preserves model and replacement map");
            settings.Close(); Flush();
            SaveFrame(window, "message-ru");
            workspace.LanguageIndex = 0;
            vm.SendCommand.Execute(null); Wait(vm);
            True(vm.Chat.Count == 2 && generatedChats == 1 && vm.ChatConnectionState == ConnectionState.Connected, "Successful reply updates the assistant signal");
            SaveFrame(window, "chat");
            True(window.GetVisualDescendants().OfType<ReplyContent>().Any(view => view.Text.Contains("Product launch roadmap")), "Model replies use formatted Markdown presentation");
            window.Height = 1086; Flush(); SaveFrame(window, "mvp-design"); window.Height = 920; Flush();
            string reply = vm.Chat[1].Text;
            workspace.LanguageIndex = 1;
            True(vm.Chat[0].Label == "ВЫ" && vm.Chat[1].Text == reply, "Language does not translate model replies");
            workspace.LanguageIndex = 0;

            vm.RefreshModelsCommand.Execute(null); Wait(vm);
            True(vm.HasLocalModels && vm.LocalModel == "demo-local" && vm.Chat.Count == 2 && vm.ContextApproved, "Refreshing the model list preserves the configured model and chat");
            window.FindControl<ComboBox>("LocalModelSelector")!.SelectedItem = "local-detector"; Flush();
            True(vm.LocalModel == "local-detector" && vm.ChatConnectionState == ConnectionState.Unchecked, "Downloaded-model picker selects the model and resets its signal");
            vm.LocalModel = "manual-local-model";
            vm.RefreshModelsCommand.Execute(null); Wait(vm);
            True(vm.LocalModel == "manual-local-model", "Refreshing a list cannot erase a manually entered model");
            vm.LocalModel = "demo-local";
            vm.UseAgent = true; vm.AgentModel = "local-detector";
            vm.CheckCleanerCommand.Execute(null); Wait(vm);
            True(vm.CleanerConnectionState == ConnectionState.Connected && detectorCalls == 0, "Cleaner probe verifies metadata without raw text");
            vm.RefreshAgentModelsCommand.Execute(null); Wait(vm);
            True(vm.HasAgentModels && vm.AgentModel == "local-detector" && vm.CleanerConnectionState == ConnectionState.Connected, "Cleaner model-list refresh preserves its verified configuration");
            vm.AgentModel = "different-model";
            True(vm.CleanerConnectionState == ConnectionState.Unchecked, "Changing cleaner model resets the signal");
            vm.AgentModel = "local-detector"; cloudCleaner = true;
            vm.CheckCleanerCommand.Execute(null); Wait(vm, expectError: true);
            True(vm.CleanerConnectionState == ConnectionState.Failed && detectorCalls == 0, "Cloud cleaner check: state=" + vm.CleanerConnectionState + ", detector calls=" + detectorCalls);
            cloudCleaner = false; vm.DismissErrorCommand.Execute(null); vm.UseAgent = false;

            vm.ProviderIndex = 1; vm.ApiEndpoint = "https://api.example.com/v1/chat/completions";
            vm.ApiModel = "demo-remote"; vm.ApiKey = "test-key";
            int generatedBeforeProbe = generatedChats;
            vm.CheckChatCommand.Execute(null); Wait(vm);
            True(vm.ChatConnectionState == ConnectionState.Connected && generatedChats == generatedBeforeProbe, "API probe verifies model ID without generating a reply");
            rejectApi = true;
            vm.CheckChatCommand.Execute(null); Wait(vm, expectError: true);
            True(vm.ChatConnectionState == ConnectionState.Failed && !vm.Error.Contains("echoed-sensitive-value"), "Failed API check is red and does not reveal provider data");
            rejectApi = false; vm.DismissErrorCommand.Execute(null);
            vm.ApiEndpoint = "https://api.example.com/custom";
            vm.CheckChatCommand.Execute(null); Wait(vm, expectError: true);
            True(vm.ChatConnectionState == ConnectionState.Unavailable, "Unsupported API probe stays unverified");
            vm.DismissErrorCommand.Execute(null);
            vm.ProviderIndex = 0; vm.LocalModel = "demo-local";
            delayChecks = true; vm.CheckChatCommand.Execute(null); Flush();
            True(vm.Busy && vm.ChatConnectionState == ConnectionState.Checking && !workspace.NewChatCommand.CanExecute(null), "Navigation is locked during a connection check");
            vm.CancelCommand.Execute(null); Wait(vm); delayChecks = false;
            True(vm.ChatConnectionState == ConnectionState.Unchecked, "Cancelled checks cannot stay green or connecting");
            // Closing a session cancels its probe and ignores a response that completes later.
            var probeCompletion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken closingProbeToken = default;
            var closingClient = new ModelClient(new StubHandler((_, token) =>
            { closingProbeToken = token; return probeCompletion.Task; }));
            using var closingVm = new MainViewModel(closingClient, new ContextPreparationService(new FolderCollector(), closingClient));
            closingVm.LocalModel = "demo-local"; closingVm.ApiKey = "closing-only-test-key";
            closingVm.CustomTerms = "closing-only-private-value"; closingVm.Prompt = "closing-only-draft";
            closingVm.CheckChatCommand.Execute(null); Flush();
            True(closingVm.Busy, "Closing scenario has a pending request");
            closingVm.Dispose();
            probeCompletion.SetResult(Response("{\"model_info\":{\"general.architecture\":\"llama\"}}"));
            Wait(closingVm);
            True(closingProbeToken.IsCancellationRequested && closingVm.ChatConnectionState != ConnectionState.Connected &&
                closingVm.ApiKey == "" && closingVm.CustomTerms == "" && closingVm.Prompt == "" && !closingVm.Ready,
                "Closure cancels probes and prevents late results from restoring private state");

            // Independent sessions prevent one chat's originals, mappings and history entering another.
            vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true;
            vm.Prompt = "First chat task"; vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            vm.Prompt = "Unsent first chat draft";
            string firstContext = vm.ContextText;
            string firstPlaceholder = vm.Replacements[0].Placeholder;
            workspace.NewChatCommand.Execute(null); Flush();
            var second = workspace.Active!; var secondVm = second.Session;
            True(workspace.Conversations.Count == 2 && !secondVm.Ready && secondVm.Chat.Count == 0 && secondVm.LocalModel == "demo-local", "New chat copies configuration but has its own empty context and history");
            secondVm.ProviderIndex = 1; secondVm.ApiEndpoint = "https://api.example.com/v1/chat/completions"; secondVm.ApiModel = "demo-remote"; secondVm.ApiKey = "second-test-key";
            secondVm.Folder = folderB; secondVm.PrepareCommand.Execute(null); Wait(secondVm); secondVm.ContextApproved = true;
            secondVm.Prompt = "Second chat task"; secondVm.PreviewPromptCommand.Execute(null); Wait(secondVm); secondVm.SendCommand.Execute(null); Wait(secondVm);
            string secondPayload = Decode(payloads[^1]);
            True(!secondPayload.Contains("First chat task") && !secondPayload.Contains("Unsent first chat draft") && !secondPayload.Contains(firstPlaceholder), "Another chat's messages and placeholders stay out of requests");
            var chatList = window.FindControl<ListBox>("ConversationsList")!;
            chatList.SelectedItem = first; Flush();
            True(workspace.Active == first && vm.Chat.Count == 2 && vm.ContextText == firstContext && vm.Prompt == "Unsent first chat draft" && vm.ContextApproved, "Sidebar selection restores the original chat intact");
            True(vm.ProviderIndex == 0 && vm.LocalModel == "demo-local" && vm.ApiKey == "test-key" && secondVm.ProviderIndex == 1 && secondVm.ApiKey == "second-test-key", "Model and credential settings stay separate between chats");
            True(window.FindControl<ComboBox>("ProviderSelector")!.SelectedIndex == 0, "Restoring a chat restores the correct provider selection");
            string firstTitle = first.Title, secondTitle = second.Title;
            workspace.LanguageIndex = 1;
            True(first.Title == firstTitle && second.Title == secondTitle && secondVm.LanguageIndex == 1, "Language preserves message-derived titles and applies to inactive sessions");
            workspace.LanguageIndex = 0;
            SaveFrame(window, "chats");
            workspace.ArchiveChatCommand.Execute(null); Flush();
            True(first.IsArchived && workspace.Active == second && workspace.ArchivedCount == 1 && !workspace.VisibleConversations.Contains(first), "Archive hides a chat without deleting its state");
            workspace.ToggleArchiveViewCommand.Execute(null); Flush();
            True(workspace.ShowingArchive && workspace.Active == first && vm.ContextText == firstContext && vm.Prompt == "Unsent first chat draft", "Archive navigation restores the archived session intact");
            SaveFrame(window, "archive");
            workspace.ArchiveChatCommand.Execute(null); Flush();
            True(!first.IsArchived && !workspace.ShowingArchive && workspace.Active == first && workspace.ArchivedCount == 0, "Restore returns the chat to the active list");


            vm.ContextText += "\nEdited context";
            True(!vm.ContextApproved && vm.Chat.Count == 0, "Context edits still revoke approval and history");
            File.WriteAllText(Path.Combine(folderA, "example.md"), "PrivateOrganization builds this project.");
            vm.UseAgent = true; vm.AgentModel = "local-detector";
            vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true;
            vm.Prompt = "Explain the project"; vm.PreviewPromptCommand.Execute(null); Wait(vm);
            vm.PendingText = "Edited: explain PrivateOrganization";
            int beforeSend = generatedChats;
            vm.SendCommand.Execute(null); Wait(vm);
            True(!vm.ContextApproved && vm.SelectedTab == 1 && !vm.ContextText.Contains("PrivateOrganization") && generatedChats == beforeSend, "Edits to previews still go through the local agent and trigger review");
            vm.ContextApproved = true; vm.Prompt = new string('x', 16_001);
            vm.PreviewPromptCommand.Execute(null); Wait(vm, expectError: true);
            True(vm.Error.StartsWith("The message is too long"), "English validation errors");
            workspace.LanguageIndex = 1;
            True(vm.Error.StartsWith("Сообщение слишком длинное"), "Existing errors translate live");
            workspace.LanguageIndex = 0; vm.DismissErrorCommand.Execute(null);
            workspace.DeleteChatCommand.Execute(null); Flush();
            True(workspace.Conversations.Count == 1 && workspace.Active == second && vm.ApiKey == "" && vm.ContextText == "" && vm.CustomTerms == "", "Deleting a chat releases its credentials, custom values and context");
            workspace.ArchiveChatCommand.Execute(null); Flush();
            True(second.IsArchived && workspace.Conversations.Count == 2 && workspace.Active != second, "Archiving the last chat leaves a fresh usable chat");
            settings = OpenSettings(window, workspace);
            workspace.ClearAllCommand.Execute(null); Flush();
            True(settings.FindControl<ComboBox>("ProfileSelector")!.SelectedIndex == 0, "Clearing all resets the active settings form");
            settings.Close(); Flush();
            True(workspace.Conversations.Count == 1 && !workspace.Active!.Session.Ready && workspace.Active.Session.ApiKey == "" &&
                secondVm.ApiKey == "" && workspace.ArchivedCount == 0, "Clear all also releases archived credentials and context");
            var cleanVm = workspace.Active?.Session ?? throw new Exception("Clear all must leave an active chat");
            cleanVm.TrimmingMode = ContextTrimmingMode.Custom; cleanVm.TrimmingInstructions = "Keep goals"; cleanVm.Folder = folderB;
            cleanVm.PrepareCommand.Execute(null); Wait(cleanVm, expectError: true);
            True(!cleanVm.Ready && cleanVm.Error.Contains("Ollama cleaner"), "Custom trimming cannot silently use the remote assistant");
            cleanVm.DismissErrorCommand.Execute(null); cleanVm.UseAgent = true; cleanVm.AgentModel = "local-detector";
            cleanVm.PrepareCommand.Execute(null); Wait(cleanVm);
            True(cleanVm.Ready && !cleanVm.ContextApproved && cleanVm.ContextText.Length <= ContextReducer.TargetCharacters(cleanVm.ContextLimit, cleanVm.TrimmingMode),
                "Custom local trimming produces bounded context that still requires approval");
            workspace.ClearAllCommand.Execute(null); Flush();
            window.Width = 1000; window.Height = 720; Flush();
            SaveFrame(window, "compact");
            True(window.FindControl<TextBox>("PromptBox")!.Bounds.Width > 200, "Chat remains usable at minimum window size");
            Console.WriteLine("PASS UI: MVP design, formatted replies, trimming settings, archive/restore, isolated sessions, model signals, language and privacy review.");
            Console.WriteLine("Synthetic screenshots: artifacts/screenshots/{mvp-design,start,collapsed,settings,context,message-ru,chat,chats,archive,compact}.png");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL UI: " + e.Message); return 1; }
        finally
        {
            window.Close();
            string resolved = Path.GetFullPath(fixture);
            if (Path.GetDirectoryName(resolved) == fixtureRoot && Path.GetFileName(resolved).StartsWith("ui-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    static string Decode(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        return string.Join("\n", json.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("content").GetString()));
    }
    static SettingsWindow OpenSettings(Window window, WorkspaceViewModel workspace)
    {
        workspace.SettingsCommand.Execute(null); Flush();
        return window.OwnedWindows.OfType<SettingsWindow>().Single();
    }
    static void Flush() { Dispatcher.UIThread.RunJobs(); }
    static bool VisibleText(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text);
    static void Wait(MainViewModel vm, bool expectError = false)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (vm.Busy && DateTime.UtcNow < deadline) { Flush(); Thread.Sleep(10); }
        Flush(); True(!vm.Busy, "Operation timed out");
        True(vm.HasError == expectError, "Unexpected operation result: " + vm.Error);
    }
    static void SaveFrame(Window window, string name)
    {
        Flush(); window.UpdateLayout();
        string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "screenshots");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) throw new Exception("Could not render frame");
        frame.Save(Path.Combine(directory, name + ".png"));
    }
    static void True(bool value, string message) { if (!value) throw new Exception(message); }
}

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
}
