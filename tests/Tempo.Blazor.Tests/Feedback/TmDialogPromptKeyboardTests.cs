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
/// Prompt submit is an Enter-on-keydown activation (UX review round 6, M-r6-1). The submit used to
/// ride on the input's Enter <c>keyup</c>: since the per-type initial focus the opener's own Enter
/// keydown opens the dialog, focus lands on the input while the key is still down, and the trailing
/// keyup submitted the DefaultValue at once — one press, two activations, the dialog flashing past.
/// Enter acts on keydown (docs/keyboard-activation-convention.md), guarded against auto-repeat, and
/// a keyup without a prior accepted keydown must never submit.
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
            "the opener's Enter keyup lands on the freshly focused input; a keyup without an accepted " +
            "keydown must never submit — that is the one-press-two-activations defect");
    }

    [Fact]
    public void EnterKeyDown_SubmitsTheDefaultValue_ExactlyOnce()
    {
        var cut = RenderPrompt();

        cut.Find("input.tm-dialog-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().ContainSingle("Enter activates exactly once per press").Which.Should().Be("John Doe");
    }

    [Fact]
    public void EnterKeyDownRepeat_DoesNotSubmit()
    {
        var cut = RenderPrompt();

        cut.Find("input.tm-dialog-input").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });

        _results.Should().BeEmpty("a held Enter auto-repeats keydowns; a repeat event must not re-submit");
    }

    [Fact]
    public void EnterKeyDown_WhitespaceOnlyValue_DoesNotSubmit()
    {
        var cut = RenderPrompt("   ");

        cut.Find("input.tm-dialog-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().BeEmpty("an empty prompt has nothing to confirm — the OK button is disabled for the same reason");
    }

    [Fact]
    public void TypedValue_EnterKeyDown_SubmitsTheTypedValue_ExactlyOnce()
    {
        var cut = RenderPrompt();
        cut.Find("input.tm-dialog-input").Input("Pavel");

        cut.Find("input.tm-dialog-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().ContainSingle().Which.Should().Be("Pavel");
    }

    [Fact]
    public void TypedValue_TrailingEnterKeyUp_AfterAnAcceptedKeyDown_DoesNotSubmitAgain()
    {
        var cut = RenderPrompt();
        var input = cut.Find("input.tm-dialog-input");
        input.Input("Pavel");

        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        input.KeyUp(new KeyboardEventArgs { Key = "Enter" });

        _results.Should().ContainSingle("the keyup of the same press is spent once the keydown submitted");
    }
}
