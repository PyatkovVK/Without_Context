using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using ContextGuard.Core;

namespace ContextGuard.App;

public sealed class ConversationViewModel : Bindable, IDisposable
{
    public int Number { get; }
    public MainViewModel Session { get; }
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.Now;
    public string TimeLabel => UpdatedAt.ToLocalTime().ToString("HH:mm");
    public string Title
    {
        get
        {
            string? firstMessage = Session.Chat.FirstOrDefault(row => !row.IsAssistant)?.Text.Split('\n')[0].Trim();
            return string.IsNullOrWhiteSpace(firstMessage) ? Session.Text.Format("ConversationTitle", Number) : ContextReducer.LimitText(firstMessage, 46);
        }
    }
    public string Subtitle => Session.Text[Session.Ready ? "ChatContextReady" : "ChatNoContext"];
    private bool archived;
    public bool IsArchived => archived;
    internal void SetArchived(bool value) { archived = value; Notify(nameof(IsArchived)); }
    public ConversationViewModel(int number, MainViewModel session)
    {
        Number = number; Session = session;
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
        if (e.PropertyName == nameof(MainViewModel.Text)) Notify(nameof(Title));
        if (e.PropertyName is nameof(MainViewModel.Text) or nameof(MainViewModel.Ready)) Notify(nameof(Subtitle));
    }
    public void Dispose()
    {
        Session.PropertyChanged -= SessionChanged; Session.Chat.CollectionChanged -= ChatChanged; Session.Dispose();
    }
}

public sealed class WorkspaceViewModel : Bindable, IDisposable
{
    private readonly Func<MainViewModel> sessionFactory;
    private ConversationViewModel? active;
    private int nextNumber;
    private bool disposed;
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
            if (value is null || !Conversations.Contains(value) || IsBusy || !Set(ref active, value)) return;
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
            foreach (var chat in Conversations) chat.Session.LanguageIndex = value;
            NotifyArchive();
        }
    }
    private bool chatsSidebarOpen = true;
    public bool ChatsSidebarOpen
    {
        get => chatsSidebarOpen;
        set { if (Set(ref chatsSidebarOpen, value)) { Notify(nameof(ChatsSidebarWidth)); Notify(nameof(ChatsSidebarPadding)); Notify(nameof(ChatsToggleTip)); Notify(nameof(ChatsToggleGlyph)); } }
    }
    private bool modelsSidebarOpen = true;
    public bool ModelsSidebarOpen
    {
        get => modelsSidebarOpen;
        set { if (Set(ref modelsSidebarOpen, value)) { Notify(nameof(ModelsSidebarWidth)); Notify(nameof(ModelsSidebarPadding)); Notify(nameof(ModelsToggleTip)); } }
    }
    private bool compactLayout;
    public bool CompactLayout
    {
        get => compactLayout;
        set { if (Set(ref compactLayout, value)) { Notify(nameof(ChatsSidebarWidth)); Notify(nameof(ModelsSidebarWidth)); Notify(nameof(TitleMaxWidth)); } }
    }
    public double TitleMaxWidth => CompactLayout ? 220 : 350;
    public string ChatsToggleGlyph => ChatsSidebarOpen ? "‹" : "›";
    public GridLength ChatsSidebarWidth => new(ChatsSidebarOpen ? CompactLayout ? 240 : 310 : 56);
    public GridLength ModelsSidebarWidth => new(ModelsSidebarOpen ? CompactLayout ? 300 : 350 : 56);
    public Thickness ChatsSidebarPadding => ChatsSidebarOpen ? new(12, 20, 12, 16) : new(8, 20, 8, 16);
    public Thickness ModelsSidebarPadding => ModelsSidebarOpen ? new(22, 20, 22, 18) : new(8, 20, 8, 18);
    public string ChatsToggleTip => Text[ChatsSidebarOpen ? "HideChats" : "ShowChats"];
    public string ModelsToggleTip => Text[ModelsSidebarOpen ? "HideModels" : "ShowModels"];
    public bool IsBusy => Conversations.Any(chat => chat.Session.Busy);
    public bool NotBusy => !IsBusy;
    private bool showingArchive;
    public bool ShowingArchive => showingArchive;
    public int ArchivedCount => Conversations.Count(chat => chat.IsArchived);
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
    public RelayCommand SettingsCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public WorkspaceViewModel(Func<ModelClient>? clientFactory = null)
    {
        // Compose one complete set of dependencies per chat; sessions never share a client or state.
        sessionFactory = () =>
        {
            var client = clientFactory?.Invoke() ?? new ModelClient();
            return new MainViewModel(client, new ContextPreparationService(new FolderCollector(), client));
        };
        ToggleChatsCommand = Command(() => ChatsSidebarOpen = !ChatsSidebarOpen);
        ToggleModelsCommand = Command(() => ModelsSidebarOpen = !ModelsSidebarOpen);
        OpenModelOptionsCommand = Command(() => { ModelsSidebarOpen = true; if (Active is { } chat) chat.Session.ChatModelExpanded = true; });
        ToggleArchiveViewCommand = Command(() => ShowArchive(!ShowingArchive), () => NotBusy && (ShowingArchive || ArchivedCount > 0));
        ArchiveChatCommand = Command(ArchiveChat, () => NotBusy && Active is not null);
        NewChatCommand = Command(() => CreateChat(Active?.Session), () => NotBusy);
        DeleteChatCommand = Command(DeleteChat, () => NotBusy && Active is not null);
        SettingsCommand = Command(() => RequestSettings?.Invoke(), () => NotBusy);
        ClearAllCommand = Command(ClearAll, () => NotBusy);
        CreateChat(null);
    }
    private RelayCommand Command(Action action, Func<bool>? allowed = null)
    {
        var command = new RelayCommand(action, () => !disposed && (allowed?.Invoke() ?? true));
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
        var chat = new ConversationViewModel(++nextNumber, session);
        Conversations.Add(chat); showingArchive = false; UpdateVisible(); Active = chat; Refresh();
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(MainViewModel.Busy)) Refresh(); }
    private void OnScrollChat() => ScrollChat?.Invoke();
    private void Release(ConversationViewModel chat)
    { chat.Session.PropertyChanged -= SessionChanged; chat.Session.ScrollChat -= OnScrollChat; chat.Dispose(); }
    private void UpdateVisible()
    {
        VisibleConversations.Clear();
        foreach (var chat in Conversations.Where(chat => chat.IsArchived == ShowingArchive).Reverse()) VisibleConversations.Add(chat);
        NotifyArchive();
    }
    private void NotifyArchive()
    {
        Notify(nameof(ShowingArchive)); Notify(nameof(ArchivedCount)); Notify(nameof(ChatListHeading));
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
    private void ArchiveChat()
    {
        if (Active is not { } chat) return;
        bool restore = chat.IsArchived;
        chat.SetArchived(!restore);
        if (restore) showingArchive = false;
        UpdateVisible(); SelectVisible(restore ? chat : null); Refresh();
    }
    private void DeleteChat()
    {
        if (Active is not { } chat) return;
        Conversations.Remove(chat); Release(chat); UpdateVisible(); SelectVisible(); Refresh();
    }
    private void ClearAll()
    {
        foreach (var chat in Conversations.ToArray()) Release(chat);
        Conversations.Clear(); VisibleConversations.Clear(); active = null; nextNumber = 0; showingArchive = false;
        CreateChat(null); Notify(nameof(Active)); Refresh();
    }
    private void Refresh()
    {
        Notify(nameof(IsBusy)); Notify(nameof(NotBusy)); NotifyArchive();
        foreach (var command in commands) command.Refresh();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var chat in Conversations) Release(chat);
        Conversations.Clear(); VisibleConversations.Clear(); active = null; PickFolderAsync = null;
    }
}
