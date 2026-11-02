using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningDateStep (Signing redesign phase 4, SI1 exception): the input stays the NATIVE
/// <c>&lt;input type="date|month|datetime-local"&gt;</c> so the mobile pickers keep working — it is
/// styled with the core <c>tm-input</c> classes, and "Today" renders as a tonal
/// (<c>TmButton Secondary</c>) action that drops under the input on a narrow container (CSS).
/// </summary>
public class TmSigningDateStepTests : LocalizationTestBase
{
    [Theory]
    [InlineData(null, "date")]
    [InlineData("month", "month")]
    [InlineData("datetime-local", "datetime-local")]
    public void Render_Format_RendersExpectedInputType(string? format, string expectedType)
    {
        var cut = Render<TmSigningDateStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Date",
                Type = SigningFieldType.Date,
                Preferences = new SigningFieldPreferences { Format = format }
            }));

        cut.Find("input.tm-signing-date-step__input").GetAttribute("type").Should().Be(expectedType);
    }

    [Fact]
    public void DateStep_RemainsNativeDateInput_WithTmInputClasses()
    {
        var cut = Render<TmSigningDateStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Date", Type = SigningFieldType.Date }));

        var input = cut.Find("input.tm-signing-date-step__input");
        input.GetAttribute("type").Should().Be("date", "the SI1 exception keeps the native date picker");
        input.ClassList.Should().Contain("tm-input", "the native input wears the core Tm input classes");

        var labelledBy = input.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{labelledBy}").TextContent.Should().Contain("Date");
    }

    [Fact]
    public void DateStep_TodayButton_TonalVariant()
    {
        string? captured = null;
        var cut = Render<TmSigningDateStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Date", Type = SigningFieldType.Date })
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        var today = cut.Find(".tm-signing-date-step__today");
        today.ClassList.Should().Contain("tm-btn-secondary", "Today is the tonal (secondary) variant");

        today.Click();

        captured.Should().MatchRegex("^\\d{4}-\\d{2}-\\d{2}$");
    }

    [Fact]
    public void Render_DateToken_NormalizesMinToToday()
    {
        var cut = Render<TmSigningDateStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Date",
                Type = SigningFieldType.Date,
                Validation = new SigningFieldValidation { Min = "{{date}}" }
            }));

        cut.Find("input").GetAttribute("min").Should().NotBe("{{date}}");
    }
}
