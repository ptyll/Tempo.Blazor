using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Inputs;

public class TmColorPickerTests : LocalizationTestBase
{
    [Fact]
    public void TmColorPicker_Renders_Trigger()
    {
        var cut = Render<TmColorPicker>();

        cut.Find(".tm-color-picker-trigger").Should().NotBeNull();
        cut.Find(".tm-color-picker-trigger-bg").Should().NotBeNull();
    }

    [Fact]
    public void TmColorPicker_Empty_Value_Shows_Placeholder()
    {
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.Placeholder, "Pick a color");
        });

        cut.Find(".tm-color-picker-trigger-text").TextContent.Trim().Should().Be("Pick a color");
    }

    [Fact]
    public void TmColorPicker_Value_Shows_Value()
    {
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.Value, "#FF5733");
        });

        var text = cut.Find(".tm-color-picker-trigger-text").TextContent.Trim();
        text.Should().Be("#FF5733");
    }

    [Fact]
    public void TmColorPicker_Click_Opens_Dropdown()
    {
        var cut = Render<TmColorPicker>();

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();

        cut.Find(".tm-color-picker-trigger").Click();

        cut.FindAll(".tm-color-picker-dropdown").Should().HaveCount(1);
        cut.Find(".tm-flat-color-picker").Should().NotBeNull();
    }

    [Fact]
    public void TmColorPicker_TriggerKeyboard_TogglesDropdownAndExposesLowercaseExpandedState()
    {
        var cut = Render<TmColorPicker>();
        var trigger = cut.Find(".tm-color-picker-trigger");

        trigger.GetAttribute("aria-expanded").Should().Be("false");

        trigger.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.FindAll(".tm-color-picker-dropdown").Should().HaveCount(1);
        trigger = cut.Find(".tm-color-picker-trigger");
        trigger.GetAttribute("aria-expanded").Should().Be("true");

        // Space activates on KEYUP, matching the native <button> contract
        // (docs/keyboard-activation-convention.md) — the keydown itself must not toggle.
        trigger.KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-color-picker-dropdown").Should().HaveCount(1,
            "Space keydown must not toggle — activation lives on the release");
        // Re-find: the keydown completed with a re-render, rotating handler IDs.
        cut.Find(".tm-color-picker-trigger").KeyUp(new KeyboardEventArgs { Key = " " });

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();
        cut.Find(".tm-color-picker-trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void TmColorPicker_RepeatedEnterKeydown_DoesNotToggle()
    {
        // A held Enter emits auto-repeat keydowns; the guard bounds activation to the real
        // press so holding the key cannot open-and-close the dropdown in one gesture.
        var cut = Render<TmColorPicker>();

        cut.Find(".tm-color-picker-trigger").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();
    }

    [Fact]
    public void TmColorPicker_Open_Adds_Open_Class()
    {
        var cut = Render<TmColorPicker>();

        cut.Find(".tm-color-picker").ClassList.Should().NotContain("tm-color-picker--open");

        cut.Find(".tm-color-picker-trigger").Click();

        cut.Find(".tm-color-picker").ClassList.Should().Contain("tm-color-picker--open");
    }

    [Fact]
    public void TmColorPicker_Selection_Closes_Dropdown()
    {
        var selectedValue = string.Empty;
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => selectedValue = v ?? string.Empty));
        });

        cut.Find(".tm-color-picker-trigger").Click();
        cut.Find(".tm-color-palette-swatch").Click();

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();
    }

    [Fact]
    public void TmColorPicker_ShowAlpha_False_Passed_To_Gradient()
    {
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.ShowAlpha, false);
        });

        cut.Find(".tm-color-picker-trigger").Click();

        var gradient = cut.FindComponent<TmFlatColorPicker>();
        gradient.Instance.ShowAlpha.Should().BeFalse();
    }

    [Fact]
    public void TmColorPicker_ShowApplyButton_Renders_Apply_Button()
    {
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.ShowApplyButton, true);
        });

        cut.Find(".tm-color-picker-trigger").Click();

        var applyBtn = cut.Find(".tm-color-picker-apply");
        applyBtn.Should().NotBeNull();
        applyBtn.TextContent.Trim().Should().Be("Apply");
    }

    [Fact]
    public void TmColorPicker_ShowApplyButton_Selection_Does_Not_Close_Dropdown()
    {
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.ShowApplyButton, true);
        });

        cut.Find(".tm-color-picker-trigger").Click();
        cut.Find(".tm-color-palette-swatch").Click();

        cut.FindAll(".tm-color-picker-dropdown").Should().HaveCount(1);
    }

    [Fact]
    public void TmColorPicker_ShowApplyButton_Apply_Closes_Dropdown_And_Fires_ValueChanged()
    {
        string? changed = null;
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.ShowApplyButton, true);
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => changed = v));
        });

        cut.Find(".tm-color-picker-trigger").Click();
        cut.Find(".tm-color-palette-swatch").Click();
        cut.Find(".tm-color-picker-apply").Click();

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();
        changed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TmColorPicker_ShowCancelButton_ClosesDropdownWithoutFiringValueChanged()
    {
        string? changed = null;
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.Value, "#112233");
            parameters.Add(p => p.ShowApplyButton, true);
            parameters.Add(p => p.ShowCancelButton, true);
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => changed = v));
        });

        cut.Find(".tm-color-picker-trigger").Click();
        cut.Find(".tm-color-palette-swatch").Click();
        cut.Find(".tm-color-picker-cancel").Click();

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();
        changed.Should().BeNull();
        cut.Find(".tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#112233");
    }

    [Fact]
    public async Task TmColorPicker_Escape_ClosesDropdownWithoutFiringValueChanged()
    {
        string? changed = null;
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.Value, "#112233");
            parameters.Add(p => p.ShowApplyButton, true);
            parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, v => changed = v));
        });

        cut.Find(".tm-color-picker-trigger").Click();
        cut.Find(".tm-color-palette-swatch").Click();

        // A bubbled Escape keydown must not close on its own: overlay.js consumes Escape at the
        // window capture phase and delivers it through NotifyDismissedAsync — the component's
        // own keydown branches were dead code in a real browser (dead-branch sweep).
        cut.Find(".tm-color-picker-trigger").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-color-picker-dropdown").Should().HaveCount(1,
            "a bubbled Escape keydown is not the dismissal path — overlay.js owns it");

        var overlay = cut.FindComponent<Tempo.Blazor.Components.Overlay.TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();
        changed.Should().BeNull();
        cut.Find(".tm-color-picker-trigger-text").TextContent.Trim().Should().Be("#112233");
    }

    [Fact]
    public void TmColorPicker_Disabled_DoesNotOpen()
    {
        var cut = Render<TmColorPicker>(parameters =>
        {
            parameters.Add(p => p.Disabled, true);
        });

        cut.Find(".tm-color-picker-trigger").Click();

        cut.FindAll(".tm-color-picker-dropdown").Should().BeEmpty();
        cut.Find(".tm-color-picker-trigger").GetAttribute("aria-disabled").Should().Be("true");
    }
}
