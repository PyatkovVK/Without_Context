using System.Text;

namespace ContextGuard.Core;

public sealed class FolderCollector
{
    public const int MaxFiles = 160;
    public const int MaxFileBytes = 128 * 1024;
    public const int MaxContextChars = 96_000;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "vendor", "venv", ".venv", "__pycache__", "bin", "obj", "dist", "build", ".next", ".idea", ".vs", ".aws", ".ssh", ".azure", ".kube", ".codex", ".agents"
    };
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".cs", ".xaml", ".csproj", ".fs", ".vb", ".py", ".js", ".jsx", ".ts", ".tsx", ".html", ".css", ".scss", ".json", ".yaml", ".yml", ".toml", ".xml", ".sql", ".go", ".rs", ".java", ".kt", ".php", ".rb", ".c", ".cpp", ".h", ".hpp", ".sh", ".ps1", ".bat", ".ini", ".cfg", ".conf", ".csv", ".log", ".r", ".vue", ".svelte"
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
        int maxContextCharacters = 24_000)
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
        var pending = new Stack<string>();
        pending.Push(root);
        int skipped = 0, characters = 0, visited = 0;
        bool limited = false;
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
                if (IsExcludedFile(name) || (!Extensions.Contains(Path.GetExtension(name)) && name is not "Dockerfile" and not "Makefile")) { skipped++; continue; }
                if (safeFiles.Count >= MaxFiles || characters >= maxContextCharacters) { limited = true; break; }
                byte[] bytes;
                try
                {
                    // Recheck the path immediately before opening, and cap reads even if a file grows.
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > MaxFileBytes) { skipped++; continue; }
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
                    bytes = new byte[MaxFileBytes + 1];
                    int read = 0, next;
                    while (read < bytes.Length && (next = await stream.ReadAsync(bytes.AsMemory(read), cancellationToken)) > 0) read += next;
                    if (read > MaxFileBytes) { skipped++; continue; }
                    Array.Resize(ref bytes, read);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipped++; continue; }
                string raw;
                try
                {
                    if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) raw = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
                    else if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) raw = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
                    else raw = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                    if (raw.Contains('\0')) { skipped++; continue; }
                }
                catch (DecoderFallbackException) { skipped++; continue; }
                if (raw.Length == 0) { skipped++; continue; }
                if (raw.Length + characters > maxContextCharacters) { limited = true; skipped++; continue; }
                progress?.Report(safeFiles.Count + 1);
                // Never send raw file names. Aliases preserve only the extension.
                if (inspectLocally is not null) await inspectLocally(raw, cancellationToken);
                string safe = session.Sanitize(raw);
                if (safe.Length + characters > maxContextCharacters) { limited = true; skipped++; continue; }
                string alias = $"file_{safeFiles.Count + 1:000}{Path.GetExtension(name).ToLowerInvariant()}";
                string relativePath = Path.GetRelativePath(root, path);
                session.AddFileAlias(alias, relativePath);
                safeFiles.Add(new SafeFile(alias, safe, relativePath));
                characters += safe.Length;
            }
            if (visited > 15_000 || safeFiles.Count >= MaxFiles || characters >= maxContextCharacters) { limited = true; break; }
        }
        // Entities learned in later files must also be removed from earlier files.
        var finalFiles = new List<SafeFile>();
        int finalCharacters = 0;
        foreach (var file in safeFiles)
        {
            var updated = file with { Content = session.Sanitize(file.Content) };
            if (finalCharacters + updated.Content.Length > maxContextCharacters) { limited = true; skipped++; continue; }
            finalFiles.Add(updated); finalCharacters += updated.Content.Length;
        }
        return new ScanSummary(finalFiles.Count, skipped, finalCharacters, limited, finalFiles);
    }
}
