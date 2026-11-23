using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmDecimalInput — phase 1: binding, parsing and culture-aware formatting.</summary>
public class TmDecimalInputTests : LocalizationTestBase
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Czech group separators are (narrow) non-breaking spaces; compare on a normalized form.</summary>
    private static string Normalize(string? value) =>
        (value ?? string.Empty).Replace(' ', ' ').Replace(' ', ' ');

    // ── Rendering ──────────────────────────────────────────────────────────────

    [Fact]
    public void DecimalInput_Renders_Wrapper()
    {
        var cut = Render<TmDecimalInput>();

        cut.Find(".tm-decimal-input").Should().NotBeNull();
        var input = cut.Find(".tm-decimal-input__input");
        input.GetAttribute("type").Should().Be("text");
        input.GetAttribute("inputmode").Should().Be("decimal");
    }

    [Fact]
    public void DecimalInput_Label_Renders()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Label, "Unit price"));

        cut.Find(".tm-input-label").TextContent.Should().Contain("Unit price");
    }

    [Fact]
    public void DecimalInput_Placeholder_Renders()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Placeholder, "0,00"));

        cut.Find(".tm-decimal-input__input").GetAttribute("placeholder").Should().Be("0,00");
    }

    [Fact]
    public void DecimalInput_PrefixAndSuffix_Render()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Prefix, "€")
            .Add(x => x.Suffix, "/ ks"));

        cut.Find(".tm-decimal-input__prefix").TextContent.Should().Contain("€");
        cut.Find(".tm-decimal-input__suffix").TextContent.Should().Contain("/ ks");
    }

    [Fact]
    public void DecimalInput_ErrorAndHelpText_Render()
    {
        var withError = Render<TmDecimalInput>(p => p
            .Add(x => x.Error, "Invalid amount")
            .Add(x => x.HelpText, "Two decimal places"));

        withError.Find(".tm-input-error-message").TextContent.Should().Contain("Invalid amount");
        withError.FindAll(".tm-input-help-text").Should().BeEmpty("error replaces the help text");
        withError.Find(".tm-decimal-input__control").ClassList.Should().Contain("tm-decimal-input__control--error");
        withError.Find(".tm-decimal-input__input").GetAttribute("aria-invalid").Should().Be("true");

        var withHelp = Render<TmDecimalInput>(p => p
            .Add(x => x.HelpText, "Two decimal places"));

        withHelp.Find(".tm-input-help-text").TextContent.Should().Contain("Two decimal places");
    }

    [Fact]
    public void DecimalInput_Disabled_DisablesInput()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Disabled, true));

        cut.Find(".tm-decimal-input__input").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-decimal-input__control").ClassList.Should().Contain("tm-decimal-input__control--disabled");
    }

    [Fact]
    public void DecimalInput_ReadOnly_MarksInputReadOnly()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.ReadOnly, true));

        cut.Find(".tm-decimal-input__input").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void DecimalInput_Class_IsAppliedToWrapper()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Class, "my-field"));

        cut.Find(".tm-decimal-input").ClassList.Should().Contain("my-field");
    }

    // ── Binding ────────────────────────────────────────────────────────────────

    [Fact]
    public void DecimalInput_Value_RendersFormattedText()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, 1234.5m)
            .Add(x => x.Culture, Czech));

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("1 234,50");
    }

    [Fact]
    public void DecimalInput_Change_InvokesValueChanged()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change("42.75");

        value.Should().Be(42.75m);
    }

    [Fact]
    public void DecimalInput_EmptyInput_ProducesNull()
    {
        decimal? value = 12m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change("   ");

        value.Should().BeNull();
    }

    [Fact]
    public void DecimalInput_InvalidInput_ProducesNull()
    {
        decimal? value = 12m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change("abc");

        value.Should().BeNull();
        cut.Find(".tm-decimal-input__input").GetAttribute("value").Should().BeEmpty();
    }

    // ── Parsing ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1234,56")]
    [InlineData("1234.56")]
    public void DecimalInput_Parses_CommaAndDotDecimalSeparator(string typed)
    {
        decimal? czechValue = null;
        var czech = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => czechValue = v)));
        czech.Find(".tm-decimal-input__input").Change(typed);
        czechValue.Should().Be(1234.56m);

        decimal? englishValue = null;
        var english = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => englishValue = v)));
        english.Find(".tm-decimal-input__input").Change(typed);
        englishValue.Should().Be(1234.56m);
    }

    [Theory]
    [InlineData("1 234,50 Kč")]
    [InlineData("1 234,50 Kč")]
    [InlineData("1.234,50 Kč")]
    public void DecimalInput_CleansPastedText(string pasted)
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change(pasted);

        value.Should().Be(1234.50m);
    }

    [Theory]
    [InlineData("$1,234.50")]
    [InlineData("1,234.50 USD")]
    public void DecimalInput_CleansPastedText_EnglishCulture(string pasted)
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change(pasted);

        value.Should().Be(1234.50m);
    }

    [Fact]
    public void DecimalInput_ParsesNegativeValues()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change("-1 234,50");

        value.Should().Be(-1234.50m);
    }

    [Fact]
    public void DecimalInput_GroupSeparatorOnly_ParsesAsThousands()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.DecimalPlaces, 2)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change("1,234");

        value.Should().Be(1234m);
    }

    // ── Rounding ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "1234,56", 1235)]
    [InlineData(1, "1234,56", 1234.6)]
    [InlineData(3, "1,2345", 1.235)]
    public void DecimalInput_RoundsToDecimalPlaces(int decimalPlaces, string typed, double expected)
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.DecimalPlaces, decimalPlaces)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change(typed);

        value.Should().Be((decimal)expected);
    }

    // ── Formatting on blur ─────────────────────────────────────────────────────

    [Fact]
    public void DecimalInput_Blur_FormatsUsingCzechCulture()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech));

        var input = cut.Find(".tm-decimal-input__input");
        input.Focus();
        input.Change("1234.5");
        input.Blur();

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("1 234,50");
    }

    [Fact]
    public void DecimalInput_Blur_FormatsUsingEnglishCulture()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English));

        var input = cut.Find(".tm-decimal-input__input");
        input.Focus();
        input.Change("1234,5");
        input.Blur();

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("1,234.50");
    }

    [Fact]
    public void DecimalInput_UseGroupingFalse_FormatsWithoutGroupSeparator()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.UseGrouping, false)
            .Add(x => x.Value, 1234.5m));

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("1234,50");
    }

    [Fact]
    public void DecimalInput_Focus_KeepsTheShownText()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Value, 1234.5m));

        cut.Find(".tm-decimal-input__input").Focus();

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value"))
            .Should().Be("1 234,50", "focus must not drop grouping so fill() can replace the shown text");
    }

    [Fact]
    public void DecimalInput_ArrowUpWhileFocused_ShowsGroupedText()
    {
        decimal? value = 1234.5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        var input = cut.Find(".tm-decimal-input__input");
        input.Focus();
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        value.Should().Be(1235.5m);
        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value"))
            .Should().Be("1 235,50", "steppers keep grouped text while focused");
    }

    [Fact]
    public void DecimalInput_FocusThenChangeGroupedText_CommitsExactValue()
    {
        decimal? value = 1234.5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        var input = cut.Find(".tm-decimal-input__input");
        input.Focus();
        input.Change("50 000,00");

        value.Should().Be(50000m);
        input.Blur();
        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("50 000,00");
    }

    [Fact]
    public void DecimalInput_Blur_ReformatsUnchangedValue()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Value, 1234.5m));

        var input = cut.Find(".tm-decimal-input__input");
        input.Focus();
        input.Blur();

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("1 234,50");
    }

    [Fact]
    public void DecimalInput_ReadOnly_KeepsFormattingOnFocus()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.ReadOnly, true)
            .Add(x => x.Value, 1234.5m));

        cut.Find(".tm-decimal-input__input").Focus();

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("1 234,50");
    }

    // ── Accessibility ──────────────────────────────────────────────────────────

    [Fact]
    public void DecimalInput_Label_IsAssociatedWithGeneratedInputId()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Label, "Unit price"));

        var inputId = cut.Find(".tm-decimal-input__input").GetAttribute("id");
        inputId.Should().NotBeNullOrEmpty();
        cut.Find(".tm-input-label").GetAttribute("for").Should().Be(inputId);
    }

    [Fact]
    public void DecimalInput_SplattedId_StaysAssociatedWithLabel()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Label, "Unit price")
            .Add(x => x.HelpText, "Two decimal places")
            .AddUnmatched("id", "unit-price"));

        cut.Find(".tm-decimal-input__input").GetAttribute("id").Should().Be("unit-price");
        cut.Find(".tm-input-label").GetAttribute("for").Should().Be("unit-price");
        cut.Find(".tm-decimal-input__input").GetAttribute("aria-describedby").Should().Be("unit-price-help");
        cut.Find(".tm-input-help-text").GetAttribute("id").Should().Be("unit-price-help");
    }

    // ── Steppers ───────────────────────────────────────────────────────────────

    [Fact]
    public void DecimalInput_Steppers_RenderByDefault_WithLocalizedAriaLabels()
    {
        var cut = Render<TmDecimalInput>();

        cut.Find(".tm-decimal-input__increment").GetAttribute("aria-label").Should().Be("Increase");
        cut.Find(".tm-decimal-input__decrement").GetAttribute("aria-label").Should().Be("Decrease");
    }

    [Fact]
    public void DecimalInput_ShowButtonsFalse_HidesSteppers()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.ShowButtons, false));

        cut.FindAll(".tm-decimal-input__increment").Should().BeEmpty();
        cut.FindAll(".tm-decimal-input__decrement").Should().BeEmpty();
    }

    [Fact]
    public void DecimalInput_Increment_AddsStep()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.Value, value)
            .Add(x => x.Step, 0.5m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__increment").Click();

        value.Should().Be(5.5m);
    }

    [Fact]
    public void DecimalInput_Decrement_SubtractsStep()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.Value, value)
            .Add(x => x.Step, 0.25m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__decrement").Click();

        value.Should().Be(4.75m);
    }

    [Fact]
    public void DecimalInput_Increment_FromNull_StartsAtStep()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Step, 1m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__increment").Click();

        value.Should().Be(1m);
    }

    [Fact]
    public void DecimalInput_Increment_FromNull_RespectsMin()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Min, 10m)
            .Add(x => x.Step, 1m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__increment").Click();

        value.Should().Be(10m);
    }

    [Fact]
    public void DecimalInput_Increment_ClampsToMax_AndDisablesButton()
    {
        decimal? value = 10m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Max, 10m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__increment").HasAttribute("disabled").Should().BeTrue();

        cut.Find(".tm-decimal-input__increment").Click();

        value.Should().Be(10m);
    }

    [Fact]
    public void DecimalInput_Decrement_ClampsToMin_AndDisablesButton()
    {
        decimal? value = 0m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Min, 0m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__decrement").HasAttribute("disabled").Should().BeTrue();

        cut.Find(".tm-decimal-input__decrement").Click();

        value.Should().Be(0m);
    }

    [Fact]
    public void DecimalInput_Steppers_DisabledWhenComponentDisabled()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Disabled, true)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__increment").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-decimal-input__decrement").HasAttribute("disabled").Should().BeTrue();

        cut.Find(".tm-decimal-input__increment").Click();

        value.Should().Be(5m, "a disabled control must not change its value");
    }

    [Fact]
    public void DecimalInput_Steppers_DoNothingWhenReadOnly()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ReadOnly, true)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__increment").Click();

        value.Should().Be(5m);
    }

    [Fact]
    public void DecimalInput_Change_ClampsToRange()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.Min, 0m)
            .Add(x => x.Max, 100m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        var input = cut.Find(".tm-decimal-input__input");
        input.Change("150");
        value.Should().Be(100m);

        input.Change("-20");
        value.Should().Be(0m);
    }

    // ── Keyboard ───────────────────────────────────────────────────────────────

    [Fact]
    public void DecimalInput_ArrowUp_Increments()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Step, 0.5m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        value.Should().Be(5.5m);
    }

    [Fact]
    public void DecimalInput_ArrowDown_Decrements()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.Step, 0.5m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        value.Should().Be(4.5m);
    }

    [Fact]
    public void DecimalInput_ArrowKeys_IgnoredWhenReadOnly()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ReadOnly, true)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        value.Should().Be(5m);
    }

    [Fact]
    public void DecimalInput_ArrowUp_StepsFromUncommittedText()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.Value, value)
            .Add(x => x.Step, 1m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        var input = cut.Find(".tm-decimal-input__input");
        input.Focus();
        input.Input("50");                                    // typed, change has not fired yet
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        value.Should().Be(51m, "the arrow key must step from what the user just typed");
    }

    [Fact]
    public void DecimalInput_OtherKeys_LeaveValueAlone()
    {
        decimal? value = 5m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").KeyDown(new KeyboardEventArgs { Key = "a" });

        value.Should().Be(5m);
    }

    // ── Percent mode ───────────────────────────────────────────────────────────

    [Fact]
    public void DecimalInput_Percent_DisplaysValueScaledByHundred()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Percent, true)
            .Add(x => x.DecimalPlaces, 0)
            .Add(x => x.Value, 0.15m));

        cut.Find(".tm-decimal-input__input").GetAttribute("value").Should().Be("15");
        cut.Find(".tm-decimal-input__suffix").TextContent.Should().Contain("%");
    }

    [Fact]
    public void DecimalInput_Percent_UsesDecimalPlacesOnTheDisplayedNumber()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Percent, true)
            .Add(x => x.Value, 0.15m));

        Normalize(cut.Find(".tm-decimal-input__input").GetAttribute("value")).Should().Be("15,00");
    }

    [Fact]
    public void DecimalInput_Percent_Change_StoresFraction()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Percent, true)
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change("12,5");

        value.Should().Be(0.125m);
    }

    [Fact]
    public void DecimalInput_Percent_Steppers_WorkInPercentScale()
    {
        decimal? value = 0.15m;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, Czech)
            .Add(x => x.Percent, true)
            .Add(x => x.Step, 1m)
            .Add(x => x.Value, value)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__increment").Click();

        value.Should().Be(0.16m);
    }

    [Fact]
    public void DecimalInput_Percent_MinMax_AreInPercentScale()
    {
        decimal? value = null;
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Culture, English)
            .Add(x => x.Percent, true)
            .Add(x => x.Min, 0m)
            .Add(x => x.Max, 100m)
            .Add(x => x.ValueChanged, EventCallback.Factory.Create<decimal?>(this, v => value = v)));

        cut.Find(".tm-decimal-input__input").Change("150");

        value.Should().Be(1m, "150 % is clamped to the 100 % maximum, stored as the fraction 1.0");
    }

    [Fact]
    public void DecimalInput_Percent_ExplicitSuffix_Wins()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Percent, true)
            .Add(x => x.Suffix, "pct")
            .Add(x => x.Value, 0.15m));

        cut.Find(".tm-decimal-input__suffix").TextContent.Should().Contain("pct");
    }

    [Fact]
    public void DecimalInput_NonPercent_HasNoAutomaticSuffix()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(x => x.Value, 0.15m));

        cut.FindAll(".tm-decimal-input__suffix").Should().BeEmpty();
    }

    // ── Helpers (static, culture aware) ────────────────────────────────────────

    [Fact]
    public void ParseDecimal_ReturnsNull_ForNullOrWhitespaceOrGarbage()
    {
        TmDecimalInput.ParseDecimal(null, Czech).Should().BeNull();
        TmDecimalInput.ParseDecimal("", Czech).Should().BeNull();
        TmDecimalInput.ParseDecimal("   ", Czech).Should().BeNull();
        TmDecimalInput.ParseDecimal("abc", Czech).Should().BeNull();
        TmDecimalInput.ParseDecimal("-", Czech).Should().BeNull();
        TmDecimalInput.ParseDecimal(",", Czech).Should().BeNull();
    }

    [Fact]
    public void ParseDecimal_HandlesMixedSeparators()
    {
        TmDecimalInput.ParseDecimal("1.234.567,89", Czech).Should().Be(1234567.89m);
        TmDecimalInput.ParseDecimal("1,234,567.89", English).Should().Be(1234567.89m);
        TmDecimalInput.ParseDecimal("1 234 567,89", Czech).Should().Be(1234567.89m);
    }

    [Fact]
    public void FormatDecimal_RespectsCultureGroupingAndPlaces()
    {
        TmDecimalInput.FormatDecimal(null, 2, true, Czech).Should().BeEmpty();
        Normalize(TmDecimalInput.FormatDecimal(1234.5m, 2, true, Czech)).Should().Be("1 234,50");
        Normalize(TmDecimalInput.FormatDecimal(1234.5m, 2, true, English)).Should().Be("1,234.50");
        Normalize(TmDecimalInput.FormatDecimal(1234.5m, 0, true, English)).Should().Be("1,235");
        Normalize(TmDecimalInput.FormatDecimal(1234.5m, 2, false, Czech)).Should().Be("1234,50");
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void DecimalInput_Required_SetsAriaRequiredOnInput()
    {
        var cut = Render<TmDecimalInput>(p => p.Add(c => c.Required, true));
        cut.Find("input").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void DecimalInput_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmDecimalInput>(p => p
            .Add(c => c.Label, "Amount")
            .Add(c => c.Required, true));
        cut.Find("label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void DecimalInput_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmDecimalInput>(p => p.Add(c => c.Label, "Amount"));
        cut.Find("input").HasAttribute("aria-required").Should().BeFalse();
        cut.Find("label").ClassList.Should().NotContain("tm-input-label-required");
    }
}
