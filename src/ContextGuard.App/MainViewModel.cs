using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Avalonia.Threading;
using ContextGuard.Core;

namespace ContextGuard.App;

public class Bindable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value; Notify(name); return true;
    }
}

public sealed class ChatRow : Bindable
{
    private string label;
    public string Label { get => label; set => Set(ref label, value); }
    public bool IsAssistant { get; }
    private string text;
    public string Text { get => text; set => Set(ref text, value); }
    public string Color { get; }
    public ChatRow(string label, string text, bool assistant = false)
    { this.label = label; this.text = text; IsAssistant = assistant; Color = assistant ? "#A995E3" : "#71D6B3"; }
}

public sealed class LocalizedOption : Bindable
{
    public string Key { get; }
    private string label;
    public string Label { get => label; private set => Set(ref label, value); }
    public LocalizedOption(string key) { Key = key; label = new LocalizedText()[key]; }
    public void Update(LocalizedText text) => Label = text[Key];
}

public sealed class RelayCommand : ICommand
{
    private readonly Action action;
    private readonly Func<bool> allowed;
    public RelayCommand(Action action, Func<bool>? allowed = null) { this.action = action; this.allowed = allowed ?? (() => true); }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => allowed();
    public void Execute(object? parameter) { if (allowed()) action(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class MainViewModel : Bindable, IDisposable
{
    private readonly ModelClient client;
    private readonly List<ChatMessage> history = new();
    private RedactionSession? session;
    private ScanSummary? scan;
    private CancellationTokenSource? operation;
    private readonly List<RelayCommand> commands = new();
    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public event Action? ScrollChat;

    public ObservableCollection<ChatRow> Chat { get; } = new();
    public ObservableCollection<string> LocalModels { get; } = new();
    public ObservableCollection<ReplacementInfo> Replacements { get; } = new();
    public ObservableCollection<SafeFile> Files { get; } = new();
    public IReadOnlyList<LocalizedOption> Profiles { get; } = [new("ProfileAll"), new("ProfilePersonal"), new("ProfileInternal"), new("ProfileSecrets")];
    public IReadOnlyList<LocalizedOption> Providers { get; } = [new("ProviderOllama"), new("ProviderApi")];
    public string[] Languages { get; } = ["English", "Русский"];
    public LocalizedText Text { get; private set; } = new();
    private int languageIndex;
    public int LanguageIndex
    {
        get => languageIndex;
        set
        {
            if (value is < 0 or > 1 || !Set(ref languageIndex, value)) return;
            Text = new((AppLanguage)value);
            Notify(nameof(Text)); Notify(nameof(Status)); Notify(nameof(Error));
            Notify(nameof(Summary)); Notify(nameof(CountSummary));
            foreach (var option in Profiles.Concat(Providers)) option.Update(Text);
            foreach (var row in Chat.Where(row => !row.IsAssistant)) row.Label = Text["You"];
            UpdateReplacements();
        }
    }

    private string folder = "";
    public string Folder { get => folder; set { if (Set(ref folder, value)) InvalidateContext(); } }
    private int profileIndex;
    public int ProfileIndex
    {
        get => profileIndex;
        set { if (value is >= 0 and <= 3 && Set(ref profileIndex, value)) { Notify(nameof(SelectedProfile)); InvalidateContext(); } }
    }
    public LocalizedOption? SelectedProfile
    {
        get => Profiles[ProfileIndex];
        set { if (value is not null) { int index = Array.IndexOf(Profiles.ToArray(), value); if (index >= 0) ProfileIndex = index; } }
    }
    private string customTerms = "";
    public string CustomTerms { get => customTerms; set { if (Set(ref customTerms, value)) InvalidateContext(); } }
    private bool useAgent;
    public bool UseAgent { get => useAgent; set { if (Set(ref useAgent, value)) InvalidateContext(); } }
    private string agentEndpoint = "http://localhost:11434";
    public string AgentEndpoint { get => agentEndpoint; set { if (Set(ref agentEndpoint, value)) InvalidateContext(); } }
    private string agentModel = "";
    public string AgentModel { get => agentModel; set { if (Set(ref agentModel, value)) InvalidateContext(); } }
    private int contextLimit = 24000;
    public int ContextLimit { get => contextLimit; set { if (Set(ref contextLimit, Math.Clamp(value, 4000, FolderCollector.MaxContextChars))) InvalidateContext(); } }
    private int providerIndex;
    public int ProviderIndex
    {
        get => providerIndex;
        set { if (value is >= 0 and <= 1 && Set(ref providerIndex, value)) { Notify(nameof(SelectedProvider)); ResetConversation(); Notify(nameof(IsOllama)); Notify(nameof(IsRemote)); Refresh(); } }
    }
    public LocalizedOption? SelectedProvider
    {
        get => Providers[ProviderIndex];
        set { if (value is not null) { int index = Array.IndexOf(Providers.ToArray(), value); if (index >= 0) ProviderIndex = index; } }
    }
    public bool IsOllama => ProviderIndex == 0;
    public bool IsRemote => !IsOllama;
    private string ollamaEndpoint = "http://localhost:11434";
    public string OllamaEndpoint { get => ollamaEndpoint; set { if (Set(ref ollamaEndpoint, value)) ResetConversation(); } }
    private string localModel = "";
    public string LocalModel { get => localModel; set { if (Set(ref localModel, value)) ResetConversation(); } }
    private string apiEndpoint = "";
    public string ApiEndpoint { get => apiEndpoint; set { if (Set(ref apiEndpoint, value)) ResetConversation(); } }
    private string apiModel = "";
    public string ApiModel { get => apiModel; set { if (Set(ref apiModel, value)) ResetConversation(); } }
    private string apiKey = "";
    public string ApiKey
    {
        get => apiKey;
        set
        {
            if (!Set(ref apiKey, value)) return;
            ResetConversation();
            if (session is not null && !string.IsNullOrWhiteSpace(value))
            {
                session.AddEntity(value, "SECRET");
                string updated = session.Sanitize(ContextText);
                if (updated != ContextText) ContextText = updated;
                UpdateReplacements();
            }
        }
    }
    private string contextText = "";
    public string ContextText
    {
        get => contextText;
        set
        {
            if (!Set(ref contextText, value)) return;
            ContextApproved = false;
            ResetConversation();
        }
    }
    private bool contextApproved;
    public bool ContextApproved
    {
        get => contextApproved;
        set
        {
            bool actual = value && session is not null && scan is not null && !string.IsNullOrWhiteSpace(ContextText);
            if (Set(ref contextApproved, actual))
            {
                ClearPending(); Refresh();
                if (actual) SetStatus(ModelConfigured ? "StatusReviewed" : "StatusSelectModel");
            }
        }
    }
    private string prompt = "";
    public string Prompt { get => prompt; set { if (Set(ref prompt, value)) { ClearPending(); Refresh(); } } }
    private string pendingText = "";
    public string PendingText { get => pendingText; set { if (Set(ref pendingText, value)) Refresh(); } }
    private bool hasPending;
    public bool HasPending { get => hasPending; private set { if (Set(ref hasPending, value)) Refresh(); } }
    private bool busy;
    public bool Busy { get => busy; private set { if (Set(ref busy, value)) { Notify(nameof(NotBusy)); Refresh(); } } }
    public bool NotBusy => !Busy;
    public bool Ready => session is not null && scan is not null;
    public bool CanReview => Ready && !Busy;
    public bool ModelConfigured => !string.IsNullOrWhiteSpace(IsOllama ? LocalModel : ApiModel) && !string.IsNullOrWhiteSpace(IsOllama ? OllamaEndpoint : ApiEndpoint);
    public bool CanCompose => Ready && ContextApproved && ModelConfigured && !Busy;
    public bool HasChat => Chat.Count > 0;
    private string statusKey = "StatusInitial";
    private object[] statusArguments = [];
    public string Status => Text.Format(statusKey, statusArguments);
    private void SetStatus(string key, params object[] arguments)
    { statusKey = key; statusArguments = arguments; Notify(nameof(Status)); }
    private SafeOperationException? error;
    public string Error => error?.GetLocalizedMessage(Text) ?? "";
    private void SetError(SafeOperationException? value)
    { error = value; Notify(nameof(Error)); Notify(nameof(HasError)); }
    public bool HasError => error is not null;
    public string Summary => scan is null ? Text["ContextNotReady"] :
        Text.Format("ScanSummary", scan.Included, scan.Characters, scan.Skipped) + (scan.LimitReached ? Text["LimitReached"] : "");
    public string CountSummary => Text.Format("ReplacementCount", session?.Count ?? 0);
    private int selectedTab;
    public int SelectedTab { get => selectedTab; set => Set(ref selectedTab, value); }
    private bool restoreLocally;
    public bool RestoreLocally { get => restoreLocally; set { if (Set(ref restoreLocally, value)) UpdateDisplayedAnswers(); } }
    private readonly Dictionary<ChatRow, string> safeAnswers = new();

    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshModelsCommand { get; }
    public RelayCommand PrepareCommand { get; }
    public RelayCommand PreviewPromptCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public RelayCommand EditPromptCommand { get; }
    public RelayCommand ClearSessionCommand { get; }
    public RelayCommand DismissErrorCommand { get; }

    public MainViewModel(ModelClient? client = null)
    {
        this.client = client ?? new ModelClient();
        BrowseCommand = Command(() => RunAsync(async ct => { string? picked = PickFolderAsync is null ? null : await PickFolderAsync(); if (picked is not null) Folder = picked; }), () => !Busy);
        RefreshModelsCommand = Command(() => RunAsync(RefreshModelsAsync), () => !Busy);
        PrepareCommand = Command(() => RunAsync(PrepareAsync), () => !Busy && !string.IsNullOrWhiteSpace(Folder));
        PreviewPromptCommand = Command(() => RunAsync(PreviewPromptAsync), () => CanCompose && !string.IsNullOrWhiteSpace(Prompt) && !HasPending);
        SendCommand = Command(() => RunAsync(SendAsync), () => CanCompose && HasPending && !string.IsNullOrWhiteSpace(PendingText));
        CancelCommand = Command(() => operation?.Cancel(), () => Busy);
        NewChatCommand = Command(() => { ResetConversation(); SetStatus("StatusNewChat"); }, () => !Busy && Ready);
        EditPromptCommand = Command(ClearPending, () => !Busy && HasPending);
        ClearSessionCommand = Command(() => { InvalidateContext(); Prompt = ""; ApiKey = ""; SetStatus("StatusCleared"); }, () => !Busy);
        DismissErrorCommand = Command(() => SetError(null));
    }

    private RelayCommand Command(Action action, Func<bool>? canExecute = null)
    { var result = new RelayCommand(action, canExecute); commands.Add(result); return result; }
    private void Refresh()
    {
        Notify(nameof(Ready)); Notify(nameof(CanReview)); Notify(nameof(ModelConfigured)); Notify(nameof(CanCompose)); Notify(nameof(HasChat));
        foreach (var command in commands) command.Refresh();
    }
    private ProtectionOptions Options() => new((ProtectionProfile)Math.Clamp(ProfileIndex, 0, 3),
        CustomTerms.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private ModelSettings Settings() => IsOllama ? new(ModelProvider.Ollama, OllamaEndpoint, LocalModel) : new(ModelProvider.RemoteApi, ApiEndpoint, ApiModel, ApiKey);
    private void ClearPending() { PendingText = ""; HasPending = false; }
    private void ResetConversation()
    {
        ClearPending(); history.Clear(); Chat.Clear(); safeAnswers.Clear(); Refresh();
    }
    private void InvalidateContext()
    {
        session = null; scan = null; ContextApproved = false;
        if (contextText.Length != 0) { contextText = ""; Notify(nameof(ContextText)); }
        Replacements.Clear(); Files.Clear(); Notify(nameof(CountSummary)); Notify(nameof(Summary));
        ResetConversation(); Refresh();
    }
    private void UpdateReplacements()
    {
        Replacements.Clear();
        if (session is not null) foreach (var replacement in session.Replacements)
            Replacements.Add(replacement with { Category = Text["Category_" + replacement.Category] });
        Notify(nameof(CountSummary));
    }
    private void UpdateDisplayedAnswers()
    {
        foreach (var pair in safeAnswers) pair.Key.Text = RestoreLocally && session is not null ? session.RestoreForLocalDisplay(pair.Value) : pair.Value;
    }

    private async void RunAsync(Func<CancellationToken, Task> action)
    {
        if (Busy) return;
        Busy = true; SetError(null); operation = new();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { SetStatus("StatusCancelled"); }
        catch (SafeOperationException e) { SetError(e); SetStatus("StatusStopped"); }
        catch { SetError(new SafeOperationException("UnexpectedError")); SetStatus("StatusStopped"); }
        finally { operation.Dispose(); operation = null; Busy = false; UpdateReplacements(); }
    }

    private async Task RefreshModelsAsync(CancellationToken ct)
    {
        SetStatus("StatusCheckingOllama");
        string endpoint = IsOllama ? OllamaEndpoint : AgentEndpoint;
        var models = await client.ListLocalModelsAsync(endpoint, ct);
        LocalModels.Clear(); foreach (string model in models) LocalModels.Add(model);
        if (string.IsNullOrWhiteSpace(LocalModel) && models.Count > 0) LocalModel = models[0];
        if (string.IsNullOrWhiteSpace(AgentModel) && models.Count > 0) AgentModel = models[0];
        SetStatus(models.Count > 0 ? "StatusModelsFound" : "StatusNoModels", models.Count);
    }

    private async Task PrepareAsync(CancellationToken ct)
    {
        InvalidateContext();
        var options = Options();
        var nextSession = new RedactionSession(options);
        if (!string.IsNullOrWhiteSpace(ApiKey)) nextSession.AddEntity(ApiKey, "SECRET");
        if (UseAgent) await client.EnsureLocalModelAsync(AgentEndpoint, AgentModel, ct);
        SetStatus(UseAgent ? "StatusAgentWorking" : "StatusCollecting");
        var progress = new Progress<int>(number => SetStatus("ProcessingFile", number));
        // CPU scanning does not block the UI thread.
        var nextScan = await Task.Run(() => new FolderCollector().CollectAsync(Folder, nextSession,
            UseAgent ? (text, token) => client.DiscoverEntitiesAsync(text, options, nextSession, AgentEndpoint, AgentModel, token) : null,
            progress, ct, ContextLimit), ct);
        ct.ThrowIfCancellationRequested();
        if (nextScan.Included == 0) throw new SafeOperationException("NoTextFiles");
        session = nextSession; scan = nextScan;
        Files.Clear(); foreach (var file in nextScan.Files) Files.Add(file);
        ContextText = nextScan.Context;
        Notify(nameof(Summary));
        SelectedTab = 1; UpdateReplacements(); Refresh();
        SetStatus("StatusReviewContext");
    }

    private async Task PreviewPromptAsync(CancellationToken ct)
    {
        ModelClient.ValidateSettings(Settings());
        if (Prompt.Length > 16_000) throw new SafeOperationException("PromptTooLong");
        SetStatus("StatusAnonymizingPrompt");
        if (UseAgent) await client.DiscoverEntitiesAsync(Prompt, Options(), session!, AgentEndpoint, AgentModel, ct);
        string safe = session!.Sanitize(Prompt);
        // Newly found entities may invalidate the old reviewed context. Ask for another review.
        string updatedContext = session.Sanitize(ContextText);
        if (updatedContext != ContextText)
        {
            ContextText = updatedContext; SelectedTab = 1;
            SetStatus("StatusContextUpdated");
            return;
        }
        PendingText = safe; HasPending = true; SelectedTab = 0;
        SetStatus("StatusReviewMessage");
    }

    private async Task SendAsync(CancellationToken ct)
    {
        var settings = Settings();
        ModelClient.ValidateSettings(settings);
        if (PendingText.Length > 16_000) throw new SafeOperationException("PromptTooLong");
        SetStatus("StatusCheckingMessage");
        // The user can edit the safe preview. Inspect those edits locally as well.
        if (UseAgent) await client.DiscoverEntitiesAsync(PendingText, Options(), session!, AgentEndpoint, AgentModel, ct);
        string outgoing = session!.Sanitize(PendingText);
        string updatedContext = session.Sanitize(ContextText);
        if (updatedContext != ContextText)
        {
            ContextText = updatedContext; SelectedTab = 1;
            SetStatus("StatusReviewUpdatedContext");
            return;
        }
        if (outgoing != PendingText)
        {
            PendingText = outgoing;
            SetStatus("StatusReviewUpdatedMessage");
            return;
        }
        var userRow = new ChatRow(Text["You"], outgoing);
        var modelRow = new ChatRow(settings.Model, "", true);
        Chat.Add(userRow); Chat.Add(modelRow); Refresh(); ScrollChat?.Invoke();
        var nextHistory = history.Append(new ChatMessage("user", outgoing)).ToArray();
        var streamed = new StringBuilder();
        bool success = false;
        SetStatus(IsOllama ? "StatusLocalReply" : "StatusRemoteReply");
        try
        {
            string answer = await client.ChatAsync(settings, ContextText, nextHistory, session, delta =>
            {
                streamed.Append(delta);
                modelRow.Text = streamed.ToString();
                ScrollChat?.Invoke();
            }, ct);
            string safeAnswer = session.Sanitize(answer);
            history.Add(new("user", outgoing)); history.Add(new("assistant", safeAnswer));
            safeAnswers[modelRow] = safeAnswer;
            modelRow.Text = RestoreLocally ? session.RestoreForLocalDisplay(safeAnswer) : safeAnswer;
            success = true; Prompt = ""; ClearPending();
            SetStatus("StatusReplyReceived");
        }
        finally
        {
            if (!success)
            {
                // Failed/cancelled messages do not become hidden history for later requests.
                Chat.Remove(userRow); Chat.Remove(modelRow);
            }
            ScrollChat?.Invoke(); Refresh();
        }
    }

    public void Dispose() { operation?.Cancel(); client.Dispose(); session = null; history.Clear(); safeAnswers.Clear(); }
}
