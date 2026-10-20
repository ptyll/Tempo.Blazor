using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>
/// Phase 5.1: the core library must not execute script strings through JS interop.
/// Focus moves through ElementReference.FocusAsync or a named function in an existing module.
/// TmGantt still measures its timeline through eval; that one file is the only exception.
/// </summary>
public class NoEvalInCoreLibraryTests
{
    private static readonly Regex EvalInvoke = new(
        @"Invoke(?:Void)?Async(?:<[^>]+>)?\(\s*""eval""",
        RegexOptions.Compiled);

    [Fact]
    public void CoreLibrary_DoesNotInvokeEval_ExceptTmGantt()
    {
        var root = FindRepoRoot();
        var srcDir = Path.Combine(root.FullName, "src", "Tempo.Blazor");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root.FullName, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
                continue;
            if (relative.EndsWith("Components/Scheduler/TmGantt.razor.cs", StringComparison.Ordinal))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (EvalInvoke.IsMatch(lines[i]))
                    offenders.Add($"{relative}:{i + 1}  {lines[i].Trim()}");
            }
        }

        offenders.Should().BeEmpty(
            "core JS interop must call a named function, never eval (TmGantt is the only exception):\n" +
            string.Join("\n", offenders));
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
                return directory;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
