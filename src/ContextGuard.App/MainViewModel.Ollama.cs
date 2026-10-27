using ContextGuard.Core;

namespace ContextGuard.App;

public sealed record OllamaDownloadOption(string Model, int DownloadMegabytes);

public sealed partial class MainViewModel
{
    private readonly OllamaSetupService? ollamaSetup;
    private string? ollamaExecutable;
    private int ollamaSetupRevision;
    private OllamaAvailability ollamaAvailability;
    private string ollamaNoticeKey = "";
    public Func<Task<string?>>? PickOllamaExecutableAsync { get; set; }
    public OllamaAvailability OllamaAvailability => ollamaAvailability;
    public string OllamaExecutable => ollamaExecutable ?? "";
    public bool HasOllamaExecutable => ollamaExecutable is not null;
    public bool IsOllamaRunning => ollamaAvailability == OllamaAvailability.Running;
    public bool NoInstalledAgentModels => IsOllamaRunning && !HasAgentModels;
    public bool SelectedAgentModelUnavailable => IsOllamaRunning && AgentModel.Length > 0 && !AgentModels.Contains(AgentModel);
    public bool NeedsOllamaExecutable => IsOllamaRunning && !HasOllamaExecutable;
    public bool CanStartOllama => ollamaAvailability == OllamaAvailability.Stopped;
    public bool CanInstallOllamaModel => ollamaAvailability == OllamaAvailability.Running && HasOllamaExecutable;
    public bool NeedsOllamaDownload => ollamaAvailability == OllamaAvailability.Missing;
    public string OllamaSetupMessage => ollamaNoticeKey.Length > 0 ? Text[ollamaNoticeKey] :
        ollamaAvailability == OllamaAvailability.Running ? Text.Format("OllamaRunning", AgentModels.Count) : Text["Ollama" + ollamaAvailability];
    private string modelToInstall = "qwen3:4b";
    // A fixed presentation catalog, not a cached installation list. Sizes are approximate downloads.
    public IReadOnlyList<OllamaDownloadOption> DownloadableAgentModels { get; } = Array.AsReadOnly<OllamaDownloadOption>([
        new("qwen3:4b", 2500), new("qwen3:0.6b", 523), new("qwen3:1.7b", 1400), new("qwen3:8b", 5200),
        new("llama3.2:3b", 2000), new("gemma3:1b", 806), new("gemma3:4b", 3400)
    ]);
    public string ModelToInstall
    {
        get => modelToInstall;
        set { if (Set(ref modelToInstall, value)) { Notify(nameof(SelectedDownloadableAgentModel)); NotifyOllamaModelChoices(); } }
    }
    public OllamaDownloadOption? SelectedDownloadableAgentModel
    {
        get => DownloadableAgentModels.FirstOrDefault(option => option.Model == ModelToInstall);
        set { if (value is not null && DownloadableAgentModels.Contains(value)) ModelToInstall = value.Model; }
    }
    public bool HasSelectedDownloadableAgentModel => SelectedDownloadableAgentModel is not null;
    public bool IsInstallModelPresent => AgentModels.Contains(ModelToInstall);
    public string ModelDownloadHint => IsInstallModelPresent ? Text["OllamaModelAlreadyInstalled"] :
        SelectedDownloadableAgentModel is { } selected ? Text.Format("OllamaDownloadSize", selected.DownloadMegabytes) : "";
    public RelayCommand DetectOllamaCommand { get; private set; } = null!;
    public RelayCommand FindOllamaCommand { get; private set; } = null!;
    public RelayCommand DownloadOllamaCommand { get; private set; } = null!;
    public RelayCommand StartOllamaCommand { get; private set; } = null!;
    public RelayCommand InstallOllamaModelCommand { get; private set; } = null!;

    private void InitializeOllamaCommands()
    {
        DetectOllamaCommand = Command(() => RunAsync(InspectOllamaAsync), () => !Busy && ollamaSetup is not null);
        FindOllamaCommand = Command(() => RunAsync(async ct =>
        {
            string? selected = PickOllamaExecutableAsync is null ? null : await PickOllamaExecutableAsync();
            ct.ThrowIfCancellationRequested();
            if (disposed || selected is null) return;
            await InspectOllamaAsync(ct, selected);
        }), () => !Busy && ollamaSetup is not null && PickOllamaExecutableAsync is not null);
        DownloadOllamaCommand = Command(() => RunAsync(ct =>
        {
            ct.ThrowIfCancellationRequested(); ollamaSetup?.OpenDownloadPage();
            return Task.CompletedTask;
        }), () => !Busy && ollamaSetup is not null);
        StartOllamaCommand = Command(() => RunAsync(ct => InspectOllamaAsync(ct, start: true)),
            () => !Busy && CanStartOllama && ollamaSetup is not null);
        InstallOllamaModelCommand = Command(() => RunAsync(async ct =>
        {
            if (ollamaSetup is null || ollamaExecutable is null) return;
            int revision = ollamaSetupRevision;
            await ollamaSetup.OpenPullTerminalAsync(AgentEndpoint, ollamaExecutable, ModelToInstall, ct);
            ct.ThrowIfCancellationRequested();
            if (disposed || revision != ollamaSetupRevision) return;
            ollamaNoticeKey = "OllamaPullOpened"; NotifyOllamaSetup();
        }), () => !Busy && CanInstallOllamaModel && !IsInstallModelPresent && OllamaSetupService.IsValidPullModel(ModelToInstall));
    }

    private void NotifyOllamaModelChoices()
    {
        Notify(nameof(NoInstalledAgentModels)); Notify(nameof(SelectedAgentModelUnavailable));
        Notify(nameof(HasSelectedDownloadableAgentModel)); Notify(nameof(IsInstallModelPresent)); Notify(nameof(ModelDownloadHint));
        Refresh();
    }

    private void NotifyOllamaSetup()
    {
        Notify(nameof(OllamaAvailability)); Notify(nameof(OllamaExecutable)); Notify(nameof(HasOllamaExecutable));
        Notify(nameof(IsOllamaRunning)); Notify(nameof(NeedsOllamaExecutable));
        Notify(nameof(CanStartOllama)); Notify(nameof(CanInstallOllamaModel)); Notify(nameof(NeedsOllamaDownload));
        Notify(nameof(OllamaSetupMessage)); NotifyOllamaModelChoices();
    }

    private void ResetOllamaSetup(bool cancelOperation = true)
    {
        if (cancelOperation) operation?.Cancel();
        ollamaSetupRevision++; ollamaAvailability = OllamaAvailability.Unchecked;
        ollamaExecutable = null; ollamaNoticeKey = ""; NotifyOllamaSetup();
    }

    private Task InspectOllamaAsync(CancellationToken ct) => InspectOllamaAsync(ct, null);
    private async Task InspectOllamaAsync(CancellationToken ct, string? selected = null, bool start = false)
    {
        if (ollamaSetup is null) return;
        int revision = ollamaSetupRevision;
        string endpoint = AgentEndpoint;
        ollamaNoticeKey = start ? "OllamaStarting" : "OllamaChecking"; NotifyOllamaSetup();
        try
        {
            var result = start && ollamaExecutable is not null
                ? await ollamaSetup.StartAsync(endpoint, ollamaExecutable, ct)
                : await ollamaSetup.InspectAsync(endpoint, selected ?? ollamaExecutable, ct);
            ct.ThrowIfCancellationRequested();
            if (disposed || revision != ollamaSetupRevision) return;
            ollamaExecutable = result.Executable; ollamaAvailability = result.Availability; ollamaNoticeKey = "";
            AgentModels.Clear(); foreach (string model in result.Models) AgentModels.Add(model);
            if (result.Availability != OllamaAvailability.Running || !AgentModels.Contains(AgentModel)) ResetCleanerConnection();
            NotifyOllamaSetup();
        }
        catch
        {
            if (!disposed && revision == ollamaSetupRevision)
            {
                ollamaNoticeKey = "OllamaCheckFailed"; ollamaAvailability = OllamaAvailability.Unchecked;
                AgentModels.Clear(); ResetCleanerConnection(); NotifyOllamaSetup();
            }
            throw;
        }
    }
}
