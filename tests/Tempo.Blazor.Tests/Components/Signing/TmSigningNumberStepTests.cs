using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningNumberStep on the Tm components (Signing redesign phase 4, SI1): the value input is a
/// <c>TmTextInput</c> with <c>type="text"</c> + <c>inputmode="decimal"</c> so a Czech signer can
/// type the decimal COMMA ("125,5") — the old <c>type="number"</c> + InvariantCulture parse rejected
/// it. The allowed range renders as a localized hint formatted with the field culture.
/// </summary>
public class TmSigningNumberStepTests : LocalizationTestBase
{
    [Fact]
    public void NumberStep_PreservesInputModeAndMinMax()
    {
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Amount",
                Type = SigningFieldType.Number,
                Validation = new SigningFieldValidation { Min = "1", Max = "10" }
            }));

        var input = cut.Find("input.tm-input.tm-signing-number-step__input");
        input.GetAttribute("type").Should().Be("text", "the decimal comma must be typeable");
        input.GetAttribute("inputmode").Should().Be("decimal");
        input.GetAttribute("aria-labelledby").Should().NotBeNullOrWhiteSpace();

        // The range is announced (hint + aria-describedby), not native min/max attributes — those
        // are inert on type="text".
        var describedBy = input.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace("the range hint must be described on the input");
        cut.Find($"#{describedBy}").TextContent.Should().Contain("1").And.Contain("10");
    }

    [Fact]
    public void NumberStep_RangeHint_FormattedByCulture()
    {
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Amount",
                Type = SigningFieldType.Number,
                Validation = new SigningFieldValidation { Min = "1", Max = "1000000" }
            })
            .Add(p => p.Culture, "cs-CZ"));

        cut.Find(".tm-signing-number-step__range")
            .TextContent
            .Should()
            .Contain("1 – 1\u00A0000\u00A0000", "cs-CZ groups thousands with a non-breaking space");
    }

    [Fact]
    public void Change_CultureDecimal_Comma_Parses()
    {
        decimal? captured = null;
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Amount", Type = SigningFieldType.Number })
            .Add(p => p.Culture, "cs")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<decimal?>(this, value => captured = value)));

        cut.Find("input").Change("125,5");

        captured.Should().Be(125.5m);
    }

    [Fact]
    public void Change_InvariantDecimal_StillParses()
    {
        decimal? captured = null;
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Amount", Type = SigningFieldType.Number })
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<decimal?>(this, value => captured = value)));

        cut.Find("input").Change("12.5");

        captured.Should().Be(12.5m, "the invariant spelling keeps working for en templates");
    }

    // ── Parsing contract (phase 4 review: no AllowThousands) ─────

    /// <summary>"1,5" under en-US is a mistyped decimal point, NOT one thousand five: without
    /// AllowThousands the group separator is rejected and the token stays invalid.</summary>
    [Fact]
    public void EnUs_GroupSeparator_TokenStaysInvalid()
    {
        decimal? captured = 42m;
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Amount", Type = SigningFieldType.Number })
            .Add(p => p.Culture, "en-US")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<decimal?>(this, value => captured = value)));

        cut.Find("input").Input("1,5");
        cut.Find("input").Blur();

        captured.Should().BeNull("1,5 must not parse as one thousand five under en-US");
        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("valid number");
    }

    /// <summary>The whitespace group separator of cs-CZ ("1 000,5") is a formatting artifact and
    /// is stripped before the parse — typed with a plain space OR the culture's NBSP.</summary>
    [Theory]
    [InlineData("1 000,5")]
    [InlineData("1\u00A0000,5")]
    public void CsCz_WhitespaceGroupSeparator_IsStrippedAndParses(string typed)
    {
        decimal? captured = null;
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Amount", Type = SigningFieldType.Number })
            .Add(p => p.Culture, "cs-CZ")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<decimal?>(this, value => captured = value)));

        cut.Find("input").Input(typed);

        captured.Should().Be(1000.5m);
    }

    /// <summary>An invariant-spelled token still parses under a cs template (the "." is not the
    /// Czech decimal separator, so the invariant fallback handles it).</summary>
    [Fact]
    public void CsCz_InvariantSpelledToken_StillParses()
    {
        decimal? captured = null;
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Amount", Type = SigningFieldType.Number })
            .Add(p => p.Culture, "cs-CZ")
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<decimal?>(this, value => captured = value)));

        cut.Find("input").Input("12.5");

        captured.Should().Be(12.5m);
    }

    // ── Validation on commit, not per keystroke (phase 4 review) ──

    /// <summary>Typing raises the value and never fires an alert; the COMMIT (blur) computes it.</summary>
    [Fact]
    public void OutOfRange_Input_RaisesValueWithoutAlert_BlurValidates()
    {
        decimal? captured = null;
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Amount",
                Type = SigningFieldType.Number,
                Validation = new SigningFieldValidation { Min = "1", Max = "10" }
            })
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<decimal?>(this, value => captured = value)));

        cut.Find("input").Input("11");

        captured.Should().Be(11m, "the value still reaches the runner per keystroke");
        cut.FindAll(".tm-signing-step-shell__validation").Should().BeEmpty("no alert while typing");

        cut.Find("input").Blur();

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().NotBeEmpty("the commit computes the range message");
    }

    [Fact]
    public void RequiredEmpty_Input_RaisesNullWithoutAlert_BlurValidates()
    {
        decimal? captured = 5m;
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Amount", Type = SigningFieldType.Number, Required = true })
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<decimal?>(this, value => captured = value)));

        cut.Find("input").Input(string.Empty);

        captured.Should().BeNull();
        cut.FindAll(".tm-signing-step-shell__validation").Should().BeEmpty();

        cut.Find("input").Blur();

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("required");
    }

    /// <summary>Typing again after a failed commit clears the message; the next commit recomputes it.</summary>
    [Fact]
    public void Input_AfterFailedCommit_ClearsTheMessage()
    {
        var cut = Render<TmSigningNumberStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Amount",
                Type = SigningFieldType.Number,
                Validation = new SigningFieldValidation { Min = "1", Max = "10" }
            }));

        cut.Find("input").Input("11");
        cut.Find("input").Blur();
        cut.Find(".tm-signing-step-shell__validation").Should().NotBeNull();

        cut.Find("input").Input("5");
        cut.FindAll(".tm-signing-step-shell__validation").Should().BeEmpty("typing clears the stale message");
    }
}
