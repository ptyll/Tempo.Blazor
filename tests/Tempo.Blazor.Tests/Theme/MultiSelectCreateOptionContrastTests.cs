using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the opt-in create row of <c>TmMultiSelect</c> once the keyboard reaches it — WCAG 2.4.7 (Focus
/// Visible) and 1.4.3 (Contrast, minimum).
/// <para>
/// UX review of plan ff043b2e phase 8, finding 1: <c>aria-activedescendant</c> pointed at the row but the
/// row never carried <c>tm-multiselect__option--focused</c>, so a sighted keyboard user watched the
/// highlight vanish from the list exactly where Enter was about to create an item. The markup fix lives in
/// <c>TmMultiSelect.razor</c> and is pinned by
/// <c>TmComboboxHighlightTests.MultiSelect_TheCreateRowPaintsTheFocusedStyle_WhenArrowedOnto</c>; this
/// guard answers the part a class assertion cannot: the row's own accent tone must still reach 4.5:1 on
/// every background the focus and hover states paint behind it.
/// </para>
/// <para>
/// Measured before the fix, the accent was <c>var(--tm-color-primary)</c>: <b>4.07:1</b> on the dark
/// <c>--tm-bg-muted</c> panel — below AA — while <c>--tm-color-primary-text</c> (the token that exists for
/// "primary as INK on a surface") measures 7.92:1 light and 5.74:1 dark. The row therefore keeps its
/// accent identity instead of turning into ordinary text (the reviewer's fallback suggestion), and the
/// values are read from the shipped bundle rather than copied into the test.
/// </para>
/// </summary>
public sealed class MultiSelectCreateOptionContrastTests
{
    /// <summary>WCAG 2.1 SC 1.4.3 — 4.5:1 for normal-size text.</summary>
    private const double TextMinimum = 4.5;

    private static readonly IReadOnlySet<string> Resting = new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> Hovered =
        new HashSet<string>(StringComparer.Ordinal) { ":hover" };

    public static TheoryData<string, bool> States() => new()
    {
        { "resting", false },
        { "hover", false },
        { "focused", false },
        { "resting", true },
        { "hover", true },
        { "focused", true },
    };

    private static IReadOnlyList<CssCascade.Element> CreateRow(params string[] extraClasses)
    {
        var classes = new HashSet<string>(
            new[] { "tm-multiselect__option", "tm-multiselect__option--create" }.Concat(extraClasses),
            StringComparer.Ordinal);

        return
        [
            new("div", "tm-multiselect"),
            new("div", "tm-multiselect__popup"),
            new("div", "tm-multiselect__options"),
            new("div", classes),
        ];
    }

    private static (IReadOnlyList<CssCascade.Element> Chain, IReadOnlySet<string> States) State(string name) =>
        (name switch
        {
            "focused" => CreateRow("tm-multiselect__option--focused"),
            _ => CreateRow(),
        }, name == "hover" ? Hovered : Resting);

    /// <summary>
    /// The opaque colour a viewer sees behind the row: the row's own declared background when it paints
    /// one, otherwise the nearest ancestor that does (the popup surface). Read through the cascade so a
    /// later, more specific rule cannot be missed.
    /// </summary>
    private static string Background(
        string css, IReadOnlyList<CssCascade.Element> chain, IReadOnlySet<string> states,
        Dictionary<string, string> tokens)
    {
        for (var length = chain.Count; length > 0; length--)
        {
            var resolved = CssCascade.Resolve(css, chain.Take(length).ToList(), "background-color", states);
            resolved.Unmodelled.Should().BeEmpty(
                "a selector the probe cannot read is UNMEASURABLE — it must not count as 'does not match'");
            if (resolved.Value is not null)
            {
                return ThemeCss.ResolveColour(resolved.Value, tokens);
            }
        }

        throw new InvalidOperationException(
            "the create row has no background in its ancestor chain — the probe would measure the page");
    }

    [Theory]
    [MemberData(nameof(States))]
    public void TheCreateRowTextMeetsTextContrast_InEveryStateAndTheme(string state, bool dark)
    {
        var css = ThemeCss.BundledCss();
        var (chain, states) = State(state);
        var tokens = ThemeCss.TokenGraph(dark);

        var foreground = ThemeCss.ResolveColour(
            CssCascade.Winning(css, chain, "color", states), tokens);
        var background = Background(css, chain, states, tokens);

        ThemeCss.Contrast(foreground, background).Should().BeGreaterThanOrEqualTo(
            TextMinimum,
            "the create row is text ({0}) on {1} while {2} in the {3} theme",
            foreground, background, state, dark ? "dark" : "light");
    }

    [Fact]
    public void TheCreateRowKeepsTheAccentTone_InsteadOfFallingBackToOrdinaryText()
    {
        var css = ThemeCss.BundledCss();
        var accent = ThemeCss.ResolveColour(
            "var(--tm-color-primary-text)", ThemeCss.TokenGraph(dark: false));
        var body = ThemeCss.ResolveColour("var(--tm-text-primary)", ThemeCss.TokenGraph(dark: false));

        var colour = ThemeCss.ResolveColour(
            CssCascade.Winning(css, CreateRow(), "color", Resting), ThemeCss.TokenGraph(dark: false));

        colour.Should().Be(accent,
            "the row is an ACTION at the end of the list, so it keeps the theme's primary-ink tone");
        colour.Should().NotBe(body, "ordinary option text would erase the accent that marks the action");
    }
}
