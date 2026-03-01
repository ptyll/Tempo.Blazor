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
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 5));

        cut.Find("input[type='number']").Should().NotBeNull();
    }

    [Fact]
    public void NumberInput_DisplaysValue()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 42));

        var input = cut.Find("input[type='number']");
        input.GetAttribute("value").Should().Be("42");
    }

    [Fact]
    public void NumberInput_IncrementButton_IncreasesValue()
    {
        int? value = 5;
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__increment").Click();
        value.Should().Be(6);
    }

    [Fact]
    public void NumberInput_DecrementButton_DecreasesValue()
    {
        int? value = 5;
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__decrement").Click();
        value.Should().Be(4);
    }

    [Fact]
    public void NumberInput_Step_AppliesCustomStep()
    {
        int? value = 10;
        var cut = RenderComponent<TmNumberInput>(p => p
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
        var cut = RenderComponent<TmNumberInput>(p => p
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
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Min, 0)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<int?>(this, v => value = v)));

        cut.Find(".tm-number-input__decrement").Click();
        value.Should().Be(0); // Clamped
    }

    [Fact]
    public void NumberInput_Label_Renders()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Label, "Quantity"));

        cut.Find(".tm-input-label").TextContent.Should().Contain("Quantity");
    }

    [Fact]
    public void NumberInput_Error_Renders()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Error, "Invalid value"));

        cut.Find(".tm-input-error-message").TextContent.Should().Contain("Invalid value");
    }

    [Fact]
    public void NumberInput_Disabled_DisablesInput()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.Disabled, true));

        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void NumberInput_HideButtons_RemovesButtons()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.ShowButtons, false));

        cut.FindAll(".tm-number-input__increment").Should().BeEmpty();
        cut.FindAll(".tm-number-input__decrement").Should().BeEmpty();
    }

    [Fact]
    public void NumberInput_Prefix_Renders()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 100)
            .Add(x => x.Prefix, "$"));

        cut.Find(".tm-number-input__prefix").TextContent.Should().Contain("$");
    }

    [Fact]
    public void NumberInput_Suffix_Renders()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 30)
            .Add(x => x.Suffix, "days"));

        cut.Find(".tm-number-input__suffix").TextContent.Should().Contain("days");
    }

    [Fact]
    public void NumberInput_HelpText_Renders()
    {
        var cut = RenderComponent<TmNumberInput>(p => p
            .Add(x => x.Value, 1)
            .Add(x => x.HelpText, "Enter a number between 1 and 100"));

        cut.Find(".tm-input-help-text").TextContent.Should().Contain("Enter a number between 1 and 100");
    }
}
