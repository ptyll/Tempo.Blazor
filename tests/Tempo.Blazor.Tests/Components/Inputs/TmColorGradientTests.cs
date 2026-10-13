using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

public class TmColorGradientTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-color-gradient.js";

    [Fact]
    public void TmColorGradient_Renders_Area_And_Sliders()
    {
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#FF0000"));

        cut.Find(".tm-color-gradient-area").Should().NotBeNull();
        cut.Find(".tm-color-gradient-hue-track").Should().NotBeNull();
        cut.Find(".tm-color-gradient-alpha-track").Should().NotBeNull();
    }

    [Fact]
    public void TmColorGradient_HideAlpha_Hides_Alpha_Slider()
    {
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#FF0000")
            .Add(c => c.ShowAlpha, false));

        cut.FindAll(".tm-color-gradient-alpha-track").Should().BeEmpty();
    }

    [Fact]
    public void TmColorGradient_Value_Red_Renders_Correct_Preview()
    {
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#FF0000"));

        var preview = cut.Find(".tm-color-gradient-preview");
        preview.GetAttribute("style")!.Should().Contain("rgba(255, 0, 0, 1)");
    }

    [Fact]
    public void TmColorGradient_ValueChanged_Fires_On_Input_Change()
    {
        string? changed = null;
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#000000")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed = v)));

        // Simulate changing red input to 255
        var redInput = cut.Find("input.tm-color-gradient-input");
        redInput.Change("255");

        changed.Should().NotBeNull();
    }

    [Theory]
    [InlineData(ColorFormat.Hex, "#FF0000")]
    [InlineData(ColorFormat.Rgb, "rgb(255, 0, 0)")]
    [InlineData(ColorFormat.Rgba, "rgba(255, 0, 0, 1)")]
    public async Task TmColorGradient_Format_Applies_To_Value(ColorFormat format, string expected)
    {
        string? changed = null;
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#FF0000")
            .Add(c => c.Format, format)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed = v)));

        // Commit hue 0 (red) through the single commit path the JS drag ends with.
        await cut.InvokeAsync(() => cut.Instance.OnGradientCommit("hue", 0, 0));

        changed.Should().StartWith(expected[..5]);
    }

    [Fact]
    public void TmColorGradient_Rgba_Input_Syncs()
    {
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "rgba(10, 20, 30, 0.5)")
            .Add(c => c.Format, ColorFormat.Rgba));

        var valueText = cut.Find(".tm-color-gradient-value-text").TextContent;
        valueText.Should().Contain("rgba(10, 20, 30, 0.5)");
    }

    [Fact]
    public void TmColorGradient_Thumb_Position_Reflects_Value()
    {
        // Pure red (saturation=1, value=1) → thumb at top-right
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#FF0000"));

        var thumb = cut.Find(".tm-color-gradient-thumb");
        var style = thumb.GetAttribute("style")!;

        // Saturation=1 → left ~100%, Value=1 → top ~0%
        style.Should().Contain("left: 100");
        style.Should().Contain("top: 0");
    }

    // ── FZE-21: focusable keyboard sliders over the shared pointer-drag helper ──────────────

    [Fact]
    public void Sliders_Are_Focusable()
    {
        var cut = Render<TmColorGradient>(p => p.Add(c => c.Value, "#FF0000"));

        foreach (var selector in new[] { ".tm-color-gradient-area", ".tm-color-gradient-hue-track", ".tm-color-gradient-alpha-track" })
        {
            var slider = cut.Find(selector);
            slider.GetAttribute("role").Should().Be("slider", selector);
            slider.GetAttribute("tabindex").Should().Be("0", selector);
            // A slider announces its value and its bounds, always.
            slider.GetAttribute("aria-valuemin").Should().NotBeNullOrEmpty(selector);
            slider.GetAttribute("aria-valuemax").Should().NotBeNullOrEmpty(selector);
            slider.GetAttribute("aria-valuenow").Should().NotBeNullOrEmpty(selector);
            slider.GetAttribute("aria-label").Should().NotBeNullOrEmpty(selector);
        }
    }

    [Fact]
    public async Task Hue_ArrowRight_Adds1_ShiftAdds10_And_Fires_ValueChanged()
    {
        string? changed = null;
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#FF0000")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed = v)));
        var hue = cut.Find(".tm-color-gradient-hue-track");

        hue.KeyDown("ArrowRight");
        changed.Should().NotBeNull("the key commits through the same path as the inputs");

        // The parent round trip: the committed value flows back through Value (like @bind-Value).
        cut.Render(p => p.Add(c => c.Value, changed));
        cut.Find(".tm-color-gradient-hue-track").GetAttribute("aria-valuenow").Should().Be("1");

        cut.Find(".tm-color-gradient-hue-track").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        cut.Render(p => p.Add(c => c.Value, changed));
        cut.Find(".tm-color-gradient-hue-track").GetAttribute("aria-valuenow").Should().Be("11", "Shift steps by 10");
    }

    [Fact]
    public async Task Area_ArrowUp_Raises_Value()
    {
        string? changed = null;
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#808080") // grey: saturation 0, value ~0.5
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed = v)));

        cut.Find(".tm-color-gradient-area").KeyDown("ArrowUp");

        changed.Should().NotBeNull();
        cut.Render(p => p.Add(c => c.Value, changed));
        var area = cut.Find(".tm-color-gradient-area");
        area.GetAttribute("aria-valuenow").Should().Be("0", "the saturation did not move");
        area.GetAttribute("aria-valuetext").Should().Contain("51", "the brightness rose by 1%");
    }

    [Fact]
    public async Task Alpha_Home_Sets_Zero()
    {
        string? changed = null;
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#FF0000")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed = v)));

        cut.Find(".tm-color-gradient-alpha-track").KeyDown("Home");

        changed.Should().NotBeNull();
        cut.Render(p => p.Add(c => c.Value, changed));
        cut.Find(".tm-color-gradient-alpha-track").GetAttribute("aria-valuenow").Should().Be("0");
    }

    [Fact]
    public async Task OnGradientDrag_Does_Not_Fire_ValueChanged()
    {
        string? changed = null;
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#000000")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed = v)));

        await cut.InvokeAsync(() => cut.Instance.OnGradientDrag("area", 0.5, 0.5));

        changed.Should().BeNull("a live drag previews without committing");
        // The preview moved the thumb to the middle of the area.
        var thumb = cut.Find(".tm-color-gradient-thumb").GetAttribute("style")!;
        thumb.Should().Contain("left: 50");
    }

    [Fact]
    public async Task OnGradientCancel_Restores_Start()
    {
        var cut = Render<TmColorGradient>(p => p.Add(c => c.Value, "#FF0000"));

        await cut.InvokeAsync(() => cut.Instance.OnGradientDrag("hue", 0.5, 0.5));
        cut.Find(".tm-color-gradient-hue-track").GetAttribute("aria-valuenow").Should().Be("180", "precondition: the drag moved the hue");

        await cut.InvokeAsync(() => cut.Instance.OnGradientCancel());

        cut.Find(".tm-color-gradient-hue-track").GetAttribute("aria-valuenow").Should().Be("0", "the cancelled drag restored the start hue");
        cut.Find(".tm-color-gradient-preview").GetAttribute("style")!.Should().Contain("rgba(255, 0, 0, 1)");
    }

    [Fact]
    public async Task OnGradientCancel_Restores_Rgb_Verbatim_Not_Through_Hsv()
    {
        // #3B82F6 sits where the HSV round trip loses a unit: RgbToHsv → HsvToRgb rebuilds it as
        // #3A82F6. The cancel must restore the captured R/G/B, never re-derive them.
        var cut = Render<TmColorGradient>(p => p.Add(c => c.Value, "#3B82F6"));
        cut.Find(".tm-color-gradient-preview").GetAttribute("style")!.Should().Contain("rgba(59, 130, 246, 1)", "precondition: the start colour parsed exactly");

        await cut.InvokeAsync(() => cut.Instance.OnGradientDrag("hue", 0.25, 0.5));
        await cut.InvokeAsync(() => cut.Instance.OnGradientCancel());

        cut.Find(".tm-color-gradient-preview").GetAttribute("style")!.Should().Contain("rgba(59, 130, 246, 1)",
            "the cancelled drag restored the R/G/B the gesture started from");
    }

    [Fact]
    public async Task Hue_ValueRoundTrip_DoesNotDrift()
    {
        string? changed = null;
        var cut = Render<TmColorGradient>(p => p
            .Add(c => c.Value, "#3B82F6")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => changed = v)));

        for (var i = 0; i < 10; i++)
        {
            cut.Find(".tm-color-gradient-hue-track").KeyDown("ArrowRight");
            // The committed value flows back through Value, exactly like @bind-Value.
            cut.Render(p => p.Add(c => c.Value, changed));
        }

        cut.Find(".tm-color-gradient-hue-track").GetAttribute("aria-valuenow").Should().Be("227",
            "ten 1° steps moved the hue from 217 to 227 — the echo re-parse must not add drift");
    }

    [Fact]
    public void HueSlider_Announces_Localized_Degree_ValueText()
    {
        var cut = Render<TmColorGradient>(p => p.Add(c => c.Value, "#3B82F6"));

        cut.Find(".tm-color-gradient-hue-track").GetAttribute("aria-valuetext").Should().Be("217°");
    }

    [Fact]
    public async Task ShowAlpha_Toggle_Reattaches_The_Gesture()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("attachGradient", _ => true).SetVoidResult();
        module.SetupVoid("detachGradient", _ => true).SetVoidResult();

        var cut = Render<TmColorGradient>(p => p.Add(c => c.Value, "#FF0000"));
        JSInterop.Invocations.Count(i => i.Identifier == "attachGradient").Should().Be(1, "attached once on the first render");

        cut.Render(p => p.Add(c => c.ShowAlpha, false));

        // The alpha track is part of the attachment: toggling it re-attaches (the module replaces
        // its own registration for the id; no explicit C#-side detach is needed).
        JSInterop.Invocations.Count(i => i.Identifier == "attachGradient").Should().Be(2);
    }
}
