using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Redesign 146c7e1b, phase 13: <c>TmModal.TrackVirtualKeyboard</c> is an opt-in (default false, like <c>TmAiComposer.TrackVirtualKeyboard</c>).
/// An open modal hands its overlay to the core <c>tm-visual-viewport.js</c> module, which writes
/// <c>--tm-visual-viewport-inset-block-end</c> from <c>window.visualViewport</c>; closing detaches it again.
/// </summary>
public class TmModalTrackVirtualKeyboardTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-visual-viewport.js";

    private BunitJSModuleInterop Setup(bool attach = true)
    {
        JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js").SetupVoid("promote", _ => true).SetVoidResult();
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        trap.SetupVoid("noteReturnTarget", _ => true).SetVoidResult();
        trap.SetupVoid("activate", _ => true).SetVoidResult();
        var module = JSInterop.SetupModule(ModulePath);
        if (attach)
        {
            module.SetupVoid("attach", _ => true).SetVoidResult();
            module.SetupVoid("detach", _ => true).SetVoidResult();
        }

        return module;
    }

    [Fact]
    public void Default_DoesNotTrackTheKeyboard_AndNeverImportsTheModule()
    {
        var module = Setup();
        Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Dialog")
            .AddChildContent("<p>content</p>"));

        module.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void Opted_In_AttachesTheOverlayOnOpen_AndDetachesOnClose()
    {
        var module = Setup();
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Dialog")
            .Add(m => m.LayoutMode, TmLayoutMode.Mobile)
            .Add(m => m.MobilePresentation, MobilePresentation.AutoFullscreen)
            .Add(m => m.TrackVirtualKeyboard, true)
            .AddChildContent("<p>content</p>"));

        cut.WaitForAssertion(() => module.Invocations.Should().ContainSingle(i => i.Identifier == "attach"));

        cut.Render(p => p.Add(m => m.Show, false));

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "detach"));
    }

    [Fact]
    public void Opted_In_ABottomSheetNeverAttaches_ItsOwnSheetTrackerLifts()
    {
        var module = Setup();
        Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Sheet")
            .Add(m => m.LayoutMode, TmLayoutMode.Mobile)
            .Add(m => m.MobilePresentation, MobilePresentation.Auto)
            .Add(m => m.TrackVirtualKeyboard, true)
            .AddChildContent("<p>content</p>"));

        module.Invocations.Should().BeEmpty("a sheet lifts through tm-sheet.js (--tm-sheet-keyboard); a second tracker would lift it twice");
    }

    [Fact]
    public void Opted_In_ARenderThatTurnsTheModalIntoASheet_DetachesTheTracker()
    {
        var module = Setup();
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Dialog")
            .Add(m => m.LayoutMode, TmLayoutMode.Desktop)
            .Add(m => m.MobilePresentation, MobilePresentation.Auto)
            .Add(m => m.TrackVirtualKeyboard, true)
            .AddChildContent("<p>content</p>"));
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "attach").Should().Be(1));

        cut.Render(p => p.Add(m => m.LayoutMode, TmLayoutMode.Mobile));

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "detach"));
    }

    [Fact]
    public void Opted_In_ReattachesOnTheNextOpen()
    {
        var module = Setup();
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Dialog")
            .Add(m => m.LayoutMode, TmLayoutMode.Mobile)
            .Add(m => m.MobilePresentation, MobilePresentation.AutoFullscreen)
            .Add(m => m.TrackVirtualKeyboard, true)
            .AddChildContent("<p>content</p>"));
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "attach").Should().Be(1));
        cut.Render(p => p.Add(m => m.Show, false));
        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "detach"));
        cut.Render(p => p.Add(m => m.Show, true));

        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "attach").Should().Be(2));
    }

    [Fact]
    public void Css_FullscreenAndDialogPresentations_PadTheOverlayByTheTrackedInset_WithAZeroFallback()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var css = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components", "_modal.css"));

        css.Should().Contain(".tm-modal-overlay.tm-modal--fullscreen-presentation[data-tm-keyboard-open]");
        css.Should().Contain(".tm-modal-overlay.tm-modal--dialog[data-tm-keyboard-open]");
        css.Should().Contain("var(--tm-visual-viewport-inset-block-end, 0px)");
        css.Should().Contain(".tm-modal-overlay.tm-modal--dialog[data-tm-keyboard-open] .tm-modal", "a tall centred dialog must not outgrow the padded box and slide under the keyboard");
        css.Should().MatchRegex(@"\[data-tm-keyboard-open\] \.tm-modal,\s*\.tm-modal-overlay\.tm-modal--dialog\[data-tm-keyboard-open\] \.tm-dialog \{\s*max-height: 100%;");
        css.Should().NotContain("--tm-visual-viewport-inset-block-end:", "the component stylesheet only reads the knob; tokens.css declares the 0px default, the module writes the value");
    }
}
