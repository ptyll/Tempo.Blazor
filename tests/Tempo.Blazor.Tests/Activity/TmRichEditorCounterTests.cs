using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// KG-008 (plan 92e96b25, phase 11): the <c>TmRichEditorFull</c> / <c>TmRichEditorSimple</c> footer
/// counters were hardcoded English ("5 words", "12 / 200 characters") — a "No Hardcoded Text"
/// violation the audit ca474f34 caught together with their under-AA paint. They now render through
/// <c>Loc.Plural</c> families (words) and localized templates (characters), in en + cs here and
/// sk + fr via <c>TmResourceCompletenessTests</c>.
/// </summary>
public class TmRichEditorCounterTests : LocalizationTestBase
{
    [Fact]
    public async Task FullEditor_WordCount_Uses_LocalizedPluralFamily()
    {
        var cut = Render<TmRichEditorFull>(p => p
            .Add(c => c.ShowWordCount, true)
            .Add(c => c.MaxLength, 0));

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>one two three four five</p>"));

        cut.Find(".tm-rte-word-count").TextContent.Trim().Should().Be("5 words");
    }

    [Fact]
    public async Task FullEditor_WordCount_One_Uses_TheSingularForm()
    {
        var cut = Render<TmRichEditorFull>(p => p
            .Add(c => c.ShowWordCount, true)
            .Add(c => c.MaxLength, 0));

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>only</p>"));

        cut.Find(".tm-rte-word-count").TextContent.Trim().Should().Be("1 word");
    }

    [Fact]
    public async Task FullEditor_CharCount_Uses_LocalizedTemplate_With_TheLimit()
    {
        var cut = Render<TmRichEditorFull>(p => p
            .Add(c => c.ShowWordCount, false)
            .Add(c => c.MaxLength, 200));

        // UpdateCounts strips tags to a SPACE, so "<p>abcd</p>" counts 6 — the assertion pins
        // the localized TEMPLATE ("{0} / {1} characters"), not the counting arithmetic.
        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>abcd</p>"));

        cut.Find(".tm-rte-char-count").TextContent.Trim().Should().Be("6 / 200 characters");
    }

    [Fact]
    public async Task FullEditor_CharCount_Over_TheLimit_Keeps_TheOverClass()
    {
        var cut = Render<TmRichEditorFull>(p => p
            .Add(c => c.ShowWordCount, false)
            .Add(c => c.MaxLength, 3));

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>abcd</p>"));

        cut.Find(".tm-rte-char-count").ClassList.Should().Contain("tm-rte-char-count-over");
        cut.Find(".tm-rte-char-count").TextContent.Trim().Should().Be("6 / 3 characters");
    }

    [Fact]
    public async Task SimpleEditor_CharCount_Uses_TheLocalizedShortTemplate()
    {
        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.MaxLength, 500));

        // The SIMPLE editor strips tags to "" (not to a space like the full editor), so 5.
        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>hello</p>"));

        cut.Find(".tm-rte-char-count").TextContent.Trim().Should().Be("5 / 500");
    }

    [Fact]
    public async Task SimpleEditor_CharCount_Speaks_ThePluralFamily_Once_And_Hides_TheFigure()
    {
        // KG-008 review nit: the bare "5 / 500" figure carried no words for a screen reader. The
        // visible figure is now aria-hidden and a .tm-sr-only span speaks the localized plural
        // family — the reader hears "5 / 500 characters" exactly once (the TmChangeDiff pattern).
        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.MaxLength, 500));

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>hello</p>"));

        cut.Find(".tm-rte-char-count").GetAttribute("aria-hidden").Should().Be("true");
        cut.Find(".tm-rte-footer .tm-sr-only").TextContent.Trim().Should().Be("5 / 500 characters");
    }

    [Fact]
    public async Task SimpleEditor_CharCount_Czech_SrOnly_Uses_TheCzechPluralForms()
    {
        UseCzechLocalization();
        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.MaxLength, 500));

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>ab</p>"));
        cut.Find(".tm-rte-footer .tm-sr-only").TextContent.Trim().Should().Be("2 / 500 znaky");

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>abcde</p>"));
        cut.Find(".tm-rte-footer .tm-sr-only").TextContent.Trim().Should().Be("5 / 500 znaků");
    }

    [Fact]
    public async Task FullEditor_WordCount_Czech_Uses_TheCzechPluralForms()
    {
        UseCzechLocalization();
        var cut = Render<TmRichEditorFull>(p => p
            .Add(c => c.ShowWordCount, true)
            .Add(c => c.MaxLength, 0));

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>a</p>"));
        cut.Find(".tm-rte-word-count").TextContent.Trim().Should().Be("1 slovo");

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>a b</p>"));
        cut.Find(".tm-rte-word-count").TextContent.Trim().Should().Be("2 slova");

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>a b c d e</p>"));
        cut.Find(".tm-rte-word-count").TextContent.Trim().Should().Be("5 slov");
    }

    [Fact]
    public async Task FullEditor_CharCount_Czech_Uses_TheCzechPluralForms()
    {
        UseCzechLocalization();
        var cut = Render<TmRichEditorFull>(p => p
            .Add(c => c.ShowWordCount, false)
            .Add(c => c.MaxLength, 200));

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>ab</p>"));
        cut.Find(".tm-rte-char-count").TextContent.Trim().Should().Be("4 / 200 znaky");

        await cut.InvokeAsync(() => cut.Instance.NotifyContentChangedForTest("<p>abcde</p>"));
        cut.Find(".tm-rte-char-count").TextContent.Trim().Should().Be("7 / 200 znaků");
    }
}
