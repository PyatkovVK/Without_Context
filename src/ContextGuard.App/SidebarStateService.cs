using System.Text.Json;
using System.Text.Json.Serialization;
using ContextGuard.Core;

namespace ContextGuard.App;

// This contract deliberately accepts only the two sidebar preferences.
public sealed record SidebarState(bool ChatsSidebarOpen = true, bool ModelsSidebarOpen = true);

public interface ISidebarStateService
{
    Task<SidebarState> LoadAsync(CancellationToken ct);
    Task SaveAsync(SidebarState state, CancellationToken ct);
}

public sealed class SidebarStateService : ISidebarStateService
{
    public const int MaxFileBytes = 4096;
    private readonly string folder, path;
    private readonly SemaphoreSlim writer = new(1, 1);
    private static readonly JsonSerializerOptions Json = new()
    {
        MaxDepth = 3, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private sealed record Document(
        [property: JsonRequired] int Version,
        [property: JsonRequired] bool ChatsSidebarOpen,
        [property: JsonRequired] bool ModelsSidebarOpen);

    public SidebarStateService(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        path = Path.Combine(this.folder, "sidebar-state.local.json");
    }

    public async Task<SidebarState> LoadAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            CheckPath(path);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                MaxFileBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaxFileBytes) return new();
            // Bound the actual read even if a file grows after its length was checked.
            var bytes = new byte[MaxFileBytes + 1];
            int count = 0, read;
            while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token).ConfigureAwait(false)) > 0)
                count += read;
            deadline.Token.ThrowIfCancellationRequested();
            if (count > MaxFileBytes) return new();
            var document = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, count), Json);
            return document is { Version: 1 }
                ? new(document.ChatsSidebarOpen, document.ModelsSidebarOpen) : new();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException) { return new(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SafeOperationException("SidebarStateReadFailed"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new SafeOperationException("SidebarStateReadFailed"); }
    }

    public async Task SaveAsync(SidebarState state, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        bool entered = false;
        string? temporary = null;
        try
        {
            await writer.WaitAsync(deadline.Token).ConfigureAwait(false); entered = true;
            CheckPath(path); Directory.CreateDirectory(folder);
            temporary = Path.Combine(folder, ".sidebar-state-" + Guid.NewGuid().ToString("N") + ".tmp");
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, state.ChatsSidebarOpen, state.ModelsSidebarOpen), Json);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                MaxFileBytes, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, deadline.Token).ConfigureAwait(false);
                await stream.FlushAsync(deadline.Token).ConfigureAwait(false);
            }
            deadline.Token.ThrowIfCancellationRequested(); CheckPath(path);
            File.Move(temporary, path, overwrite: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SafeOperationException("SidebarStateWriteFailed"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new SafeOperationException("SidebarStateWriteFailed"); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (entered) writer.Release();
        }
    }

    private static void CheckPath(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Redirected sidebar state path.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
