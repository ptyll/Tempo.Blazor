using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

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
                // Required: the primary action is contextual (phase 3) and an all-optional plan
                // renders Complete in the primary slot instead of Next.
                CreateField("signature", "Signature", SigningFieldType.Signature, required: true)
            ]));

        cut.FindAll(".tm-signing-form-runner__step-panel").Should().ContainSingle(
            "the mobile presentation is CSS-only; the step panel must exist once in the DOM");
        cut.FindAll(".tm-signing-step-shell").Should().ContainSingle();
        cut.FindAll(".tm-signature-capture").Should().ContainSingle();
        cut.FindAll(".tm-signing-form-runner__progress").Should().ContainSingle();
        // The single-step plan's primary slot is Complete (phase 3: on the last step the primary
        // reads Complete); the invariant is ONE primary action, never a duplicated row.
        cut.FindAll(".tm-signing-form-runner__complete").Should().ContainSingle(
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
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2, required: true)
            ])
            .Add(p => p.ValuesChanged, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, v => values = v))
            .Add(p => p.OnStepSubmit, EventCallback.Factory.Create<SigningStepItem>(this, step => submitted = step)));

        // Phase 3: the primary action switches to Complete once everything required is done, so the
        // Next click must happen while the SECOND required step is still empty.
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

    /// <summary>
    /// Signing redesign phase 3: the primary action is CONTEXTUAL. While later steps remain the
    /// footer renders "Next"; on the LAST step — and once every required field is complete — the
    /// same slot switches to "Complete" (enabled only when the required work is done, so a click
    /// can no longer land the signer on an empty step).
    /// </summary>
    [Fact]
    public void Runner_PrimaryButton_SwitchesToComplete_WhenAllDone()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2, required: true)
            ]));

        cut.FindAll(".tm-signing-form-runner__next").Should().ContainSingle(
            "while later steps remain the primary action is Next");
        cut.FindAll(".tm-signing-form-runner__complete").Should().BeEmpty(
            "Complete is not offered before the last step");

        cut.Find("input.tm-signing-text-step__input").Change("Alice");
        cut.Find(".tm-signing-form-runner__next").Click();

        // Last step, required work missing: the primary already reads Complete, but stays disabled.
        cut.FindAll(".tm-signing-form-runner__next").Should().BeEmpty();
        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeTrue(
            "on the last step the primary shows the finishing action, disabled while work is missing");

        cut.Find("input.tm-signing-date-step__input").Change("2026-05-08");

        cut.Find(".tm-signing-form-runner__complete").HasAttribute("disabled").Should().BeFalse(
            "with every required field complete the Complete action is enabled");
    }

    /// <summary>The OnComplete contract is unchanged: one snapshot dictionary of the entered values.</summary>
    [Fact]
    public async Task Runner_OnComplete_ContractUnchanged()
    {
        IReadOnlyDictionary<string, object?>? completed = null;
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("note", "Note", SigningFieldType.Text, y: 0.2)
            ])
            .Add(p => p.OnComplete, EventCallback.Factory.Create<IReadOnlyDictionary<string, object?>>(this, v => completed = v)));

        cut.Find("input.tm-signing-text-step__input").Change("Alice");
        await cut.Find(".tm-signing-form-runner__complete").ClickAsync(new MouseEventArgs());

        completed.Should().NotBeNull();
        completed!["name"].Should().Be("Alice");
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
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2, required: true)
            ])
            .Add(p => p.IsLoading, true));

        cut.Find(".tm-signing-form-runner__next").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Completing_DisablesForwardNavigation()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2, required: true)
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
    /// the step instead of on the clicked button. The step must be VALID to advance (phase 3: the
    /// primary action refuses an invalid step), so the test completes the first field first.
    /// </summary>
    [Fact]
    public async Task Runner_GoNext_RequestsFocusOnStepTitle()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2, required: true)
            ]));

        await cut.Find("input.tm-signing-text-step__input").ChangeAsync(new ChangeEventArgs { Value = "Alice" });
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
        cut.FindAll(".tm-signing-form-runner__field-button").Should().HaveCount(3);
        cut.FindAll(".tm-signing-form-runner__field-button").Last().TextContent.Should().Contain("Added field");

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
                CreateChoiceField("delivery", "Delivery", SigningFieldType.Select, y: 0.2, required: true),
                CreateField("note", "Note", SigningFieldType.Text, y: 0.3)
            ]));

        cut.Find(".tm-signing-form-runner__next").Click();

        var input = cut.Find("input.tm-signing-text-step__input");
        input.GetAttribute("aria-invalid").Should().Be("true");
        var validationId = cut.Find(".tm-signing-step-shell__validation[role='alert']").GetAttribute("id");
        input.GetAttribute("aria-describedby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Contain(validationId!, "the step alert is part of the input's description");

        // Complete the text step, land on the SELECT step, and fail THERE: the validation travels
        // into the choice step through the runner cascade, so the select is marked invalid too.
        // (A trailing optional step keeps the primary a Next on the select step — on a LAST step
        // the primary is Complete and a disabled TmButton swallows the click like a browser does.)
        cut.Find("input.tm-signing-text-step__input").Change("Alice");
        cut.Find(".tm-signing-form-runner__next").Click();
        cut.Find(".tm-signing-form-runner__next").Click();

        // The choice select is a core TmSelect: the core SelectAttributes splat (phase 4 review)
        // carries aria-invalid + the validation id onto the NATIVE <select>, so the runner's
        // forwarded validation marks the actual control, not the wrapper.
        var select = cut.Find(".tm-signing-choice-step__select select");
        select.GetAttribute("aria-invalid").Should().Be("true");
        var selectValidationId = cut.Find(".tm-signing-step-shell__validation[role='alert']").GetAttribute("id");
        select.GetAttribute("aria-describedby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Contain(selectValidationId!);
    }

    /// <summary>
    /// Signing redesign phase 3 (SI4): the field list is a PERMANENT panel section — the old
    /// "Accessibility mode" toggle (U2) is gone. The progress count reflects COMPLETED FIELDS, not
    /// the step index: filling the second of three required fields reads "1 of 3 done", no matter
    /// which step the signer is on.
    /// </summary>
    [Fact]
    public void Runner_Progress_CountsCompletedFields_NotStepIndex()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("first", "First", SigningFieldType.Text, y: 0.1, required: true),
                CreateField("second", "Second", SigningFieldType.Text, y: 0.2, required: true),
                CreateField("third", "Third", SigningFieldType.Text, y: 0.3, required: true)
            ]));

        cut.Find(".tm-signing-form-runner__progress-text").TextContent.Should().Contain("0 of 3");

        // Jump to the SECOND field through the list and complete it: the count follows the FIELDS
        // (1 of 3), not the step position (which would announce "2 of 3").
        cut.FindAll(".tm-signing-form-runner__field-button")[1].Click();
        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        cut.Find(".tm-signing-form-runner__progress-text").TextContent.Should().Contain("1 of 3",
            "the progress counts completed fields, never the step index");
        cut.Find(".tm-signing-form-runner__progress").GetAttribute("aria-label").Should().Contain("1 of 3");
    }

    /// <summary>
    /// Every list row renders the type icon, the label and the META line (page + status chip);
    /// the done status carries the check icon, the required status is a danger-subtle chip.
    /// </summary>
    [Fact]
    public void Runner_FieldList_RendersIconLabelPageAndStatus()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Full name", SigningFieldType.Text, y: 0.1, required: true),
                CreateField("note", "Internal note", SigningFieldType.Text, y: 0.2)
            ]));

        var rows = cut.FindAll(".tm-signing-form-runner__field-button");
        rows.Should().HaveCount(2);

        var requiredRow = rows[0];
        requiredRow.QuerySelector(".tm-signing-form-runner__field-icon")!.GetAttribute("data-icon").Should().Be("file-text");
        requiredRow.QuerySelector(".tm-signing-form-runner__field-label")!.TextContent.Should().Be("Full name");
        var requiredMeta = requiredRow.QuerySelector(".tm-signing-form-runner__field-meta")!;
        requiredMeta.Should().NotBeNull("page + status share the meta line under the label");
        requiredMeta.QuerySelector(".tm-signing-form-runner__field-page")!.TextContent.Should().Contain("1");
        requiredMeta.QuerySelector(".tm-signing-form-runner__field-status")!.TextContent.Should().Contain("Required");
        requiredMeta.QuerySelector(".tm-signing-form-runner__field-status")!.ClassList
            .Should().Contain("tm-signing-form-runner__field-status--required");

        var optionalRow = rows[1];
        var optionalMeta = optionalRow.QuerySelector(".tm-signing-form-runner__field-meta")!;
        optionalMeta.QuerySelector(".tm-signing-form-runner__field-status")!.TextContent.Should().Contain("Optional");
    }

    /// <summary>
    /// The done row reads success + a check icon; after the value lands the status flips and the
    /// check appears (never colour-only, WCAG 1.4.1).
    /// </summary>
    [Fact]
    public void Runner_FieldList_DoneRow_ShowsSuccessStatusWithCheckIcon()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Full name", SigningFieldType.Text, y: 0.1, required: true),
                CreateField("note", "Internal note", SigningFieldType.Text, y: 0.2, required: true)
            ]));

        cut.FindAll(".tm-signing-form-runner__field-status--done").Should().BeEmpty();
        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        var done = cut.Find(".tm-signing-form-runner__field-status--done");
        done.TextContent.Should().Contain("Done");
        done.QuerySelector("svg.tm-icon, .tm-icon").Should().NotBeNull("the done chip carries the check icon");
    }

    /// <summary>
    /// CSS contract of the field-list rows (phase 3 review): the button grid is the two-line
    /// AREA layout (icon spans both rows, label above the meta line), and the status chips carry
    /// the token-driven states.
    /// </summary>
    [Fact]
    public void Runner_FieldList_CssContract_GridAreasAndStatusChips()
    {
        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components", "_signing-form-runner.css"));

        css.Should().Contain(".tm-signing-form-runner__field-meta", "page + status are wrapped in the meta line");
        css.Should().Contain("grid-template-areas:")
            .And.Contain("\"icon label\"")
            .And.Contain("\"icon meta\"", "the button grid is the icon / label+meta area layout");
        css.Should().Contain(".tm-signing-form-runner__field-status--done", "done has its own status rule");
        css.Should().Contain(".tm-signing-form-runner__field-status--required");
        css.Should().Contain(".tm-signing-form-runner__field-status--optional");
    }

    /// <summary>
    /// The active row's accent bar derives from the border-width token (no 3px length literal).
    /// </summary>
    [Fact]
    public void Runner_FieldList_CssContract_ActiveBarUsesBorderWidthToken()
    {
        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components", "_signing-form-runner.css"));

        css.Should().Contain("inset calc(var(--tm-border-width) * 3)",
            "the active-row bar is border-width-token math, not a bare 3px");
        css.Should().NotContain("inset 3px");
    }

    /// <summary>The active list row carries <c>aria-current="step"</c>; the others carry nothing.</summary>
    [Fact]
    public void Runner_FieldList_ActiveItemHasAriaCurrentStep()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("first", "First", SigningFieldType.Text, y: 0.1),
                CreateField("second", "Second", SigningFieldType.Text, y: 0.2)
            ]));

        cut.FindAll(".tm-signing-form-runner__field-button")[0].GetAttribute("aria-current").Should().Be("step");
        cut.FindAll(".tm-signing-form-runner__field-button")[1].GetAttribute("aria-current").Should().BeNull();

        cut.FindAll(".tm-signing-form-runner__field-button")[1].Click();

        cut.FindAll(".tm-signing-form-runner__field-button")[0].GetAttribute("aria-current").Should().BeNull();
        cut.FindAll(".tm-signing-form-runner__field-button")[1].GetAttribute("aria-current").Should().Be("step");
    }

    /// <summary>Clicking a list row selects that step (the old accessibility list's jump, now permanent).</summary>
    [Fact]
    public void Runner_FieldListClick_SelectsStep()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields,
            [
                CreateField("first", "First", SigningFieldType.Text, y: 0.1),
                CreateField("second", "Second", SigningFieldType.Text, y: 0.2)
            ]));

        cut.FindAll(".tm-signing-form-runner__field-button")[1].Click();

        cut.Find(".tm-signing-form-runner__step-panel").TextContent.Should().Contain("Second");
        cut.FindAll(".tm-signing-field-overlay")[1].ClassList.Should().Contain("tm-signing-field--selected");
    }

    /// <summary>Fields of OTHER submitters never appear in the list (the plan is submitter-filtered).</summary>
    [Fact]
    public void Runner_FieldList_ExcludesOtherSubmittersFields()
    {
        var mine = CreateField("mine", "My field", SigningFieldType.Text, y: 0.1);
        mine.SubmitterUuid = "signer-1";
        var theirs = CreateField("theirs", "Other field", SigningFieldType.Text, y: 0.2);
        theirs.SubmitterUuid = "signer-2";

        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [mine, theirs])
            .Add(p => p.SubmitterUuid, "signer-1"));

        cut.FindAll(".tm-signing-form-runner__field-button").Should().ContainSingle(
            "a signer's list never shows another submitter's fields");
        cut.Find(".tm-signing-form-runner__field-list").TextContent.Should().NotContain("Other field");
    }

    /// <summary>Once every required field is complete the success banner appears.</summary>
    [Fact]
    public void Runner_AllRequiredDone_ShowsSuccessBanner()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("note", "Note", SigningFieldType.Text, y: 0.2)
            ]));

        cut.FindAll(".tm-signing-form-runner__all-done").Should().BeEmpty(
            "the banner waits until the required work is done");

        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        var banner = cut.Find(".tm-signing-form-runner__all-done");
        banner.ClassList.Should().Contain("tm-alert--success");
        banner.TextContent.Should().Contain("All required fields are complete");
    }

    /// <summary>
    /// Skip renders ONLY for an optional current field that is NOT the last step: a required step
    /// has no skip at all, and a final optional step has nowhere to skip to.
    /// </summary>
    [Fact]
    public void Runner_SkipButton_NotRenderedForRequiredField()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, y: 0.1, required: true),
                CreateField("note", "Note", SigningFieldType.Text, y: 0.2),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.3)
            ]));

        cut.FindAll(".tm-signing-form-runner__skip").Should().BeEmpty(
            "a required current step offers no skip");

        cut.Find("input.tm-signing-text-step__input").Change("Alice");
        cut.FindAll(".tm-signing-form-runner__skip").Should().BeEmpty(
            "filling the field does not add a skip while the required step is current");

        // Jump to the optional MIDDLE field through the permanent list: only THERE the skip exists.
        cut.FindAll(".tm-signing-form-runner__field-button")[1].Click();

        cut.FindAll(".tm-signing-form-runner__skip").Should().ContainSingle(
            "an optional non-final current step offers the skip");

        // The LAST step (optional here) has nowhere to skip to: no skip on it either.
        cut.FindAll(".tm-signing-form-runner__field-button")[2].Click();

        cut.FindAll(".tm-signing-form-runner__skip").Should().BeEmpty(
            "the last step offers no skip even when it is optional");
    }

    /// <summary>Skip lives INSIDE the sticky action footer as a full-width row (phase 3 review),
    /// keeping the keyboard order Previous → Next/Complete → Skip.</summary>
    [Fact]
    public void Runner_SkipButton_RendersInsideTheActionsFooter()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("note", "Note", SigningFieldType.Text, y: 0.1),
                CreateField("date", "Date", SigningFieldType.Date, y: 0.2)
            ]));

        var actions = cut.Find(".tm-signing-form-runner__actions");
        actions.QuerySelector(".tm-signing-form-runner__skip").Should().NotBeNull(
            "the skip row renders inside the sticky action footer");
    }

    /// <summary>U4 (G9): the optional document header renders the title and the host actions slot.</summary>
    [Fact]
    public void Runner_DocumentTitle_AndHeaderActionsSlot_Render()
    {
        var withHeader = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.DocumentTitle, "Service order approval")
            .Add(p => p.DocumentHeaderActions, builder => builder.AddContent(0, "host-action-marker")));

        var header = withHeader.Find(".tm-signing-form-runner__document-header");
        header.TextContent.Should().Contain("Service order approval");
        header.TextContent.Should().Contain("host-action-marker");

        // The default stays today's chrome-free look: no title, no slot, no header element.
        var withoutHeader = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)]));
        withoutHeader.FindAll(".tm-signing-form-runner__document-header").Should().BeEmpty();
    }

    /// <summary>
    /// ShowDocumentToolbar renders the page viewers' ZOOM controls only (phase 3 review): the
    /// runner presents one page at a time, so the per-page pagination group would be dead chrome.
    /// </summary>
    [Fact]
    public void Runner_ShowDocumentToolbar_RendersZoomOnly_NoDeadPagination()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.ShowDocumentToolbar, true));

        cut.FindAll(".tm-document-page-viewer__toolbar").Should().NotBeEmpty("the toolbar renders");
        cut.FindAll(".tm-document-page-viewer__zoom-controls").Should().NotBeEmpty("the zoom controls render");
        cut.FindAll(".tm-document-page-viewer__pagination").Should().BeEmpty(
            "per-page pagination controls are dead chrome on a one-page-at-a-time runner");

        // The default stays chrome-free.
        var withoutToolbar = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)]));
        withoutToolbar.FindAll(".tm-document-page-viewer__toolbar").Should().BeEmpty();
    }

    /// <summary>The "all done" banner is a POLITE status region, never a second alert (phase 3
    /// review): the TmAlert root itself carries role=status via the Role parameter — no wrapper,
    /// and no alert nested inside the status.</summary>
    [Fact]
    public void Runner_AllDone_IsPoliteStatusRegion()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, required: true),
                CreateField("note", "Note", SigningFieldType.Text, y: 0.2)
            ]));

        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        var banner = cut.Find(".tm-signing-form-runner__all-done");
        banner.GetAttribute("role").Should().Be("status",
            "the banner announces politely, not as a role=alert interruption");
        banner.QuerySelectorAll("[role='alert']").Should().BeEmpty(
            "no alert role may live inside the polite status region");
    }

    /// <summary>Phase 3 review (dark-mode contrast): every document wrapper is a LIGHT island — the
    /// paper sheet keeps the light tokens under a dark host, so overlay ink stays readable.</summary>
    [Fact]
    public void Runner_DocumentWrapper_IsALightIsland()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)]));

        cut.FindAll(".tm-signing-form-runner__document")
            .Should().OnlyContain(document => document.GetAttribute("data-theme") == "light",
                "the white paper sheet must keep the light tokens inside a dark host");
    }

    /// <summary>The compact language selector shares the quiet footer row with the autosave text
    /// (phase 3 review — the header keeps title / subtitle / progress only).</summary>
    [Fact]
    public void Runner_LanguageSelect_RendersInTheFooterRow_NextToAutosave()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.SupportedCultures, ["en", "cs"])
            .Add(p => p.ShowLanguageSelector, true));

        var footer = cut.Find(".tm-signing-form-runner__footer");
        footer.QuerySelector(".tm-signing-form-runner__autosave").Should().NotBeNull();
        footer.QuerySelector(".tm-signing-form-runner__language-select").Should().NotBeNull();

        var header = cut.Find(".tm-signing-form-runner__panel-header");
        header.QuerySelector(".tm-signing-form-runner__language-select").Should().BeNull(
            "the language selector left the panel header");
    }

    /// <summary>CSS contract of the sticky action footer (phase 3 review): desktop/tablet panels
    /// cap at the viewport and keep the actions reachable; the skip row spans the grid.</summary>
    [Fact]
    public void Runner_CssContract_StickyFooter_AndSkipRow()
    {
        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Signing", "wwwroot", "css", "components", "_signing-form-runner.css"));

        css.Should().Contain("@container tm-signing-runner (width >= 768px)",
            "the sticky footer is scoped to desktop/tablet containers");
        css.Should().Contain("max-height: calc(100dvh - (2 * var(--tm-space-4)))");
        css.Should().Contain("inset-block-end: 0");
        css.Should().Contain("z-index: var(--tm-z-sticky)", "a sticky surface never rides the modal band");
        css.Should().Contain("grid-column: 1 / -1", "the skip renders as a full-width row inside the actions");
        // Phase 3 review: the footer language select flexes and never overflows the footer row.
        css.Should().Contain("flex: 1 1 auto", "the language select grows with the footer row");
        css.Should().Contain("min-width: 0", "the language select may shrink instead of overflowing");
    }

    /// <summary>
    /// The document overlay prompts ONLY the current field while it is an empty REQUIRED field
    /// (review round 2 fix): other required fields stay quiet so the document never fills with
    /// prompts, optional fields never prompt, and the current field drops the prompt once it is
    /// completed.
    /// </summary>
    [Fact]
    public void Overlay_ShowsPrompt_OnlyForTheCurrentEmptyRequiredField()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Pages, [CreatePage()])
            .Add(p => p.Fields,
            [
                CreateField("name", "Name", SigningFieldType.Text, y: 0.1, required: true),
                CreateField("email", "Email", SigningFieldType.Text, y: 0.2, required: true),
                CreateField("note", "Note", SigningFieldType.Text, y: 0.3)
            ]));

        var overlays = cut.FindAll(".tm-signing-field-overlay");
        overlays[0].QuerySelector(".tm-signing-field__prompt")!.TextContent.Should().Contain("Click to sign",
            "the CURRENT empty required field carries the call to action");
        overlays[1].QuerySelector(".tm-signing-field__prompt").Should().BeNull(
            "a required field that is NOT current stays quiet");
        overlays[2].QuerySelector(".tm-signing-field__prompt").Should().BeNull(
            "an optional field never carries the required-field prompt");

        cut.Find("input.tm-signing-text-step__input").Change("Alice");

        cut.FindAll(".tm-signing-field-overlay")[0].QuerySelector(".tm-signing-field__prompt").Should().BeNull(
            "the prompt disappears once the current field is completed");
    }

    [Fact]
    public void LanguageSelector_ChangesCultureWithoutLosingValueOrStep()
    {
        string? culture = null;
        IReadOnlyDictionary<string, object?>? values = null;
        SigningSubmissionLocalizationSnapshot? snapshot = null;
        var first = CreateField("first", "First", SigningFieldType.Text, y: 0.1, required: true);
        first.Labels.Translations["en"] = "First name";
        first.Labels.Translations["cs"] = "Jméno";
        var second = CreateField("second", "Second", SigningFieldType.Text, y: 0.2, required: true);
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
