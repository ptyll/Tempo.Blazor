using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// The mount note and the activation are INDEPENDENT decisions, never else-if branches. A gated
/// owner's scope notes the opener during its first parameter-set phase and activates only later
/// (after the promote interop released <see cref="TmFocusScope.Active"/>); an inline surface — a
/// <c>Modal="false"</c> TmDrawer (TmEditorShell), or a plain scope — is Active on that very FIRST
/// parameter set. Chaining the mount note with the activation through <c>else if</c> used to leave
/// such surfaces rendered but never trapped: no <c>activate</c> call, no Tab cycling, no Escape,
/// no restore — the close then had nothing to restore from.
/// </summary>
public class TmFocusScopeActivationTests : LocalizationTestBase
{
    private const string FocusTrapModule = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    [Fact]
    public void InlineDrawer_FirstRender_ActivatesOnce_AndNotesTheOpenerFirst()
    {
        var trap = JSInterop.SetupModule(FocusTrapModule);
        var note = trap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();
        var activate = trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmDrawer>(p => p
            .Add(d => d.IsOpen, true)
            .Add(d => d.Modal, false)
            .AddChildContent("<button>Inside</button>"));
        cut.WaitForAssertion(() => activate.Invocations["activate"].Should().HaveCount(1,
            "an inline drawer is Active on its first parameter set — the trap must activate even " +
            "though the drawer's mount note fired in the same pass"));

        note.Invocations["noteReturnTarget"].Should().HaveCount(1,
            "the drawer notes its opener once per open — on the first parameter set");
        var identifiers = trap.Invocations.Select(i => i.Identifier).ToList();
        identifiers.IndexOf("noteReturnTarget").Should().BeLessThan(identifiers.IndexOf("activate"),
            "the note names the pre-render opener before the activation capture can be poisoned");
    }

    [Fact]
    public void PlainScope_NoteOpenerOnMount_AndActiveOnFirstRender_StillActivates()
    {
        var trap = JSInterop.SetupModule(FocusTrapModule);
        var note = trap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();
        var activate = trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmFocusScope>(p => p
            .Add(s => s.Active, true)
            .Add(s => s.NoteOpenerOnMount, true)
            .AddChildContent("<button>Inside</button>"));
        cut.WaitForAssertion(() => activate.Invocations["activate"].Should().HaveCount(1,
            "a scope that is Active on the same first parameter set it notes on mount must still " +
            "activate — the note must never suppress the trap"));

        note.Invocations["noteReturnTarget"].Should().HaveCount(1);
        var identifiers = trap.Invocations.Select(i => i.Identifier).ToList();
        identifiers.IndexOf("noteReturnTarget").Should().BeLessThan(identifiers.IndexOf("activate"));
    }
}
