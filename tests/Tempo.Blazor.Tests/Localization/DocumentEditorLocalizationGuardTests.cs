using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Localization guards for the document editor package:
/// 1) No hardcoded user-facing attribute text — every aria-label/title/placeholder in the editor's
///    .razor files must bind a localized value ('@…'), never a literal.
/// 2) Full cs/en/fr parity for every TmDocumentEditor_* resource key — a key added to one language
///    file must exist in all three.
/// Reads every file via File.ReadAllLines so nothing is skipped (grep classifies some .razor
/// files as binary and silently misses them).
/// </summary>
public class DocumentEditorLocalizationGuardTests
{
    private static readonly Regex AttributeLiteral = new(
        "(aria-label|title|placeholder)\\s*=\\s*\"(?<value>[^\"@][^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void EditorRazorFiles_HaveNoHardcodedAriaLabelTitleOrPlaceholderText()
    {
        var root = FindRepositoryRoot();
        var componentDir = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor", "Components");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(componentDir, "*.razor", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in AttributeLiteral.Matches(lines[i]))
                {
                    var value = match.Groups["value"].Value;
                    // Literal values without any letters (e.g. "0", "-", css-ish tokens) are not
                    // user-facing text; anything with letters must come from the localizer.
                    if (value.Any(char.IsLetter))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {match.Value}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "every user-facing aria-label/title/placeholder in the document editor must bind a localized value:\n{0}",
            string.Join("\n", offenders));
    }

    [Fact]
    public void DocumentEditorResourceKeys_HaveFullCsEnFrParity()
    {
        var root = FindRepositoryRoot();
        var resources = Path.Combine(root, "src", "Tempo.Blazor", "Resources");
        var en = ReadKeys(Path.Combine(resources, "TmResources.json"));
        var cs = ReadKeys(Path.Combine(resources, "TmResources.cs.json"));
        var fr = ReadKeys(Path.Combine(resources, "TmResources.fr.json"));

        cs.Except(en).Should().BeEmpty("cs must not carry editor keys unknown to en");
        fr.Except(en).Should().BeEmpty("fr must not carry editor keys unknown to en");
        en.Except(cs).Should().BeEmpty("every editor key needs a Czech translation");
        en.Except(fr).Should().BeEmpty("every editor key needs a French translation");
    }

    [Fact]
    public void EveryEditorKeyUsedInComponents_ExistsInAllThreeLanguages()
    {
        var root = FindRepositoryRoot();
        var componentDir = Path.Combine(root, "src", "Tempo.Blazor.DocumentEditor");
        var usedKeys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(componentDir, "*.razor", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(componentDir, "*.cs", SearchOption.AllDirectories))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "Loc\\[\\\"(TmDocumentEditor_[A-Za-z0-9_]+)\\\"\\]"))
            {
                usedKeys.Add(match.Groups[1].Value);
            }
        }

        usedKeys.Should().NotBeEmpty("the editor is localized through Loc[...] keys");

        var resources = Path.Combine(root, "src", "Tempo.Blazor", "Resources");
        var en = ReadKeys(Path.Combine(resources, "TmResources.json"));
        var cs = ReadKeys(Path.Combine(resources, "TmResources.cs.json"));
        var fr = ReadKeys(Path.Combine(resources, "TmResources.fr.json"));

        usedKeys.Except(en).Should().BeEmpty("used keys must exist in en");
        usedKeys.Except(cs).Should().BeEmpty("used keys must exist in cs");
        usedKeys.Except(fr).Should().BeEmpty("used keys must exist in fr");
    }

    private static SortedSet<string> ReadKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new SortedSet<string>(
            document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name.StartsWith("TmDocumentEditor_", StringComparison.Ordinal))
                // _Few / _Many are optional plural forms (docs/localization-plurals.md): Czech has them,
                // English and French need only the forms their grammar uses. _One/_Other stay compared.
                .Where(name => !name.EndsWith("_Few", StringComparison.Ordinal) && !name.EndsWith("_Many", StringComparison.Ordinal)),
            StringComparer.Ordinal);
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

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
