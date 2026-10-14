using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// The modal/dialog focus-restore contract against a poisoned return target (P04-CF F7). The
/// trap's own capture runs only after the lazy ES-module import completed, so an app that focuses
/// a field inside the overlay during the open can be there first — the capture then names an
/// element that dies with the surface, and the close used to focus it (the focus fell to
/// <c>&lt;body&gt;</c> the moment the overlay left the DOM). A gated owner's scope therefore notes
/// the opener in its FIRST parameter-set phase — the mount render, before the content's child
/// lifecycles can autofocus a field inside — through the module's <c>noteReturnTarget</c>, and the
/// module ignores a capture that lies inside the surface. The restore itself (no steal of an app
/// focus placed outside, no dying target) is pinned by the module's node tests; this file pins
/// the C# wiring.
/// </summary>
public class TmModalFocusRestoreTests : LocalizationTestBase
{
    private const string FocusTrapModule = "./_content/Tempo.Blazor/js/tm-focus-trap.js";
    private const string TopLayerModule = "./_content/Tempo.Blazor/js/tm-top-layer.js";

    [Fact]
    public void TmModal_Opening_NotesTheOpenerBeforeTheTrapActivates()
    {
        var top = JSInterop.SetupModule(TopLayerModule);
        top.SetupVoid("promote", _ => true).SetVoidResult();
        var trap = JSInterop.SetupModule(FocusTrapModule);
        var note = trap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();
        trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Focus restore")
            .AddChildContent("<p>content</p>"));
        cut.WaitForAssertion(() => trap.Invocations
            .Should().Contain(i => i.Identifier == "activate"));

        note.Invocations["noteReturnTarget"].Should().HaveCount(1,
            "the gated scope notes the opener once per open — at its MOUNT parameter-set phase, before the overlay renders");
        var identifiers = trap.Invocations.Select(i => i.Identifier).ToList();
        identifiers.IndexOf("noteReturnTarget").Should().BeLessThan(identifiers.IndexOf("activate"),
            "the note must precede the activation — it names the pre-render opener the late capture can no longer see");
    }

    /// <summary>
    /// The blocker this phase fixes: a gated scope used to note only at its Active flip — AFTER
    /// the promote and after the content child's first render had already autofocus-focussed the
    /// field, poisoning the note. The mount note must be on the wire BEFORE the content child's
    /// first <c>OnAfterRenderAsync</c> runs (the probe counts the module's note invocations at
    /// that exact point).
    /// </summary>
    [Fact]
    public void TmModal_NotesTheOpener_BeforeTheContentChildsFirstOnAfterRender()
    {
        var top = JSInterop.SetupModule(TopLayerModule);
        top.SetupVoid("promote", _ => true).SetVoidResult();
        var trap = JSInterop.SetupModule(FocusTrapModule);
        trap.SetupVoid("ensureLoaded", _ => true).SetVoidResult();
        trap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();
        trap.SetupVoid("activate", _ => true).SetVoidResult();

        var notesAtChildFirstRender = -1;
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Focus restore")
            .AddChildContent(builder =>
            {
                builder.OpenComponent<FirstRenderProbe>(0);
                builder.AddComponentParameter(1, nameof(FirstRenderProbe.OnFirstRender),
                    EventCallback.Factory.Create(this, () => notesAtChildFirstRender =
                        trap.Invocations.Count(i => i.Identifier == "noteReturnTarget")));
                builder.CloseComponent();
            }));
        cut.WaitForAssertion(() => notesAtChildFirstRender.Should().BeGreaterThanOrEqualTo(0));

        notesAtChildFirstRender.Should().BeGreaterThanOrEqualTo(1,
            "the mount note is issued in the scope's parameter-set phase — the content child's " +
            "first OnAfterRenderAsync (an autofocus) can never run before it");
    }

    [Fact]
    public void TmDialog_Opening_NotesTheOpenerBeforeTheTrapActivates()
    {
        var top = JSInterop.SetupModule(TopLayerModule);
        top.SetupVoid("promote", _ => true).SetVoidResult();
        var trap = JSInterop.SetupModule(FocusTrapModule);
        var note = trap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();
        trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Title, "Focus restore")
            .Add(d => d.Message, "Sure?"));
        cut.WaitForAssertion(() => trap.Invocations
            .Should().Contain(i => i.Identifier == "activate"));

        note.Invocations["noteReturnTarget"].Should().HaveCount(1,
            "a dialog's scope notes its opener before the overlay renders");
        var identifiers = trap.Invocations.Select(i => i.Identifier).ToList();
        identifiers.IndexOf("noteReturnTarget").Should().BeLessThan(identifiers.IndexOf("activate"));
    }

    [Fact]
    public void TmModal_Closing_DoesNotNoteAgain()
    {
        var top = JSInterop.SetupModule(TopLayerModule);
        top.SetupVoid("promote", _ => true).SetVoidResult();
        var trap = JSInterop.SetupModule(FocusTrapModule);
        trap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();
        trap.SetupVoid("activate", _ => true).SetVoidResult();
        var deactivate = trap.SetupVoid("deactivate", _ => true).SetVoidResult();

        var show = true;
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, show)
            .Add(m => m.Title, "Focus restore")
            .AddChildContent("<p>content</p>"));
        cut.WaitForAssertion(() => trap.Invocations
            .Should().Contain(i => i.Identifier == "activate"));

        cut.InvokeAsync(async () => await cut.Instance.SetParametersAsync(
            ParameterView.FromDictionary(new Dictionary<string, object> { [nameof(TmModal.Show)] = false })));
        cut.WaitForAssertion(() => deactivate.Invocations["deactivate"].Should().HaveCount(1));

        trap.Invocations.Count(i => i.Identifier == "noteReturnTarget").Should().Be(1,
            "only the open notes an opener — the close must not overwrite it");
    }
}

/// <summary>A content child whose first OnAfterRender reports to the test — the autofocus stand-in.</summary>
internal sealed class FirstRenderProbe : ComponentBase
{
    /// <summary>Invoked once, from the child's first OnAfterRender (where an autofocus would run).</summary>
    [Parameter] public EventCallback OnFirstRender { get; set; }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await OnFirstRender.InvokeAsync();
        }
    }
}
