using System.Globalization;
using System.Resources;

namespace MyDock.Core.Localization;

/// <summary>Looks up UI text in Strings.resx (English) and its translations; a missing key shows as itself.</summary>
public static class Loc
{
    public static readonly IReadOnlyList<string> Languages = ["en", "it"];

    private static readonly ResourceManager Resources = new("MyDock.Core.Localization.Strings", typeof(Loc).Assembly);

    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Picks the language by code ("en", "it"); null or empty follows the system's. Anything unknown, even a malformed code, falls back to English.</summary>
    public static void Use(string? language)
    {
        try
        {
            Culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            Culture = CultureInfo.InvariantCulture;
        }
    }

    public static string Get(string key) => Resources.GetString(key, Culture) ?? key;

    public static string Get(string key, params object?[] args) => string.Format(Culture, Get(key), args);
}
