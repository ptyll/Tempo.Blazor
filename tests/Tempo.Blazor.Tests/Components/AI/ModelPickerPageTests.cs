using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// The <c>/model-picker</c> demo page: the picker at full strength, two variants, the live selection stream and
/// the edge cases. These facts walk the wiring a reader actually uses — open the catalogue, pick a model, read
/// the page's own selection, adjust the reasoning options, clear the choice, watch the event log and inspect
/// the edge states — so a page that only renders the component cannot pass.
/// </summary>
public class ModelPickerPageTests : DemoPageLocalizationTestBase
{
    private IRenderedComponent<ModelPickerPage> RenderPage() => Render<ModelPickerPage>();

    private static IElement Picker(IRenderedComponent<ModelPickerPage> cut, string testId)
        => cut.Find($"[data-testid='{testId}']");

    private static IElement Trigger(IRenderedComponent<ModelPickerPage> cut, string testId)
        => Picker(cut, testId).QuerySelector("[data-testid='model-picker-trigger']")!;

    private static IReadOnlyList<IElement> Options(IRenderedComponent<ModelPickerPage> cut, string testId)
        => Picker(cut, testId).QuerySelectorAll("[data-testid='model-picker-option']").ToList();

    private static IElement Option(IRenderedComponent<ModelPickerPage> cut, string testId, string modelId)
        => Options(cut, testId).Single(option => option.GetAttribute("data-model-id") == modelId);

    private static string Text(IRenderedComponent<ModelPickerPage> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    [Fact]
    public void ThePageRendersItsFourSectionsWithoutRawKeys()
    {
        var cut = RenderPage();

        cut.FindAll("section.demo-section").Should().HaveCount(4);
        foreach (var section in new[]
                 {
                     "model-picker-basic-section", "model-picker-variants-section",
                     "model-picker-events-section", "model-picker-edge-section",
                 })
        {
            cut.Find($"[data-testid='{section}']").TextContent.Should().NotContain("[Tm");
        }
    }

    [Fact]
    public void TheBasicPickerStartsWithTheFlagship_AndThePageReadsIt()
    {
        var cut = RenderPage();

        Trigger(cut, "model-picker-basic").TextContent.Should().Contain("GPT-4o");
        Text(cut, "model-picker-selection-readout")
            .Should().Be("Model: GPT-4o · extended thinking: off · effort: not set");
        Text(cut, "model-picker-selection-model").Should().Be("gpt-4o");
    }

    [Fact]
    public void PickingAModelUpdatesThePageReadout()
    {
        var cut = RenderPage();

        Trigger(cut, "model-picker-basic").Click();
        Option(cut, "model-picker-basic", "claude-3-5-sonnet").Click();

        Text(cut, "model-picker-selection-readout")
            .Should().Be("Model: Claude 3.5 Sonnet · extended thinking: off · effort: not set");
        Text(cut, "model-picker-selection-model").Should().Be("claude-3-5-sonnet");
        Picker(cut, "model-picker-basic")
            .QuerySelectorAll("[data-testid='model-picker-list']").Should().BeEmpty("a pick closes the list");
    }

    [Fact]
    public void TheReasoningOptionsOfThePickedModelAreOffered_AndReachTheReadout()
    {
        var cut = RenderPage();

        Trigger(cut, "model-picker-basic").Click();
        Option(cut, "model-picker-basic", "gpt-4o").Click();

        Trigger(cut, "model-picker-basic").Click();
        var thinking = Picker(cut, "model-picker-basic").QuerySelector("[data-testid='model-picker-thinking'] input");
        thinking.Should().NotBeNull();
        thinking!.Change(true);

        Text(cut, "model-picker-selection-readout")
            .Should().Be("Model: GPT-4o · extended thinking: on · effort: not set");

        Picker(cut, "model-picker-basic")
            .QuerySelector("[data-testid='model-picker-effort'] [data-value='medium']")!.Click();

        Text(cut, "model-picker-selection-readout")
            .Should().Be("Model: GPT-4o · extended thinking: on · effort: Medium");
    }

    [Fact]
    public void ThePresetAndTheClearButtonsDriveTheSameSelection()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='model-picker-preset-claude']").Click();

        Text(cut, "model-picker-selection-readout")
            .Should().Be("Model: Claude 3.5 Sonnet · extended thinking: on · effort: High");

        cut.Find("[data-testid='model-picker-reset']").Click();

        Text(cut, "model-picker-selection-readout").Should().Be("No model selected.");
        Text(cut, "model-picker-selection-model").Should().Be("not set");
    }

    [Fact]
    public void TheVariantsPickIndependently()
    {
        var cut = RenderPage();

        Trigger(cut, "model-picker-family").Click();
        Picker(cut, "model-picker-family").QuerySelectorAll("[data-testid='model-picker-group-header']")
            .Select(header => header.TextContent.Trim())
            .Should().Equal("GPT-4", "Claude 3.5", "Open weights", "Legacy");

        Option(cut, "model-picker-family", "local-8b").Click();

        // The compact control keeps its own choice: the variants share the catalogue, not the selection.
        Trigger(cut, "model-picker-compact").TextContent.Should().Contain("Claude 3.5 Sonnet");
        Picker(cut, "model-picker-compact")
            .QuerySelectorAll("[data-testid='model-picker-search']").Should().BeEmpty();
        Picker(cut, "model-picker-compact")
            .QuerySelectorAll("[data-testid='model-picker-option-quality']").Should().BeEmpty();
        Trigger(cut, "model-picker-family").TextContent.Should().Contain("Local 8B");
    }

    [Fact]
    public void TheOpenListReportsEveryChangeToThePageLog()
    {
        var cut = RenderPage();

        // Nothing has been raised yet.
        Text(cut, "model-picker-events-empty").Should().Be("No change yet.");

        Trigger(cut, "model-picker-events").Click();
        Option(cut, "model-picker-events", "gpt-4o-mini").Click();

        Picker(cut, "model-picker-events")
            .QuerySelectorAll("[data-testid='model-picker-list']").Should().NotBeEmpty(
                "CloseOnSelect=false keeps the list open");
        cut.FindAll("[data-testid='model-picker-events-entry']").Select(entry => entry.TextContent.Trim())
            .Should().ContainSingle().Which.Should()
            .Be("Picked GPT-4o mini (thinking off, effort not set)");

        Option(cut, "model-picker-events", "claude-3-5-sonnet").Click();

        cut.FindAll("[data-testid='model-picker-events-entry']").Select(entry => entry.TextContent.Trim())
            .Should().HaveCount(2);
        cut.FindAll("[data-testid='model-picker-events-entry']")[0].TextContent.Trim()
            .Should().Be("Picked Claude 3.5 Sonnet (thinking off, effort not set)",
                "the newest event is on top");
    }

    [Fact]
    public void TheEmptyCatalogueSaysSo()
    {
        var cut = RenderPage();

        Trigger(cut, "model-picker-empty").Click();

        Picker(cut, "model-picker-empty").QuerySelector("[data-testid='model-picker-empty']")!.TextContent.Trim()
            .Should().Be("No models are available.");
    }

    [Fact]
    public void TheDisabledPickerDoesNotOpen()
    {
        var cut = RenderPage();

        Trigger(cut, "model-picker-disabled").HasAttribute("disabled").Should().BeTrue();
        Trigger(cut, "model-picker-disabled").Click();

        Picker(cut, "model-picker-disabled")
            .QuerySelectorAll("[data-testid='model-picker-list']").Should().BeEmpty();
    }

    [Fact]
    public void TheDeprecatedSelectionAndTheRetiredModelAreStillNamed()
    {
        var cut = RenderPage();

        Trigger(cut, "model-picker-deprecated")
            .QuerySelector("[data-testid='model-picker-trigger-deprecated']")!.TextContent.Trim()
            .Should().Be("Deprecated");
        Trigger(cut, "model-picker-missing").TextContent.Should().Contain("retired-model");
    }

    [Fact]
    public void TheHostOwnsTheMoneyVocabulary()
    {
        // The money and credits assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderPage();

        Trigger(cut, "model-picker-credits").Click();

        Option(cut, "model-picker-credits", "gpt-4o")
            .QuerySelector("[data-testid='model-picker-option-price']")!.TextContent.Trim()
            .Should().Be("300 credits / 1M input");

        // The demo's own currency formatter (the demo hosts run the invariant culture, where a library
        // currency format would paint "¤") is used by the other pickers.
        Trigger(cut, "model-picker-basic").Click();
        Option(cut, "model-picker-basic", "gpt-4o")
            .QuerySelector("[data-testid='model-picker-option-price']")!.TextContent.Trim()
            .Should().Be("$3.00 / 1M input");
        Picker(cut, "model-picker-basic")
            .QuerySelector("[data-testid='model-picker-estimate']")!.TextContent.Trim()
            .Should().Be("Estimated prompt cost ≈ $0.30");
    }
}
