using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// <c>Loc.Plural("Key", n)</c> (B16): the right grammatical form for a count, chosen from the
/// CLDR plural rules of the localizer's culture and read from <c>Key_One/_Few/_Many/_Other</c>,
/// falling back to the base <c>Key</c>. Czech has four forms (1 / 2-4 / fractions / 5+), French
/// has one/many/other, English one/other.
/// </summary>
public class PluralLocalizerTests
{
    private static CultureInfo Culture(string name) => CultureInfo.GetCultureInfo(name);

    // ── the rules themselves ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, TmPluralCategory.Other)]
    [InlineData(1, TmPluralCategory.One)]
    [InlineData(2, TmPluralCategory.Few)]
    [InlineData(3, TmPluralCategory.Few)]
    [InlineData(4, TmPluralCategory.Few)]
    [InlineData(5, TmPluralCategory.Other)]
    [InlineData(11, TmPluralCategory.Other)]
    [InlineData(12, TmPluralCategory.Other)]
    [InlineData(21, TmPluralCategory.Other)]
    [InlineData(22, TmPluralCategory.Other)]
    [InlineData(100, TmPluralCategory.Other)]
    [InlineData(101, TmPluralCategory.Other)]
    [InlineData(0.5, TmPluralCategory.Many)]
    [InlineData(1.5, TmPluralCategory.Many)]
    [InlineData(2.5, TmPluralCategory.Many)]
    [InlineData(5.1, TmPluralCategory.Many)]
    public void Czech_1_2_5_AndFractions(double count, TmPluralCategory expected)
    {
        TmPluralRules.Select(Culture("cs"), count).Should().Be(expected);
        TmPluralRules.Select(Culture("cs-CZ"), count).Should().Be(expected, "a region uses its language's rules");
        TmPluralRules.Select(Culture("sk"), count).Should().Be(expected, "Slovak shares the Czech rules");
    }

    [Theory]
    [InlineData(0, TmPluralCategory.One)]
    [InlineData(1, TmPluralCategory.One)]
    [InlineData(1.5, TmPluralCategory.One)]
    [InlineData(1.99, TmPluralCategory.One)]
    [InlineData(2, TmPluralCategory.Other)]
    [InlineData(5, TmPluralCategory.Other)]
    [InlineData(100, TmPluralCategory.Other)]
    [InlineData(1_000_000, TmPluralCategory.Many)]
    [InlineData(5_000_000, TmPluralCategory.Many)]
    [InlineData(1_000_001, TmPluralCategory.Other)]
    public void French_one_many_other(double count, TmPluralCategory expected)
    {
        TmPluralRules.Select(Culture("fr"), count).Should().Be(expected);
        TmPluralRules.Select(Culture("fr-CA"), count).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, TmPluralCategory.Other)]
    [InlineData(1, TmPluralCategory.One)]
    [InlineData(2, TmPluralCategory.Other)]
    [InlineData(5, TmPluralCategory.Other)]
    [InlineData(1.5, TmPluralCategory.Other)]
    public void English_one_other(double count, TmPluralCategory expected)
    {
        TmPluralRules.Select(Culture("en"), count).Should().Be(expected);
        TmPluralRules.Select(Culture("en-GB"), count).Should().Be(expected);
    }

    [Fact]
    public void An_unknown_language_and_the_invariant_culture_use_the_English_rules()
    {
        TmPluralRules.Select(Culture("de"), 1).Should().Be(TmPluralCategory.One);
        TmPluralRules.Select(Culture("de"), 3).Should().Be(TmPluralCategory.Other);
        TmPluralRules.Select(CultureInfo.InvariantCulture, 1).Should().Be(TmPluralCategory.One);
        TmPluralRules.Select(CultureInfo.InvariantCulture, 2).Should().Be(TmPluralCategory.Other);
    }

    [Theory]
    [InlineData(1, TmPluralCategory.One)]
    [InlineData(2, TmPluralCategory.Few)]
    [InlineData(4, TmPluralCategory.Few)]
    [InlineData(5, TmPluralCategory.Many)]
    [InlineData(12, TmPluralCategory.Many)]
    [InlineData(14, TmPluralCategory.Many)]
    [InlineData(22, TmPluralCategory.Few)]
    [InlineData(25, TmPluralCategory.Many)]
    [InlineData(1.5, TmPluralCategory.Other)]
    public void Polish_has_the_teens_exception(double count, TmPluralCategory expected)
        => TmPluralRules.Select(Culture("pl"), count).Should().Be(expected);

    [Fact]
    public void A_negative_count_uses_its_absolute_value()
    {
        TmPluralRules.Select(Culture("cs"), -1).Should().Be(TmPluralCategory.One);
        TmPluralRules.Select(Culture("cs"), -3).Should().Be(TmPluralCategory.Few);
    }

    [Fact]
    public void Not_a_number_and_infinity_are_Other()
    {
        TmPluralRules.Select(Culture("cs"), double.NaN).Should().Be(TmPluralCategory.Other);
        TmPluralRules.Select(Culture("cs"), double.PositiveInfinity).Should().Be(TmPluralCategory.Other);
    }

    [Fact]
    public void A_null_culture_is_rejected()
    {
        var act = () => TmPluralRules.Select(null!, 1);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Loc.Plural over a localizer ─────────────────────────────────────────────────────────

    private static MockTmLocalizer Czech(params (string Key, string Value)[] entries)
        => new(entries.ToDictionary(e => e.Key, e => e.Value), Culture("cs"));

    private static readonly (string, string)[] CzechPending =
    [
        ("Pending_One", "{0} čekající změna"),
        ("Pending_Few", "{0} čekající změny"),
        ("Pending_Many", "{0} čekající změny"),
        ("Pending_Other", "{0} čekajících změn"),
    ];

    [Theory]
    [InlineData(0, "0 čekajících změn")]
    [InlineData(1, "1 čekající změna")]
    [InlineData(2, "2 čekající změny")]
    [InlineData(4, "4 čekající změny")]
    [InlineData(5, "5 čekajících změn")]
    [InlineData(21, "21 čekajících změn")]
    [InlineData(1.5, "1,5 čekající změny")]
    public void Plural_picks_the_Czech_form_and_formats_the_count_in_the_localizer_culture(double count, string expected)
        => Czech(CzechPending).Plural("Pending", count).Should().Be(expected);

    [Fact]
    public void Plural_passes_further_arguments_after_the_count()
    {
        var loc = Czech(("Found_One", "{0} nález ve složce {1}"), ("Found_Few", "{0} nálezy ve složce {1}"),
            ("Found_Many", "{0} nálezu ve složce {1}"), ("Found_Other", "{0} nálezů ve složce {1}"));

        loc.Plural("Found", 3, "Doklady").Should().Be("3 nálezy ve složce Doklady");
    }

    [Fact]
    public void A_missing_category_falls_back_to_Other_and_then_to_the_base_key()
    {
        var onlyOther = Czech(("Items_Other", "{0} položek"));
        onlyOther.Plural("Items", 1).Should().Be("1 položek", "no _One -> _Other");
        onlyOther.Plural("Items", 3).Should().Be("3 položek", "no _Few -> _Other");

        var onlyBase = Czech(("Items", "Položky: {0}"));
        onlyBase.Plural("Items", 1).Should().Be("Položky: 1", "no suffixed key at all -> the base key (the neutral wording)");
        onlyBase.Plural("Items", 7).Should().Be("Položky: 7");

        var mixed = Czech(("Items_One", "1 položka"), ("Items", "Položky: {0}"));
        mixed.Plural("Items", 1).Should().Be("1 položka");
        mixed.Plural("Items", 3).Should().Be("Položky: 3", "_Few is absent, _Other is absent -> base key");
    }

    [Fact]
    public void An_unknown_key_returns_what_the_indexer_returns()
        => Czech().Plural("Nope", 2).Should().Be("[Nope]");

    [Fact]
    public void English_uses_One_and_Other_only()
    {
        var loc = new MockTmLocalizer(new Dictionary<string, string>
        {
            ["Pending_One"] = "{0} pending change",
            ["Pending_Other"] = "{0} pending changes",
        });

        loc.Plural("Pending", 1).Should().Be("1 pending change");
        loc.Plural("Pending", 2).Should().Be("2 pending changes");
        loc.Plural("Pending", 0).Should().Be("0 pending changes");
    }

    [Fact]
    public void French_uses_the_one_form_for_zero_and_one()
    {
        var loc = new MockTmLocalizer(new Dictionary<string, string>
        {
            ["Pending_One"] = "{0} modification en attente",
            ["Pending_Many"] = "{0} de modifications en attente",
            ["Pending_Other"] = "{0} modifications en attente",
        }, Culture("fr"));

        loc.Plural("Pending", 0).Should().Be("0 modification en attente");
        loc.Plural("Pending", 1).Should().Be("1 modification en attente");
        loc.Plural("Pending", 2).Should().Be("2 modifications en attente");
    }

    [Fact]
    public void A_substitute_whose_Culture_is_null_does_not_throw_and_returns_the_indexer_value()
    {
        // NSubstitute/Moq proxies return null for an unimplemented default interface member.
        // Plural must treat that as CurrentUICulture, not throw ArgumentNullException.
        var localizer = Substitute.For<ITmLocalizer>();
        localizer[Arg.Any<string>()].Returns(ci => ci.Arg<string>());
        localizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns("proxied");
        _ = localizer.Culture; // the proxy's default is null; the call must not depend on it

        var act = () => localizer.Plural("Pending", 2);

        act.Should().NotThrow().Which.Should().Be("proxied");
    }

    [Fact]
    public void Changelog_and_docs_do_not_claim_a_delegating_plain_key_override_keeps_working()
    {
        // The documented delegating pattern reads Key_One through the indexer, and the built-in
        // fallback then returns the shipped family — so overriding only the plain key is bypassed.
        var root = ThemeCss.RepositoryRoot().FullName;
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        var unreleased = changelog[..changelog.IndexOf("## 2.9.0 - ", StringComparison.Ordinal)];
        var doc = File.ReadAllText(Path.Combine(root, "docs", "localization-plurals.md"));

        unreleased.Should().NotContain("keeps working (`Plural` falls back to the plain key)");
        unreleased.Should().NotContain("implementations compile and work unchanged");
        unreleased.Should().Contain("_One/_Other").And.Contain("delegating");
        doc.Should().Contain("delegating").And.Contain("_Few/_Many");
        doc.Should().Contain("only when no family exists");
    }

    [Fact]
    public void An_int_overload_keeps_the_count_an_integer_so_a_D2_format_works()
    {
        // Passing an int to the double overload boxes it as double, and "{0:D2}" then throws.
        var loc = new MockTmLocalizer(new Dictionary<string, string>
        {
            ["Code_One"] = "{0:D2}",
            ["Code_Other"] = "{0:D2}",
        }, Culture("en"));

        var act = () => loc.Plural("Code", 7);

        act.Should().NotThrow().Which.Should().Be("07");
    }

    [Fact]
    public void French_many_strings_group_a_whole_million_with_N0()
    {
        var fr = Table("TmResources.fr.json");
        foreach (var family in Families(fr))
        {
            fr[family + "_Many"].Should().Contain("{0:N0}", $"{family}_Many is a whole number in French; N0 groups it");
        }
    }

    [Fact]
    public void Reserved_suffixes_and_the_arrowhead_keys_are_excluded_from_the_parity_guard()
    {
        var doc = File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName, "docs", "localization-plurals.md"));
        doc.Should().Contain("reserved").And.Contain("_One/_Few/_Many/_Other");
        doc.Should().Contain("TmDiagramProperties_Arrowhead").And.Contain("TmDiagramProperties_Cardinality");
        doc.Should().Contain("deprecated").And.Contain("TmTimeline_MinutesAgo");
    }

    [Fact]
    public void A_null_localizer_or_key_is_rejected()
    {
        ITmLocalizer? none = null;
        var act = () => none!.Plural("K", 1);
        act.Should().Throw<ArgumentNullException>();
        var act2 = () => Czech().Plural(null!, 1);
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_localizer_that_overrides_neither_Culture_nor_TryGetString_still_works()
    {
        // A host's own ITmLocalizer written before plurals existed: the defaults read the indexer and
        // the ambient UI culture.
        var original = CultureInfo.CurrentUICulture;
        var originalFormat = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentUICulture = Culture("cs");
            CultureInfo.CurrentCulture = Culture("cs");
            ITmLocalizer legacy = new LegacyLocalizer(new Dictionary<string, string>
            {
                ["Pending_One"] = "{0} změna", ["Pending_Few"] = "{0} změny", ["Pending_Other"] = "{0} změn",
            });

            legacy.Plural("Pending", 1).Should().Be("1 změna");
            legacy.Plural("Pending", 3).Should().Be("3 změny");
            legacy.Plural("Pending", 8).Should().Be("8 změn");
            legacy.Plural("Pending", 1.5).Should().Be("1,5 změn", "_Many is absent -> _Other");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
            CultureInfo.CurrentCulture = originalFormat;
        }
    }

    private sealed class LegacyLocalizer(Dictionary<string, string> data) : ITmLocalizer
    {
        public string this[string key] => data.TryGetValue(key, out var v) ? v : key;

        public string this[string key, params object[] arguments]
            => string.Format(CultureInfo.CurrentCulture, this[key], arguments);
    }

    // ── the real localizer over the embedded JSON ───────────────────────────────────────────

    private static ITmLocalizer Real() => new DefaultTmLocalizer(new JsonStringLocalizer<TmResources>());

    [Theory]
    [InlineData("cs", 1, "1 čekající změna")]
    [InlineData("cs", 2, "2 čekající změny")]
    [InlineData("cs", 4, "4 čekající změny")]
    [InlineData("cs", 5, "5 čekajících změn")]
    [InlineData("en", 1, "1 pending change")]
    [InlineData("en", 3, "3 pending changes")]
    [InlineData("fr", 1, "1 modification en attente")]
    [InlineData("fr", 3, "3 modifications en attente")]
    [InlineData("sk", 1, "1 čakajúca zmena")]
    [InlineData("sk", 3, "3 čakajúce zmeny")]
    [InlineData("sk", 5, "5 čakajúcich zmien")]
    public void The_shipped_B16_texts_are_natural_in_every_language(string culture, int count, string expected)
    {
        using var _ = CultureScope.Use(culture);
        Real().Plural("TmDocumentEditor_ReviewSummaryRevisions", count).Should().Be(expected);
    }

    // The Modeling issue count was rewritten (Modeling redesign plan, phase 1) from a plural
    // family to the NEUTRAL B16 formulation ("Nálezy: {0}" / "Issues: {0}") — no language has to
    // pick a grammatical form for a simple panel header. Plural() falls through to the plain key.
    [Theory]
    [InlineData("cs", 1, "Nálezy: 1")]
    [InlineData("cs", 7, "Nálezy: 7")]
    [InlineData("en", 1, "Issues: 1")]
    [InlineData("en", 2, "Issues: 2")]
    [InlineData("fr", 1, "Problèmes : 1")]
    [InlineData("fr", 2, "Problèmes : 2")]
    [InlineData("sk", 1, "Problémy: 1")]
    [InlineData("sk", 7, "Problémy: 7")]
    public void The_modeling_issue_count_is_neutral_in_every_language(string culture, int count, string expected)
    {
        using var _ = CultureScope.Use(culture);
        Real().Plural("TmModelingIssuePanel_Count", count).Should().Be(expected);
    }

    // ── resource parity: a plural family is complete in every language ──────────────────────

    private static readonly string[] Suffixes = ["_One", "_Few", "_Many", "_Other"];

    private static Dictionary<string, string> Table(string file)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "Tempo.Blazor", "Resources", file);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    /// <summary>
    /// Keys that end in a reserved plural suffix but are not counted families: a property label
    /// ("Arrowhead: Many") happens to share the suffix. They stay out of the parity guard.
    /// </summary>
    private static readonly string[] ReservedSuffixExclusions =
    [
        "TmDiagramProperties_Arrowhead",
        "TmDiagramProperties_Cardinality",
    ];

    /// <summary>The base keys of every plural family in a table (a key with a _One/_Few/_Many suffix and an _Other sibling).</summary>
    private static HashSet<string> Families(Dictionary<string, string> table)
        => table.Keys
            .Where(k => k.EndsWith("_Other", StringComparison.Ordinal))
            .Select(k => k[..^"_Other".Length])
            .Where(b => table.ContainsKey(b + "_One"))
            .Where(b => !ReservedSuffixExclusions.Contains(b, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_plural_family_exists_in_every_shipped_language_with_the_forms_the_language_needs()
    {
        var en = Table("TmResources.json");
        var cs = Table("TmResources.cs.json");
        var sk = Table("TmResources.sk.json");
        var fr = Table("TmResources.fr.json");

        var families = Families(en);
        families.Should().NotBeEmpty("the B16 texts ship as plural families");
        Families(cs).Should().BeEquivalentTo(families, "Czech has the same families as English");
        Families(sk).Should().BeEquivalentTo(families, "Slovak has the same families as English");
        Families(fr).Should().BeEquivalentTo(families, "French has the same families as English");

        foreach (var family in families)
        {
            foreach (var suffix in new[] { "_One", "_Other" })
            {
                en.Should().ContainKey(family + suffix);
            }

            foreach (var suffix in Suffixes)
            {
                cs.Should().ContainKey(family + suffix, "Czech needs one/few/many/other");
                sk.Should().ContainKey(family + suffix, "Slovak needs one/few/many/other");
            }

            foreach (var suffix in new[] { "_One", "_Many", "_Other" })
            {
                fr.Should().ContainKey(family + suffix, "French needs one/many/other");
            }

            foreach (var table in new[] { en, cs, sk, fr })
            {
                foreach (var suffix in Suffixes.Where(s => table.ContainsKey(family + s)))
                {
                    table[family + suffix].Should().Contain("{0", $"{family}{suffix} must show the count");
                }
            }
        }
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
}