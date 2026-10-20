using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Resource completeness guard (plan Fáze 3 — Slovak resources): every key in the neutral English
/// table must exist in the Czech, Slovak and French tables with the same format placeholders, the
/// Slovak table must actually read as Slovak (not Czech), and every plural family must provide the
/// forms each language's grammar needs (docs/localization-plurals.md). Catches "the key was added
/// to en and forgotten in a translation" before it ships — cs shipped 53 keys behind en before this
/// guard existed.
/// </summary>
public class TmResourceCompletenessTests
{
    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> TablesLazy = new(LoadAll);

    private static Dictionary<string, Dictionary<string, string>> Tables => TablesLazy.Value;

    private static Dictionary<string, Dictionary<string, string>> LoadAll()
    {
        var resources = Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor", "Resources");
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = Load(Path.Combine(resources, "TmResources.json")),
            ["cs"] = Load(Path.Combine(resources, "TmResources.cs.json")),
            ["sk"] = Load(Path.Combine(resources, "TmResources.sk.json")),
            ["fr"] = Load(Path.Combine(resources, "TmResources.fr.json")),
        };
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
    public void Every_english_key_exists_in_cs_sk_and_fr()
    {
        var en = Tables["en"].Keys.ToList();

        foreach (var language in new[] { "cs", "sk", "fr" })
        {
            var missing = en.Where(key => !Tables[language].ContainsKey(key)).ToList();
            missing.Should().BeEmpty(
                "every English key needs a {0} translation ({1} missing)",
                language, missing.Count);
        }
    }

    [Fact]
    public void Slovak_ships_exactly_the_english_keys_plus_the_plural_forms_its_grammar_needs()
    {
        var en = Tables["en"];
        var sk = Tables["sk"];

        static string Base(string key) => key.EndsWith("_Few", StringComparison.Ordinal)
            ? key[..^"_Few".Length]
            : key[..^"_Many".Length];

        var foreign = sk.Keys
            .Where(key => !en.ContainsKey(key))
            .Where(key => !(key.EndsWith("_Few", StringComparison.Ordinal) || key.EndsWith("_Many", StringComparison.Ordinal))
                          || !en.ContainsKey(Base(key) + "_One") || !en.ContainsKey(Base(key) + "_Other"))
            .ToList();

        foreign.Should().BeEmpty(
            "sk may only add the _Few/_Many forms of plural families that exist in en:\n{0}",
            string.Join("\n", foreign));
    }

    [Fact]
    public void Translations_keep_the_english_format_placeholders()
    {
        // Multiset comparison: "Select flow: {0} → {1} ({2})" must keep exactly {0},{1},{2}.
        foreach (var language in new[] { "cs", "sk", "fr" })
        {
            var mismatched = new List<string>();
            foreach (var (key, value) in Tables["en"])
            {
                if (!Tables[language].TryGetValue(key, out var translated))
                {
                    continue; // covered by the completeness test
                }

                if (!SamePlaceholders(value, translated))
                {
                    mismatched.Add($"{key}: en=\"{value}\" {language}=\"{translated}\"");
                }
            }

            mismatched.Should().BeEmpty(
                "{0} translations must keep the English {{0}}-style placeholders:\n{1}",
                language, string.Join("\n", mismatched.Take(10)));
        }
    }

    [Fact]
    public void Slovak_table_reads_as_slovak_not_czech()
    {
        // ě, ř and ů exist only in Czech — their presence marks a machine-copied Czech table.
        var offenders = Tables["sk"]
            .Where(pair => Regex.IsMatch(pair.Value, "[ěřů]"))
            .Select(pair => $"{pair.Key} = {pair.Value}")
            .ToList();

        offenders.Should().BeEmpty(
            "Slovak values must not contain Czech-only characters (ě/ř/ů):\n{0}",
            string.Join("\n", offenders.Take(10)));
    }

    [Fact]
    public void Slovak_plural_families_provide_all_four_slavic_forms()
    {
        var sk = Tables["sk"];
        var families = Tables["en"].Keys
            .Where(k => k.EndsWith("_Other", StringComparison.Ordinal) && sk.ContainsKey(k[..^"_Other".Length] + "_One"))
            .Select(k => k[..^"_Other".Length])
            .Where(baseKey => baseKey is not ("TmDiagramProperties_Arrowhead" or "TmDiagramProperties_Cardinality"))
            .ToList();

        families.Should().NotBeEmpty("the shipped counted texts are plural families");

        foreach (var family in families)
        {
            foreach (var suffix in new[] { "_One", "_Few", "_Many", "_Other" })
            {
                sk.Should().ContainKey(family + suffix,
                    "Slovak counts 1 / 2–4 / fractions / 5+ need one/few/many/other");

                sk[family + suffix].Should().Contain("{0", $"{family}{suffix} must show the count");
            }
        }
    }

    [Fact]
    public void Slovak_translates_the_color_palette_keys()
    {
        // The keys SpravaFinanci reported as missing in sk (plan Fáze 3 context).
        var sk = Tables["sk"];

        sk["TmColorPalette_SelectColor"].Should().Be("Vybrať farbu");
        sk["TmColorPalette_Clear"].Should().Be("Vymazať");
        sk["TmColorPalette_SelectColorNamed"].Should().Be("Vybrať farbu {0}");
    }

    private static bool SamePlaceholders(string english, string translated)
    {
        static string[] Extract(string text)
            => [.. Regex.Matches(text, @"\{[^{}]*\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal)];

        return Extract(english).SequenceEqual(Extract(translated), StringComparer.Ordinal);
    }
}
