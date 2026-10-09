using System.Text;

namespace ContextGuard.Core;

public enum ContextTrimmingMode { Minimal, Moderate, Maximum, Custom }

public static class ContextReducer
{
    public static int TargetCharacters(int collectionLimit, ContextTrimmingMode mode) => Math.Max(1000,
        Math.Clamp(collectionLimit, 4000, FolderCollector.MaxContextChars) * (mode switch
        { ContextTrimmingMode.Minimal => 3, ContextTrimmingMode.Maximum => 1, _ => 2 }) / 4);

    public static int RetainedMessages(ContextTrimmingMode mode) => mode switch
    { ContextTrimmingMode.Minimal => 12, ContextTrimmingMode.Maximum => 2, _ => 6 };

    public static IReadOnlyList<ChatMessage> SelectHistory(IReadOnlyList<ChatMessage> history, ChatMessage next, ContextTrimmingMode mode)
    {
        // Retain complete user/assistant pairs; the new user message is always included.
        int start = Math.Max(0, history.Count - RetainedMessages(mode));
        while (start < history.Count && history[start].Role != "user") start++;
        return history.Skip(start).Append(next).ToArray();
    }

    public static string Reduce(ScanSummary snapshot, int budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string original = snapshot.Context;
        if (original.Length <= budget) return original;
        var result = new StringBuilder();
        for (int i = 0; i < snapshot.Files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = snapshot.Files[i];
            string header = $"--- FILE: {file.Alias} ---\n";
            int quota = (budget - result.Length) / (snapshot.Files.Count - i);
            int available = quota - header.Length - (result.Length > 0 ? 2 : 0);
            if (available <= 0) continue;
            string content = LimitText(file.Content, available);
            if (content.Length == 0) continue;
            if (result.Length > 0) result.Append("\n\n");
            result.Append(header).Append(content);
        }
        if (result.Length == 0) throw new SafeOperationException("TrimmedContextEmpty");
        return result.ToString();
    }

    public static string LimitText(string value, int budget)
    {
        if (budget <= 0) return "";
        if (value.Length <= budget) return value;
        int end = Math.Max(0, budget - 1);
        // Never split an anonymization placeholder or a UTF-16 surrogate pair.
        int token = end > 0 ? value.LastIndexOf('[', end - 1) : -1;
        if (token >= 0 && value.AsSpan(token).StartsWith("[CG_", StringComparison.Ordinal) && value.IndexOf(']', token) >= end) end = token;
        if (end > 0 && char.IsHighSurrogate(value[end - 1])) end--;
        int line = end > 0 ? value.LastIndexOf('\n', end - 1) : -1;
        if (line >= end * 3 / 4) end = line;
        else
        {
            int space = end > 0 ? value.LastIndexOf(' ', end - 1) : -1;
            if (space >= end * 3 / 4) end = space;
        }
        return end == 0 ? "" : value[..end].TrimEnd() + "…";
    }
}
