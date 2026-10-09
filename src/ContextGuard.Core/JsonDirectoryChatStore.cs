using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextGuard.Core;

public sealed class JsonDirectoryChatStore : IChatStore
{
    public const int MaxChats = 200, MaxMessages = 512, MaxFileBytes = 8 * 1024 * 1024;
    public const long MaxTotalBytes = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false, MaxDepth = 12,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public string Folder { get; }
    public JsonDirectoryChatStore(string folder) => Folder = Path.GetFullPath(folder);

    private static void CheckPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new SafeOperationException("ChatStorageUnsafePath");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    private static bool OwnedFile(string path) => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)
        && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
    private static void Validate(StoredConversation chat)
    {
        if (chat.Version is not (1 or 2) || chat.Id == Guid.Empty || chat.Number is < 1 or > 1_000_000 || chat.Context is null ||
            chat.Title is null || chat.Title.Length > ConversationTitleService.MaxCharacters || chat.Title.Contains('\n') || chat.Title.Contains('\r') ||
            chat.Context.Length > FolderCollector.MaxContextChars || chat.Messages is null ||
            chat.Messages.Count > MaxMessages || chat.Messages.Count % 2 != 0 ||
            chat.Messages.Where((m, i) => m is null || m.Role != (i % 2 == 0 ? "user" : "assistant") ||
                m.Content is null || m.Content.Length > 4 * 1024 * 1024).Any())
            throw new SafeOperationException("ChatStorageInvalid");
        // Bound serialization allocation before creating UTF-8 JSON (escaping can expand text).
        if ((long)chat.Context.Length + chat.Messages.Sum(m => (long)m.Content.Length) > MaxFileBytes)
            throw new SafeOperationException("ChatStorageLimit");
    }
    private static byte[] Serialize(StoredConversation chat)
    {
        Validate(chat);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(chat, Json);
        if (bytes.Length > MaxFileBytes) throw new SafeOperationException("ChatStorageLimit");
        return bytes;
    }
    private static void ValidateSet(IReadOnlyList<StoredConversation> chats)
    {
        if (chats.Count > MaxChats || chats.Select(c => c.Id).Distinct().Count() != chats.Count)
            throw new SafeOperationException("ChatStorageLimit");
    }

    public async Task<IReadOnlyList<StoredConversation>> LoadAsync(CancellationToken ct)
    {
        try
        {
            CheckPath(Folder);
            if (!Directory.Exists(Folder)) return [];
            var chats = new List<StoredConversation>();
            long total = 0; int entries = 0;
            foreach (string path in Directory.EnumerateFiles(Folder))
            {
                ct.ThrowIfCancellationRequested();
                if (++entries > 1000) throw new SafeOperationException("ChatStorageLimit");
                if (!OwnedFile(path)) continue;
                CheckPath(path);
                long length = new FileInfo(path).Length;
                total += length;
                if (length > MaxFileBytes || total > MaxTotalBytes || chats.Count == MaxChats)
                    throw new SafeOperationException("ChatStorageLimit");
                // Read at most the checked limit, even if another process grows the file.
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var bytes = new MemoryStream();
                var buffer = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    if (bytes.Length + read > MaxFileBytes) throw new SafeOperationException("ChatStorageLimit");
                    await bytes.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                total += bytes.Length - length;
                if (total > MaxTotalBytes) throw new SafeOperationException("ChatStorageLimit");
                var chat = JsonSerializer.Deserialize<StoredConversation>(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), Json)
                    ?? throw new SafeOperationException("ChatStorageInvalid");
                Validate(chat);
                if (chat.Id.ToString("N") != Path.GetFileNameWithoutExtension(path)) throw new SafeOperationException("ChatStorageInvalid");
                chats.Add(chat);
            }
            ValidateSet(chats);
            return chats.OrderBy(c => c.Number).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new SafeOperationException("ChatStorageReadFailed"); }
    }

    public async Task ReplaceAsync(IReadOnlyList<StoredConversation> chats, CancellationToken ct)
    {
        try
        {
            ValidateSet(chats); long total = 0;
            // Validate the whole transaction before replacing or deleting any file.
            foreach (var chat in chats) { ct.ThrowIfCancellationRequested(); total += Serialize(chat).Length; if (total > MaxTotalBytes) throw new SafeOperationException("ChatStorageLimit"); }
            CheckPath(Folder); Directory.CreateDirectory(Folder);
            foreach (var chat in chats)
                await AtomicWriteAsync(Path.Combine(Folder, chat.Id.ToString("N") + ".json"), Serialize(chat), ct).ConfigureAwait(false);
            var keep = chats.Select(c => c.Id.ToString("N") + ".json").ToHashSet(StringComparer.OrdinalIgnoreCase);
            int entries = 0;
            foreach (string path in Directory.EnumerateFiles(Folder))
            {
                ct.ThrowIfCancellationRequested();
                if (++entries > 1000) throw new SafeOperationException("ChatStorageLimit");
                if (OwnedFile(path) && !keep.Contains(Path.GetFileName(path))) { CheckPath(path); File.Delete(path); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new SafeOperationException("ChatStorageWriteFailed"); }
    }

    public async Task ExportAsync(IReadOnlyList<StoredConversation> chats, string folder, CancellationToken ct)
    {
        try
        {
            ValidateSet(chats); folder = Path.GetFullPath(folder); CheckPath(folder);
            string relative = Path.GetRelativePath(Folder, folder);
            if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                throw new SafeOperationException("ChatExportOwnFolder");
            Directory.CreateDirectory(folder);
            long total = 0;
            foreach (var chat in chats)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bytes = Serialize(chat); total += bytes.Length;
                if (total > MaxTotalBytes) throw new SafeOperationException("ChatStorageLimit");
                string stem = Path.Combine(folder, "chat-" + chat.Id.ToString("N"));
                await AtomicWriteAsync(stem + ".json", bytes, ct).ConfigureAwait(false);
                var text = new StringBuilder("# ").Append(chat.Title.Length > 0 ? chat.Title : "Without Context").Append("\n\n");
                text.Append("Updated: ").Append(chat.UpdatedAt.ToString("O")).Append("\n\n");
                foreach (var message in chat.Messages)
                    text.Append("## ").Append(message.Role == "user" ? "You" : "Assistant").Append(" · ")
                        .Append(message.CreatedAt.ToString("O")).Append("\n\n").Append(message.Content).Append("\n\n");
                if (chat.Context.Length > 0) text.Append("## Anonymized context\n\n").Append(chat.Context).Append('\n');
                await AtomicWriteAsync(stem + ".md", Encoding.UTF8.GetBytes(text.ToString()), ct).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new SafeOperationException("ChatExportFailed"); }
    }
    private static async Task AtomicWriteAsync(string path, byte[] bytes, CancellationToken ct)
    {
        CheckPath(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                8192, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested(); CheckPath(path);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
