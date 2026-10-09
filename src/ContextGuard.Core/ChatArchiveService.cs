namespace ContextGuard.Core;

// Only completed, anonymized messages cross this boundary. No connection settings or mapping.
public sealed record StoredMessage(string Role, string Content, DateTimeOffset CreatedAt);
public sealed record StoredConversation(int Version, Guid Id, int Number, DateTimeOffset UpdatedAt,
    bool Archived, string Context, IReadOnlyList<StoredMessage> Messages, string Title = "");

public interface IChatStore
{
    string Folder { get; }
    Task<IReadOnlyList<StoredConversation>> LoadAsync(CancellationToken ct);
    Task ReplaceAsync(IReadOnlyList<StoredConversation> chats, CancellationToken ct);
    Task ExportAsync(IReadOnlyList<StoredConversation> chats, string folder, CancellationToken ct);
}

public sealed class ChatArchiveService : IDisposable
{
    private readonly IChatStore store;
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<StoredConversation>? pending;
    private Task writer = Task.CompletedTask;
    private bool running, disposed;
    private readonly CancellationToken ownerToken;
    public ChatArchiveService(IChatStore store) { this.store = store; ownerToken = lifetime.Token; }
    public string Folder => store.Folder;
    public async Task<IReadOnlyList<StoredConversation>> LoadAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        // Directory enumeration and JSON validation include synchronous work; keep them off the UI.
        var result = await Task.Run(() => store.LoadAsync(deadline.Token), deadline.Token).ConfigureAwait(false);
        deadline.Token.ThrowIfCancellationRequested(); return result;
    }

    public Task SaveAsync(IReadOnlyList<StoredConversation> chats)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // Copy nested collections so callers cannot mutate a queued snapshot.
            pending = chats.Select(chat => chat with { Messages = Array.AsReadOnly(chat.Messages.ToArray()) }).ToArray();
            if (!running)
            {
                running = true;
                writer = Task.Run(WritePendingAsync);
            }
            return writer;
        }
    }

    private async Task WritePendingAsync()
    {
        Exception? failure = null;
        while (true)
        {
            IReadOnlyList<StoredConversation>? snapshot;
            lock (gate)
            {
                snapshot = pending; pending = null;
                if (snapshot is null || disposed) { running = false; break; }
            }
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                await store.ReplaceAsync(snapshot, deadline.Token).ConfigureAwait(false); failure = null;
            }
            catch (OperationCanceledException) when (ownerToken.IsCancellationRequested) { throw; }
            catch (Exception e) { failure = e; }
        }
        if (failure is not null) throw failure;
    }

    public Task FlushAsync() { lock (gate) return writer; }
    public async Task ExportAsync(IReadOnlyList<StoredConversation> chats, string folder)
    {
        await FlushAsync().ConfigureAwait(false);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ownerToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await Task.Run(() => store.ExportAsync(chats, folder, deadline.Token), deadline.Token).ConfigureAwait(false);
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; pending = null; lifetime.Cancel(); }
        lifetime.Dispose();
    }
}
