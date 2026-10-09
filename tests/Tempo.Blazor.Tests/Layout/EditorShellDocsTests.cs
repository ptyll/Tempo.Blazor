using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// F6 review round 1, F16-F18: the documentation and the changelog describe the behaviour that
/// shipped (decisions Q1-Q4), not the one the first implementation had.
/// </summary>
public class EditorShellDocsTests
{
    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    [Fact]
    public void EditorShellDoc_DescribesTheDockedTabletAndTheOtherQ1ToQ4Decisions()
    {
        var doc = Read("docs", "editor-shell.md");

        doc.Should().NotContain("modal side sheet", "tablet is docked panels, never a modal (Q1=A)");
        doc.Should().NotContain("tablet one side sheet");
        foreach (var term in new[]
        {
            "ActiveMobilePanel", "MobileSheetSnapIndex", "CanvasTitle", "Breakpoints", "TmLayoutBreakpoints",
            "MinCanvasWidth", "MinLeftWidth", "MaxRightWidth", "PersistWidthsKey", "role=\"separator\"",
            "LeftRail", "RightRail", "FooterContent", "SidePanelSide", "at most one", "Bloky",
        })
        {
            doc.Should().Contain(term, $"docs/editor-shell.md must document {term}");
        }
    }

    [Fact]
    public void ObsoleteTabletSideSheetWording_IsGoneFromSourceComments()
    {
        Read("src", "Tempo.Blazor", "Components", "Layout", "SidePanelPresentation.cs")
            .Should().NotContain("below the desktop breakpoint");
        Read("src", "Tempo.Blazor", "Components", "Layout", "TmSidePanel.razor")
            .Should().NotContain("docks below the");
        Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_side-panel.css")
            .Should().NotContain("sheet is");
    }

    [Fact]
    public void DeadPanelsToggleCss_IsRemoved()
    {
        Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_editor-shell.css")
            .Should().NotContain("__panels-toggle").And.NotContain("__panels {");
    }

    [Fact]
    public void OverlaysDocAndDrawerCss_PointAtTmTopLayerForPromotion()
    {
        Read("docs", "overlays.md").Should().NotContain("(`TopLayerInterop` → `tm-sheet.js`)");
        Read("docs", "overlays.md").Should().Contain("tm-top-layer.js");
        Read("src", "Tempo.Blazor", "wwwroot", "css", "components", "_drawer.css")
            .Should().NotContain("showPopover (tm-sheet.js)");
    }

    [Fact]
    public void Changelog_RecordsTheScrollRegionTabStopChange_AndTheNewApi()
    {
        var changelog = Read("CHANGELOG.md");
        var unreleased = changelog[..changelog.IndexOf("## 2.9.0 - ", StringComparison.Ordinal)];

        unreleased.Should().Contain("tab stop", "the syncScrollRegion adoption adds a tab stop to an overflowing TmDrawer/TmModal body");
        foreach (var term in new[]
        {
            "TmLayoutBreakpoints", "ActiveMobilePanel", "MobileSheetSnapIndex", "CanvasTitle", "MinCanvasWidth",
            "LeftRail", "FooterContent", "SidePanelSide", "separator",
        })
        {
            unreleased.Should().Contain(term, $"the unreleased changelog must mention {term}");
        }
        unreleased.Should().NotContain("tablet** shows at most one modal side sheet");
    }
}