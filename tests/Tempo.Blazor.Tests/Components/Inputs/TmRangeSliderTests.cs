using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

public class TmRangeSliderTests : LocalizationTestBase
{
    [Fact]
    public void TmRangeSlider_Renders_Two_Inputs()
    {
        var cut = Render<TmRangeSlider>();
        cut.FindAll("input[type='range']").Count.Should().Be(2);
    }

    [Fact]
    public void TmRangeSlider_StartValue_EndValue_Renders_Correctly()
    {
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.StartValue, 20)
            .Add(x => x.EndValue, 80));

        var inputs = cut.FindAll("input");
        inputs[0].GetAttribute("value").Should().Be("20");
        inputs[1].GetAttribute("value").Should().Be("80");
    }

    [Fact]
    public void TmRangeSlider_StartValueChanged_Fires_Event()
    {
        int? captured = null;
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.StartValueChanged, EventCallback.Factory.Create<int?>(this, v => captured = v)));

        var inputs = cut.FindAll("input");
        inputs[0].Input(30);

        captured.Should().Be(30);
    }

    [Fact]
    public void TmRangeSlider_EndValueChanged_Fires_Event()
    {
        int? captured = null;
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.EndValueChanged, EventCallback.Factory.Create<int?>(this, v => captured = v)));

        var inputs = cut.FindAll("input");
        inputs[1].Input(70);

        captured.Should().Be(70);
    }

    [Fact]
    public void TmRangeSlider_StartValue_Constrained_By_EndValue()
    {
        int? startCaptured = null;
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.StartValue, 50)
            .Add(x => x.EndValue, 80)
            .Add(x => x.StartValueChanged, EventCallback.Factory.Create<int?>(this, v => startCaptured = v)));

        var inputs = cut.FindAll("input");
        // Try to set start above end - should be constrained to end. The clamp lands after the
        // forced raw-value rewrite, so the handler's continuation must flush first.
        inputs[0].Input(90);
        cut.WaitForState(() => startCaptured != null);

        startCaptured.Should().Be(80);
    }

    [Fact]
    public void TmRangeSlider_EndValue_Constrained_By_StartValue()
    {
        int? endCaptured = null;
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.StartValue, 50)
            .Add(x => x.EndValue, 80)
            .Add(x => x.EndValueChanged, EventCallback.Factory.Create<int?>(this, v => endCaptured = v)));

        var inputs = cut.FindAll("input");
        // Try to set end below start - should be constrained to start (after the forced rewrite).
        inputs[1].Input(30);
        cut.WaitForState(() => endCaptured != null);

        endCaptured.Should().Be(50);
    }

    [Fact]
    public void TmRangeSlider_Disabled_Has_Disabled_Attribute()
    {
        var cut = Render<TmRangeSlider>(p => p.Add(x => x.Disabled, true));
        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    // ── Fáze 20: the thumbs drag natively (CSS dual-thumb), no private JS gesture ──────────────

    [Fact]
    public void Inputs_Have_Accessible_Names()
    {
        var cut = Render<TmRangeSlider>(p => p.Add(x => x.Label, "Price"));

        var inputs = cut.FindAll("input[type='range']");
        inputs[0].GetAttribute("aria-label").Should().Be("Price – Minimum",
            "with a Label each thumb's name starts with it: '{Label} – Minimum/Maximum'");
        inputs[1].GetAttribute("aria-label").Should().Be("Price – Maximum");
    }

    [Fact]
    public void Thumbs_Without_A_Label_Keep_The_Bare_Names()
    {
        var cut = Render<TmRangeSlider>();

        var inputs = cut.FindAll("input[type='range']");
        inputs[0].GetAttribute("aria-label").Should().Be("Minimum");
        inputs[1].GetAttribute("aria-label").Should().Be("Maximum");
    }

    [Fact]
    public void No_Eval_Interop()
    {
        var cut = Render<TmRangeSlider>();

        JSInterop.Invocations.Should().NotContain(i => i.Identifier == "eval",
            "a strict CSP has no unsafe-eval; the thumbs drag themselves");
    }

    [Fact]
    public void StartOnTop_Class_When_Start_At_Max()
    {
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.Min, 0)
            .Add(x => x.Max, 100)
            .Add(x => x.Step, 1)
            .Add(x => x.StartValue, 100)
            .Add(x => x.EndValue, 100));

        cut.Find(".tm-range-slider").GetAttribute("class").Should().Contain("tm-range-slider--start-on-top",
            "with both thumbs at the max the start thumb must stay reachable above the end thumb");
    }

    [Fact]
    public void StartOnTop_Class_When_Start_Is_In_The_Upper_Half()
    {
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.Min, 0)
            .Add(x => x.Max, 100)
            .Add(x => x.Step, 1)
            .Add(x => x.StartValue, 80)
            .Add(x => x.EndValue, 80));

        cut.Find(".tm-range-slider").GetAttribute("class").Should().Contain("tm-range-slider--start-on-top",
            "80 is in the upper half: the start thumb lifts above the end one it parks on");
    }

    [Fact]
    public void No_StartOnTop_Class_When_Start_Is_In_The_Lower_Half()
    {
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.Min, 0)
            .Add(x => x.Max, 100)
            .Add(x => x.Step, 1)
            .Add(x => x.StartValue, 10)
            .Add(x => x.EndValue, 10));

        cut.Find(".tm-range-slider").GetAttribute("class").Should().NotContain("tm-range-slider--start-on-top",
            "10 is in the lower half: the thumbs do not overlap and need no lifting");
    }

    [Fact]
    public void No_StartOnTop_Class_When_Start_Below_Max()
    {
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.Min, 0)
            .Add(x => x.Max, 100)
            .Add(x => x.Step, 1)
            .Add(x => x.StartValue, 20)
            .Add(x => x.EndValue, 80));

        cut.Find(".tm-range-slider").GetAttribute("class").Should().NotContain("tm-range-slider--start-on-top");
    }

    [Fact]
    public void Clamped_Start_Renders_The_Clamped_Value_On_The_Input()
    {
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.Min, 0)
            .Add(x => x.Max, 100)
            .Add(x => x.StartValue, 80)
            .Add(x => x.EndValue, 80));

        var inputs = cut.FindAll("input");
        // The start is already at the end: dragging to 90 clamps back to 80, and the input's own
        // value must be rewritten to 80 (the raw 90 is rendered first so the rewrite always lands).
        inputs[0].Input(90);

        inputs[0].GetAttribute("value").Should().Be("80",
            "the input keeps the clamped value, never the out-of-range gesture value");
    }

    [Fact]
    public void Clamped_End_Renders_The_Clamped_Value_On_The_Input()
    {
        var cut = Render<TmRangeSlider>(p => p
            .Add(x => x.Min, 0)
            .Add(x => x.Max, 100)
            .Add(x => x.StartValue, 20)
            .Add(x => x.EndValue, 20));

        var inputs = cut.FindAll("input");
        inputs[1].Input(10);

        inputs[1].GetAttribute("value").Should().Be("20",
            "the end input is rewritten to the clamped value even when the state did not change");
    }
}
