using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// The phase-12 slice of <see cref="TmPdfAnnotatorTests"/>: the runtime color picker
/// (FZE-12) and the resolve/reopen filter-radio flows. Split from TmPdfAnnotatorTests.cs
/// to keep both under the 1200-line ceiling (N270/F13).
/// </summary>
public partial class TmPdfAnnotatorTests
{
    [Fact]
    public void Panel_ResolveThread_MarksResolvedAndHidesFromDefaultFilter()
    {
        var cut = Render(Seeded(Highlight("h1")));

        cut.WaitForElement("[data-testid='pdf-annotator-thread']").Click();
        cut.WaitForElement("[data-testid='pdf-annotator-resolve']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-thread']").Should().BeEmpty());

        cut.Find("[data-testid='pdf-annotator-filter-all']").Change(true);
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-thread']").Should().HaveCount(1));
    }

    [Fact]
    public void Panel_ReopenResolvedThread_MakesItOpenAgain()
    {
        var resolved = Highlight("h1");
        resolved.Status = DocumentCommentThreadStatus.Resolved;
        var cut = Render(Seeded(resolved));

        cut.Find("[data-testid='pdf-annotator-filter-all']").Change(true);
        cut.WaitForElement("[data-testid='pdf-annotator-thread']").Click();
        cut.WaitForElement("[data-testid='pdf-annotator-reopen']").Click();

        cut.Find("[data-testid='pdf-annotator-filter-unresolved']").Change(false);
        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pdf-annotator-thread']").Should().HaveCount(1));
    }

    // ── FZE-12: runtime color picker for new annotations ─────────────────────

    /// <summary>Opens the color picker and picks the second default-palette swatch (#b45309).</summary>
    private IRenderedComponent<TmPdfAnnotator> PickColor(
        IPdfAnnotationProvider? provider = null,
        Action<Bunit.ComponentParameterCollectionBuilder<TmPdfAnnotator>>? configure = null)
    {
        var cut = Render(provider, configure);
        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();
        cut.FindAll(".tm-color-palette-swatch")[1].Click();
        return cut;
    }

    [Fact]
    public void ColorPicker_OpensPaletteFromColorChip()
    {
        var cut = Render();

        cut.FindAll(".tm-color-palette").Should().BeEmpty("the palette is closed until the trigger is pressed");

        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();

        var palette = cut.Find(".tm-color-palette");
        palette.Should().NotBeNull();
        cut.FindAll(".tm-color-palette-swatch").Should().HaveCount(PdfAnnotationColorHelper.DefaultPalette.Count);
        cut.Find("[data-testid='pdf-annotator-color-trigger']").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public async Task ColorPicker_Select_SetsColorForNewHighlightStampInkAndComment()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = PickColor(provider);

        // Stamp: click the surface — the thread carries the picked color.
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 200, OffsetY = 100 });
        cut.WaitForAssertion(async () =>
            (await provider.GetThreadsAsync(DocId))[0].Color.Should().Be("#b45309", "stamps pick up the runtime color"));

        // Highlight: select text in highlight mode, submit the draft composer.
        cut.Find("[data-testid='pdf-annotator-mode-highlight']").Click();
        var viewer = cut.FindComponent<TmPdfViewer>().Instance;
        await cut.InvokeAsync(() => viewer.OnTextSelectionChanged("picked words", 1, [0.1, 0.2, 0.3, 0.02]));
        cut.WaitForElement("[data-testid='pdf-annotator-new-input']").Input("highlight note");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();
        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Single(t => t.Kind == DocumentAnnotationKind.Highlight).Color
                .Should().Be("#b45309", "highlights pick up the runtime color");
        });

        // Ink: draw a stroke and save the draft.
        cut.Find("[data-testid='pdf-annotator-mode-draw']").Click();
        var surface = cut.Find("[data-testid='pdf-annotator-surface']");
        await surface.TriggerEventAsync("onpointerdown", new PointerEventArgs { OffsetX = 80, OffsetY = 100 });
        await surface.TriggerEventAsync("onpointermove", new PointerEventArgs { OffsetX = 160, OffsetY = 200 });
        await surface.TriggerEventAsync("onpointerup", new PointerEventArgs { OffsetX = 160, OffsetY = 200 });
        cut.WaitForElement("[data-testid='pdf-annotator-new-submit']").Click();
        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Single(t => t.Kind == DocumentAnnotationKind.Drawing).Color
                .Should().Be("#b45309", "ink strokes pick up the runtime color");
        });

        // Comment: click the surface in comment mode, submit the note.
        cut.Find("[data-testid='pdf-annotator-mode-comment']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 120, OffsetY = 80 });
        cut.WaitForElement("[data-testid='pdf-annotator-new-input']").Input("comment note");
        cut.Find("[data-testid='pdf-annotator-new-submit']").Click();
        cut.WaitForAssertion(async () =>
        {
            var threads = await provider.GetThreadsAsync(DocId);
            threads.Single(t => t.Kind == DocumentAnnotationKind.Comment).Color
                .Should().Be("#b45309", "comment threads pick up the runtime color");
        });
    }

    [Fact]
    public async Task ColorPicker_RaisesAnnotationColorChanged_AndHonoursBoundValue()
    {
        string? raised = null;
        var cut = Render(configure: p => p
            .Add(x => x.AnnotationColorChanged, EventCallback.Factory.Create<string?>(this, v => raised = v)));

        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();
        cut.FindAll(".tm-color-palette-swatch")[1].Click();

        raised.Should().Be("#b45309", "the selection flows through the two-way AnnotationColorChanged");

        // A bound host echoes the value back: the trigger chip and the draft indicator follow.
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object> { [nameof(TmPdfAnnotator.AnnotationColor)] = "#b45309" })
        )).GetAwaiter().GetResult();
        cut.Render();
        cut.Find(".tm-pdf-annotator__color-chip").GetAttribute("style").Should().Contain("#b45309");

        // A host-supplied value wins even without the picker: new annotations carry it.
        var provider = new InMemoryPdfAnnotationProvider();
        var bound = Render(provider, p => p.Add(x => x.AnnotationColor, "#15803d"));
        bound.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await bound.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 200, OffsetY = 100 });
        bound.WaitForAssertion(async () =>
            (await provider.GetThreadsAsync(DocId))[0].Color.Should().Be("#15803d"));
    }

    [Fact]
    public void ColorPicker_DoesNotRecolorExistingThreads()
    {
        var cut = PickColor(Seeded(Highlight("h1")));

        // The existing thread's badge keeps its author-resolved color — the provider has no
        // color update and the picked color applies to NEW annotations only.
        cut.Find(".tm-pdf-thread-card__badge").GetAttribute("style")
            .Should().NotContain("#b45309", "existing threads are never recolored");
    }

    [Fact]
    public async Task ColorPicker_Reset_FallsBackToAuthorRoleColor()
    {
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider, p => p.Add(x => x.RoleColors, new Dictionary<string, string>
        {
            ["lawyer"] = "#b45309"
        }));

        // Pick a different color, then reset back to the per-role color (the picker closes on
        // a pick, so the reset action reopens the palette first).
        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();
        cut.FindAll(".tm-color-palette-swatch")[4].Click(); // green 700
        cut.Render();
        cut.Find(".tm-pdf-annotator__color-chip").GetAttribute("style").Should().Contain("#15803d");

        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();
        cut.Find("[data-testid='pdf-annotator-color-reset']").Click();
        cut.Render();
        cut.Find(".tm-pdf-annotator__color-chip").GetAttribute("style")
            .Should().Contain("#b45309", "the reset returns to the per-author/role color");

        // New annotations after the reset carry NO explicit color again.
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 200, OffsetY = 100 });
        cut.WaitForAssertion(async () =>
            (await provider.GetThreadsAsync(DocId))[0].Color.Should().BeNull());
    }

    [Fact]
    public async Task ColorPicker_Reset_BoundHost_RaisesNull_RoleColorChipAndNullStampColor()
    {
        string? raised = null;
        var provider = new InMemoryPdfAnnotationProvider();
        var cut = Render(provider, p => p
            .Add(x => x.RoleColors, new Dictionary<string, string> { ["lawyer"] = "#b45309" })
            .Add(x => x.AnnotationColorChanged, EventCallback.Factory.Create<string?>(this, v => raised = v)));

        // Pick green and echo it back like a two-way bound host.
        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();
        cut.FindAll(".tm-color-palette-swatch")[4].Click(); // green 700
        raised.Should().Be("#15803d");
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.AnnotationColor)] = "#15803d" })));
        cut.Render();
        cut.Find(".tm-pdf-annotator__color-chip").GetAttribute("style").Should().Contain("#15803d");

        // Reopen and reset: the bound host sees NULL (the per-author/role fallback), the chip
        // returns to the role color and the picker closes.
        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();
        cut.Find("[data-testid='pdf-annotator-color-reset']").Click();
        raised.Should().BeNull("the reset raises null through the two-way AnnotationColorChanged");
        cut.Render();
        cut.Find(".tm-pdf-annotator__color-chip").GetAttribute("style")
            .Should().Contain("#b45309", "the chip shows the role color again");

        // The bound host echoes null back; a NEW stamp carries NO explicit color.
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmPdfAnnotator.AnnotationColor)] = (string?)null })));
        cut.Render();
        cut.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await cut.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 200, OffsetY = 100 });
        cut.WaitForAssertion(async () =>
            (await provider.GetThreadsAsync(DocId))[0].Color.Should().BeNull());
    }

    [Fact]
    public async Task ColorPicker_AuthorColorFallback_CheckedSwatch_NoCallback_ResetDisabled()
    {
        string? raised = null;
        var cut = Render(configure: p => p
            .Add(x => x.AnnotationColorChanged, EventCallback.Factory.Create<string?>(this, v => raised = v)));

        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();

        // No explicit pick: the resolved per-author color is the checked swatch — the palette's
        // display Value is the same color the chip shows.
        var authorColor = PdfAnnotationColorHelper.ResolveForAuthor(Alice.UserId, null, null, [Alice]);
        cut.Find(".tm-color-palette-swatch--selected").GetAttribute("style")
            .Should().Contain(authorColor, "the per-author/role fallback is the checked swatch");
        cut.Find(".tm-pdf-annotator__color-chip").GetAttribute("style").Should().Contain(authorColor);

        // The reset action is disabled while the author color is already shown.
        cut.Find("[data-testid='pdf-annotator-color-reset']").HasAttribute("disabled")
            .Should().BeTrue("there is nothing to reset while the author color is shown");

        // Selecting the already-checked fallback swatch must NOT raise AnnotationColorChanged.
        cut.Find(".tm-color-palette-swatch--selected").Click();
        raised.Should().BeNull("selecting the fallback swatch keeps the implicit author/role resolution");

        // The chip keeps the fallback color and a new annotation carries no explicit color.
        cut.Find(".tm-pdf-annotator__color-chip").GetAttribute("style").Should().Contain(authorColor);
        var provider = new InMemoryPdfAnnotationProvider();
        var bound = Render(provider);
        bound.Find("[data-testid='pdf-annotator-color-trigger']").Click();
        bound.Find(".tm-color-palette-swatch--selected").Click();
        bound.Find("[data-testid='pdf-annotator-mode-stamp']").Click();
        await bound.Find("[data-testid='pdf-annotator-surface']")
            .TriggerEventAsync("onclick", new MouseEventArgs { OffsetX = 200, OffsetY = 100 });
        bound.WaitForAssertion(async () =>
            (await provider.GetThreadsAsync(DocId))[0].Color.Should().BeNull());
    }

    [Fact]
    public void ColorPicker_CustomColorPalette_Rendered()
    {
        var cut = Render(provider: null, p => p.Add(x => x.ColorPalette, new[] { "#112233", "#445566" }));

        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();

        cut.FindAll(".tm-color-palette-swatch").Should().HaveCount(2);
        cut.FindAll(".tm-color-palette-swatch")[0].GetAttribute("style").Should().Contain("#112233");
        cut.FindAll(".tm-color-palette-swatch")[1].GetAttribute("style").Should().Contain("#445566");
    }

    [Fact]
    public void ColorPicker_SwatchesHaveLocalizedNames()
    {
        var cut = Render();

        cut.Find("[data-testid='pdf-annotator-color-trigger']").Click();

        // The default palette announces its localized color names ("Select color {0}").
        cut.FindAll(".tm-color-palette-swatch")[0].GetAttribute("aria-label")
            .Should().Contain("Blue", "the first default swatch is named Blue");
        cut.FindAll(".tm-color-palette-swatch")[1].GetAttribute("aria-label")
            .Should().Contain("Amber", "the second default swatch is named Amber");
    }

    [Fact]
    public void ColorPicker_Mobile_OpensAsBottomSheet()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile);

        // G2: the palette tile rides the Browse/Add-comment tile row and opens the palette as
        // the F3 bottom sheet.
        cut.Find("[data-testid='pdf-annotator-color-tile']").Click();

        cut.Find(".tm-overlay-panel-sheet .tm-color-palette").Should().NotBeNull(
            "on a mobile layout the palette opens as a bottom sheet");
        cut.FindAll(".tm-color-palette-swatch").Should().HaveCount(PdfAnnotationColorHelper.DefaultPalette.Count);
    }

    [Fact]
    public void ColorPicker_AllowColorPickerFalse_KeepsStaticChipAndLabel()
    {
        var cut = Render(provider: null, p => p.Add(x => x.AllowColorPicker, false));

        cut.FindAll("[data-testid='pdf-annotator-color-trigger']").Should().BeEmpty(
            "no trigger button without the picker");
        cut.FindAll("[data-testid='pdf-annotator-color-tile']").Should().BeEmpty(
            "no mobile palette tile without the picker");

        // The pre-FZE-12 static chip + label pair remains as the plain indicator.
        var chip = cut.Find(".tm-pdf-annotator__color-chip");
        chip.TagName.Should().Be("SPAN");
        chip.HasAttribute("aria-hidden").Should().BeTrue();
        cut.Find("[data-testid='pdf-annotator-color-label']").TextContent.Should().Contain("annotation color");
    }
}
