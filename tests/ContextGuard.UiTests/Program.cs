using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
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
        int networkCalls = 0, generatedChats = 0, detectorCalls = 0, generatedTitles = 0;
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
            if (document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.StartsWith("Create a short chat title", StringComparison.Ordinal))
            {
                generatedTitles++;
                string safeInput = document.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
                True(!safeInput.Contains("user@example.com") && !safeInput.Contains("demo-sensitive-password") && !safeInput.Contains("second-test-key"), "Title request contains only anonymized task and context");
                string title = safeInput.Contains("Second chat task") ? "Second workspace task" : "Product launch plan";
                return path == "/api/chat" ? Response(JsonSerializer.Serialize(new { message = new { content = title } })) :
                    Response(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = title } } } }));
            }
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
            True(!window.FindControl<Button>("ArchivesButton")!.IsVisible && !workspace.HasArchivedChats, "An empty archive has no sidebar entry");
            True(!VisibleText(window, "Chats are saved locally. Export from chat settings."), "Local storage information is not repeated in the sidebar");
            True(window.FindControl<Button>("ToggleChatsButton")!.Bounds.Width == 44 && window.FindControl<Button>("ToggleModelsButton")!.Bounds.Width == 44,
                "Both sidebar toggles have larger accessible hit areas");
            CheckCenteredToggleIcon(window, "ToggleChatsButton"); CheckCenteredToggleIcon(window, "ToggleModelsButton");
            True(!window.FindControl<Button>("CollapsedSettingsButton")!.IsVisible, "Collapsed settings icon is hidden in the expanded sidebar");
            CheckStableHover(window, window.FindControl<Button>("NewChatButton")!);
            True(window.FindControl<RadioButton>("ModerateTrimRadio")!.IsChecked == true, "Moderate trimming is selected by default");
            window.FindControl<RadioButton>("MinimalTrimRadio")!.IsChecked = true; Flush();
            True(vm.TrimmingMode == ContextTrimmingMode.Minimal, "Trimming radio changes the real setting");
            vm.ModerateTrimming = true;
            SaveFrame(window, "start");
            double footerPosition = window.FindControl<Button>("SettingsButton")!.TranslatePoint(default, window)!.Value.Y;
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); SettleAnimations(180);
            double inFlightWidth = window.FindControl<Border>("ChatsSidebar")!.Bounds.Width;
            True(inFlightWidth > 72 && inFlightWidth < 310, "Sidebar resizing progresses through intermediate widths");
            SaveFrame(window, "sidebar-motion");
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); SettleAnimations(100);
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); SettleAnimations();
            SaveFrame(window, "collapsed");
            True(window.FindControl<Border>("ChatsSidebar")!.Bounds.Width == 72 && window.FindControl<Border>("ModelsSidebar")!.Bounds.Width == 72, "Both sidebars collapse with room for the enlarged toggles");
            CheckInsideSidebar(window, "ToggleChatsButton", "ChatsSidebar");
            CheckInsideSidebar(window, "CollapsedNewChatButton", "ChatsSidebar");
            CheckInsideSidebar(window, "ToggleModelsButton", "ModelsSidebar");
            CheckInsideSidebar(window, "CollapsedSettingsButton", "ChatsSidebar");
            CheckCenteredToggleIcon(window, "ToggleChatsButton"); CheckCenteredToggleIcon(window, "ToggleModelsButton");
            CheckStableHover(window, window.FindControl<Button>("ToggleChatsButton")!);
            SaveFrame(window, "collapsed-hover");
            True(Math.Abs(window.FindControl<Button>("SettingsButton")!.TranslatePoint(default, window)!.Value.Y - footerPosition) < 1 &&
                window.FindControl<ListBox>("ConversationsList")!.IsVisible && !window.FindControl<ListBox>("ConversationsList")!.IsHitTestVisible,
                "Sidebar content retains layout during fades without remaining interactive");
            True(!window.FindControl<Button>("NewChatButton")!.IsEffectivelyEnabled && window.FindControl<Button>("CollapsedNewChatButton")!.IsEffectivelyEnabled,
                "Only the visible new-chat control is keyboard-accessible");
            var collapsedSettings = window.FindControl<Button>("CollapsedSettingsButton")!;
            True(collapsedSettings.IsVisible && collapsedSettings.IsEffectivelyEnabled && collapsedSettings.IsHitTestVisible &&
                !window.FindControl<Button>("SettingsButton")!.IsEffectivelyEnabled,
                "Collapsed settings are accessible while expanded settings are disabled");
            CheckStableHover(window, collapsedSettings);
            var settingsPoint = collapsedSettings.TranslatePoint(new Point(22, 22), window)!.Value;
            window.MouseDown(settingsPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(settingsPoint, MouseButton.Left, RawInputModifiers.None); SettleAnimations();
            var collapsedDialog = window.OwnedWindows.OfType<SettingsWindow>().Single();
            True(workspace.SettingsOpen && !workspace.ChatsSidebarOpen && workspace.Active == first,
                "Clicking the collapsed settings icon opens the same modal without expanding or changing chats");
            collapsedDialog.FindControl<ComboBox>("LanguageSelector")!.SelectedIndex = 1; Flush();
            True((ToolTip.GetTip(collapsedSettings) as string) == "Настройки", "Collapsed settings tooltip follows the selected language");
            collapsedDialog.FindControl<ComboBox>("LanguageSelector")!.SelectedIndex = 0; Flush();
            collapsedDialog.Close(); SettleAnimations();
            True(!workspace.SettingsOpen && !workspace.ChatsSidebarOpen && collapsedSettings.IsEffectivelyEnabled,
                "Closing settings restores the collapsed icon without expanding the sidebar");
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); SettleAnimations();
            True(!collapsedSettings.IsVisible && !collapsedSettings.IsEffectivelyEnabled, "Expanded sidebar has no hidden focusable settings icon");

            var settings = OpenSettings(window, workspace);
            True(settings.IsVisible && settings.Owner == window, "Settings open as an owned modal window");
            True(workspace.SettingsOpen && !window.FindControl<Grid>("WorkspaceSurface")!.IsHitTestVisible &&
                window.FindControl<Border>("SettingsBackdrop")!.Opacity > 0.99, "Settings dim and block background input, including scrolling");
            True(window.FindControl<ScrollViewer>("ChatScroll")!.VerticalScrollBarVisibility == Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                "Background chat scrollbar is hidden while settings are open");
            True(!settings.GetVisualDescendants().OfType<ScrollViewer>().Any(s => s.Content is StackPanel), "Settings have no page scroll container");
            True(window.FindControl<TextBox>("FolderBox")!.GetVisualAncestors().Contains(window.FindControl<Border>("ModelsSidebar")!), "Folder context selection is in the right sidebar");
            var languageSelector = settings.FindControl<ComboBox>("LanguageSelector")!;
            True(languageSelector.SelectedIndex == 0 && settings.FindControl<ComboBox>("ProfileSelector")!.SelectedIndex == 0, "Default selections render in settings");
            SaveFrame(settings, "settings");
            languageSelector.SelectedIndex = 1; Flush();
            True(workspace.LanguageIndex == 1 && vm.LanguageIndex == 1 && VisibleText(window, "Начните с вашей папки"), "Language selection applies to the workspace");
            languageSelector.SelectedIndex = 0; Flush();
            vm.ProfileIndex = 1; vm.CustomTerms = "cleanup-only-private-value";
            settings.Close(); SettleAnimations();
            True(!window.OwnedWindows.Any(), "Closing settings releases the owned dialog");

            vm.Folder = folderA;
            vm.PrepareCommand.Execute(null); Wait(vm);
            True(vm.Ready && !vm.ContextApproved && !vm.CanCompose, "Context preparation still requires review");
            True(window.GetVisualDescendants().OfType<Control>().Count(c => SmoothVisibility.GetMotion(c) == VisibilityMotion.View && c.IsVisible) == 1,
                "Switching documents never overlays outgoing and incoming text");
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
            settings.Close(); SettleAnimations();
            SaveFrame(window, "message-ru");
            workspace.LanguageIndex = 0;
            vm.SendCommand.Execute(null); Wait(vm);
            WaitTask(vm.TitleUpdate);
            True(vm.Chat.Count == 2 && generatedChats == 1 && vm.ChatConnectionState == ConnectionState.Connected, "Successful reply updates the assistant signal");
            True(first.Title == "Product launch plan" && generatedTitles == 1, "A completed chat is named for its goal, rather than copying the opening message");
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
            using var closingVm = new MainViewModel(closingClient, new ContextPreparationService(new FolderCollector(), closingClient), new ConversationTitleService(closingClient));
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
            WaitTask(secondVm.TitleUpdate);
            string secondPayload = Decode(payloads[^1]);
            True(!secondPayload.Contains("First chat task") && !secondPayload.Contains("Unsent first chat draft") && !secondPayload.Contains(firstPlaceholder), "Another chat's messages and placeholders stay out of requests");
            var menu = OpenRowMenu(window, first);
            True(menu.Items.OfType<MenuItem>().First().Command == first.ArchiveCommand, "Sidebar menu is bound to its own chat");
            menu.Items.OfType<MenuItem>().Last().Command!.Execute(null); menu.Hide(); SettleAnimations();
            True(workspace.DeleteConfirmationOpen && workspace.PendingDeleteTitle == first.Title && workspace.Active == second && workspace.Conversations.Count == 2,
                "An inactive chat with history opens confirmation without deleting or changing the active chat");
            True(window.FindControl<Border>("DeleteConfirmationDialog")!.IsVisible && !window.FindControl<Grid>("WorkspaceSurface")!.IsHitTestVisible &&
                window.FindControl<Border>("SettingsBackdrop")!.Opacity > 0.99 && !workspace.NewChatCommand.CanExecute(null) && !first.ArchiveCommand.CanExecute(null),
                "Deletion confirmation dims the background and blocks navigation and other row actions");
            True(!window.FindControl<TextBox>("FolderBox")!.IsEffectivelyEnabled && !window.FindControl<TextBox>("PromptBox")!.IsEffectivelyEnabled,
                "Background editors cannot receive keyboard input through the deletion modal");
            True(window.FocusManager!.GetFocusedElement() == window.FindControl<Button>("CancelDeleteButton"), "Cancel is initially focused to prevent accidental confirmation");
            for (int i = 0; i < 5; i++) { window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None); Flush(); }
            True(window.FocusManager.GetFocusedElement() is Control focused && focused.GetVisualAncestors().Contains(window.FindControl<Border>("DeleteConfirmationDialog")!),
                "Keyboard navigation stays within the confirmation modal");
            SaveFrame(window, "delete-confirmation");
            workspace.LanguageIndex = 1; Flush();
            True(VisibleText(window, "Удалить этот диалог?"), "Deletion confirmation supports Russian");
            workspace.LanguageIndex = 0;
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); SettleAnimations();
            True(!workspace.DeleteConfirmationOpen && workspace.Conversations.Count == 2 && workspace.Active == second && secondVm.Chat.Count == 2,
                "Escape cancels deletion and preserves both conversations");
            True(workspace.PendingDeleteTitle == first.Title && window.FocusManager.GetFocusedElement() is Control restoredFocus &&
                !restoredFocus.GetVisualAncestors().Contains(window.FindControl<Border>("DeleteConfirmationDialog")!),
                "Closing keeps the modal layout stable during its fade and returns focus to the workspace");
            menu = OpenRowMenu(window, first);
            menu.Items.OfType<MenuItem>().First().Command!.Execute(null); menu.Hide(); Flush();
            True(first.IsArchived && workspace.Active == second && secondVm.Chat.Count == 2, "Archiving an inactive chat preserves the active conversation");
            True(window.FindControl<Button>("ArchivesButton")!.IsVisible, "Archiving the first chat reveals the archive entry");
            workspace.ToggleArchiveViewCommand.Execute(null); Flush();
            menu = OpenRowMenu(window, first);
            True((string)menu.Items.OfType<MenuItem>().First().Header! == "Restore chat", "Archived rows offer restore");
            menu.Items.OfType<MenuItem>().First().Command!.Execute(null); menu.Hide(); Flush();
            True(!first.IsArchived && workspace.Active == first && !workspace.ShowingArchive, "Sidebar restore returns the active archived chat to recent chats");
            True(!window.FindControl<Button>("ArchivesButton")!.IsVisible, "Restoring the last archived chat hides the archive entry");
            workspace.NewChatCommand.Execute(null); Flush();
            var temporary = workspace.Active!; temporary.Session.Prompt = "Unsent disposable draft";
            workspace.Active = second; Flush();
            menu = OpenRowMenu(window, temporary);
            menu.Items.OfType<MenuItem>().Last().Command!.Execute(null); menu.Hide(); Flush();
            True(!workspace.DeleteConfirmationOpen && workspace.Active == second && !workspace.Conversations.Contains(temporary) && temporary.Session.Prompt == "" && !temporary.DeleteCommand.CanExecute(null),
                "Deleting an inactive chat from its menu releases only that chat and disables stale actions");
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
            settings.Close(); SettleAnimations();
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
            window.Width = 1000; window.Height = 720; SettleAnimations();
            SaveFrame(window, "compact");
            True(window.FindControl<TextBox>("PromptBox")!.Bounds.Width > 200, "Chat remains usable at minimum window size");
            var toggle = window.FindControl<Button>("ToggleChatsButton")!;
            True(toggle.TranslatePoint(new Point(toggle.Bounds.Width, 0), window.FindControl<Border>("ChatsSidebar")!)!.Value.X <= 240,
                "Chat sidebar toggle stays visible at minimum window width");
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); SettleAnimations();
            CheckInsideSidebar(window, "ToggleChatsButton", "ChatsSidebar"); CheckInsideSidebar(window, "ToggleModelsButton", "ModelsSidebar");
            CheckInsideSidebar(window, "CollapsedSettingsButton", "ChatsSidebar");
            CheckCenteredToggleIcon(window, "ToggleChatsButton"); CheckCenteredToggleIcon(window, "ToggleModelsButton");
            workspace.ToggleChatsCommand.Execute(null); workspace.ToggleModelsCommand.Execute(null); SettleAnimations();
            CheckPersistence(fixture, folderB);
            SidebarStateChecks.Run(fixture);
            CheckTitleLifecycle(folderB);
            Console.WriteLine("PASS UI: centered chevron drawings, collapsed settings access, constant hover/pressed colors, fully contained collapsed toggles, deletion confirmation, hidden empty archive, reversible motion, isolated sessions, persistence and privacy review.");
            Console.WriteLine("Synthetic screenshots: artifacts/screenshots/{mvp-design,start,sidebar-motion,collapsed,collapsed-hover,settings,delete-confirmation,context,message-ru,chat,chats,archive,compact}.png");
            return 0;
        }
        catch (Exception e) { Console.WriteLine("FAIL UI: " + e.Message); return 1; }
        finally
        {
            window.Close(); SettleAnimations();
            string resolved = Path.GetFullPath(fixture);
            if (Path.GetDirectoryName(resolved) == fixtureRoot && Path.GetFileName(resolved).StartsWith("ui-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
        }
    }

    static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    static void CheckCenteredToggleIcon(Window window, string buttonName)
    {
        var button = window.FindControl<Button>(buttonName)!;
        var icon = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.IsVisible);
        var center = icon.TranslatePoint(new Point(icon.Bounds.Width / 2, icon.Bounds.Height / 2), button)!.Value;
        True(Math.Abs(center.X - button.Bounds.Width / 2) < 0.5 && Math.Abs(center.Y - button.Bounds.Height / 2) < 0.5,
            buttonName + " icon drawing area is centered in its button");
        var geometry = icon.Data!.Bounds;
        True(Math.Abs(icon.Bounds.Width / icon.Bounds.Height - geometry.Width / geometry.Height) < 0.01,
            buttonName + " drawing area matches the geometry aspect ratio, without unused horizontal space");
    }
    static void CheckInsideSidebar(Window window, string buttonName, string sidebarName)
    {
        var button = window.FindControl<Button>(buttonName)!;
        var sidebar = window.FindControl<Border>(sidebarName)!;
        var position = button.TranslatePoint(default, sidebar)!.Value;
        True(position.X >= 10 && position.X + button.Bounds.Width <= sidebar.Bounds.Width - 10,
            buttonName + " and its entire hover surface stay inside the collapsed sidebar: x=" + position.X + ", width=" + button.Bounds.Width);
        True(Math.Abs(position.X + button.Bounds.Width / 2 - sidebar.Bounds.Width / 2) < 1,
            buttonName + " is centered in the collapsed sidebar");
    }
    static void CheckStableHover(Window window, Button button)
    {
        var surface = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ButtonSurface");
        var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var expected = Color.Parse("#183047");
        void AssertColor() => True(surface.Background is ISolidColorBrush brush && brush.Color == expected,
            "Hovered/pressed " + button.Name + " retains a single color without intermediate bright frames");
        window.MouseMove(center, RawInputModifiers.None); Flush(); AssertColor();
        SettleAnimations(100); AssertColor(); SettleAnimations(550); AssertColor();
        window.MouseMove(center + new Vector(3, 2), RawInputModifiers.None); Flush(); AssertColor();
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None); Flush(); AssertColor();
        var outside = new Point(window.Width / 2, 10);
        window.MouseMove(outside, RawInputModifiers.None); window.MouseUp(outside, MouseButton.Left, RawInputModifiers.None); Flush();
        window.MouseMove(center, RawInputModifiers.None); Flush(); AssertColor();
    }
    static MenuFlyout OpenRowMenu(Window window, ConversationViewModel chat)
    {
        Flush(); window.UpdateLayout();
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ChatRowMenuButton" && b.DataContext == chat);
        var menu = (MenuFlyout)button.Flyout!; menu.ShowAt(button); Flush();
        True(menu.Items.OfType<MenuItem>().All(item => item.Command is not null), "Chat menu commands resolve through the flyout");
        return menu;
    }
    static void CheckTitleLifecycle(string sourceFolder)
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>();
        CancellationToken titleToken = default;
        using var workspace = new WorkspaceViewModel(() => new ModelClient(new StubHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/show") return Response("{\"model_info\":{\"general.architecture\":\"llama\"}}");
            string body = await request.Content!.ReadAsStringAsync(ct);
            if (body.Contains("Create a short chat title")) { titleToken = ct; return await completion.Task; }
            return Response("{\"message\":{\"content\":\"Completed safe reply\"},\"done\":true}\n");
        })));
        var first = workspace.Active!; var vm = first.Session;
        vm.Folder = sourceFolder; vm.LocalModel = "demo-local"; vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true;
        void Send()
        {
            vm.Prompt = "Please help me plan a product launch"; vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            True(vm.Chat.Count == 2 && !vm.TitleUpdate.IsCompleted && !vm.Busy, "Naming runs in the background after a complete reply");
        }
        Send(); vm.NewChatCommand.Execute(null);
        completion.SetResult(Response("{\"message\":{\"content\":\"Late name after reset\"}}")); WaitTask(vm.TitleUpdate);
        True(titleToken.IsCancellationRequested && vm.ChatTitle == "" && vm.Chat.Count == 0, "Late title cannot restore a cleared conversation");
        completion = new TaskCompletionSource<HttpResponseMessage>(); Send();
        workspace.NewChatCommand.Execute(null); var second = workspace.Active!; second.Session.Prompt = "Keep this draft";
        first.DeleteCommand.Execute(null);
        True(workspace.DeleteConfirmationOpen && workspace.Conversations.Contains(first), "Pending title does not bypass deletion confirmation");
        workspace.ConfirmDeleteChatCommand.Execute(null);
        completion.SetResult(Response("{\"message\":{\"content\":\"Late name after delete\"}}")); WaitTask(vm.TitleUpdate);
        True(titleToken.IsCancellationRequested && vm.ChatTitle == "" && workspace.Active == second && second.Session.Prompt == "Keep this draft" && workspace.Conversations.Count == 1,
            "Late title cannot recreate a deleted chat or alter the active chat");
    }
    static void CheckPersistence(string fixture, string sourceFolder)
    {
        string storageFolder = Path.Combine(fixture, "saved-chats"), exportFolder = Path.Combine(fixture, "exports");
        Func<ModelClient> clients = () => new ModelClient(new StubHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/show") return Response("{\"model_info\":{\"general.architecture\":\"llama\"}}");
            string payload = await request.Content!.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(payload);
            if (json.RootElement.TryGetProperty("format", out _)) return Response("{\"message\":{\"content\":\"{\\\"entities\\\":[]}\"}}");
            if (payload.Contains("Create a short chat title")) return Response("{\"message\":{\"content\":\"Discussion with second@example.com\"}}");
            string reply = payload.Contains("Continue the discussion") ? "Continuing the safe discussion" : "Contact second@example.com; disk-only-key; DiskPrivateProject";
            return Response(JsonSerializer.Serialize(new { message = new { content = reply }, done = true }) + "\n");
        }));
        Guid id;
        using (var original = new WorkspaceViewModel(clients, new ChatArchiveService(new JsonDirectoryChatStore(storageFolder))))
        {
            WaitTask(original.InitializeAsync());
            True(original.Active!.Number == 1, "An empty store starts with Chat 1");
            var vm = original.Active!.Session; id = original.Active.Id;
            vm.LocalModel = "demo-local"; vm.ApiKey = "disk-only-key"; vm.CustomTerms = "DiskPrivateProject";
            vm.Folder = sourceFolder; vm.PrepareCommand.Execute(null); Wait(vm); vm.ContextApproved = true;
            vm.RestoreLocally = true; vm.Prompt = "Discuss DiskPrivateProject with second@example.com";
            vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            WaitTask(vm.TitleUpdate);
            string savedTitle = original.Active.Title;
            True(!savedTitle.Contains("second@example.com") && savedTitle.StartsWith("Discussion"), "Generated title is sanitized again before display and saving");
            True(vm.Chat[1].Text.Contains("second@example.com"), "Local display can contain restored originals");
            vm.Prompt = "disk-only-unsent-draft";
            WaitTask(original.FlushStorageAsync());
            string json = File.ReadAllText(Directory.GetFiles(storageFolder, "*.json").Single());
            foreach (string secret in new[] { "second@example.com", "second-sensitive-password", "disk-only-key", "DiskPrivateProject", "disk-only-unsent-draft", "ApiKey", "LocalRelativePath", "ContextApproved" })
                True(!json.Contains(secret), "Storage must exclude secrets, original paths, keys, drafts and approval");
            original.PickExportFolderAsync = () => Task.FromResult<string?>(exportFolder);
            original.ExportChatCommand.Execute(null); WaitWorkspace(original);
            True(!original.StorageFailed && Directory.GetFiles(exportFolder).Length == 2, "Export writes JSON and Markdown");
            foreach (string file in Directory.GetFiles(exportFolder))
                True(!File.ReadAllText(file).Contains("second@example.com"), "Export excludes restored display values");
            original.ArchiveChatCommand.Execute(null); WaitTask(original.FlushStorageAsync());
            var closingWindow = new MainWindow(original); closingWindow.Show(); Flush(); closingWindow.Close();
            SettleAnimations(); True(!closingWindow.IsVisible, "Normal window closure flushes storage and releases the window");
        }
        using (var reopened = new WorkspaceViewModel(clients, new ChatArchiveService(new JsonDirectoryChatStore(storageFolder))))
        {
            WaitTask(reopened.InitializeAsync());
            True(reopened.ArchivedCount == 1, "Archive survives application restart");
            reopened.ToggleArchiveViewCommand.Execute(null);
            var vm = reopened.Active!.Session;
            True(reopened.Active.Id == id && vm.Chat.Count == 2 && vm.Ready && !vm.ContextApproved && vm.ApiKey == "" && vm.Folder == "" && vm.CustomTerms == "" && vm.Replacements.Count == 0,
                "Reopening restores safe chat and context, with no original configuration, mapping or approval");
            True(reopened.Active.Title == "Discussion with", "Goal-derived title survives restart");
            vm.LocalModel = "demo-local"; True(vm.Chat.Count == 2, "Initial model setup preserves reopened history");
            vm.UseAgent = true; vm.AgentModel = "local-cleaner";
            True(vm.Chat.Count == 2 && vm.Ready && !vm.ContextApproved, "Initial cleaner setup preserves reopened history and requires review");
            vm.ContextApproved = true; vm.Prompt = "Continue the discussion";
            vm.PreviewPromptCommand.Execute(null); Wait(vm); vm.SendCommand.Execute(null); Wait(vm);
            True(vm.Chat.Count == 4, "A reviewed reopened chat can continue its conversation");
            reopened.DeleteChatCommand.Execute(null); WaitTask(reopened.FlushStorageAsync());
            True(reopened.DeleteConfirmationOpen && File.Exists(Path.Combine(storageFolder, id.ToString("N") + ".json")), "Opening confirmation does not delete the stored transcript");
            reopened.CancelDeleteChatCommand.Execute(null);
            reopened.DeleteChatCommand.Execute(null); reopened.ConfirmDeleteChatCommand.Execute(null); WaitTask(reopened.FlushStorageAsync());
            True(!File.Exists(Path.Combine(storageFolder, id.ToString("N") + ".json")), "Deleting removes the persisted chat");
            reopened.ClearAllCommand.Execute(null); WaitTask(reopened.FlushStorageAsync());
            True(new JsonDirectoryChatStore(storageFolder).LoadAsync(default).GetAwaiter().GetResult().All(chat => chat.Messages.Count == 0 && !chat.Archived),
                "Clear all also removes persisted archived transcripts");
        }
    }
    static void WaitTask(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Flush(); Thread.Sleep(10); }
        True(task.IsCompleted, "Storage operation timed out"); task.GetAwaiter().GetResult(); Flush();
    }
    static void WaitWorkspace(WorkspaceViewModel workspace)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (workspace.IsBusy && DateTime.UtcNow < deadline) { Flush(); Thread.Sleep(10); }
        True(!workspace.IsBusy, "Workspace operation timed out"); Flush();
    }
    static string Decode(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        return string.Join("\n", json.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("content").GetString()));
    }
    static SettingsWindow OpenSettings(Window window, WorkspaceViewModel workspace)
    {
        workspace.SettingsCommand.Execute(null); Flush();
        SettleAnimations();
        return window.OwnedWindows.OfType<SettingsWindow>().Single();
    }
    static void Flush() { Dispatcher.UIThread.RunJobs(); }
    static void SettleAnimations(int milliseconds = 650)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline) { Flush(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); }
        Flush();
    }
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
