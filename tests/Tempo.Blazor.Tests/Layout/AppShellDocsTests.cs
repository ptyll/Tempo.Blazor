using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// Plan 5d396592 phase 3: the documentation and the changelog describe the behaviour that shipped —
/// the frame's landmarks, the rail/overlay tablet presentation, the resizable work area with its
/// non-drag alternatives, the persistence rules and the shared resize helper.
/// </summary>
public class AppShellDocsTests
{
    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    [Fact]
    public void AppShellDoc_DocumentsTheApiAndEveryLayout()
    {
        var doc = Read("docs", "app-shell.md");

        foreach (var term in new[]
        {
            "TmAppShell", "AppShellVariant", "Inset", "Flat", "TmLayoutObserver", "TmSidebar", "TmTopBar",
            "TmDrawer", "NavSections", "NavItems", "SidebarCollapsedChanged", "SidebarNavLabel",
            "SkipLinkTarget", "WorkAreaOpen", "WorkAreaWidth", "WorkAreaMinWidth", "WorkAreaMaxWidth",
            "MinContentWidth", "WorkAreaLabel", "PersistKey", "Breakpoints", "768", "1200",
        })
        {
            doc.Should().Contain(term, $"docs/app-shell.md must document {term}");
        }
    }

    [Fact]
    public void AppShellDoc_DocumentsTheLandmarkOwnershipAndTheOverlayNesting()
    {
        var doc = Read("docs", "app-shell.md");

        foreach (var term in new[]
        {
            "IsLandmark", "one `nav`", "one `main`", "one `aside`", "sibling", "container-type",
            "role=\"separator\"", "aria-valuenow", "aria-valuemax", "ArrowLeft", "Shift", "Home",
            "Double click", "WCAG 2.5.7", "forced-colors", "Highlight", "--tm-touch-target",
            "tm-panel-resize.js", "pointer-drag.js", "localStorage", "focus restore",
        })
        {
            doc.Should().Contain(term, $"docs/app-shell.md must document {term}");
        }
    }

    [Fact]
    public void SidebarAndTopBarDoc_DocumentTheAdditiveLandmarkOptOut()
    {
        var doc = Read("docs", "sidebar-and-topbar.md");

        doc.Should().Contain("`IsLandmark`").And.Contain("TmAppShell");
        doc.Should().Contain("plain `<div>`", "the opt-out renders a wrapper instead of the landmark element");
    }

    [Fact]
    public void TouchInteractionDoc_DocumentsTheSharedPanelResizeHelper()
    {
        var doc = Read("docs", "touch-interaction.md");

        doc.Should().Contain("tm-panel-resize.js");
        doc.Should().Contain("clampWidth");
        doc.Should().Contain("PanelResize.Clamp", "the C# keyboard path shares the same vectors");
    }

    [Fact]
    public void Changelog_RecordsTheAppShellPhase()
    {
        var changelog = Read("CHANGELOG.md");
        var unreleased = changelog[..changelog.IndexOf("## 2.9.1 ", StringComparison.Ordinal)];

        foreach (var term in new[]
        {
            "TmAppShell", "AppShellVariant", "tm-panel-resize.js", "IsLandmark", "WorkAreaMinWidth",
            "MinContentWidth", "docs/app-shell.md", "No breaking change",
        })
        {
            unreleased.Should().Contain(term, $"the unreleased changelog section must record {term}");
        }
    }
}
