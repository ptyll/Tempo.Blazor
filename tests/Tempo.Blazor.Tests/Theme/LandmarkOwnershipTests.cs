using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// A page can have only one meaningful <c>&lt;main&gt;</c> landmark, and the host layout owns it —
/// every shipped demo/app shell renders its own. A component that emits <c>&lt;main&gt;</c>
/// inside a host page produces nested landmarks: AT landmark navigation and strict-mode
/// locators see two mains (the N3/N190 defect class fixed per-component in
/// <c>036ce5fd</c>/<c>a59cac9a</c>, generalised here). Components mark their principal area a
/// NAMED REGION instead — <c>&lt;section aria-label="…"&gt;</c>: a section only becomes a
/// region landmark when it has an accessible name, and it never collides with the page's main.
/// <para>
/// The sweep is fail-closed like <see cref="MarkupClassCoverageTests"/>: every shipped component
/// package under <c>src/Tempo.Blazor*/</c> is scanned for <c>&lt;main&gt;</c> emissions in
/// comment-stripped markup (a commented-out element is not emitted) and the offender set must be
/// EMPTY — there is no component-level reason to own the main landmark. Application projects
/// (<c>Tempo.Blazor.Demo*</c>, <c>Tempo.ReportServer.Web*</c>) are out of scope: their pages and
/// layouts are exactly where a <c>&lt;main&gt;</c> belongs.
/// </para>
/// </summary>
public sealed class LandmarkOwnershipTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex MainElement =
        new(@"<\s*/?\s*main\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    private static readonly Regex RazorComment =
        new(@"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex HtmlComment =
        new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    [Fact]
    public void NoShippedComponentEmitsMainLandmark()
    {
        var population = ComponentMarkupFiles().ToList();
        population.Should().NotBeEmpty(
            "the landmark sweep must read a real markup population — an empty enumeration "
            + "(moved src root, renamed package pattern) would pass vacuously");

        var offenders = new List<string>();

        foreach (var file in population)
        {
            var markup = HtmlComment.Replace(
                RazorComment.Replace(File.ReadAllText(file), string.Empty),
                string.Empty);

            if (MainElement.IsMatch(markup))
            {
                offenders.Add(Path.GetRelativePath(FindRepositoryRoot(), file));
            }
        }

        offenders.Should().BeEmpty(
            "<main> patří host layoutu — komponenta ho nikdy neemituje; hlavní plochu označ " +
            "pojmenovaným regionem (<section aria-label=\"…\">), ne druhým landmarkem. " +
            "Porušení: {0}",
            string.Join(" | ", offenders));
    }

    /// <summary>
    /// Every <c>.razor</c> document a shipped component package could render inside a host page.
    /// Enumerated live so a future satellite package joins the population automatically; build
    /// output trees are excluded the same way the class sweep treats them.
    /// </summary>
    private static IEnumerable<string> ComponentMarkupFiles()
    {
        var src = Path.Combine(FindRepositoryRoot(), "src");
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        return Directory.EnumerateDirectories(src, "Tempo.Blazor*")
            .Where(dir => !Path.GetFileName(dir).Contains(".Demo", StringComparison.Ordinal))
            .SelectMany(dir => Directory
                .EnumerateFiles(dir, "*.razor", SearchOption.AllDirectories)
                .Where(file => !file.Contains(bin, StringComparison.Ordinal)
                            && !file.Contains(obj, StringComparison.Ordinal)));
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
