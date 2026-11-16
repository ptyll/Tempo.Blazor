using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmNumberInput.</summary>
public class TmNumberInputTests : LocalizationTestBase
{
    [Fact]
    public void NumberInput_Renders()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 5));

        cut.Find("input[type='number']").Should().NotBeNull();
    }

    [Fact]
    public void NumberInput_DisplaysValue()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 42));

        var input = cut.Find("input[type='number']");
        input.GetAttribute("value").Should().Be("42");
    }

    [Fact]
    public void NumberInput_IncrementButton_IncreasesValue()
    {
        int? value = 5;
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__increment").Click();
        value.Should().Be(6);
    }

    [Fact]
    public void NumberInput_DecrementButton_DecreasesValue()
    {
        int? value = 5;
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__decrement").Click();
        value.Should().Be(4);
    }

    [Fact]
    public void NumberInput_Step_AppliesCustomStep()
    {
        int? value = 10;
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Step, 5)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__increment").Click();
        value.Should().Be(15);
    }

    [Fact]
    public void NumberInput_Max_ClampsValue()
    {
        int? value = 10;
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Max, 10)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__increment").Click();
        value.Should().Be(10); // Clamped
    }

    [Fact]
    public void NumberInput_Min_ClampsValue()
    {
        int? value = 0;
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Min, 0)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__decrement").Click();
        value.Should().Be(0); // Clamped
    }

    [Fact]
    public void NumberInput_Label_Renders()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Label, "Quantity"));

        cut.Find(".tm-input-label").TextContent.Should().Contain("Quantity");
    }

    [Fact]
    public void NumberInput_Error_Renders()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Error, "Invalid value"));

        cut.Find(".tm-input-error-message").TextContent.Should().Contain("Invalid value");
    }

    [Fact]
    public void NumberInput_Disabled_DisablesInput()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Disabled, true));

        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void NumberInput_HideButtons_RemovesButtons()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.ShowButtons, false));

        cut.FindAll(".tm-number-input__increment").Should().BeEmpty();
        cut.FindAll(".tm-number-input__decrement").Should().BeEmpty();
    }

    [Fact]
    public void NumberInput_Prefix_Renders()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 100)
            .Add(x => x.Prefix, "$"));

        cut.Find(".tm-number-input__prefix").TextContent.Should().Contain("$");
    }

    [Fact]
    public void NumberInput_Suffix_Renders()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 30)
            .Add(x => x.Suffix, "days"));

        cut.Find(".tm-number-input__suffix").TextContent.Should().Contain("days");
    }

    [Fact]
    public void NumberInput_HelpText_Renders()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.HelpText, "Enter a number between 1 and 100"));

        cut.Find(".tm-input-help-text").TextContent.Should().Contain("Enter a number between 1 and 100");
    }

    // ── Required ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TmNumberInput had no way to say the field is mandatory, so a form mixing it with TmTextInput
    /// marked some of its required fields and silently not others.
    /// </summary>
    [Fact]
    public void NumberInput_Required_MarksLabelAndInput()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Label, "Retention")
            .Add(x => x.Required, true));

        cut.Find("label").ClassList.Should().Contain("tm-input-label-required");
        cut.Find("input").HasAttribute("required").Should().BeTrue();
        cut.Find("input").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void NumberInput_NotRequired_LeavesNoRequiredMarkers()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Label, "Retention"));

        cut.Find("label").ClassList.Should().NotContain("tm-input-label-required");
        cut.Find("input").HasAttribute("required").Should().BeFalse();
        cut.Find("input").GetAttribute("aria-required").Should().BeNull();
    }

    /// <summary>An asterisk on a label that names nothing is decoration, so the label got a `for`.</summary>
    [Fact]
    public void NumberInput_Label_IsAssociatedWithTheInput()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Label, "Retention"));

        var id = cut.Find("input").GetAttribute("id");
        id.Should().NotBeNullOrEmpty();
        cut.Find("label").GetAttribute("for").Should().Be(id);
    }

    [Fact]
    public void NumberInput_ExplicitId_DrivesTheLabelAndTheDescribedByTargets()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Label, "Retention")
            .Add(x => x.Id, "retention-days")
            .Add(x => x.Error, "Too small"));

        cut.Find("input").GetAttribute("id").Should().Be("retention-days");
        cut.Find("label").GetAttribute("for").Should().Be("retention-days");
        cut.Find("input").GetAttribute("aria-describedby").Should().Be("retention-days-error");
        cut.Find(".tm-input-error-message").GetAttribute("id").Should().Be("retention-days-error");
    }

    // ── AriaDescribedBy + LabelTrailing (plan 98c80bc3, Fáze 3) ─────────────

    [Fact]
    public void NumberInput_AriaDescribedBy_AppendsToTheOwnChain()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.AriaDescribedBy, "days-hint"));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be("days-hint");

        var withError = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Error, "Too small")
            .Add(x => x.AriaDescribedBy, "days-hint"));

        withError.Find("input").GetAttribute("aria-describedby")
            .Should().Be($"{withError.Instance.Id}-error days-hint",
                "the own chain stays first, the host ids follow");
    }

    [Fact]
    public void NumberInput_LabelTrailing_RendersAtTheEndOfTheLabelRow()
    {
        var cut = Render<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Label, "Retention")
            .Add(x => x.LabelTrailing, "<span id='retention-unit'>days</span>"));

        var row = cut.Find(".tm-input-label-row");
        row.QuerySelector("label")!.TextContent.Trim().Should().Be("Retention");
        row.QuerySelector("#retention-unit")!.TextContent.Should().Be("days");
    }

    /// <summary>
    /// Review round (plan 98c80bc3, Fáze 3, S2): the generated default id is the readable sequential
    /// <c>tm-numberinput-{n}</c>, stable for the instance and distinct per instance — the scheme
    /// TmTextInput and TmSelect already use.
    /// </summary>
    [Fact]
    public void NumberInput_DefaultId_IsSequentialAndStableForTheInstance()
    {
        var cut = Render<TmNumberInput>(p => p.Add(x => x.Value, 1));

        var first = cut.Instance.Id;
        first.Should().MatchRegex("^tm-numberinput-\\d+$",
            "the default id is the component prefix plus a sequence number, not a GUID fragment");

        cut.Render(p => p.Add(x => x.Value, 2));
        cut.Instance.Id.Should().Be(first, "the id never churns between parameter snapshots");

        Render<TmNumberInput>(p => p.Add(x => x.Value, 1)).Instance.Id.Should().NotBe(first);
    }
}
