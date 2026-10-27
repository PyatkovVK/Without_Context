using ContextGuard.Core;

namespace ContextGuard.App;

public sealed partial class MainViewModel
{
    private bool cleanerVerified, assistantVerified, editingCleaner, editingAssistant;
    private bool cleanerFollowsAssistant;
    private bool cleanerManuallyDisabled, assistantDisabled;
    private bool CleanerSwitchActive => UseAgent || cleanerConnection == ConnectionState.Checking;
    private bool AssistantSwitchActive => !assistantDisabled && (assistantVerified || chatConnection == ConnectionState.Checking);
    public string CleanerToggleTip => Text[CleanerSwitchActive ? "DisableCleanerHint" : "EnableCleanerHint"];
    public string AssistantToggleTip => Text[AssistantSwitchActive ? "DisableAssistantHint" : "EnableAssistantHint"];
    public bool CleanerConnected => UseAgent && cleanerVerified;
    public bool AssistantConnected => assistantVerified;
    public bool HasContextFolder => !string.IsNullOrWhiteSpace(Folder);
    public bool WorkspaceReady => AssistantConnected;
    public bool CanSendReviewedMessage => CanCompose && session is not null && (!HasContextFolder || Ready) &&
        (string.IsNullOrWhiteSpace(ContextText) || ContextApproved);
    public bool CanUseCustomTrimming => CustomCodeCleanup || (UseAgent && !string.IsNullOrWhiteSpace(AgentModel) && !string.IsNullOrWhiteSpace(TrimmingInstructions));
    public ContextTrimmingMode EffectiveTrimmingMode => CustomTrimming
        ? CustomCodeCleanup ? ContextTrimmingMode.Code : CanUseCustomTrimming ? ContextTrimmingMode.Custom : ContextTrimmingMode.Moderate
        : TrimmingMode;
    public bool ShowAutomaticTrimmingHint => CustomTrimming && !CanUseCustomTrimming;
    public string AutomaticTrimmingHint => Text[UseAgent && !string.IsNullOrWhiteSpace(AgentModel) ? "CustomTrimmingEnterInstructions" : "CustomTrimmingAutomatic"];
    public string WorkspaceWelcomeTitle => Text[HasContextFolder ? "WorkspaceReadyTitle" : "ReadyToChatTitle"];
    public string WorkspaceWelcomeHelp => Text[HasContextFolder ? "WorkspaceReadyHelp" : "ReadyToChatHelp"];
    public bool CanStartContextPreparation => HasContextFolder && !Busy;
    public bool SetupRequired => !WorkspaceReady;
    public bool ShowMainWorkArea => WorkspaceReady || HasChat;
    public bool ShowSetupGate => SetupRequired && !HasChat;
    public bool ShowCleanerSummary => CleanerConnected && !editingCleaner;
    public bool ShowAssistantSummary => AssistantConnected && !editingAssistant;
    public bool ShowCleanerEditor => !ShowCleanerSummary;
    public bool ShowAssistantEditor => !ShowAssistantSummary;
    public string FolderReadinessColor => HasContextFolder ? "#71D6B3" : "#687C96";
    public string CleanerReadinessColor => CleanerConnected ? "#71D6B3" : "#687C96";
    public string AssistantReadinessColor => AssistantConnected ? "#71D6B3" : "#687C96";
    public RelayCommand EditCleanerCommand { get; private set; } = null!;
    public RelayCommand EditAssistantCommand { get; private set; } = null!;
    public RelayCommand ToggleCleanerCommand { get; private set; } = null!;
    public RelayCommand ToggleAssistantCommand { get; private set; } = null!;

    private void InitializeSetupCommands()
    {
        ToggleCleanerCommand = Command(() =>
        {
            if (CleanerSwitchActive) DisableCleaner();
            else if (string.IsNullOrWhiteSpace(AgentModel)) EditCleanerCommand.Execute(null);
            else RunAsync(ResumeCleanerAsync);
        }, () => !disposed && (!Busy || CleanerSwitchActive));
        ToggleAssistantCommand = Command(() =>
        {
            if (AssistantSwitchActive) DisableAssistant();
            else if (!ModelConfigured) EditAssistantCommand.Execute(null);
            else RunAsync(ct => ConnectAndPrepareAsync(false, ct));
        }, () => !disposed && (!Busy || AssistantSwitchActive));
        EditCleanerCommand = Command(() =>
        {
            editingCleaner = true; NotifySetup(); CleanerModelExpanded = true;
        }, () => !Busy);
        EditAssistantCommand = Command(() =>
        {
            editingAssistant = true; NotifySetup(); ChatModelExpanded = true;
        }, () => !Busy);
    }

    private void DisableCleaner()
    {
        operation?.Cancel();
        cleanerManuallyDisabled = true; cleanerFollowsAssistant = false; contextRevision++;
        Set(ref useAgent, false, nameof(UseAgent)); ResetCleanerConnection();
        ContextApproved = false; ClearPending();
        SetStatus("StatusCleanerDisabled"); Refresh();
    }

    private async Task ResumeCleanerAsync(CancellationToken ct)
    {
        int revision = cleanerRevision;
        await CheckConnectionAsync(true, ct);
        ct.ThrowIfCancellationRequested();
        if (disposed || revision != cleanerRevision) return;
        cleanerManuallyDisabled = false; cleanerFollowsAssistant = false; contextRevision++;
        Set(ref useAgent, true, nameof(UseAgent));
        ContextApproved = false; ClearPending();
        SetConnection(true, ConnectionState.Connected); CompleteConnectionEditing(true);
        SetStatus("StatusCleanerConnected"); Refresh();
    }

    private void DisableAssistant()
    {
        operation?.Cancel(); CancelTitle();
        assistantDisabled = true; ResetChatConnection(); ClearPending();
        SetStatus("StatusAssistantDisabled"); Refresh();
    }

    private void CompleteConnectionEditing(bool cleaner)
    {
        if (cleaner) { editingCleaner = false; CleanerModelExpanded = false; }
        else { editingAssistant = false; ChatModelExpanded = false; }
        NotifySetup();
    }

    private async Task ConnectAndPrepareAsync(bool cleaner, CancellationToken ct)
    {
        int revision = cleaner ? cleanerRevision : chatRevision;
        await CheckConnectionAsync(cleaner, ct, allowNeutralProbe: true);
        ct.ThrowIfCancellationRequested();
        if (disposed || revision != (cleaner ? cleanerRevision : chatRevision)) return;
        if (cleaner)
        {
            EnableConnectedLocalProcessing(AgentEndpoint, AgentModel, followAssistant: false);
            if (!AssistantConnected && !assistantDisabled)
            {
                ProviderIndex = 0; OllamaEndpoint = AgentEndpoint; LocalModel = AgentModel;
                SetConnection(false, ConnectionState.Connected); CompleteConnectionEditing(false);
                cleanerFollowsAssistant = true;
            }
        }
        else if (IsOllama && !cleanerManuallyDisabled && (cleanerFollowsAssistant || !UseAgent || string.IsNullOrWhiteSpace(AgentModel)))
            EnableConnectedLocalProcessing(OllamaEndpoint, LocalModel, followAssistant: true);
        else if (IsRemote && cleanerFollowsAssistant)
        {
            Set(ref useAgent, false, nameof(UseAgent)); cleanerFollowsAssistant = false;
            ResetCleanerConnection(); ResetCleanerContext(cancelOperation: false);
        }
        if (!AssistantConnected) { SetStatus("StatusCleanerConnected"); NotifySetup(); Refresh(); return; }
        if (HasContextFolder && !Ready) await PrepareAsync(ct);
        else
        {
            if (!HasContextFolder) EnsureConversationSession();
            SetStatus(HasContextFolder && !ContextApproved ? "StatusReviewContext" : "StatusReadyToChat");
        }
        NotifySetup(); Refresh();
    }

    private void EnableConnectedLocalProcessing(string endpoint, string model, bool followAssistant)
    {
        cleanerManuallyDisabled = false;
        bool endpointChanged = Set(ref agentEndpoint, endpoint, nameof(AgentEndpoint));
        bool modelChanged = Set(ref agentModel, model, nameof(AgentModel));
        bool enabled = Set(ref useAgent, true, nameof(UseAgent));
        cleanerFollowsAssistant = followAssistant;
        if (endpointChanged) { AgentModels.Clear(); ResetOllamaSetup(cancelOperation: false); }
        if (endpointChanged || modelChanged || enabled)
        {
            cleanerRevision++;
            Notify(nameof(SelectedAgentModel)); Notify(nameof(SelectedAgentModelUnavailable));
            ResetCleanerContext(cancelOperation: false);
        }
        SetConnection(true, ConnectionState.Connected); CompleteConnectionEditing(true);
    }

    private void EnsureConversationSession()
    {
        if (session is not null) return;
        session = new RedactionSession(Options());
        if (!string.IsNullOrWhiteSpace(ApiKey)) session.AddEntity(ApiKey, "SECRET");
        scan = new(0, 0, 0, false, []);
        Refresh();
    }

    private void NotifyTrimmingAvailability()
    {
        Notify(nameof(CanUseCustomTrimming)); Notify(nameof(EffectiveTrimmingMode)); Notify(nameof(ShowAutomaticTrimmingHint));
        Notify(nameof(ShowTrimmingInstructions)); Notify(nameof(ShowCodeCleanupHint));
        Notify(nameof(AutomaticTrimmingHint)); Notify(nameof(TrimmingSummary));
    }

    private void NotifySetup()
    {
        foreach (string name in new[] { nameof(CleanerConnected), nameof(AssistantConnected), nameof(HasContextFolder),
            nameof(WorkspaceReady), nameof(SetupRequired), nameof(ShowMainWorkArea), nameof(ShowSetupGate), nameof(ShowCleanerSummary), nameof(ShowAssistantSummary),
            nameof(ShowCleanerEditor), nameof(ShowAssistantEditor), nameof(FolderReadinessColor),
            nameof(CleanerReadinessColor), nameof(AssistantReadinessColor), nameof(CanSendReviewedMessage),
            nameof(WorkspaceWelcomeTitle), nameof(WorkspaceWelcomeHelp), nameof(CanStartContextPreparation) }) Notify(name);
        Notify(nameof(CleanerToggleTip)); Notify(nameof(AssistantToggleTip));
        NotifyTrimmingAvailability();
    }
}
