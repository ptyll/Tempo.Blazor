using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public class DocumentEditorToolbarDeclarativeMigrationTests : LocalizationTestBase
{
    [Fact]
    public void BuiltInToolbar_HomeTabMetadataCoversRenderedGroups()
    {
        AssertMetadata(DocumentToolbarTab.Home,
            "save", "undo", "redo",
            "bold", "italic", "underline", "fontFamily", "fontSize", "textColor", "highlightColor", "clearFormatting", "link",
            "alignLeft", "alignCenter", "alignRight", "alignJustify", "lineSpacing", "spacingBefore", "spacingAfter",
            "increaseIndent", "decreaseIndent");
    }

    [Fact]
    public void Toolbar_HomeTabRendersRegistryBackedCommands()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        AssertCommand(cut, "document-save", "save");
        AssertCommand(cut, "document-undo", "undo");
        AssertCommand(cut, "document-redo", "redo");
        AssertCommand(cut, "document-bold", "bold");
        AssertCommand(cut, "document-italic", "italic");
        AssertCommand(cut, "document-underline", "underline");
        AssertCommand(cut, "document-link", "link");
        AssertCommand(cut, "document-clear-formatting", "clearFormatting");
        AssertCommand(cut, "document-align", "alignment");
        AssertCommand(cut, "document-decrease-indent", "decreaseIndent");
        AssertCommand(cut, "document-increase-indent", "increaseIndent");

        cut.Find("[data-testid='document-font-family']").Should().NotBeNull();
        cut.Find("[data-testid='document-font-size']").Should().NotBeNull();
        cut.Find("[data-testid='document-font-color-trigger']").Should().NotBeNull();
        cut.Find("[data-testid='document-highlight-color-trigger']").Should().NotBeNull();
        cut.Find("[data-testid='document-line-spacing']").Should().NotBeNull();

        // Paragraph spacing before/after are less frequent: they live in the core "More" menu (OverflowOnly), never on the bar.
        cut.FindAll("[data-testid='document-spacing-before']").Should().BeEmpty();
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();
        cut.FindAll("[role='menu'] [role='group']").Select(group => group.GetAttribute("aria-label"))
            .Should().Contain(["Before", "After"]);
    }

    [Fact]
    public void Toolbar_FormattingControlsPreserveEditorSelectionOnPointerDown()
    {
        // Nativní selecty (font-family, font-size, change-case) preventDefault mít NESMÍ —
        // rušil by default akci otevírající popup (Fáze 13, DocumentEditorToolbarDropdownFixTests).
        var cut = Render<TmDocumentEditorToolbar>();

        foreach (var testId in new[]
        {
            "document-bold",
            "document-italic",
            "document-underline",
            "document-strikethrough",
            "document-font-color-trigger",
            "document-highlight-color-trigger",
            "document-link",
            "document-clear-formatting"
        })
        {
            cut.Find($"[data-testid='{testId}']").OuterHtml
                .Should()
                .Contain("preventdefault", $"'{testId}' must not let pointer focus collapse the editor selection before the command runs");
        }
    }

    [Fact]
    public void BuiltInToolbar_InsertReviewViewAndHeaderFooterMetadataCoversRenderedCommands()
    {
        AssertMetadata(DocumentToolbarTab.Insert, "insertTable", "insertImage", "insertEquation", "insertSymbol", "insertPageBreak");
        AssertMetadata(DocumentToolbarTab.Math, "mathInsertEquation");
        AssertMetadata(DocumentToolbarTab.Review,
            "trackChanges", "reviewDisplayMode", "addComment", "openComments", "openRevisions",
            "compareDocuments", "protectDocument", "markEditableRegion");
        AssertMetadata(DocumentToolbarTab.View,
            "showRuler", "zoomPageWidth", "showBlocks", "fullscreen", "openPrintPreview", "printDocument", "viewDocumentJson",
            "viewClipboardHtml", "exportPdf", "importDocx", "exportDocx", "openVersions");
        AssertMetadata(DocumentToolbarTab.HeaderFooter,
            "insertPageNumber", "insertPageCount", "insertPageXOfY", "insertDateField", "insertDocumentTitleField",
            "differentFirstPage", "differentOddEven", "closeHeaderFooter");
    }

    [Fact]
    public void Toolbar_InsertTabRendersRegistryBackedCommands()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        AssertCommand(cut, "document-toolbar-table", "insertTable");
        AssertCommand(cut, "document-toolbar-image", "insertImage");
        AssertCommand(cut, "document-insert-page-break", "insertPageBreak");
    }

    [Fact]
    public void Toolbar_InsertTabRendersSymbolPaletteWhenCanvasToolsEnabled()
    {
        string? insertedPreset = null;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowSymbolTools, true)
            .Add(p => p.OnInsertSymbol, EventCallback.Factory.Create<string>(this, value => insertedPreset = value)));

        cut.Find("[data-testid='document-ribbon-tab-insert']").Click();

        AssertCommand(cut, "document-toolbar-symbol", "insertSymbol");
        cut.Find("[data-testid='document-toolbar-symbol']").Click();

        cut.Find("[role='group'][aria-label='Special characters']").Should().NotBeNull();
        cut.Find("[role='group'][aria-label='Emoji']").Should().NotBeNull();
        cut.Find("[data-testid='document-symbol-em-dash']").Click();

        insertedPreset.Should().Be("emDash");
    }

    [Fact]
    public void Toolbar_MathTabRendersEquationGalleryWhenCanvasToolsEnabled()
    {
        string? insertedPreset = null;
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowEquationTools, true)
            .Add(p => p.OnInsertEquation, EventCallback.Factory.Create<string>(this, value => insertedPreset = value)));

        cut.Find("[data-testid='document-ribbon-tab-math']").Click();

        AssertCommand(cut, "document-toolbar-equation", "insertEquation");
        cut.Find("[data-testid='document-toolbar-equation']").Click();

        cut.Find("[role='group'][aria-label='Structures']").Should().NotBeNull();
        cut.Find("[role='group'][aria-label='Symbols']").Should().NotBeNull();
        cut.Find("[data-testid='document-equation-sum']").Click();

        insertedPreset.Should().Be("sum");
    }

    [Fact]
    public void Toolbar_ReviewTabRendersRegistryBackedCommands()
    {
        var cut = Render<TmDocumentEditorToolbar>();

        cut.Find("[data-testid='document-ribbon-tab-review']").Click();

        AssertCommand(cut, "document-track-changes", "trackChanges");
        AssertCommand(cut, "document-add-comment", "addComment");
        AssertCommand(cut, "document-open-comments", "openComments");
        AssertCommand(cut, "document-open-revisions", "openRevisions");
        AssertCommand(cut, "document-compare-open", "compareDocuments");
        AssertCommand(cut, "document-protect-document", "protectDocument");
        AssertCommand(cut, "document-mark-editable-region", "markEditableRegion");
        var reviewMode = cut.Find("[data-testid='document-review-display-mode']");
        reviewMode.Should().NotBeNull();
        reviewMode.TextContent.Should().Contain("Original");
    }

    [Fact]
    public void Toolbar_ViewTabRendersRegistryBackedCommands()
    {
        var cut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ShowDebugTools, true)
            .Add(p => p.ShowPrintPreviewTools, true)
            .Add(p => p.CanExportPdf, true)
            .Add(p => p.CanExportDocx, true));

        cut.Find("[data-testid='document-ribbon-tab-view']").Click();

        AssertCommand(cut, "document-toggle-ruler", "showRuler");
        AssertCommand(cut, "document-zoom-page-width", "zoomPageWidth");
        AssertCommand(cut, "document-show-blocks", "showBlocks");
        AssertCommand(cut, "document-fullscreen", "fullscreen");
        AssertCommand(cut, "document-open-print-preview", "openPrintPreview");
        AssertCommand(cut, "document-print-document", "printDocument");
        AssertCommand(cut, "document-open-versions", "openVersions");

        // The debug tools are OverflowOnly: no bar button (the old siblings overlapped them), a "More" entry that owns the test id.
        cut.FindAll("button[data-testid='document-view-json']").Should().BeEmpty();
        cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();
        cut.Find("[role='menu'] [data-testid='document-view-json']").Should().NotBeNull();
        cut.Find("[role='menu'] [data-testid='document-view-clipboard-html']").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_HeaderFooterContextualTabRendersOnlyInHeaderFooterMode()
    {
        var bodyCut = Render<TmDocumentEditorToolbar>();
        bodyCut.FindAll("[data-testid='document-ribbon-tab-header-footer']").Should().BeEmpty();

        var headerCut = Render<TmDocumentEditorToolbar>(parameters => parameters
            .Add(p => p.ActiveRegion, "Header"));

        headerCut.Find("[data-testid='document-ribbon-tab-header-footer']")
            .GetAttribute("aria-selected").Should().Be("true");
        AssertCommand(headerCut, "document-header-footer-insert-page-number", "insertPageNumber");
        AssertCommand(headerCut, "document-header-footer-insert-page-count", "insertPageCount");
        AssertCommand(headerCut, "document-header-footer-insert-page-x-of-y", "insertPageXOfY");
        AssertCommand(headerCut, "document-header-footer-insert-date", "insertDateField");
        AssertCommand(headerCut, "document-header-footer-insert-title", "insertDocumentTitleField");
        AssertCommand(headerCut, "document-header-footer-different-first-page", "differentFirstPage");
        AssertCommand(headerCut, "document-header-footer-different-odd-even", "differentOddEven");
        AssertCommand(headerCut, "document-close-header-footer", "closeHeaderFooter");
    }

    [Fact]
    public void BuiltInToolbar_HeaderFooterItemsRespectContextVisibility()
    {
        var items = DocumentEditorBuiltInToolbar.DefaultItems
            .Where(item => item.Tab == DocumentToolbarTab.HeaderFooter)
            .ToList();

        items.Should().OnlyContain(item =>
            !item.IsVisible(new DocumentToolbarVisibilityContext { IsHeaderFooterMode = false }));
        items.Should().OnlyContain(item =>
            item.IsVisible(new DocumentToolbarVisibilityContext { IsHeaderFooterMode = true }));
    }

    private static void AssertMetadata(DocumentToolbarTab tab, params string[] itemIds)
    {
        var metadata = DocumentEditorBuiltInToolbar.DefaultItems
            .Where(item => item.Tab == tab)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var itemId in itemIds)
        {
            metadata.Should().Contain(itemId);
        }
    }

    private static void AssertCommand(IRenderedComponent<TmDocumentEditorToolbar> cut, string testId, string commandName)
    {
        var element = cut.Find($"[data-testid='{testId}']");
        element.GetAttribute("data-command").Should().Be(commandName);
        DocumentEditorBuiltInToolbar.FindItemByCommandName(commandName)
            .Should().NotBeNull($"'{commandName}' must be present in built-in toolbar metadata");
    }
}
