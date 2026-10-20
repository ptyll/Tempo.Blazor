using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for <see cref="TmPdfAnnotatorPanel"/>: the draft indicator color must come from
/// <see cref="PdfAnnotationColorHelper"/>, not from a hex literal hard-coded in the component
/// (PdfViewer redesign phase 2 — the palette is data owned by the helper).
/// </summary>
public class TmPdfAnnotatorPanelTests : LocalizationTestBase
{
    [Fact]
    public void Draft_WithoutColor_UsesHelperDefault_NotHex()
    {
        // The helper's first palette entry is the library's default annotation color (blue 600).
        var expected = PdfAnnotationColorHelper.DefaultPalette[0];

        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.Draft, new PdfAnnotatorDraft { Kind = DocumentAnnotationKind.Highlight }));

        var dot = cut.Find(".tm-pdf-annotator-panel__dot");
        dot.GetAttribute("style").Should().Be(
            $"background:{expected}",
            "the draft indicator paints the helper's default annotation color");

        // The components must not carry the color as a hex literal default either — a re-skin of
        // the palette would otherwise leave the draft behind (it is helper data, not markup data).
        foreach (var component in new[]
                 {
                     "TmPdfAnnotatorPanel.razor",
                     "TmPdfAnnotatorLayer.razor",
                 })
        {
            var source = File.ReadAllText(Path.Combine(
                ThemeCss.RepositoryRoot().FullName,
                "src", "Tempo.Blazor.PdfViewer", "Components", "Files", component));
            source.Should().NotContain(expected,
                $"{component} resolves the draft color through {nameof(PdfAnnotationColorHelper)}");
        }
    }

    [Fact]
    public void Draft_WithColor_UsesTheGivenColor()
    {
        using var cut = Render<TmPdfAnnotatorPanel>(parameters => parameters
            .Add(p => p.Draft, new PdfAnnotatorDraft { Kind = DocumentAnnotationKind.Highlight })
            .Add(p => p.DraftColor, "#0e7490"));

        cut.Find(".tm-pdf-annotator-panel__dot").GetAttribute("style")
            .Should().Be("background:#0e7490");
    }
}
