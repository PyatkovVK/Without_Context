using System.Text.RegularExpressions;

namespace ContextGuard.Core;

public interface IConversationTitleModel
{
    Task<string> GenerateTitleAsync(ModelSettings settings, string safeGoal, string safeContext, CancellationToken ct);
}

// The caller supplies only reviewed, anonymized input. The output still requires final anonymization.
public sealed class ConversationTitleService(IConversationTitleModel model)
{
    public const int MaxCharacters = 64;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    public async Task<string?> GenerateAsync(ModelSettings settings, string safeGoal, string safeContext, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            string result = await model.GenerateTitleAsync(settings, ContextReducer.LimitText(safeGoal, 2000),
                ContextReducer.LimitText(safeContext, 4000), deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return Normalize(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; } // Naming is optional; failure must not invalidate a completed chat reply.
    }

    public static string? Normalize(string text)
    {
        if (text.Length > 256 || text.Contains('\n') || text.Contains('\r')) return null;
        text = Clean(text).Trim(' ', '"', '\'', '`', '#', '*', '.', ':', '«', '»', '“', '”');
        if (text.Length == 0 || !text.Any(char.IsLetter) || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) return null;
        return ContextReducer.LimitText(text, MaxCharacters);
    }

    public static string Fallback(string safeGoal)
    {
        string text = Clean(ContextReducer.LimitText(safeGoal, 2000));
        text = Regex.Replace(text, @"^(?:(?:hi|hello|hey|привет|здравствуйте|добрый день)[!,.\s]*)+", "", RegexOptions.IgnoreCase, RegexTimeout);
        for (int i = 0; i < 3; i++)
            text = Regex.Replace(text, @"^(?:please\s+|help\s+me\s+(?:to\s+)?|i\s+(?:want|need)\s+to\s+|пожалуйста[,\s]+|помоги(?:те)?\s+(?:мне\s+)?|мне\s+нужно\s+|я\s+хочу\s+|(?:task|задача)\s*:\s*)", "", RegexOptions.IgnoreCase, RegexTimeout);
        text = text.Split(['\r', '\n', '.', '!', '?', ';'], 2)[0].Trim();
        if (text.Length == 0 || !text.Any(char.IsLetter)) return "";
        text = char.ToUpperInvariant(text[0]) + text[1..];
        return ContextReducer.LimitText(text, MaxCharacters);
    }

    private static string Clean(string text) => Regex.Replace(
        Regex.Replace(text, @"\[CG_[A-F0-9]{12}_[A-Z]+_\d+\]", "", RegexOptions.None, RegexTimeout),
        @"[^\S\r\n]+", " ", RegexOptions.None, RegexTimeout).Trim();
}
