using Bunit;
using Microsoft.AspNetCore.Components;
using FluentAssertions;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Files;

/// <summary>
/// bUnit tests for the shared <see cref="TmPdfThreadCard"/> — one card implementation used by
/// <see cref="TmPdfAnnotationThreadPanel"/> and <see cref="TmPdfAnnotatorPanel"/> (PdfViewer
/// redesign phase 4, PD4): colored badge, plain-case kind label, preview, "author · time",
/// "Str. N" page reference, neutral reply count, chevron, and a success-subtle resolved chip.
/// </summary>
public class TmPdfThreadCardTests : LocalizationTestBase
{
    private static DateTimeOffset At(int hour) => new(2025, 6, 1, hour, 0, 0, TimeSpan.Zero);

    private IRenderedComponent<TmPdfThreadCard> Render(Action<Bunit.ComponentParameterCollectionBuilder<TmPdfThreadCard>>? configure = null)
    {
        return Render<TmPdfThreadCard>(p =>
        {
            p.Add(x => x.ThreadId, "t1");
            p.Add(x => x.Number, 1);
            p.Add(x => x.Preview, "First comment preview");
            p.Add(x => x.AuthorName, "Alena");
            p.Add(x => x.CreatedAt, At(9));
            p.Add(x => x.PageNumber, 3);
            p.Add(x => x.ReplyCount, 2);
            p.Add(x => x.HeaderTestId, "pdf-annotation-thread");
            p.Add(x => x.DetailTestId, "pdf-annotation-detail");
            configure?.Invoke(p);
        });
    }

    [Fact]
    public void Header_HasAriaExpandedReflectingSelection()
    {
        var expanded = Render(p => p
            .Add(x => x.Selected, true)
            .Add(x => x.DetailId, "detail-1"));

        var header = expanded.Find("[data-testid='pdf-annotation-thread']");
        header.GetAttribute("aria-expanded").Should().Be("true");
        header.GetAttribute("aria-controls").Should().Be("detail-1");
        expanded.Find("[data-testid='pdf-annotation-detail']").Id.Should().Be("detail-1");

        var collapsed = Render(p => p.Add(x => x.Selected, false));
        var collapsedHeader = collapsed.Find("[data-testid='pdf-annotation-thread']");
        collapsedHeader.GetAttribute("aria-expanded").Should().Be("false");
        collapsedHeader.GetAttribute("aria-controls").Should().BeNull("a collapsed thread header must not reference a detail region");
        collapsed.FindAll("[data-testid='pdf-annotation-detail']").Should().BeEmpty();

        var toggled = false;
        var clickable = Render(p => p.Add(x => x.OnToggle, EventCallback.Factory.Create(this, () => toggled = true)));
        clickable.Find("[data-testid='pdf-annotation-thread']").Click();
        toggled.Should().BeTrue("clicking the card header toggles thread selection");
    }

    [Fact]
    public void ShowsAuthorTimePageAndReplyCount()
    {
        var cut = Render();

        var header = cut.Find("[data-testid='pdf-annotation-thread']");
        header.TextContent.Should().Contain("Alena");
        header.TextContent.Should().Contain("·", "author and time are joined by an interpunct");
        header.TextContent.Should().Contain("Page 3", "the page reference uses the localized TmPdfThread_PageRef format");
        header.TextContent.Should().Contain("2 replies", "the reply count uses the TmPdfThread_ReplyCount plural family");

        // The pluralized reply count is a single formatted string, not an upper-cased label.
        var replies = cut.Find(".tm-pdf-thread-card__replies");
        replies.TextContent.Should().Contain("2");
    }

    // ── FZE-9: the reply count renders natural plural forms per language ─────

    /// <summary>Renders the card without the shared helper's default ReplyCount/CreatedAt,
    /// so a test can drive those two parameters itself.</summary>
    private IRenderedComponent<TmPdfThreadCard> RenderBare(Action<Bunit.ComponentParameterCollectionBuilder<TmPdfThreadCard>> configure)
        => Render<TmPdfThreadCard>(p =>
        {
            p.Add(x => x.ThreadId, "t1");
            p.Add(x => x.Number, 1);
            p.Add(x => x.PageNumber, 1);
            p.Add(x => x.CreatedAt, DateTimeOffset.UtcNow);
            configure(p);
        });

    [Fact]
    public void ReplyCount_UsesPluralForms_Cs_1_2_5()
    {
        UseCzechLocalization();

        string Count(int replyCount) => RenderBare(p => p.Add(x => x.ReplyCount, replyCount))
            .Find(".tm-pdf-thread-card__replies").TextContent;

        Count(1).Should().Be("1 odpověď", "Czech 1 → One");
        Count(2).Should().Be("2 odpovědi", "Czech 2–4 → Few");
        Count(5).Should().Be("5 odpovědí", "Czech 5+ → Other");
    }

    [Fact]
    public void ReplyCount_ZeroUsesOtherForm()
    {
        RenderBare(p => p.Add(x => x.ReplyCount, 0))
            .Find(".tm-pdf-thread-card__replies").TextContent.Should().Be("0 replies");
    }

    [Fact]
    public void TimeStamp_IsRelativeTimeWithAbsoluteTitle()
    {
        var stamp = RenderBare(p => p.Add(x => x.ReplyCount, 1)).Find(".tm-pdf-thread-card__time");
        stamp.TextContent.Should().Be("Just now",
            "the card header shows the shared relative-time formatter's stamp");
        stamp.GetAttribute("title").Should().NotBeNullOrEmpty("the absolute time is the tooltip");
        stamp.GetAttribute("datetime").Should().NotBeNullOrEmpty("the machine-readable instant rides along");
    }

    [Fact]
    public void ResolvedShowsSuccessChip()
    {
        var resolved = Render(p => p.Add(x => x.Resolved, true));
        var chip = resolved.Find(".tm-pdf-thread-card__chip");
        chip.TextContent.Should().Be("Resolved");
        chip.ClassList.Should().Contain("tm-pdf-thread-card__chip--success",
            "the resolved chip is styled as a success-subtle chip");

        var open = Render(p => p.Add(x => x.Resolved, false));
        open.FindAll(".tm-pdf-thread-card__chip").Should().BeEmpty();
    }

    [Fact]
    public void KindLabel_NotUppercase()
    {
        // The kind label is plain text ("Highlight"), never an uppercased caption — the
        // reference design uses normal casing for the annotation kind.
        var cut = Render(p => p.Add(x => x.KindLabel, "Highlight"));
        cut.Find(".tm-pdf-thread-card__kind").TextContent.Should().Be("Highlight");

        var css = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.PdfViewer", "Components", "Files", "TmPdfThreadCard.razor.css"));
        css.Should().NotContain("text-transform: uppercase",
            "the thread card kind label must render in normal casing");
    }
}
