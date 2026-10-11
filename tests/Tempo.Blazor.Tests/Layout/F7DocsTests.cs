using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// F7: the documentation and the changelog describe the shipped pointer-drag helper, the
/// mandatory non-drag alternative, plurals and the relative-time formatter.
/// </summary>
public class F7DocsTests
{
    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    [Fact]
    public void TouchInteractionDoc_DocumentsTheHelperAndTheMandatoryNonDragAlternative()
    {
        var doc = Read("docs", "touch-interaction.md");

        foreach (var term in new[]
        {
            "pointer-drag.js", "attach(", "threshold", "longPressMs", "onStart", "onMove", "onEnd", "onCancel",
            "setPointerCapture", "pointerId", "lostpointercapture", "pointercancel", "Escape", "touch-action",
            "tm-touch-drag-handle", "tm-pointer-dragging", "prefers-reduced-motion", "reducedMotion",
            "WCAG 2.5.7", "Alt+", "selector", "detach", "scheduler.js", "tm-editor-shell.js",
        })
        {
            doc.Should().Contain(term, $"docs/touch-interaction.md must document {term}");
        }
    }

    [Fact]
    public void LocalizationPluralsDoc_DocumentsPluralsAndRelativeTime()
    {
        var doc = Read("docs", "localization-plurals.md");

        foreach (var term in new[]
        {
            "Loc.Plural", "_One", "_Few", "_Many", "_Other", "TmPluralRules", "ITmRelativeTimeFormatter",
            "Tm_Time_", "TimeProvider", "FakeTimeProvider", "Culture", "TryGetString", "fallback",
        })
        {
            doc.Should().Contain(term, $"docs/localization-plurals.md must document {term}");
        }
    }

    [Fact]
    public void Changelog_RecordsTheF7Surface_AndItsMigration()
    {
        var changelog = Read("CHANGELOG.md");
        var unreleased = changelog[..changelog.IndexOf("## 2.9.0 - ", StringComparison.Ordinal)];

        foreach (var term in new[]
        {
            "pointer-drag.js", "ITmRelativeTimeFormatter", "Loc.Plural", "TmPluralRules", "ITmLocalizer.Culture",
            "scheduler.js", "TmTimeline_MinutesAgo", "Čekající změny",
        })
        {
            unreleased.Should().Contain(term, $"the 2.9.1 changelog must mention {term}");
        }
    }

    [Fact]
    public void PointerDragImports_AreRelative_AndTheSplitPackageSectionExists()
    {
        // A bare `from '_content/...'` specifier throws without an import map. Core imports the
        // sibling file; a split-package module walks up to the core static assets.
        var root = ThemeCss.RepositoryRoot().FullName;
        var doc = Read("docs", "touch-interaction.md");
        var helper = Read("src", "Tempo.Blazor", "wwwroot", "js", "pointer-drag.js");

        doc.Should().NotContain("from '_content/");
        helper.Should().NotContain("from '_content/");
        doc.Should().Contain("./pointer-drag.js").And.Contain("../../Tempo.Blazor/js/pointer-drag.js");
        doc.Should().Contain("Consuming from a split package");
        doc.Should().Contain("document.currentScript").And.Contain("MapStaticAssets").And.Contain("script-src");
        doc.Should().Contain("pointerdown").And.Contain("compatibility mouse");

        foreach (var header in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.js", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            File.ReadAllText(header).Should().NotContain("from '_content/", Path.GetFileName(header));
        }
    }

    [Fact]
    public void AgentsGuide_ListsThePointerDragModule()
    {
        Read("AGENTS.md").Should().Contain("pointer-drag.js");
    }

    [Fact]
    public void ThePointerDragHelper_IsTheOnlyPointerGestureImplementationInTheCoreModulesItMigrated()
    {
        // F6 carry-forward: the resizer must not keep a private copy of capture / cancel / cleanup.
        var shell = Read("src", "Tempo.Blazor", "wwwroot", "js", "tm-editor-shell.js");
        shell.Should().Contain("pointer-drag.js");
        shell.Should().NotContain("setPointerCapture", "the capture belongs to the shared helper");
        shell.Should().NotContain("releasePointerCapture");

        var scheduler = Read("src", "Tempo.Blazor", "wwwroot", "js", "scheduler.js");
        scheduler.Should().Contain("pointer-drag.js");
        scheduler.Should().NotContain("'mousedown'").And.NotContain("'mousemove'").And.NotContain("'mouseup'",
            "B13: the scheduler resize is Pointer Events, not mouse events");
    }
}