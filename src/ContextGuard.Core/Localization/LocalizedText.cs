using System.Globalization;
using System.Resources;

namespace ContextGuard.Core;

public enum AppLanguage { English, Russian }

public sealed class LocalizedText
{
    private static readonly ResourceManager Resources = new("ContextGuard.Core.Localization.Strings", typeof(LocalizedText).Assembly);
    public AppLanguage Language { get; }
    public CultureInfo Culture { get; }

    public LocalizedText(AppLanguage language = AppLanguage.English)
    {
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language == AppLanguage.Russian ? "ru" : "en");
    }

    public string this[string key] => Resources.GetString(key, Culture)
        ?? throw new InvalidOperationException($"Missing localization resource: {key}");

    public string Format(string key, params object[] arguments) => string.Format(Culture, this[key], arguments);
}
