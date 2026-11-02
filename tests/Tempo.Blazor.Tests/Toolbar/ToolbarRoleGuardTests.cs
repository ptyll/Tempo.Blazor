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
/// <para>
/// 2.8.26 rozšiřuje sondu o <c>COMPONENTS.md</c>: dokumentace tvrdila u <c>tm-form-action-bar</c>
/// <c>role="toolbar"</c> poté, co komponenta přešla na <c>role="group"</c> — stejný slib bez
/// mechanismu, jen o úroveň výš. Sonda čte dokument jako markup, takže věta v backtitech padá
/// stejně jako atribut v razoru.
/// </para>
/// </remarks>
public sealed class ToolbarRoleGuardTests
{
    /// <summary>
    /// The markup that claims <c>role="toolbar"</c>, each with the mechanism it ships: core <c>TmToolbar</c>
    /// (tm-toolbar.js: roving tabindex + arrow/Home/End) and the AI package's <c>TmAiMessage</c> action bar
    /// (a Blazor-owned roving tabindex, no second JS gesture module). Anything else stays banned; a new
    /// claimant must add itself here AND pass <see cref="EveryAllowListedToolbar_ShipsTheMechanismItClaims"/>.
    /// </summary>
    /// <remarks>
    /// The mechanism half of this guard used to be dead code: its assertion block carried literal
    /// <c>\n</c> sequences instead of newlines, so — being preceded by <c>//</c> — the whole block sat
    /// inside one line comment and only the allow-list membership was ever measured. It now runs through
    /// <see cref="ToolbarMechanismCheck"/>, which is itself pinned on a synthetic toolbar that claims the
    /// role and ships nothing (<see cref="TheMechanismCheck_RejectsAToolbarWithoutItsMechanism"/>).
    /// </remarks>
    private static readonly (string RelativePath, ToolbarMechanism Mechanism)[] RovingToolbarImplementations =
    [
        ("src/Tempo.Blazor/Components/Toolbar/TmToolbar.razor", ToolbarMechanism.CoreModule),
        ("src/Tempo.Blazor.AI/Components/AI/TmAiMessage.razor", ToolbarMechanism.BlazorRovingTabIndex),
    ];

    [Fact]
    public void EveryAllowListedToolbar_ShipsTheMechanismItClaims()
    {
        var root = FindRepositoryRoot();
        foreach (var (relative, mechanism) in RovingToolbarImplementations)
        {
            var markup = File.ReadAllText(Path.Combine(root, relative));
            ToolbarRoleScanner.Find(markup, relative).Should().NotBeEmpty(
                $"{relative} is on the allow-list because it claims role=toolbar");

            // H14: a comment mentioning the module satisfied the old Contain("tm-toolbar.js"). The markup
            // must USE the mechanism, with the comments stripped first.
            ToolbarMechanismCheck.Findings(mechanism, relative, markup).Should().BeEmpty(
                "{0} claims role=toolbar but does not ship the roving mechanism it declared:{1}{2}",
                relative,
                Environment.NewLine,
                string.Join(Environment.NewLine, ToolbarMechanismCheck.Findings(mechanism, relative, markup)));
        }

        var interop = ToolbarMechanismCheck.StripComments(
            File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor", "Helpers", "ToolbarInterop.cs")));
        interop.Should().Contain("\"./_content/Tempo.Blazor/js/tm-toolbar.js\"").And.Contain("\"attach\"");

        var module = File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "js", "tm-toolbar.js"));
        module.Should().Contain("export function nextRovingIndex").And.Contain("export function rovingItems")
            .And.Contain("tabindex").And.Contain("keydown");
    }

    /// <summary>
    /// The checker itself is pinned on a synthetic toolbar that claims the role and ships nothing: without
    /// this fact a swallowed or vacuous check would pass for every real component (the previous revision of
    /// this guard had its whole assertion block inside a line comment).
    /// </summary>
    [Fact]
    public void TheMechanismCheck_RejectsAToolbarWithoutItsMechanism()
    {
        const string markup = """
            <div role="toolbar" aria-label="akce"><button type="button">A</button></div>
            """;

        ToolbarMechanismCheck.Findings(ToolbarMechanism.BlazorRovingTabIndex, "fixture.razor", markup)
            .Should().NotBeEmpty();
        ToolbarMechanismCheck.Findings(ToolbarMechanism.CoreModule, "fixture.razor", markup)
            .Should().NotBeEmpty();

        const string blazor = """
            <div role="toolbar" aria-label="akce" @onkeydown="HandleKeyDown">
                <button type="button" tabindex="@TabIndexFor(0)">A</button>
            </div>
            """;
        ToolbarMechanismCheck.Findings(ToolbarMechanism.BlazorRovingTabIndex, "fixture.razor", blazor)
            .Should().NotBeEmpty("Arrow keys and a focus move are part of the mechanism");

        var code = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.AI", "Components", "AI", "TmAiMessage.razor"));
        ToolbarMechanismCheck.Findings(ToolbarMechanism.BlazorRovingTabIndex, "TmAiMessage.razor", code)
            .Should().BeEmpty();
    }

    [Fact]
    public void LibraryAndDemoMarkup_DoesNotClaimToolbarWithoutARovingMechanism()
    {
        var hits = FindToolbarRoles(ReadSrcMarkup().Where(file => !RovingToolbarImplementations.Any(entry => entry.RelativePath == file.RelativePath)).ToList());

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
        var files = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (
                RelativePath: Path.GetRelativePath(root, path).Replace('\\', '/'),
                Text: File.ReadAllText(path)))
            .ToList();

        // COMPONENTS.md is the contract a consumer reads before the markup itself; a role claim
        // there is the same affordance without a mechanism, one level up.
        var componentsDoc = Path.Combine(root, "COMPONENTS.md");
        if (File.Exists(componentsDoc))
        {
            files.Add(("COMPONENTS.md", File.ReadAllText(componentsDoc)));
        }

        return files;
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

/// <summary>How a <c>role="toolbar"</c> markup implements the roving tabindex it promises.</summary>
internal enum ToolbarMechanism
{
    /// <summary>The core <c>tm-toolbar.js</c> module, attached through <c>ToolbarInterop</c>.</summary>
    CoreModule,

    /// <summary>
    /// A Blazor-owned roving tabindex: the <c>tabindex</c> of every control is COMPUTED from one tab-stop
    /// field, Arrow/Home/End are handled in C#, and focus follows the tab stop through
    /// <c>ElementReference.FocusAsync</c> — no second JS gesture module. The pattern
    /// <c>TmEmailVariablePicker</c> and <c>TmModelingModelTree</c> already use.
    /// </summary>
    BlazorRovingTabIndex,
}

/// <summary>
/// Static probe of one allow-listed <c>role="toolbar"</c> markup: every promise the role makes has to be
/// backed by code in the file, with Razor and HTML comments stripped first (a comment mentioning the
/// mechanism is not the mechanism).
/// </summary>
internal static class ToolbarMechanismCheck
{
    private static readonly Regex RazorComment = new(@"@\*.*?\*@", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlComment = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineComment = new(@"//.*?$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Returns one finding per missing piece of the declared mechanism; empty when it is complete.</summary>
    public static IReadOnlyList<string> Findings(
        ToolbarMechanism mechanism,
        string relativePath,
        string markup)
    {
        var code = StripComments(markup);
        var findings = new List<string>();

        void Require(bool present, string description)
        {
            if (!present)
            {
                findings.Add($"{relativePath}: {description}");
            }
        }

        switch (mechanism)
        {
            case ToolbarMechanism.CoreModule:
                Require(code.Contains("new ToolbarInterop(", StringComparison.Ordinal), "no ToolbarInterop wrapper");
                Require(code.Contains("AttachAsync(", StringComparison.Ordinal), "no AttachAsync call");
                break;

            case ToolbarMechanism.BlazorRovingTabIndex:
                Require(code.Contains("tabindex=", StringComparison.Ordinal), "no tabindex attribute");
                Require(code.Contains("TabIndexFor(", StringComparison.Ordinal), "the tab stop is not computed");
                Require(code.Contains("\"ArrowRight\"", StringComparison.Ordinal), "ArrowRight is not handled");
                Require(code.Contains("\"ArrowLeft\"", StringComparison.Ordinal), "ArrowLeft is not handled");
                Require(code.Contains("\"Home\"", StringComparison.Ordinal), "Home is not handled");
                Require(code.Contains("\"End\"", StringComparison.Ordinal), "End is not handled");
                Require(code.Contains("FocusAsync(", StringComparison.Ordinal), "the tab stop never moves the focus");
                break;

            default:
                findings.Add($"{relativePath}: unknown mechanism {mechanism}");
                break;
        }

        return findings;
    }

    /// <summary>Removes Razor, HTML and line comments so a mentioning comment cannot satisfy a check.</summary>
    public static string StripComments(string markup)
        => LineComment.Replace(
            HtmlComment.Replace(RazorComment.Replace(markup, string.Empty), string.Empty),
            string.Empty);
}
