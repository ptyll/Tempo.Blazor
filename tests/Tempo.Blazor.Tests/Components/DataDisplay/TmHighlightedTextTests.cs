using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 8: <c>TmHighlightedText</c> — the matched part of a combobox row, wrapped in
/// <c>&lt;mark&gt;</c> so a reader sees WHERE the search term hit. These facts pin the matching rules
/// (case-insensitive by default, accent-insensitive so "usti" finds "Ústí", every occurrence, an empty
/// query meaning plain text), the safety rules (a query is literal text, never a regular expression, and
/// the text is never re-interpreted as markup) and the stylesheet contract (tokens only, a forced-colors
/// fallback).
/// </summary>
public class TmHighlightedTextTests : LocalizationTestBase
{
    private IRenderedComponent<TmHighlightedText> RenderText(
        string? text, string? query, bool caseSensitive = false, bool accentInsensitive = true, string? matchClass = null)
        => Render<TmHighlightedText>(p => p
            .Add(c => c.Text, text)
            .Add(c => c.Query, query)
            .Add(c => c.CaseSensitive, caseSensitive)
            .Add(c => c.AccentInsensitive, accentInsensitive)
            .Add(c => c.MatchClass, matchClass));

    private static string[] Marks(IRenderedComponent<TmHighlightedText> cut)
        => cut.FindAll("mark").Select(mark => mark.TextContent).ToArray();

    /// <summary>The rendered text with the wrapper tags removed — what a reader/copy/paste actually gets.</summary>
    private static string PlainText(IRenderedComponent<TmHighlightedText> cut)
        => Regex.Replace(cut.Markup, "<[^>]+>", string.Empty);

    [Fact]
    public void WithoutAQuery_TheTextIsRenderedPlain()
    {
        var cut = RenderText("Praha", null);

        PlainText(cut).Should().Be("Praha");
        cut.FindAll("mark").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyQuery_IsNotAMatchEverythingWildcard(string query)
    {
        var cut = RenderText("Praha a Brno", query);

        cut.FindAll("mark").Should().BeEmpty();
        PlainText(cut).Should().Be("Praha a Brno");
    }

    [Fact]
    public void ASingleMatchIsMarkedAndTheRestStaysPlainText()
    {
        var cut = RenderText("Praha", "rah");

        Marks(cut).Should().Equal("rah");
        PlainText(cut).Should().Be("Praha");
        cut.Find("mark").ClassList.Should().Contain("tm-highlighted-text__match");
    }

    [Fact]
    public void EveryOccurrenceIsMarked()
    {
        var cut = RenderText("Praha, Prachatice, Praga", "pra");

        Marks(cut).Should().Equal("Pra", "Pra", "Pra");
        PlainText(cut).Should().Be("Praha, Prachatice, Praga");
    }

    [Fact]
    public void MatchingIsCaseInsensitiveByDefault_AndKeepsTheOriginalCasing()
    {
        var cut = RenderText("Ústí nad Labem", "úSTÍ");

        Marks(cut).Should().Equal(["Ústí"], "the rendered match is the ORIGINAL text, not the lower-cased query");
    }

    [Fact]
    public void CaseSensitiveMatchingOnlyFindsTheExactCasing()
    {
        var cut = RenderText("Praha PRAHA", "PRA", caseSensitive: true);

        Marks(cut).Should().Equal("PRA");
    }

    [Fact]
    public void MatchingIsAccentInsensitiveByDefault()
    {
        var cut = RenderText("Ústí nad Orlicí", "usti");

        Marks(cut).Should().Equal("Ústí");
    }

    [Fact]
    public void TheReverseDirectionWorksToo_AnAccentedQueryFindsPlainText()
    {
        var cut = RenderText("Usti nad Labem", "Ústí");

        Marks(cut).Should().Equal("Usti");
    }

    [Fact]
    public void AccentInsensitivityCanBeTurnedOff()
    {
        var cut = RenderText("Ústí", "usti", accentInsensitive: false);

        cut.FindAll("mark").Should().BeEmpty();
    }

    [Theory]
    [InlineData("a.c", "a.c")]
    [InlineData("(1+1)", "(1+1)")]
    [InlineData("*", "*")]
    [InlineData("[a-z]", "[a-z]")]
    [InlineData("a|b", "a|b")]
    [InlineData("\\d", "\\d")]
    [InlineData("^$", "^$")]
    [InlineData("a?", "a?")]
    public void AQueryIsLiteralText_NotARegularExpression(string text, string query)
    {
        var cut = RenderText(text, query);

        Marks(cut).Should().Equal([text],
            "a search term is data — a regex metacharacter must match itself instead of throwing or matching everything");
    }

    [Fact]
    public void AMetacharacterQueryDoesNotOverMatch()
    {
        var cut = RenderText("abc adc a.c", "a.c");

        Marks(cut).Should().Equal(["a.c"], "the dot is a literal dot, so abc and adc are not matches");
    }

    [Fact]
    public void TheTextIsNeverReinterpretedAsMarkup()
    {
        var cut = RenderText("<b>bold</b> & \"quoted\"", "bold");

        cut.FindAll("b").Should().BeEmpty();
        cut.Find("mark").TextContent.Should().Be("bold");
        cut.Markup.Should().Contain("&lt;b&gt;");
        cut.Markup.Should().Contain("&amp;");
    }

    [Fact]
    public void OverlappingOccurrencesAreNotDoubleWrapped()
    {
        var cut = RenderText("aaaa", "aa");

        Marks(cut).Should().Equal("aa", "aa");
        PlainText(cut).Should().Be("aaaa", "each character belongs to at most one mark and no character is lost");
    }

    [Fact]
    public void AQueryLongerThanTheTextMatchesNothing()
    {
        var cut = RenderText("abc", "abcdef");

        cut.FindAll("mark").Should().BeEmpty();
        PlainText(cut).Should().Be("abc");
    }

    [Fact]
    public void AMatchClassIsAddedOnTopOfTheDefaultOne()
    {
        var cut = RenderText("Praha", "Pra", matchClass: "host-mark");

        cut.Find("mark").ClassList.Should().Contain("tm-highlighted-text__match");
        cut.Find("mark").ClassList.Should().Contain("host-mark");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoText_RendersNothing(string? text)
    {
        var cut = RenderText(text, "pra");

        cut.Markup.Trim().Should().BeEmpty();
    }

    [Fact]
    public void TheWholeTextSurvivesTheSplit()
    {
        const string text = "Ústí nad Labem — a beautiful city";
        var cut = RenderText(text, "nad");

        PlainText(cut).Should().Be(text);
    }

    [Fact]
    public void TheStylesheetUsesOnlyDesignTokens_AndKeepsAForcedColorsFallback()
    {
        var css = ThemeCss.Normalise(ThemeCss.ComponentCss("_highlighted-text.css"));

        css.Should().NotContain("[data-theme");
        css.Should().Contain("var(--tm-", "the mark reads the theme tokens");
        css.Should().Contain("forced-colors: active",
            "forced colors drops the background the match is drawn with, so the system Highlight pair has to be re-declared");
        css.Should().Contain("Highlight");
    }
}
