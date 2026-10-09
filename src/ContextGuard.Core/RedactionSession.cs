using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ContextGuard.Core;

// This object, including the reversible mapping, lives only in memory.
public sealed class RedactionSession
{
    private readonly ProtectionOptions options;
    private readonly Dictionary<string, (string Token, string Category)> replacements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> counters = new();
    private readonly Dictionary<string, string> learned = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> fileAliases = new(StringComparer.Ordinal);
    private readonly string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    public IReadOnlyList<ReplacementInfo> Replacements => replacements.Values.Select(v => new ReplacementInfo(v.Token, v.Category)).ToArray();
    public int Count => replacements.Count;

    public RedactionSession(ProtectionOptions options)
    {
        this.options = options;
        foreach (var term in options.CustomTerms.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            if (term.Trim().Length < 2) throw new SafeOperationException("CustomValueTooShort");
            learned[term.Trim()] = "CUSTOM";
        }
    }

    public void AddEntity(string original, string category = "ENTITY")
    {
        if (original.Length >= 2 && original.Length <= 4000 && !original.Contains("[CG_", StringComparison.Ordinal))
            learned[original] = category;
    }

    public void AddFileAlias(string alias, string localRelativePath) => fileAliases[alias] = localRelativePath;

    private string Token(string value, string category)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (replacements.TryGetValue(value, out var existing)) return existing.Token;
        int next = counters.GetValueOrDefault(category) + 1;
        counters[category] = next;
        string token = $"[CG_{sessionId}_{category}_{next:000}]";
        replacements[value] = (token, category);
        if (value.Length >= 3) learned.TryAdd(value, category);
        return token;
    }

    private static Regex Pattern(string pattern, RegexOptions extra = RegexOptions.None) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | extra, MatchTimeout);

    private string OutsideTokens(string text, Func<string, string> transform)
    {
        // Reopened chats also contain opaque placeholders from an earlier session.
        string[] parts = Regex.Split(text, @"(\[CG_[A-F0-9]{12}_[A-Z]+_\d+\])", RegexOptions.None, MatchTimeout);
        return string.Concat(parts.Select((part, i) => i % 2 == 0 ? transform(part) : part));
    }

    private string Replace(string text, string pattern, string category, RegexOptions extra = RegexOptions.None) =>
        OutsideTokens(text, part => Pattern(pattern, extra).Replace(part, m => Token(m.Value, category)));

    private string ReplaceField(string text, string pattern, string category) => OutsideTokens(text, part => Pattern(pattern).Replace(part, m =>
    {
        var value = m.Groups["value"];
        if (value.Value.StartsWith("[CG_", StringComparison.Ordinal)) return m.Value;
        return m.Value[..(value.Index - m.Index)] + Token(value.Value, category) + m.Value[(value.Index - m.Index + value.Length)..];
    }));

    public string Sanitize(string text)
    {
        try
        {
            // Protect blocks and credentials first, before replacing individual words inside them.
            text = Replace(text, @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|\z)", "SECRET");
            text = Replace(text, @"\b(?:sk-(?:proj-)?[A-Za-z0-9_-]{12,}|gh[pousr]_[A-Za-z0-9]{15,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16}|AIza[A-Za-z0-9_-]{30,}|xox[baprs]-[A-Za-z0-9-]{12,})\b", "SECRET");
            text = Replace(text, @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", "SECRET");
            text = Replace(text, @"(?<=\bBearer\s)[A-Za-z0-9._~+/=-]{8,}", "SECRET");
            text = ReplaceField(text, "(?<![\\w.-])(?<key>[\\\"']?(?:[\\w.-]{0,80}(?:api[_-]?key|password|passwd|pwd|secret|access[_-]?token|refresh[_-]?token|auth[_-]?token|credential|connection[_-]?string)[\\w.-]{0,80}|token|пароль|секрет)[\\\"']?\\s*[:=]\\s*)(?:\\\"(?<value>[^\\\"\\r\\n]+)\\\"|'(?<value>[^'\\r\\n]+)'|(?<value>[^\\s,;}\\\"'\\r\\n]+))", "SECRET");
            text = Replace(text, @"\b(?:postgres(?:ql)?|mysql|mongodb(?:\+srv)?|redis|amqp)://[^\s\""'<>]+", "SECRET");
            text = Replace(text, @"(?<=://)[^/\s:@]+:[^/\s@]+@", "SECRET");

            foreach (var item in learned.OrderByDescending(x => x.Key.Length))
                text = OutsideTokens(text, part => Pattern(Regex.Escape(item.Key)).Replace(part, m => Token(m.Value, item.Value)));

            if (options.Personal)
            {
                text = Replace(text, @"(?<![\w.+-])[\w.!#$%&'*+/=?^`{|}~-]+@[\w-]+(?:\.[\w-]+)+(?![\w.-])", "EMAIL");
                text = Replace(text, @"(?<!\w)(?:\+7|8)[\s(-]*\d{3}[\s)-]*\d{3}[\s-]*\d{2}[\s-]*\d{2}(?!\d)", "PHONE");
                text = Replace(text, @"(?<!\w)\+\d{1,3}(?:[\s().-]*\d){8,12}(?!\d)", "PHONE");
                text = ReplaceField(text, @"(?:ФИО|Ф\.И\.О\.|full[_ ]?name|имя|фамилия|сотрудник|клиент|контакт)\s*[\""']?\s*[:=]\s*[\""']?(?<value>[\p{L}][\p{L} .'-]{2,90})", "PERSON");
                text = Replace(text, @"\b[А-ЯЁ][а-яё]{2,}\s+[А-ЯЁ][а-яё]{2,}\s+[А-ЯЁ][а-яё]*(?:ович|евич|ич|овна|евна|ична)\b", "PERSON", RegexOptions.None);
                text = ReplaceField(text, @"(?:passport|паспорт|инн|снилс|iban|дата рождения|birth[_ ]?date|адрес проживания)\s*[\""']?\s*[:=]\s*[\""']?(?<value>[^\""'\r\n;,]{3,150})", "PERSONAL");
                text = Replace(text, @"(?<!\d)\d{3}-\d{3}-\d{3}\s?\d{2}(?!\d)", "PERSONAL");
                text = Replace(text, @"(?<!\d)\d{2}\s?\d{2}\s\d{6}(?!\d)", "PERSONAL");
                text = OutsideTokens(text, part => Pattern(@"(?<!\d)(?:\d[ -]?){12,18}\d(?!\d)").Replace(part, m => IsCard(m.Value) ? Token(m.Value, "CARD") : m.Value));
            }
            if (options.Internal)
            {
                text = Replace(text, @"\b(?:10(?:\.\d{1,3}){3}|192\.168(?:\.\d{1,3}){2}|172\.(?:1[6-9]|2\d|3[01])(?:\.\d{1,3}){2}|127(?:\.\d{1,3}){3})\b", "ADDRESS");
                text = Replace(text, @"\b(?:fd[0-9a-f]{2}|fe80):[0-9a-f:]+\b", "ADDRESS");
                text = Replace(text, @"(?<!\w)(?:https?://)?[\w-]+(?:\.[\w-]+)*\.(?:internal|local|corp|lan|intranet)(?::\d+)?(?:/[^\s\""'<>]*)?", "ADDRESS");
                text = Replace(text, @"(?:[A-Z]:\\|\\\\)[^\r\n\""'<>|]+", "PATH");
                text = Replace(text, @"/(?:home|Users|srv|opt|mnt)/[^\s\""'<>]+", "PATH");
            }
            return text;
        }
        catch (RegexMatchTimeoutException)
        {
            throw new SafeOperationException("RedactionTimeout");
        }
    }

    private static bool IsCard(string value)
    {
        var digits = value.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length < 13 || digits.Length > 19 || digits.Distinct().Count() <= 1) return false;
        int total = 0;
        for (int i = digits.Length - 1, n = 0; i >= 0; i--, n++)
        {
            int digit = digits[i];
            if (n % 2 == 1) { digit *= 2; if (digit > 9) digit -= 9; }
            total += digit;
        }
        return total % 10 == 0;
    }

    // Restoration is used only for a separately requested local display, never for HTTP messages.
    public string RestoreForLocalDisplay(string text)
    {
        foreach (var item in replacements) text = text.Replace(item.Value.Token, item.Key, StringComparison.Ordinal);
        foreach (var item in fileAliases.OrderByDescending(p => p.Key.Length)) text = text.Replace(item.Key, item.Value, StringComparison.Ordinal);
        return text;
    }
}
