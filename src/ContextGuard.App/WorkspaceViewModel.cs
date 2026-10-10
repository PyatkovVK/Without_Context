using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using ContextGuard.Core;

namespace ContextGuard.App;

public sealed class ConversationViewModel : Bindable, IDisposable
{
    public int Number { get; }
    public Guid Id { get; }
    public MainViewModel Session { get; }
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.Now;
    public string TimeLabel => UpdatedAt.ToLocalTime().ToString("HH:mm");
    public string Title => string.IsNullOrWhiteSpace(Session.ChatTitle) ? Session.Text.Format("ConversationTitle", Number) : Session.ChatTitle;
    public string Subtitle => Session.Text[Session.Ready ? "ChatContextReady" : "ChatNoContext"];
    private bool archived;
    private bool disposed;
    public bool IsArchived => archived;
    public string ArchiveActionLabel => Session.Text[IsArchived ? "RestoreChat" : "ArchiveChat"];
    public RelayCommand ArchiveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    internal void SetArchived(bool value) { archived = value; Notify(nameof(IsArchived)); Notify(nameof(ArchiveActionLabel)); }
    public ConversationViewModel(int number, MainViewModel session, Action<ConversationViewModel> archive,
        Action<ConversationViewModel> delete, Func<bool> canAct, StoredConversation? stored = null)
    {
        Number = number; Session = session; Id = stored?.Id ?? Guid.NewGuid();
        UpdatedAt = stored?.UpdatedAt ?? DateTimeOffset.Now; archived = stored?.Archived ?? false;
        ArchiveCommand = new(() => archive(this), () => !disposed && canAct());
        DeleteCommand = new(() => delete(this), () => !disposed && canAct());
        Session.PropertyChanged += SessionChanged;
        Session.Chat.CollectionChanged += ChatChanged;
    }
    private void ChatChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add) { UpdatedAt = DateTimeOffset.Now; Notify(nameof(TimeLabel)); }
        Notify(nameof(Title));
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Text) or nameof(MainViewModel.ChatTitle)) Notify(nameof(Title));
        if (e.PropertyName == nameof(MainViewModel.Text)) Notify(nameof(ArchiveActionLabel));
        if (e.PropertyName is nameof(MainViewModel.Text) or nameof(MainViewModel.Ready)) Notify(nameof(Subtitle));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; RefreshCommands();
        Session.PropertyChanged -= SessionChanged; Session.Chat.CollectionChanged -= ChatChanged; Session.Dispose();
    }
    public void RefreshCommands() { ArchiveCommand.Refresh(); DeleteCommand.Refresh(); }
}

public sealed class WorkspaceViewModel : Bindable, IDisposable
{
    private readonly Func<MainViewModel> sessionFactory;
    private ConversationViewModel? active;
    private int nextNumber;
    private bool disposed;
    private readonly ChatArchiveService? archive;
    private readonly ISidebarStateService? sidebarState;
    private readonly CancellationTokenSource sidebarLifetime = new();
    private Task? sidebarInitialization;
    private int chatsSidebarRevision, modelsSidebarRevision;
    private bool loading, exporting, initialized;
    private string storageMessageKey = "", storageErrorKey = "", sidebarErrorKey = "";
    public string StorageFolder => archive?.Folder ?? "";
    public string StorageMessage => storageErrorKey.Length > 0 ? Text[storageErrorKey] : sidebarErrorKey.Length > 0 ? Text[sidebarErrorKey] : storageMessageKey.Length > 0 ? Text[storageMessageKey] : "";
    public bool HasStorageMessage => StorageMessage.Length > 0;
    public bool StorageFailed => storageErrorKey.Length > 0 || sidebarErrorKey.Length > 0;
    public Func<Task<string?>>? PickExportFolderAsync { get; set; }
    private ConversationViewModel? pendingDeletion;
    private string deletionTitle = "";
    public bool DeleteConfirmationOpen => pendingDeletion is not null;
    // Retain the displayed title during the closing fade; the panel must not reflow.
    public string PendingDeleteTitle => deletionTitle;
    public bool ModalOpen => SettingsOpen || DeleteConfirmationOpen;
    public double ModalShade => ModalOpen ? 1 : 0;
    private bool settingsOpen;
    public bool SettingsOpen
    {
        get => settingsOpen;
        set { if (Set(ref settingsOpen, value)) NotifyModal(); }
    }
    public ScrollBarVisibility ChatScrollBarVisibility => ModalOpen ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
    private void NotifyModal()
    {
        Notify(nameof(DeleteConfirmationOpen)); Notify(nameof(PendingDeleteTitle)); Notify(nameof(ModalOpen));
        Notify(nameof(ModalShade)); Notify(nameof(ChatScrollBarVisibility)); Refresh();
    }
    private readonly List<RelayCommand> commands = [];
    public ObservableCollection<ConversationViewModel> Conversations { get; } = [];
    public ObservableCollection<ConversationViewModel> VisibleConversations { get; } = [];
    public event Action? RequestSettings;
    public event Action? ScrollChat;
    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public ConversationViewModel? Active
    {
        get => active;
        set
        {
            if (value is null || !Conversations.Contains(value) || IsBusy || DeleteConfirmationOpen || !Set(ref active, value)) return;
            Refresh(); ScrollChat?.Invoke();
        }
    }
    private int languageIndex;
    public string[] Languages { get; } = ["English", "Русский"];
    public LocalizedText Text { get; private set; } = new();
    public int LanguageIndex
    {
        get => languageIndex;
        set
        {
            if (value is < 0 or > 1 || !Set(ref languageIndex, value)) return;
            Text = new((AppLanguage)value); Notify(nameof(Text)); Notify(nameof(ChatsToggleTip)); Notify(nameof(ModelsToggleTip));
            Notify(nameof(StorageMessage));
            foreach (var chat in Conversations) chat.Session.LanguageIndex = value;
            NotifyArchive(); Notify(nameof(PendingDeleteTitle));
        }
    }
    private bool chatsSidebarOpen = true;
    public bool ChatsSidebarOpen
    {
        get => chatsSidebarOpen;
        set
        {
            if (disposed) return;
            chatsSidebarRevision++;
            if (Set(ref chatsSidebarOpen, value))
            {
                Notify(nameof(ChatsSidebarWidth)); Notify(nameof(ChatsSidebarPixels)); Notify(nameof(ChatsSidebarPadding));
                Notify(nameof(ChatsHeaderPixels)); Notify(nameof(ChatsBrandPixels)); Notify(nameof(ChatsToggleTip)); Notify(nameof(CanUseChatList));
            }
        }
    }
    private bool modelsSidebarOpen = true;
    public bool ModelsSidebarOpen
    {
        get => modelsSidebarOpen;
        set
        {
            if (disposed) return;
            modelsSidebarRevision++;
            if (Set(ref modelsSidebarOpen, value))
            {
                Notify(nameof(ModelsSidebarWidth)); Notify(nameof(ModelsSidebarPixels)); Notify(nameof(ModelsSidebarPadding));
                Notify(nameof(ModelsHeaderPixels)); Notify(nameof(ModelsBrandPixels)); Notify(nameof(ModelsToggleTip));
            }
        }
    }
    private bool compactLayout;
    public bool CompactLayout
    {
        get => compactLayout;
        set { if (Set(ref compactLayout, value)) { Notify(nameof(ChatsSidebarWidth)); Notify(nameof(ModelsSidebarWidth)); Notify(nameof(ChatsSidebarPixels)); Notify(nameof(ModelsSidebarPixels)); Notify(nameof(ChatsBodyWidth)); Notify(nameof(ModelsBodyWidth)); Notify(nameof(ChatsHeaderPixels)); Notify(nameof(ChatsBrandPixels)); Notify(nameof(ModelsHeaderPixels)); Notify(nameof(ModelsBrandPixels)); Notify(nameof(TitleMaxWidth)); } }
    }
    public double TitleMaxWidth => CompactLayout ? 220 : 350;
    public GridLength ChatsSidebarWidth => new(ChatsSidebarOpen ? CompactLayout ? 240 : 310 : 72);
    public GridLength ModelsSidebarWidth => new(ModelsSidebarOpen ? CompactLayout ? 300 : 350 : 72);
    public double ChatsSidebarPixels => ChatsSidebarWidth.Value;
    public double ModelsSidebarPixels => ModelsSidebarWidth.Value;
    public double ChatsBodyWidth => (CompactLayout ? 240 : 310) - 26;
    public double ModelsBodyWidth => (CompactLayout ? 300 : 350) - 46;
    public Thickness ChatsSidebarPadding => ChatsSidebarOpen ? new(12, 20, 12, 16) : new(13, 20, 13, 16);
    public Thickness ModelsSidebarPadding => ModelsSidebarOpen ? new(22, 20, 22, 18) : new(13, 20, 13, 18);
    public double ChatsHeaderPixels => ChatsSidebarPixels - ChatsSidebarPadding.Left - ChatsSidebarPadding.Right - 2;
    public double ModelsHeaderPixels => ModelsSidebarPixels - ModelsSidebarPadding.Left - ModelsSidebarPadding.Right - 2;
    public double ChatsBrandPixels => ChatsHeaderPixels - 44;
    public double ModelsBrandPixels => ModelsHeaderPixels - 44;
    public string ChatsToggleTip => Text[ChatsSidebarOpen ? "HideChats" : "ShowChats"];
    public string ModelsToggleTip => Text[ModelsSidebarOpen ? "HideModels" : "ShowModels"];
    public bool IsBusy => loading || exporting || Conversations.Any(chat => chat.Session.Busy);
    public bool NotBusy => !IsBusy;
    public bool CanUseChatList => NotBusy && ChatsSidebarOpen;
    private bool showingArchive;
    public bool ShowingArchive => showingArchive;
    public int ArchivedCount => Conversations.Count(chat => chat.IsArchived);
    public bool HasArchivedChats => ArchivedCount > 0;
    public string ChatListHeading => Text[ShowingArchive ? "ArchivedChats" : "RecentChats"];
    public string ArchiveNavigationLabel => ShowingArchive ? Text["BackToChats"] : Text.Format("ArchiveCount", ArchivedCount);
    public string ArchiveActionLabel => Text[Active?.IsArchived == true ? "RestoreChat" : "ArchiveChat"];
    public string ArchiveActionHelp => Text[Active?.IsArchived == true ? "RestoreChatHelp" : "ArchiveChatHelp"];
    public RelayCommand ToggleChatsCommand { get; }
    public RelayCommand ToggleModelsCommand { get; }
    public RelayCommand OpenModelOptionsCommand { get; }
    public RelayCommand ToggleArchiveViewCommand { get; }
    public RelayCommand ArchiveChatCommand { get; }
    public RelayCommand NewChatCommand { get; }
    public RelayCommand DeleteChatCommand { get; }
    public RelayCommand ConfirmDeleteChatCommand { get; }
    public RelayCommand CancelDeleteChatCommand { get; }
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand ExportChatCommand { get; }
    public RelayCommand ExportAllCommand { get; }
    public RelayCommand DismissStorageMessageCommand { get; }
    public RelayCommand OpenContextOptionsCommand { get; }
    public WorkspaceViewModel(Func<ModelClient>? clientFactory = null, ChatArchiveService? archive = null, ISidebarStateService? sidebarState = null)
    {
        this.archive = archive;
        this.sidebarState = sidebarState;
        // Compose one complete set of dependencies per chat; sessions never share a client or state.
        sessionFactory = () =>
        {
            var client = clientFactory?.Invoke() ?? new ModelClient();
            return new MainViewModel(client, new ContextPreparationService(new FolderCollector(), client), new ConversationTitleService(client));
        };
        ToggleChatsCommand = Command(() => ChatsSidebarOpen = !ChatsSidebarOpen);
        ToggleModelsCommand = Command(() => ModelsSidebarOpen = !ModelsSidebarOpen);
        OpenModelOptionsCommand = Command(() => { ModelsSidebarOpen = true; if (Active is { } chat) chat.Session.ChatModelExpanded = true; });
        OpenContextOptionsCommand = Command(() => { ModelsSidebarOpen = true; Active?.Session.ShowContextCommand.Execute(null); });
        ToggleArchiveViewCommand = Command(() => ShowArchive(!ShowingArchive), () => NotBusy && (ShowingArchive || ArchivedCount > 0));
        ArchiveChatCommand = Command(() => { if (Active is { } chat) ArchiveChat(chat); }, () => NotBusy && Active is not null);
        NewChatCommand = Command(() => CreateChat(Active?.Session), () => NotBusy);
        DeleteChatCommand = Command(() => { if (Active is { } chat) DeleteChat(chat); }, () => NotBusy && Active is not null);
        ConfirmDeleteChatCommand = new(() =>
        {
            var chat = pendingDeletion;
            pendingDeletion = null; NotifyModal();
            if (chat is not null) DeleteConfirmed(chat);
        }, () => !disposed && NotBusy && pendingDeletion is not null && Conversations.Contains(pendingDeletion));
        CancelDeleteChatCommand = new(() => { pendingDeletion = null; NotifyModal(); }, () => !disposed && DeleteConfirmationOpen);
        SettingsCommand = Command(() => RequestSettings?.Invoke(), () => NotBusy);
        ClearAllCommand = Command(ClearAll, () => NotBusy);
        ExportChatCommand = Command(() => Export(false), () => NotBusy && initialized && archive is not null && Active is not null);
        ExportAllCommand = Command(() => Export(true), () => NotBusy && initialized && archive is not null);
        DismissStorageMessageCommand = Command(() => { SetStorageMessage(""); SetSidebarError(""); });
        CreateChat(null);
        loading = archive is not null; initialized = archive is null; Refresh();
    }
    private RelayCommand Command(Action action, Func<bool>? allowed = null)
    {
        var command = new RelayCommand(action, () => !disposed && !DeleteConfirmationOpen && (allowed?.Invoke() ?? true));
        commands.Add(command); return command;
    }
    private void CreateChat(MainViewModel? previous)
    {
        var session = sessionFactory();
        session.LanguageIndex = LanguageIndex;
        if (previous is not null)
        {
            session.Folder = previous.Folder; session.ProfileIndex = previous.ProfileIndex;
            session.CustomTerms = previous.CustomTerms; session.ContextLimit = previous.ContextLimit;
            session.TrimmingMode = previous.TrimmingMode; session.TrimmingInstructions = previous.TrimmingInstructions;
            session.UseAgent = previous.UseAgent; session.AgentEndpoint = previous.AgentEndpoint;
            session.AgentModel = previous.AgentModel; session.ProviderIndex = previous.ProviderIndex;
            session.OllamaEndpoint = previous.OllamaEndpoint; session.LocalModel = previous.LocalModel;
            session.ApiEndpoint = previous.ApiEndpoint; session.ApiModel = previous.ApiModel;
            session.ApiKey = previous.ApiKey; session.RestoreLocally = previous.RestoreLocally;
        }
        session.PickFolderAsync = () => PickFolderAsync?.Invoke() ?? Task.FromResult<string?>(null);
        session.PropertyChanged += SessionChanged; session.ScrollChat += OnScrollChat;
        session.StoredStateChanged += QueueSave;
        var chat = new ConversationViewModel(++nextNumber, session, ArchiveChat, DeleteChat, () => !disposed && NotBusy && !DeleteConfirmationOpen);
        Conversations.Add(chat); showingArchive = false; UpdateVisible(); Active = chat; Refresh();
        QueueSave();
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Busy)) Refresh();
    }
    private void OnScrollChat() => ScrollChat?.Invoke();
    private void Release(ConversationViewModel chat)
    { chat.Session.PropertyChanged -= SessionChanged; chat.Session.ScrollChat -= OnScrollChat; chat.Session.StoredStateChanged -= QueueSave; chat.Dispose(); }
    private void UpdateVisible()
    {
        VisibleConversations.Clear();
        foreach (var chat in Conversations.Where(chat => chat.IsArchived == ShowingArchive).Reverse()) VisibleConversations.Add(chat);
        NotifyArchive();
    }
    private void NotifyArchive()
    {
        Notify(nameof(ShowingArchive)); Notify(nameof(ArchivedCount)); Notify(nameof(HasArchivedChats)); Notify(nameof(ChatListHeading));
        Notify(nameof(ArchiveNavigationLabel)); Notify(nameof(ArchiveActionLabel)); Notify(nameof(ArchiveActionHelp));
    }
    private void SelectVisible(ConversationViewModel? preferred = null)
    {
        if (preferred is not null && VisibleConversations.Contains(preferred)) Active = preferred;
        else if (VisibleConversations.Count > 0) Active = VisibleConversations[0];
        else if (ShowingArchive) { showingArchive = false; UpdateVisible(); SelectVisible(); }
        else CreateChat(null);
    }
    private void ShowArchive(bool show)
    { showingArchive = show; UpdateVisible(); SelectVisible(); Refresh(); }
    private void ArchiveChat(ConversationViewModel chat)
    {
        if (disposed || !NotBusy || !Conversations.Contains(chat)) return;
        bool restore = chat.IsArchived;
        chat.SetArchived(!restore);
        if (restore && Active == chat) showingArchive = false;
        UpdateVisible();
        if (Active is null || !VisibleConversations.Contains(Active)) SelectVisible();
        Refresh();
        QueueSave();
    }
    private void DeleteChat(ConversationViewModel chat)
    {
        if (disposed || !NotBusy || DeleteConfirmationOpen || !Conversations.Contains(chat)) return;
        if (chat.Session.HasChat) { deletionTitle = chat.Title; pendingDeletion = chat; NotifyModal(); }
        else DeleteConfirmed(chat);
    }
    private void DeleteConfirmed(ConversationViewModel chat)
    {
        if (disposed || !NotBusy || !Conversations.Contains(chat)) return;
        Conversations.Remove(chat); Release(chat); UpdateVisible();
        if (Active is null || !VisibleConversations.Contains(Active)) SelectVisible();
        Refresh();
        QueueSave();
    }
    private void ClearAll()
    {
        initialized = true;
        foreach (var chat in Conversations.ToArray()) Release(chat);
        Conversations.Clear(); VisibleConversations.Clear(); active = null; nextNumber = 0; showingArchive = false;
        CreateChat(null); Notify(nameof(Active)); Refresh();
        QueueSave();
    }
    public async Task InitializeAsync()
    {
        await InitializeSidebarStateAsync();
        if (initialized || archive is null || disposed) return;
        try
        {
            var saved = await archive.LoadAsync();
            if (disposed) return;
            foreach (var chat in Conversations) Release(chat);
            Conversations.Clear(); VisibleConversations.Clear(); active = null; nextNumber = 0;
            foreach (var item in saved)
            {
                var session = sessionFactory(); session.LanguageIndex = LanguageIndex; session.RestoreStoredState(item);
                session.PickFolderAsync = () => PickFolderAsync?.Invoke() ?? Task.FromResult<string?>(null);
                session.PropertyChanged += SessionChanged; session.ScrollChat += OnScrollChat; session.StoredStateChanged += QueueSave;
                Conversations.Add(new(item.Number, session, ArchiveChat, DeleteChat, () => !disposed && NotBusy && !DeleteConfirmationOpen, item)); nextNumber = Math.Max(nextNumber, item.Number);
            }
            initialized = true; loading = false; UpdateVisible(); SelectVisible();
            Notify(nameof(Active)); Refresh(); QueueSave();
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (SafeOperationException e) { if (!disposed) { loading = false; SetStorageMessage(e.MessageKey, true); Refresh(); } }
        catch { if (!disposed) { loading = false; SetStorageMessage("ChatStorageReadFailed", true); Refresh(); } }
    }
    private Task InitializeSidebarStateAsync() => sidebarInitialization ??= LoadSidebarStateAsync();
    private async Task LoadSidebarStateAsync()
    {
        if (sidebarState is null || disposed) return;
        int chatsRevision = chatsSidebarRevision, modelsRevision = modelsSidebarRevision;
        try
        {
            var saved = await sidebarState.LoadAsync(sidebarLifetime.Token);
            if (disposed) return;
            // A late startup read must not override a newer user choice on either panel.
            if (chatsRevision == chatsSidebarRevision) ChatsSidebarOpen = saved.ChatsSidebarOpen;
            if (modelsRevision == modelsSidebarRevision) ModelsSidebarOpen = saved.ModelsSidebarOpen;
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (SafeOperationException e) { if (!disposed) SetSidebarError(e.MessageKey); }
        catch { if (!disposed) SetSidebarError("SidebarStateReadFailed"); }
    }
    private StoredConversation Snapshot(ConversationViewModel chat)
    {
        var state = chat.Session.CaptureStoredState();
        return new(2, chat.Id, chat.Number, chat.UpdatedAt, chat.IsArchived, state.Context, state.Messages, state.Title);
    }
    private async void QueueSave()
    {
        if (disposed || !initialized || archive is null) return;
        try
        {
            await archive.SaveAsync(Conversations.Select(Snapshot).ToArray());
            if (!disposed && storageErrorKey.Length > 0) SetStorageMessage("");
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (SafeOperationException e) { if (!disposed) SetStorageMessage(e.MessageKey, true); }
        catch { if (!disposed) SetStorageMessage("ChatStorageWriteFailed", true); }
    }
    public async Task FlushStorageAsync()
    {
        await InitializeSidebarStateAsync();
        if (archive is not null && !initialized) throw new SafeOperationException("ChatStorageReadFailed");
        if (archive is not null) await archive.FlushAsync();
        if (sidebarState is null || disposed) return;
        try
        {
            await sidebarState.SaveAsync(new(ChatsSidebarOpen, ModelsSidebarOpen), sidebarLifetime.Token);
            if (!disposed) SetSidebarError("");
        }
        catch (SafeOperationException e) { if (!disposed) SetSidebarError(e.MessageKey); throw; }
    }
    private void SetSidebarError(string key)
    {
        sidebarErrorKey = key;
        Notify(nameof(StorageMessage)); Notify(nameof(HasStorageMessage)); Notify(nameof(StorageFailed));
    }
    private void SetStorageMessage(string key, bool error = false)
    {
        storageMessageKey = error ? "" : key; storageErrorKey = error ? key : "";
        Notify(nameof(StorageMessage)); Notify(nameof(HasStorageMessage)); Notify(nameof(StorageFailed));
    }
    private async void Export(bool all)
    {
        if (archive is null || disposed) return;
        exporting = true; Refresh();
        try
        {
            var chats = (all ? Conversations.ToArray() : Active is { } activeChat ? [activeChat] : Array.Empty<ConversationViewModel>())
                .Select(Snapshot).ToArray();
            string? folder = PickExportFolderAsync is null ? null : await PickExportFolderAsync();
            if (folder is null || disposed) return;
            await archive.ExportAsync(chats, folder);
            if (!disposed) SetStorageMessage("ChatExportComplete");
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (SafeOperationException e) { if (!disposed) SetStorageMessage(e.MessageKey, true); }
        catch { if (!disposed) SetStorageMessage("ChatExportFailed", true); }
        finally { exporting = false; if (!disposed) Refresh(); }
    }
    public void CancelOperations() { foreach (var chat in Conversations) chat.Session.CancelOperation(); }
    private void Refresh()
    {
        Notify(nameof(IsBusy)); Notify(nameof(NotBusy)); Notify(nameof(CanUseChatList)); NotifyArchive();
        foreach (var command in commands) command.Refresh();
        ConfirmDeleteChatCommand?.Refresh(); CancelDeleteChatCommand?.Refresh();
        foreach (var chat in Conversations) chat.RefreshCommands();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        sidebarLifetime.Cancel(); sidebarLifetime.Dispose();
        pendingDeletion = null; deletionTitle = "";
        archive?.Dispose();
        foreach (var chat in Conversations) Release(chat);
        Conversations.Clear(); VisibleConversations.Clear(); active = null; PickFolderAsync = null; PickExportFolderAsync = null;
    }
}
