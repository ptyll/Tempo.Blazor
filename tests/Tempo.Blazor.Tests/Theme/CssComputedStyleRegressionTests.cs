using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Pins the COMPUTED-style contract that 2.8.26 broke when it deleted five "dead duplicate" rules
/// that were not dead: each one carried at least one declaration no other rule supplied, or beat the
/// component's own class on specificity — so removing them changed what users see.
/// <para>
/// THE MECHANISM, per removal, and the owning declaration that restores the 2.8.25 paint:
/// <list type="bullet">
/// <item><c>_dashboard.css</c>'s <c>.tm-modal-header h3</c> (0,1,1) out-ranked
/// <c>.tm-modal-title</c> (0,1,0) and pinned the title at <c>--tm-font-size-lg</c> (18px). With the
/// descendant gone the title grew to <c>xl</c> (20px). Restored on <c>.tm-modal-title</c> itself.</item>
/// <item><c>_dashboard.css</c>'s bare <c>.tm-modal-close</c> was the only rule declaring its
/// <c>font-size</c> (<c>xl</c>); without it the × renders at the inherited 16px. Restored on
/// <c>.tm-modal-close</c>.</item>
/// <item><c>_dashboard.css</c>'s bare <c>.tm-modal-header</c> was the only rule declaring
/// <c>justify-content: space-between</c>; shared properties were already lost to the later-imported
/// <c>_modal.css</c>, this one was not shared. Restored on <c>.tm-modal-header</c>.</item>
/// <item><c>_dashboard.css</c>'s <c>@media (max-width: 768px) .tm-modal { margin: space-4 }</c> was
/// the only margin the modal ever had below 768px. Restored on <c>.tm-modal</c> under the same
/// condition in <c>_modal.css</c>.</item>
/// <item><c>_data-table.css</c>'s <c>.tm-filter-chip button</c> (0,1,1) beat
/// <c>.tm-filter-chip-remove</c> (0,1,0): the × lost its 18px font for 16px and gained 4px of
/// horizontal padding it never had. Restored on <c>.tm-filter-chip-remove</c>.</item>
/// <item><c>_rich-text-editor.css</c>'s bare <c>.tm-rte-mention-dropdown</c> was the only rule
/// giving the live MentionAutocomplete dropdown its vertical padding
/// (<c>var(--tm-space-1) 0</c>). Restored on <c>.tm-rte-mention-dropdown</c> in
/// <c>_mention-autocomplete.css</c>.</item>
/// </list>
/// </para>
/// <para>
/// The resolution runs over the COMMITTED BUNDLE, not the component sources: which of two rules wins
/// is a question of their order in what the browser actually downloads, and that order exists only in
/// <c>tempo-blazor.bundled.css</c>. The removed rules lived in <c>_dashboard.css</c>,
/// <c>_data-table.css</c> and <c>_rich-text-editor.css</c> — all imported BEFORE the owning
/// stylesheets, which is why every shared property already lost and only the unique or higher-
/// specificity declarations ever painted.
/// </para>
/// </summary>
public sealed class CssComputedStyleRegressionTests
{
    private static string Bundle => ThemeCss.BundledCss();

    private static readonly IReadOnlyList<CssCascade.Element> ModalTitleChain =
    [
        new CssCascade.Element("div", "tm-modal-header"),
        new CssCascade.Element("div", "tm-modal-header-content"),
        new CssCascade.Element("h3", "tm-modal-title"),
    ];

    private static readonly IReadOnlyList<CssCascade.Element> ModalCloseChain =
    [
        new CssCascade.Element("div", "tm-modal-header"),
        new CssCascade.Element("button", "tm-modal-close"),
    ];

    private static readonly IReadOnlyList<CssCascade.Element> ModalHeaderChain =
    [
        new CssCascade.Element("div", "tm-modal"),
        new CssCascade.Element("div", "tm-modal-header"),
    ];

    private static readonly IReadOnlyList<CssCascade.Element> ModalChain =
        [new CssCascade.Element("div", "tm-modal")];

    private static readonly IReadOnlyList<CssCascade.Element> FilterChipRemoveChain =
    [
        new CssCascade.Element("span", "tm-filter-chip"),
        new CssCascade.Element("button", "tm-filter-chip-remove"),
    ];

    private static readonly IReadOnlyList<CssCascade.Element> MentionDropdownChain =
        [new CssCascade.Element("div", "tm-rte-mention-dropdown")];

    [Fact]
    public void ModalTitle_PaintsAtTheSizeTheRemovedDescendantPinned()
    {
        CssCascade.Winning(Bundle, ModalTitleChain, "font-size")
            .Should().Be("var(--tm-font-size-lg)",
                "2.8.25 maloval titulek 18px: .tm-modal-header h3 (0,1,1) přebíjelo .tm-modal-title; " +
                "regrese ho zvětšila na xl — vlastnická deklarace musí hodnotu vrátit");
    }

    [Fact]
    public void ModalClose_KeepsTheFontSizeOnlyTheRemovedRuleDeclared()
    {
        CssCascade.Winning(Bundle, ModalCloseChain, "font-size")
            .Should().Be("var(--tm-font-size-xl)",
                "jedinou deklarací font-size na × byl mrtvý-bare .tm-modal-close v _dashboard.css; " +
                "bez ní tlačítko dědí menší velikost, než mělo v 2.8.25");
    }

    [Fact]
    public void ModalHeader_KeepsTheSpaceBetweenTheRemovedRuleDeclared()
    {
        CssCascade.Winning(Bundle, ModalHeaderChain, "justify-content")
            .Should().Be("space-between",
                "justify-content deklarovalo jen mrtvé .tm-modal-header v _dashboard.css — " +
                "vlastník ho musí deklarovat sám, jinak computed style driftne");
    }

    [Fact]
    public void Modal_KeepsItsMobileMarginBelow768px()
    {
        var mobile = new CssCascade.MediaContext(WidthPx: 500);
        CssCascade.Winning(Bundle, ModalChain, "margin", media: mobile)
            .Should().Be("var(--tm-space-4)",
                "pod 768 px měl .tm-modal v 2.8.25 margin space-4 z @media bloku v _dashboard.css; " +
                "pořadí importů tu nic neřešilo — deklarace byla jediná, a tedy platila");
    }

    [Fact]
    public void FilterChipRemove_KeepsZeroPaddingAndTheLargerGlyph()
    {
        CssCascade.Winning(Bundle, FilterChipRemoveChain, "padding")
            .Should().Be("0",
                ".tm-filter-chip button (0,1,1) držel padding na 0; bez něj vyhrává " +
                "0 var(--tm-space-1) vlastníka a × dostalo 4px navíc, které v 2.8.25 nemělo");
        CssCascade.Winning(Bundle, FilterChipRemoveChain, "font-size")
            .Should().Be("var(--tm-font-size-lg)",
                "totéž pro font-size: potomek držel lg (18px), vlastník deklaruje md (16px) — " +
                "hodnota se vrací na vlastní třídu");
    }

    [Fact]
    public void MentionDropdown_KeepsItsVerticalPadding()
    {
        CssCascade.Winning(Bundle, MentionDropdownChain, "padding")
            .Should().Be("var(--tm-space-1) 0",
                "živý MentionAutocomplete používá stejnou třídu jako mrtvá implementace v " +
                "_rich-text-editor.css; její padding: var(--tm-space-1) 0 byl jediný, takže platil — " +
                "a dropdown bez něj slepí položky k okrajům");
    }
}
