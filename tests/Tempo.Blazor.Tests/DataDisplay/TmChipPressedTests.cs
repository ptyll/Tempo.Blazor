using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// TDD (phase 15b carry-forwards): the TmChip TOGGLE variant. <c>Pressed</c> is additive and
/// nullable — unset keeps today's <c>&lt;span&gt;</c> DOM untouched; set, the chip renders a
/// NATIVE <c>&lt;button type="button" aria-pressed&gt;</c> carrying the same classes plus
/// <c>tm-chip--pressed</c>. Keyboard follows docs/keyboard-activation-convention.md: a native
/// button is activated by the browser itself — no Enter/Space emulation, exactly one
/// activation per press.
/// </summary>
public class TmChipPressedTests : LocalizationTestBase
{
    [Fact]
    public void Pressed_null_keeps_span_dom()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Tag"));

        cut.Find(".tm-chip").TagName.Should().Be("SPAN",
            "unset Pressed keeps the legacy span root (default = today's behavior)");
    }

    [Fact]
    public void Pressed_renders_native_button_with_aria_pressed()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Error")
            .Add(x => x.Pressed, true));

        var chip = cut.Find("button.tm-chip");
        chip.TagName.Should().Be("BUTTON", "a pressed chip is a native toggle button");
        chip.GetAttribute("type").Should().Be("button");
        chip.GetAttribute("aria-pressed").Should().Be("true");
        chip.ClassList.Should().Contain("tm-chip--pressed",
            "the pressed variant carries its own class for the core stylesheet");
        chip.ClassList.Should().Contain("tm-chip--soft", "the visual classes are unchanged");
    }

    [Fact]
    public void Pressed_false_renders_aria_pressed_false_button()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Error")
            .Add(x => x.Pressed, false));

        var chip = cut.Find("button.tm-chip");
        chip.GetAttribute("aria-pressed").Should().Be("false",
            "any non-null Pressed renders the button; the VALUE lands on aria-pressed");
    }

    [Fact]
    public void Click_raises_PressedChanged_with_toggled_value()
    {
        bool? raised = null;
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Error")
            .Add(x => x.Pressed, true)
            .Add(x => x.PressedChanged, EventCallback.Factory.Create<bool>(this, value => raised = value)));

        cut.Find("button.tm-chip").Click();

        raised.Should().BeFalse("clicking a pressed chip raises the toggled value");
    }

    [Fact]
    public void Disabled_pressed_chip_is_disabled_button()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Error")
            .Add(x => x.Pressed, true)
            .Add(x => x.Disabled, true));

        var chip = cut.Find("button.tm-chip");
        chip.HasAttribute("disabled").Should().BeTrue("a disabled pressed chip is a disabled button");
    }

    [Fact]
    public void Removable_and_pressed_do_not_nest_buttons()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Pressed, true)
            .Add(x => x.Removable, true));

        // HTML forbids a <button> inside a <button>: the remove action stays a SIBLING
        // of the pressed toggle, never a descendant.
        cut.FindAll("button.tm-chip button").Should().BeEmpty("no nested button inside the pressed chip");
        cut.FindAll(".tm-chip__remove").Should().HaveCount(1, "the remove action still renders");
        cut.Find(".tm-chip__remove").ParentElement!.QuerySelector("button.tm-chip")
            .Should().NotBeNull("the pressed button and the remove button share the wrapper");
    }

    [Fact]
    public void Removable_pressed_still_raises_OnRemove()
    {
        bool removed = false;
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Pressed, true)
            .Add(x => x.Removable, true)
            .Add(x => x.OnRemove, EventCallback.Factory.Create(this, () => removed = true)));

        cut.Find(".tm-chip__remove").Click();

        removed.Should().BeTrue("the sibling remove button keeps its own handler");
    }

    [Fact]
    public void Removable_pressed_routes_aria_and_testid_to_the_toggle_button()
    {
        // Phase 15b review: assistive tech and host/E2E selectors must reach the ACTIVATABLE
        // element — aria-* and data-testid route to the pressed button, the rest stay on the
        // shell wrapper (the legacy "root" position).
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Pressed, true)
            .Add(x => x.Removable, true)
            .AddUnmatched("data-testid", "chip-tag")
            .AddUnmatched("aria-describedby", "hint-1")
            .AddUnmatched("id", "chip-shell"));

        var button = cut.Find("button.tm-chip");
        button.GetAttribute("data-testid").Should().Be("chip-tag",
            "data-testid is routed to the toggle button");
        button.GetAttribute("aria-describedby").Should().Be("hint-1",
            "aria-* attributes are routed to the toggle button");

        var shell = cut.Find(".tm-chip__shell");
        shell.GetAttribute("id").Should().Be("chip-shell",
            "non-aria/testid attributes stay on the shell wrapper");
        shell.HasAttribute("data-testid").Should().BeFalse(
            "the routed attribute must not be duplicated on the shell");
    }

    [Fact]
    public void Removable_pressed_without_routed_attributes_keeps_shell_clean()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Pressed, true)
            .Add(x => x.Removable, true));

        cut.Find(".tm-chip__shell").Attributes.Should().NotContain(a => a.Name == "data-testid");
        cut.Find("button.tm-chip").HasAttribute("aria-describedby").Should().BeFalse();
    }

    [Fact]
    public void Pressed_native_button_does_not_emulate_enter()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Error")
            .Add(x => x.Pressed, true));

        // A native button turns Enter/Space into clicks ITSELF — an @onkeydown emulation
        // would double-fire (keyboard-activation-convention). bUnit proves the absence:
        // dispatching a keydown finds no handler at all.
        var act = () => cut.Find("button.tm-chip").KeyDown("Enter");
        act.Should().Throw<MissingEventHandlerException>(
            "the pressed chip is a native activatable element — never emulated");
    }

    [Fact]
    public void Keyboard_activation_runs_exactly_once_per_press()
    {
        var toggles = 0;
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Error")
            .Add(x => x.Pressed, true)
            .Add(x => x.PressedChanged, EventCallback.Factory.Create<bool>(this, _ => toggles++)));

        // The real browser order for Enter on a native element: the keydown does nothing
        // (no handler — proven above), the click it produces fires ONCE.
        cut.Find("button.tm-chip").Click();

        toggles.Should().Be(1, "exactly one activation per press");
    }

    [Fact]
    public void ChildContent_rides_after_the_label_in_the_accessible_name()
    {
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Error")
            .Add(x => x.Pressed, true)
            .AddChildContent("<span class=\"chip-count\">2</span>"));

        var chip = cut.Find("button.tm-chip");
        chip.TextContent.Should().Contain("Error").And.Contain("2",
            "trailing child content (a count) belongs to the chip's accessible name");
        cut.Find("button.tm-chip .tm-chip__label").TextContent.Should().Be("Error");
    }

    [Fact]
    public void Unset_pressed_keeps_clickable_span_path()
    {
        var clicks = 0;
        using var cut = Render<TmChip>(p => p
            .Add(x => x.Label, "Tag")
            .Add(x => x.Clickable, true)
            .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicks++)));

        cut.Find("span.tm-chip").Click();

        clicks.Should().Be(1, "the legacy span click path is untouched");
    }
}
