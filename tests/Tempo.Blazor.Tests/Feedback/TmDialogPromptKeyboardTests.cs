using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Prompt submit is one activation per Enter press (UX review round 6, M-r6-1). The submit used to
/// ride on the input's bare Enter <c>keyup</c>: since the per-type initial focus the opener's own
/// Enter keydown opens the dialog, focus lands on the input while the key is still down, and the
/// trailing keyup submitted the DefaultValue at once — one press, two activations, the dialog
/// flashing past. The submit is now armed by the input's OWN Enter keydown and fired by its keyup:
/// a bare keyup (the opener's trailing one) arrives unarmed and must not submit, a full press
/// submits exactly once, auto-repeat must not double it, and an arm must not survive a reopen.
/// (A bare keydown submit was probed and rejected: it closes the dialog between keydown and keyup,
/// and Chromium then runs the keydown's default action against the re-focused opener — the dialog
/// re-opened itself with the default value. docs/keyboard-activation-convention.md.)
/// </summary>
public class TmDialogPromptKeyboardTests : LocalizationTestBase
{
    private readonly List<string?> _results = new();

    public TmDialogPromptKeyboardTests()
    {
        Services.AddSingleton(Substitute.For<IJSRuntime>());
    }

    private IRenderedComponent<TmDialog> RenderPrompt(string? defaultValue = "John Doe")
        => Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Prompt)
            .Add(d => d.Title, "Enter Your Name")
            .Add(d => d.DefaultValue, defaultValue)
            .Add(d => d.OnPromptResult, EventCallback.Factory.Create<string?>(this, v => _results.Add(v))));

    [Fact]
    public void EnterKeyUpAlone_DoesNotSubmit()
    {
        var cut = RenderPrompt();

        cut.Find("input.tm-dialog-input").KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().BeEmpty(
            "the opener's Enter keyup lands on the freshly focused input; a keyup the input never " +
            "saw go down must never submit — that is the one-press-two-activations defect");
    }

    [Fact]
    public void EnterKeyDownAlone_DoesNotSubmit()
    {
        var cut = RenderPrompt();

        cut.Find("input.tm-dialog-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().BeEmpty(
            "the submit fires on the keyup of the same press — closing on the keydown would let " +
            "Chromium run the keydown's default action against the re-focused opener and re-open the dialog");
    }

    [Fact]
    public void EnterPress_KeyDownThenKeyUp_SubmitsTheDefaultValue_ExactlyOnce()
    {
        var cut = RenderPrompt();
        var input = cut.Find("input.tm-dialog-input");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        input.KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().ContainSingle("Enter activates exactly once per press").Which.Should().Be("John Doe");
    }

    [Fact]
    public void HeldEnter_KeyDownRepeatThenKeyUp_SubmitsExactlyOnce()
    {
        var cut = RenderPrompt();
        var input = cut.Find("input.tm-dialog-input");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        input.KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        input.KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        input.KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().ContainSingle("a held key's auto-repeat must not multiply the submit")
            .Which.Should().Be("John Doe");
    }

    [Fact]
    public void EnterPress_WhitespaceOnlyValue_DoesNotSubmit()
    {
        var cut = RenderPrompt("   ");
        var input = cut.Find("input.tm-dialog-input");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        input.KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().BeEmpty("an empty prompt has nothing to confirm — the OK button is disabled for the same reason");
    }

    [Fact]
    public void TypedValue_EnterPress_SubmitsTheTypedValue_ExactlyOnce()
    {
        var cut = RenderPrompt();
        var input = cut.Find("input.tm-dialog-input");
        input.Input("Pavel");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        input.KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().ContainSingle().Which.Should().Be("Pavel");
    }

    [Fact]
    public void SecondEnterKeyUp_WithoutANewKeyDown_DoesNotSubmitAgain()
    {
        var cut = RenderPrompt();
        var input = cut.Find("input.tm-dialog-input");
        input.Input("Pavel");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        input.KeyUp(new KeyboardEventArgs { Key = "Enter" });
        input.KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().ContainSingle("the arm is consumed by the keyup that submitted; a stray keyup is unarmed");
    }

    [Fact]
    public void EnterArm_DoesNotSurviveAReopen()
    {
        var cut = RenderPrompt();
        cut.Find("input.tm-dialog-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.InvokeAsync(async () =>
        {
            await cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
                new Dictionary<string, object> { [nameof(TmDialog.Show)] = false }));
            await cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
                new Dictionary<string, object> { [nameof(TmDialog.Show)] = true }));
        }).GetAwaiter().GetResult();

        cut.Find("input.tm-dialog-input").KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().BeEmpty("an arm left over from the previous session must not submit into the fresh dialog");
    }
}
