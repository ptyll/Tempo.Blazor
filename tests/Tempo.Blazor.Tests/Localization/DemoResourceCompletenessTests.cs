using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// The demo resource tables (<c>src/Tempo.Blazor.Demo.SharedUI/Resources/DemoResources*.json</c>)
/// own the demo-only keys (<c>TmDemo_*</c> and the <c>*Demo_*</c> page strings) that used to ship
/// inside the library's <c>TmResources</c>. These guards keep the split honest: the demo tables are
/// complete in all four languages with matching placeholders, every demo key is visibly demo-owned
/// (carries "Demo" in its name), and no key lives in both tables — so a key re-added to the library
/// resources or a translation forgotten in the demo tables fails here instead of rendering a raw
/// key on a demo page.
/// </summary>
public class DemoResourceCompletenessTests
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> DemoTablesLazy = new(LoadDemoTables);
    private static readonly Lazy<Dictionary<string, string>> LibraryEnglishLazy = new(LoadLibraryEnglish);

    private static Dictionary<string, Dictionary<string, string>> DemoTables => DemoTablesLazy.Value;

    private static Dictionary<string, string> LibraryEnglish => LibraryEnglishLazy.Value;

    private static Dictionary<string, Dictionary<string, string>> LoadDemoTables()
    {
        var resources = Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor.Demo.SharedUI", "Resources");
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = Load(Path.Combine(resources, "DemoResources.json")),
            ["cs"] = Load(Path.Combine(resources, "DemoResources.cs.json")),
            ["sk"] = Load(Path.Combine(resources, "DemoResources.sk.json")),
            ["fr"] = Load(Path.Combine(resources, "DemoResources.fr.json")),
        };
    }

    private static Dictionary<string, string> LoadLibraryEnglish()
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "src", "Tempo.Blazor", "Resources", "TmResources.json");
        return Load(path);
    }

    private static Dictionary<string, string> Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            table[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return table;
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void Demo_tables_carry_keys_and_every_english_key_exists_in_cs_sk_and_fr()
    {
        DemoTables["en"].Should().NotBeEmpty("the demo owns its own strings");

        foreach (var language in new[] { "cs", "sk", "fr" })
        {
            var missing = DemoTables["en"].Keys
                .Where(key => !DemoTables[language].ContainsKey(key))
                .ToList();
            missing.Should().BeEmpty(
                "every demo English key needs a {0} translation ({1} missing)",
                language, missing.Count);
        }
    }

    [Fact]
    public void Demo_translations_keep_the_english_format_placeholders()
    {
        foreach (var language in new[] { "cs", "sk", "fr" })
        {
            var mismatched = new List<string>();
            foreach (var (key, value) in DemoTables["en"])
            {
                if (!DemoTables[language].TryGetValue(key, out var translated))
                {
                    continue; // covered by the completeness test
                }

                if (!SamePlaceholders(value, translated))
                {
                    mismatched.Add($"{key}: en=\"{value}\" {language}=\"{translated}\"");
                }
            }

            mismatched.Should().BeEmpty(
                "{0} demo translations must keep the English {{0}}-style placeholders:\n{1}",
                language, string.Join("\n", mismatched.Take(10)));
        }
    }

    [Fact]
    public void Every_demo_key_carries_Demo_in_its_name()
    {
        // The split criterion: these tables exist for keys the library never needed. A key without
        // "Demo" in its name in here is either a misfiled library string or a demo string named
        // ambiguously — both need a human decision, not a silent pass.
        var offenders = DemoTables["en"].Keys
            .Where(key => !key.Contains("Demo", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "demo-owned keys must carry Demo in their name:\n{0}", string.Join("\n", offenders));
    }

    [Fact]
    public void Demo_keys_are_disjoint_from_the_library_resources()
    {
        var shared = DemoTables["en"].Keys
            .Where(LibraryEnglish.ContainsKey)
            .ToList();

        shared.Should().BeEmpty(
            "a key must live in exactly one table — the library or the demo, never both:\n{0}",
            string.Join("\n", shared));

        // And the reverse direction for the demo-only namespaces: the library table must not
        // re-grow demo-only keys (they moved out of it deliberately).
        var demoOnlyPrefixes = DemoTables["en"].Keys
            .Select(key => key[..key.IndexOf("Demo", StringComparison.Ordinal)] + "Demo")
            .Distinct()
            .ToList();
        var regrown = LibraryEnglish.Keys
            .Where(key => demoOnlyPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        regrown.Should().BeEmpty(
            "demo-only key families belong to the demo resources, not the library:\n{0}",
            string.Join("\n", regrown));
    }

    private static bool SamePlaceholders(string english, string translated)
    {
        static string[] Extract(string text)
            => [.. Regex.Matches(text, @"\{[^{}]*\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal)];

        return Extract(english).SequenceEqual(Extract(translated), StringComparer.Ordinal);
    }
}
