using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Localization contract of the collapsible-column surface of <c>TmKanbanBoard</c> (plan a98e4a71,
/// phase 1). This is the ONE part of phase 1 that is already green: the three keys are shipped in all
/// four languages now, so phase 2 only has to render them.
/// <para>
/// The family is <c>TmKanban_CollapsedColumnSummary</c> — the accessible summary of a collapsed column
/// ("To Do, 2 cards"). It is a real plural family, not a neutral key: it counts cards. The count is
/// <c>{0}</c> because <see cref="TmLocalizerPluralExtensions.Plural(ITmLocalizer, string, int, object[])"/>
/// always formats the count there, and the column title is <c>{1}</c> (hence the templates read
/// <c>"{1}, {0} cards"</c>).
/// </para>
/// </summary>
public class TmKanbanBoardCollapseLocalizationTests : LocalizationTestBase
{
    private const string SummaryFamily = "TmKanban_CollapsedColumnSummary";

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> TablesLazy = new(LoadAll);

    private static Dictionary<string, Dictionary<string, string>> Tables => TablesLazy.Value;

    [Fact]
    public void Collapse_and_expand_labels_interpolate_the_column_title()
    {
        var loc = BuildEnglishLocalizer();

        loc["TmKanban_CollapseColumn", "To Do"].Should().Be("Collapse column To Do");
        loc["TmKanban_ExpandColumn", "To Do"].Should().Be("Expand column To Do");
    }

    [Fact]
    public void Collapsed_column_summary_is_a_plural_family_that_keeps_title_and_count()
    {
        var loc = BuildEnglishLocalizer();

        loc.Plural(SummaryFamily, 1, "To Do").Should().Be("To Do, 1 card");
        loc.Plural(SummaryFamily, 2, "To Do").Should().Be("To Do, 2 cards");
        loc.Plural(SummaryFamily, 7, "Done").Should().Be("Done, 7 cards");
    }

    [Fact]
    public void Shipped_czech_summary_family_picks_one_few_and_other()
    {
        // Built from the SHIPPED Czech table (not from a local copy), bound to cs so the CLDR rules apply.
        var czech = new MockTmLocalizer(Tables["cs"], CultureInfo.GetCultureInfo("cs"));

        czech.Plural(SummaryFamily, 1, "Úkol").Should().Be("Úkol, 1 karta");
        czech.Plural(SummaryFamily, 3, "Úkol").Should().Be("Úkol, 3 karty");
        czech.Plural(SummaryFamily, 5, "Úkol").Should().Be("Úkol, 5 karet");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("fr")]
    public void Every_shipped_language_carries_the_collapse_keys(string language)
    {
        var table = Tables[language];

        table.Should().ContainKey("TmKanban_CollapseColumn");
        table.Should().ContainKey("TmKanban_ExpandColumn");
        table["TmKanban_CollapseColumn"].Should().Contain("{0}", "the label names the column");
        table["TmKanban_ExpandColumn"].Should().Contain("{0}", "the label names the column");

        // The forms the language's grammar needs (docs/localization-plurals.md): en one/other,
        // fr one/many/other, cs+sk one/few/many/other.
        foreach (var suffix in SuffixesFor(language))
        {
            var key = SummaryFamily + suffix;
            table.Should().ContainKey(key, "{0} needs {1}", language, key);
            // "{0" rather than "{0}": French whole-millions group the count with "{0:N0}".
            table[key].Should().Contain("{0", "{0} must show the count", key);
            table[key].Should().Contain("{1}", "{0} must show the column title", key);
        }
    }

    private static string[] SuffixesFor(string language) => language switch
    {
        "en" => ["_One", "_Other"],
        "fr" => ["_One", "_Many", "_Other"],
        _ => ["_One", "_Few", "_Many", "_Other"],
    };

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
}
