using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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

        cut.Find(".tm-signing-form-runner__validation").TextContent.Should().Contain("required");
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
        }, TimeSpan.FromSeconds(5));
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

    [Fact]
    public void MobileCollapsed_CanExpandAndMinimize()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.MobilePanelMode, TmSigningFormRunnerMobilePanelMode.Collapsed));

        cut.Find(".tm-signing-form-runner__mobile-expand").Click();
        cut.Find(".tm-signing-form-runner__mobile-panel--expanded").Should().NotBeNull();

        cut.Find(".tm-signing-form-runner__mobile-minimize").Click();
        cut.Find(".tm-signing-form-runner__mobile-panel--collapsed").Should().NotBeNull();
    }

    [Fact]
    public void MobileCompleteTarget_AddsAvoidanceClass()
    {
        var cut = Render<TmSigningFormRunner>(parameters => parameters
            .Add(p => p.Fields, [CreateField("name", "Name", SigningFieldType.Text)])
            .Add(p => p.MobileCompleteTargetSelector, "#fixed-complete"));

        cut.Find(".tm-signing-form-runner__mobile-panel").ClassList.Should().Contain("tm-signing-form-runner__mobile-panel--has-complete-target");
        cut.Find(".tm-signing-form-runner__mobile-panel").GetAttribute("data-complete-target").Should().Be("#fixed-complete");
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
