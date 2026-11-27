using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Review fixes of redesign plan 146c7e1b phase 4 (DE-1.2): the declared priority applies (no item without a More form except the
/// phase-5 popups), one alignment item, colour / image / style menu forms, the font select never blank, the group split, the
/// resolved quick-action titles, the click-away of the header.
/// </summary>
public class DocumentEditorRibbonReviewFixesTests : LocalizationTestBase
{
    // The popups that move onto TmOverlayPanel in phase 5 and only then get a More form.
    private static readonly string[] Phase5PinnedTestIds =
    [
        "document-toolbar-table", "document-toolbar-equation", "document-toolbar-symbol", "document-page-layout",
        "document-header-footer-insert-field-menu", "document-header-footer-preset-menu",
    ];

    private IRenderedComponent<TmDocumentEditorToolbar> Render(Action<ComponentParameterCollectionBuilder<TmDocumentEditorToolbar>>? configure = null)
        => Render<TmDocumentEditorToolbar>(p =>
        {
            p.Add(x => x.ShowAdvancedCharacterFormatting, true)
             .Add(x => x.ShowBlockStyle, true)
             .Add(x => x.ShowStyleManagement, true)
             .Add(x => x.ShowEquationTools, true)
             .Add(x => x.ShowSymbolTools, true)
             .Add(x => x.ShowDebugTools, true)
             .Add(x => x.ShowPrintPreviewTools, true)
             .Add(x => x.ShowAdvancedFields, true);
            configure?.Invoke(p);
        });

    // ─── the declared priority applies ───────────────────────────────────────

    [Theory]
    [InlineData("home", false)]
    [InlineData("insert", false)]
    [InlineData("math", false)]
    [InlineData("layout", false)]
    [InlineData("references", false)]
    [InlineData("review", false)]
    [InlineData("view", false)]
    [InlineData("header-footer", true)]
    public void NoRibbonItem_IsPinned_ExceptThePhase5Popups(string tab, bool headerFooter)
    {
        var cut = Render(p => p.Add(x => x.ActiveRegion, headerFooter ? "Header" : "Body"));
        cut.Find($"[data-testid='document-ribbon-tab-{tab}']").Click();

        var pinned = cut.FindAll("[data-testid='document-ribbon-toolbar'] [data-pin='never']");
        foreach (var item in pinned)
        {
            var ids = item.QuerySelectorAll("[data-testid]").Select(e => e.GetAttribute("data-testid")!).ToList();
            ids.Should().Contain(id => Phase5PinnedTestIds.Contains(id),
                $"a pinned item on the {tab} tab must be one of the phase-5 popups, found [{string.Join(", ", ids)}]");
        }
    }

    // Only the character toggles and the calls to action are Primary; the controls the design keeps on a 1080 px row (font, size,
    // colours, alignment, lists) are Secondary but sit EARLY in the row, so they leave last among the Secondary commands - the rank
    // order alone keeps Link and Comment on a tablet row while the font and the lists stay on a desktop one.
    [Theory]
    [InlineData("fontFamily")]
    [InlineData("fontSize")]
    [InlineData("textColor")]
    [InlineData("highlightColor")]
    [InlineData("bulletList")]
    [InlineData("numberedList")]
    [InlineData("alignment")]
    public void DesktopRowControls_AreDeclaredSecondary(string command)
        => DocumentEditorBuiltInToolbar.FindItemByCommandName(command)!.Priority.Should().Be(ToolbarItemPriority.Secondary);

    [Theory]
    [InlineData("bold")]
    [InlineData("italic")]
    [InlineData("underline")]
    [InlineData("strikethrough")]
    [InlineData("link")]
    [InlineData("addComment")]
    [InlineData("insertImage")]
    [InlineData("insertTable")]
    public void CharacterTogglesAndCallsToAction_AreDeclaredPrimary(string command)
        => DocumentEditorBuiltInToolbar.FindItemByCommandName(command)!.Priority.Should().Be(ToolbarItemPriority.Primary);

    [Fact]
    public void Home_ParagraphGroup_KeepsListsBeforeSpacingAndIndents_SoTheLastLeaveFirst()
    {
        var cut = Render();
        var order = cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-document-editor__ribbon-group--paragraph [data-testid]")
            .Select(e => e.GetAttribute("data-testid")).Where(id => id is not null && !id.StartsWith("document-align-")).ToList();
        order.IndexOf("document-numbered-list").Should().BeLessThan(order.IndexOf("document-line-spacing"));
        order.IndexOf("document-line-spacing").Should().BeLessThan(order.IndexOf("document-decrease-indent"));
    }

    [Theory]
    [InlineData("clearFormatting")]
    [InlineData("lineSpacing")]
    [InlineData("increaseIndent")]
    [InlineData("decreaseIndent")]
    public void LessFrequentCommands_AreDeclaredSecondary(string command)
        => DocumentEditorBuiltInToolbar.FindItemByCommandName(command)!.Priority.Should().Be(ToolbarItemPriority.Secondary);

    [Fact]
    public void Ribbon_UsesLabelsShrinkBeforeOverflow()
    {
        var module = SetupToolbarModule();
        Render();

        System.Text.Json.JsonSerializer.Serialize(module.Invocations["attach"].First().Arguments[2]).Should().Contain("\"labelsFirst\":true");
    }

    // ─── one alignment item ──────────────────────────────────────────────────

    [Fact]
    public void Alignment_IsOneItem_ShowingTheCurrentAlignmentIcon_NotFourButtons()
    {
        var cut = Render(p => p.Add(x => x.ParagraphAlignment, DocumentTextAlignment.Center));

        cut.FindAll("button[data-testid='document-align-left']").Should().BeEmpty("the four buttons are one item now");
        cut.Find("[data-testid='document-align']").GetAttribute("data-priority").Should().Be("secondary");
        var trigger = cut.Find("[data-testid='document-align'] button");
        trigger.GetAttribute("aria-label").Should().Contain("Center");
        trigger.QuerySelector("svg").Should().NotBeNull();
    }

    [Fact]
    public void Alignment_Menu_HasFourCheckedRadioItems_AndAppliesTheChoice()
    {
        DocumentTextAlignment? applied = null;
        var cut = Render(p => p
            .Add(x => x.ParagraphAlignment, DocumentTextAlignment.Right)
            .Add(x => x.OnParagraphAlignmentChanged, a => applied = a));

        cut.Find("[data-testid='document-align'] button").Click();

        var items = cut.FindAll("[data-testid^='document-align-']").ToList();
        items.Should().HaveCount(4);
        items.Single(i => i.GetAttribute("aria-checked") == "true").GetAttribute("data-testid").Should().Be("document-align-right");
        cut.Find("[data-testid='document-align-center']").Click();

        cut.WaitForAssertion(() => applied.Should().Be(DocumentTextAlignment.Center));
    }

    [Fact]
    public async Task Alignment_InMore_IsOneLabelledSectionOfFourCheckableEntries()
    {
        var cut = Render(p => p.Add(x => x.ParagraphAlignment, DocumentTextAlignment.Justify));
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var section = cut.FindAll("[role='menu'] [role='group']").Single(g => g.GetAttribute("aria-label") == "Alignment");
        var entries = section.QuerySelectorAll("[role='menuitemcheckbox']");
        entries.Should().HaveCount(4);
        entries.Single(e => e.GetAttribute("aria-checked") == "true").TextContent.Should().Contain("Justify");
    }

    // ─── colour pickers: icon + bar trigger, a menu form ─────────────────────

    [Fact]
    public void ColourTriggers_ShowAnIcon_AndNameTheCurrentColour()
    {
        var cut = Render(p => p.Add(x => x.CurrentTextColor, "#dc2626"));

        var trigger = cut.Find("[data-testid='document-font-color-trigger'] .tm-color-picker-trigger");
        trigger.QuerySelector(".tm-color-picker-trigger-glyph svg").Should().NotBeNull("an icon, not a checkerboard swatch");
        trigger.GetAttribute("aria-label").Should().Be("Font color: #dc2626");
        cut.Find("[data-testid='document-highlight-color-trigger'] .tm-color-picker-trigger")
            .GetAttribute("aria-label").Should().StartWith("Highlight");
        cut.FindAll(".tm-color-picker-trigger-bg").Should().BeEmpty("no checkerboard in the ribbon");
    }

    [Fact]
    public async Task Colours_InMore_AreNamedAndChecked_AndApplyTheHex()
    {
        string? applied = null;
        var cut = Render(p => p
            .Add(x => x.CurrentTextColor, "#2563eb")
            .Add(x => x.OnTextColorChanged, c => applied = c));
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var section = cut.FindAll("[role='menu'] [role='group']").Single(g => g.GetAttribute("aria-label") == "Font color");
        section.QuerySelectorAll("[role='menuitemcheckbox']").Should().HaveCount(12);
        section.QuerySelector("[aria-checked='true']")!.TextContent.Should().Contain("Blue");
        section.QuerySelectorAll("[role='menuitemcheckbox']").First(e => e.TextContent.Contains("Red")).Click();
        cut.WaitForAssertion(() => applied.Should().Be("#dc2626"));

        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();
        cut.FindAll("[role='menu'] [role='group']").Single(g => g.GetAttribute("aria-label") == "Highlight")
            .TextContent.Should().Contain("No highlight");
    }

    // ─── image menu form, style gallery ──────────────────────────────────────

    [Fact]
    public async Task Image_InMore_OffersTheThreeSources()
    {
        var cut = Render(p => p.Add(x => x.CanUploadImages, true).Add(x => x.CanBrowseImageAssets, true));
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var section = cut.FindAll("[role='menu'] [role='group']").Single(g => g.GetAttribute("aria-label") == "Image");
        section.QuerySelectorAll("[role^='menuitem']").Select(e => e.TextContent.Trim())
            .Should().HaveCount(3).And.Contain(t => t.Contains("URL")).And.Contain(t => t.Contains("Upload")).And.Contain(t => t.Contains("assets", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StyleGallery_IsOverflowOnly_AndItsPopupLivesOutsideTheToolbar()
    {
        var cut = Render();
        cut.FindAll("button[data-testid='document-style-gallery-toggle']").Should().BeEmpty("an OverflowOnly command never renders on the bar");
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var entry = cut.Find("[role='menu'] [data-testid='document-style-gallery-toggle']");
        entry.Click();

        cut.WaitForAssertion(() =>
        {
            var gallery = cut.Find("[data-testid='document-style-gallery']");
            gallery.Closest("[data-testid='document-ribbon-toolbar']").Should().BeNull("the popup is not inside the (collapsible) toolbar, so it opens from More");
            gallery.Closest("[data-testid='document-ribbon-panel']").Should().NotBeNull();
        });
    }

    // ─── font select never blank ─────────────────────────────────────────────

    [Fact]
    public void FontSelect_UnknownFamily_GetsAnExtraSelectedOption_WithTheFirstToken()
    {
        var cut = Render(p => p
            .Add(x => x.FontFamilies, [new DocumentFontFamily { Key = "arial", DisplayName = "Arial", CssFamily = "Arial, sans-serif" }])
            .Add(x => x.CurrentFontFamily, "\"Inter\", system-ui, sans-serif"));

        var selected = cut.Find("[data-testid='document-font-family']").QuerySelectorAll("option").Single(o => o.HasAttribute("selected"));
        selected.TextContent.Trim().Should().Be("Inter");
        selected.GetAttribute("value").Should().Be("\"Inter\", system-ui, sans-serif");
    }

    [Fact]
    public async Task FontMenu_UnknownFamily_HasACheckedEntry_AndMixedHasNone()
    {
        var cut = Render(p => p.Add(x => x.CurrentFontFamily, "Inter, sans-serif"));
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();
        var group = cut.FindAll("[role='menu'] [role='group']").Single(g => g.GetAttribute("aria-label") == "Font");
        group.QuerySelectorAll("[aria-checked='true']").Should().ContainSingle().Which.TextContent.Should().Contain("Inter");

        var mixed = Render(p => p.Add(x => x.FontFamilyMixed, true));
        mixed.Find("[data-testid='document-font-family']").QuerySelectorAll("option")
            .Single(o => o.HasAttribute("selected")).TextContent.Should().Contain("Mixed");
    }

    // ─── group split, spacing ────────────────────────────────────────────────

    [Fact]
    public void Home_SplitsStyleAndCharacter_IntoTwoGroupsWithADivider()
    {
        var cut = Render();

        var labels = cut.FindAll("[data-testid='document-ribbon-toolbar'] .tm-toolbar-group[aria-label]").Select(g => g.GetAttribute("aria-label")).ToList();
        labels.Should().ContainInOrder("Style", "Character", "Paragraph");
        cut.Find("[data-testid='document-block-style']").Closest(".tm-toolbar-group")!.GetAttribute("aria-label").Should().Be("Style");
        cut.Find("[data-testid='document-bold']").Closest(".tm-toolbar-group")!.GetAttribute("aria-label").Should().Be("Character");
        DocumentEditorBuiltInToolbar.DefaultGroups.Select(g => g.Id).Should().Contain(["style", "character"]);
    }

    [Fact]
    public async Task FontSizeMenu_IsTheShortList()
    {
        var cut = Render(p => p.Add(x => x.CurrentFontSize, "11"));
        await cut.InvokeAsync(() => cut.FindComponent<TmToolbar>().Instance.OnFitChanged(0));
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();

        var group = cut.FindAll("[role='menu'] [role='group']").Single(g => g.GetAttribute("aria-label") == "Size");
        group.QuerySelectorAll("[role^='menuitem']").Select(e => e.TextContent.Trim())
            .Should().Equal("8 pt", "9 pt", "10 pt", "11 pt", "12 pt", "14 pt", "18 pt", "24 pt", "36 pt");
    }

    // ─── quick-action titles from the resolved state ─────────────────────────

    [Fact]
    public void QuickActionTitles_FollowTheResolvedState_NotTheRawHostFlag()
    {
        var registry = new DocumentEditorCommandRegistry();
        registry.Register(new FuncDocumentEditorCommandEntry("undo", affectsData: false, computeEnabled: _ => true, computeValue: _ => null, execute: (_, _) => Task.CompletedTask));
        registry.RefreshAllAsync(new DocumentEditorCommandContext()).GetAwaiter().GetResult();

        var ribbon = Render<TmDocumentEditorToolbar>(p => p.Add(x => x.CommandRegistry, registry).Add(x => x.CanUndo, false));
        var header = Render<TmDocumentEditorHeader>(p => p.Add(x => x.CommandRegistry, registry).Add(x => x.CanUndo, false));

        ribbon.Find("[data-testid='document-undo']").HasAttribute("disabled").Should().BeFalse("the registry enables it");
        ribbon.Find("[data-testid='document-undo']").GetAttribute("title").Should().Be("Undo");
        header.Find("[data-testid='document-undo']").GetAttribute("title").Should().Be("Undo");
    }

    [Fact]
    public void CommandTitleFormat_IsLocalized()
    {
        var loc = Services.GetRequiredService<ITmLocalizer>();
        DocumentQuickActionState.SaveTitle(loc, readOnly: true, isSaving: false).Should().Be("Save: Read-only document");
        loc["TmDocumentEditor_CommandTitleFormat", "A", "B"].Should().Be("A: B");
    }

    // ─── no aria-disabled next to the native disabled ─────────────────────────

    [Fact]
    public void RibbonButtons_UseOnlyTheNativeDisabledIdiom()
    {
        var cut = Render(p => p.Add(x => x.ReadOnly, true));

        cut.Find("[data-testid='document-bold']").HasAttribute("disabled").Should().BeTrue();
        cut.FindAll("[data-testid='document-ribbon-toolbar'] button[aria-disabled]").Should().BeEmpty();
    }

    // ─── a header action closes an open popup ────────────────────────────────

    [Fact]
    public void CloseFloatingUi_ClosesAnOpenRibbonPopup()
    {
        var cut = Render();
        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();
        cut.Find("[data-testid='document-toolbar-equation']").Click();
        cut.FindAll("[data-testid='document-equation-menu']").Should().HaveCount(1);

        cut.InvokeAsync(() => cut.Instance.CloseFloatingUi());

        cut.FindAll("[data-testid='document-equation-menu']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-toolbar-floating-backdrop']").Should().BeEmpty();
    }
}
