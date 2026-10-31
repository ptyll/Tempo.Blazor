using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Signing redesign phase 5 (S2-4, SI3): the mobile sheet grows a HANDLE (its ONLY aria-expanded
/// toggle), a PEEK state that keeps the progress and the primary call to action reachable, a FULL
/// edit state that every navigation lands in (step panel + sticky action bar, field list hidden),
/// a "More" overflow in the action row, and the active field is kept visible by an auto-scroll the
/// after-render pass performs (the new <c>signing-runner.js</c> module). The tests FORCE
/// <c>LayoutMode=Mobile</c> because bUnit cannot evaluate CSS container queries — the same
/// contract the forced-layout editors use (F1).
/// </summary>
public class TmSigningFormRunnerMobileTests : LocalizationTestBase
{
    private const string RunnerModule = "./_content/Tempo.Blazor.Signing/js/signing-runner.js";

    // ── Sheet handle ─────────────────────────────────────────────

    /// <summary>The handle is the sheet's ONLY aria-expanded toggle: one control, always visible
    /// in the mobile sheet, whose <c>aria-expanded</c> describes the sheet state. From the FULL
    /// edit state it collapses straight to peek (phase 5 review).</summary>
    [Fact]
    public void Runner_Mobile_HandleTogglesAriaExpanded()
    {
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Collapsed);

        var handle = cut.Find(".tm-signing-form-runner__sheet-handle");
        handle.GetAttribute("aria-expanded").Should().Be("false", "the sheet starts collapsed (peek)");
        handle.GetAttribute("aria-controls").Should().Be(cut.Find(".tm-signing-form-runner__step-panel").Id,
            "the handle names the ONE step panel it toggles");

        handle.Click();

        cut.Find(".tm-signing-form-runner__sheet-handle").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain(
            "tm-signing-form-runner__steps--sheet-expanded",
            "the handle expands the same single rail (B25: one panel instance)").And.NotContain(
            "tm-signing-form-runner__steps--sheet-full",
            "the handle expands onto the LIST presentation, not the full edit state");

        cut.Find(".tm-signing-form-runner__sheet-handle").Click();

        cut.Find(".tm-signing-form-runner__sheet-handle").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain(
            "tm-signing-form-runner__steps--sheet-collapsed");
    }

    /// <summary>The peek state (today's Collapsed) is not a dead end: it keeps the done count and
    /// the primary "Sign now" call to action visible, so the signer can measure progress and
    /// re-enter the panel without expanding blind (phase 5). The count is the strip's own polite
    /// status region while collapsed (phase 5 review).</summary>
    [Fact]
    public void Runner_Mobile_PeekContainsProgressAndPrimaryCta()
    {
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Collapsed);

        var peek = cut.Find(".tm-signing-form-runner__sheet-progress");
        peek.TextContent.Should().Contain("of", "the peek strip carries the done count (\"{0} of {1} done\")")
            .And.Contain("done");
        peek.GetAttribute("role").Should().Be("status",
            "the collapsed sheet's count is its own status region (the header live region is display:none)");

        cut.Find(".tm-signing-form-runner__mobile-expand").Should().NotBeNull(
            "the primary CTA (Sign now) stays visible in the peek state");
    }

    /// <summary>While EXPANDED the peek strip is hidden (the panel header's live region takes
    /// over) — the strip drops its status role so two progress regions never both announce.</summary>
    [Fact]
    public void Runner_Mobile_ExpandedSheet_DropsThePeekProgressRole()
    {
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);

        cut.Find(".tm-signing-form-runner__sheet-progress").GetAttribute("role").Should().BeNull(
            "the expanded sheet announces progress through the panel header's live region only");
    }

    // ── Full state (phase 5 review) ──────────────────────────────

    /// <summary>Every mobile navigation lands in the FULL edit state: step panel + sticky action
    /// bar visible, field list hidden (the CSS folds it away under <c>--sheet-full</c>).</summary>
    [Fact]
    public async Task Runner_Mobile_NextLandsInFullState_FieldListHidden()
    {
        SetupRunnerModule();
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);
        await cut.Find("input.tm-signing-text-step__input").ChangeAsync(new ChangeEventArgs { Value = "Alice" });
        await cut.Find(".tm-signing-form-runner__next").ClickAsync(new MouseEventArgs());

        var steps = cut.Find(".tm-signing-form-runner__steps").ClassList;
        steps.Should().Contain("tm-signing-form-runner__steps--sheet-full",
                "Next lands the sheet in the full edit state")
            .And.Contain("tm-signing-form-runner__steps--sheet-expanded",
                "full rides the expanded sheet frame (one CSS presentation)");

        FullStateHidesFieldList_CssContract();
    }

    /// <summary>A field list tap also lands in full — the list is a picker, the edit happens in
    /// the full sheet (phase 5 review).</summary>
    [Fact]
    public async Task Runner_Mobile_FieldListTapLandsInFullState()
    {
        SetupRunnerModule();
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);

        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='second']")
            .ClickAsync(new MouseEventArgs());

        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain(
            "tm-signing-form-runner__steps--sheet-full",
            "a list tap is a navigation — the sheet lands in the full edit state");
    }

    /// <summary>The More menu's "Field list" entry switches the full edit state onto the list
    /// presentation (full → expanded) and hands focus to the current field's row.</summary>
    [Fact]
    public async Task Runner_Mobile_MoreMenu_FieldListSwitchesFullToExpanded_AndFocusesTheRow()
    {
        var module = SetupRunnerModule();
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);
        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='second']")
            .ClickAsync(new MouseEventArgs());
        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain("tm-signing-form-runner__steps--sheet-full");

        await OpenMoreMenuAsync(cut);
        await cut.Find(".tm-signing-form-runner__more-fields").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain(
                "tm-signing-form-runner__steps--sheet-expanded")
            .And.NotContain("tm-signing-form-runner__steps--sheet-full",
                "\"Field list\" leaves the full edit state");

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "focusFieldButton"
                && i.Arguments.Contains("second"),
            "the current field's row is scrolled into view and focused"));
    }

    /// <summary>The CSS contract of the full state: the field list folds away under
    /// <c>--sheet-full</c> while the step panel and the sticky action bar stay visible.</summary>
    [Fact]
    public void FullStateHidesFieldList_CssContract()
    {
        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components", "_signing-form-runner.css"));

        css.Should().Contain(".tm-signing-form-runner__steps--sheet-full .tm-signing-form-runner__field-list",
            "the full state hides the field list (CSS-only, no new public parameter)");
    }

    // ── More menu ────────────────────────────────────────────────

    /// <summary>The mobile sticky action row's "More" overflow carries the secondary actions (Skip,
    /// Language, Field list, Show whole document). Skip appears ONLY while the CURRENT field is
    /// optional — the desktop Skip row already renders only for an optional non-final field.</summary>
    [Fact]
    public async Task Runner_Mobile_MoreMenu_ContainsSkipOnlyForOptionalField()
    {
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);
        await OpenMoreMenuAsync(cut);

        cut.FindAll(".tm-signing-form-runner__more-skip").Should().ContainSingle(
            "the first field is optional, so the More menu carries Skip");

        // Skipping advances to the required second field: the reopened menu must drop Skip.
        await cut.Find(".tm-signing-form-runner__more-skip").ClickAsync(new MouseEventArgs());
        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Second");

        await OpenMoreMenuAsync(cut);
        cut.FindAll(".tm-signing-form-runner__more-skip").Should().BeEmpty(
            "the second field is required — Skip must leave the More menu");
    }

    [Fact]
    public async Task Runner_Mobile_MoreMenu_RequiredField_HasNoSkip()
    {
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded, firstRequired: true);
        await OpenMoreMenuAsync(cut);

        cut.FindAll(".tm-signing-form-runner__more-skip").Should().BeEmpty(
            "no optional current field — no Skip in the More menu");
        cut.FindAll(".tm-signing-form-runner__more-fields").Should().ContainSingle("the field list entry is permanent");
        cut.FindAll(".tm-signing-form-runner__more-document").Should().ContainSingle(
            "\"Show whole document\" collapses the sheet to the peek state");
    }

    [Fact]
    public async Task Runner_Mobile_MoreMenu_ShowDocument_CollapsesToPeek()
    {
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);
        await OpenMoreMenuAsync(cut);

        await cut.Find(".tm-signing-form-runner__more-document").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain(
                "tm-signing-form-runner__steps--sheet-collapsed",
                "\"Show whole document\" collapses the sheet so the document shows (peek)")
            .And.NotContain("tm-signing-form-runner__steps--sheet-full");
    }

    /// <summary>The More menu's Language entry hands the focus to the sheet's language selector
    /// (expanding the sheet first when it was collapsed) — the picker itself stays ONE control.</summary>
    [Fact]
    public async Task Runner_Mobile_MoreMenu_Language_ExpandsSheetForTheLanguageSelect()
    {
        SetupRunnerModule();
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Collapsed);
        await OpenMoreMenuAsync(cut);

        await cut.Find(".tm-signing-form-runner__more-language").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
            cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain(
                "tm-signing-form-runner__steps--sheet-expanded",
                "focusing the language select requires the expanded sheet"));
    }

    // ── Auto-scroll ──────────────────────────────────────────────

    /// <summary>The active field must stay visible above the sheet: every step landing RECORDS the
    /// pending scroll and the after-render pass invokes the signing-runner module's
    /// <c>scrollToField</c> with the NEW current field's uuid (phase 5 review — handlers never
    /// measure, the scroll runs after the render applied the sheet state).</summary>
    [Fact]
    public async Task Runner_GoNext_CallsScrollToField_WithActiveFieldUuid()
    {
        var module = SetupRunnerModule();
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);

        await cut.Find("input.tm-signing-text-step__input").ChangeAsync(new ChangeEventArgs { Value = "Alice" });
        await cut.Find(".tm-signing-form-runner__next").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var invocation = module.Invocations.Should().Contain(i => i.Identifier == "scrollToField",
                "advancing to the next step scrolls the active field into view").Subject;
            invocation.Arguments.Should().ContainSingle(
                "the scroll targets the NEW current field").Which.Should().Be("second");
        });
    }

    [Fact]
    public async Task Runner_FieldListClick_CallsScrollToField_WithTheSelectedFieldUuid()
    {
        var module = SetupRunnerModule();
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);

        await cut.Find(".tm-signing-form-runner__field-button[data-field-uuid='second']")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "scrollToField"
            && i.Arguments.Contains("second"),
            "a field list click is also a step landing"));
    }

    /// <summary>Clicking a field overlay on the document opens the sheet on that field's step,
    /// landing in the FULL edit state — the sheet presents the step the signer just asked for
    /// (phase 5; inert on desktop, where the sheet presentation does not exist).</summary>
    [Fact]
    public void Runner_OverlayClick_ExpandsTheMobileSheet()
    {
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Collapsed);

        cut.FindAll(".tm-signing-field-overlay")[1].Click();

        var classes = cut.Find(".tm-signing-form-runner__steps").ClassList;
        classes.Should().Contain("tm-signing-form-runner__steps--sheet-expanded")
            .And.Contain("tm-signing-form-runner__steps--sheet-full", "an overlay tap lands in the full edit state");
        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Second");
    }

    // ── SI3: DefaultSignatureMode ────────────────────────────────

    /// <summary>The template's stored format has PRIORITY over the runner parameter (phase 5, SI3):
    /// a template that asks for drawn signatures stays drawn even when the host prefers typed.</summary>
    [Fact]
    public async Task Runner_DefaultSignatureMode_TemplateFormatWinsOverParameter()
    {
        SetupRunnerModule(coarsePointer: true);
        var cut = RenderMobileRunner(
            mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded,
            defaultSignatureMode: TmSigningDefaultSignatureMode.Typed,
            signatureFormat: "draw",
            signatureOnly: true);

        await Task.Yield();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-signature-capture").GetAttribute("data-mode").Should().Be("Draw",
                "the template's Preferences.Format=\"draw\" wins over DefaultSignatureMode=Typed"));
    }

    /// <summary>When the template is silent, an explicit parameter mode (Draw/Typed/Upload) applies
    /// on EVERY pointer type — the parameter exists so a host can pin the ceremony.</summary>
    [Fact]
    public void Runner_DefaultSignatureMode_ParameterChoosesModeWhenTemplateSilent()
    {
        SetupRunnerModule(coarsePointer: false);
        var cut = RenderMobileRunner(
            mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded,
            defaultSignatureMode: TmSigningDefaultSignatureMode.Upload,
            signatureOnly: true);

        cut.Find(".tm-signature-capture").GetAttribute("data-mode").Should().Be("Upload");
    }

    /// <summary>Auto (the default) resolves to DRAW on a coarse pointer — a finger cannot hover, so
    /// the signing pad is the honest first presentation on touch (phase 5, SI3).</summary>
    [Fact]
    public void Runner_DefaultSignatureModeAuto_UsesDrawOnCoarsePointer()
    {
        SetupRunnerModule(coarsePointer: true);
        var cut = RenderMobileRunner(
            mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded,
            signatureOnly: true);

        cut.WaitForAssertion(() =>
            cut.Find(".tm-signature-capture").GetAttribute("data-mode").Should().Be("Draw",
                "Auto = Draw on pointer: coarse"));
    }

    [Fact]
    public void Runner_DefaultSignatureModeAuto_KeepsTypedOnFinePointer()
    {
        SetupRunnerModule(coarsePointer: false);
        var cut = RenderMobileRunner(
            mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded,
            signatureOnly: true);

        cut.WaitForAssertion(() =>
            cut.Find(".tm-signature-capture").GetAttribute("data-mode").Should().Be("Typed",
                "Auto keeps today's Typed default on a fine pointer"));
    }

    /// <summary>Desktop keeps the auto-scroll too (the plan: auto-scroll on the desktop left column
    /// as well) — the module invocation is identical, only the sheet offset is absent.</summary>
    [Fact]
    public async Task Runner_GoPrevious_CallsScrollToField_AfterMovingBack()
    {
        var module = SetupRunnerModule();
        var cut = RenderMobileRunner(mobilePanelMode: TmSigningFormRunnerMobilePanelMode.Expanded);
        await cut.Find("input.tm-signing-text-step__input").ChangeAsync(new ChangeEventArgs { Value = "Alice" });
        await cut.Find(".tm-signing-form-runner__next").ClickAsync(new MouseEventArgs());

        await cut.Find(".tm-signing-form-runner__previous").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => module.Invocations.Should().Contain(i => i.Identifier == "scrollToField"
            && i.Arguments.Contains("first"),
            "moving back re-centres the field the signer returned to"));
    }

    // ── Disposal (phase 5 review) ────────────────────────────────

    /// <summary>The runner is IAsyncDisposable ONLY: disposing the renderer must cancel the
    /// pending autosave debounce, so a change made right before the disposal never fires
    /// <c>OnAutoSave</c> after the clock advances (the old sync Dispose never ran — the renderer
    /// disposes through IAsyncDisposable when both interfaces are implemented).</summary>
    [Fact]
    public async Task Dispose_CancelsPendingAutosave_OnAutoSaveNeverInvoked()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var autosaves = 0;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.AutoSaveDelay, TimeSpan.FromSeconds(1))
            .Add(p => p.OnAutoSave, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, _ => autosaves++)));

        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        await DisposeComponentsAsync();
        time.Advance(TimeSpan.FromSeconds(5));

        autosaves.Should().Be(0, "the pending autosave was cancelled by the runner's DisposeAsync");
        cut.IsDisposed.Should().BeTrue("the component itself is disposed");
    }

    // ── Plumbing ─────────────────────────────────────────────────

    private BunitJSModuleInterop SetupRunnerModule(bool coarsePointer = false)
    {
        var module = JSInterop.SetupModule(RunnerModule);
        module.Setup<bool>("isCoarsePointer").SetResult(coarsePointer);
        module.SetupVoid("scrollToField", _ => true).SetVoidResult();
        module.SetupVoid("focusFieldButton", _ => true).SetVoidResult();
        return module;
    }

    private IRenderedComponent<TmSigningFormRunner> RenderMobileRunner(
        TmSigningFormRunnerMobilePanelMode mobilePanelMode,
        bool secondRequired = true,
        bool firstRequired = false,
        TmSigningDefaultSignatureMode defaultSignatureMode = TmSigningDefaultSignatureMode.Auto,
        string? signatureFormat = null,
        bool signatureOnly = false)
    {
        var signature = CreateField("signature", "Signature", SigningFieldType.Signature, y: 0.3, required: false);
        signature.Preferences.Format = signatureFormat;

        var fields = signatureOnly
            ? new List<SigningField> { signature }
            : new List<SigningField>
            {
                CreateField("first", "First", SigningFieldType.Text, y: 0.1, required: firstRequired),
                CreateField("second", "Second", SigningFieldType.Text, y: 0.2, required: secondRequired),
                signature
            };

        return Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, fields)
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile)
            .Add(p => p.MobilePanelMode, mobilePanelMode)
            .Add(p => p.DefaultSignatureMode, defaultSignatureMode)
            .Add(p => p.ShowLanguageSelector, true)
            .Add(p => p.SupportedCultures, ["en", "cs"]));
    }

    private static async Task OpenMoreMenuAsync(IRenderedComponent<TmSigningFormRunner> cut)
    {
        await cut.Find(".tm-signing-form-runner__more > button").ClickAsync(new MouseEventArgs());
        cut.Find(".tm-signing-form-runner__more > button").GetAttribute("aria-expanded").Should().Be("true");
    }

    private static SigningDocumentPage CreatePage()
    {
        return new SigningDocumentPage
        {
            AttachmentUuid = "doc",
            PageIndex = 0,
            Width = 600,
            Height = 800,
            Label = "Page 1"
        };
    }

    private static SigningField CreateField(
        string uuid,
        string name,
        SigningFieldType type,
        double y = 0.1,
        bool required = false)
    {
        return new SigningField
        {
            Uuid = uuid,
            Name = name,
            Type = type,
            Required = required,
            Areas =
            [
                new SigningFieldArea
                {
                    AttachmentUuid = "doc",
                    Page = 0,
                    X = 0.1,
                    Y = y,
                    Width = 0.2,
                    Height = 0.05
                }
            ]
        };
    }
}
