using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Localization guard for the DiagramEditor package: every
/// <c>Loc["TmDiagram…"]</c> key referenced from the package's Razor/C# files
/// must exist in the en, cs, sk and fr resource tables, so no raw key can ever
/// surface in the UI. Guards the whole <c>TmDiagram*</c> family used by the
/// package (dialogs, layers, toolbox, canvas, editor).
/// </summary>
public class DiagramEditorLocalizationGuardTests
{
    [Fact]
    public void EveryDiagramKeyUsedInPackage_ExistsInAllFourLanguages()
    {
        var root = FindRepositoryRoot();
        var packageDir = Path.Combine(root, "src", "Tempo.Blazor.DiagramEditor");
        var usedKeys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(packageDir, "*.razor", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(packageDir, "*.cs", SearchOption.AllDirectories))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "Loc\\[\\\"(TmDiagram[A-Za-z0-9_]+)\\\"\\]"))
            {
                usedKeys.Add(match.Groups[1].Value);
            }
        }

        usedKeys.Should().NotBeEmpty("the diagram editor is localized through Loc[...] keys");

        var resources = Path.Combine(root, "src", "Tempo.Blazor", "Resources");
        var en = ReadKeys(Path.Combine(resources, "TmResources.json"));
        var cs = ReadKeys(Path.Combine(resources, "TmResources.cs.json"));
        var sk = ReadKeys(Path.Combine(resources, "TmResources.sk.json"));
        var fr = ReadKeys(Path.Combine(resources, "TmResources.fr.json"));

        usedKeys.Except(en).Should().BeEmpty("used keys must exist in en");
        usedKeys.Except(cs).Should().BeEmpty("used keys must exist in cs");
        usedKeys.Except(sk).Should().BeEmpty("used keys must exist in sk");
        usedKeys.Except(fr).Should().BeEmpty("used keys must exist in fr");
    }

    [Fact]
    public void EditorRazorFiles_HaveNoHardcodedAriaLabelTitleOrPlaceholderText()
    {
        var root = FindRepositoryRoot();
        var componentDir = Path.Combine(root, "src", "Tempo.Blazor.DiagramEditor");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(componentDir, "*.razor", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in AttributeLiteral.Matches(lines[i]))
                {
                    var value = match.Groups["value"].Value;
                    // Literal values without any letters (glyphs like "×", digits, css tokens)
                    // are not user-facing text; anything with letters must come from the localizer.
                    if (value.Any(char.IsLetter))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {match.Value}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "every user-facing aria-label/title/placeholder in the diagram editor must bind a localized value:\n{0}",
            string.Join("\n", offenders));
    }

    private static readonly Regex AttributeLiteral = new(
        "(aria-label|title|placeholder)\\s*=\\s*\"(?<value>[^\"@][^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static SortedSet<string> ReadKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new SortedSet<string>(
            document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => name.StartsWith("TmDiagram", StringComparison.Ordinal)),
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
