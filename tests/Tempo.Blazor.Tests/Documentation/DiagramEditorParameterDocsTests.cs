using FluentAssertions;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// D10: AGENTS.md requires an XML summary on every <c>[Parameter]</c>/<c>[CascadingParameter]</c>.
/// This guard walks all DiagramEditor sources and, for every parameter attribute line, walks up
/// over all consecutive attribute-only lines (they may stack, e.g. <c>[Parameter]</c> above
/// <c>[EditorRequired]</c>, and the property may sit on the line below a standalone attribute)
/// and then requires a <c>///</c> doc comment above the stack.
/// </summary>
public class DiagramEditorParameterDocsTests
{
    [Fact]
    public void EveryDiagramEditorParameter_HasXmlSummary()
    {
        var root = FindRepositoryRoot();
        var packageRoot = Path.Combine(root, "src", "Tempo.Blazor.DiagramEditor");
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(packageRoot, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                var isParameter = trimmed.StartsWith("[Parameter", StringComparison.Ordinal)
                                  || trimmed.StartsWith("[CascadingParameter", StringComparison.Ordinal);
                if (!isParameter)
                    continue;

                // The property may sit on the same line as the attribute
                // ("[Parameter] public string X { get; set; }") or on the line below a
                // standalone attribute; attribute lines may also stack
                // ("[Parameter]" / "[EditorRequired]" / "public … { get; set; }"). Walk up
                // over ALL consecutive attribute-only lines, skip blanks, then require a ///
                // doc comment above the stack.
                var j = i - 1;
                while (j >= 0 && IsAttributeOnlyLine(lines[j].TrimStart()))
                    j--;
                while (j >= 0 && string.IsNullOrWhiteSpace(lines[j]))
                    j--;
                if (j < 0 || !lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal))
                    violations.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        violations.Should().BeEmpty(
            "every DiagramEditor [Parameter]/[CascadingParameter] must carry an XML <summary> " +
            "(file:line of the undocumented properties above)");
    }

    /// <summary>An attribute-only line: starts with '[' and carries no member body (a '{' would
    /// mark a property declaration on the same line).</summary>
    private static bool IsAttributeOnlyLine(string trimmed)
        => trimmed.StartsWith("[", StringComparison.Ordinal) && !trimmed.Contains('{');

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
