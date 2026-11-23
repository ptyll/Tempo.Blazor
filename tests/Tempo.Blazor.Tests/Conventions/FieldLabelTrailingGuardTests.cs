using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Conventions;

/// <summary>
/// 2.13 field-label parity: every Inputs/Pickers/Dropdowns component that owns a block
/// <c>Label</c> also exposes <c>LabelTrailing</c>. Inline labels (checkbox, toggle, radio)
/// are the documented exceptions.
/// </summary>
public sealed class FieldLabelTrailingGuardTests
{
    private static readonly HashSet<string> InlineLabelExceptions =
    [
        "TmCheckbox",
        "TmToggle",
        "TmRadio",
    ];

    private static readonly Regex LabelParameter = new(
        @"\[Parameter\][^\n]*?(?:\n\s*)?public string\?\s+Label\b",
        RegexOptions.Compiled);

    private static readonly Regex LabelTrailingParameter = new(
        @"\[Parameter\][^\n]*?(?:\n\s*)?public RenderFragment\?\s+LabelTrailing\b",
        RegexOptions.Compiled);

    [Fact]
    public void BlockLabelComponents_InInputsPickersDropdowns_ExposeLabelTrailing()
    {
        var root = FindRepoRoot();
        var folders = new[]
        {
            Path.Combine(root, "src", "Tempo.Blazor", "Components", "Inputs"),
            Path.Combine(root, "src", "Tempo.Blazor", "Components", "Pickers"),
            Path.Combine(root, "src", "Tempo.Blazor", "Components", "Dropdowns"),
        };

        var offenders = new List<string>();
        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "Tm*.*", SearchOption.TopDirectoryOnly)
                         .Where(path => path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                                        || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
                {
                    name = Path.GetFileNameWithoutExtension(name);
                }

                if (InlineLabelExceptions.Contains(name))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (!LabelParameter.IsMatch(text))
                {
                    continue;
                }

                if (!LabelTrailingParameter.IsMatch(text)
                    && !HasLabelTrailingInPair(file, name))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        offenders.Should().BeEmpty(
            "every block-label component in Inputs/Pickers/Dropdowns must expose LabelTrailing; missing:\n"
            + string.Join("\n", offenders));
    }

    private static bool HasLabelTrailingInPair(string file, string componentName)
    {
        var dir = Path.GetDirectoryName(file)!;
        var sibling = file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(dir, componentName + ".razor.cs")
            : Path.Combine(dir, componentName + ".razor");
        if (!File.Exists(sibling))
        {
            return false;
        }

        return LabelTrailingParameter.IsMatch(File.ReadAllText(sibling));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
