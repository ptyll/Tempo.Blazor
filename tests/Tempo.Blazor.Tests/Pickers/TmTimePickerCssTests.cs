using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// CSS contract of TmTimePicker (plan 1c95309c Fáze 6): tokens only, no hardcoded sizes.
/// </summary>
public sealed class TmTimePickerCssTests
{
    [Fact]
    public void TriggerOnly_MinInlineSize_UsesAToken()
    {
        var css = ThemeCss.ComponentCss("_time-picker.css");
        css.Should().Contain(".tm-time-picker--trigger-only .tm-time-picker__trigger");
        css.Should().NotContain("min-inline-size: 10rem;",
            "component CSS uses tokens, never a hardcoded rem size");
        css.Should().Contain("min-inline-size: var(--tm-time-picker-trigger-min-width, calc(var(--tm-space-10) * 4));");
    }

    [Fact]
    public void BundledStylesheet_CarriesTheTokenizedTriggerMinWidth()
    {
        var bundled = ThemeCss.BundledCss();
        bundled.Should().Contain(".tm-time-picker--trigger-only .tm-time-picker__trigger");
        bundled.Should().NotContain(
            ".tm-time-picker--trigger-only .tm-time-picker__trigger{min-inline-size:10rem;",
            "the committed bundle must match the tokenized source");
        bundled.Should().Contain("min-inline-size:var(--tm-time-picker-trigger-min-width,calc(var(--tm-space-10) * 4))");
    }
}
