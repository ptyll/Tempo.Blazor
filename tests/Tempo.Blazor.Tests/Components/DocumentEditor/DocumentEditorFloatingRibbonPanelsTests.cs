using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 5 (DE-1.4, DE-1.7): every ribbon popup is a TmDocumentFloatingPanel over the core overlay, rendered AFTER
/// the toolbar - the style gallery, the table grid, the equation and symbol palettes, the page layout, the header/footer menus and
/// the link popover. Each one has a More form, so nothing stays pinned and nothing needs a click-away backdrop.
/// </summary>
public sealed class DocumentEditorFloatingRibbonPanelsTests : LocalizationTestBase
{
    private IRenderedComponent<TmDocumentEditorToolbar> Render(Action<ComponentParameterCollectionBuilder<TmDocumentEditorToolbar>>? configure = null)
        => Render<TmDocumentEditorToolbar>(p =>
        {
            p.Add(x => x.ShowAdvancedCharacterFormatting, true)
             .Add(x => x.ShowBlockStyle, true)
             .Add(x => x.ShowStyleManagement, true)
             .Add(x => x.ShowEquationTools, true)
             .Add(x => x.ShowSymbolTools, true);
            configure?.Invoke(p);
        });

    private static void OpenTab(IRenderedComponent<TmDocumentEditorToolbar> cut, string tab)
        => cut.Find($"[data-testid='document-ribbon-tab-{tab}']").Click();

    private static void OpenStyleGallery(IRenderedComponent<TmDocumentEditorToolbar> cut)
    {
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();
        cut.Find("[role='menu'] [data-testid='document-style-gallery-toggle']").Click();
        // The More entry closes the menu and then runs its action.
        cut.WaitForElement("[data-testid='document-style-gallery']");
    }

    // ─── style gallery ───────────────────────────────────────────────────────

    [Fact]
    public void StyleGallery_IsAFloatingPanel_WithALiveTypographySamplePerTile()
    {
        var cut = Render(p => p.Add(x => x.CurrentBlockStyle, "Heading1"));
        OpenStyleGallery(cut);

        var panel = cut.Find("[data-testid='document-style-gallery']");
        panel.GetAttribute("role").Should().Be("dialog");
        panel.ClassList.Should().Contain("tm-document-floating-panel--wide");

        var tiles = cut.FindAll("[data-testid^='document-style-quick-']");
        tiles.Should().HaveCount(6);
        var heading = cut.Find("[data-testid='document-style-quick-heading-1'] .tm-document-style-gallery__sample");
        heading.GetAttribute("style").Should().Contain("font-size").And.Contain("font-weight").And.Contain("color");
        var normal = cut.Find("[data-testid='document-style-quick-normal'] .tm-document-style-gallery__sample");
        normal.GetAttribute("style").Should().NotBe(heading.GetAttribute("style"), "a heading does not look like normal text");
    }

    [Fact]
    public void StyleGallery_HasTitledSections_AndTheRenamedFieldLabels()
    {
        var cut = Render();
        OpenStyleGallery(cut);

        cut.FindAll(".tm-document-palette-grid__heading").Select(h => h.TextContent).Should().Contain(["Quick styles", "Style actions"]);
        foreach (var section in cut.FindAll("[data-testid='document-style-gallery'] section[aria-labelledby]"))
        {
            cut.Find($"[id='{section.GetAttribute("aria-labelledby")}']").TextContent.Should().NotBeNullOrWhiteSpace();
        }

        var labels = cut.FindAll("[data-testid='document-style-gallery'] .tm-document-inspector-form__label").Select(l => l.TextContent).ToList();
        labels.Should().Contain(["Font size", "New style name", "Current style name"]);
        cut.FindAll("[data-testid='document-style-gallery'] [data-testid='document-style-quick-normal']").Should().ContainSingle();
    }

    [Fact]
    public void StyleGallery_ClickAppliesAtOnce_AndThereIsNoApplyFooter()
    {
        string? applied = null;
        var cut = Render(p => p.Add(x => x.OnBlockStyleChanged, EventCallback.Factory.Create<string>(this, value => applied = value)));
        OpenStyleGallery(cut);

        cut.Find("[data-testid='document-style-quick-heading-2']").Click();

        applied.Should().Be("Heading2");
        cut.FindAll("[data-testid='document-style-gallery']").Should().BeEmpty("the quick style applied and the gallery closed");
        cut.FindAll(".tm-document-floating-panel__footer").Should().BeEmpty();
    }

    [Fact]
    public void StyleGallery_ArrowNavigationFollowsTheSixColumns()
    {
        var cut = Render();
        OpenStyleGallery(cut);

        var tiles = cut.FindAll("[data-testid^='document-style-quick-']");
        tiles[0].GetAttribute("tabindex").Should().Be("0");
        cut.Find("[data-testid='document-style-gallery-quick']").GetAttribute("data-columns").Should().Be("6");
        tiles[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.FindAll("[data-testid^='document-style-quick-']")[1].GetAttribute("tabindex").Should().Be("0");
    }

    // ─── equation ────────────────────────────────────────────────────────────

    [Fact]
    public void Equation_StructuresShowAMathPreview_NotALucideDivideOrRadicalIcon()
    {
        var cut = Render();
        OpenTab(cut, "math");
        cut.Find("[data-testid='document-toolbar-equation']").Click();

        var fraction = cut.Find("[data-testid='document-equation-fraction']");
        fraction.QuerySelector(".tm-document-math__frac").Should().NotBeNull();
        fraction.QuerySelector("svg").Should().BeNull("the preview is HTML/CSS, not an icon");
        cut.Find("[data-testid='document-equation-radical'] .tm-document-math__rad").Should().NotBeNull();
        cut.Find("[data-testid='document-equation-superscript'] sup").Should().NotBeNull();
        cut.Find("[data-testid='document-equation-matrix'] .tm-document-math__matrix").Should().NotBeNull();
        cut.FindAll("[data-testid='document-equation-menu'] .tm-document-palette-grid[data-columns='6']").Should().HaveCount(2);
    }

    [Fact]
    public void Equation_ClickInsertsAtOnce_AndClosesThePanel()
    {
        string? preset = null;
        var cut = Render(p => p.Add(x => x.OnInsertEquation, EventCallback.Factory.Create<string>(this, v => preset = v)));
        OpenTab(cut, "math");
        cut.Find("[data-testid='document-toolbar-equation']").Click();

        cut.Find("[data-testid='document-equation-fraction']").Click();

        preset.Should().Be("fraction");
        cut.FindAll("[data-testid='document-equation-menu']").Should().BeEmpty();
    }

    // ─── symbols ─────────────────────────────────────────────────────────────

    [Fact]
    public void Symbols_AreAnEightColumnGrid_WithSectionHeadings_AndKeepTheirSpecialEntries()
    {
        var cut = Render();
        OpenTab(cut, "insert");
        cut.Find("[data-testid='document-toolbar-symbol']").Click();

        cut.FindAll("[data-testid='document-symbol-menu'] .tm-document-palette-grid[data-columns='8']").Should().HaveCount(2);
        cut.FindAll(".tm-document-palette-grid__heading").Select(h => h.TextContent).Should().Contain(["Special characters", "Emoji"]);
        foreach (var testId in new[] { "non-breaking-space", "optional-hyphen", "em-dash", "en-dash" })
        {
            cut.FindAll($"[data-testid='document-symbol-{testId}']").Should().ContainSingle();
        }
    }

    // ─── table grid ──────────────────────────────────────────────────────────

    [Fact]
    public void TableGrid_OpensInAFloatingPanel_OutsideTheToolbar()
    {
        var cut = Render();
        OpenTab(cut, "insert");
        cut.Find("[data-testid='document-toolbar-table']").Click();

        var picker = cut.Find("[data-testid='document-table-grid-picker']");
        picker.Closest("[data-testid='document-table-grid-panel']").Should().NotBeNull();
        picker.Closest("[data-testid='document-ribbon-toolbar']").Should().BeNull("the panel is rendered after the toolbar, so a collapsed item can still open it");
        cut.Find("[data-testid='document-toolbar-table']").GetAttribute("aria-haspopup").Should().Be("dialog");
    }

    // ─── page layout ─────────────────────────────────────────────────────────

    [Fact]
    public void PageLayout_EveryNumericInputHasAPointsSuffix_AndIsDescribedByIt()
    {
        var cut = Render();
        OpenTab(cut, "layout");
        cut.Find("[data-testid='document-page-layout']").Click();

        foreach (var testId in new[] { "document-margin-top", "document-margin-right", "document-margin-bottom", "document-margin-left", "document-header-distance", "document-footer-distance", "document-column-spacing", "document-line-numbering-distance" })
        {
            var input = cut.Find($"[data-testid='{testId}']");
            var suffix = cut.Find($"[id='{input.GetAttribute("aria-describedby")}']");
            suffix.TextContent.Trim().Should().Be("pt", $"{testId} shows its unit");
            suffix.ClassList.Should().Contain("tm-document-inspector-form__suffix");
        }
    }

    [Fact]
    public void PageLayout_CaptionsThePageSizeAndOrientation_AndTheOnlyFooterIsDone()
    {
        var cut = Render(p => p.Add(x => x.PageSettings, new DocumentPageSettings { Size = DocumentPageSize.A4, Landscape = false }));
        OpenTab(cut, "layout");
        cut.Find("[data-testid='document-page-layout']").Click();

        var caption = cut.Find("[data-testid='document-page-layout-caption']");
        caption.Children[0].TextContent.Should().Be("A4 (210 × 297 mm)");
        caption.Children[1].TextContent.Should().Be("Portrait", "the orientation is its own line - no dangling separator");
        var footer = cut.FindAll(".tm-document-floating-panel__footer button");
        footer.Should().ContainSingle().Which.TextContent.Trim().Should().Be("Done");
    }

    [Fact]
    public void PageLayout_PreviewCarriesTheMarginsAndColumnsAsCssVariables()
    {
        var settings = new DocumentPageSettings { Size = DocumentPageSize.A4, Landscape = false };
        settings.Margins.Left = 144;
        var cut = Render(p => p
            .Add(x => x.PageSettings, settings)
            .Add(x => x.SectionColumns, new DocumentSectionColumns { Count = 2, Spacing = 36 }));
        OpenTab(cut, "layout");
        cut.Find("[data-testid='document-page-layout']").Click();

        var style = cut.Find("[data-testid='document-page-layout-preview']").GetAttribute("style")!;
        style.Should().Contain("--tm-page-preview-margin-left").And.Contain("--tm-page-preview-columns: 2").And.Contain("--tm-page-preview-ratio");
        cut.FindAll(".tm-document-page-layout__preview-column").Should().HaveCount(2);
    }

    [Fact]
    public void PageLayout_InputsKeepTheirLiveOnInputHandlers()
    {
        DocumentPageSettings? changed = null;
        var cut = Render(p => p.Add(x => x.PageSettingsChanged, EventCallback.Factory.Create<DocumentPageSettings>(this, s => changed = s)));
        OpenTab(cut, "layout");
        cut.Find("[data-testid='document-page-layout']").Click();

        cut.Find("[data-testid='document-margin-top']").Input("30");

        changed.Should().NotBeNull("the field relayouts as it is typed");
        changed!.Margins.Top.Should().Be(30);
    }

    // ─── header / footer menus ───────────────────────────────────────────────

    [Fact]
    public void HeaderFooterMenus_AreFloatingPanels_NotRoleMenuPopups()
    {
        var cut = Render(p => p.Add(x => x.ActiveRegion, "Header"));
        OpenTab(cut, "header-footer");
        cut.Find("[data-testid='document-header-footer-insert-field-menu']").Click();

        var panel = cut.Find("[data-testid='document-header-footer-field-menu']");
        panel.GetAttribute("role").Should().Be("dialog");
        panel.QuerySelectorAll("button").Should().HaveCount(7, "six field entries and the X");
        cut.FindAll("[role='menuitem']").Should().BeEmpty("a dialog does not own menu items");

        cut.Find("[data-testid='document-header-footer-preset-menu']").Click();
        cut.FindAll("[data-testid='document-header-footer-field-menu']").Should().BeEmpty("one panel at a time");
        cut.Find("[data-testid='document-header-footer-presets-menu']").Should().NotBeNull();
    }

    // ─── link popover ────────────────────────────────────────────────────────

    [Fact]
    public void Link_IsAPopover_NotAStripUnderTheRibbon_SoThereIsNoLayoutShift()
    {
        var cut = Render();
        cut.Find("[data-testid='document-link']").Click();

        var link = cut.Find("[data-testid='document-link-dialog']");
        link.ClassList.Should().Contain("tm-document-floating-panel");
        link.Closest("[data-testid='document-ribbon-panel']").Should().BeNull();
        cut.FindAll(".tm-document-editor__link-dialog").Should().BeEmpty("the strip under the ribbon is gone");
    }

    [Fact]
    public void Link_FieldsAreUrlInputs_WithPrimarySecondaryAndGhostButtons()
    {
        var cut = Render(p => p.Add(x => x.OnLinkRemoved, EventCallback.Factory.Create(this, () => { })).Add(x => x.LinkInfoProvider, () => Task.FromResult<WysiwygLinkInfo?>(new WysiwygLinkInfo { Href = "https://example.test/a" })));
        cut.Find("[data-testid='document-link']").Click();

        var url = cut.Find("[data-testid='document-link-url']");
        url.GetAttribute("type").Should().Be("url");
        url.GetAttribute("inputmode").Should().Be("url");
        url.GetAttribute("enterkeyhint").Should().Be("done");
        cut.Find("[data-testid='document-apply-link']").ClassList.Should().Contain("tm-btn-primary");
        cut.Find("[data-testid='document-remove-link']").ClassList.Should().Contain("tm-btn-secondary");
        cut.Find("[data-testid='document-link-cancel']").ClassList.Should().Contain("tm-btn-ghost");
    }

    [Fact]
    public void Link_RemoveIsOfferedOnlyForAnExistingLink_AndOnlyWithALinkRemovedHandler()
    {
        var withoutLink = Render(p => p.Add(x => x.OnLinkRemoved, EventCallback.Factory.Create(this, () => { })));
        withoutLink.Find("[data-testid='document-link']").Click();
        withoutLink.FindAll("[data-testid='document-remove-link']").Should().BeEmpty();

        var withoutHandler = Render(p => p.Add(x => x.LinkInfoProvider, () => Task.FromResult<WysiwygLinkInfo?>(new WysiwygLinkInfo { Href = "https://example.test" })));
        withoutHandler.Find("[data-testid='document-link']").Click();
        withoutHandler.FindAll("[data-testid='document-remove-link']").Should().BeEmpty();
    }

    [Fact]
    public void Link_Enter_Submits_AndAnUnsafeUrlIsRejectedInline()
    {
        string? applied = null;
        var cut = Render(p => p.Add(x => x.OnLinkApplied, EventCallback.Factory.Create<WysiwygLinkPayload>(this, payload => applied = payload.Href)));
        cut.Find("[data-testid='document-link']").Click();

        cut.Find("[data-testid='document-link-url']").Input("javascript:alert(1)");
        cut.Find("[data-testid='document-link-error']").TextContent.Should().Contain("safe URL");
        cut.Find("[data-testid='document-link-url']").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find("[data-testid='document-link-url']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        applied.Should().BeNull();

        cut.Find("[data-testid='document-link-url']").Input("https://example.test/ok");
        cut.FindAll("[data-testid='document-link-error']").Should().BeEmpty();
        cut.Find("[data-testid='document-link-url']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        applied.Should().Be("https://example.test/ok");
        cut.FindAll("[data-testid='document-link-dialog']").Should().BeEmpty("applying closes the popover");
    }

    [Fact]
    public async Task Link_Escape_ClosesThePopover_AndGivesFocusBackToTheCanvas()
    {
        var restored = 0;
        var cut = Render(p => p.Add(x => x.OnRestoreFocus, EventCallback.Factory.Create(this, () => restored++)));
        cut.Find("[data-testid='document-link']").Click();

        var core = cut.FindComponents<TmOverlayPanel>().First(panel => panel.Instance.Id.StartsWith("tm-document-floating-", StringComparison.Ordinal));
        await cut.InvokeAsync(() => core.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll("[data-testid='document-link-dialog']").Should().BeEmpty();
        restored.Should().Be(1);
    }

    // ─── no backdrop, a More form for every popup ────────────────────────────

    [Fact]
    public void EveryRibbonPanel_OpensWithoutAClickAwayBackdrop()
    {
        var cut = Render();
        OpenTab(cut, "insert");
        cut.Find("[data-testid='document-toolbar-table']").Click();

        cut.FindAll("[data-testid='document-toolbar-floating-backdrop'], .tm-document-editor__toolbar-floating-backdrop").Should().BeEmpty();
    }

    [Theory]
    [InlineData("home", "document-link")]
    [InlineData("insert", "document-toolbar-table")]
    [InlineData("insert", "document-toolbar-equation")]
    [InlineData("insert", "document-toolbar-symbol")]
    [InlineData("insert", "document-insert-menu")]
    [InlineData("layout", "document-page-layout")]
    public void PopupItems_HaveAMoreForm_SoNoneIsPinnedOnTheBar(string tab, string testId)
    {
        var cut = Render();
        OpenTab(cut, tab);

        var item = cut.Find($"[data-testid='{testId}']").Closest(".tm-toolbar-item")!;
        item.GetAttribute("data-pin").Should().NotBe("never", "an item with a More form can leave the bar");
    }

    [Fact]
    public void ImagePanelAnchor_FallsBackToTheRibbonRow_WhenTheRequestCameFromMore()
    {
        var cut = Render();
        OpenTab(cut, "insert");

        cut.Instance.ImagePanelAlign.Should().Be(OverlayAlign.Start, "a bar click aligns to the start of the button");
    }
}
