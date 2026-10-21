using System.Text.Json;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Localization;

/// <summary>
/// Fáze 7A carry-forward z F5/F7 (B11): every scalar field the inspector can render —
/// every block type of the registry plus the document, section, column, the document styles and the
/// list-item types — resolves its label from the JSON tables through
/// <c>EmailEditor_Field_{Owner}_{Property}</c> with the shared fallback <c>EmailEditor_Field_{Property}</c>.
/// A field without a key would render as a raw key (never as an English name derived from the property),
/// so the guard here is per OWNER too: a new field cannot silently borrow a humanized name.
/// </summary>
public class TmEmailFieldLocalizationParityTests
{
    private static readonly string[] Languages = ["en", "cs", "sk", "fr"];

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> TablesLazy =
        new(() => LoadAll("Tempo.Blazor.EmailTemplates", "TmEmailResources"));

    private static Dictionary<string, Dictionary<string, string>> Tables => TablesLazy.Value;

    public static TheoryData<string, string> AllFields()
    {
        var data = new TheoryData<string, string>();
        foreach (var target in Targets())
        {
            foreach (var field in PropertyReflection.GetFields(target))
            {
                data.Add(field.Owner, field.Name);
            }
        }

        return data;
    }

    [Fact]
    public void Every_owner_is_covered_by_the_test_data()
    {
        var owners = Targets()
            .SelectMany(PropertyReflection.GetFields)
            .Select(field => field.Owner)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(owner => owner, StringComparer.Ordinal)
            .ToList();

        owners.Should().Contain(["Text", "Button", "Image", "Table", "Social", "Navbar", "Carousel",
            "Accordion", "Hero", "Wrapper", "Group", "Divider", "Spacer", "Raw",
            "Document", "Section", "Column", "Styles",
            "SocialElement", "NavbarLink", "CarouselImage", "AccordionItem", "Font", "Style"]);
    }

    [Theory]
    [MemberData(nameof(AllFields))]
    public void Every_field_has_a_label_key_in_all_four_tables(string owner, string property)
    {
        var candidates = new[] { $"EmailEditor_Field_{owner}_{property}", $"EmailEditor_Field_{property}" };
        var resolved = candidates.FirstOrDefault(key => Tables["en"].ContainsKey(key));

        resolved.Should().NotBeNull(
            $"no label key for {owner}.{property}: add EmailEditor_Field_{owner}_{property} " +
            $"or the shared EmailEditor_Field_{property} to all four TmEmailResources tables");

        foreach (var language in Languages)
        {
            Tables[language].Should().ContainKey(resolved!,
                $"{language} must translate {resolved} — the parity test would fail on a raw key");
            Tables[language][resolved!].Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [MemberData(nameof(AllFields))]
    public void No_czech_label_is_the_humanized_property_name(string owner, string property)
    {
        var label = Resolve(owner, property, "cs");

        if (property is "Text" or "Preheader")
        {
            return; // the same word in Czech; every other field proves the rule below.
        }

        label.Should().NotBe(Humanize(property),
            $"{owner}.{property} must come from the JSON tables, not from Humanize(field.Name) (B11)");
    }

    [Fact]
    public void Choice_values_are_localized_in_all_four_tables()
    {
        var problems = new List<string>();
        foreach (var target in Targets())
            foreach (var field in PropertyReflection.GetFields(target).Where(f => f.Choices.Count > 0))
            {
                foreach (var choice in field.Choices)
                {
                    var key = choice.LabelKey;
                    foreach (var language in Languages)
                    {
                        if (!Tables[language].TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
                        {
                            problems.Add($"{language}: {field.Owner}.{field.Name} = {choice.Value} (missing {key})");
                        }
                    }
                }
            }

        problems.Should().BeEmpty("every select option needs a label:\n{0}", string.Join("\n", problems.Take(20)));
    }

    [Fact]
    public void Every_label_key_in_the_table_names_a_real_field()
    {
        var known = AllFields().Select(row => $"{row[0]}.{row[1]}").ToHashSet(StringComparer.Ordinal);
        var fieldNames = AllFields().Select(row => (string)row[1]!).ToHashSet(StringComparer.Ordinal);

        var strays = new List<string>();
        foreach (var key in Tables["en"].Keys.Where(key => key.StartsWith("EmailEditor_Field_", StringComparison.Ordinal)))
        {
            var rest = key["EmailEditor_Field_".Length..];
            var separator = rest.IndexOf('_');
            if (separator < 0)
            {
                // A shared fallback key: it must name a property that exists, otherwise the next rename
                // leaves a dead key nobody notices.
                if (!fieldNames.Contains(rest)) strays.Add(key + " (no such property)");
                continue;
            }

            if (!known.Contains(rest[..separator] + "." + rest[(separator + 1)..]))
            {
                strays.Add(key + " (no such owner.field)");
            }
        }

        strays.Should().BeEmpty(
            "a label key that names no field is dead weight the next rename leaves behind:\n{0}",
            string.Join("\n", strays.Take(20)));
    }

    private static string Resolve(string owner, string property, string language)
    {
        var ownerKey = $"EmailEditor_Field_{owner}_{property}";
        if (Tables[language].TryGetValue(ownerKey, out var ownerLabel)) return ownerLabel;
        return Tables[language].TryGetValue($"EmailEditor_Field_{property}", out var sharedLabel)
            ? sharedLabel
            : $"EmailEditor_Field_{property}";
    }

    private static IEnumerable<object> Targets()
    {
        foreach (var descriptor in new BlockRegistry().GetAll())
        {
            yield return descriptor.Factory();
        }

        yield return new EmailTemplateDocument();
        yield return new EmailSection();
        yield return new EmailColumn();
        yield return new TemplateStyles();
        yield return new EmailSocialElement();
        yield return new EmailNavbarLink();
        yield return new EmailCarouselImage();
        yield return new EmailAccordionItem();
        yield return new EmailTableCell();
        yield return new EmailTableRow();
        yield return new EmailFont();
        yield return new EmailStyle();
    }

    private static string Humanize(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c)) builder.Append(' ').Append(char.ToLowerInvariant(c));
            else builder.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        return builder.ToString();
    }

    private static Dictionary<string, Dictionary<string, string>> LoadAll(string project, string fileStem)
    {
        var resources = Path.Combine(FindRepositoryRoot(), "src", project, "Resources");
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in Languages)
        {
            var path = language == "en"
                ? Path.Combine(resources, $"{fileStem}.json")
                : Path.Combine(resources, $"{fileStem}.{language}.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            tables[language] = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty,
                    StringComparer.Ordinal);
        }

        return tables;
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
