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
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group").Should().NotBeNull();
    }

    [Fact]
    public void TmRadioGroup_Renders_One_Radio_Per_Option()
    {
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.FindAll("input[type='radio']").Count.Should().Be(3);
    }

    [Fact]
    public void TmRadioGroup_Label_Renders_Group_Label()
    {
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
            .Add(c => c.Label, "Pick a fruit")
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group-label").TextContent.Should().Contain("Pick a fruit");
    }

    [Fact]
    public void TmRadioGroup_Selected_Option_Has_Checked_Input()
    {
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
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
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v)));

        cut.FindAll("input[type='radio']")[0].Change(true);

        captured.Should().Be("apple");
    }

    [Fact]
    public void TmRadioGroup_Default_Layout_Is_Vertical()
    {
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group").ClassList.Should().Contain("tm-radio-group-vertical");
    }

    [Fact]
    public void TmRadioGroup_Horizontal_Layout_CssClass()
    {
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
            .Add(c => c.Layout, RadioLayout.Horizontal)
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group").ClassList.Should().Contain("tm-radio-group-horizontal");
    }

    [Fact]
    public void TmRadioGroup_Error_Shows_Error_Message()
    {
        var cut = RenderComponent<TmRadioGroup<string>>(p => p
            .Add(c => c.Error, "Selection required")
            .Add(c => c.Options, SampleOptions)
            .Add(c => c.Name, "fruit"));

        cut.Find(".tm-radio-group-error").TextContent.Should().Contain("Selection required");
    }
}
