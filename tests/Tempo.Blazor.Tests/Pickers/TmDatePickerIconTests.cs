using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// The date-picker trigger's calendar affordance must be a real TmIcon (crisp, theme-aware SVG)
/// rather than the old emoji rendered via CSS ::after content.
/// </summary>
public class TmDatePickerIconTests : LocalizationTestBase
{
    [Fact]
    public void DatePicker_RendersCalendarSvgIcon_InsideIconSlot()
    {
        var cut = Render<TmDatePicker>();

        var iconSlot = cut.Find(".tm-date-picker-icon");
        var svg = iconSlot.QuerySelector("svg.tm-icon");
        svg.Should().NotBeNull("the calendar affordance is a TmIcon SVG, not an emoji");
    }

    [Fact]
    public void DatePicker_IconSlot_IsAriaHidden()
    {
        var cut = Render<TmDatePicker>();

        cut.Find(".tm-date-picker-icon").GetAttribute("aria-hidden").Should().Be("true");
    }

    // Plan b75365fb Fáze 4 (review): a locked trigger has no popup to advertise, so the calendar
    // affordance is not rendered at all — the secondary-surface tint names the lock, not an icon.
    [Fact]
    public void DatePicker_ReadOnly_DoesNotRenderTheCalendarIcon()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Value, new DateOnly(2026, 3, 15))
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-date-picker-icon").Should().BeEmpty(
            "a locked trigger has no popup to advertise — the surface tint names the lock, not an icon");
    }
}
