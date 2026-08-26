using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards that a user can SEE which column a <c>TmDataTable</c> is sorted by — WCAG 2.2 SC 1.4.11 asks
/// for 3:1 both between the two states of a control and between the control and its background.
/// <para>
/// Measured on 2.8.21, both themes: the difference between a sorted and an unsorted column was
/// <b>1,32:1</b> light and <b>1,22:1</b> dark. The mechanism found in the library — not in the register
/// — is a pair of dead rules, and neither of the two hypotheses that had been written down was the
/// whole story:
/// </para>
/// <list type="number">
/// <item>
/// <c>.tm-col-sorted-asc { color: var(--tm-color-primary-text) }</c> is (0,1,0) and
/// <c>.tm-data-table th { color: var(--tm-text-secondary) }</c> is (0,1,1). The header colour that
/// announces the sort therefore NEVER applies, at any source order. The sorted state contributed
/// nothing at all.
/// </item>
/// <item>
/// <c>opacity: 1</c> on <c>.tm-sort-icon.tm-sort-asc::after</c> was inert, because nested opacity
/// multiplies: 1 inside a box at <c>opacity: .4</c> still paints at 0.4. The icon painted at 0.4 in
/// BOTH states, so the "0,30 → 0,40" in the register is not what the CSS does either.
/// </item>
/// </list>
/// <para>
/// What the probe of Fáze 14 actually caught was the <c>:hover</c>/<c>:focus-visible</c> colour of the
/// header it had just clicked: composite <c>--tm-text-secondary</c> at 0.4 over the header background
/// and you get exactly <c>rgb(179,184,190)</c> light and <c>rgb(90,99,115)</c> dark; composite
/// <c>--tm-text-primary</c> at the same 0.4 and you get exactly <c>rgb(156,160,166)</c> and
/// <c>rgb(105,112,125)</c>. Both recorded pairs reproduce to the pixel, and the second one is a
/// pointer state that disappears the moment the mouse leaves. <see cref="TheTwoRecordedReadings"/>
/// pins that arithmetic so the finding cannot be lost again.
/// </para>
/// <para>
/// The fix therefore does not tune an opacity. It gives the indicator two explicit colours, one per
/// state, on the SPAN itself — where nothing the header declares can reach it, because a directly
/// matching rule always beats an inherited value.
/// </para>
/// </summary>
public class SortIndicatorContrastTests
{
    /// <summary>WCAG 2.2 SC 1.4.11 — 3:1 for a non-text user-interface component.</summary>
    private const double NonTextMinimum = 3.0;

    /// <summary>The background the header paints itself with, and therefore the icon's neighbour.</summary>
    private const string HeaderBackground = "var(--tm-bg-surface-secondary)";

    private static IReadOnlyList<CssCascade.Element> Icon(string stateClass) =>
    [
        new("table", "tm-data-table"),
        new("thead"),
        new("tr"),
        stateClass == "tm-sort-none"
            ? new CssCascade.Element("th", "tm-col-sortable")
            : new CssCascade.Element("th", "tm-col-sortable", "tm-col-sorted-asc"),
        new("span", "tm-sort-icon", stateClass),
    ];

    private static string IconColour(string stateClass, bool dark) =>
        ThemeCss.ResolveColour(
            CssCascade.Winning(ThemeCss.BundledCss(), Icon(stateClass), "color"),
            ThemeCss.TokenGraph(dark));

    /// <summary>
    /// Both states in both themes. Named cases rather than booleans, because the failure message of a
    /// theory is the only thing a reader of a red run gets.
    /// </summary>
    public static TheoryData<string, bool> States() => new()
    {
        { "tm-sort-none", false },
        { "tm-sort-asc", false },
        { "tm-sort-desc", false },
        { "tm-sort-none", true },
        { "tm-sort-asc", true },
        { "tm-sort-desc", true },
    };

    // ── The state change has to be perceivable ────────────────────

    [Theory]
    [InlineData("tm-sort-asc", false)]
    [InlineData("tm-sort-desc", false)]
    [InlineData("tm-sort-asc", true)]
    [InlineData("tm-sort-desc", true)]
    public void SortedAndUnsorted_DifferAboveTheNonTextThreshold(string sortedState, bool dark)
    {
        var sorted = IconColour(sortedState, dark);
        var unsorted = IconColour("tm-sort-none", dark);

        ThemeCss.Contrast(sorted, unsorted).Should().BeGreaterThanOrEqualTo(
            NonTextMinimum,
            "podle čeho je tabulka seřazená se musí dát POZNAT: {0} proti {1} v {2} motivu",
            sorted, unsorted, dark ? "tmavém" : "světlém");
    }

    // ── …and the indicator itself has to be visible at all ────────

    [Theory]
    [MemberData(nameof(States))]
    public void TheIndicator_MeetsNonTextContrast_AgainstTheHeaderBackground(string stateClass, bool dark)
    {
        var colour = IconColour(stateClass, dark);

        ThemeCss.Ratio(colour, HeaderBackground, dark).Should().BeGreaterThanOrEqualTo(
            NonTextMinimum,
            "ikona {0} v {1} motivu je netextový prvek rozhraní", stateClass, dark ? "tmavém" : "světlém");
    }

    /// <summary>
    /// The glyph must reach the screen at full strength — measured as the PRODUCT of every opacity from
    /// the table down to the <c>::after</c> that draws the arrow, not as "no rule mentioning tm-sort-*
    /// declares opacity".
    /// </summary>
    /// <remarks>
    /// The earlier version of this guard read only <c>_data-table.css</c> rules whose selector contained
    /// <c>tm-sort-</c>, and a mutation adding <c>.tm-data-table thead th { opacity: .4 }</c> — which
    /// restores the ORIGINAL defect one level up — stayed green. Nested opacity multiplies, so the
    /// population of a guard about it is the whole ancestor chain and nothing less.
    /// </remarks>
    [Theory]
    [MemberData(nameof(States))]
    public void TheIndicator_ReachesTheScreenAtFullOpacity(string stateClass, bool dark)
    {
        _ = dark; // opacity does not depend on the token graph; the theme is here to keep the case list one list.

        CssCascade.EffectiveOpacity(ThemeCss.BundledCss(), Icon(stateClass)).Should().Be(
            1.0,
            "součin průhledností celého řetězce musí být 1 — jinak výsledná barva ikony není v CSS " +
            "napsaná nikde a každý, kdo ji chce znát, ji musí dopočítat");

        var glyph = Icon(stateClass).ToList();
        glyph[^1] = glyph[^1].With("after");
        CssCascade.EffectiveOpacity(ThemeCss.BundledCss(), glyph).Should().Be(
            1.0,
            "šipku maluje ::after, takže i ta má vlastní box s vlastní opacitou");
    }

    /// <summary>
    /// The negative control for the guard above, and the reason it was rewritten: the defect this
    /// release removed has to be VISIBLE to the reader that claims it is gone — including when it is
    /// reintroduced on an ancestor rather than on the icon.
    /// </summary>
    [Theory]
    [InlineData(".tm-sort-icon{opacity:0.4;}", 0.4)]
    [InlineData(".tm-data-table thead th{opacity:0.4;}", 0.4)]
    [InlineData(".tm-data-table{opacity:0.5;}", 0.5)]
    [InlineData(".tm-sort-icon.tm-sort-asc::after{opacity:0.25;}", 0.25)]
    public void TheOpacityReader_SeesATransparencyWhereverItIsReintroduced(string mutation, double expected)
    {
        var glyph = Icon("tm-sort-asc").ToList();
        glyph[^1] = glyph[^1].With("after");

        CssCascade.EffectiveOpacity(ThemeCss.BundledCss() + mutation, glyph).Should().Be(expected);
    }

    /// <summary>
    /// The sorted header's LABEL was the other dead rule. It is asserted separately from the icon
    /// because it is a separate promise: text colour, not the indicator.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheSortedHeaderLabel_BeatsTheGenericHeaderColour(bool dark)
    {
        IReadOnlyList<CssCascade.Element> header =
        [
            new("table", "tm-data-table"),
            new("thead"),
            new("tr"),
            new("th", "tm-col-sortable", "tm-col-sorted-asc"),
        ];

        var winner = CssCascade.Resolve(ThemeCss.BundledCss(), header, "color");

        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be(
            "var(--tm-color-primary-text)",
            ".tm-col-sorted-asc (0,1,0) prohrávalo s .tm-data-table th (0,1,1) při každém pořadí");

        // The label is text, so it answers to 4.5:1, not to the non-text 3:1.
        ThemeCss.Ratio(winner.Value!, HeaderBackground, dark).Should().BeGreaterThanOrEqualTo(4.5);
    }

    // ── The measuring method itself ───────────────────────────────

    /// <summary>
    /// One compositing function, fed the declarations of 2.8.21, reproduces every number the two
    /// reviews of Fáze 14 disagreed about. There was no methodological difference and no difference of
    /// reference background: the UX review read the icon of a header AT REST (1,91:1 light, 2,95:1
    /// dark), the application probe read the icon of the header it had just clicked, which is a
    /// <c>:hover</c>/<c>:focus-visible</c> state (2,51:1 and 3,58:1 — recorded as 2,52 and 3,58). The
    /// difference is the STATE OF THE ELEMENT, not the method — and neither reading is "the sorted
    /// state", which had no colour of its own at all.
    /// </summary>
    [Theory]
    [InlineData(false, "var(--tm-text-secondary)", "#b3b8be", 1.9108)]
    [InlineData(false, "var(--tm-text-primary)", "#9ca0a6", 2.5142)]
    [InlineData(true, "var(--tm-text-secondary)", "#5a6373", 2.9474)]
    [InlineData(true, "var(--tm-text-primary)", "#69707d", 3.5828)]
    public void TheTwoRecordedReadings(bool dark, string colour, string expectedPixels, double expectedRatio)
    {
        var tokens = ThemeCss.TokenGraph(dark);
        var background = ThemeCss.ResolveColour(HeaderBackground, tokens);

        // 0.4 is what `.tm-sort-icon { opacity: .4 }` painted at in BOTH states; the `opacity: 1` on the
        // pseudo-element multiplied with it instead of replacing it.
        var painted = ThemeCss.Composite(ThemeCss.ResolveColour(colour, tokens), 0.4, background);

        painted.Should().Be(
            expectedPixels,
            "naměřený pixel se musí dát zopakovat ze zdrojů, ne jen opsat z recenze");
        ThemeCss.Contrast(painted, background).Should().BeApproximately(
            expectedRatio,
            0.0001,
            "jedna sonda, jeden způsob skládání — rozdíl mezi recenzí a sondou byl v tom, KTERÝ stav " +
            "která z nich četla, ne v referenčním pozadí");
    }

    /// <summary>
    /// The correction itself, asserted rather than described: over the declarations of 2.8.21 the two
    /// RESTING states resolved to the same colour, so the difference a user could see was 1,00:1 — not
    /// the 1,32:1 / 1,22:1 the register carried, which is idle against hovered. Written as a guard on
    /// the ARITHMETIC (the reader is fed the old declarations explicitly), because the shipped CSS no
    /// longer contains the defect and a guard over it would be measuring the fix, not the claim.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRestingStatesOf2821_WereIndistinguishable(bool dark)
    {
        var tokens = ThemeCss.TokenGraph(dark);
        var background = ThemeCss.ResolveColour(HeaderBackground, tokens);

        // At rest BOTH states took `.tm-data-table th { color: var(--tm-text-secondary) }`: the sorted
        // rule was (0,1,0) against its (0,1,1) and never applied. Same colour, same 0.4 opacity.
        var unsorted = ThemeCss.Composite(ThemeCss.ResolveColour("var(--tm-text-secondary)", tokens), 0.4, background);
        var sorted = ThemeCss.Composite(ThemeCss.ResolveColour("var(--tm-text-secondary)", tokens), 0.4, background);

        sorted.Should().Be(unsorted);
        ThemeCss.Contrast(sorted, unsorted).Should().Be(
            1.0,
            "v klidovém stavu nebyl rozdíl žádný; 1,32:1 a 1,22:1 z registru je idle proti hoveru");

        // And the hovered reading the register mistook for it, reproduced from the rule that did apply.
        var hovered = ThemeCss.Composite(ThemeCss.ResolveColour("var(--tm-text-primary)", tokens), 0.4, background);
        ThemeCss.Contrast(hovered, unsorted).Should().BeApproximately(dark ? 1.2156 : 1.3158, 0.0002);
    }

    /// <summary>
    /// Negative control for the compositing function: at alpha 1 it must return the foreground
    /// untouched, at alpha 0 the background. A mixer that quietly ignores alpha would make every number
    /// above reproduce for the wrong reason.
    /// </summary>
    [Fact]
    public void Compositing_IsNotAConstantFunction()
    {
        ThemeCss.Composite("#112233", 1.0, "#ffffff").Should().Be("#112233");
        ThemeCss.Composite("#112233", 0.0, "#ffffff").Should().Be("#ffffff");
        ThemeCss.Composite("#000000", 0.5, "#ffffff").Should().Be("#808080");
    }

    /// <summary>
    /// Negative control for the cascade reader on THIS element: reintroducing the beaten rule has to
    /// change the answer, otherwise the guards above would be green over a reader that always says the
    /// same thing.
    /// </summary>
    [Fact]
    public void TheCascadeReader_SeesAHeaderRuleThatOutranksTheIndicator()
    {
        var mutated = ThemeCss.BundledCss()
                      + ".tm-data-table th .tm-sort-icon{color:var(--tm-text-disabled);}";

        var winner = CssCascade.Resolve(mutated, Icon("tm-sort-asc"), "color");

        winner.Source.Should().Be(".tm-data-table th .tm-sort-icon");
        winner.Value.Should().Be("var(--tm-text-disabled)");
    }

}
