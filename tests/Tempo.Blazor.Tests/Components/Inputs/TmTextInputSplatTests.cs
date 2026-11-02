using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// The Signing steps (redesign phase 4) compose <see cref="TmTextInput"/> / <see cref="TmTextArea"/>
/// and pass the step's accessible plumbing through <c>AdditionalAttributes</c> — the ids the
/// <c>TmSigningStepShell</c> cascades (<c>aria-labelledby</c>, the description + validation
/// <c>aria-describedby</c>) plus mobile keyboard hints (<c>inputmode</c>, <c>autocomplete</c>) and
/// native validation attributes (<c>maxlength</c>, <c>pattern</c>). These tests pin the contract
/// those steps depend on: unmatched attributes SPLAT onto the native <c>&lt;input&gt;</c>/<c>&lt;textarea&gt;</c>
/// element, not the wrapper, and a splatted <c>aria-describedby</c> wins over the component's own
/// (null when no <see cref="TmTextInput.Error"/>/<see cref="TmTextInput.HelpText"/> is set).
/// </summary>
public class TmTextInputSplatTests : LocalizationTestBase
{
    [Fact]
    public void AdditionalAttributes_SplatOntoTheNativeInput()
    {
        var cut = Render<TmTextInput>(parameters => parameters
            .Add(p => p.AdditionalAttributes, new Dictionary<string, object>
            {
                ["aria-labelledby"] = "step-title",
                ["inputmode"] = "decimal",
                ["maxlength"] = 10,
                ["pattern"] = "[0-9]+"
            }));

        var input = cut.Find("input.tm-input");
        input.GetAttribute("aria-labelledby").Should().Be("step-title");
        input.GetAttribute("inputmode").Should().Be("decimal");
        input.GetAttribute("maxlength").Should().Be("10");
        input.GetAttribute("pattern").Should().Be("[0-9]+");
    }

    [Fact]
    public void SplattedAriaDescribedBy_ReachesTheInput_WhenNoOwnMessageIsSet()
    {
        var cut = Render<TmTextInput>(parameters => parameters
            .Add(p => p.AdditionalAttributes, new Dictionary<string, object>
            {
                ["aria-describedby"] = "step-description step-validation"
            }));

        cut.Find("input.tm-input")
            .GetAttribute("aria-describedby")
            .Should().Be("step-description step-validation");
    }

    [Fact]
    public void TextArea_SplatsTheAccessibleNameOntoTheTextarea()
    {
        var cut = Render<TmTextArea>(parameters => parameters
            .Add(p => p.AdditionalAttributes, new Dictionary<string, object>
            {
                ["aria-labelledby"] = "step-title"
            }));

        cut.Find("textarea.tm-textarea")
            .GetAttribute("aria-labelledby")
            .Should().Be("step-title");
    }
}
