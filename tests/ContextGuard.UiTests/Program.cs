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
        AppBuilder.Configure<ContextGuard.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().WithInterFont().SetupWithoutStarting();
        string fixtureRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".test-work"));
        string fixture = Path.Combine(fixtureRoot, "ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "example.md"), "# Project\nA useful project with contact user@example.com\npassword=demo-sensitive-password\n");
        int networkCalls = 0;
        int generatedChats = 0;
        using var client = new ModelClient(new StubHandler(request =>
        {
            networkCalls++;
            string data;
            if (request.RequestUri!.AbsolutePath == "/api/show") data = "{\"model_info\":{\"general.architecture\":\"llama\"}}";
            else
            {
                string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("format", out _))
                {
                    string text = doc.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
                    // Only recognise this name in the edited message, not in the initial file.
                    string[] entities = text.StartsWith("Edited:", StringComparison.Ordinal) && text.Contains("PrivateOrganization") ? new[] { "PrivateOrganization" } : Array.Empty<string>();
                    data = JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(new { entities }) } });
                }
                else { generatedChats++; data = "{\"message\":{\"content\":\"Here is a project overview.\"},\"done\":true}\n"; }
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") };
        }));
        using var vm = new MainViewModel(client);
        var window = new MainWindow();
        (window.DataContext as MainViewModel)?.Dispose();
        window.DataContext = vm;
        window.Show();
        try
        {
            Flush();
            True(!vm.CanCompose, "Composer is locked before context preparation");
            var languageSelector = window.FindControl<ComboBox>("LanguageSelector") ?? throw new Exception("Language selector missing");
            True(vm.LanguageIndex == 0 && languageSelector.SelectedIndex == 0, "English is selected by default");
            True(vm.ProfileIndex == 0 && vm.ProviderIndex == 0 && window.FindControl<ComboBox>("ProfileSelector")!.SelectedIndex == 0 && window.FindControl<ComboBox>("ProviderSelector")!.SelectedIndex == 0, "Profile and provider defaults appear in the selectors");
            True(VisibleText(window, "Start with your folder"), "Welcome screen is in English");
            True(window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Prepare context  →")), "Action captions are translated");
            SaveFrame(window, "start");
            vm.ProviderIndex = 1;
            vm.ApiModel = "demo-remote";
            vm.ApiEndpoint = "https://api.example.com/v1/chat/completions";
            languageSelector.SelectedIndex = 1; Flush();
            True(vm.LanguageIndex == 1 && vm.ProviderIndex == 1 && vm.ApiModel == "demo-remote", "Switching language preserves remote model settings");
            True(VisibleText(window, "Начните с вашей папки"), "Russian labels render through the language selector");
            languageSelector.SelectedIndex = 0; Flush();
            vm.ProviderIndex = 0;
            vm.ProfileIndex = 1;
            vm.Folder = fixture;
            vm.PrepareCommand.Execute(null); Wait(vm);
            True(vm.Ready && !vm.ContextApproved && !vm.CanCompose, "Prepared context must be reviewed");
            True(!vm.ContextText.Contains("user@example.com") && !vm.ContextText.Contains("demo-sensitive-password"), "Safe preview");
            True(networkCalls == 0, "Local rule-based preparation has no network calls");
            SaveFrame(window, "context");
            vm.ContextApproved = true;
            True(!vm.CanCompose, "Model selection is required before chat activates");
            vm.LocalModel = "demo-local";
            True(vm.CanCompose, "Review unlocks composer");
            vm.SelectedTab = 0;
            vm.Prompt = "Explain this project. My email: another@example.com";
            vm.PreviewPromptCommand.Execute(null); Wait(vm);
            True(vm.HasPending && !vm.PendingText.Contains("another@example.com"), "Chat preview masks contact data");
            True(networkCalls == 0, "Preview does not send to main model");
            SaveFrame(window, "message");
            string reviewedContext = vm.ContextText, reviewedMessage = vm.PendingText, draft = vm.Prompt;
            string[] aliases = vm.Files.Select(file => file.Alias).ToArray();
            string[] placeholders = vm.Replacements.Select(replacement => replacement.Placeholder).ToArray();
            languageSelector.SelectedIndex = 1; Flush();
            True(vm.ContextApproved && vm.CanCompose && vm.HasPending && vm.ContextText == reviewedContext && vm.PendingText == reviewedMessage && vm.Prompt == draft,
                "Language switching preserves context approval, pending message and draft");
            True(vm.ProfileIndex == 1 && vm.ProviderIndex == 0 && vm.LocalModel == "demo-local", "Language switching preserves profile and model choices");
            True(vm.Files.Select(file => file.Alias).SequenceEqual(aliases) && vm.Replacements.Select(replacement => replacement.Placeholder).SequenceEqual(placeholders),
                "File aliases and replacement map survive language switching");
            True(VisibleText(window, "Начните с вашей папки") && vm.Status.StartsWith("Проверьте сообщение"), "Both labels and status are translated");
            True(vm.Summary.Contains("файлов") && vm.CountSummary.StartsWith("Замен:"), "Dynamic summaries are translated");
            SaveFrame(window, "message-ru");
            languageSelector.SelectedIndex = 0; Flush();
            True(VisibleText(window, "Start with your folder") && vm.Status.StartsWith("Review the message"), "Switching back restores English");
            vm.Prompt = "Explain the project structure";
            True(!vm.HasPending, "Editing prompt invalidates preview");
            vm.PreviewPromptCommand.Execute(null); Wait(vm);
            vm.SendCommand.Execute(null); Wait(vm);
            True(vm.Chat.Count == 2 && !vm.HasError, "Complete chat response appears");
            True(networkCalls == 2, "Only model verification and explicit chat use network");
            SaveFrame(window, "chat");
            string reply = vm.Chat[1].Text;
            languageSelector.SelectedIndex = 1; Flush();
            True(vm.Chat.Count == 2 && vm.Chat[0].Label == "ВЫ" && vm.Chat[1].Text == reply && vm.ContextApproved,
                "Language switching updates chat labels without translating replies or clearing history");
            languageSelector.SelectedIndex = 0; Flush();
            True(vm.Chat[0].Label == "YOU" && vm.Chat[1].Text == reply, "English chat labels are restored");
            vm.ContextText += "\nReviewed context edited";
            True(!vm.ContextApproved && !vm.CanCompose && vm.Chat.Count == 0, "Context edits revoke review and clear conversation");
            vm.CustomTerms = "CompanyName";
            True(!vm.Ready && vm.ContextText.Length == 0, "Rule edits invalidate prepared context");
            // Edits made directly to the preview must go through the local agent again.
            File.WriteAllText(Path.Combine(fixture, "example.md"), "PrivateOrganization builds this project.");
            vm.UseAgent = true; vm.AgentModel = "local-detector";
            vm.PrepareCommand.Execute(null); Wait(vm);
            vm.ContextApproved = true; vm.Prompt = "Explain the project";
            vm.PreviewPromptCommand.Execute(null); Wait(vm);
            vm.PendingText = "Edited: explain PrivateOrganization";
            vm.SendCommand.Execute(null); Wait(vm);
            True(!vm.ContextApproved && vm.SelectedTab == 1 && !vm.ContextText.Contains("PrivateOrganization"), "Edited preview entities force another context review");
            True(generatedChats == 1, "No new main-model request before updated context review");
            vm.ContextApproved = true;
            vm.Prompt = new string('x', 16_001);
            vm.PreviewPromptCommand.Execute(null); Wait(vm, expectError: true);
            True(vm.Error.StartsWith("The message is too long"), "Validation errors are in English by default");
            languageSelector.SelectedIndex = 1; Flush();
            True(vm.Error.StartsWith("Сообщение слишком длинное") && vm.Status == "Операция остановлена.", "Existing errors and status update in Russian");
            languageSelector.SelectedIndex = 0; Flush();
            True(vm.Error.StartsWith("The message is too long"), "Existing errors update back to English");
            vm.DismissErrorCommand.Execute(null);
            vm.ApiKey = "test-key";
            vm.ClearSessionCommand.Execute(null);
            True(vm.ApiKey == "" && !vm.Ready, "Clear session removes credential and context references");
            vm.SelectedTab = 0; Flush();
            True(window.GetVisualDescendants().OfType<TextBox>().Any(t => t.Name == "PromptBox"), "Chat control renders");
            Console.WriteLine("PASS UI: preparation, review, chat, session cleanup, English defaults and Russian/English switching without data loss.");
            Console.WriteLine("Rendered frames: artifacts/screenshots/{start,context,message,message-ru,chat}.png");
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

    static void Flush() { Dispatcher.UIThread.RunJobs(); }
    static bool VisibleText(Window window, string text) => window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text);
    static void Wait(MainViewModel vm, bool expectError = false)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (vm.Busy && DateTime.UtcNow < deadline) { Flush(); Thread.Sleep(10); }
        Flush();
        True(!vm.Busy, "Operation timed out");
        True(vm.HasError == expectError, "Unexpected operation result: " + vm.Error);
    }
    static void SaveFrame(Window window, string name)
    {
        Flush();
        string directory = Path.Combine(Environment.CurrentDirectory, "artifacts", "screenshots");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        if (frame is null) throw new Exception("Could not render frame");
        frame.Save(Path.Combine(directory, name + ".png"));
    }
    static void True(bool value, string message) { if (!value) throw new Exception(message); }
}

sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(action(request));
}
