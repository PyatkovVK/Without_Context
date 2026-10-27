namespace ContextGuard.Core;

public interface IContextCollector
{
    Task<ScanSummary> CollectAsync(string folder, RedactionSession session,
        Func<string, CancellationToken, Task>? inspectLocally, IProgress<int>? progress,
        CancellationToken cancellationToken, int maxContextCharacters,
        Func<byte[], CancellationToken, Task<string>>? inspectImageLocally = null);
}

public interface IContextImageModel
{
    Task<string> DescribeImageAsync(byte[] image, ModelSettings model, CancellationToken ct);
}

public interface ILocalContextModel
{
    Task DiscoverEntitiesAsync(string raw, ProtectionOptions options, RedactionSession session,
        string endpoint, string model, CancellationToken ct);
    Task<string> SummarizeContextAsync(string context, string instructions, int targetCharacters,
        ModelSettings model, CancellationToken ct);
}

public sealed record ContextPreparationSettings(string Folder, ProtectionOptions Protection, int CollectionLimit,
    ContextTrimmingMode Trimming, string Instructions, bool UseLocalModel, ModelSettings LocalModel, string KnownCredential);
public sealed record PreparedContext(RedactionSession Session, ScanSummary Snapshot, string Text, int TargetCharacters);

public sealed class ContextPreparationService(IContextCollector collector, ILocalContextModel localModel, IContextImageModel? imageModel = null)
{
    public async Task<PreparedContext> PrepareAsync(ContextPreparationSettings settings, IProgress<int>? progress, CancellationToken ct)
    {
        if (settings.Trimming == ContextTrimmingMode.Custom)
        {
            if (!settings.UseLocalModel || string.IsNullOrWhiteSpace(settings.LocalModel.Model)) throw new SafeOperationException("CustomTrimmingNeedsLocalModel");
            if (string.IsNullOrWhiteSpace(settings.Instructions)) throw new SafeOperationException("TrimmingInstructionsRequired");
            if (settings.Instructions.Length > 1000) throw new SafeOperationException("TrimmingInstructionsTooLong");
        }
        var session = new RedactionSession(settings.Protection);
        if (!string.IsNullOrWhiteSpace(settings.KnownCredential)) session.AddEntity(settings.KnownCredential, "SECRET");
        // Keep the CPU-intensive collection and anonymization off the presentation thread.
        var snapshot = await Task.Run(() => collector.CollectAsync(settings.Folder, session,
            settings.UseLocalModel ? (raw, token) => localModel.DiscoverEntitiesAsync(raw, settings.Protection, session,
                settings.LocalModel.Endpoint, settings.LocalModel.Model, token) : null,
            progress, ct, settings.CollectionLimit,
            settings.UseLocalModel && imageModel is not null ? (image, token) => imageModel.DescribeImageAsync(image, settings.LocalModel, token) : null), ct);
        ct.ThrowIfCancellationRequested();
        if (snapshot.Included == 0) throw new SafeOperationException("NoTextFiles");
        int target = ContextReducer.TargetCharacters(settings.CollectionLimit, settings.Trimming);
        progress?.Report(0); // Zero marks the trimming phase; positive values are file progress.
        string text;
        if (settings.Trimming == ContextTrimmingMode.Custom)
        {
            string instructions = session.Sanitize(settings.Instructions);
            text = await localModel.SummarizeContextAsync(session.Sanitize(snapshot.Context), instructions, target, settings.LocalModel, ct);
            ct.ThrowIfCancellationRequested();
            text = ContextReducer.LimitText(session.Sanitize(text), target);
        }
        else text = ContextReducer.Reduce(snapshot, target, ct);
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text)) throw new SafeOperationException("TrimmedContextEmpty");
        return new(session, snapshot, text, target);
    }
}
