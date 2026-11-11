using FluentAssertions;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Plan 7ba95ed6 phase 8 — the waterfall's scroll owner is a CONTAINMENT context, so a clipped
/// rowgroup cannot inflate the page's own scroll height.
/// <para>
/// The grid (<c>.tm-trace-waterfall__grid</c>) is the ONE scroll owner in both axes: it has a
/// <c>max-block-size</c> and <c>overflow: auto</c>, and the rows live inside it. A 152-row trace
/// therefore paints 152 rows' worth of boxes inside a 420 px scrollport. Without a containment rule
/// Chromium reports those clipped boxes in the ROOT scroller too: <c>documentElement.scrollHeight</c>
/// came back as 5024 px against a 1016 px body, and <c>window.scrollTo(0, 4000)</c> stayed at 0 —
/// the void is unreachable by a reader but real for anything that measures the document, which is
/// exactly what a Playwright <c>FullPage</c> screenshot does (it captures the tail as thousands of
/// pixels of blank page).
/// </para>
/// <para>
/// The guard reads the stylesheet, not a layout engine: <c>contain: layout paint</c> on the scroll
/// owner is the declaration that makes the element a containment context, so its overflowing content
/// stops contributing to the viewport's scrollable overflow. The behaviour itself is proven by the
/// browser lane (<c>TraceWaterfallPageE2EBase.FullPageScreenshot_HasNoBlankTail</c>), which measures the
/// two scroll heights in Chromium and takes the shot.
/// </para>
/// </summary>
public sealed class TraceWaterfallContainmentCssTests
{
    private const string PartialFile = "_trace-waterfall.css";
    private const string ScrollOwner = ".tm-trace-waterfall__grid";

    private static readonly Regex Containment =
        new(@"contain\s*:\s*(?<value>[^;]+);", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    [Fact]
    public void ScrollOwner_DeclaresLayoutAndPaintContainment()
    {
        var grid = Block(Source(), ScrollOwner);

        var containment = Containment.Match(grid);
        containment.Success.Should().BeTrue(
            $"{ScrollOwner} must declare a contain rule — without it the clipped rowgroup keeps " +
            "counting toward documentElement.scrollHeight (measured 5024 px vs a 1016 px body)");

        var value = containment.Groups["value"].Value;
        value.Should().Contain("layout",
            "layout containment is what stops the clipped content from propagating its box to the root scroller");
        value.Should().Contain("paint",
            "paint containment clips the overflowing rows to the scroll owner itself");
        value.Should().NotContain("size",
            "size containment would collapse the grid's own box — the grid is sized by its host, not by its rows");
    }

    [Fact]
    public void ScrollOwner_StaysTheSingleOverflowAutoBox()
    {
        var source = Source();
        var grid = Block(source, ScrollOwner);

        grid.Should().Contain("overflow: auto",
            "the grid owns both scroll axes (a second horizontal scroller is how a header and its rows drift)");
        grid.Should().Contain("max-block-size: var(--tm-trace-waterfall-max-height)",
            "the scrollport height is a token-driven knob, not a hardcoded pixel value");

        // The rowgroup is deliberately NOT a scroll container; it only carries the shared grid width.
        var rowgroup = Block(source, ".tm-trace-waterfall__scroll");
        rowgroup.Should().NotContain("overflow",
            "the rowgroup must not become a second scroll container — the header would stop tracking its rows");
    }

    [Fact]
    public void BundledStylesheet_CarriesTheContainmentRule()
    {
        // Consumers link tempo-blazor.bundled.css; a partial edited without rebundling ships nothing.
        var bundled = ThemeCss.BundledCss();
        var grid = Block(bundled, ScrollOwner);

        var containment = Containment.Match(grid);
        containment.Success.Should().BeTrue(
            "the committed bundle must carry the containment rule the partial declares — " +
            "run `dotnet build src/Tempo.Blazor/Tempo.Blazor.csproj -f net10.0 -t:BundleCssFiles`");
        containment.Groups["value"].Value.Should().Contain("layout").And.Contain("paint");
    }

    private static string Source() => ThemeCss.ComponentCss(PartialFile);

    /// <summary>
    /// The declaration body of the FIRST rule whose selector list mentions <paramref name="selector"/>.
    /// Hand-scanned instead of regex-matched: the committed bundle is one huge minified line, and a
    /// <c>[^{}]*</c>-style selector pattern over it backtracks catastrophically.
    /// </summary>
    private static string Block(string css, string selector)
    {
        var search = 0;
        while (true)
        {
            var index = css.IndexOf(selector, search, StringComparison.Ordinal);
            if (index < 0)
                throw new InvalidOperationException($"the stylesheet must declare a rule for {selector}");

            // A selector boundary, not the tail of a longer class name (`__grid-extra`) or of an
            // identifier inside a declaration value.
            var previous = index == 0 ? '\0' : css[index - 1];
            var boundary = index == 0 || char.IsWhiteSpace(previous) || previous is ',' or '{' or '}' or ')';

            var open = boundary ? css.IndexOf('{', index) : -1;
            if (open >= 0)
            {
                var close = css.IndexOf('}', open + 1);
                if (close > open)
                    return css[(open + 1)..close];
            }

            search = index + selector.Length;
        }
    }
}
