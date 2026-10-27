using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using MyDock.Core.Localization;

namespace MyDock.Core.Tests;

public partial class LocalizationTests
{
    private static readonly ResourceManager Resources = new("MyDock.Core.Localization.Strings", typeof(Loc).Assembly);
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it");

    private static IEnumerable<string> Keys(CultureInfo culture)
    {
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).Order().ToList();
    }

    [Fact]
    public void Italian_has_exactly_the_keys_of_the_english_texts() =>
        Assert.Equal(Keys(CultureInfo.InvariantCulture), Keys(Italian));

    [Fact]
    public void Translations_keep_the_placeholders_of_the_english_text()
    {
        foreach (var key in Keys(CultureInfo.InvariantCulture))
        {
            var english = Resources.GetString(key, CultureInfo.InvariantCulture)!;
            var italian = Resources.GetString(key, Italian)!;
            Assert.True(Placeholders(english).SequenceEqual(Placeholders(italian)), $"{key}: {english} / {italian}");
        }
    }

    [Fact]
    public void Every_key_used_in_the_code_exists()
    {
        var known = Keys(CultureInfo.InvariantCulture).ToHashSet();
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "src")))
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("repository root");

        var used = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && (f.EndsWith(".axaml") || f.EndsWith(".cs")))
            .SelectMany(f => (f.EndsWith(".axaml") ? XamlKey() : CodeKey()).Matches(File.ReadAllText(f)).Select(m => (File: f, Key: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(used);
        Assert.Empty(used.Where(u => !known.Contains(u.Key)).Select(u => $"{Path.GetFileName(u.File)}: {u.Key}"));
        Assert.Empty(known.Except(used.Select(u => u.Key)));
    }

    [Fact]
    public void Get_formats_arguments_and_shows_an_unknown_key_as_itself()
    {
        Assert.Equal("web exited with code 3", Loc.Get("Alert.Crashed", "web", 3));
        Assert.Equal("No.Such.Key", Loc.Get("No.Such.Key"));
    }

    [Fact]
    public void A_malformed_language_code_falls_back_to_english()
    {
        try
        {
            Loc.Use("not a language!");

            Assert.Equal("Settings", Loc.Get("Panel.Settings"));
        }
        finally
        {
            Loc.Use("en");
        }
    }

    private static List<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => m.Value).Order().ToList();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\{loc:L ([\w.]+)\}")]
    private static partial Regex XamlKey();

    [GeneratedRegex("Loc\\.Get\\(\"([\\w.]+)\"")]
    private static partial Regex CodeKey();
}
