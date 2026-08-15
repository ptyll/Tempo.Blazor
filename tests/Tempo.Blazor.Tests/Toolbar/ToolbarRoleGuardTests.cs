using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// 2.8.17 slib: <c>role="toolbar"</c> bez roving tabindexu se nevydává.
/// </summary>
/// <remarks>
/// 2.8.16 to uzavřelo jen u <c>TmFormActionBar</c>. Zbytek (25 atributů / 22 souborů) je stejná
/// vada: odečítač uslyší toolbar, šipky nic neudělají. Soubory se slovem <c>tabindex</c> ho
/// používají na tablist / plátno / komentáře, ne na roving položek toolbaru — změřeno před
/// tímto slibem. Strážce proto tvrdí prázdnou množinu atributů v <c>src/</c> (komentáře se
/// nepočítají).
/// </remarks>
public sealed class ToolbarRoleGuardTests
{
    [Fact]
    public void LibraryAndDemoMarkup_DoesNotClaimToolbarWithoutARovingMechanism()
    {
        var hits = FindToolbarRoles(ReadSrcMarkup());

        hits.Should().BeEmpty(
            "role=toolbar bez roving tabindexu je afordance bez mechanismu. Nalezeno:\n"
            + string.Join("\n", hits));
    }

    [Fact]
    public void TheScanner_ReportsAnAttribute_AndIgnoresAComment()
    {
        const string markup = """
            @* role="toolbar" je zrušený kontrakt *@
            <div role="group" aria-label="akce">A</div>
            <div role="toolbar" aria-label="slib">B</div>
            <!-- role="toolbar" v HTML komentáři -->
            """;

        var hits = ToolbarRoleScanner.Find(markup, "fixture.razor");

        hits.Should().ContainSingle()
            .Which.Should().Contain("role=\"toolbar\"")
            .And.Contain("fixture.razor");
        hits[0].Should().Contain("slib");
    }

    [Fact]
    public void TheScanner_TreatsAnEmptyFileAsClean_NotAsAVacuousPassHiddenByAMissingDenominator()
    {
        ReadSrcMarkup().Should().NotBeEmpty("bez souborů by prázdný seznam toolbarů nic neměřil");
        ToolbarRoleScanner.Find(string.Empty, "empty.razor").Should().BeEmpty();
    }

    private static IReadOnlyList<string> FindToolbarRoles(
        IReadOnlyList<(string RelativePath, string Text)> files)
    {
        return
        [
            .. files.SelectMany(file => ToolbarRoleScanner.Find(file.Text, file.RelativePath))
        ];
    }

    private static IReadOnlyList<(string RelativePath, string Text)> ReadSrcMarkup()
    {
        var root = FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        string[] extensions = [".razor", ".mjs", ".js"];
        return
        [
            .. Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                    extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(path => (
                    RelativePath: Path.GetRelativePath(root, path).Replace('\\', '/'),
                    Text: File.ReadAllText(path)))
        ];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find TempoBlazor.slnx.");
    }
}

internal static class ToolbarRoleScanner
{
    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlComment = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ToolbarAttribute = new(
        @"role\s*=\s*(['""])toolbar\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Find(string markup, string relativePath)
    {
        var stripped = HtmlComment.Replace(RazorComment.Replace(markup, string.Empty), string.Empty);
        var hits = new List<string>();
        foreach (Match match in ToolbarAttribute.Matches(stripped))
        {
            hits.Add($"{relativePath}: {Snippet(stripped, match.Index)}");
        }

        return hits;
    }

    private static string Snippet(string text, int index)
    {
        var from = Math.Max(0, index - 20);
        var to = Math.Min(text.Length, index + 40);
        return text[from..to].Replace('\n', ' ').Trim();
    }
}
