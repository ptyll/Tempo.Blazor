using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.AI.Services;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of <see cref="TmAiComposer"/> (plan 7f21f26b, Fáze 6): the draft, the send/stop switch, the
/// attachments with their limits, the model selector and its template slot, the opt-in voice input and the one
/// live region of the component.
/// </summary>
/// <remarks>
/// The keyboard and suggestion-list halves live in <c>TmAiComposerKeyboardTests</c> and
/// <c>TmAiComposerSuggestionTests</c>; the file-selection limits in <c>TmAiComposerAttachmentTests</c>; the
/// stylesheet gate is <c>TmAiComposerCssTests</c>.
/// </remarks>
public sealed class TmAiComposerTests : LocalizationTestBase
{
    internal const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-composer.js";

    /// <summary>
    /// Registers the package module the component imports on its first render. Every export is answered
    /// explicitly: an unmocked export in <c>JSRuntimeMode.Loose</c> answers a default value, which can flip
    /// component state and add renders a counting assertion would then blame on the component.
    /// </summary>
    internal BunitJSModuleInterop SetupInterop(bool speechSupported = false)
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("resize", _ => true).SetVoidResult();
        module.SetupVoid("placeSuggestions", _ => true).SetVoidResult();
        module.Setup<bool>("speechSupported", _ => true).SetResult(speechSupported);
        module.Setup<bool>("startSpeech", _ => true).SetResult(true);
        module.SetupVoid("stopSpeech", _ => true).SetVoidResult();
        module.SetupVoid("attachViewportTracker", _ => true).SetVoidResult();
        module.SetupVoid("detachViewportTracker", _ => true).SetVoidResult();
        module.Setup<bool>("isFocusWithin", _ => true).SetResult(false);
        module.SetupVoid("focusFirst", _ => true).SetVoidResult();
        return module;
    }

    internal IRenderedComponent<TmAiComposer> RenderComposer(
        Action<ComponentParameterCollectionBuilder<TmAiComposer>>? configure = null)
        => Render<TmAiComposer>(parameters => configure?.Invoke(parameters));

    internal static int Count(BunitJSModuleInterop module, string identifier)
        => module.Invocations.Count(invocation => invocation.Identifier == identifier);

    internal static T ArgumentAt<T>(BunitJSModuleInterop module, string identifier, int index)
        => (T)module.Invocations.Last(invocation => invocation.Identifier == identifier).Arguments[index]!;

    internal static object? OptionValue(BunitJSModuleInterop module, string identifier, string property, int index = 2)
    {
        var options = module.Invocations.Last(invocation => invocation.Identifier == identifier).Arguments[index];
        return options?.GetType().GetProperty(property)?.GetValue(options);
    }

    // ── The root and the textarea ────────────────────────────────────────────────────────────────

    [Fact]
    public void Root_CarriesTheStableTestIdAndTheGeneratingFlag()
    {
        SetupInterop();
        var cut = RenderComposer();

        var root = cut.Find(".tm-ai-composer");
        root.GetAttribute("data-testid").Should().Be("ai-composer");
        root.GetAttribute("data-generating").Should().Be("false");
    }

    /// <summary>
    /// The textarea is the combobox of the suggestion list: a stable role with
    /// <c>aria-autocomplete="list"</c>, a collapsed popup while nothing is suggested, and NO
    /// <c>aria-controls</c> — the attribute may only name a listbox that is actually rendered.
    /// </summary>
    [Fact]
    public void Textarea_IsTheComboboxAndNamesNoMissingListbox()
    {
        SetupInterop();
        var cut = RenderComposer();

        var input = cut.Find("textarea");
        input.GetAttribute("role").Should().Be("combobox");
        input.GetAttribute("aria-autocomplete").Should().Be("list");
        input.GetAttribute("aria-expanded").Should().Be("false");
        input.HasAttribute("aria-controls").Should().BeFalse(
            "a collapsed combobox must not point at a listbox that is not in the DOM");
        input.HasAttribute("aria-activedescendant").Should().BeFalse();
        input.GetAttribute("aria-label").Should().Be("Message");
        input.HasAttribute("aria-describedby").Should().BeFalse(
            "the transient status text must not become the prompt's permanent description — a stale " +
            "'too many files' would be re-read on every focus");
    }

    [Fact]
    public void Placeholder_IsLocalizedUnlessTheHostSuppliesOne()
    {
        SetupInterop();
        RenderComposer().Find("textarea").GetAttribute("placeholder").Should().Be("Ask anything…");

        RenderComposer(parameters => parameters.Add(component => component.Placeholder, "Type here")).Find("textarea")
            .GetAttribute("placeholder").Should().Be("Type here");
    }

    /// <summary>
    /// Exactly ONE live region for the whole composer: attachment errors, voice failures and status notes are
    /// additions to the same polite region instead of a second announcer per feature.
    /// </summary>
    [Fact]
    public void Composer_OwnsExactlyOnePoliteLiveRegion()
    {
        SetupInterop();
        var cut = RenderComposer();

        var regions = cut.FindAll("[role='status'], [aria-live], [role='alert'], [role='log']");
        regions.Should().ContainSingle("the composer announces through one polite live region");
        regions[0].GetAttribute("role").Should().Be("status");
        regions[0].GetAttribute("aria-live").Should().Be("polite");
    }

    /// <summary>
    /// WCAG 4.1.3: a live region announces a CHANGE to a node the accessibility tree already holds. A region
    /// that is destroyed and re-created with its text already filled in (the old <c>@key</c> on the
    /// paragraph) is often silent in NVDA, JAWS and VoiceOver — so the paragraph has to survive and only the
    /// text inside it may be replaced.
    /// </summary>
    [Fact]
    public void TheLiveRegionParagraphSurvivesEveryAnnouncement()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.MaxAttachments, 1)
            .Add(component => component.MaxFileSize, 1024));

        cut.FindComponent<InputFile>().UploadFiles(File("a.txt"));
        cut.FindAll(".tm-ai-composer__attachment").Should().ContainSingle();

        cut.FindComponent<InputFile>().UploadFiles(File("b.txt"));
        cut.Find("[data-testid='ai-composer-announcement']").TextContent.Trim()
            .Should().Be("You can attach at most 1 file.");

        // The paragraph comes from the raw AngleSharp tree: Find() hands out a fresh wrapper per call, which
        // cannot answer "is this the same DOM node as before?".
        var container = (IElement)cut.Nodes[0];
        var region = container.QuerySelector("[data-testid='ai-composer-announcement']");
        var firstText = region!.QuerySelector("span");

        firstText.Should().NotBeNull("the text lives in the keyed child, not directly in the region");

        cut.FindComponent<InputFile>().UploadFiles(File("c.txt"));
        var again = container.QuerySelector("[data-testid='ai-composer-announcement']");

        again.Should().BeSameAs(region,
            "the role=status node must stay in the accessibility tree; only its content changes");
        again!.TextContent.Trim().Should().Be("You can attach at most 1 file.",
            "an identical wording is announced again because the keyed child is a new node");
        again.QuerySelector("span").Should().NotBeNull(
            "a repeated wording replaces the keyed CHILD of the region instead of the region itself");
    }

    internal static InputFileContent File(string name, string body = "hello")
        => InputFileContent.CreateFromText(body, name, contentType: "text/plain");

    // ── Draft and submit ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Typing_RaisesTheDraftBinding()
    {
        SetupInterop();
        var changes = new List<string?>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.Text, string.Empty)
            .Add(component => component.TextChanged, value => changes.Add(value)));

        cut.Find("textarea").Input("Hello");

        changes.Should().Equal("Hello");
        cut.Find("textarea").GetAttribute("value").Should().Be("Hello");
    }

    [Fact]
    public void Send_IsDisabledWhileTheDraftIsEmptyOrWhitespace()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { }));

        cut.Find("[data-testid='ai-composer-send']").HasAttribute("disabled").Should().BeTrue(
            "an empty prompt has nothing to send");

        cut.Find("textarea").Input("   ");
        cut.Find("[data-testid='ai-composer-send']").HasAttribute("disabled").Should().BeTrue(
            "a whitespace-only prompt is empty");
    }

    [Fact]
    public void Send_RaisesOnSubmitWithTheTrimmedTextAndClearsTheDraft()
    {
        SetupInterop();
        var submits = new List<AiComposerSubmit>();
        var changes = new List<string?>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit))
            .Add(component => component.Text, string.Empty)
            .Add(component => component.TextChanged, value => changes.Add(value)));

        cut.Find("textarea").Input("  Summarise the report  ");
        cut.Find("[data-testid='ai-composer-send']").Click();

        submits.Should().ContainSingle();
        submits[0].Text.Should().Be("Summarise the report");
        submits[0].Attachments.Should().BeEmpty();
        submits[0].Mentions.Should().BeEmpty();
        submits[0].Command.Should().BeNull();

        cut.Find("textarea").GetAttribute("value").Should().BeEmpty("the composer clears after a successful send");
        changes.Should().Equal("  Summarise the report  ", string.Empty);
    }

    [Fact]
    public void Send_IsNotOfferedWithoutAnOnSubmitHandler()
    {
        SetupInterop();
        var cut = RenderComposer();

        cut.Find("[data-testid='ai-composer-send']").HasAttribute("disabled").Should().BeTrue(
            "a send button that raises nothing is not offered");
    }

    [Fact]
    public async Task AThrowingSubmitHandler_KeepsTheDraft()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.OnSubmit,
            (AiComposerSubmit _) => throw new InvalidOperationException("host failed")));

        cut.Find("textarea").Input("Keep me");
        var act = async () => await cut.InvokeAsync(() => cut.Instance.SubmitAsync());

        await act.Should().ThrowAsync<InvalidOperationException>();
        cut.Find("textarea").GetAttribute("value").Should().Be("Keep me");
        cut.Find("[data-testid='ai-composer-send']").HasAttribute("disabled").Should().BeFalse(
            "a failed generation must not leave the slot disabled — the reader has to be able to retry");
    }

    /// <summary>
    /// One submit at a time. <c>IsGenerating</c> is a parameter, so it does not move until the host re-renders
    /// the composer — a fast second Enter or a double click would otherwise reach <c>OnSubmit</c> twice with
    /// the same text and start two generations.
    /// </summary>
    [Fact]
    public async Task AFastSecondSubmit_IsIgnoredWhileTheFirstHandlerIsStillRunning()
    {
        SetupInterop();
        var gate = new TaskCompletionSource();
        var submits = 0;
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, EventCallback.Factory.Create<AiComposerSubmit>(
                this, async (AiComposerSubmit _) =>
                {
                    submits++;
                    await gate.Task;
                })));

        cut.Find("textarea").Input("Hello");
        var first = cut.InvokeAsync(() => cut.Instance.SubmitAsync());

        cut.Find("[data-testid='ai-composer-send']").HasAttribute("disabled").Should().BeTrue(
            "an in-flight submit disables the slot, so the second click cannot pass CanSubmit");

        await cut.InvokeAsync(() => cut.Instance.SubmitAsync());
        submits.Should().Be(1, "the second activation was a no-op");
        first.IsCompleted.Should().BeFalse("the first handler is still running");

        await cut.InvokeAsync(() => gate.SetResult());
        await first;

        cut.Find("textarea").GetAttribute("value").Should().BeEmpty("the draft clears once the send completed");
        cut.Find("[data-testid='ai-composer-send']").HasAttribute("disabled").Should().BeTrue(
            "the composer is idle again with an empty draft");
    }

    /// <summary>
    /// A slow host handler must not swallow text the reader typed while it ran. The clear is a compare against
    /// the sent snapshot, not a blind reset.
    /// </summary>
    [Fact]
    public async Task TextTypedWhileTheHandlerRuns_SurvivesThePostSubmitClear()
    {
        SetupInterop();
        var gate = new TaskCompletionSource();
        var changes = new List<string?>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, EventCallback.Factory.Create<AiComposerSubmit>(
                this, async (AiComposerSubmit _) => await gate.Task))
            .Add(component => component.Text, string.Empty)
            .Add(component => component.TextChanged, value => changes.Add(value)));

        cut.Find("textarea").Input("First prompt");
        var first = cut.InvokeAsync(() => cut.Instance.SubmitAsync());

        cut.Find("textarea").Input("A second thought");
        await cut.InvokeAsync(() => gate.SetResult());
        await first;

        cut.Find("textarea").GetAttribute("value").Should().Be("A second thought",
            "clearing the draft blindly would throw away what the reader typed during the send");
        changes.Should().Equal("First prompt", "A second thought");
    }

    // ── Generating: Send becomes Stop ──────────────────────────────────────────────────────────

    [Fact]
    public async Task IsGenerating_SwitchesSendToStop()
    {
        SetupInterop();
        var stopped = 0;
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.OnStop, () => stopped++));

        cut.FindAll("[data-testid='ai-composer-send']").Should().ContainSingle();
        cut.FindAll("[data-testid='ai-composer-stop']").Should().BeEmpty();

        cut.Render(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.OnStop, () => stopped++)
            .Add(component => component.IsGenerating, true));

        cut.FindAll("[data-testid='ai-composer-send']").Should().BeEmpty(
            "one control occupies the slot: a running generation offers Stop, not a second Send");
        var stop = cut.Find("[data-testid='ai-composer-stop']");
        stop.GetAttribute("aria-label").Should().Be("Stop generating");

        stop.Click();
        stopped.Should().Be(1);
    }

    [Fact]
    public void Disabled_DisablesTheTextareaAndTheControls()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.Disabled, true));

        cut.Find("textarea").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='ai-composer-send']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("input[type='file']").HasAttribute("disabled").Should().BeTrue();
    }

    // ── Model selector ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ModelSelector_RendersTheModelsAndTheSelectedOne()
    {
        SetupInterop();
        var selected = new List<string?>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.Models, new[] { "gpt-4o", "o3-mini" })
            .Add(component => component.SelectedModel, "o3-mini")
            .Add(component => component.SelectedModelChanged, value => selected.Add(value)));

        var select = cut.Find("[data-testid='ai-composer-model']");
        select.GetAttribute("aria-label").Should().Be("Model");
        select.QuerySelectorAll("option").Select(option => option.TextContent.Trim())
            .Should().Equal("gpt-4o", "o3-mini");
        select.QuerySelector("option[selected]")!.TextContent.Trim().Should().Be("o3-mini");

        select.Change("gpt-4o");
        selected.Should().Equal("gpt-4o");
    }

    [Fact]
    public void ModelSelector_RendersNothingWithoutModelsOrATemplate()
    {
        SetupInterop();
        var cut = RenderComposer();

        cut.FindAll("[data-testid='ai-composer-model']").Should().BeEmpty();
    }

    [Fact]
    public void ModelSelectorTemplate_ReplacesTheBuiltInSelect()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.Models, new[] { "gpt-4o" })
            .Add(component => component.ModelSelectorTemplate, builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "host-model-picker");
                builder.AddContent(2, "host picker");
                builder.CloseElement();
            }));

        cut.FindAll("[data-testid='ai-composer-model']").Should().BeEmpty(
            "the template is the host's picker — the built-in select would be a second control");
        cut.Find(".host-model-picker").TextContent.Should().Contain("host picker");
    }

    [Fact]
    public void Submit_CarriesTheSelectedModel()
    {
        SetupInterop();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.Models, new[] { "gpt-4o", "o3-mini" })
            .Add(component => component.SelectedModel, "o3-mini")
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").Input("Hello");
        cut.Find("[data-testid='ai-composer-send']").Click();

        submits.Should().ContainSingle();
        submits[0].Model.Should().Be("o3-mini");
    }

    // ── Attachments ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PickedFiles_RenderAsChipsAndTravelWithTheSubmit()
    {
        SetupInterop();
        var changed = new List<IReadOnlyList<AiComposerAttachment>>();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit))
            .Add(component => component.AttachmentsChanged, value => changed.Add(value)));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("hello", "notes.txt", contentType: "text/plain"));

        cut.FindAll(".tm-ai-composer__attachment").Should().ContainSingle();
        cut.Find(".tm-ai-composer__attachment-name").TextContent.Trim().Should().Be("notes.txt");
        cut.Find("[data-testid='ai-composer-remove-attachment']").GetAttribute("aria-label")
            .Should().Be("Remove notes.txt", "a repeated row control is named by its file (WCAG 2.4.6)");

        changed.Should().ContainSingle();
        changed[0].Should().ContainSingle();
        changed[0][0].Name.Should().Be("notes.txt");
        changed[0][0].SizeBytes.Should().Be(5);
        changed[0][0].ContentType.Should().Be("text/plain");

        cut.Find("textarea").Input("Look at this");
        cut.Find("[data-testid='ai-composer-send']").Click();

        submits.Should().ContainSingle();
        submits[0].Attachments.Should().ContainSingle();
        submits[0].Attachments[0].Name.Should().Be("notes.txt");
        cut.FindAll(".tm-ai-composer__attachment").Should().BeEmpty("the draft and its files are cleared together");
    }

    [Fact]
    public void RemoveAttachment_DropsTheChipAndFocusesTheInput()
    {
        SetupInterop();
        var changed = new List<IReadOnlyList<AiComposerAttachment>>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.AttachmentsChanged, value => changed.Add(value)));

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("a", "a.txt", contentType: "text/plain"),
            InputFileContent.CreateFromText("b", "b.txt", contentType: "text/plain"));
        cut.FindAll(".tm-ai-composer__attachment").Should().HaveCount(2);

        cut.FindAll("[data-testid='ai-composer-remove-attachment']")[0].Click();

        cut.FindAll(".tm-ai-composer__attachment").Should().ContainSingle();
        changed[^1].Select(attachment => attachment.Name).Should().Equal("b.txt");
        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].Should().BeOfType<ElementReference>(
            "the Remove control left the DOM — the focus returns to the prompt instead of <body>");
    }

    [Fact]
    public void AttachmentsParameter_SeedsTheChips()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.Attachments,
            new[] { Attachment("seed.txt", 12) }));

        cut.FindAll(".tm-ai-composer__attachment").Should().ContainSingle();
        cut.Find(".tm-ai-composer__attachment-name").TextContent.Trim().Should().Be("seed.txt");
        cut.Find(".tm-ai-composer__attachment-size").TextContent.Trim().Should().Be("12 B");
    }

    [Fact]
    public void EnableAttachmentsFalse_RemovesTheFileControl()
    {
        SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.EnableAttachments, false));

        cut.FindComponents<InputFile>().Should().BeEmpty();
        cut.FindAll("[data-testid='ai-composer-attach']").Should().BeEmpty();
    }

    internal static AiComposerAttachment Attachment(string name, long size, string? contentType = null)
        => new() { Name = name, SizeBytes = size, ContentType = contentType };

    // ── Voice input ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VoiceInput_IsNotOfferedByDefault()
    {
        var module = SetupInterop(speechSupported: true);
        var cut = RenderComposer();

        cut.FindAll("[data-testid='ai-composer-voice']").Should().BeEmpty("voice is an opt-in");
        Count(module, "speechSupported").Should().Be(0,
            "a component that never offers voice does not probe the browser for it");
    }

    [Fact]
    public void VoiceInput_IsHiddenWhenTheBrowserHasNoSpeechRecognition()
    {
        SetupInterop(speechSupported: false);
        var cut = RenderComposer(parameters => parameters.Add(component => component.EnableVoiceInput, true));

        cut.FindAll("[data-testid='ai-composer-voice']").Should().BeEmpty(
            "a control that cannot work must not be rendered");
    }

    [Fact]
    public async Task VoiceInput_StartsSpeechAndAppendsTheTranscript()
    {
        var module = SetupInterop(speechSupported: true);
        var changes = new List<string?>();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.EnableVoiceInput, true)
            .Add(component => component.Text, string.Empty)
            .Add(component => component.TextChanged, value => changes.Add(value)));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-composer-voice']").Should().ContainSingle());
        var voice = cut.Find("[data-testid='ai-composer-voice']");
        voice.GetAttribute("aria-label").Should().Be("Start voice input");
        voice.GetAttribute("aria-pressed").Should().Be("false");

        voice.Click();
        Count(module, "startSpeech").Should().Be(1);
        cut.Find("[data-testid='ai-composer-voice']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='ai-composer-voice']").GetAttribute("aria-label").Should().Be("Stop voice input");

        await cut.InvokeAsync(() => cut.Instance.OnSpeechResult("Hello there", true));
        changes.Should().Contain("Hello there");
        cut.Find("textarea").GetAttribute("value").Should().Be("Hello there");

        await cut.InvokeAsync(() => cut.Instance.OnSpeechEnded());
        cut.Find("[data-testid='ai-composer-voice']").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid='ai-composer-announcement']").TextContent.Trim().Should().BeEmpty(
            "the note is also the prompt's description — a stale 'listening' next to a stopped microphone is a lie");
    }

    [Fact]
    public async Task VoiceInput_ReportsAFailureThroughTheLiveRegion()
    {
        SetupInterop(speechSupported: true);
        var cut = RenderComposer(parameters => parameters.Add(component => component.EnableVoiceInput, true));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='ai-composer-voice']").Should().ContainSingle());
        cut.Find("[data-testid='ai-composer-voice']").Click();
        await cut.InvokeAsync(() => cut.Instance.OnSpeechError("not-allowed"));

        var announcement = cut.Find("[data-testid='ai-composer-announcement']");
        announcement.TextContent.Trim().Should().Be("Voice input failed.");
        announcement.GetAttribute("role").Should().Be("status");
        cut.Find("[data-testid='ai-composer-voice']").GetAttribute("aria-pressed").Should().Be("false");
    }

    // ── Autosize interop ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The module owns the autosize: it is attached with the row cap, the first render measures the prompt, and a
    /// programmatic clear (which raises no <c>input</c> event) has to re-measure explicitly — otherwise the
    /// composer keeps the height of the text it no longer holds.
    /// </summary>
    [Fact]
    public void Autosize_IsAttachedWithTheRowCapAndReMeasuredAfterAClear()
    {
        var module = SetupInterop();
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit _) => { })
            .Add(component => component.MaxRows, 5));

        cut.WaitForAssertion(() => Count(module, "attach").Should().Be(1));
        OptionValue(module, "attach", "maxRows").Should().Be(5);

        cut.WaitForAssertion(() => Count(module, "resize").Should().Be(1));
        ArgumentAt<int>(module, "resize", 1).Should().Be(5, "the row cap travels with every measurement");

        cut.Find("textarea").Input("A long prompt");
        cut.Find("[data-testid='ai-composer-send']").Click();

        // Clearing the draft is not an input event, so the component has to ask for the measurement.
        cut.WaitForAssertion(() => Count(module, "resize").Should().Be(2));
        ArgumentAt<int>(module, "resize", 1).Should().Be(5);
    }


    // ── Layout and lifetime ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Sticky_AddsTheModifierClass()
    {
        SetupInterop();
        RenderComposer().Find(".tm-ai-composer").ClassList.Should().NotContain("tm-ai-composer--sticky");

        RenderComposer(parameters => parameters.Add(component => component.Sticky, true))
            .Find(".tm-ai-composer").ClassList.Should().Contain("tm-ai-composer--sticky");
    }

    [Fact]
    public void TrackVirtualKeyboard_AttachesTheViewportTracker()
    {
        var module = SetupInterop();
        var cut = RenderComposer(parameters => parameters.Add(component => component.TrackVirtualKeyboard, true));

        cut.WaitForAssertion(() => Count(module, "attachViewportTracker").Should().Be(1));
    }

    [Fact]
    public async Task Dispose_ReleasesTheModuleListenersAndTheSpeechSession()
    {
        var module = SetupInterop(speechSupported: true);
        var cut = RenderComposer(parameters => parameters
            .Add(component => component.EnableVoiceInput, true)
            .Add(component => component.TrackVirtualKeyboard, true));

        cut.WaitForAssertion(() => Count(module, "attach").Should().Be(1));
        cut.WaitForAssertion(() => Count(module, "attachViewportTracker").Should().Be(1));

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        Count(module, "stopSpeech").Should().Be(1, "a disposed composer stops listening");
        Count(module, "detachViewportTracker").Should().Be(1);
        Count(module, "detach").Should().Be(1);
    }

}
