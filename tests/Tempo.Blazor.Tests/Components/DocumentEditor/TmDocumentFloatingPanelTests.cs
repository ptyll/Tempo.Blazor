using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 5: the one frame of every floating editing surface of the editor - a thin wrapper over the core
/// TmOverlayPanel (no own sheet, no own focus trap) - and the titled palette grid the ribbon panels are made of.
/// </summary>
public sealed class TmDocumentFloatingPanelTests : LocalizationTestBase
{
    private IRenderedComponent<TmDocumentFloatingPanel> RenderPanel(
        Action<ComponentParameterCollectionBuilder<TmDocumentFloatingPanel>>? configure = null,
        TmLayoutMode mode = TmLayoutMode.Desktop,
        bool open = true)
        => Render<TmDocumentFloatingPanel>(p =>
        {
            p.Add(x => x.IsOpen, open)
             .Add(x => x.Title, "Panel title")
             .Add(x => x.Kind, "test")
             .Add(x => x.PanelTestId, "floating-panel")
             .Add(x => x.CloseTestId, "floating-close")
             .Add(x => x.LayoutMode, mode)
             .AddChildContent("<p data-testid=\"body\">Body</p>");
            configure?.Invoke(p);
        });

    [Fact]
    public void Desktop_RendersAnAnchoredDialog_WithoutAScrim_NamedByItsTitle()
    {
        var cut = RenderPanel();

        var panel = cut.Find("[data-testid='floating-panel']");
        panel.GetAttribute("role").Should().Be("dialog");
        panel.GetAttribute("popover").Should().Be("manual", "the panel is a core top-layer popover");
        panel.ClassList.Should().Contain("tm-document-floating-panel").And.Contain("tm-document-floating-panel--medium");
        panel.GetAttribute("data-floating-kind").Should().Be("test");
        cut.Find($"[id='{panel.GetAttribute("aria-labelledby")}']").TextContent.Should().Be("Panel title");
        cut.FindAll(".tm-modal-overlay, .tm-drawer__overlay, .tm-document-dialog-scrim").Should().BeEmpty("a desktop menu has no scrim");
        cut.Find("[data-testid='body']").Should().NotBeNull();
    }

    [Fact]
    public void ForcedMobile_RendersTheCoreSheet_WithAriaModal_AndNoOwnSheet()
    {
        var cut = RenderPanel(mode: TmLayoutMode.Mobile);

        var sheet = cut.Find(".tm-overlay-panel-sheet[role='dialog']");
        sheet.GetAttribute("aria-modal").Should().Be("true");
        sheet.ClassList.Should().Contain("tm-drawer--bottom", "the sheet is the core bottom drawer");
        cut.FindAll(".tm-overlay-panel[popover]").Should().BeEmpty("a sheet is not the anchored popover");
        cut.Find("[data-testid='body']").Should().NotBeNull();
        // The sheet header carries the title and Done: the frame renders no second header.
        cut.FindAll(".tm-document-floating-panel__header").Should().BeEmpty();
    }

    [Fact]
    public void WithAMobileViewportScope_TheCoreSheetIsUsed_AndTheFrameDropsItsOwnHeader()
    {
        // The page's viewport scope resolves Mobile on the FIRST render, which the core panel never reports as a change.
        var cut = Render<TmDocumentFloatingPanel>(p => p
            .AddCascadingValue(Tempo.Blazor.Abstractions.Layout.TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile))
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Panel title")
            .AddChildContent("<p data-testid=\"body\">Body</p>"));

        cut.Find(".tm-overlay-panel-sheet[role='dialog']").Should().NotBeNull();
        cut.FindAll(".tm-document-floating-panel__header").Should().BeEmpty("the sheet header already carries the title and Done");
    }

    [Fact]
    public void CloseButton_HasALocalizedLabel_AndRaisesTheButtonReason()
    {
        DocumentFloatingCloseReason? reason = null;
        var cut = RenderPanel(p => p.Add(x => x.OnClose, EventCallback.Factory.Create<DocumentFloatingCloseReason>(this, r => reason = r)));

        var close = cut.Find("[data-testid='floating-close']");
        close.GetAttribute("aria-label").Should().Be("Close");

        close.Click();

        reason.Should().Be(DocumentFloatingCloseReason.Button);
    }

    [Fact]
    public async Task Escape_Closes_AndGivesFocusBackToTheRestoreTarget()
    {
        DocumentFloatingCloseReason? reason = null;
        var restored = 0;
        var cut = RenderPanel(p => p
            .Add(x => x.RestoreFocusTarget, new ElementReference("canvas-input"))
            .Add(x => x.OnRestoreFocus, EventCallback.Factory.Create(this, () => restored++))
            .Add(x => x.OnClose, EventCallback.Factory.Create<DocumentFloatingCloseReason>(this, r => reason = r)));

        // overlay.js reports Escape (window capture) through this JSInvokable of the core panel.
        var core = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => core.Instance.NotifyDismissedAsync("escape"));

        reason.Should().Be(DocumentFloatingCloseReason.Escape);
        restored.Should().Be(1, "the host moves focus to its canvas");
        JSInterop.VerifyFocusAsyncInvoke().Should().NotBeNull("the restore target itself is focused first");
    }

    [Fact]
    public async Task OutsidePress_Closes_WithoutRestoringFocus_BecauseTheUserClickedSomewhereElse()
    {
        DocumentFloatingCloseReason? reason = null;
        var restored = 0;
        var cut = RenderPanel(p => p
            .Add(x => x.OnRestoreFocus, EventCallback.Factory.Create(this, () => restored++))
            .Add(x => x.OnClose, EventCallback.Factory.Create<DocumentFloatingCloseReason>(this, r => reason = r)));

        var core = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => core.Instance.NotifyDismissedAsync("outside"));

        reason.Should().Be(DocumentFloatingCloseReason.Outside);
        restored.Should().Be(0);
    }

    [Fact]
    public void Panel_FencesPointerAndClickInsideIt_SoTheCanvasSelectionSurvives()
    {
        var cut = RenderPanel();

        var frame = cut.Find(".tm-document-floating-panel__frame");
        frame.GetAttribute("tabindex").Should().Be("-1");
        cut.Markup.Should().Contain("blazor:onclick:stopPropagation").And.Contain("blazor:onpointerdown:stopPropagation");
    }

    [Fact]
    public void Footer_IsRenderedOnlyWhenProvided()
    {
        RenderPanel().FindAll(".tm-document-floating-panel__footer").Should().BeEmpty();

        var with = RenderPanel(p => p.Add(x => x.Footer, (RenderFragment)(b => b.AddMarkupContent(0, "<button data-testid=\"done\">Done</button>"))));
        with.Find(".tm-document-floating-panel__footer [data-testid='done']").Should().NotBeNull();
    }

    [Fact]
    public void Width_IsAModifierClass()
    {
        RenderPanel(p => p.Add(x => x.Width, DocumentFloatingPanelWidth.Wide)).Find("[data-testid='floating-panel']")
            .ClassList.Should().Contain("tm-document-floating-panel--wide");
        RenderPanel(p => p.Add(x => x.Width, DocumentFloatingPanelWidth.Compact)).Find("[data-testid='floating-panel']")
            .ClassList.Should().Contain("tm-document-floating-panel--compact");
    }

    [Fact]
    public void Panel_RendersNothing_WhenClosed()
    {
        var cut = RenderPanel(open: false);

        cut.FindAll("[data-testid='floating-panel']").Should().BeEmpty();
    }

    // ─── DocumentPaletteGrid ────────────────────────────────────────────────

    private IRenderedComponent<DocumentPaletteGrid<string>> RenderGrid(int columns, int count, Action<string>? onSelect = null)
        => Render<DocumentPaletteGrid<string>>(p => p
            .Add(x => x.Heading, "Quick styles")
            .Add(x => x.Columns, columns)
            .Add(x => x.Items, Enumerable.Range(0, count).Select(i => $"item-{i}").ToList())
            .Add(x => x.ItemTestId, item => $"tile-{item}")
            .Add(x => x.ItemContent, item => (RenderFragment)(b => b.AddContent(0, item)))
            .Add(x => x.OnSelect, EventCallback.Factory.Create<string>(this, item => onSelect?.Invoke(item))));

    [Fact]
    public void PaletteGrid_HasAVisibleHeading_AndNamesItsGroupByIt()
    {
        var cut = RenderGrid(6, 12);

        var heading = cut.Find(".tm-document-palette-grid__heading");
        heading.TextContent.Should().Be("Quick styles");
        var group = cut.Find("[role='group']");
        group.GetAttribute("aria-labelledby").Should().Be(heading.Id);
        cut.Find("section.tm-document-palette-grid").GetAttribute("aria-labelledby").Should().Be(heading.Id);
        cut.Find("section").GetAttribute("data-columns").Should().Be("6");
        group.GetAttribute("style").Should().Contain("--tm-document-palette-columns: 6");
    }

    [Theory]
    [InlineData(6, 0, "ArrowRight", 1)]
    [InlineData(6, 0, "ArrowDown", 6)]
    [InlineData(6, 7, "ArrowUp", 1)]
    [InlineData(6, 3, "ArrowUp", 3)]
    [InlineData(6, 11, "ArrowDown", 11)]
    [InlineData(6, 11, "ArrowRight", 11)]
    [InlineData(6, 0, "ArrowLeft", 0)]
    [InlineData(6, 4, "Home", 0)]
    [InlineData(6, 4, "End", 11)]
    [InlineData(8, 1, "ArrowDown", 9)]
    public void PaletteGrid_ArrowNavigation_RespectsTheColumnCount(int columns, int from, string key, int expected)
        => DocumentPaletteGrid<string>.NextIndex(from, key, columns, 12).Should().Be(expected);

    [Fact]
    public void PaletteGrid_KeepsOneTabStop_AndMovesItWithTheArrowKeys()
    {
        var cut = RenderGrid(6, 12);

        cut.FindAll("button[tabindex='0']").Should().ContainSingle().Which.GetAttribute("data-testid").Should().Be("tile-item-0");

        cut.Find("[data-testid='tile-item-0']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.FindAll("button[tabindex='0']").Should().ContainSingle().Which.GetAttribute("data-testid").Should().Be("tile-item-6");
    }

    [Fact]
    public void PaletteGrid_ClickAppliesAtOnce_WithNoConfirmFooter()
    {
        string? picked = null;
        var cut = RenderGrid(6, 12, item => picked = item);

        cut.Find("[data-testid='tile-item-3']").Click();

        picked.Should().Be("item-3");
        cut.FindAll("footer, .tm-document-floating-panel__footer").Should().BeEmpty("G8: choices apply immediately");
    }
}
