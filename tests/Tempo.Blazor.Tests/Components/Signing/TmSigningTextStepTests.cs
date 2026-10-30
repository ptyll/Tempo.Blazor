using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// TmSigningTextStep on the Tm components (Signing redesign phase 4, SI1): the single-line variant
/// renders <c>TmTextInput</c>, the multiline variant <c>TmTextArea</c>, and the accessible name is
/// the step shell's title id (<c>aria-labelledby</c> through the component splat) — never a second
/// visible label.
/// </summary>
public class TmSigningTextStepTests : LocalizationTestBase
{
    [Fact]
    public void TextStep_UsesTmTextInput_WithLabelHelpAndAriaDescribedBy()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Name", Type = SigningFieldType.Text }));

        var input = cut.Find("input.tm-input.tm-signing-text-step__input");
        input.GetAttribute("type").Should().Be("text");

        // The accessible name is the shell title id (phase 2 contract kept through the splat), and
        // the step renders NO second visible label of its own.
        var labelledBy = input.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace();
        cut.Find($"#{labelledBy}").TextContent.Should().Contain("Name");
        cut.FindAll(".tm-input-label").Should().BeEmpty("the shell title is the label — a second one would read twice");
    }

    [Fact]
    public void TextStep_Multiline_UsesTmTextArea()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Notes",
                Type = SigningFieldType.Text,
                Preferences = new SigningFieldPreferences { Format = "multiline" }
            }));

        cut.Find("textarea.tm-textarea").Should().NotBeNull("multiline renders the core TmTextArea");
    }

    /// <summary>
    /// The multiline <c>TmTextArea</c> has no <c>IsValid</c> parameter — the splat is the only
    /// channel that marks it invalid. A required multiline field committed empty shows the alert
    /// and the textarea mirrors it (<c>aria-invalid</c> + the validation id in
    /// <c>aria-describedby</c>).
    /// </summary>
    [Fact]
    public void Multiline_RequiredCommittedEmpty_MarksTheTextareaInvalid()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Notes",
                Type = SigningFieldType.Text,
                Required = true,
                Preferences = new SigningFieldPreferences { Format = "multiline" }
            }));

        cut.Find("textarea").Change(string.Empty);

        var validation = cut.Find(".tm-signing-step-shell__validation");
        validation.TextContent.Should().NotBeEmpty("the commit computes the required message");
        validation.GetAttribute("id").Should().NotBeNullOrWhiteSpace();

        var textarea = cut.Find("textarea");
        textarea.GetAttribute("aria-invalid").Should().Be("true");
        textarea.GetAttribute("aria-describedby").Should().Contain(validation.GetAttribute("id"));
    }

    [Fact]
    public void TextStep_CharCounter_VisualIsAriaHidden_LiveRegionAnnouncesOnlyAtLimit()
    {
        var field = new SigningField { Name = "Code", Type = SigningFieldType.Text };
        field.Preferences.AdditionalSettings["maxLength"] = 10;

        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, field)
            .Add(p => p.Value, "abc"));

        // The visible count is decorative; the live announcement is a permanent visually-hidden
        // polite region whose TEXT fills only at the limit (phase 4 review).
        var counter = cut.Find(".tm-signing-text-step__count");
        counter.TextContent.Should().Contain("3").And.Contain("10");
        counter.GetAttribute("aria-hidden").Should().Be("true");
        counter.GetAttribute("aria-live").Should().BeNull();

        var liveRegion = cut.Find(".tm-sr-only[aria-live='polite']");
        liveRegion.TextContent.Trim().Should().BeEmpty("below the limit the region stays silent");

        cut.Find("input").Input("abcdefghij");

        var announced = cut.Find(".tm-sr-only[aria-live='polite']");
        announced.TextContent.Should().Contain("10", "at the limit the region announces the count");
        cut.Find(".tm-signing-text-step__count").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void Render_CellsField_SetsMaxLengthFromArea()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Code", Type = SigningFieldType.Cells })
            .Add(p => p.Area, new SigningFieldArea { Width = 0.4, CellWidth = 0.1 }));

        cut.Find("input").GetAttribute("maxlength").Should().Be("4");
    }

    [Fact]
    public void Render_Pattern_SplattedOntoTheNativeInput()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Code",
                Type = SigningFieldType.Text,
                Validation = new SigningFieldValidation { Pattern = "^[0-9]+$" }
            }));

        cut.Find("input").GetAttribute("pattern").Should().Be("^[0-9]+$");
    }

    /// <summary>
    /// Validation on COMMIT, not per keystroke (phase 4 review): typing "a" into a pattern field
    /// raises the value and fires NO alert; the alert appears when the field commits (blur).
    /// </summary>
    [Fact]
    public void PatternField_InputRaisesValueWithoutAlert_BlurValidates()
    {
        string? captured = null;
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Code",
                Type = SigningFieldType.Text,
                Validation = new SigningFieldValidation { Pattern = "^[0-9]+$" }
            })
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        cut.Find("input").Input("a");

        captured.Should().Be("a", "the value still reaches the runner per keystroke");
        cut.FindAll(".tm-signing-step-shell__validation").Should().BeEmpty("no alert while typing");

        cut.Find("input").Blur();

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().NotBeEmpty("the commit computes the message");
        cut.Find("input").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void Change_InvalidPattern_ShowsCustomMessage()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Code",
                Type = SigningFieldType.Text,
                Validation = new SigningFieldValidation { Pattern = "^[0-9]+$", Message = "Digits only" }
            }));

        cut.Find("input").Input("abc");
        cut.Find("input").Blur();

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Be("Digits only");
        cut.Find("input").GetAttribute("aria-invalid").Should().Be("true");
    }

    /// <summary>Validation.Min doubles as the minimum LENGTH for text-like fields; the message is
    /// a dedicated localized string and the commit path computes it.</summary>
    [Fact]
    public void Change_BelowMinLength_ShowsMinLengthMessageOnCommit()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Code",
                Type = SigningFieldType.Text,
                Validation = new SigningFieldValidation { Min = "3" }
            }));

        cut.Find("input").Input("ab");
        cut.FindAll(".tm-signing-step-shell__validation").Should().BeEmpty();

        cut.Find("input").Blur();

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("at least 3");
    }

    [Fact]
    public void Input_AfterFailedCommit_ClearsTheMessage()
    {
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField
            {
                Name = "Code",
                Type = SigningFieldType.Text,
                Validation = new SigningFieldValidation { Pattern = "^[0-9]+$", Message = "Digits only" }
            }));

        cut.Find("input").Input("abc");
        cut.Find("input").Blur();
        cut.Find(".tm-signing-step-shell__validation").Should().NotBeNull();

        cut.Find("input").Input("12");
        cut.FindAll(".tm-signing-step-shell__validation").Should().BeEmpty("typing clears the stale message");
    }

    [Fact]
    public void Change_ValidValue_InvokesValueChanged()
    {
        string? captured = null;
        var cut = Render<TmSigningTextStep>(parameters => parameters
            .Add(p => p.Field, new SigningField { Name = "Name", Type = SigningFieldType.Text })
            .Add(p => p.ValueChanged, EventCallback.Factory.Create<string?>(this, value => captured = value)));

        cut.Find("input").Change("Alice");

        captured.Should().Be("Alice");
    }
}
