using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Registry;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Fáze 18 — drobné UX/markup bugy toolbaru: changeCase select zamrzal na poslední volbě
/// (value="" konstanta → Blazor diff neemituje reset), fontFamily/fontSize double-fire
/// (@oninput + @onchange maskované 150ms dedupem), duplicitní ovládání orientace stránky,
/// overflow menu skrývalo search box při prázdném filtru, mini font-size bez option pro
/// neceločíselné hodnoty, chybějící data-command na Layout tozích.
/// </summary>
public class DocumentEditorToolbarUxBugsTests : LocalizationTestBase
{
    // ─── changeCase reset ────────────────────────────────────────────────────

    [Fact]
    public void ChangeCase_SelectResetsToPlaceholderAfterSelection()
    {
        string? received = null;
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ShowAdvancedCharacterFormatting, true)
            .Add(x => x.OnChangeCase, EventCallback.Factory.Create<string>(this, v => received = v)));

        cut.Find("[data-testid='document-change-case']").Change("uppercase");
        received.Should().Be("uppercase");

        // @key bump po výběru vynutí přegenerování selectu s value="" — druhý výběr TÉŽE volby
        // znovu vystřelí (select nezamrzne na poslední hodnotě).
        cut.Instance.ChangeCaseResetSeqForTests.Should().BeGreaterThan(0,
            "po výběru se select musí přegenerovat na placeholder (value=\"\" je jinak konstanta a diff reset neemituje)");

        received = null;
        cut.Find("[data-testid='document-change-case']").Change("uppercase");
        received.Should().Be("uppercase");
        cut.Instance.ChangeCaseResetSeqForTests.Should().Be(2);
    }

    // ─── fontFamily/fontSize double-fire ─────────────────────────────────────

    [Fact]
    public void FontSelects_HaveOnlyChangeHandler_NoInputHandler()
    {
        var invoked = 0;
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.OnFontSizeChanged, EventCallback.Factory.Create<double>(this, _ => invoked++)));

        var fontSize = cut.Find("[data-testid='document-font-size']");
        // @oninput odstraněn — browser input event už nemá handler (double-fire zdroj).
        var act = () => fontSize.Input("24");
        act.Should().Throw<Bunit.MissingEventHandlerException>("@oninput handler byl odstraněn, zůstává jen @onchange");

        fontSize.Change("24");
        invoked.Should().Be(1, "jedna změna = přesně jedno vyvolání (bez dedup hacku)");

        var fontFamily = cut.Find("[data-testid='document-font-family']");
        var actFamily = () => fontFamily.Input("Georgia");
        actFamily.Should().Throw<Bunit.MissingEventHandlerException>();
    }

    // ─── Orientace stránky ───────────────────────────────────────────────────

    [Fact]
    public void LayoutTab_PageOrientation_HasOnlySegmentedButtons()
    {
        var cut = Render<TmDocumentEditorToolbar>();
        cut.Find("[data-testid='document-ribbon-tab-layout']").Click();
        cut.Find("[data-testid='document-page-layout']").Click();

        cut.FindAll("[data-testid='document-page-orientation']").Should().BeEmpty(
            "duplicitní <select> orientace byl odstraněn — zůstávají segmentová tlačítka");
        cut.Find("[data-testid='document-page-orientation-portrait']").Should().NotBeNull();
        cut.Find("[data-testid='document-page-orientation-landscape']").Should().NotBeNull();
    }

    // ─── Layout toggly data-command ──────────────────────────────────────────

    [Fact]
    public void LayoutTab_HeaderFooterToggles_CarryDataCommand()
    {
        var cut = Render<TmDocumentEditorToolbar>();
        cut.Find("[data-testid='document-ribbon-tab-layout']").Click();

        cut.Find("[data-testid='document-different-first-page']").GetAttribute("data-command")
            .Should().Be("differentFirstPage");
        cut.Find("[data-testid='document-different-odd-even']").GetAttribute("data-command")
            .Should().Be("differentOddEven");
    }

    // ─── Overflow menu ───────────────────────────────────────────────────────

    [Fact]
    public void OverflowMenu_EmptyFilter_KeepsSearchVisible_WithNoResultsMessage()
    {
        var cut = Render<TmDocumentToolbarOverflowMenu>(p => p
            .Add(x => x.IsOverflowing, true)
            .Add(x => x.IsOpen, true)
            .Add(x => x.ShowSearch, true)
            .Add(x => x.SearchQuery, "xyz-nenalezitelné")
            .Add(x => x.Groups, Array.Empty<DocumentToolbarOverflowMenuGroup>())
            .Add(x => x.NoResultsLabel, "No results"));

        cut.Find("[data-testid='document-toolbar-more-search']").Should().NotBeNull(
            "search box musí zůstat viditelný, i když filtr nic nenajde");
        cut.Find("[data-testid='document-toolbar-more-empty']").TextContent.Should().Be("No results");
    }

    [Fact]
    public void OverflowMenu_FilterChange_ResetsActiveIndex()
    {
        var groups = BuildGroups("bold", "italic", "underline");
        var cut = Render<TmDocumentToolbarOverflowMenu>(p => p
            .Add(x => x.IsOverflowing, true)
            .Add(x => x.IsOpen, true)
            .Add(x => x.ShowSearch, true)
            .Add(x => x.SearchQuery, "")
            .Add(x => x.Groups, groups));

        // Fokusem posuneme aktivní index na třetí item.
        cut.FindAll("[role='menuitem']")[2].Focus();
        cut.FindAll("[role='menuitem']")[2].ClassList.Should().Contain("tm-document-editor__overflow-menu-item--active");

        // Změna filtru (jiná množina výsledků) → aktivní index se resetuje na první item.
        cut.Render(p => p
            .Add(x => x.SearchQuery, "b")
            .Add(x => x.Groups, BuildGroups("bold", "italic")));

        cut.FindAll("[role='menuitem']")[0].ClassList.Should().Contain("tm-document-editor__overflow-menu-item--active");
    }

    [Fact]
    public void OverflowMenu_EnterKeySequence_ExecutesExactlyOnce()
    {
        // The menu items are native <button> elements: the browser itself turns
        // Enter into a click (keydown -> click). The keydown handler must NOT
        // also run the command, otherwise one key press executes it twice.
        var executed = new List<string>();
        var parentKeys = new List<string>();
        var cut = Render<TmDocumentToolbarOverflowMenu>(p => p
            .Add(x => x.IsOverflowing, true)
            .Add(x => x.IsOpen, true)
            .Add(x => x.Groups, BuildGroups("bold"))
            .Add(x => x.OnCommandRequested, EventCallback.Factory.Create<string>(this, executed.Add))
            .Add(x => x.OnMenuItemKeyDown, EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.KeyboardEventArgs>(this, args => parentKeys.Add(args.Key))));

        var item = cut.Find("[role='menuitem']");
        // Real browser sequence for Enter on a focused <button>: keydown, then
        // the native click on the same element (bUnit does not synthesize it).
        item.KeyDown("Enter");
        item.Click();

        executed.Should().Equal("bold");

        // Unhandled keys still delegate to the parent (it owns Escape/focus);
        // Enter is delegated too — the parent handler ignores it.
        parentKeys.Should().Equal("Enter");

        item.KeyDown("ArrowDown");
        parentKeys.Should().Equal("Enter", "ArrowDown");
    }

    // ─── Mini toolbar fractional font size ──────────────────────────────────

    [Theory]
    [InlineData("10.5pt", "10.5")]
    [InlineData("11.5", "11.5")]
    public void MiniToolbarFontSizeOptions_IncludeDynamicOptionForFractionalValue(string current, string expected)
    {
        var options = TmDocumentEditor.BuildMiniToolbarFontSizeOptions(current);

        options.Should().Contain(expected, "aktuální neceločíselná velikost musí mít vlastní option, jinak select ukáže prázdný box");
        options.Should().OnlyHaveUniqueItems();
        options.Should().Contain("11").And.Contain("72");
    }

    [Fact]
    public void MiniToolbarFontSizeOptions_StandardValue_HasNoDuplicate()
    {
        var options = TmDocumentEditor.BuildMiniToolbarFontSizeOptions("12");

        options.Count(o => o == "12").Should().Be(1);
        options.Should().HaveCount(15);
    }

    private static DocumentToolbarOverflowMenuGroup[] BuildGroups(params string[] commands) =>
    [
        new DocumentToolbarOverflowMenuGroup(
            "formatting",
            "Formatting",
            commands
                .Select(name => new DocumentToolbarOverflowMenuItem(
                    name,
                    new DocumentToolbarItem { Id = name, CommandName = name },
                    name,
                    IsEnabled: true))
                .ToArray())
    ];
}
