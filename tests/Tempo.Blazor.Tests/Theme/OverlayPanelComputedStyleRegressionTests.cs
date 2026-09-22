using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// N196 (code review 2026-09-22, BLOCKER): <c>_overlay-panel.css</c> is imported by
/// <c>tempo-blazor.css</c> AFTER every consumer stylesheet, so a plain <c>.tm-overlay-panel</c>
/// rule sits on the SAME element as the consumer's own <see cref="Class"/> at the SAME (0,1,0)
/// specificity and wins by source order — every migrated dropdown, calendar, menu and popover
/// rendered transparent, borderless and unpadded. The compound
/// <c>.tm-overlay-panel.tm-overlay-panel--open{display:block}</c> (0,2,0) additionally overrode
/// the four consumers that lay their panel out with <c>display:flex</c>
/// (TmMultiSelect, TmNotificationBell, TmDateRangePicker, TmTagPicker).
/// <para>
/// The fix keeps the reset functional but takes its specificity to ZERO via <c>:where()</c>:
/// <c>:where(.tm-overlay-panel)</c> for the UA-popover-default reset,
/// <c>:where(.tm-overlay-panel.tm-overlay-panel--open)</c> for the open-state display, while the
/// closed-state <c>.tm-overlay-panel:not(.tm-overlay-panel--open){display:none}</c> stays at full
/// strength on purpose (the fallback path without the Popover API hides the panel through it).
/// Every row below pins the values the consumer's own class must win.
/// </para>
/// <para>
/// THE POPULATION IS MEASURED FROM THE SOURCE TREE, not from this table: a fresh
/// <c>&lt;TmOverlayPanel</c> grep over <c>src/Tempo.Blazor/Components</c> is the denominator, and
/// <see cref="Population_MatchesGrepList_FailClosed"/> fails when a fifteenth (or a dropped)
/// consumer drifts away from the inventory the assertions run over.
/// </para>
/// </summary>
public sealed class OverlayPanelComputedStyleRegressionTests
{
    private static string Bundle => ThemeCss.BundledCss();

    /// <summary>
    /// One <c>&lt;TmOverlayPanel&gt;</c> consumer: its razor file name (the population key), the
    /// <c>Class</c> string it passes to the panel (<see langword="null"/> when it passes none),
    /// and the computed values the consumer class must keep against the reset. A
    /// <see langword="null"/> expectation means the owner does not declare that property on the
    /// panel class — it is NOT asserted, but the consumer still has to appear in the population.
    /// </summary>
    /// <summary>Public: xUnit MemberData rows require an accessible parameter type.</summary>
    public sealed record Consumer(
        string File,
        string? PanelClass,
        string? Background,
        string? BorderColor,
        string? Padding,
        string? Display)
    {
        /// <summary>
        /// Selectors the cascade model cannot decide that this row is allowed to produce —
        /// recorded per row so a NEW unreadable selector fails instead of blending in. The only
        /// ones today are the <c>[data-theme="dark"]</c> theme variants, whose ancestor the
        /// modelled chain cannot express (they answer the dark reading; the row's expectation
        /// is the resting light-theme winner).
        /// </summary>
        public IReadOnlyList<string> AllowedUnmodelled { get; init; } = [];
    }

    private static readonly IReadOnlyList<Consumer> Consumers =
    [
        new("TmSplitButton.razor", "tm-split-button__dropdown",
            "var(--tm-bg-surface)", "var(--tm-border-color)", "0.25rem", null),
        new("TmDropdown.razor", "tm-dropdown-menu",
            "var(--tm-bg-surface)", "var(--tm-border-color)", "var(--tm-space-1)", null),
        new("TmFilterableDropdown.razor", "tm-filterable-dropdown-menu",
            "var(--tm-bg-surface)", "var(--tm-border-color)", null, null),
        // Bundle spellings: the minifier strips the space after commas inside var().
        new("TmPopover.razor", "tm-popover__body",
            "var(--tm-color-surface,#fff)", "var(--tm-border-color)", "0.75rem 1rem", null),
        new("TmColorPicker.razor", "tm-color-picker-dropdown",
            "var(--tm-surface)", null, null, null),
        new("TmEntityPicker.razor", "tm-entity-picker__dropdown",
            "var(--tm-bg-surface)", "var(--tm-border-color)", null, null),
        new("TmMultiColumnComboBox.razor", "tm-multi-column-combo-box__dropdown",
            "var(--tm-surface,#ffffff)", "var(--tm-border-color,#d1d5db)", null, null),
        new("TmMultiSelect.razor", "tm-multiselect__popup",
            "var(--tm-bg-surface)", "var(--tm-border-color)", null, "flex"),
        // TmQueryInput passes no Class: the box lives on the nested <ul.tm-query-input__dropdown>,
        // asserted separately in QueryInput_NestedDropdown_KeepsItsSurfaceBorderAndPadding.
        new("TmQueryInput.razor", null, null, null, null, null),
        new("TmContextMenu.razor", "tm-context-menu",
            "var(--tm-bg-surface)", "var(--tm-border-color)", "0.25rem", null),
        new("TmNotificationBell.razor", "tm-notification-bell__dropdown",
            "var(--tm-bg-surface)", "var(--tm-border-color)", null, "flex")
        {
            // [data-theme="dark"] .tm-notification-bell__dropdown re-declares background +
            // border-color for the dark theme — an ancestor the chain model cannot express, so
            // the selector is reported as UNMODELLED rather than silently skipped. Recorded here
            // so any further unreadable selector on this row still fails.
            AllowedUnmodelled = ["[data-theme=\"dark\"] .tm-notification-bell__dropdown"],
        },
        new("TmDatePicker.razor", "tm-date-picker-popup",
            "var(--tm-bg-surface)", "var(--tm-border-color)", "var(--tm-space-2)", null),
        new("TmDateRangePicker.razor", "tm-date-range-popup",
            "var(--tm-bg-surface)", "var(--tm-border-color)", "var(--tm-space-3)", "flex"),
        // TmDateTimePicker intentionally shares TmDatePicker's panel class.
        new("TmDateTimePicker.razor", "tm-date-picker-popup",
            "var(--tm-bg-surface)", "var(--tm-border-color)", "var(--tm-space-2)", null),
        new("TmTagPicker.razor", "tm-tag-picker-dropdown",
            "var(--tm-bg-surface)", "var(--tm-border-color)", "var(--tm-space-2)", "flex"),
    ];

    private static IReadOnlyList<CssCascade.Element> PanelChain(string panelClass) =>
        [new CssCascade.Element("div", "tm-overlay-panel", "tm-overlay-panel--open", panelClass)];

    /// <summary>
    /// The open panel element carrying the consumer class — the element N196's reset and
    /// <c>display:block</c> beat. The same chain every row resolves against.
    /// </summary>
    private static void AssertOwned(Consumer row, string property, string expected)
    {
        var winner = CssCascade.Resolve(Bundle, PanelChain(row.PanelClass!), property);
        winner.Unmodelled.Should().BeSubsetOf(row.AllowedUnmodelled,
            "selektor, který sonda neumí přečíst, je NEMĚŘITELNÝ — povolené jsou jen jmenovitě " +
            "vypsané tmavé varianty řádku; nalezeno navíc: {0}",
            string.Join(" | ", winner.Unmodelled.Except(row.AllowedUnmodelled)));
        winner.Value.Should().Be(expected,
            "{0}: otevřený panel musí malovat {1} = {2} z vlastnické třídy .{3} — :where() reset " +
            "v _overlay-panel.css (specificita 0) ho nesmí přebít (N196)",
            row.File, property, expected, row.PanelClass);
    }

    /// <summary>
    /// The population of overlay consumers, measured independently of the table above — a grep
    /// over the razor sources. Zero found means the probe is reading the wrong tree
    /// (empty-population), and any divergence from the table is a missing row, not a smaller
    /// world.
    /// </summary>
    [Fact]
    public void Population_MatchesGrepList_FailClosed()
    {
        var componentsDir = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "Components");
        var found = Directory.GetFiles(componentsDir, "*.razor", SearchOption.AllDirectories)
            .Where(file => !string.Equals(
                Path.GetFileName(file), "TmOverlayPanel.razor", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("<TmOverlayPanel", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        found.Should().NotBeEmpty(
            "empty-population: žádný soubor s <TmOverlayPanel — sonda čte špatnou složku");
        found.Should().BeEquivalentTo(Consumers.Select(c => c.File),
            "jmenovatel je nezávislý grep zdrojů — nový konzument do tabulky přidá řádek, " +
            "jinak sonda měří menší svět, než jaký existuje");
    }

    /// <summary>Every declared consumer property wins over the zero-specificity reset.</summary>
    [Theory]
    [MemberData(nameof(ConsumerData))]
    public void ConsumerPanel_OwnedDeclarations_WinOverTheReset(Consumer row)
    {
        if (row.Background is not null)
        {
            AssertOwned(row, "background-color", row.Background);
        }

        if (row.BorderColor is not null)
        {
            AssertOwned(row, "border-color", row.BorderColor);
        }

        if (row.Padding is not null)
        {
            AssertOwned(row, "padding", row.Padding);
        }

        if (row.Display is not null)
        {
            AssertOwned(row, "display", row.Display);
        }
    }

    /// <summary>Rows that carry a panel class — TmQueryInput's null-Class row is asserted by its
    /// own nested-chain fact instead, so a vacuous pass cannot masquerade as coverage.</summary>
    public static IEnumerable<object[]> ConsumerData =>
        Consumers.Where(consumer => consumer.PanelClass is not null)
            .Select(consumer => new object[] { consumer });

    // ── N196's two halves, named for the review finding ─────────────────────

    /// <summary>The reset half: a consumer's surface colour must beat the transparent reset.</summary>
    [Fact]
    public void TmDropdown_Background_IsSurface_NotTransparent()
    {
        var winner = CssCascade.Resolve(
            Bundle, PanelChain("tm-dropdown-menu"), "background-color");
        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be("var(--tm-bg-surface)",
            "N196: .tm-overlay-panel{background:transparent} (0,1,0) za _dropdown.css v bundlu " +
            "přebíjelo .tm-dropdown-menu — dropdowny se malovaly průhledné");
    }

    /// <summary>The display half: a consumer's flex layout must beat the open-state block.</summary>
    [Fact]
    public void TmMultiSelect_Popup_Display_IsFlex_NotBlock()
    {
        var winner = CssCascade.Resolve(
            Bundle, PanelChain("tm-multiselect__popup"), "display");
        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be("flex",
            "N196: .tm-overlay-panel.tm-overlay-panel--open{display:block} (0,2,0) přebíjelo " +
            ".tm-multiselect__popup{display:flex} — filtr a položky spadly pod sebe");
    }

    [Fact]
    public void TmNotificationBell_Dropdown_Display_IsFlex_NotBlock()
    {
        var winner = CssCascade.Resolve(
            Bundle, PanelChain("tm-notification-bell__dropdown"), "display");
        winner.Unmodelled.Should().BeEmpty(
            "display tmavá varianta nedeklaruje — žádný nečitelný selektor tu nesmí být");
        winner.Value.Should().Be("flex");
    }

    [Fact]
    public void TmDateRangePicker_Popup_Display_IsFlex_NotBlock()
    {
        var winner = CssCascade.Resolve(
            Bundle, PanelChain("tm-date-range-popup"), "display");
        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be("flex");
    }

    [Fact]
    public void TmTagPicker_Dropdown_Display_IsFlex_NotBlock()
    {
        var winner = CssCascade.Resolve(
            Bundle, PanelChain("tm-tag-picker-dropdown"), "display");
        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be("flex");
    }

    /// <summary>
    /// TmQueryInput is the one consumer that passes no <c>Class</c>: its visible box is the
    /// nested <c>&lt;ul class="tm-query-input__dropdown"&gt;</c>. The reset must not reach that
    /// descendant either — the chain models panel → list, exactly as rendered.
    /// </summary>
    [Fact]
    public void QueryInput_NestedDropdown_KeepsItsSurfaceBorderAndPadding()
    {
        IReadOnlyList<CssCascade.Element> chain =
        [
            new CssCascade.Element("div", "tm-overlay-panel", "tm-overlay-panel--open"),
            new CssCascade.Element("ul", "tm-query-input__dropdown"),
        ];

        var background = CssCascade.Resolve(Bundle, chain, "background-color");
        background.Unmodelled.Should().BeEmpty();
        background.Value.Should().Be("var(--tm-bg-surface)",
            "vnořený <ul> nesmí zdědit průhlednost panelu — vlastní pozadí deklaruje sám");
        var border = CssCascade.Resolve(Bundle, chain, "border-color");
        border.Unmodelled.Should().BeEmpty();
        border.Value.Should().Be("var(--tm-border-color)");
        var padding = CssCascade.Resolve(Bundle, chain, "padding");
        padding.Unmodelled.Should().BeEmpty();
        padding.Value.Should().Be("var(--tm-space-1,0.25rem)");
    }

    /// <summary>
    /// Mutation proof, display half: reintroducing the pre-:where() open rule must flip the
    /// multiselect winner back to <c>block</c> — the same <see cref="CssCascade.Resolve"/> the
    /// live assertions call, run over a mutated bundle string.
    /// </summary>
    [Fact]
    public void MutationProof_ReintroducingPlainClassReset_FlipsDisplayBackToBlock()
    {
        var mutated = Bundle.Replace(
            ":where(.tm-overlay-panel.tm-overlay-panel--open)",
            ".tm-overlay-panel.tm-overlay-panel--open",
            StringComparison.Ordinal);
        mutated.Should().NotBe(Bundle,
            "mutace nezasáhla žádný text — test by byl vacuous");

        var winner = CssCascade.Resolve(mutated, PanelChain("tm-multiselect__popup"), "display");
        winner.Unmodelled.Should().BeEmpty(
            "mutace je platné CSS — model ho musí umět přečíst, jinak dokazuje díru v parseru");
        winner.Value.Should().Be("block",
            "mutovaný bundle reprodukuje PŮVODNÍ bug N196: (0,2,0) selektor přebije flex");
    }

    /// <summary>
    /// Mutation proof, reset half: unwrapping the :where() on the base rule must flip the
    /// dropdown's surface back to <c>transparent</c> — same mechanism, the visual half.
    /// </summary>
    [Fact]
    public void MutationProof_ReintroducingUnwrappedReset_FlipsBackgroundToTransparent()
    {
        var mutated = Bundle.Replace(
            ":where(.tm-overlay-panel){",
            ".tm-overlay-panel{",
            StringComparison.Ordinal);
        mutated.Should().NotBe(Bundle,
            "mutace nezasáhla žádný text — test by byl vacuous");

        var winner = CssCascade.Resolve(
            mutated, PanelChain("tm-dropdown-menu"), "background-color");
        winner.Unmodelled.Should().BeEmpty();
        winner.Value.Should().Be("transparent",
            "mutovaný bundle reprodukuje PŮVODNÍ bug N196: pozdější reset stejné specificity " +
            "přebije vlastnické pozadí");
    }
}
