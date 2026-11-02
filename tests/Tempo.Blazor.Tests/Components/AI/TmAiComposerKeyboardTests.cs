using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Keyboard contract of <see cref="TmAiComposer"/> (plan 7f21f26b, Fáze 6). The package's ES module owns Enter
/// once attached (a net8 <see cref="KeyboardEventArgs"/> cannot see an IME composition), the C#
/// <c>@onkeydown</c> handler is the pre-attach fallback and the combobox navigation — so Enter activates
/// exactly once and never submits the half-composed text of an IME.
/// </summary>
public sealed class TmAiComposerKeyboardTests : LocalizationTestBase
{
    private const string InteropModulePath = TmAiComposerTests.InteropModulePath;

    private BunitJSModuleInterop SetupInterop(bool actionSlotFocused = false)
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("resize", _ => true).SetVoidResult();
        module.SetupVoid("placeSuggestions", _ => true).SetVoidResult();
        module.Setup<bool>("speechSupported", _ => true).SetResult(false);
        module.Setup<bool>("startSpeech", _ => true).SetResult(true);
        module.SetupVoid("stopSpeech", _ => true).SetVoidResult();
        module.SetupVoid("attachViewportTracker", _ => true).SetVoidResult();
        module.SetupVoid("detachViewportTracker", _ => true).SetVoidResult();
        module.Setup<bool>("isFocusWithin", _ => true).SetResult(actionSlotFocused);
        module.SetupVoid("focusFirst", _ => true).SetVoidResult();
        return module;
    }

    /// <summary>A module whose attach fails: the component stays on the C# fallback for the whole test.</summary>
    private void SetupFailedAttach()
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.SetupVoid("attach", _ => true).SetException(new JSException("module unavailable"));
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("resize", _ => true).SetVoidResult();
        module.SetupVoid("placeSuggestions", _ => true).SetVoidResult();
        module.Setup<bool>("speechSupported", _ => true).SetResult(false);
    }

    private IRenderedComponent<TmAiComposer> RenderComposer(
        Action<ComponentParameterCollectionBuilder<TmAiComposer>> configure)
        => Render<TmAiComposer>(parameters => configure(parameters));

    /// <summary>
    /// The element reference id the renderer stamped on <paramref name="element"/>. It has to be read BEFORE the
    /// interaction: the render that follows serializes the capture attribute away while the reference itself
    /// stays valid, which is why the repo's focus assertions compare ids instead of elements.
    /// </summary>
    private static string? ReferenceId(IElement element) => element.GetAttribute("blazor:elementreference");

    // ── Pre-attach fallback: the C# handler drives Enter ────────────────────────────────────────

    [Fact]
    public void Enter_SubmitsThroughTheFallbackWhileTheModuleIsNotAttached()
    {
        SetupFailedAttach();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").Input("Hello");
        cut.Find("textarea").KeyDown("Enter");

        submits.Should().ContainSingle("Enter sends the prompt");
        submits[0].Text.Should().Be("Hello");
    }

    [Fact]
    public void ShiftEnter_IsANewLineAndNeverSends()
    {
        SetupFailedAttach();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").Input("line one");
        cut.Find("textarea").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        submits.Should().BeEmpty("Shift+Enter is the documented new-line chord");
        cut.Find("textarea").GetAttribute("value").Should().Be("line one");
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void AModifiedEnter_IsNotASubmit(bool alt, bool ctrl, bool meta)
    {
        SetupFailedAttach();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").Input("Hello");
        cut.Find("textarea").TriggerEvent("onkeydown", new KeyboardEventArgs
        {
            Key = "Enter",
            AltKey = alt,
            CtrlKey = ctrl,
            MetaKey = meta,
        });

        submits.Should().BeEmpty("a modifier chord belongs to the browser or the host's shortcut");
    }

    [Fact]
    public void Enter_DuringAnImeComposition_DoesNotSubmit()
    {
        SetupFailedAttach();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").Input("こんにち");
        cut.Find("textarea").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter", IsComposing = true });

        submits.Should().BeEmpty("the Enter that commits an IME composition must not send half-composed text");
    }

    [Fact]
    public void Enter_WithAnEmptyDraft_DoesNotSubmit()
    {
        SetupFailedAttach();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").KeyDown("Enter");

        submits.Should().BeEmpty();
    }

    // ── Attached: the module owns Enter, the invokable is the one entry point ───────────────────

    [Fact]
    public async Task Enter_ThroughTheModuleSubmitsExactlyOnce()
    {
        var module = SetupInterop();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.WaitForAssertion(() => TmAiComposerTests.Count(module, "attach").Should().Be(1));

        cut.Find("textarea").Input("Hello");
        cut.Find("textarea").KeyDown("Enter");
        submits.Should().BeEmpty("the attached module owns Enter — the C# fallback must stay quiet");

        await cut.InvokeAsync(() => cut.Instance.OnComposerSubmit());
        submits.Should().ContainSingle();
        submits[0].Text.Should().Be("Hello");
    }

    [Fact]
    public async Task TheModuleSubmit_WithAnEmptyDraft_IsANoOp()
    {
        var module = SetupInterop();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.WaitForAssertion(() => TmAiComposerTests.Count(module, "attach").Should().Be(1));
        await cut.InvokeAsync(() => cut.Instance.OnComposerSubmit());

        submits.Should().BeEmpty();
    }

    [Fact]
    public async Task TheModuleSubmit_IsANoOpWhileGenerating()
    {
        var module = SetupInterop();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit))
            .Add(component => component.IsGenerating, true));

        cut.WaitForAssertion(() => TmAiComposerTests.Count(module, "attach").Should().Be(1));
        cut.Find("textarea").Input("Next question");
        await cut.InvokeAsync(() => cut.Instance.OnComposerSubmit());

        submits.Should().BeEmpty("the slot shows Stop while a generation runs — Enter must not start a second one");
    }

    // ── Combobox navigation ────────────────────────────────────────────────────────────────────

    private IRenderedComponent<TmAiComposer> RenderWithCommands()
        => RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.Commands, new[]
            {
                new AiSlashCommand { Id = "summarise", Title = "summarise", Description = "Summarise the thread" },
                new AiSlashCommand { Id = "translate", Title = "translate", Description = "Translate the thread" },
                new AiSlashCommand { Id = "summon", Title = "summon", Description = "Unrelated command" },
            }));

    [Fact]
    public void Slash_OpensTheCommandListboxAsAnExpandedCombobox()
    {
        SetupInterop();
        var cut = RenderWithCommands();

        cut.Find("textarea").Input("/sum");
        cut.WaitForAssertion(() => cut.FindAll("[role='listbox']").Should().ContainSingle());

        var input = cut.Find("textarea");
        var listbox = cut.Find("[role='listbox']");
        input.GetAttribute("aria-expanded").Should().Be("true");
        input.GetAttribute("aria-controls").Should().Be(listbox.Id);
        input.GetAttribute("aria-activedescendant").Should().Be(listbox.QuerySelectorAll("[role='option']")[0].Id);
        listbox.GetAttribute("aria-label").Should().Be("Commands");

        var options = listbox.QuerySelectorAll("[role='option']");
        options.Select(option => option.TextContent).Should().Contain(text => text.Contains("summarise"));
        options.Select(option => option.TextContent).Should().Contain(text => text.Contains("summon"));
        options.Select(option => option.TextContent).Should().NotContain(text => text.Contains("translate"));
        options[0].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void ArrowKeys_MoveTheActiveOptionAndWrap()
    {
        SetupInterop();
        var cut = RenderWithCommands();

        cut.Find("textarea").Input("/");
        cut.WaitForAssertion(() => cut.FindAll("[role='option']").Should().HaveCount(3));

        cut.Find("textarea").KeyDown("ArrowDown");
        ActiveOptionText(cut).Should().Contain("translate");
        cut.Find("textarea").KeyDown("ArrowDown");
        ActiveOptionText(cut).Should().Contain("summon");
        cut.Find("textarea").KeyDown("ArrowDown");
        ActiveOptionText(cut).Should().Contain("summarise", "the list wraps");
        cut.Find("textarea").KeyDown("ArrowUp");
        ActiveOptionText(cut).Should().Contain("summon");
    }

    private static string ActiveOptionText(IRenderedComponent<TmAiComposer> cut)
    {
        var id = cut.Find("textarea").GetAttribute("aria-activedescendant");
        return cut.Find($"#{id}").TextContent;
    }

    [Fact]
    public void Enter_OnAnOpenListbox_SelectsTheActiveOptionInsteadOfSending()
    {
        SetupInterop();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit))
            .Add(component => component.Commands, new[]
            {
                new AiSlashCommand { Id = "summarise", Title = "summarise" },
                new AiSlashCommand { Id = "translate", Title = "translate" },
            }));

        cut.Find("textarea").Input("/");
        cut.WaitForAssertion(() => cut.FindAll("[role='option']").Should().HaveCount(2));

        cut.Find("textarea").KeyDown("ArrowDown");
        cut.Find("textarea").KeyDown("Enter");

        submits.Should().BeEmpty("Enter picks the highlighted command; the reader still has to send it");
        cut.FindAll("[role='listbox']").Should().BeEmpty();
        cut.Find("textarea").GetAttribute("value").Should().Be("/translate ");
        cut.Find("textarea").GetAttribute("aria-expanded").Should().Be("false");
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>(
            "the pointer path can activate an option, so the focus has to come back to the prompt");
    }

    [Fact]
    public void Escape_ClosesTheListboxAndKeepsTheDraft()
    {
        SetupInterop();
        var cut = RenderWithCommands();

        cut.Find("textarea").Input("/sum");
        cut.WaitForAssertion(() => cut.FindAll("[role='listbox']").Should().ContainSingle());

        cut.Find("textarea").KeyDown("Escape");

        cut.FindAll("[role='listbox']").Should().BeEmpty();
        cut.Find("textarea").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("textarea").HasAttribute("aria-controls").Should().BeFalse();
        cut.Find("textarea").GetAttribute("value").Should().Be("/sum");
    }

    [Fact]
    public void PickingACommand_RecordsItOnTheNextSubmit()
    {
        SetupInterop();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit))
            .Add(component => component.Commands, new[]
            {
                new AiSlashCommand { Id = "summarise", Title = "summarise", Prompt = "/summarise the thread: " },
            }));

        cut.Find("textarea").Input("/sum");
        cut.Find("textarea").KeyDown("Enter");
        cut.Find("textarea").Input(cut.Find("textarea").GetAttribute("value") + "contract");
        cut.Find("[data-testid='ai-composer-send']").Click();

        submits.Should().ContainSingle();
        submits[0].Text.Should().Be("/summarise the thread: contract");
        submits[0].Command.Should().NotBeNull();
        submits[0].Command!.Id.Should().Be("summarise");
    }

    [Fact]
    public void ASlashInTheMiddleOfAWord_DoesNotOpenTheCommands()
    {
        SetupInterop();
        var cut = RenderWithCommands();

        cut.Find("textarea").Input("and/or");

        cut.FindAll("[role='listbox']").Should().BeEmpty("a slash inside a word is text, not a trigger");
    }

    // ── Focus order across the send/stop swap (WCAG 2.4.3) ──────────────────────────────────────

    /// <summary>
    /// The slot holds ONE control: Send or Stop. Activating Send makes <c>IsGenerating</c> true, so the button
    /// the reader just used leaves the DOM — the focus has to follow the replacement instead of dropping to
    /// <c>&lt;body&gt;</c>. The component reads the DOM BEFORE the swap (one boolean probe) so a generation
    /// started from anywhere else never steals the caret.
    /// </summary>
    [Fact]
    public void SendingWithTheSlotFocused_MovesTheFocusToStop()
    {
        var module = SetupInterop(actionSlotFocused: true);
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { }));
        var slotReference = ReferenceId(cut.Find(".tm-ai-composer__action-slot"));

        cut.Find("textarea").Input("Hello");
        cut.Find("[data-testid='ai-composer-send']").Click();

        cut.Render(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.IsGenerating, true));

        TmAiComposerTests.Count(module, "isFocusWithin").Should().Be(1,
            "the focus probe runs once, on the swap");
        module.Invocations.Last(invocation => invocation.Identifier == "isFocusWithin").Arguments[0]
            .Should().BeOfType<ElementReference>().Subject.Id.Should().Be(slotReference);
        TmAiComposerTests.Count(module, "focusFirst").Should().Be(1,
            "the replacement control in the slot takes the focus");
        module.Invocations.Last(invocation => invocation.Identifier == "focusFirst").Arguments[0]
            .Should().BeOfType<ElementReference>().Subject.Id.Should().Be(slotReference);
        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus",
            "the Stop control is the successor of the Send control, not the prompt");
    }

    /// <summary>
    /// Stop brings Send back. An empty draft leaves that Send disabled, and a disabled button cannot take the
    /// focus — so the prompt is the fallback target rather than a second dead end.
    /// </summary>
    [Fact]
    public void StoppingWithAnEmptyDraft_FocusesThePromptWhenSendCannotTakeIt()
    {
        var module = SetupInterop(actionSlotFocused: true);
        var stopped = 0;
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.OnStop, () => stopped++)
            .Add(component => component.IsGenerating, true));

        var promptReference = ReferenceId(cut.Find("textarea"));
        cut.Find("[data-testid='ai-composer-stop']").Click();
        stopped.Should().Be(1);

        cut.Render(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.OnStop, () => stopped++)
            .Add(component => component.IsGenerating, false));

        TmAiComposerTests.Count(module, "focusFirst").Should().Be(0,
            "a disabled Send cannot receive the focus, so asking for it would lose the reader anyway");
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>()
            .Subject.Id.Should().Be(promptReference, "the prompt is the fallback target when Send is disabled");
    }

    /// <summary>
    /// The keyboard path keeps the caret: an Enter fast enough to start a generation before the host re-renders
    /// must not pull the reader out of the prompt and into Stop.
    /// </summary>
    [Fact]
    public void AGenerationStartingWhileThePromptHoldsTheFocus_MovesNothing()
    {
        var module = SetupInterop(actionSlotFocused: false);
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { }));

        cut.Render(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.IsGenerating, true));

        TmAiComposerTests.Count(module, "isFocusWithin").Should().Be(1);
        TmAiComposerTests.Count(module, "focusFirst").Should().Be(0);
        JSInterop.Invocations.Should().NotContain(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus",
            "the prompt already holds the focus — the swap must not move it");
    }
}
