using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 4 — <c>TmModelPicker</c> (Tempo.Blazor.AI): the trigger shows the chosen model plus
/// its state tags, the popup is a listbox of the catalogue (grouped by provider / family / nothing), every row
/// carries its speed, quality and relative price bars with the values in WORDS, its context window, its
/// capability chips and the deprecated marker, and the reader compares models through a search field whose
/// hits are highlighted. The selected model's reasoning options (extended thinking, effort levels) are offered
/// next to the list, and <c>@bind-Selection</c> carries the whole answer.
/// </summary>
public class TmModelPickerTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private AiModelSelection? _selection;

    private static AiModelEffortLevel[] Effort(params string[] ids)
        => ids.Select(id => new AiModelEffortLevel(id, id.ToUpperInvariant())).ToArray();

    /// <summary>
    /// The catalogue the tests compare: a premium GPT-4o ($3 per million input), a cheap sibling ($0.15), a
    /// Claude model at the same premium price with a 200k window and effort levels, and a deprecated legacy
    /// model that reports almost nothing (so every "not reported" branch is exercised).
    /// </summary>
    private static AiModelInfo[] Catalogue() =>
    [
        new("gpt-4o", "GPT-4o")
        {
            Provider = "OpenAI",
            Family = "GPT-4",
            Description = "The flagship.",
            ContextWindow = 128_000,
            SpeedScore = 4,
            QualityScore = 5,
            InputPricePerMillion = 3m,
            OutputPricePerMillion = 15m,
            Capabilities = ["vision", "tools"],
            EffortLevels = Effort("low", "medium", "high"),
            SupportsExtendedThinking = true,
        },
        new("gpt-4o-mini", "GPT-4o mini")
        {
            Provider = "OpenAI",
            Family = "GPT-4",
            ContextWindow = 128_000,
            SpeedScore = 5,
            QualityScore = 3,
            InputPricePerMillion = 0.15m,
            Capabilities = ["tools"],
        },
        new("claude-3-5-sonnet", "Claude 3.5 Sonnet")
        {
            Provider = "Anthropic",
            Family = "Claude 3.5",
            ContextWindow = 200_000,
            SpeedScore = 3,
            QualityScore = 5,
            InputPricePerMillion = 3m,
            Capabilities = ["vision"],
            EffortLevels = Effort("low", "high"),
            SupportsExtendedThinking = true,
        },
        new("legacy-text", "Legacy Text")
        {
            Family = "Legacy",
            IsDeprecated = true,
        },
    ];

    private IRenderedComponent<TmModelPicker> RenderPicker(
        IReadOnlyList<AiModelInfo>? models = null,
        AiModelSelection? selection = null,
        Action<ComponentParameterCollectionBuilder<TmModelPicker>>? configure = null,
        CultureInfo? culture = null)
    {
        _selection = selection;

        return Render<TmModelPicker>(parameters =>
        {
            parameters
                .Add(picker => picker.Models, models ?? Catalogue())
                .Add(picker => picker.Selection, selection)
                .Add(picker => picker.Culture, culture ?? CultureInfo.GetCultureInfo("en-US"))
                .Add(picker => picker.SelectionChanged,
                    EventCallback.Factory.Create<AiModelSelection?>(this, value => _selection = value));

            configure?.Invoke(parameters);
        });
    }

    private IRenderedComponent<TmModelPicker> Open(
        Action<ComponentParameterCollectionBuilder<TmModelPicker>>? configure = null,
        AiModelSelection? selection = null,
        IReadOnlyList<AiModelInfo>? models = null,
        CultureInfo? culture = null)
    {
        var cut = RenderPicker(models, selection, configure, culture);
        cut.Find("[data-testid='model-picker-trigger']").Click();
        return cut;
    }

    private static IReadOnlyList<IElement> Options(IRenderedComponent<TmModelPicker> cut)
        => cut.FindAll("[data-testid='model-picker-option']").ToList();

    private static IElement Option(IRenderedComponent<TmModelPicker> cut, string modelId)
        => Options(cut).Single(option => option.GetAttribute("data-model-id") == modelId);

    private static string Text(IRenderedComponent<TmModelPicker> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    private static string? TextOrNull(IRenderedComponent<TmModelPicker> cut, string testId)
    {
        var element = cut.FindAll($"[data-testid='{testId}']");
        return element.Count == 0 ? null : element[0].TextContent.Trim();
    }

    private static Task SetAsync(IRenderedComponent<TmModelPicker> cut, string parameter, object? value)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [parameter] = value })));

    private static async Task SearchAsync(IRenderedComponent<TmModelPicker> cut, string text)
    {
        cut.Find("[data-testid='model-picker-search']").Input(text);
        await cut.InvokeAsync(() => { });
    }

    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (string?)((ElementReference)invocation.Arguments[0]!).Id)
            .ToList();

    // ── Trigger ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheTriggerShowsTheSelectedModel_AndItsStateTags()
    {
        var cut = RenderPicker(selection: new AiModelSelection("gpt-4o")
        {
            ExtendedThinking = true,
            EffortLevel = "high",
        });

        var trigger = cut.Find("[data-testid='model-picker-trigger']");
        Text(cut, "model-picker-trigger-name").Should().Be("GPT-4o");
        Text(cut, "model-picker-trigger-provider").Should().Be("OpenAI");

        var tags = cut.Find("[data-testid='model-picker-trigger-tags']").TextContent;
        tags.Should().Contain("Extended thinking");
        tags.Should().Contain("HIGH", "the effort level names itself in the trigger");
        cut.FindAll("[data-testid='model-picker-trigger-deprecated']").Should().BeEmpty();

        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.GetAttribute("aria-haspopup").Should().Be("listbox");
        trigger.GetAttribute("aria-label").Should().BeNull(
            "an aria-label on the trigger would replace its visible text and hide the state tags " +
            "from the accessible name (WCAG 1.3.1 / 4.1.2)");
        trigger.TextContent.Should().Contain("Model: ",
            "a visually hidden prefix keeps the field's context in the name built from the visible text");
        trigger.TextContent.Should().Contain("Extended thinking",
            "the state tags are visible, so they must be part of the name a screen reader hears");
    }

    [Fact]
    public void AnEmptySelection_ShowsTheLocalizedPlaceholder()
    {
        var cut = RenderPicker();

        Text(cut, "model-picker-trigger-name").Should().Be("Select a model");
        var trigger = cut.Find("[data-testid='model-picker-trigger']");
        trigger.GetAttribute("aria-label").Should().BeNull(
            "the placeholder itself names the trigger; a second name would be a duplicate");
        trigger.TextContent.Should().Contain("Select a model");
        trigger.QuerySelectorAll(".tm-sr-only").Should().BeEmpty(
            "nothing is selected, so there is no 'Model:' prefix to add");
        cut.FindAll("[data-testid='model-picker-trigger-provider']").Should().BeEmpty();
    }

    [Fact]
    public void ADeprecatedSelection_IsMarkedInTheTrigger()
    {
        var cut = RenderPicker(selection: new AiModelSelection("legacy-text"));

        cut.Find("[data-testid='model-picker-trigger-deprecated']").TextContent.Trim()
            .Should().Be("Deprecated");
        cut.Find("[data-testid='model-picker-trigger']").TextContent.Should().Contain("Deprecated",
            "the chip is inside the button, which carries no aria-label, so the word reaches the accessible name");
    }

    [Fact]
    public void AHostAriaLabel_KeepsTheStateTagsInTheDescription()
    {
        var cut = RenderPicker(
            selection: new AiModelSelection("legacy-text"),
            configure: parameters => parameters.Add(picker => picker.AriaLabel, "Conversation model"));

        var trigger = cut.Find("[data-testid='model-picker-trigger']");
        trigger.GetAttribute("aria-label").Should().Be("Conversation model");

        var describedBy = trigger.GetAttribute("aria-describedby")!
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var instructions = cut.Find("[data-testid='model-picker-instructions']");
        var tags = cut.Find("[data-testid='model-picker-trigger-tags']");
        describedBy.Should().Contain(instructions.Id);
        describedBy.Should().Contain(tags.Id,
            "a host-supplied name replaces the visible text, so the state tags must be described instead");
        tags.TextContent.Should().Contain("Deprecated",
            "a blind reader still has to learn that the selected model is deprecated");
    }

    [Fact]
    public void AHostAriaLabel_WithoutStateTags_DescribesOnlyTheInstructions()
    {
        var cut = RenderPicker(
            selection: new AiModelSelection("gpt-4o"),
            configure: parameters => parameters.Add(picker => picker.AriaLabel, "Conversation model"));

        var trigger = cut.Find("[data-testid='model-picker-trigger']");
        trigger.GetAttribute("aria-label").Should().Be("Conversation model");
        trigger.GetAttribute("aria-describedby").Should()
            .Be(cut.Find("[data-testid='model-picker-instructions']").Id,
                "a dangling id reference would describe nothing");
    }

    [Fact]
    public void ASelectionOutsideTheCatalogue_StillNamesTheTrigger()
    {
        var cut = RenderPicker(selection: new AiModelSelection("gone-model"));

        Text(cut, "model-picker-trigger-name").Should().Be("gone-model");
    }

    [Fact]
    public void ThePickerIsClosedUntilTheTriggerIsActivated()
    {
        var cut = RenderPicker();

        cut.FindAll("[data-testid='model-picker-list']").Should().BeEmpty();

        cut.Find("[data-testid='model-picker-trigger']").Click();

        cut.Find("[data-testid='model-picker-list']").GetAttribute("role").Should().Be("listbox");
        cut.Find("[data-testid='model-picker-trigger']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='model-picker-trigger']").GetAttribute("aria-controls")
            .Should().Be(cut.Find("[data-testid='model-picker-list']").Id);
    }

    [Fact]
    public void TheListboxIsNamedByItsOwnListLabel_NotByThePopupTitle()
    {
        var cut = Open();

        var list = cut.Find("[data-testid='model-picker-list']");
        list.GetAttribute("aria-label").Should().Be("Models",
            "the popup title already names the card / bottom sheet; reusing it names the list a second time");
        Text(cut, "model-picker-title").Should().Be("Choose a model",
            "the visible heading keeps naming the surface");
    }

    [Fact]
    public void ADisabledPicker_DoesNotOpen()
    {
        var cut = RenderPicker();

        cut.Find("[data-testid='model-picker-trigger']").HasAttribute("disabled").Should().BeFalse();

        var disabled = RenderPicker(configure: parameters => parameters.Add(picker => picker.Disabled, true));
        disabled.Find("[data-testid='model-picker-trigger']").HasAttribute("disabled").Should().BeTrue();

        disabled.Find("[data-testid='model-picker-trigger']").Click();
        disabled.FindAll("[data-testid='model-picker-list']").Should().BeEmpty();
    }

    // ── Grouping ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultGroupingIsByProvider_AndTheOtherGroupCollectsTheUnreported()
    {
        var cut = Open();

        var headers = cut.FindAll("[data-testid='model-picker-group-header']")
            .Select(header => header.TextContent.Trim())
            .ToList();
        headers.Should().Equal("OpenAI", "Anthropic", "Other");

        Options(cut).Select(option => option.GetAttribute("data-model-id")).Should().Equal(
            "gpt-4o", "gpt-4o-mini", "claude-3-5-sonnet", "legacy-text");

        var groups = cut.FindAll("[data-testid='model-picker-group']");
        groups.Should().HaveCount(3);
        groups[0].GetAttribute("role").Should().Be("group");
        groups[0].GetAttribute("aria-labelledby").Should().Be(
            groups[0].QuerySelector("[data-testid='model-picker-group-header']")!.Id);
    }

    [Fact]
    public void GroupingByFamily_UsesTheFamilyAndFallsBackToTheOtherGroup()
    {
        var cut = Open(parameters => parameters.Add(picker => picker.GroupBy, AiModelGroupBy.Family));

        cut.FindAll("[data-testid='model-picker-group-header']")
            .Select(header => header.TextContent.Trim())
            .Should().Equal("GPT-4", "Claude 3.5", "Legacy");
    }

    [Fact]
    public void GroupingByNone_IsOneFlatListbox()
    {
        var cut = Open(parameters => parameters.Add(picker => picker.GroupBy, AiModelGroupBy.None));

        cut.FindAll("[data-testid='model-picker-group']").Should().BeEmpty();
        cut.FindAll("[data-testid='model-picker-group-header']").Should().BeEmpty();

        var list = cut.Find("[data-testid='model-picker-list']");
        list.QuerySelectorAll("[data-testid='model-picker-option']").Should().HaveCount(4);
    }

    // ── Row content ──────────────────────────────────────────────────────────

    [Fact]
    public void EveryRowCarriesItsSpeedQualityAndPriceBars_WithTheValuesInWords()
    {
        var cut = Open();

        var premium = Option(cut, "gpt-4o");
        premium.QuerySelector("[data-testid='model-picker-option-speed']")!.TextContent.Trim()
            .Should().Be("Speed 4/5");
        premium.QuerySelector("[data-testid='model-picker-option-quality']")!.TextContent.Trim()
            .Should().Be("Quality 5/5");
        premium.QuerySelector("[data-testid='model-picker-option-price']")!.TextContent.Trim()
            .Should().Be("$3.00 / 1M input");

        // The price bar is relative to the most expensive model in the list, so the cheap sibling is a sliver
        // and the premium models are full — that is what makes the bars comparable.
        BarWidth(premium, "speed").Should().Be("80%");
        BarWidth(premium, "quality").Should().Be("100%");
        BarWidth(premium, "price").Should().Be("100%");

        var cheap = Option(cut, "gpt-4o-mini");
        BarWidth(cheap, "speed").Should().Be("100%");
        BarWidth(cheap, "price").Should().Be("5%");
    }

    [Fact]
    public void TheBarsAreDecorative_AndTheirValuesAreRealText()
    {
        var cut = Open();

        foreach (var option in Options(cut))
        {
            foreach (var bar in option.QuerySelectorAll(".tm-model-picker__bar"))
            {
                bar.GetAttribute("aria-hidden").Should().Be("true",
                    "the bar is a picture of the number next to it");
            }
        }
    }

    [Fact]
    public void ARowCarriesItsContextWindowCapabilities_AndTheDeprecatedMarker()
    {
        var cut = Open();

        var claude = Option(cut, "claude-3-5-sonnet");
        claude.QuerySelector("[data-testid='model-picker-option-context']")!.TextContent.Trim()
            .Should().Be("200k context");
        claude.QuerySelector("[data-testid='model-picker-option-capabilities']")!.TextContent
            .Should().Contain("vision");

        var legacy = Option(cut, "legacy-text");
        legacy.QuerySelector("[data-testid='model-picker-option-deprecated']")!.TextContent.Trim()
            .Should().Be("Deprecated");
        legacy.QuerySelector("[data-testid='model-picker-option-context']").Should().BeNull();
        legacy.QuerySelector("[data-testid='model-picker-option-speed']").Should().BeNull(
            "an unreported score is an absent bar, never a zero");
        legacy.QuerySelector("[data-testid='model-picker-option-price']").Should().BeNull(
            "an unknown price is never rendered as free");
    }

    [Fact]
    public void TheMetricBarsCanBeTurnedOffOneByOne()
    {
        var cut = Open(parameters => parameters
            .Add(picker => picker.ShowSpeed, false)
            .Add(picker => picker.ShowQuality, false)
            .Add(picker => picker.ShowPrice, false)
            .Add(picker => picker.ShowContextWindow, false)
            .Add(picker => picker.ShowCapabilities, false));

        cut.FindAll("[data-testid='model-picker-option-speed']").Should().BeEmpty();
        cut.FindAll("[data-testid='model-picker-option-quality']").Should().BeEmpty();
        cut.FindAll("[data-testid='model-picker-option-price']").Should().BeEmpty();
        cut.FindAll("[data-testid='model-picker-option-context']").Should().BeEmpty();
        cut.FindAll("[data-testid='model-picker-option-capabilities']").Should().BeEmpty();
    }

    // ── Search ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheSearchFiltersTheList_AndHighlightsTheMatchedRun()
    {
        var cut = Open();

        await SearchAsync(cut, "mini");

        Options(cut).Select(option => option.GetAttribute("data-model-id")).Should().Equal("gpt-4o-mini");
        var marks = cut.FindAll("mark.tm-highlighted-text__match");
        marks.Should().NotBeEmpty();
        marks.Select(mark => mark.TextContent).Should().Contain("mini");

        // A group with no surviving model is not rendered at all.
        cut.FindAll("[data-testid='model-picker-group-header']")
            .Select(header => header.TextContent.Trim())
            .Should().Equal("OpenAI");
    }

    [Fact]
    public async Task TheSearchAlsoMatchesTheProviderFamilyAndCapabilities()
    {
        var cut = Open();

        await SearchAsync(cut, "anthropic");
        Options(cut).Select(option => option.GetAttribute("data-model-id"))
            .Should().Equal("claude-3-5-sonnet");

        await SearchAsync(cut, "vision");
        Options(cut).Select(option => option.GetAttribute("data-model-id"))
            .Should().Equal("gpt-4o", "claude-3-5-sonnet");

        await SearchAsync(cut, "GPT-4");
        Options(cut).Select(option => option.GetAttribute("data-model-id"))
            .Should().Equal("gpt-4o", "gpt-4o-mini");
    }

    [Fact]
    public async Task NoMatchRendersTheLocalizedEmptyState_AndTheRegionReportsTheCount()
    {
        var cut = Open();

        await SearchAsync(cut, "zzz");

        Options(cut).Should().BeEmpty();
        Text(cut, "model-picker-empty").Should().Be("No model matches \"zzz\".");
        Text(cut, "model-picker-announcement").Should().Be("Matches: 0");

        await SearchAsync(cut, "mini");
        Text(cut, "model-picker-announcement").Should().Be("Matches: 1");
    }

    [Fact]
    public void AnEmptyCatalogueSaysSo()
    {
        var cut = Open(models: []);

        cut.FindAll("[data-testid='model-picker-option']").Should().BeEmpty();
        Text(cut, "model-picker-empty").Should().Be("No models are available.");
    }

    [Fact]
    public void TheSearchFieldCanBeHidden_AndTheListboxThenOwnsTheTabStop()
    {
        var cut = Open(parameters => parameters.Add(picker => picker.ShowSearch, false));

        cut.FindAll("[data-testid='model-picker-search']").Should().BeEmpty();
        var list = cut.Find("[data-testid='model-picker-list']");
        list.GetAttribute("tabindex").Should().Be("0");
        list.GetAttribute("aria-activedescendant").Should().Be(Options(cut)[0].Id);
    }

    [Fact]
    public void OpeningFocusesTheSearchField_OrTheListboxWhenThereIsNone()
    {
        var cut = Open();
        cut.WaitForAssertion(() => FocusTargets().Should().HaveCount(1));

        var withoutSearch = Open(parameters => parameters.Add(picker => picker.ShowSearch, false));
        withoutSearch.WaitForAssertion(() => FocusTargets().Should().HaveCount(2));
    }

    // ── Selection ────────────────────────────────────────────────────────────

    [Fact]
    public void ClickingARowRaisesTheWholeSelection_AndClosesAndAnnounces()
    {
        var cut = Open();

        Option(cut, "claude-3-5-sonnet").Click();

        _selection.Should().NotBeNull();
        _selection!.ModelId.Should().Be("claude-3-5-sonnet");
        _selection.ExtendedThinking.Should().BeFalse("a fresh pick starts from the model's defaults");
        _selection.EffortLevel.Should().BeNull();

        cut.FindAll("[data-testid='model-picker-list']").Should().BeEmpty("the picker closes on a pick");
        Text(cut, "model-picker-announcement").Should().Be("Model Claude 3.5 Sonnet selected.");
    }

    [Fact]
    public void PickingTheSameModelKeepsItsReasoningOptions()
    {
        var cut = RenderPicker(selection: new AiModelSelection("gpt-4o")
        {
            ExtendedThinking = true,
            EffortLevel = "high",
        });

        cut.Find("[data-testid='model-picker-trigger']").Click();
        Option(cut, "gpt-4o").Click();

        _selection!.ModelId.Should().Be("gpt-4o");
        _selection.ExtendedThinking.Should().BeTrue();
        _selection.EffortLevel.Should().Be("high");
    }

    [Fact]
    public void CloseOnSelectFalse_KeepsTheListOpenForComparison()
    {
        var cut = Open(parameters => parameters.Add(picker => picker.CloseOnSelect, false));

        Option(cut, "claude-3-5-sonnet").Click();

        cut.FindAll("[data-testid='model-picker-list']").Should().NotBeEmpty();
        Option(cut, "claude-3-5-sonnet").GetAttribute("aria-selected").Should().Be("true",
            "the reader sees what the pick landed on while the list stays open");
    }

    [Fact]
    public async Task TheSelectionParameter_SelectsTheRow_AndTheHostValueWins()
    {
        var cut = Open(selection: new AiModelSelection("gpt-4o-mini"));

        Option(cut, "gpt-4o-mini").GetAttribute("aria-selected").Should().Be("true");
        Option(cut, "gpt-4o").GetAttribute("aria-selected").Should().Be("false");

        await SetAsync(cut, nameof(TmModelPicker.Selection), new AiModelSelection("gpt-4o"));

        Option(cut, "gpt-4o").GetAttribute("aria-selected").Should().Be("true");
        Text(cut, "model-picker-trigger-name").Should().Be("GPT-4o");
    }

    [Fact]
    public void ARowWithoutASelectionIsNotSelected_AndTheListIsSingleSelect()
    {
        var cut = Open();

        Options(cut).Should().OnlyContain(option => option.GetAttribute("aria-selected") == "false");
        cut.Find("[data-testid='model-picker-list']").GetAttribute("aria-multiselectable").Should().BeNull();
    }

    // ── Reasoning options ────────────────────────────────────────────────────

    [Fact]
    public void TheSelectedModelOffersExtendedThinking_AndTheToggleRaisesTheSameModel()
    {
        var cut = Open(selection: new AiModelSelection("gpt-4o"));

        var toggle = cut.Find("[data-testid='model-picker-thinking']");
        toggle.Should().NotBeNull();

        cut.Find("[data-testid='model-picker-thinking'] input").Change(true);
        cut.WaitForAssertion(() => _selection!.ExtendedThinking.Should().BeTrue());
        _selection!.ModelId.Should().Be("gpt-4o");
        Text(cut, "model-picker-announcement").Should().Be("Extended thinking on.");

        cut.Find("[data-testid='model-picker-thinking'] input").Change(false);
        cut.WaitForAssertion(() => _selection!.ExtendedThinking.Should().BeFalse());
        Text(cut, "model-picker-announcement").Should().Be("Extended thinking off.");
    }

    [Fact]
    public void AModelWithoutExtendedThinking_OffersNoToggle()
    {
        var cut = Open(selection: new AiModelSelection("gpt-4o-mini"));

        cut.FindAll("[data-testid='model-picker-thinking']").Should().BeEmpty();
    }

    [Fact]
    public void TheEffortControlIsOfferedOnlyForAModelWithLevels()
    {
        var withLevels = Open(selection: new AiModelSelection("claude-3-5-sonnet"));

        var effort = withLevels.Find("[data-testid='model-picker-effort']");
        effort.GetAttribute("role").Should().BeNull("the segmented control's tray carries the role");
        effort.QuerySelector("[role='radiogroup']").Should().NotBeNull();
        effort.QuerySelectorAll("[role='radio']").Should().HaveCount(3,
            "the model's own levels plus the model default");

        var withoutLevels = Open(selection: new AiModelSelection("gpt-4o-mini"));
        withoutLevels.FindAll("[data-testid='model-picker-effort']").Should().BeEmpty();
    }

    [Fact]
    public void PickingAnEffortLevel_RaisesTheSelectionAndAnnouncesIt()
    {
        var cut = Open(selection: new AiModelSelection("claude-3-5-sonnet"));

        cut.Find("[data-testid='model-picker-effort'] [data-value='high']").Click();

        cut.WaitForAssertion(() => _selection!.EffortLevel.Should().Be("high"));
        _selection!.ModelId.Should().Be("claude-3-5-sonnet");
        Text(cut, "model-picker-announcement").Should().Be("Reasoning effort: HIGH.");

        cut.FindAll("[data-testid='model-picker-effort'] [role='radio']")
            .Single(radio => radio.TextContent.Contains("Default")).Click();
        cut.WaitForAssertion(() => _selection!.EffortLevel.Should().BeNull());
        Text(cut, "model-picker-announcement").Should().Be("Reasoning effort: model default.");
    }

    [Fact]
    public void TheReasoningSwitchesHonourTheirShowFlags()
    {
        var cut = Open(
            selection: new AiModelSelection("gpt-4o") { EffortLevel = "low" },
            configure: parameters => parameters
                .Add(picker => picker.ShowExtendedThinking, false)
                .Add(picker => picker.ShowEffort, false));

        cut.FindAll("[data-testid='model-picker-thinking']").Should().BeEmpty();
        cut.FindAll("[data-testid='model-picker-effort']").Should().BeEmpty();
    }

    [Fact]
    public void TheOptionsBlockIsAbsent_WhenTheSelectedModelOffersNothing()
    {
        var cut = Open(selection: new AiModelSelection("gpt-4o-mini"));

        cut.FindAll("[data-testid='model-picker-options']").Should().BeEmpty();
    }

    [Fact]
    public void ThePriceEstimateUsesTheSelectedModelAndTheCulture()
    {
        var cut = Open(selection: new AiModelSelection("gpt-4o"),
            configure: parameters => parameters.Add(picker => picker.EstimatedPromptTokens, 100_000));

        Text(cut, "model-picker-estimate").Should().Be("Estimated prompt cost ≈ $0.30");

        var withoutTokens = Open(selection: new AiModelSelection("gpt-4o"));
        withoutTokens.FindAll("[data-testid='model-picker-estimate']").Should().BeEmpty();

        var unpriced = Open(selection: new AiModelSelection("legacy-text"),
            configure: parameters => parameters.Add(picker => picker.EstimatedPromptTokens, 100_000));
        unpriced.FindAll("[data-testid='model-picker-estimate']").Should().BeEmpty(
            "an unknown price cannot be estimated");
    }

    [Fact]
    public void AHostPriceFormatterWins()
    {
        var cut = Open(selection: new AiModelSelection("gpt-4o"),
            configure: parameters => parameters
                .Add(picker => picker.EstimatedPromptTokens, 100_000)
                .Add(picker => picker.PriceFormatter, amount => $"{amount * 100:0} credits"));

        Text(cut, "model-picker-estimate").Should().Be("Estimated prompt cost ≈ 30 credits");
        Option(cut, "gpt-4o").QuerySelector("[data-testid='model-picker-option-price']")!.TextContent.Trim()
            .Should().Be("300 credits / 1M input");
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    [Fact]
    public void ArrowDownOnTheTrigger_OpensAndMovesTheFocusIntoThePanel()
    {
        var cut = RenderPicker();

        cut.Find("[data-testid='model-picker-trigger']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='model-picker-list']").Should().HaveCount(1));
        cut.WaitForAssertion(() => FocusTargets().Should().NotBeEmpty());
    }

    [Fact]
    public void TheSearchComboboxMovesTheActiveOptionAndEnterSelectsIt()
    {
        var cut = Open();
        var search = cut.Find("[data-testid='model-picker-search']");

        search.GetAttribute("role").Should().Be("combobox");
        search.GetAttribute("aria-autocomplete").Should().Be("list");
        search.GetAttribute("aria-activedescendant").Should().Be(Options(cut)[0].Id);

        search.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find("[data-testid='model-picker-search']").GetAttribute("aria-activedescendant")
            .Should().Be(Options(cut)[1].Id);
        Options(cut)[1].ClassList.Should().Contain("tm-model-picker__option--active");

        cut.Find("[data-testid='model-picker-search']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        cut.Find("[data-testid='model-picker-search']").GetAttribute("aria-activedescendant")
            .Should().Be(Options(cut)[0].Id);

        cut.Find("[data-testid='model-picker-search']")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() => _selection!.ModelId.Should().Be("gpt-4o"));
        cut.FindAll("[data-testid='model-picker-list']").Should().BeEmpty("a keyboard pick closes the list too");
    }

    [Fact]
    public void ArrowUpFromTheFirstOptionStaysOnTheFirst_NotWrappingToTheEnd()
    {
        var cut = Open();
        var search = cut.Find("[data-testid='model-picker-search']");

        search.KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        cut.Find("[data-testid='model-picker-search']").GetAttribute("aria-activedescendant")
            .Should().Be(Options(cut)[0].Id);
    }

    [Fact]
    public async Task TheArrowKeysSkipTheActiveOptionWhenTheSearchFilteredTheList()
    {
        var cut = Open();

        await SearchAsync(cut, "gpt");
        cut.Find("[data-testid='model-picker-search']").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.Find("[data-testid='model-picker-search']").GetAttribute("aria-activedescendant")
            .Should().Be(Options(cut)[1].Id);
        Options(cut)[1].GetAttribute("data-model-id").Should().Be("gpt-4o-mini");
    }

    [Fact]
    public void TheListboxWithoutASearchField_AnswersArrowsEnterAndSpace()
    {
        var cut = Open(parameters => parameters.Add(picker => picker.ShowSearch, false));
        var list = cut.Find("[data-testid='model-picker-list']");

        list.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find("[data-testid='model-picker-list']").GetAttribute("aria-activedescendant")
            .Should().Be(Options(cut)[1].Id);

        cut.Find("[data-testid='model-picker-list']").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        _selection!.ModelId.Should().Be("gpt-4o-mini");

        // Space is emulated on keyup for a non-native focusable (the native split), so the keydown alone
        // must not pick anything.
        var second = Open(parameters => parameters.Add(picker => picker.ShowSearch, false));
        second.Find("[data-testid='model-picker-list']")
            .KeyDown(new KeyboardEventArgs { Key = " " });
        _selection.Should().BeNull("the second picker has no selection yet and Space has not lifted");

        second.Find("[data-testid='model-picker-list']")
            .KeyUp(new KeyboardEventArgs { Key = " " });
        _selection!.ModelId.Should().Be("gpt-4o");
    }

    [Fact]
    public void TheListboxOffersTypeahead_ThatCyclesFromTheActiveOption()
    {
        var cut = Open(parameters => parameters.Add(picker => picker.ShowSearch, false));
        var list = cut.Find("[data-testid='model-picker-list']");

        // One character jumps to the first model that starts with it.
        list.KeyDown(new KeyboardEventArgs { Key = "l" });
        cut.Find("[data-testid='model-picker-list']").GetAttribute("aria-activedescendant")
            .Should().Be(Option(cut, "legacy-text").Id);

        // A second press inside the buffer window cycles past the active row to the next match (and wraps),
        // because "ll" names nothing.
        cut.Find("[data-testid='model-picker-list']").KeyDown(new KeyboardEventArgs { Key = "l" });
        cut.Find("[data-testid='model-picker-list']").GetAttribute("aria-activedescendant")
            .Should().Be(Option(cut, "legacy-text").Id, "Legacy is the only model starting with L");

        // The buffer resolves a shared prefix: "g" then "p" narrows to the GPT rows, "t" stays on them.
        cut.Find("[data-testid='model-picker-list']").KeyDown(new KeyboardEventArgs { Key = "g" });
        cut.Find("[data-testid='model-picker-list']").KeyDown(new KeyboardEventArgs { Key = "p" });
        cut.Find("[data-testid='model-picker-list']").KeyDown(new KeyboardEventArgs { Key = "t" });
        cut.Find("[data-testid='model-picker-list']").GetAttribute("aria-activedescendant")
            .Should().Be(Option(cut, "gpt-4o").Id);

        // A character nobody starts with leaves the active option alone.
        cut.Find("[data-testid='model-picker-list']").KeyDown(new KeyboardEventArgs { Key = "z" });
        cut.Find("[data-testid='model-picker-list']").GetAttribute("aria-activedescendant")
            .Should().Be(Option(cut, "gpt-4o").Id);
    }

    [Fact]
    public void ClosingRestoresTheFocusToTheTrigger_ButADismissalLeavesItToThePanel()
    {
        var cut = Open();

        Option(cut, "gpt-4o").Click();
        cut.WaitForAssertion(() => FocusTargets().Should().HaveCount(2));
    }

    // ── Accessibility and culture ────────────────────────────────────────────

    [Fact]
    public void ThereIsExactlyOnePoliteLiveRegion()
    {
        var cut = Open();

        var regions = cut.FindAll("[role='status']");
        regions.Should().HaveCount(1);
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].GetAttribute("data-testid").Should().Be("model-picker-announcement");
    }

    [Fact]
    public void TheTriggerIsDescribedByTheInstructions()
    {
        var cut = RenderPicker();

        var trigger = cut.Find("[data-testid='model-picker-trigger']");
        var describedBy = trigger.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{describedBy}").TextContent.Should().Contain("up and down arrows");
    }

    [Fact]
    public void TheGroupByControl_IsReflectedOnTheRoot()
    {
        var cut = RenderPicker(configure: parameters => parameters.Add(picker => picker.GroupBy, AiModelGroupBy.None));

        cut.Find("[data-testid='model-picker']").GetAttribute("data-group-by").Should().Be("none");

        cut.Find("[data-testid='model-picker-trigger']").Click();
        cut.Find("[data-testid='model-picker']").GetAttribute("data-open").Should().Be("true");
    }

    [Fact]
    public void TheCzechCultureFormatsTheMoneyAndTheContextWindowInCzech()
    {
        UseCzechLocalization();
        var cut = RenderPicker(selection: new AiModelSelection("gpt-4o"),
            configure: parameters => parameters.Add(picker => picker.EstimatedPromptTokens, 100_000),
            culture: CultureInfo.GetCultureInfo("cs-CZ"));

        cut.Find("[data-testid='model-picker-trigger']").Click();

        Normalize(Option(cut, "gpt-4o").QuerySelector("[data-testid='model-picker-option-price']")!.TextContent)
            .Should().Be("3,00 Kč / 1M vstup");
        Option(cut, "claude-3-5-sonnet").QuerySelector("[data-testid='model-picker-option-context']")!
            .TextContent.Trim().Should().Be("200k kontext");
        Normalize(Text(cut, "model-picker-estimate")).Should().Be("Odhad ceny promptu ≈ 0,30 Kč");
    }

    /// <summary>Collapses the non-breaking spaces a currency format may use into plain spaces.</summary>
    private static string Normalize(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static string? BarWidth(IElement option, string metric)
        => option.QuerySelector($"[data-testid='model-picker-option-{metric}'] .tm-model-picker__bar-fill")
            ?.GetAttribute("style")
            ?.Split(';')
            .Select(part => part.Trim())
            .Where(part => part.StartsWith("--tm-model-picker-bar", StringComparison.Ordinal))
            .Select(part => part[(part.IndexOf(':') + 1)..].Trim())
            .FirstOrDefault();
}
