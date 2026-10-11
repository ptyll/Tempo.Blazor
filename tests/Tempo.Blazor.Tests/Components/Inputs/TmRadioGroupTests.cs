using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmRadioGroup&lt;T&gt;.</summary>
public class TmRadioGroupTests : LocalizationTestBase
{
    private static List<RadioOption<string>> SampleOptions =>
    [
        new("Apple",  "apple"),
        new("Banana", "banana"),
        new("Cherry", "cherry")
    ];

    [Fact]
    public void TmRadioGroup_Renders_Wrapper_Element()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group").Should().NotBeNull();
    }

    [Fact]
    public void TmRadioGroup_Renders_One_Radio_Per_Option()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.FindAll("input[type='radio']").Count.Should().Be(3);
    }

    [Fact]
    public void TmRadioGroup_Label_Renders_Group_Label()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Label, "Pick a fruit")
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group-label").TextContent.Should().Contain("Pick a fruit");
    }

    [Fact]
    public void TmRadioGroup_Selected_Option_Has_Checked_Input()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Value, "banana")
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        var checkedInputs = cut.FindAll("input[type='radio'][checked]");
        checkedInputs.Count.Should().Be(1);
    }

    [Fact]
    public void TmRadioGroup_ValueChanged_Fires_On_Click()
    {
        string? captured = null;
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v)));

        cut.FindAll("input[type='radio']")[0].Change(true);

        captured.Should().Be("apple");
    }

    [Fact]
    public void TmRadioGroup_DefaultAppearance_RendersRadios_WithoutSegmentClass()
    {
        // Frozen default: a radio group stays radios. Segmented is opt-in.
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group").ClassList.Should().NotContain("tm-radio-group--segmented");
        cut.FindAll("input[type='radio']").Should().NotBeEmpty();
    }

    [Fact]
    public void TmRadioGroup_SegmentedAppearance_RendersButtonsInAGroup()
    {
        string? captured = null;
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit")
            .Add(c => c.Value, "banana")
            .Add(c => c.Appearance, RadioAppearance.Segmented)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v)));

        var group = cut.Find(".tm-radio-group--segmented");
        // A value, not content: a radiogroup whose pressed option is the checked native radio.
        group.GetAttribute("role").Should().Be("radiogroup");
        var segments = cut.FindAll(".tm-radio-segment");
        segments.Count.Should().Be(3);
        segments.Single(s => s.ClassList.Contains("tm-radio-segment--checked")).TextContent.Should().Contain("Banana");

        cut.FindAll(".tm-radio-segment input[type='radio']")[0].Change(true);
        captured.Should().Be("apple");
    }

    [Fact]
    public void TmRadioGroup_Default_Layout_Is_Vertical()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group").ClassList.Should().Contain("tm-radio-group-vertical");
    }

    [Fact]
    public void TmRadioGroup_Horizontal_Layout_CssClass()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Layout, RadioLayout.Horizontal)
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group").ClassList.Should().Contain("tm-radio-group-horizontal");
    }

    [Fact]
    public void TmRadioGroup_Error_Shows_Error_Message()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Error, "Selection required")
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group-error").TextContent.Should().Contain("Selection required");
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void TmRadioGroup_Required_SetsAriaRequiredOnRadiogroup()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Required, true));
        cut.Find("[role='radiogroup']").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void TmRadioGroup_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Label, "Pick a fruit")
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Required, true));
        cut.Find(".tm-radio-group-label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void TmRadioGroup_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Label, "Pick a fruit")
            .Add(c => c.Options, SampleOptions));
        cut.Find("[role='radiogroup']").HasAttribute("aria-required").Should().BeFalse();
        cut.Find(".tm-radio-group-label").ClassList.Should().NotContain("tm-input-label-required");
    }

    [Fact]
    public void TmRadioGroup_Segmented_UsesNativeRadiosSharingTheGroupName()
    {
        // L2: a segment is a label wrapping a visually hidden native radio, not a button. Native
        // radios give arrow keys, one tab stop and one activation, and they submit under Name.
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit")
            .Add(c => c.Value, "banana")
            .Add(c => c.Appearance, RadioAppearance.Segmented));

        cut.FindAll("button.tm-radio-segment").Should().BeEmpty();
        cut.FindAll(".tm-radio-segment").Should().OnlyContain(s => s.TagName == "LABEL");
        var inputs = cut.FindAll(".tm-radio-segment input[type='radio']");
        inputs.Should().HaveCount(3);
        inputs.Should().OnlyContain(i => i.GetAttribute("name") == "fruit");
        inputs.Count(i => i.HasAttribute("checked")).Should().Be(1);
    }

    [Fact]
    public void TmRadioGroup_Segmented_HomeAndEndJumpToTheEnds()
    {
        // Arrows are the browser's; Home/End are not, so the group handles them itself.
        string? captured = "banana";
        var cut = Render<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit")
            .Add(c => c.Value, "banana")
            .Add(c => c.Appearance, RadioAppearance.Segmented)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v)));

        // The key lands on the focused radio, which is where the handler lives.
        var radios = cut.FindAll(".tm-radio-segment input[type='radio']");
        radios[1].KeyDown("End");
        captured.Should().Be("cherry");

        radios[0].KeyDown("Home");
        captured.Should().Be("apple");
    }
}
