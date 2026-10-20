using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Resources;

namespace Tempo.Blazor.EmailTemplates.Tests.Localization;

/// <summary>
/// Key-completeness guard for the email packages' embedded JSON tables (plan 2.10.0, Fáze 5).
/// <para>
/// The editor used to ship <c>.resx</c> files, which resolve through whatever
/// <c>IStringLocalizerFactory</c> the host registered — a host that replaces that factory saw raw keys.
/// The tables are now flat <c>{"Key": "Value"}</c> JSON embedded in the package assembly, exactly like
/// the Tempo core's <c>TmResources*.json</c>, so this guard mirrors
/// <c>TmResourceCompletenessTests</c>: every English key must exist in Czech, Slovak and French with
/// identical <c>{0}</c>-style placeholders, no table may invent a key the neutral one lacks, and every
/// key the packages' own constants (<see cref="ImportKeys"/>, <see cref="RenderKeys"/>,
/// <see cref="DocumentValidationKeys"/>, <see cref="SerializationKeys"/>, <c>LayoutValidationKeys</c>)
/// hand to the localizer must resolve — a missing entry renders as the key in the UI.
/// </para>
/// </summary>
public class TmEmailResourcesParityTests
{
    private static readonly string[] Languages = ["en", "cs", "sk", "fr"];

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> EmailTablesLazy =
        new(() => LoadAll("Tempo.Blazor.EmailTemplates", "TmEmailResources"));

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> ValidationTablesLazy =
        new(() => LoadAll("Tempo.Blazor.EmailTemplates.Abstractions", "EmailTemplateValidationResources"));

    private static Dictionary<string, Dictionary<string, string>> EmailTables => EmailTablesLazy.Value;

    private static Dictionary<string, Dictionary<string, string>> ValidationTables => ValidationTablesLazy.Value;

    private static Dictionary<string, Dictionary<string, string>> LoadAll(string project, string fileStem)
    {
        var resources = Path.Combine(FindRepositoryRoot(), "src", project, "Resources");
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var language in Languages)
        {
            var path = language == "en"
                ? Path.Combine(resources, $"{fileStem}.json")
                : Path.Combine(resources, $"{fileStem}.{language}.json");
            File.Exists(path).Should().BeTrue(
                $"{project} must ship {Path.GetFileName(path)} — the language set is exactly en/cs/sk/fr, " +
                "the same four the Tempo core ships");
            tables[language] = Load(path);
        }

        return tables;
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
    public void Neutral_HasKeys()
    {
        EmailTables["en"].Should().NotBeEmpty();
        ValidationTables["en"].Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("fr")]
    public void EveryNeutralKey_IsTranslated(string culture)
    {
        foreach (var (name, tables) in new[]
                 {
                     ("TmEmailResources", EmailTables),
                     ("EmailTemplateValidationResources", ValidationTables),
                 })
        {
            var neutral = tables["en"];
            var translated = tables[culture];

            neutral.Keys.Except(translated.Keys, StringComparer.Ordinal).Should().BeEmpty(
                $"every neutral {name} key must have a {culture} translation");
        }
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("fr")]
    public void NoExtraKeys_InTranslations(string culture)
    {
        foreach (var (name, tables) in new[]
                 {
                     ("TmEmailResources", EmailTables),
                     ("EmailTemplateValidationResources", ValidationTables),
                 })
        {
            tables[culture].Keys.Except(tables["en"].Keys, StringComparer.Ordinal).Should().BeEmpty(
                $"the {culture} {name} table must not define keys missing from the neutral table");
        }
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("fr")]
    public void Translations_KeepTheEnglishFormatPlaceholders(string culture)
    {
        foreach (var (name, tables) in new[]
                 {
                     ("TmEmailResources", EmailTables),
                     ("EmailTemplateValidationResources", ValidationTables),
                 })
        {
            var mismatched = new List<string>();
            foreach (var (key, value) in tables["en"])
            {
                if (!tables[culture].TryGetValue(key, out var translated))
                {
                    continue; // covered by the completeness test
                }

                if (!SamePlaceholders(value, translated))
                {
                    mismatched.Add($"{key}: en=\"{value}\" {culture}=\"{translated}\"");
                }
            }

            mismatched.Should().BeEmpty(
                "{0} translations must keep the English {{0}}-style placeholders:\n{1}\n{2}",
                culture, string.Join("\n", mismatched.Take(10)), name);
        }
    }

    [Theory]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("fr")]
    public void NoValueIsLeftAsItsOwnKey(string culture)
    {
        foreach (var (_, tables) in new[]
                 {
                     ("TmEmailResources", EmailTables),
                     ("EmailTemplateValidationResources", ValidationTables),
                 })
        {
            var offenders = tables[culture]
                .Where(pair => string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToList();

            offenders.Should().BeEmpty("a value that equals its key renders as a raw key in the UI ({0})", culture);
        }
    }

    [Fact]
    public void Slovak_table_reads_as_slovak_not_czech()
    {
        // ě, ř and ů exist only in Czech — their presence marks a machine-copied Czech table.
        foreach (var (name, tables) in new[]
                 {
                     ("TmEmailResources", EmailTables),
                     ("EmailTemplateValidationResources", ValidationTables),
                 })
        {
            var offenders = tables["sk"]
                .Where(pair => Regex.IsMatch(pair.Value, "[ěřů]"))
                .Select(pair => $"{name}: {pair.Key} = {pair.Value}")
                .ToList();

            offenders.Should().BeEmpty(
                "Slovak values must not contain Czech-only characters (ě/ř/ů):\n{0}",
                string.Join("\n", offenders.Take(10)));
        }
    }

    [Fact]
    public void Validation_table_ships_exactly_the_english_keys()
    {
        ValidationTables["en"].Keys.Should().BeEquivalentTo(
            ValidationTables["cs"].Keys
                .Intersect(ValidationTables["sk"].Keys, StringComparer.Ordinal)
                .Intersect(ValidationTables["fr"].Keys, StringComparer.Ordinal),
            "the four validation tables must be the same key set");
    }

    [Fact]
    public void Every_key_constant_the_packages_localize_resolves_in_all_four_languages()
    {
        var tables = EmailTables;

        var keys = typeof(ImportKeys).Assembly
            .GetTypes()
            .Where(type => type.IsPublic && type.IsAbstract && type.IsSealed)
            .Where(type => type.Name.EndsWith("Keys", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
                .Select(field => (Constant: $"{type.Name}.{field.Name}", Key: (string)field.GetRawConstantValue()!)))
            .ToList();

        keys.Should().NotBeEmpty("the packages hand these constants to the localizer");

        var unresolved = new List<string>();
        foreach (var (constant, key) in keys)
        {
            foreach (var language in Languages)
            {
                if (!tables[language].TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    unresolved.Add($"{language}: {constant} (\"{key}\")");
                }
            }
        }

        unresolved.Should().BeEmpty(
            "every localization key the packages emit must exist in the email table, or the UI shows the key:\n{0}",
            string.Join("\n", unresolved));
    }

    [Fact]
    public void No_resx_resources_remain_in_the_email_packages()
    {
        var root = FindRepositoryRoot();
        var leftovers = new[] { "Tempo.Blazor.EmailTemplates", "Tempo.Blazor.EmailTemplates.Abstractions" }
            .SelectMany(project => Directory.GetFiles(
                Path.Combine(root, "src", project), "*.resx", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToList();

        leftovers.Should().BeEmpty(
            "the email packages resolve resources from embedded JSON — a resx would go through the host's " +
            "IStringLocalizerFactory again and reintroduce the raw-key bug");
    }

    [Fact]
    public void Both_json_tables_are_embedded_in_their_package_assembly()
    {
        // A name such as "TmEmailResources.cs.json" makes the SDK infer a culture and route the file into
        // a satellite assembly — which is exactly what returns raw keys under published Blazor WebAssembly.
        // The csproj therefore pins WithCulture=false + an explicit LogicalName; this test proves it.
        AssertEmbedded(typeof(TmEmailResources), "Tempo.Blazor.EmailTemplates.Resources.TmEmailResources");
        AssertEmbedded(typeof(Abstractions.Resources.EmailTemplateValidationResources),
            "Tempo.Blazor.EmailTemplates.Abstractions.Resources.EmailTemplateValidationResources");

        static void AssertEmbedded(Type resourceType, string neutralName)
        {
            var names = resourceType.Assembly.GetManifestResourceNames();

            names.Should().Contain($"{neutralName}.json",
                $"{resourceType.Name} ships a neutral embedded JSON table in its own assembly");
            foreach (var language in new[] { "cs", "sk", "fr" })
            {
                names.Should().Contain($"{neutralName}.{language}.json",
                    $"{resourceType.Name} must embed the {language} table in the MAIN assembly, not a satellite");
            }
        }
    }

    private static bool SamePlaceholders(string english, string translated)
    {
        static string[] Extract(string text)
            => [.. Regex.Matches(text, @"\{[^{}]*\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal)];

        return Extract(english).SequenceEqual(Extract(translated), StringComparer.Ordinal);
    }
}
