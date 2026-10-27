namespace ContextGuard.Core;

public sealed class FolderCollector : IContextCollector
{
    public const int MaxFiles = 160;
    public const int MaxFileBytes = 128 * 1024;
    public const int MaxContextChars = 96_000;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "vendor", "venv", ".venv", "__pycache__", "bin", "obj", "dist", "build", ".next", ".idea", ".vs", ".aws", ".ssh", ".azure", ".kube", ".codex", ".agents"
    };
    public static bool IsExcludedFile(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower == ".env" || lower.StartsWith(".env.") || lower.StartsWith("secrets.") ||
            lower.Contains("credentials") || lower.Contains("id_rsa") || lower.Contains("id_ed25519") ||
            lower is ".npmrc" or ".pypirc" or "package-lock.json" or "yarn.lock" or "pnpm-lock.yaml" or "poetry.lock" ||
            Path.GetExtension(lower) is ".pem" or ".key" or ".pfx" or ".p12" or ".cer";
    }

    public async Task<ScanSummary> CollectAsync(string folder, RedactionSession session,
        Func<string, CancellationToken, Task>? inspectLocally, IProgress<int>? progress, CancellationToken cancellationToken,
        int maxContextCharacters = 24_000, Func<byte[], CancellationToken, Task<string>>? inspectImageLocally = null)
    {
        maxContextCharacters = Math.Clamp(maxContextCharacters, 4000, MaxContextChars);
        string root;
        try
        {
            root = Path.GetFullPath(folder);
            if (!Directory.Exists(root)) throw new SafeOperationException("FolderNotFound");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new SafeOperationException("FolderIsLink");
        }
        catch (SafeOperationException) { throw; }
        catch { throw new SafeOperationException("FolderOpenFailed"); }

        var safeFiles = new List<SafeFile>();
        var skippedFiles = new List<SkippedContextFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        int skipped = 0, characters = 0, visited = 0;
        bool limited = false;
        long inputBytes = 0;
        void Skip(string path, string reason)
        {
            skipped++;
            if (skippedFiles.Count < MaxFiles) skippedFiles.Add(new(Path.GetRelativePath(root, path), reason));
        }
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; continue; }
            foreach (string path in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visited > 15_000) { limited = true; break; }
                FileAttributes attributes;
                try { attributes = File.GetAttributes(path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                string name = Path.GetFileName(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (ExcludedDirectories.Contains(name)) { skipped++; continue; }
                    pending.Push(path);
                    continue;
                }
                if (IsExcludedFile(name)) { Skip(path, "FileExcluded"); continue; }
                if (safeFiles.Count >= MaxFiles || characters >= maxContextCharacters) { limited = true; break; }
                byte[] bytes;
                string extension = Path.GetExtension(name).ToLowerInvariant();
                int fileLimit = ContextFileTextReader.IsAttachment(extension) ? ContextFileTextReader.MaxDocumentBytes : MaxFileBytes;
                try
                {
                    // Recheck the path immediately before opening, and cap reads even if a file grows.
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > fileLimit) { Skip(path, "FileTooLarge"); continue; }
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                    bytes = new byte[fileLimit + 1];
                    int read = 0, next;
                    while (read < bytes.Length && (next = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken)) > 0) read += next;
                    if (read > fileLimit) { Skip(path, "FileTooLarge"); continue; }
                    inputBytes += read;
                    if (inputBytes > 64 * 1024 * 1024) { limited = true; break; }
                    Array.Resize(ref bytes, read);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Skip(path, "FileUnreadable"); continue; }
                string raw;
                try
                {
                    raw = await ContextFileTextReader.ReadAsync(extension, bytes, inspectImageLocally, cancellationToken);
                }
                catch (SafeOperationException e) when (e.MessageKey is "FileUnsupported" or "FileUnreadable" or "FileTooLarge")
                { Skip(path, e.MessageKey); continue; }
                catch (Exception e) when (e is InvalidDataException or System.Xml.XmlException or ArgumentException)
                { Skip(path, "FileUnreadable"); continue; }
                if (string.IsNullOrWhiteSpace(raw)) { Skip(path, "FileEmpty"); continue; }
                if (raw.Length + characters > maxContextCharacters) { limited = true; Skip(path, "FileContextLimit"); continue; }
                progress?.Report(safeFiles.Count + 1);
                // Never send raw file names. Aliases preserve only the extension.
                if (inspectLocally is not null) await inspectLocally(raw, cancellationToken);
                string safe = session.Sanitize(raw);
                if (safe.Length + characters > maxContextCharacters) { limited = true; Skip(path, "FileContextLimit"); continue; }
                string alias = $"file_{safeFiles.Count + 1:000}{ContextFileTextReader.AliasExtension(extension)}";
                string relativePath = Path.GetRelativePath(root, path);
                session.AddFileAlias(alias, relativePath);
                safeFiles.Add(new SafeFile(alias, safe, relativePath));
                characters += safe.Length;
            }
            if (visited > 15_000 || safeFiles.Count >= MaxFiles || characters >= maxContextCharacters || inputBytes > 64 * 1024 * 1024) { limited = true; break; }
        }
        // Entities learned in later files must also be removed from earlier files.
        var finalFiles = new List<SafeFile>();
        int finalCharacters = 0;
        foreach (var file in safeFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var updated = file with { Content = session.Sanitize(file.Content) };
            if (finalCharacters + updated.Content.Length > maxContextCharacters) { limited = true; skipped++; continue; }
            finalFiles.Add(updated); finalCharacters += updated.Content.Length;
        }
        return new ScanSummary(finalFiles.Count, skipped, finalCharacters, limited, finalFiles, skippedFiles);
    }
}
