using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// 2.13 OPTION-LANG: BCP 47 <c>lang</c> on option text and an invariant <c>value</c> on the
/// native radio. Empty Lang is omitted; redundant <c>aria-checked</c> stays off the input.
/// </summary>
public class TmRadioLangTests : LocalizationTestBase
{
    [Fact]
    public void Radio_Lang_RendersOnText_NotOnInput()
    {
        var cut = Render<TmRadio>(p => p
            .Add(c => c.Name, "lang")
            .Add(c => c.Label, "Čeština")
            .Add(c => c.Lang, "cs")
            .Add(c => c.Value, "cs"));

        cut.Find(".tm-radio-text").GetAttribute("lang").Should().Be("cs");
        cut.Find("input[type='radio']").HasAttribute("lang").Should().BeFalse();
    }

    [Fact]
    public void Radio_EmptyLang_OmitsLangAttribute()
    {
        var cut = Render<TmRadio>(p => p
            .Add(c => c.Name, "lang")
            .Add(c => c.Label, "English")
            .Add(c => c.Lang, "")
            .Add(c => c.Value, "en"));

        cut.Find(".tm-radio-text").HasAttribute("lang").Should().BeFalse();
    }

    [Fact]
    public void Radio_NativeInput_HasInvariantValue_AndNoAriaChecked()
    {
        var cut = Render<TmRadio>(p => p
            .Add(c => c.Name, "flag")
            .Add(c => c.Label, "On")
            .Add(c => c.Value, true)
            .Add(c => c.IsChecked, true));

        var input = cut.Find("input[type='radio']");
        input.GetAttribute("value").Should().Be("true");
        input.HasAttribute("aria-checked").Should().BeFalse(
            "a native radio already exposes checked; aria-checked is redundant ARIA in HTML");
    }

    [Fact]
    public void RadioGroup_ForwardsLang_AndFormatsValue()
    {
        var options = new List<RadioOption<DateOnly>>
        {
            new("Dnes", new DateOnly(2026, 3, 15)) { Lang = "cs" },
            new("Today", new DateOnly(2026, 3, 16)),
        };

        var cut = Render<TmRadioGroup<DateOnly>>(p => p
            .Add(c => c.Name, "day")
            .Add(c => c.Options, options)
            .Add(c => c.Value, new DateOnly(2026, 3, 15)));

        cut.Find(".tm-radio-text").GetAttribute("lang").Should().Be("cs");
        cut.FindAll("input[type='radio']")[0].GetAttribute("value").Should().Be("2026-03-15");
        cut.FindAll("input[type='radio']")[1].HasAttribute("lang").Should().BeFalse();
        cut.FindAll(".tm-radio-text")[1].HasAttribute("lang").Should().BeFalse();
    }

    [Fact]
    public void RadioGroup_Segmented_RendersLangAndValue()
    {
        var options = new List<RadioOption<bool>>
        {
            new("Ano", true) { Lang = "cs" },
            new("No", false),
        };

        var cut = Render<TmRadioGroup<bool>>(p => p
            .Add(c => c.Name, "yesno")
            .Add(c => c.Appearance, RadioAppearance.Segmented)
            .Add(c => c.Options, options)
            .Add(c => c.Value, true));

        var labels = cut.FindAll(".tm-radio-segment span");
        labels[0].GetAttribute("lang").Should().Be("cs");
        labels[1].HasAttribute("lang").Should().BeFalse();
        cut.FindAll(".tm-radio-segment input[type='radio']")[0].GetAttribute("value").Should().Be("true");
        cut.FindAll(".tm-radio-segment input[type='radio']")[1].GetAttribute("value").Should().Be("false");
    }

    [Fact]
    public void SegmentedControl_Lang_OnOptionLabel_OmittedWhenEmpty()
    {
        var cut = Render<TmSegmentedControl<string>>(p => p
            .Add(c => c.Options, new List<SegmentedOption<string>>
            {
                new() { Label = "Česky", Value = "cs", Lang = "cs" },
                new() { Label = "English", Value = "en" },
            })
            .Add(c => c.Value, "cs"));

        var labels = cut.FindAll(".tm-segmented__option-label");
        labels[0].GetAttribute("lang").Should().Be("cs");
        labels[1].HasAttribute("lang").Should().BeFalse();
        cut.FindAll(".tm-segmented__option")[0].GetAttribute("data-value").Should().Be("cs");
    }
}
