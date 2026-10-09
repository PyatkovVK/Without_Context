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
    public DateTimeOffset CreatedAt { get; }
    public string TimeLabel => CreatedAt.ToLocalTime().ToString("HH:mm");
    public string Color { get; }
    public ChatRow(string label, string text, bool assistant = false, DateTimeOffset? createdAt = null)
    { this.label = label; this.text = text; IsAssistant = assistant; CreatedAt = createdAt ?? DateTimeOffset.Now; Color = assistant ? "#A995E3" : "#71D6B3"; }
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

public enum ConnectionState { Unchecked, Checking, Connected, Failed, Disabled, Unavailable }

public sealed class MainViewModel : Bindable, IDisposable
{
    private readonly ModelClient client;
    private readonly ContextPreparationService preparation;
    private readonly ConversationTitleService titles;
    private CancellationTokenSource? titleOperation;
    private int titleRevision;
    private bool titleAttempted;
    private string chatTitle = "";
    public string ChatTitle { get => chatTitle; private set => Set(ref chatTitle, value); }
    public Task TitleUpdate { get; private set; } = Task.CompletedTask;
    private bool disposed;
    private readonly List<ChatMessage> history = new();
    private RedactionSession? session;
    private ScanSummary? scan;
    private CancellationTokenSource? operation;
    private readonly List<RelayCommand> commands = new();
    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public event Action? ScrollChat;
    public event Action? StoredStateChanged;
    private string safeStoredContext = "";
    private bool reopened;
    private readonly List<StoredMessage> completedMessages = new();

    public ObservableCollection<ChatRow> Chat { get; } = new();
    public ObservableCollection<string> LocalModels { get; } = new();
    public ObservableCollection<string> AgentModels { get; } = new();
    public bool HasLocalModels => LocalModels.Count > 0;
    public bool HasAgentModels => AgentModels.Count > 0;
    public string? SelectedLocalModel
    {
        get => LocalModels.Contains(LocalModel) ? LocalModel : null;
        set { if (!string.IsNullOrWhiteSpace(value)) LocalModel = value; }
    }
    public string? SelectedAgentModel
    {
        get => AgentModels.Contains(AgentModel) ? AgentModel : null;
        set { if (!string.IsNullOrWhiteSpace(value)) AgentModel = value; }
    }
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
            UpdateReplacements(); NotifyConnections(); NotifyModelLabels(); Notify(nameof(TrimmingSummary));
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
    public bool UseAgent { get => useAgent; set { if (Set(ref useAgent, value)) { ResetCleanerConnection(); ResetCleanerContext(); } } }
    private string agentEndpoint = "http://localhost:11434";
    public string AgentEndpoint { get => agentEndpoint; set { if (Set(ref agentEndpoint, value)) { AgentModels.Clear(); ResetCleanerConnection(); ResetCleanerContext(); } } }
    private string agentModel = "";
    public string AgentModel { get => agentModel; set { if (Set(ref agentModel, value)) { Notify(nameof(SelectedAgentModel)); ResetCleanerConnection(); ResetCleanerContext(); } } }
    private int contextLimit = 24000;
    public int ContextLimit { get => contextLimit; set { if (Set(ref contextLimit, Math.Clamp(value, 4000, FolderCollector.MaxContextChars))) { Notify(nameof(TrimmingSummary)); InvalidateContext(); } } }
    private ContextTrimmingMode trimmingMode = ContextTrimmingMode.Moderate;
    public ContextTrimmingMode TrimmingMode
    {
        get => trimmingMode;
        set
        {
            if (!Enum.IsDefined(value) || !Set(ref trimmingMode, value)) return;
            Notify(nameof(MinimalTrimming)); Notify(nameof(ModerateTrimming)); Notify(nameof(MaximumTrimming)); Notify(nameof(CustomTrimming));
            Notify(nameof(TrimmingSummary)); InvalidateContext();
        }
    }
    public bool MinimalTrimming { get => TrimmingMode == ContextTrimmingMode.Minimal; set { if (value) TrimmingMode = ContextTrimmingMode.Minimal; } }
    public bool ModerateTrimming { get => TrimmingMode == ContextTrimmingMode.Moderate; set { if (value) TrimmingMode = ContextTrimmingMode.Moderate; } }
    public bool MaximumTrimming { get => TrimmingMode == ContextTrimmingMode.Maximum; set { if (value) TrimmingMode = ContextTrimmingMode.Maximum; } }
    public bool CustomTrimming { get => TrimmingMode == ContextTrimmingMode.Custom; set { if (value) TrimmingMode = ContextTrimmingMode.Custom; } }
    private string trimmingInstructions = "";
    public string TrimmingInstructions
    {
        get => trimmingInstructions;
        set { if (Set(ref trimmingInstructions, value)) { Notify(nameof(InstructionsCount)); if (CustomTrimming) InvalidateContext(); } }
    }
    public string InstructionsCount => TrimmingInstructions.Length + "/1000";
    public string TrimmingSummary => Text.Format("TrimmingSummary", ContextReducer.TargetCharacters(ContextLimit, TrimmingMode), ContextReducer.RetainedMessages(TrimmingMode));
    private bool cleanerModelExpanded, chatModelExpanded;
    public bool CleanerModelExpanded { get => cleanerModelExpanded; set => Set(ref cleanerModelExpanded, value); }
    public bool ChatModelExpanded { get => chatModelExpanded; set => Set(ref chatModelExpanded, value); }
    public string CleanerModelName => !UseAgent ? Text["SignalDisabled"] : string.IsNullOrWhiteSpace(AgentModel) ? Text["SelectCleaner"] : AgentModel;
    public string AssistantModelName => !ModelConfigured ? Text["SelectAssistant"] : IsOllama ? LocalModel : ApiModel;
    public string AssistantLocation => Text[IsOllama ? "OnThisComputer" : "RemoteModelHelp"];
    private void NotifyModelLabels() { Notify(nameof(CleanerModelName)); Notify(nameof(AssistantModelName)); Notify(nameof(AssistantLocation)); }

    private int providerIndex;
    public int ProviderIndex
    {
        get => providerIndex;
        set { if (value is >= 0 and <= 1 && Set(ref providerIndex, value)) { Notify(nameof(SelectedProvider)); ResetChatConnection(); ResetModelConversation(); Notify(nameof(IsOllama)); Notify(nameof(IsRemote)); Refresh(); } }
    }
    public LocalizedOption? SelectedProvider
    {
        get => Providers[ProviderIndex];
        set { if (value is not null) { int index = Array.IndexOf(Providers.ToArray(), value); if (index >= 0) ProviderIndex = index; } }
    }
    public bool IsOllama => ProviderIndex == 0;
    public bool IsRemote => !IsOllama;
    private string ollamaEndpoint = "http://localhost:11434";
    public string OllamaEndpoint { get => ollamaEndpoint; set { if (Set(ref ollamaEndpoint, value)) { LocalModels.Clear(); ResetChatConnection(); ResetModelConversation(); } } }
    private string localModel = "";
    public string LocalModel { get => localModel; set { if (Set(ref localModel, value)) { Notify(nameof(SelectedLocalModel)); ResetChatConnection(); ResetModelConversation(); } } }
    private string apiEndpoint = "";
    public string ApiEndpoint { get => apiEndpoint; set { if (Set(ref apiEndpoint, value)) { ResetChatConnection(); ResetModelConversation(); } } }
    private string apiModel = "";
    public string ApiModel { get => apiModel; set { if (Set(ref apiModel, value)) { ResetChatConnection(); ResetModelConversation(); } } }
    private string apiKey = "";
    public string ApiKey
    {
        get => apiKey;
        set
        {
            if (!Set(ref apiKey, value)) return;
            ResetChatConnection(); ResetModelConversation();
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
            safeStoredContext = "";
            ContextApproved = false; Notify(nameof(Summary));
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
    public bool ShowWelcome => !HasChat && !HasPending;
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
        Text.Format("ScanSummary", scan.Included, ContextText.Length, scan.Skipped) + (scan.LimitReached ? Text["LimitReached"] : "");
    public string CountSummary => Text.Format("ReplacementCount", session?.Count ?? 0);
    private int selectedTab;
    public int SelectedTab
    {
        get => selectedTab;
        set { if (value is >= 0 and <= 3 && Set(ref selectedTab, value)) { Notify(nameof(IsChatView)); Notify(nameof(IsContextView)); Notify(nameof(IsReplacementsView)); Notify(nameof(IsFilesView)); } }
    }
    public bool IsChatView => SelectedTab == 0;
    public bool IsContextView => SelectedTab == 1;
    public bool IsReplacementsView => SelectedTab == 2;
    public bool IsFilesView => SelectedTab == 3;
    public RelayCommand ShowChatCommand { get; }
    public RelayCommand ShowContextCommand { get; }
    public RelayCommand ShowReplacementsCommand { get; }
    public RelayCommand ShowFilesCommand { get; }

    private bool restoreLocally;
    public bool RestoreLocally { get => restoreLocally; set { if (Set(ref restoreLocally, value)) UpdateDisplayedAnswers(); } }
    private readonly Dictionary<ChatRow, string> safeAnswers = new();

    public RelayCommand BrowseCommand { get; }
    public RelayCommand RefreshModelsCommand { get; }
    public RelayCommand RefreshAgentModelsCommand { get; }
    public RelayCommand CheckCleanerCommand { get; }
    public RelayCommand CheckChatCommand { get; }
    public RelayCommand PrepareCommand { get; }
    public RelayCommand PreviewPromptCommand { get; }
    public RelayCommand SendCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public RelayCommand EditPromptCommand { get; }
    public RelayCommand ClearSessionCommand { get; }
    public RelayCommand DismissErrorCommand { get; }

    public MainViewModel(ModelClient client, ContextPreparationService preparation, ConversationTitleService titles)
    {
        this.client = client;
        this.preparation = preparation;
        this.titles = titles;
        ShowChatCommand = Command(() => SelectedTab = 0);
        ShowContextCommand = Command(() => SelectedTab = 1);
        ShowReplacementsCommand = Command(() => SelectedTab = 2);
        ShowFilesCommand = Command(() => SelectedTab = 3);
        LocalModels.CollectionChanged += (_, _) => { Notify(nameof(HasLocalModels)); Notify(nameof(SelectedLocalModel)); };
        AgentModels.CollectionChanged += (_, _) => { Notify(nameof(HasAgentModels)); Notify(nameof(SelectedAgentModel)); };
        BrowseCommand = Command(() => RunAsync(async ct => { string? picked = PickFolderAsync is null ? null : await PickFolderAsync(); ct.ThrowIfCancellationRequested(); if (picked is not null) Folder = picked; }), () => !Busy);
        RefreshModelsCommand = Command(() => RunAsync(RefreshModelsAsync), () => !Busy);
        RefreshAgentModelsCommand = Command(() => RunAsync(RefreshAgentModelsAsync), () => !Busy && UseAgent);
        CheckCleanerCommand = Command(() => RunAsync(ct => CheckConnectionAsync(true, ct)), () => !Busy && UseAgent && !string.IsNullOrWhiteSpace(AgentModel));
        CheckChatCommand = Command(() => RunAsync(ct => CheckConnectionAsync(false, ct)), () => !Busy && ModelConfigured);
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
        Notify(nameof(Ready)); Notify(nameof(CanReview)); Notify(nameof(ModelConfigured)); Notify(nameof(CanCompose)); Notify(nameof(HasChat)); Notify(nameof(ShowWelcome)); NotifyModelLabels();
        foreach (var command in commands) command.Refresh();
    }
    private ProtectionOptions Options() => new((ProtectionProfile)Math.Clamp(ProfileIndex, 0, 3),
        CustomTerms.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    private ModelSettings Settings() => IsOllama ? new(ModelProvider.Ollama, OllamaEndpoint, LocalModel) : new(ModelProvider.RemoteApi, ApiEndpoint, ApiModel, ApiKey);
    private void ClearPending() { PendingText = ""; HasPending = false; }
    private void ResetConversation()
    {
        CancelTitle(); titleAttempted = false; ChatTitle = "";
        reopened = false; completedMessages.Clear();
        ClearPending(); history.Clear(); Chat.Clear(); safeAnswers.Clear(); Refresh();
        if (!disposed) StoredStateChanged?.Invoke();
    }
    private void ResetModelConversation()
    { if (reopened) { ClearPending(); Refresh(); } else ResetConversation(); }
    private void ResetCleanerContext()
    {
        if (reopened) { ContextApproved = false; ClearPending(); SetStatus("StatusReopened"); Refresh(); }
        else InvalidateContext();
    }
    private void InvalidateContext()
    {
        session = null; scan = null; ContextApproved = false;
        safeStoredContext = "";
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
        if (Busy || disposed) return;
        Busy = true; SetError(null); operation = new();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { if (!disposed) SetStatus("StatusCancelled"); }
        catch (SafeOperationException e) { if (!disposed) { SetError(e); SetStatus("StatusStopped"); } }
        catch { if (!disposed) { SetError(new SafeOperationException("UnexpectedError")); SetStatus("StatusStopped"); } }
        finally { operation.Dispose(); operation = null; Busy = false; if (!disposed) UpdateReplacements(); }
    }

    private ConnectionState cleanerConnection, chatConnection;
    private int cleanerRevision, chatRevision;
    private SafeOperationException? cleanerConnectionError, chatConnectionError;
    public ConnectionState CleanerConnectionState => UseAgent ? cleanerConnection : ConnectionState.Disabled;
    public ConnectionState ChatConnectionState => chatConnection;
    public string CleanerConnectionLabel => Text["Signal" + CleanerConnectionState];
    public string ChatConnectionLabel => Text["Signal" + ChatConnectionState];
    public string CleanerConnectionColor => ConnectionColor(CleanerConnectionState);
    public string ChatConnectionColor => ConnectionColor(ChatConnectionState);
    public string CleanerConnectionTip => !UseAgent ? Text["RulesOnlyHint"] : cleanerConnectionError?.GetLocalizedMessage(Text)
        ?? Text[cleanerConnection == ConnectionState.Connected ? "ConnectionVerifiedHint" : "CheckConnectionHint"];
    public string ChatConnectionTip => chatConnectionError?.GetLocalizedMessage(Text)
        ?? Text[chatConnection == ConnectionState.Connected ? "ConnectionVerifiedHint" : "CheckConnectionHint"];
    private static string ConnectionColor(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "#B6E2CA", ConnectionState.Checking or ConnectionState.Unavailable => "#E2BE78",
        ConnectionState.Failed => "#EE9099", _ => "#7B7E86"
    };
    private void NotifyConnections()
    {
        foreach (string property in new[] { nameof(CleanerConnectionState), nameof(ChatConnectionState), nameof(CleanerConnectionLabel),
            nameof(ChatConnectionLabel), nameof(CleanerConnectionColor), nameof(ChatConnectionColor), nameof(CleanerConnectionTip), nameof(ChatConnectionTip) }) Notify(property);
    }
    private void ResetCleanerConnection() { cleanerRevision++; cleanerConnection = ConnectionState.Unchecked; cleanerConnectionError = null; NotifyConnections(); }
    private void ResetChatConnection() { chatRevision++; chatConnection = ConnectionState.Unchecked; chatConnectionError = null; NotifyConnections(); }
    private void SetConnection(bool cleaner, ConnectionState state, SafeOperationException? error = null)
    {
        if (cleaner) { cleanerConnection = state; cleanerConnectionError = error; }
        else { chatConnection = state; chatConnectionError = error; }
        NotifyConnections();
    }
    private async Task CheckConnectionAsync(bool cleaner, CancellationToken ct)
    {
        int revision = cleaner ? cleanerRevision : chatRevision;
        bool Current() => !disposed && revision == (cleaner ? cleanerRevision : chatRevision);
        var settings = cleaner ? new ModelSettings(ModelProvider.Ollama, AgentEndpoint, AgentModel) : Settings();
        SetConnection(cleaner, ConnectionState.Checking);
        SetStatus(cleaner ? "StatusCheckingCleaner" : "StatusCheckingAssistant");
        try
        {
            if (cleaner) await client.EnsureLocalModelAsync(settings.Endpoint, settings.Model, ct);
            else await client.CheckChatModelAsync(settings, ct);
            if (Current())
            {
                SetConnection(cleaner, ConnectionState.Connected);
                SetStatus(cleaner ? "StatusCleanerConnected" : "StatusAssistantConnected");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            var error = new SafeOperationException("ModelTimeout");
            if (Current()) SetConnection(cleaner, ConnectionState.Failed, error);
            throw error;
        }
        catch (OperationCanceledException) { if (Current()) SetConnection(cleaner, ConnectionState.Unchecked); throw; }
        catch (SafeOperationException e)
        {
            if (Current()) SetConnection(cleaner, e.MessageKey == "ConnectionCheckUnavailable" ? ConnectionState.Unavailable : ConnectionState.Failed, e);
            throw;
        }
        catch { if (Current()) SetConnection(cleaner, ConnectionState.Failed); throw; }
    }
    private async Task RefreshAgentModelsAsync(CancellationToken ct)
    {
        SetStatus("StatusCheckingOllama");
        var models = await client.ListLocalModelsAsync(AgentEndpoint, ct);
        ct.ThrowIfCancellationRequested();
        AgentModels.Clear(); foreach (string model in models) AgentModels.Add(model);
        if (string.IsNullOrWhiteSpace(AgentModel) && models.Count > 0) AgentModel = models[0];
        SetStatus(models.Count > 0 ? "StatusModelsFound" : "StatusNoModels", models.Count);
    }

    private async Task RefreshModelsAsync(CancellationToken ct)
    {
        SetStatus("StatusCheckingOllama");
        var models = await client.ListLocalModelsAsync(OllamaEndpoint, ct);
        ct.ThrowIfCancellationRequested();
        LocalModels.Clear(); foreach (string model in models) LocalModels.Add(model);
        if (string.IsNullOrWhiteSpace(LocalModel) && models.Count > 0) LocalModel = models[0];
        SetStatus(models.Count > 0 ? "StatusModelsFound" : "StatusNoModels", models.Count);
    }

    private async Task PrepareAsync(CancellationToken ct)
    {
        InvalidateContext();
        var settings = new ContextPreparationSettings(Folder, Options(), ContextLimit, TrimmingMode, TrimmingInstructions,
            UseAgent, new(ModelProvider.Ollama, AgentEndpoint, AgentModel), ApiKey);
        if (UseAgent) await CheckConnectionAsync(true, ct);
        SetStatus(UseAgent ? "StatusAgentWorking" : "StatusCollecting");
        var progress = new Progress<int>(number => { if (!disposed && Busy && operation is { } current && current.Token == ct) { if (number == 0) SetStatus("StatusTrimming"); else SetStatus("ProcessingFile", number); } });
        var prepared = await preparation.PrepareAsync(settings, progress, ct);
        ct.ThrowIfCancellationRequested();
        session = prepared.Session; scan = prepared.Snapshot;
        Files.Clear(); foreach (var file in prepared.Snapshot.Files) Files.Add(file);
        ContextText = prepared.Text;
        safeStoredContext = prepared.Text;
        StoredStateChanged?.Invoke();
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
        ct.ThrowIfCancellationRequested();
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
        ct.ThrowIfCancellationRequested();
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
        var nextHistory = ContextReducer.SelectHistory(history, new ChatMessage("user", outgoing), TrimmingMode);
        var streamed = new StringBuilder();
        bool success = false;
        int connectionRevision = chatRevision;
        SetConnection(false, ConnectionState.Checking);
        SetStatus(IsOllama ? "StatusLocalReply" : "StatusRemoteReply");
        try
        {
            string answer = await client.ChatAsync(settings, ContextText, nextHistory, session, delta =>
            {
                if (disposed || ct.IsCancellationRequested) return;
                streamed.Append(delta);
                modelRow.Text = streamed.ToString();
                ScrollChat?.Invoke();
            }, ct);
            ct.ThrowIfCancellationRequested();
            if (!disposed && connectionRevision == chatRevision) SetConnection(false, ConnectionState.Connected);
            string safeAnswer = session.Sanitize(answer);
            history.Add(new("user", outgoing)); history.Add(new("assistant", safeAnswer));
            completedMessages.Add(new("user", outgoing, userRow.CreatedAt));
            completedMessages.Add(new("assistant", safeAnswer, modelRow.CreatedAt));
            safeAnswers[modelRow] = safeAnswer;
            modelRow.Text = RestoreLocally ? session.RestoreForLocalDisplay(safeAnswer) : safeAnswer;
            success = true; Prompt = ""; ClearPending();
            reopened = false; safeStoredContext = ContextText;
            if (!titleAttempted)
            {
                titleAttempted = true;
                string goal = session.Sanitize(completedMessages[0].Content);
                ChatTitle = ConversationTitleService.Fallback(goal);
                var source = titleOperation = new CancellationTokenSource();
                TitleUpdate = UpdateTitleAsync(settings, goal, session, source, titleRevision);
            }
            StoredStateChanged?.Invoke();
            SetStatus("StatusReplyReceived");
        }
        catch (OperationCanceledException) { if (!disposed && connectionRevision == chatRevision) SetConnection(false, ConnectionState.Unchecked); throw; }
        catch (SafeOperationException e) { if (!disposed && connectionRevision == chatRevision) SetConnection(false, ConnectionState.Failed, e); throw; }
        catch { if (!disposed && connectionRevision == chatRevision) SetConnection(false, ConnectionState.Failed); throw; }
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

    private async Task UpdateTitleAsync(ModelSettings settings, string goal, RedactionSession owner,
        CancellationTokenSource source, int revision)
    {
        CancellationToken ct = source.Token;
        try
        {
            string? generated = await titles.GenerateAsync(settings, goal, owner.Sanitize(ContextText), ct);
            if (disposed || ct.IsCancellationRequested || revision != titleRevision || !ReferenceEquals(session, owner) || generated is null) return;
            string? safe = ConversationTitleService.Normalize(owner.Sanitize(generated));
            if (safe is null) return;
            ChatTitle = safe; StoredStateChanged?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch { } // An optional title cannot turn a successful reply into an error.
        finally { if (ReferenceEquals(titleOperation, source)) titleOperation = null; source.Dispose(); }
    }
    private void CancelTitle()
    { titleRevision++; titleOperation?.Cancel(); titleOperation = null; }

    public (string Context, IReadOnlyList<StoredMessage> Messages, string Title) CaptureStoredState()
    {
        // Display rows can contain restored originals or unfinished streaming data. Never persist them.
        return (session?.Sanitize(safeStoredContext) ?? "",
            completedMessages.Select(m => m with { Content = session?.Sanitize(m.Content) ?? m.Content }).ToArray(),
            ConversationTitleService.Normalize(session?.Sanitize(ChatTitle) ?? ChatTitle) ?? "");
    }
    public void RestoreStoredState(StoredConversation stored)
    {
        session = new RedactionSession(Options());
        contextText = safeStoredContext = session.Sanitize(stored.Context);
        scan = contextText.Length == 0 ? null : new(0, 0, contextText.Length, false, []);
        completedMessages.Clear(); history.Clear(); Chat.Clear(); safeAnswers.Clear();
        foreach (var message in stored.Messages)
        {
            string safe = session.Sanitize(message.Content);
            completedMessages.Add(message with { Content = safe }); history.Add(new(message.Role, safe));
            var row = new ChatRow(Text[message.Role == "user" ? "You" : "Assistant"], safe, message.Role == "assistant", message.CreatedAt);
            Chat.Add(row); if (row.IsAssistant) safeAnswers[row] = safe;
        }
        ChatTitle = ConversationTitleService.Normalize(session.Sanitize(stored.Title)) ??
            ConversationTitleService.Fallback(completedMessages.FirstOrDefault()?.Content ?? "");
        titleAttempted = stored.Title.Length > 0;
        reopened = true; contextApproved = false;
        Notify(nameof(ContextText)); Notify(nameof(ContextApproved)); Notify(nameof(Summary));
        SetStatus("StatusReopened"); Refresh();
    }
    public void CancelOperation() { operation?.Cancel(); CancelTitle(); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; operation?.Cancel(); CancelTitle(); client.Dispose(); session = null; scan = null;
        chatTitle = "";
        history.Clear(); completedMessages.Clear(); safeStoredContext = "";
        safeAnswers.Clear(); Chat.Clear(); Files.Clear(); Replacements.Clear();
        folder = apiKey = customTerms = trimmingInstructions = prompt = pendingText = contextText = "";
        LocalModels.Clear(); AgentModels.Clear();
        contextApproved = hasPending = false; PickFolderAsync = null;
    }
}
