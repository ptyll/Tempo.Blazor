using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the boundary that <c>_pivot-table.css</c> crossed: a component stylesheet may style ITS OWN
/// buttons, never the library-wide <c>.tm-btn</c>.
/// <para>
/// Until 2.8.22 the pivot table declared a second, GLOBAL <c>.tm-btn</c> with its own <c>gap</c>,
/// <c>padding</c>, <c>font-size</c> and the shorthand <c>border: 1px solid transparent</c>, and the
/// manifest imports it after <c>_button.css</c>. Both selectors are (0,1,0), so source order decided —
/// and every outline variant lost its border-colour to a shorthand written for a different component.
/// The token fix of 2.8.17 (<c>--tm-border-color</c> → <c>--tm-border-color-control</c>, 1,24:1 →
/// 4,83:1) was therefore delivered to a property that a few hundred lines later was overwritten back to
/// transparent: consumers of 2.8.16 AND 2.8.21 measured <c>border-color: rgba(0,0,0,0)</c> on
/// <c>ButtonVariant.OutlineSecondary</c> in both themes.
/// </para>
/// <para>
/// The guards below are deliberately of two kinds. The first is STRUCTURAL — no component stylesheet
/// other than <c>_button.css</c> may own the global button classes — and it catches the next
/// redefinition wherever it is written. The second is a CASCADE reading of the shipped bundle, which
/// is the only text where the cross-file order exists at all; it would still fail if someone
/// reintroduced the override under a different file name.
/// </para>
/// </summary>
public class PivotButtonScopeTests
{
    /// <summary>WCAG 2.2 SC 1.4.11 — a user-interface component needs 3:1 against what is next to it.</summary>
    private const double NonTextMinimum = 3.0;

    /// <summary>The classes a plain <c>ButtonVariant.OutlineSecondary</c> renders with.</summary>
    private static readonly IReadOnlyList<CssCascade.Element> OutlineSecondaryButton =
    [
        new("button", "tm-btn", "tm-btn-md", "tm-btn-outline-secondary"),
    ];

    /// <summary>
    /// Every component stylesheet of the core package. The sweep is over the DIRECTORY, not over a list
    /// written here: a guard that names the files it checks cannot see the file somebody adds tomorrow.
    /// </summary>
    public static IReadOnlyList<string> ComponentStylesheetNames() =>
        [.. Directory.EnumerateFiles(ThemeCss.CssPath("components"), "*.css")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];

    public static TheoryData<string> ComponentStylesheets()
    {
        var data = new TheoryData<string>();
        foreach (var name in ComponentStylesheetNames())
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>
    /// The class names <c>_button.css</c> owns. A rule elsewhere whose selector is exactly one of these
    /// — no ancestor, no state — is a second definition of a shared component, which is the defect.
    /// </summary>
    private static bool IsGlobalButtonSelector(string selectorPart) =>
        selectorPart.StartsWith(".tm-btn", StringComparison.Ordinal)
        && !selectorPart.Contains(' ', StringComparison.Ordinal)
        && !selectorPart.Contains(':', StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(ComponentStylesheets))]
    public void OnlyTheButtonStylesheet_OwnsTheGlobalButtonClasses(string stylesheet)
    {
        if (string.Equals(stylesheet, "_button.css", StringComparison.Ordinal))
        {
            return;
        }

        var owned = ThemeCss.Rules(stylesheet)
            .SelectMany(rule => ThemeCss.SelectorParts(rule.Selector))
            .Where(IsGlobalButtonSelector)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        owned.Should().BeEmpty(
            "{0} smí stylovat VLASTNÍ tlačítka; přebít globální .tm-btn znamená, že o vzhledu tlačítek " +
            "aplikace rozhoduje pořadí importů v manifestu, ne varianta, kterou si vývojář zvolil",
            stylesheet);
    }

    /// <summary>
    /// The pivot panel keeps its compact button — under its own name. Losing the block entirely would
    /// also make this file's first guard pass, and that would be a regression sold as a fix.
    /// </summary>
    [Fact]
    public void ThePivotPanel_KeepsItsOwnCompactButton()
    {
        var parts = ThemeCss.Rules("_pivot-table.css")
            .SelectMany(rule => ThemeCss.SelectorParts(rule.Selector))
            .ToList();

        parts.Should().Contain(".tm-pivot-btn");
        parts.Should().Contain(".tm-pivot-btn--sm");
        parts.Should().Contain(".tm-pivot-btn--secondary");
        parts.Should().Contain(".tm-pivot-btn--ghost");
    }

    // ── The cascade in the file that actually ships ───────────────

    [Fact]
    public void OutlineSecondary_KeepsItsBorderColour_InTheShippedBundle()
    {
        var winner = CssCascade.Resolve(ThemeCss.BundledCss(), OutlineSecondaryButton, "border-color");

        winner.Unmodelled.Should().BeEmpty();
        winner.Source.Should().Be(
            ".tm-btn-outline-secondary",
            "vítězem musí být pravidlo varianty, ne zkratka `border` jiné komponenty");
        winner.Value.Should().Be("var(--tm-border-color-control)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutlineSecondaryBoundary_MeetsNonTextContrast(bool dark)
    {
        var css = ThemeCss.BundledCss();
        var border = CssCascade.Winning(css, OutlineSecondaryButton, "border-color");
        var surface = ThemeCss.ResolveColour("var(--tm-bg-surface)", ThemeCss.TokenGraph(dark));

        ThemeCss.Ratio(border, "var(--tm-bg-surface)", dark).Should().BeGreaterThanOrEqualTo(
            NonTextMinimum,
            "hranice je jediná grafika, kterou obrysové tlačítko má; pod 3:1 proti {0} je to jen text",
            surface);
    }

    /// <summary>
    /// The width matters as much as the colour: a border painted in a readable tone at zero width is
    /// still nothing. Asserted from the same cascade so a shorthand cannot quietly zero it.
    /// </summary>
    [Fact]
    public void OutlineSecondaryBoundary_HasNonZeroWidth()
        => CssCascade.Winning(ThemeCss.BundledCss(), OutlineSecondaryButton, "border-width")
            .Should().Be("1px");

    // ── The rest of what the duplicate was overwriting ────────────

    [Theory]
    [InlineData("gap", "var(--tm-space-2)")]
    [InlineData("font-family", "var(--tm-font-sans)")]
    public void TheGlobalButton_KeepsItsOwnMetrics(string property, string expected)
        => CssCascade.Winning(ThemeCss.BundledCss(), OutlineSecondaryButton, property)
            .Should().Be(
                expected,
                "duplicitní .tm-btn přepisovala {0} KAŽDÉMU tlačítku Tempa, ne jen obrysovému", property);

    /// <summary>
    /// Size classes carry the padding, and the duplicate declared <c>padding</c> on the base class where
    /// no size class could beat it — same specificity, later in the bundle.
    /// </summary>
    [Fact]
    public void TheSizeClass_DecidesThePadding()
    {
        var winner = CssCascade.Resolve(ThemeCss.BundledCss(), OutlineSecondaryButton, "padding");

        winner.Unmodelled.Should().BeEmpty();
        winner.Source.Should().Be(".tm-btn-md");
    }

    // ── Mutation: the reader has to see the defect if it comes back ──

    /// <summary>
    /// The negative control. Without it the cascade guards would pass on a reader that simply never
    /// finds a later rule — a probe that cannot see the defect is not measuring the defect.
    /// </summary>
    [Fact]
    public void TheCascadeReader_SeesAGlobalButtonOverrideWhenOneIsPresent()
    {
        var mutated = ThemeCss.BundledCss()
                      + ".tm-btn{gap:var(--tm-space-1);border:1px solid transparent;}";

        var winner = CssCascade.Resolve(mutated, OutlineSecondaryButton, "border-color");

        winner.Source.Should().Be(".tm-btn");
        winner.Value.Should().Be("transparent");
        CssCascade.Resolve(mutated, OutlineSecondaryButton, "gap").Value.Should().Be("var(--tm-space-1)");
    }

    /// <summary>
    /// The other direction of the same control: the structural sweep must go red on a reintroduced
    /// definition and green on the shipped state, which the theory above already asserts.
    /// </summary>
    [Fact]
    public void TheStructuralSweep_SeesAReintroducedGlobalDefinition()
    {
        var parts = ThemeCss.SelectorParts(".tm-btn, .tm-btn--sm, .tm-pivot-btn .tm-btn, .tm-btn:disabled");

        parts.Where(IsGlobalButtonSelector).Should().BeEquivalentTo([".tm-btn", ".tm-btn--sm"]);
    }

    /// <summary>
    /// The population the sweep runs over has to be the DIRECTORY, and it has to be big enough to be
    /// the directory. A theory that enumerates nothing is green, and it looks exactly like a theory
    /// that enumerated everything and found nothing wrong.
    /// </summary>
    [Fact]
    public void TheSweep_CoversEveryComponentStylesheet()
    {
        var swept = ComponentStylesheetNames();

        swept.Should().HaveCount(
            ComponentStylesheets().Count,
            "theorie a jmenovatel musí být tentýž seznam, jinak strážce hlídá jiný soubor, než vypisuje");
        swept.Should().HaveCountGreaterThan(50, "components/ nese desítky souborů; hrstka by znamenala, " +
            "že sonda čte jinou složku, než o které tvrdí, že ji čte");
        swept.Should().Contain("_pivot-table.css").And.Contain("_data-table.css").And.Contain("_button.css");
    }

    /// <summary>
    /// The link variant was the SECOND global class a component stylesheet had taken over, and it was
    /// found by the sweep rather than reported. Asserted by name as well, because a sweep says "nothing
    /// found" in exactly the same words whether it looked or not.
    /// </summary>
    [Fact]
    public void TheLinkVariant_IsOwnedByTheButtonStylesheetAlone()
    {
        var winner = CssCascade.Resolve(
            ThemeCss.BundledCss(),
            [new CssCascade.Element("button", "tm-btn", "tm-btn-md", "tm-btn-link")],
            "border-width");

        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be(
            "1px",
            "`border: none` z _data-table.css brala variantě Link její 1px box na KAŽDÉM tlačítku Tempa");

        CssCascade.Winning(
                ThemeCss.BundledCss(),
                [new CssCascade.Element("button", "tm-btn", "tm-btn-md", "tm-btn-link")],
                "font-size")
            .Should().Be("var(--tm-font-size-sm)", "velikostní třída rozhoduje o písmu, ne kontext");
    }

    /// <summary>
    /// A bare link button — no <c>.tm-btn</c> — still needs the user-agent reset it used to get from the
    /// duplicate. It has its own class now, and losing it would be a regression sold as a cleanup.
    /// </summary>
    [Fact]
    public void ABareLinkButton_KeepsItsReset()
    {
        var block = ThemeCss.Rules("_button.css")
            .Where(rule => ThemeCss.SelectorParts(rule.Selector).Contains(".tm-link-button", StringComparer.Ordinal))
            .Select(rule => rule.Body)
            .ToList();

        block.Should().ContainSingle();
        block[0].Should().Contain("border: none")
            .And.Contain("background: none")
            .And.Contain("font-size: inherit")
            .And.Contain("padding: 0");
    }
}
