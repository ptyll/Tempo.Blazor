using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Review round (plan 98c80bc3, Fáze 3): the readable locked surface must never borrow the DISABLED
/// ink. A readonly field keeps primary-ink text on a secondary surface — <c>--tm-text-disabled</c>
/// is the token that names the "this control is off" reading, and a locked value that paints itself
/// with it reads as broken. Also locks the hidden chevron: a read-only select does not open a list,
/// so the decorative arrow must go.
/// </summary>
public sealed class FormsReadOnlySurfaceCssTests
{
    private const string InputStylesheet = "_input.css";
    private const string DropdownStylesheet = "_filterable-dropdown.css";

    private static string Rule(string stylesheet, string selector)
        => ThemeCss.RuleDeclarations(ThemeCss.ComponentCss(stylesheet), selector);

    [Fact]
    public void TheSelectReadonlySurfaceDoesNotUseTheDisabledInk()
    {
        var body = Rule(InputStylesheet, ".tm-select.tm-select-readonly");

        body.Should().NotBeEmpty("the read-only select surface must exist");
        body.Should().NotContain("--tm-text-disabled",
            "a locked value keeps the primary ink — the disabled token is the other, gray state");
        body.Should().Contain("background-color", "the locked state reads through the surface tint");
    }

    [Fact]
    public void TheReadonlySelectHidesTheChevron()
    {
        var body = Rule(InputStylesheet, ".tm-select.tm-select-readonly ~ .tm-select-arrow");

        body.Should().Contain("display: none",
            "a read-only select does not open a list, so the chevron must not advertise it");
    }

    [Fact]
    public void TheFilterableDropdownReadonlyTriggerDoesNotUseTheDisabledInk()
    {
        var body = Rule(DropdownStylesheet, ".tm-filterable-dropdown-readonly .tm-filterable-dropdown-trigger");

        body.Should().NotBeEmpty("the read-only combobox surface must exist");
        body.Should().NotContain("--tm-text-disabled",
            "a locked value keeps the primary ink — the disabled token is the other, gray state");
        body.Should().Contain("background", "the locked state reads through the surface tint");
    }

    /// <summary>
    /// Review round (plan 98c80bc3, Fáze 3): the DISABLED combobox trigger must be able to borrow the
    /// disabled ink — the gray surface token, the <c>--tm-text-disabled</c> colour and the
    /// not-allowed cursor — and its hover must keep the neutral border, never the primary one the
    /// enabled trigger advertises.
    /// </summary>
    [Fact]
    public void TheDisabledDropdownTriggerUsesTheDisabledSurfaceAndTheNeutralHover()
    {
        var body = Rule(DropdownStylesheet, ".tm-filterable-dropdown-disabled .tm-filterable-dropdown-trigger");

        body.Should().NotBeEmpty("the disabled combobox surface must exist");
        body.Should().Contain("--tm-color-gray-50", "the disabled state paints the gray surface token");
        body.Should().Contain("--tm-text-disabled", "the disabled ink is the token that names the state");
        body.Should().Contain("cursor: not-allowed", "the cursor says the control is off");

        var hover = Rule(DropdownStylesheet, ".tm-filterable-dropdown-disabled .tm-filterable-dropdown-trigger:hover");
        hover.Should().Contain("border-color: var(--tm-border-color)",
            "a disabled trigger never advertises the primary hover border");
    }

    /// <summary>Both disabled-state tokens resolve in the light AND the dark token graph.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDisabledTriggerTokensResolveInBothThemes(bool dark)
    {
        var tokens = ThemeCss.TokenGraph(dark);

        tokens.Should().ContainKey("--tm-color-gray-50");
        tokens.Should().ContainKey("--tm-text-disabled");
    }
}
