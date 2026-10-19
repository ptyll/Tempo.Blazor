using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// DiagramEditor redesign phase 6 (X2): the core <see cref="TmEditorShell"/> carries an optional
/// third mobile sheet tab through the <see cref="TmEditorShell.Extra"/> /
/// <see cref="TmEditorShell.ExtraTitle"/> slot and <see cref="EditorShellPanel.Extra"/>, so a
/// host can present flat sheet tabs (Shapes | Properties | Layers) instead of nesting tabs inside
/// the right panel. The tab renders ONLY in the mobile Sheet presentation, opens and closes with
/// the right panel, and is selected through <c>ActiveMobilePanel</c>. Without <c>Extra</c> the
/// sheet is unchanged (backward compatible).
/// </summary>
public class TmEditorShellExtraPanelTests : LocalizationTestBase
{
    private static IReadOnlyList<TmActionItem> Actions =>
    [
        new TmActionItem { Id = "save", Label = "Save", OnClick = EventCallback.Factory.Create(new object(), () => { }) },
    ];

    private IRenderedComponent<TmEditorShell> RenderShell(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmEditorShell>>? extra = null,
        bool withExtra = true)
    {
        return Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Header, builder => builder.AddContent(0, "Shell header"));
            p.Add(x => x.Toolbar, builder => builder.AddContent(0, "Shell toolbar"));
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.StatusBar, builder => builder.AddContent(0, "Status text"));
            if (withExtra)
            {
                p.Add(x => x.Extra, builder => builder.AddContent(0, "Extra layers"));
                p.Add(x => x.ExtraTitle, "Layers");
            }
            extra?.Invoke(p);
        });
    }

    [Fact]
    public void MobileSheet_WithExtra_RendersThreeFlatTabs()
    {
        var cut = RenderShell(TmLayoutMode.Mobile);

        var tabs = cut.FindAll(".tm-editor-shell__sheet [role='tablist'] [role='tab']");
        tabs.Should().HaveCount(3);
        tabs.Select(t => t.TextContent.Trim()).Should().Equal("Blocks", "Properties", "Layers");

        // Each tab controls a REAL tabpanel (aria-controls -> id, tabpanel labelled by the tab).
        foreach (var tab in tabs)
        {
            var panel = cut.Find($"#{tab.GetAttribute("aria-controls")}");
            panel.GetAttribute("role").Should().Be("tabpanel");
            panel.GetAttribute("aria-labelledby").Should().Be(tab.Id);
        }

        // The extra panel starts hidden (the left panel is the default tab).
        cut.Find("[role='tabpanel'][data-region='extra']").HasAttribute("hidden").Should().BeTrue();
        cut.Find("[role='tabpanel'][data-region='extra']").TextContent.Should().Contain("Extra layers");
    }

    [Fact]
    public void MobileSheet_SelectingExtraTab_RaisesExtra_AndShowsIt()
    {
        EditorShellPanel? raised = null;
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.ActiveMobilePanelChanged,
            EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v)));

        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[2].Click();

        raised.Should().Be(EditorShellPanel.Extra);
        cut.Find("[role='tabpanel'][data-region='extra']").HasAttribute("hidden").Should().BeFalse();
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeTrue();
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeTrue();
    }

    [Fact]
    public void MobileSheet_HostSetsActiveMobilePanelExtra_ShowsExtra()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.ActiveMobilePanel, EditorShellPanel.Extra));

        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[2]
            .GetAttribute("aria-selected").Should().Be("true");
        cut.Find("[role='tabpanel'][data-region='extra']").HasAttribute("hidden").Should().BeFalse();
    }

    [Fact]
    public void MobileSheet_ExtraClosesWithTheRightPanel()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.RightOpen, false));

        cut.FindAll("[role='tabpanel'][data-region='extra']").Should().BeEmpty(
            "the extra tab only exists while the right panel is open");
        cut.FindAll(".tm-editor-shell__sheet [role='tablist']").Should().BeEmpty("only the left panel is open");
        cut.Find(".tm-editor-shell__sheet .tm-drawer__title").TextContent.Trim().Should().Be("Blocks");
    }

    [Fact]
    public void MobileSheet_ArrowKeys_RoveAcrossThreeTabs()
    {
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[0].KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[2]
            .GetAttribute("aria-selected").Should().Be("true", "End moves to the last tab");

        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[2].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[0]
            .GetAttribute("aria-selected").Should().Be("true", "ArrowRight wraps to the first tab");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    public void DesktopAndTablet_IgnoreExtra(TmLayoutMode mode)
    {
        var cut = RenderShell(mode);

        cut.FindAll("[data-region='extra']").Should().BeEmpty();
        cut.Markup.Should().NotContain("Extra layers");
    }

    [Fact]
    public void MobileTabsPresentation_IgnoresExtra()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));

        cut.FindAll("[data-region='extra']").Should().BeEmpty();
        cut.Markup.Should().NotContain("Extra layers");
    }

    [Fact]
    public void MobileSheet_WithoutExtra_IsUnchanged()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, withExtra: false);

        var tabs = cut.FindAll(".tm-editor-shell__sheet [role='tablist'] [role='tab']");
        tabs.Should().HaveCount(2, "without Extra the sheet keeps its two tabs");
        tabs.Select(t => t.TextContent.Trim()).Should().Equal("Blocks", "Properties");
    }

    [Fact]
    public void MobileSheet_WithExtraAndMobileActions_RendersTheActionBar()
    {
        // The Extra tab must not break the F5 action bar contract.
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobileActions, Actions));

        cut.Find(".tm-mobile-action-bar__bar").Should().NotBeNull();
        cut.FindAll(".tm-editor-shell__sheet [role='tab']").Should().HaveCount(3);
    }

    [Fact]
    public void MobileSheet_ExtraTitleNull_UsesTheLocalizedDefault()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, withExtra: false);
        cut.Render(p =>
        {
            p.Add(x => x.Extra, builder => builder.AddContent(0, "Extra layers"));
        });

        cut.FindAll(".tm-editor-shell__sheet [role='tab']")[2].TextContent.Trim()
            .Should().Be("Additional panel");
    }
}
