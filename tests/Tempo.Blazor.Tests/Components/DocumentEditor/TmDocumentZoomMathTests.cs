using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

// Phase 11 (fix round): the arithmetic and the text of the zoom controls.
public class TmDocumentZoomMathTests : LocalizationTestBase
{
    [Theory]
    [InlineData("en", "62%")]
    [InlineData("cs", "62 %")]
    [InlineData("sk", "62 %")]
    [InlineData("fr", "62 %")]
    public void FormatPercent_FollowsTheCulture_AndNeverBreaksTheNumberFromItsSign(string culture, string expected)
    {
        var text = DocumentZoomMath.FormatPercent(62, CultureInfo.GetCultureInfo(culture));

        // a regular or narrow space the culture might use is normalised by the formatter: the number and the sign never wrap apart
        text.Replace(' ', ' ').Should().Be(expected);
    }

    [Theory]
    [InlineData(62, true, 70)]
    [InlineData(62, false, 60)]
    [InlineData(100, true, 110)]
    [InlineData(100, false, 90)]
    [InlineData(90, false, 80)]
    [InlineData(38, true, 50)]
    [InlineData(38, false, 50)]
    [InlineData(195, true, 200)]
    [InlineData(200, true, 200)]
    public void Step_SnapsToTheGridOfTheStep_AndStaysInTheReaderRange(int percent, bool up, int expected)
        => DocumentZoomMath.Step(percent, 10, 50, 200, up).Should().Be(expected);

    [Fact]
    public void StatusBar_StepFromAFittedPercent_LandsOnTheGrid_NotOnAnOffGridValue()
    {
        var raised = new List<int>();
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.ZoomPercent, 62)
            .Add(x => x.ZoomMin, 25)
            .Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, raised.Add)));

        cut.Find("[data-testid='document-status-zoom-in']").Click();
        cut.Find("[data-testid='document-status-zoom-out']").Click();

        raised.Should().Equal(70, 60);
    }

    [Fact]
    public void StatusBar_ZoomValue_IsAMenuButton_NamedByTheFittedAriaLabel()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.ZoomPercent, 62)
            .Add(x => x.ZoomLabel, "Fit 62%")
            .Add(x => x.ZoomAriaLabel, "Zoom 62 percent, fitted to width")
            .Add(x => x.ZoomPreset, "fitWidth")
            .Add(x => x.ZoomAutoFit, "narrow")
            .Add(x => x.ShowAutomaticZoom, true)
            .Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, _ => { })));

        var trigger = cut.Find("[data-testid='document-status-zoom'] .tm-dropdown-trigger");
        trigger.TextContent.Should().Contain("Fit 62%");
        trigger.GetAttribute("aria-label").Should().Be("Zoom 62 percent, fitted to width");
        trigger.GetAttribute("aria-haspopup").Should().Be("true");
    }

    [Fact]
    public void StatusBar_ZoomMenu_RaisesTheMatchingCallbackForEveryEntry()
    {
        var log = new List<string>();
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.ShowAutomaticZoom, true)
            .Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, percent => log.Add($"percent:{percent}")))
            .Add(x => x.OnZoomAutomatic, EventCallback.Factory.Create(this, () => log.Add("auto")))
            .Add(x => x.OnZoomPageWidth, EventCallback.Factory.Create(this, () => log.Add("pagewidth")))
            .Add(x => x.OnZoomWholePage, EventCallback.Factory.Create(this, () => log.Add("wholepage"))));

        foreach (var item in new[] { "auto", "page-width", "whole-page", "125" })
        {
            cut.Find("[data-testid='document-status-zoom'] .tm-dropdown-trigger").Click();
            cut.WaitForElement($"[data-testid='document-status-zoom-{item}']").Click();
            cut.WaitForAssertion(() => cut.FindAll("[data-testid='document-status-zoom-auto']").Should().BeEmpty("the menu closed after the pick"));
        }

        log.Should().Equal("auto", "pagewidth", "wholepage", "percent:125");
    }

    [Fact]
    public void StatusBar_WithoutAutomaticZoom_OffersNoAutomaticEntry()
    {
        var cut = Render<TmDocumentEditorStatusBar>(p => p
            .Add(x => x.OnZoomChanged, EventCallback.Factory.Create<int>(this, _ => { })));
        cut.Find("[data-testid='document-status-zoom'] .tm-dropdown-trigger").Click();

        cut.FindAll("[data-testid='document-status-zoom-auto']").Should().BeEmpty();
    }
}
