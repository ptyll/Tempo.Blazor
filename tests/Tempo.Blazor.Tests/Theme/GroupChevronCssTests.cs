using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Phase 13 (found by the UX review of the grouped-cards screenshot): the group header chevron pointed
/// right on an EXPANDED group. <c>TmIcon</c> has no <c>Class</c> parameter — the class the group
/// toggles pass lands in <c>AdditionalAttributes</c> and is then overwritten by the icon's own
/// <c>class</c> — so the <c>--open</c> modifier never reached the SVG. The rotation is therefore keyed off
/// the button's <c>aria-expanded</c>, which is the state assistive technology reads as well.
/// </summary>
public sealed class GroupChevronCssTests
{
    [Theory]
    [InlineData("_data-table.css", ".tm-data-table-group-toggle[aria-expanded=\"true\"] .tm-icon")]
    [InlineData("_multi-view-list.css", ".tm-mvl-group-toggle[aria-expanded=\"true\"] .tm-icon")]
    public void ExpandedGroupToggle_RotatesItsChevron(string file, string selector)
    {
        ThemeCss.TryProperty(file, selector, "transform").Should().Be("rotate(90deg)");
    }

    [Theory]
    [InlineData(".tm-view-radio-label input")]
    [InlineData(".tm-view-column-item input")]
    public void ViewDialogNativeControls_FollowThePrimaryToken(string selector)
    {
        // The Save/Edit view dialog is a TmModal portaled outside the table, so the browser's default #0075FF
        // blue leaked into the indigo theme (found by the UX review of the mobile dialog screenshot).
        ThemeCss.TryProperty("_data-table.css", selector, "accent-color").Should().Be("var(--tm-color-primary)");
    }
}