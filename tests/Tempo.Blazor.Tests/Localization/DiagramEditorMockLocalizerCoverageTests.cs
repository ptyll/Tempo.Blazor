using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Guards the F14 test debt: every <c>Loc[...]</c> key the DiagramEditor package uses must
/// resolve in <see cref="LocalizationTestBase.BuildEnglishLocalizer"/> and in
/// <see cref="LocalizationTestBase.BuildCzechLocalizer"/>. A missing mock entry renders as the
/// raw <c>[Key]</c> fallback, which makes any text assertion on a component test untrustworthy
/// (the assertion passes against the placeholder instead of the wording).
/// </summary>
public class DiagramEditorMockLocalizerCoverageTests : LocalizationTestBase
{
    [Fact]
    public void EveryDiagramEditorLocKey_IsInTheEnglishMock()
    {
        var keys = CollectDiagramEditorKeys();

        keys.Should().NotBeEmpty("the diagram editor is localized through Loc[...] keys");

        var localizer = BuildEnglishLocalizer();
        var missing = keys.Where(key => localizer[key].StartsWith('[')).ToList();
        missing.Should().BeEmpty(
            "every DiagramEditor Loc key must exist in the English mock — a raw [Key] fallback makes text assertions vacuous. Missing:\n{0}",
            string.Join("\n", missing));
    }

    [Fact]
    public void EveryDiagramEditorLocKey_IsInTheCzechMock()
    {
        var keys = CollectDiagramEditorKeys();

        keys.Should().NotBeEmpty("the diagram editor is localized through Loc[...] keys");

        var localizer = BuildCzechLocalizer();
        var missing = keys.Where(key => localizer[key].StartsWith('[')).ToList();
        missing.Should().BeEmpty(
            "every DiagramEditor Loc key must exist in the Czech mock — a raw [Key] fallback makes text assertions vacuous. Missing:\n{0}",
            string.Join("\n", missing));
    }

    /// <summary>
    /// Collects every localization key the DiagramEditor package references: literal
    /// <c>Loc["..."]</c> arguments plus any string literal shaped like a <c>Tm*_*</c> key that is
    /// defined in the English resource table (<c>src/Tempo.Blazor/Resources/TmResources.json</c>).
    /// The widened literal sweep also catches keys handed to the localizer indirectly (helpers,
    /// interop, dynamically built names) that the narrow <c>Loc[...]</c> rule would miss.
    /// </summary>
    private static SortedSet<string> CollectDiagramEditorKeys()
    {
        var root = FindRepositoryRoot();
        var packageDir = Path.Combine(root, "src", "Tempo.Blazor.DiagramEditor");
        var resourcePath = Path.Combine(root, "src", "Tempo.Blazor", "Resources", "TmResources.json");
        var resourceKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(resourcePath))
            ?? throw new InvalidOperationException($"Could not parse {resourcePath} as a flat key/value map.");
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var locRule = new Regex("Loc\\[(.*?)\\]", RegexOptions.Singleline);
        var literalRule = new Regex("\"(Tm\\w+_\\w+)\"");

        foreach (var file in Directory.EnumerateFiles(packageDir, "*.razor", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(packageDir, "*.cs", SearchOption.AllDirectories))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);

            foreach (Match match in locRule.Matches(text))
            {
                foreach (Match literal in literalRule.Matches(match.Groups[1].Value))
                {
                    keys.Add(literal.Groups[1].Value);
                }
            }

            foreach (Match literal in literalRule.Matches(text))
            {
                var candidate = literal.Groups[1].Value;
                if (resourceKeys.ContainsKey(candidate))
                {
                    keys.Add(candidate);
                }
            }
        }

        return keys;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
