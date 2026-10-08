using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Initial focus of <see cref="TmDialog"/> (review round 5). The scroller used to be the first
/// tabbable element of every dialog, so the focus trap landed on the title+message block: a prompt
/// never focused its input, a confirm did not start on a button, and a keyboard-opened dialog
/// outlined the whole content region. The markup contract pinned here:
/// every dialog type names an explicit initial-focus target on the scope, and the scroller is
/// NOT a hardcoded tab stop — the JS module turns it into a keyboard-reachable region only while
/// it actually overflows, and even then the region never takes initial focus.
/// </summary>
public class TmDialogFocusTests : LocalizationTestBase
{
    [Fact]
    public void Alert_NamesTheOkButton_AsTheInitialFocusTarget()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Title, "Saved")
            .Add(d => d.Message, "All done."));

        var scope = cut.Find(".tm-focus-scope");
        var initial = scope.GetAttribute("data-initial-focus");
        initial.Should().NotBeNull("an alert names its initial-focus target explicitly");
        cut.Find($"button#{initial}").Should().NotBeNull();
        cut.Find($"button#{initial}").ClassList.Should().Contain("tm-dialog-btn-ok",
            "an alert starts on OK, the only action it offers");
    }

    [Fact]
    public void Confirm_NamesTheCancelButton_AsTheInitialFocusTarget()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Confirm action")
            .Add(d => d.Message, "Are you sure?"));

        var scope = cut.Find(".tm-focus-scope");
        var initial = scope.GetAttribute("data-initial-focus");
        initial.Should().NotBeNull("a confirm names its initial-focus target explicitly");
        cut.Find($"button#{initial}").ClassList.Should().Contain("tm-dialog-btn-cancel",
            "Cancel is the least destructive action, so a confirm starts there");
    }

    [Fact]
    public void DangerousConfirm_NamesTheCancelButton_AsTheInitialFocusTarget()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.IsDangerous, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.Message, "This cannot be undone."));

        var scope = cut.Find(".tm-focus-scope");
        var initial = scope.GetAttribute("data-initial-focus");
        initial.Should().NotBeNull();
        cut.Find($"button#{initial}").ClassList.Should().Contain("tm-dialog-btn-cancel",
            "a destructive dialog must not sit on the destructive button");
        cut.Find($"button#{initial}").GetAttribute("id").Should().NotBe(
            cut.Find(".tm-dialog-btn-ok").GetAttribute("id"));
    }

    [Fact]
    public void Prompt_NamesTheInput_AsTheInitialFocusTarget()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Prompt)
            .Add(d => d.Title, "Enter your name")
            .Add(d => d.Message, "Please enter your name to continue:"));

        var scope = cut.Find(".tm-focus-scope");
        var initial = scope.GetAttribute("data-initial-focus");
        initial.Should().NotBeNull("a prompt must type straight into its input");
        cut.Find($"input#{initial}").Should().NotBeNull("the named target is the prompt input itself");
    }

    [Fact]
    public void Custom_KeepsTheDefaultFirstFocusable()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Custom)
            .Add(d => d.Title, "Custom")
            .AddChildContent("<button type=\"button\">Inside</button>"));

        cut.Find(".tm-focus-scope").GetAttribute("data-initial-focus").Should().BeNull(
            "a custom dialog's content decides its own initial focus");
    }

    [Fact]
    public void Content_IsNotAHardcodedTabStop()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Long message")
            .Add(d => d.Message, new string('x', 400)));

        var content = cut.Find(".tm-dialog-content");
        content.GetAttribute("tabindex").Should().BeNull(
            "a short dialog must not grow an extra tab stop; the JS module adds tabindex=0 only while the content overflows");
        content.GetAttribute("role").Should().BeNull(
            "the region role is owned by the overflow module, not hardcoded markup");
        content.GetAttribute("aria-labelledby").Should().BeNull();
    }
}
