using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmToggle.</summary>
public class TmToggleTests : LocalizationTestBase
{
    [Fact]
    public void TmToggle_Renders_Checkbox_Input()
    {
        var cut = Render<TmToggle>();
        cut.Find("input[type='checkbox']").Should().NotBeNull();
    }

    [Fact]
    public void TmToggle_Has_Wrapper_CssClass()
    {
        var cut = Render<TmToggle>();
        cut.Find(".tm-toggle-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void TmToggle_Checked_Adds_Checked_CssClass()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.Value, true));
        cut.Find(".tm-toggle-wrapper").ClassList.Should().Contain("tm-toggle-checked");
    }

    [Fact]
    public void TmToggle_Unchecked_Does_Not_Have_Checked_CssClass()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.Value, false));
        cut.Find(".tm-toggle-wrapper").ClassList.Should().NotContain("tm-toggle-checked");
    }

    [Fact]
    public void TmToggle_Disabled_Adds_Disabled_CssClass()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.Disabled, true));
        cut.Find(".tm-toggle-wrapper").ClassList.Should().Contain("tm-toggle-disabled");
    }

    [Fact]
    public void TmToggle_Label_Renders_Label_Text()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.Label, "Dark mode"));
        cut.Find(".tm-toggle-label-text").TextContent.Should().Contain("Dark mode");
    }

    [Fact]
    public void TmToggle_No_Label_Text_When_Null()
    {
        var cut = Render<TmToggle>();
        cut.FindAll(".tm-toggle-label-text").Should().BeEmpty();
    }

    [Fact]
    public void TmToggle_ValueChanged_Fires_On_Change()
    {
        bool? captured = null;
        var cut = Render<TmToggle>(p => p
            .Add(c => c.Value, false)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<bool>(this, v => captured = v)));

        cut.Find("input[type='checkbox']").Change(true);

        captured.Should().BeTrue();
    }

    [Fact]
    public void TmToggle_Space_Sequence_Fires_ValueChanged_Exactly_Once()
    {
        // A native <input type="checkbox"> already toggles from Space: keydown,
        // keyup, then the checked state flips and the browser fires "change" —
        // HandleChangeAsync reports it via ValueChanged. A keydown handler that
        // also invokes ValueChanged(!Value) makes one press report twice.
        var invocations = new List<bool>();
        var cut = Render<ToggleHost>(p => p
            .Add(h => h.Value, false)
            .Add(h => h.ValueChanged,
                EventCallback.Factory.Create<bool>(this, v => invocations.Add(v))));

        var input = cut.Find("input[type='checkbox']");
        // Real browser sequence for Space on a focused checkbox:
        // keydown -> keyup -> native checked flip -> change event.
        input.KeyDown(" ");
        input.KeyUp(" ");
        input.Change(true);

        invocations.Should().Equal(true);
    }

    /// <summary>
    /// Hosts a <see cref="TmToggle"/> inside a div that carries no-op key
    /// handlers so bUnit can dispatch keydown/keyup on the input (bUnit
    /// requires a handler on the target or an ancestor; real browsers bubble
    /// key events to the document regardless).
    /// </summary>
    private sealed class ToggleHost : ComponentBase
    {
        [Parameter] public bool Value { get; set; }
        [Parameter] public EventCallback<bool> ValueChanged { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "onkeydown",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));
            builder.AddAttribute(2, "onkeyup",
                EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));

            builder.OpenComponent<TmToggle>(3);
            builder.AddAttribute(4, "Value", Value);
            builder.AddAttribute(5, "ValueChanged", ValueChanged);
            builder.CloseComponent();

            builder.CloseElement();
        }
    }

    // ── Accessible name without visible text ──────────────────────
    //
    // Label fills the visible span *and* the input's aria-label at once, and AdditionalAttributes splat
    // onto the wrapper div, so a host that wants the switch named by its own text — or that must keep the
    // switch alone inside a data-testid element — had no way to name the input. AriaLabel, AriaLabelledBy
    // and Id close that gap; all three land on the input, not on the wrapper.

    [Fact]
    public void TmToggle_AriaLabel_Names_The_Input()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.AriaLabel, "Email notifications"));

        cut.Find("input[type='checkbox']").GetAttribute("aria-label").Should().Be("Email notifications");
    }

    [Fact]
    public void TmToggle_AriaLabel_Does_Not_Land_On_The_Wrapper()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.AriaLabel, "Email notifications"));

        cut.Find(".tm-toggle-wrapper").HasAttribute("aria-label").Should().BeFalse();
    }

    [Fact]
    public void TmToggle_AriaLabel_Does_Not_Render_Visible_Text()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.AriaLabel, "Email notifications"));

        cut.FindAll(".tm-toggle-label-text").Should().BeEmpty();
    }

    [Fact]
    public void TmToggle_AriaLabel_Overrides_The_Label_Derived_Name()
    {
        var cut = Render<TmToggle>(p => p
            .Add(c => c.Label, "Email")
            .Add(c => c.AriaLabel, "Email notifications"));

        cut.Find("input[type='checkbox']").GetAttribute("aria-label").Should().Be("Email notifications");
        cut.Find(".tm-toggle-label-text").TextContent.Should().Contain("Email");
    }

    [Fact]
    public void TmToggle_AriaLabelledBy_Lands_On_The_Input()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.AriaLabelledBy, "channel-email-label"));

        cut.Find("input[type='checkbox']").GetAttribute("aria-labelledby").Should().Be("channel-email-label");
        cut.Find(".tm-toggle-wrapper").HasAttribute("aria-labelledby").Should().BeFalse();
    }

    [Fact]
    public void TmToggle_No_AriaLabelledBy_Attribute_When_Not_Set()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.Label, "Email"));

        cut.Find("input[type='checkbox']").HasAttribute("aria-labelledby").Should().BeFalse();
    }

    [Fact]
    public void TmToggle_Id_Is_Applied_To_The_Input_So_An_External_Label_Can_Point_At_It()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.Id, "channel-email"));

        cut.Find("input[type='checkbox']").GetAttribute("id").Should().Be("channel-email");
        cut.Find(".tm-toggle-wrapper").HasAttribute("id").Should().BeFalse();
    }

    [Fact]
    public void TmToggle_No_Id_Attribute_When_Id_Is_Null()
    {
        var cut = Render<TmToggle>();

        cut.Find("input[type='checkbox']").HasAttribute("id").Should().BeFalse();
    }

    [Fact]
    public void TmToggle_Label_Still_Sets_AriaLabel_On_The_Input()
    {
        // Released behaviour, kept: Label alone still names the switch.
        var cut = Render<TmToggle>(p => p.Add(c => c.Label, "Dark mode"));

        cut.Find("input[type='checkbox']").GetAttribute("aria-label").Should().Be("Dark mode");
    }

    [Fact]
    public void TmToggle_No_AriaLabel_Attribute_When_Nothing_Names_It()
    {
        var cut = Render<TmToggle>();

        cut.Find("input[type='checkbox']").HasAttribute("aria-label").Should().BeFalse();
    }

    [Fact]
    public void TmToggle_AdditionalAttributes_Still_Splat_Onto_The_Wrapper()
    {
        // Released behaviour, kept: data-testid must stay on the wrapper so a host can address the switch.
        var cut = Render<TmToggle>(p => p.AddUnmatched("data-testid", "channel-email-toggle"));

        cut.Find(".tm-toggle-wrapper").GetAttribute("data-testid").Should().Be("channel-email-toggle");
    }

    // ── Required ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TmToggle_Required_MarksTheVisibleLabelAndSetsAriaRequired()
    {
        var cut = Render<TmToggle>(p => p
            .Add(c => c.Label, "Accept terms")
            .Add(c => c.Required, true));

        cut.Find(".tm-toggle-label-text").ClassList.Should().Contain("tm-input-label-required");
        cut.Find("input[type='checkbox']").GetAttribute("aria-required").Should().Be("true");
    }

    /// <summary>
    /// Deliberate asymmetry with TmCheckbox: on a checkbox the native `required` means "must be ticked",
    /// which is right for an "I agree" box and wrong for a switch whose off state is a legitimate answer —
    /// it would refuse to submit the form. Required on a switch says the choice must be made, and the form
    /// validates the value.
    /// </summary>
    [Fact]
    public void TmToggle_Required_DoesNotEmitTheNativeRequiredAttribute()
    {
        var cut = Render<TmToggle>(p => p
            .Add(c => c.Label, "Accept terms")
            .Add(c => c.Required, true));

        cut.Find("input[type='checkbox']").HasAttribute("required").Should().BeFalse();
    }

    [Fact]
    public void TmToggle_NotRequired_LeavesNoRequiredMarkers()
    {
        var cut = Render<TmToggle>(p => p.Add(c => c.Label, "Dark mode"));

        cut.Find(".tm-toggle-label-text").ClassList.Should().NotContain("tm-input-label-required");
        cut.Find("input[type='checkbox']").HasAttribute("aria-required").Should().BeFalse();
    }
}
