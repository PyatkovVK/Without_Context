namespace ContextGuard.Core;

public enum ProtectionProfile { Everything, PersonalAndSecrets, InternalAndSecrets, SecretsOnly }
public enum ModelProvider { Ollama, RemoteApi }
public sealed record ProtectionOptions(ProtectionProfile Profile, IReadOnlyList<string> CustomTerms)
{
    public bool Personal => Profile is ProtectionProfile.Everything or ProtectionProfile.PersonalAndSecrets;
    public bool Internal => Profile is ProtectionProfile.Everything or ProtectionProfile.InternalAndSecrets;
}
public sealed record ModelSettings(ModelProvider Provider, string Endpoint, string Model, string ApiKey = "");
public sealed record ChatMessage(string Role, string Content);
public sealed record SafeFile(string Alias, string Content, string LocalRelativePath = "");
public sealed record ScanSummary(int Included, int Skipped, int Characters, bool LimitReached, IReadOnlyList<SafeFile> Files)
{
    public string Context => string.Join("\n\n", Files.Select(f => $"--- FILE: {f.Alias} ---\n{f.Content}"));
}
public sealed record ReplacementInfo(string Placeholder, string Category);
public sealed class SafeOperationException : Exception
{
    private readonly object[] arguments;
    public string MessageKey { get; }
    public SafeOperationException(string messageKey, params object[] arguments)
        : base(new LocalizedText().Format(messageKey, arguments))
    { MessageKey = messageKey; this.arguments = arguments; }

    public string GetLocalizedMessage(LocalizedText text) => text.Format(MessageKey, arguments);
}
