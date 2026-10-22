using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// Review fix F9A/UX 9 — typing into the rich editor used to reorder the characters the author had just
/// typed (" EDIT" became "TIDE").
/// <para>
/// The chain: <c>HandleInput</c> reads the editor's HTML, stores it in <c>Value</c> and raises
/// <c>ValueChanged</c>. The host echoes that value back into the parameter, which re-renders this component,
/// and <c>OnAfterRenderAsync</c> then re-synced the parameter into the editor — reading the DOM and calling
/// <c>setHtml</c>, which moves the caret to the start, so the next character landed in front of the previous
/// one. The echo of our OWN emission is not an external change: the component now remembers what it emitted
/// and does not touch the DOM at all.
/// </para>
/// </summary>
public class TmRichEditorSimpleEchoTests : LocalizationTestBase
{
    /// <summary>The values the component raised through <c>ValueChanged</c>.</summary>
    private List<string> LastEmitted { get; set; } = new();

    private IRenderedComponent<TmRichEditorSimple> RenderEditor(
        string value, out Action<string> emit)
    {
        var captured = new List<string>();
        emit = captured.Add;
        var cut = Render<TmRichEditorSimple>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, (string v) => captured.Add(v)));
        LastEmitted = captured;
        return cut;
    }

    private static void Type(IRenderedComponent<TmRichEditorSimple> cut)
        => cut.Find(".tm-rte-editor-content").Input(new ChangeEventArgs { Value = "typed" });

    [Fact]
    public void The_echo_of_our_own_emission_does_not_read_the_editor_dom()
    {
        var getHtml = JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("hello EDIT");

        var cut = RenderEditor("hello", out var emitted);

        // The author types: the component reads the DOM once and emits what it read.
        Type(cut);
        LastEmitted.Should().ContainSingle().Which.Should().Be("hello EDIT");

        // The host echoes the emitted value back (bind-Value) — the ordinary path after every keystroke.
        cut.Render(p => p
            .Add(c => c.Value, "hello EDIT")
            .Add(c => c.ValueChanged, (string _) => { }));

        cut.WaitForAssertion(() => getHtml.Invocations.Should().HaveCount(1,
            "the echo of our own emission is not an external change: reading the DOM and re-setting the HTML "
            + "on every keystroke moved the caret to the start and reordered the letters"));
        JSInterop.Invocations["tmRichEditor.setHtml"].Should().HaveCount(1,
            "the editor is painted once (the initial value), never on the echo of its own emission");
    }

    [Fact]
    public void An_external_value_still_reaches_the_editor()
    {
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("old");

        var cut = RenderEditor("old", out _);
        cut.Render(p => p
            .Add(c => c.Value, "restored by undo")
            .Add(c => c.ValueChanged, (string _) => { }));

        cut.WaitForAssertion(() =>
        {
            var setHtml = JSInterop.Invocations["tmRichEditor.setHtml"];
            setHtml.Should().HaveCount(2, "the initial paint plus the external (undo) change");
            setHtml[^1].Arguments[1].Should().Be("restored by undo",
                "an external change must still be painted into the editor");
        });
    }

    [Fact]
    public void Typing_still_emits_the_editors_html()
    {
        JSInterop.Setup<string>("tmRichEditor.getHtml", _ => true).SetResult("<b>bold</b>");

        var cut = RenderEditor(string.Empty, out var emitted);
        Type(cut);

        LastEmitted.Should().ContainSingle().Which.Should().Be("<b>bold</b>");
    }
}
