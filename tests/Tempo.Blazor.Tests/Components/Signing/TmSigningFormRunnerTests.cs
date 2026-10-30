using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

public class TmSigningFormRunnerTests : LocalizationTestBase
{
    [Fact]
    public void Render_NoFields_ShowsEmptyState()
    {
        var cut = Render<TmSigningFormRunner>();

        cut.Find(".tm-signing-form-runner__empty").TextContent.Should().Contain("No fields");
    }

    /// <summary>
    /// B25 (Signing redesign phase 2): the runner used to render the step panel TWICE — once in the
    /// desktop steps rail and once in the mobile panel (hidden by CSS, still in the DOM), so every
    /// input, id and alert existed twice for assistive technology. The single-panel contract:
    /// exactly one step shell, one capture, one progress region and one action row, at any width.
    /// </summary>
    [Fact]
    public void Runner_RendersExactlyOneStepShell_AndOneSignatureCapture()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields,
            [
                CreateField("signature", "Signature", SigningFieldType.Signature)
            ]));

        cut.FindAll(".tm-signing-form-runner__step-panel").Should().ContainSingle(
            "the mobile presentation is CSS-only; the step panel must exist once in the DOM");
        cut.FindAll(".tm-signing-step-shell").Should().ContainSingle();
        cut.FindAll(".tm-signature-capture").Should().ContainSingle();
        cut.FindAll(".tm-signing-form-runner__progress").Should().ContainSingle();
        cut.FindAll(".tm-signing-form-runner__next").Should().ContainSingle(
            "the action row must not be duplicated for screen readers");
        cut.FindAll("input.tm-signing-text-step__input").Should().BeEmpty(
            "only the current step's inputs render — no hidden second panel");
    }

    /// <summary>
    /// One validation alert: the runner forwards its step validation into the step shell, so a
    /// failed Next renders exactly one <c>role="alert"</c> and the runner-level
    /// <c>__validation</c> box stays reserved for CompleteBlocked / completion errors. The
    /// completion-error surface itself keeps working (<see cref="Complete_ErrorShowsValidationState"/>).
    /// </summary>
    [Fact]
    public void Runner_RendersSingleAlertRegion_OnValidationError()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
            ]));

        cut.Find(".tm-signing-form-runner__next").Click();

        cut.FindAll("[role='alert']").Should().ContainSingle(
            "a failed step validation must surface exactly one alert region");
        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("required");
        cut.FindAll(".tm-signing-form-runner__validation").Should().BeEmpty(
            "the runner-level validation box is reserved for CompleteBlocked / completion errors");
    }

    [Fact]
    public void Render_DocumentsOverlaysAndCurrentStep()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)]));

        cut.Find(".tm-document-page-viewer__page").Should().NotBeNull();
        cut.Find(".tm-signing-field-overlay").Should().NotBeNull();
        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Name");
    }

    [Fact]
    public void Overlay_ReceivesDefaultPaletteRoleColorAsTokenReference()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.SubmitterUuid, "role-signer")
            .Add(p => p.SubmitterRoles,
            [
                new SigningSubmitterRole { Uuid = "role-signer", Name = "Signer", Color = "#0891b2" }
            ]));

        cut.Find(".tm-signing-field-overlay")
            .GetAttribute("style").Should().Contain("--tm-signing-role-color:var(--tm-signing-role-5)",
                "the runner paints the current submitter's role colour onto the field overlays; " +
                "a default-palette colour resolves to the --tm-signing-role-5 token");
    }

    [Fact]
    public void Overlay_LegacyRole6Colour_ResolvesToTheRole6Token()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.SubmitterUuid, "role-signer")
            .Add(p => p.SubmitterRoles,
            [
                new SigningSubmitterRole { Uuid = "role-signer", Name = "Signer", Color = "#ca8a04" }
            ]));

        cut.Find(".tm-signing-field-overlay")
            .GetAttribute("style").Should().Contain("--tm-signing-role-color:var(--tm-signing-role-6)",
                "stored templates keep the pre-phase-1 role-6 hex; it must follow theme/host " +
                "overrides of --tm-signing-role-6 exactly like the current default");
    }

    [Fact]
    public void Overlay_RoleWithoutColor_FallsBackToPaletteTokenOfRoleOrder()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.SubmitterUuid, "role-signer")
            .Add(p => p.SubmitterRoles,
            [
                new SigningSubmitterRole { Uuid = "role-approver", Name = "Approver" },
                new SigningSubmitterRole { Uuid = "role-signer", Name = "Signer" }
            ]));

        cut.Find(".tm-signing-field-overlay")
            .GetAttribute("style").Should().Contain("--tm-signing-role-color:var(--tm-signing-role-2)",
                "a colour-less role paints with the palette token of its order (second role → role-2)");
    }

    [Fact]
    public void Overlay_FieldWithoutRoleAndWithoutSubmitter_RendersNoRoleCustomProperty()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)]));

        cut.Find(".tm-signing-field-overlay")
            .GetAttribute("style").Should().NotContain("--tm-signing-role-color");
    }

    [Fact]
    public void ClickOverlay_SelectsMatchingStep()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields,
            [
                CreateField("first", "First", SigningFieldType.Text, y: 0.1),
                CreateField("second", "Second", SigningFieldType.Text, y: 0.2)
            ]));

        cut.FindAll(".tm-signing-field-overlay")[1].Click();

        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Second");
        cut.FindAll(".tm-signing-field-overlay")[1].ClassList.Should().Contain("tm-signing-field--selected");
    }

    /// <remarks>
    /// The Change and the Next click are AWAITED. bUnit's synchronous <c>Change()</c>/<c>Click()</c> only
    /// hand the event to the renderer's dispatcher; when the dispatcher is busy at that moment — this
    /// component's own debounced autosave continuation runs on it — the click is queued and the lines
    /// below read <c>submitted</c> before <c>GoNextAsync</c> ran. That is the CI red
    /// (<c>NullReferenceException</c> on <c>submitted!</c>, run 35862618640). Reproduced locally by
    /// holding the dispatcher from a background thread across the click: the synchronous form throws
    /// that same NullReferenceException, the awaited form passes.
    /// </remarks>
    [Fact]
    public async Task Next_SavesTextAndInvokesStepSubmit()
    {
        IReadOnlyDictionary<string, object?>? values = null;
        SigningStepItem? submitted = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
            ])
            .Add(p => p.ValuesChanged, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, v => values = v))
            .Add(p => p.OnStepSubmit, EventCallback.Factory.Create<SigningStepItem>(this, step => submitted = step)));

        await cut.Find("input.tm-signing-text-step__input").ChangeAsync(new ChangeEventArgs { Value = "Alice" });
        await cut.Find(".tm-signing-form-runner__next").ClickAsync(new MouseEventArgs());

        values.Should().NotBeNull();
        values!["name"].Should().Be("Alice");
        submitted!.Field.Uuid.Should().Be("name");
        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Date");
    }

    [Fact]
    public void Next_RequiredMissing_StaysOnFirstInvalidField()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
            ]));

        cut.Find(".tm-signing-form-runner__next").Click();

        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("required");
        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Name");
    }

    [Fact]
    public void SkipOptional_AdvancesToNextStep()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("middle", "Middle name", SigningFieldType.Text),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
            ]));

        cut.Find(".tm-signing-form-runner__skip").Click();

        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Date");
    }

    [Fact]
    public void Complete_DisabledUntilRequiredValuesArePresent()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text, required: true)]));

        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue();

        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void SignatureStep_DrawModePersistsAfterValueCommit()
    {
        var signatureField = CreateField("signature", "Signature", SigningFieldType.Signature, required: true);
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [signatureField]));

        cut.Find(".tm-signature-capture").GetAttribute("data-mode").Should().Be("Typed");
        var desktopPanel = cut.Find("[data-testid='signing-runner-steps']");
        desktopPanel.QuerySelectorAll(".tm-signature-capture__tab")
            .Single(button => button.TextContent.Contains("Draw", StringComparison.OrdinalIgnoreCase))
            .Click();

        var canvas = cut.Find("[data-testid='signing-runner-steps'] svg.tm-signature-capture__canvas");
        canvas.TriggerEvent("onpointerdown", new PointerEventArgs { OffsetX = 10, OffsetY = 10 });
        canvas.TriggerEvent("onpointermove", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });
        canvas.TriggerEvent("onpointerup", new PointerEventArgs { OffsetX = 20, OffsetY = 20 });

        cut.Find("[data-testid='signing-runner-steps'] .tm-signature-capture").GetAttribute("data-mode").Should().Be("Draw");
        cut.Find("[data-testid='signing-runner-steps'] svg.tm-signature-capture__canvas").Should().NotBeNull();
    }

    [Fact]
    public void Complete_ErrorShowsValidationState()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text, required: true)])
            .Add(p => p.OnComplete, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, _ =>
            {
                throw new InvalidOperationException("Complete service offline");
            })));

        cut.Find("input.tm-signing-text-step__input").Change("Alice");
        cut.Find(".tm-signing-form-runner__complete").Click();

        cut.Find(".tm-signing-form-runner__validation").TextContent.Should().Contain("Complete service offline");
    }

    [Fact]
    public async Task Autosave_DebouncesAndShowsError()
    {
        var autosaves = 0;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.AutoSaveDelay, TimeSpan.FromMilliseconds(20))
            .Add(p => p.OnAutoSave, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, _ =>
            {
                autosaves++;
                throw new InvalidOperationException("Offline");
            })));

        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        cut.WaitForAssertion(() =>
        {
            autosaves.Should().Be(1);
            cut.Find(".tm-signing-form-runner__autosave").TextContent.Should().Contain("Offline");
        });
    }

    [Fact]
    public void Autosave_DebouncesOnFakeClock_ExactlyOneSave()
    {
        // Review fix: the debounce runs on the injected TimeProvider — two rapid changes collapse
        // into ONE autosave when the fake clock advances past the delay, with no real sleeping.
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        var autosaves = 0;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.AutoSaveDelay, TimeSpan.FromSeconds(1))
            .Add(p => p.OnAutoSave, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, _ => autosaves++)));

        var input = cut.Find("input.tm-signing-text-step__input");
        input.Change("A");
        input.Change("Al");

        time.Advance(TimeSpan.FromSeconds(2));

        cut.WaitForAssertion(() => autosaves.Should().Be(1), TimeSpan.FromSeconds(5));
        // Advancing again without new changes must not re-fire the debounce.
        time.Advance(TimeSpan.FromSeconds(5));
        autosaves.Should().Be(1);
    }

    [Fact]
    public void Loading_DisablesNavigation()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.IsLoading, true));

        cut.Find(".tm-signing-form-runner__next").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Completing_DisablesForwardNavigation()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
            ])
            .Add(p => p.IsCompleting, true));

        cut.Find(".tm-signing-form-runner__next").HasAttribute("disabled").Should().BeTrue();
    }

    /// <summary>
    /// The mobile bottom sheet is a CSS presentation of the ONE steps rail: collapsed shows only
    /// the expand affordance, expanded shows the minimize affordance plus the panel — driven by
    /// the <c>--sheet-collapsed</c> / <c>--sheet-expanded</c> state classes.
    /// </summary>
    [Fact]
    public void MobileCollapsed_CanExpandAndMinimize()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.MobilePanelMode, TmSigningFormRunnerMobilePanelMode.Collapsed));

        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain(
            "tm-signing-form-runner__steps--sheet-collapsed",
            "a collapsed mobile panel is a state class on the single steps rail");
        cut.Find(".tm-signing-form-runner__mobile-expand").Click();
        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain("tm-signing-form-runner__steps--sheet-expanded");
        cut.Find(".tm-signing-form-runner__mobile-minimize").Should().NotBeNull();
        cut.FindAll(".tm-signing-form-runner__step-panel").Should().ContainSingle(
            "expanding must not render a second panel instance");

        cut.Find(".tm-signing-form-runner__mobile-minimize").Click();
        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain("tm-signing-form-runner__steps--sheet-collapsed");
    }

    /// <summary>The mobile sheet always renders BOTH affordances — CSS hides the inactive one per
    /// state class — and both describe the sheet state through <c>aria-expanded</c>.</summary>
    [Fact]
    public void MobileSheet_RendersBothAffordances_WithTheSheetState()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.MobilePanelMode, TmSigningFormRunnerMobilePanelMode.Collapsed));

        cut.FindAll(".tm-signing-form-runner__mobile-expand").Should().ContainSingle();
        cut.FindAll(".tm-signing-form-runner__mobile-minimize").Should().ContainSingle(
            "BOTH affordances always render; CSS hides the one that does not match the sheet state");
        cut.Find(".tm-signing-form-runner__mobile-expand").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find(".tm-signing-form-runner__mobile-minimize").GetAttribute("aria-expanded").Should().Be("false");

        cut.Find(".tm-signing-form-runner__mobile-expand").Click();

        cut.Find(".tm-signing-form-runner__mobile-expand").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find(".tm-signing-form-runner__mobile-minimize").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void MobileCompleteTarget_AddsAvoidanceClass()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.MobileCompleteTargetSelector, "#fixed-complete"));

        cut.Find(".tm-signing-form-runner__steps").ClassList.Should().Contain("tm-signing-form-runner__steps--has-complete-target");
        cut.Find(".tm-signing-form-runner__steps").GetAttribute("data-complete-target").Should().Be("#fixed-complete");
    }

    /// <summary>
    /// Focus management: moving to the next step moves focus to the new step's title heading
    /// (which carries <c>tabindex="-1"</c>), so keyboard and screen-reader users land at the top of
    /// the step instead of on the clicked button.
    /// </summary>
    [Fact]
    public async Task Runner_GoNext_RequestsFocusOnStepTitle()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
            ]));

        await cut.Find(".tm-signing-form-runner__next").ClickAsync(new MouseEventArgs());

        var invocation = JSInterop.VerifyFocusAsyncInvoke();
        invocation.Arguments[0].ShouldBeElementReferenceTo(cut.Find(".tm-signing-step-shell__title"));
        cut.Find(".tm-signing-step-shell__title").GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void Runner_ProgressLiveRegion_IsPolite()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)]));

        var progress = cut.Find(".tm-signing-form-runner__progress");
        progress.GetAttribute("role").Should().Be("status");
        progress.GetAttribute("aria-live").Should().Be("polite",
            "step progress is a polite live region, announced without interrupting");
        progress.GetAttribute("aria-atomic").Should().Be("true");
    }

    /// <summary>
    /// The step plan is a pure function of Fields / Pages / values / SubmitterUuid. The cache is
    /// invalidated on every parameter set (the host may mutate the SAME Fields list in place), so
    /// each render pass plans at most once and a field added to the same list instance appears.
    /// </summary>
    [Fact]
    public void Runner_PlanIsCached_UntilValuesChange()
    {
        var fields = new List<SigningField>
        {
            CreateField("name", "Name", SigningFieldType.Text),
            CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
        };
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, fields));

        var afterRender = cut.Instance.PlanComputationsForTests;
        afterRender.Should().BeGreaterThan(0, "the initial render plans the steps at least once");

        // A re-render without a new input invalidates the cache once and plans exactly one fresh
        // plan for that pass (OnParametersSet clears the cache; the same render's accesses reuse it).
        cut.Render();
        cut.Render();
        cut.Instance.PlanComputationsForTests.Should().Be(afterRender + 2,
            "each render pass re-plans at most once");

        // Adding a field to the SAME list instance is observed only because OnParametersSet drops
        // the cache: the new step appears.
        fields.Add(CreateField("added", "Added field", SigningFieldType.Text, y: 0.3));
        cut.Render();

        cut.Find(".tm-signing-form-runner__progress").TextContent.Should().Contain("of 3",
            "the in-place Fields mutation must produce a new plan");
        cut.Find(".tm-signing-form-runner__accessibility-entry").Click();
        cut.FindAll(".tm-signing-form-runner__accessibility-field").Should().HaveCount(3);
        cut.FindAll(".tm-signing-form-runner__accessibility-field").Last().TextContent.Should().Contain("Added field");

        var computations = cut.Instance.PlanComputationsForTests;
        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        cut.Instance.PlanComputationsForTests.Should().BeGreaterThan(computations,
            "a value change must re-plan (a conditional field may appear)");
    }

    /// <summary>
    /// Ids are per rendered component instance (B25 follow-up): two runners showing the SAME field
    /// uuid never share a title id, and each step input's <c>aria-labelledby</c> resolves INSIDE
    /// its own runner fragment.
    /// </summary>
    [Fact]
    public void Runner_SameFieldUuidTwice_TitleIdsAreDistinctAndResolveInsideTheirOwnShell()
    {
        var shared = CreateField("name", "Name", SigningFieldType.Text);
        var first = Render<TmSigningFormRunner>(parameters => parameters.Add(p => p.Fields, [shared]));
        var second = Render<TmSigningFormRunner>(parameters => parameters.Add(p => p.Fields, [shared]));

        var firstTitleId = first.Find(".tm-signing-step-shell__title").GetAttribute("id");
        var secondTitleId = second.Find(".tm-signing-step-shell__title").GetAttribute("id");

        firstTitleId.Should().StartWith("tm-signing-step-title-");
        firstTitleId.Should().NotBe(secondTitleId, "ids are per rendered instance, not derived from the field uuid");

        first.Find("input.tm-signing-text-step__input").GetAttribute("aria-labelledby").Should().Be(firstTitleId);
        second.Find("input.tm-signing-text-step__input").GetAttribute("aria-labelledby").Should().Be(secondTitleId);
        first.Find($"#{firstTitleId}").TextContent.Should().Be("Name", "the first runner's reference resolves inside its own fragment");
        second.Find($"#{secondTitleId}").TextContent.Should().Be("Name", "the second runner's reference resolves inside its own fragment");
    }

    /// <summary>
    /// A failed Next marks the current step's control invalid (<c>aria-invalid="true"</c>, on the
    /// input for single controls and on the fieldset for groups) and references the shell's single
    /// <c>role="alert"</c> through <c>aria-describedby</c> — including the forwarded runner
    /// validation.
    /// </summary>
    [Fact]
    public void Runner_FailedStep_MarksControlsInvalid_AndReferencesTheValidationAlert()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateChoiceField("delivery", "Delivery", SigningFieldType.Select, y: 0.2, required: true)
            ]));

        cut.Find(".tm-signing-form-runner__next").Click();

        var input = cut.Find("input.tm-signing-text-step__input");
        input.GetAttribute("aria-invalid").Should().Be("true");
        var validationId = cut.Find(".tm-signing-step-shell__validation[role='alert']").GetAttribute("id");
        input.GetAttribute("aria-describedby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Contain(validationId!, "the step alert is part of the input's description");

        // Complete the text step, land on the select step, and fail THERE: the validation travels
        // into the choice step through the runner cascade, so the select is marked invalid too.
        cut.Find("input.tm-signing-text-step__input").Change("Alice");
        cut.Find(".tm-signing-form-runner__next").Click();
        cut.Find(".tm-signing-form-runner__next").Click();

        var select = cut.Find("select.tm-signing-choice-step__select");
        select.GetAttribute("aria-invalid").Should().Be("true");
        var selectValidationId = cut.Find(".tm-signing-step-shell__validation[role='alert']").GetAttribute("id");
        select.GetAttribute("aria-describedby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Contain(selectValidationId!);
    }

    /// <summary>
    /// Blocked completion that lands on a DIFFERENT step is a step navigation: the focus signal
    /// fires and the signer's keyboard lands on the blocking step's title (like a failed Next).
    /// </summary>
    [Fact]
    public async Task Complete_BlockedOnEarlierStep_RequestsFocusOnThatStepTitle()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("first", "First", SigningFieldType.Text, y: 0.1, required: true),
                CreateField("second", "Second", SigningFieldType.Text, y: 0.2, required: true)
            ]));

        // Complete the LAST step so Complete is enabled while the first step stays empty.
        cut.Find(".tm-signing-form-runner__accessibility-entry").Click();
        cut.FindAll(".tm-signing-form-runner__accessibility-field")[1].Click();
        cut.Find("input.tm-signing-text-step__input").Change("Bob");

        await cut.Find(".tm-signing-form-runner__complete").ClickAsync(new MouseEventArgs());

        // TWO focus moves happened: the accessibility jump to the last step, then the blocked
        // Complete landing back on the FIRST (empty) step — a blocked Complete that changes the
        // step navigates (and moves keyboard focus) exactly like a failed Next. The PRECISE target
        // node is pinned by Runner_GoNext_RequestsFocusOnStepTitle (asserting the reference against
        // a twice-patched bUnit node is not reliable).
        var invocations = JSInterop.VerifyFocusAsyncInvoke(2);
        invocations.Should().HaveCount(2, "the accessibility jump and the blocked Complete each navigate");
        cut.Find(".tm-signing-step-shell__title").TextContent.Should().Be("First");
        cut.Find(".tm-signing-step-shell__validation").TextContent.Should().Contain("required");
    }

    [Fact]
    public void AccessibilityMode_ListsFieldsAndFocusesStep()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("first", "First", SigningFieldType.Text, y: 0.1),
                CreateField("second", "Second", SigningFieldType.Text, y: 0.2)
            ]));

        cut.Find(".tm-signing-form-runner__accessibility-entry").Click();
        cut.FindAll(".tm-signing-form-runner__accessibility-field")[1].Click();

        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Second");
        cut.Find(".tm-signing-form-runner__progress").GetAttribute("aria-label").Should().Contain("Step");
    }

    [Fact]
    public void LanguageSelector_ChangesCultureWithoutLosingValueOrStep()
    {
        string? culture = null;
        IReadOnlyDictionary<string, object?>? values = null;
        SigningSubmissionLocalizationSnapshot? snapshot = null;
        var first = CreateField("first", "First", SigningFieldType.Text, y: 0.1);
        first.Labels.Translations["en"] = "First name";
        first.Labels.Translations["cs"] = "Jméno";
        var second = CreateField("second", "Second", SigningFieldType.Text, y: 0.2);
        second.Labels.Translations["en"] = "Second name";
        second.Labels.Translations["cs"] = "Druhé jméno";

        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [first, second])
            .Add(p => p.Culture, "en")
            .Add(p => p.FallbackCulture, "en")
            .Add(p => p.SupportedCultures, ["en", "cs"])
            .Add(p => p.ShowLanguageSelector, true)
            .Add(p => p.CultureChanged, EventCallback.Factory.Create<string?>(this, changed => culture = changed))
            .Add(p => p.ValuesChanged, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, changed => values = changed))
            .Add(p => p.OnLocalizationSnapshotChanged, EventCallback.Factory.Create<SigningSubmissionLocalizationSnapshot>(this, changed => snapshot = changed)));

        cut.Find("input.tm-signing-text-step__input").Change("Alice");
        cut.Find(".tm-signing-form-runner__next").Click();
        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Second name");

        cut.Find(".tm-signing-form-runner__language-select").Change("cs");

        culture.Should().Be("cs");
        values!["first"].Should().Be("Alice");
        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Druhé jméno");
        snapshot.Should().NotBeNull();
        snapshot!.Culture.Should().Be("cs");
        snapshot.Fields.Single(field => field.FieldUuid == "first").Label.Should().Be("Jméno");
    }

    [Fact]
    public void LanguageSelector_IsHiddenWhenDisabledOrSingleCulture()
    {
        var disabled = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.SupportedCultures, ["en", "cs"])
            .Add(p => p.ShowLanguageSelector, false));

        disabled.FindAll(".tm-signing-form-runner__language-select").Should().BeEmpty();

        var singleCulture = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.SupportedCultures, ["en"])
            .Add(p => p.ShowLanguageSelector, true));

        singleCulture.FindAll(".tm-signing-form-runner__language-select").Should().BeEmpty();
    }

    private static SigningField CreateChoiceField(
        string uuid,
        string name,
        SigningFieldType type,
        double y = 0.1,
        bool required = false)
    {
        var field = CreateField(uuid, name, type, y, required);
        field.Options =
        [
            new SigningFieldOption { Uuid = "paper", Value = "Paper" },
            new SigningFieldOption { Uuid = "email", Value = "Email" }
        ];

        return field;
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
