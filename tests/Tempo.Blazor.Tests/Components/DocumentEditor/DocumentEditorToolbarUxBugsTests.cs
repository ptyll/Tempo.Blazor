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
    public void ChangeCase_EntriesInMore_FireEveryTime_EvenForTheSameChoice()
    {
        var received = new List<string>();
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ShowAdvancedCharacterFormatting, true)
            .Add(x => x.OnChangeCase, EventCallback.Factory.Create<string>(this, v => received.Add(v))));

        // Change case is a "More formatting" option list now: an entry has no sticky selected state, so the same choice repeats.
        for (var i = 0; i < 2; i++)
        {
            cut.Find("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").Click();
            var group = cut.FindAll("[role='menu'] [role='group']").Single(g => g.GetAttribute("aria-label") == "Change case");
            group.QuerySelectorAll("button").First(b => b.TextContent.Contains("UPPERCASE", StringComparison.OrdinalIgnoreCase)).Click();
            var expected = i + 1;
            cut.WaitForAssertion(() => received.Count.Should().Be(expected));
        }

        received.Should().Equal("uppercase", "uppercase");
    }

    // ─── block style select shows the current style (an id used to leave it blank) ──────────────────────

    [Theory]
    [InlineData("normal", "Normal")]
    [InlineData("heading-1", "Heading1")]
    [InlineData("Heading2", "Heading2")]
    [InlineData("quote", "Quote")]
    [InlineData(null, "Normal")]
    public void BlockStyleSelect_MarksTheOptionOfTheCurrentStyle_WhetherReportedAsIdOrName(string? reported, string expectedOption)
    {
        var cut = Render<TmDocumentEditorToolbar>(p => p
            .Add(x => x.ShowBlockStyle, true)
            .Add(x => x.CurrentBlockStyle, reported));

        var select = cut.Find("[data-testid='document-block-style']");
        select.QuerySelectorAll("option").Where(o => o.HasAttribute("selected") || o.GetAttribute("value") == expectedOption)
            .Should().Contain(o => o.GetAttribute("value") == expectedOption);
        cut.Instance.GetType().GetProperty("SelectedBlockStyleValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(cut.Instance).Should().Be(expectedOption);
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
}
