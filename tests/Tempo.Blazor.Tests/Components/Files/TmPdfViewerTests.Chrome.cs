/// <summary>
/// FZE-11 chrome carry-forwards of <see cref="TmPdfViewer"/>: counter-clockwise rotation,
/// the header file-size subtitle and the search presentation (floating card vs inline bar).
/// Split of TmPdfViewerTests.cs, mirroring the .Continuous/.Responsive/.Touch partials.
/// </summary>
using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Files;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Files;

public partial class TmPdfViewerTests
{
    private const string DemoUrl = "https://example.com/test.pdf";

    // ══════════════════════════════════════════════════════════════════════════
    // Counter-clockwise rotation (FZE-11)
    // ══════════════════════════════════════════════════════════════════════════

    // ── FZE-11: RotateCounterClockwiseAsync from 0 yields 270 and re-renders ──

    [Fact]
    public async Task RotateCounterClockwise_FromZero_Yields270_AndRerendersPage()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, DemoUrl)
            .Add(c => c.AllowRotation, true));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));

        await cut.Instance.RotateCounterClockwiseAsync();

        cut.Instance.Rotation.Should().Be(270);
        // The single-page canvas must actually re-render at the new rotation.
        cut.WaitForAssertion(() =>
            InvocationsOf(this, "tmPdfViewer.renderPage").Last().Arguments[3].Should().Be(270));
    }

    // ── FZE-11: CCW then CW returns to 0 (the two directions are inverse) ────

    [Fact]
    public async Task RotateCounterClockwise_ThenClockwise_ReturnsToZero()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, DemoUrl)
            .Add(c => c.AllowRotation, true));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));

        await cut.Instance.RotateCounterClockwiseAsync();
        cut.Instance.Rotation.Should().Be(270);

        await cut.Instance.RotateAsync();
        cut.Instance.Rotation.Should().Be(0);
    }

    // ── FZE-11: the rotate group carries both directions with localized labels ──

    [Fact]
    public async Task RotateGroup_AllowRotation_RendersBothDirections_WithLocalizedTooltips()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, DemoUrl)
            .Add(c => c.ShowToolbar, true)
            .Add(c => c.AllowRotation, true));
        // The toolbar groups render only once the document is loaded.
        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        var group = cut.Find("[data-tm-group='rotate']");
        var buttons = group.QuerySelectorAll("button").ToList();
        buttons.Should().HaveCount(2, "AllowRotation renders one button per direction");

        var titles = buttons.Select(b => b.GetAttribute("title")).ToList();
        titles.Should().Contain(t => t == "Rotate counter-clockwise");
        titles.Should().Contain(t => t == "Rotate clockwise");

        // Icon-only on desktop/tablet: the localized text rides the tooltip AND the
        // aria-label, so the pair stays on the 1440 toolbar instead of overflowing.
        buttons.Select(b => b.GetAttribute("aria-label")).Should().Contain(t => t == "Rotate counter-clockwise");
        buttons.Should().OnlyContain(b => (b.ClassName ?? string.Empty).Contains("tm-toolbar-btn--icon-only"),
            "the desktop rotate pair is icon-only (LabelPosition Hidden)");
        buttons.Should().OnlyContain(b => !b.QuerySelectorAll(".tm-toolbar-btn-text").Any(),
            "no visible label span renders for the icon-only pair");
    }

    // ── FZE-11: on mobile both directions live in the "More" overflow menu ───

    [Fact]
    public void RotateGroup_Mobile_BothDirectionsAreOverflowOnly()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, p => p
            .Add(c => c.AllowRotation, true));

        // The compact row itself has no room for the rotate commands: they may only
        // surface through the overflow ("More") menu.
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        cut.Find("button.tm-toolbar-more").Click();

        var labels = cut.FindAll("[role='menu'] [role='menuitem']")
                        .Select(i => i.TextContent.Trim()).ToList();
        labels.Should().Contain(l => l.Contains("Rotate counter-clockwise"));
        labels.Should().Contain(l => l.Contains("Rotate clockwise"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Header file size (FZE-11)
    // ══════════════════════════════════════════════════════════════════════════

    // ── FZE-11: ShowFileSize appends the formatted size to the default subtitle ──

    [Fact]
    public async Task Header_ShowFileSize_AppendsFormattedSizeToDefaultSubtitle()
    {
        // The header formats through CurrentCulture (the request culture in production);
        // pin it so the expected digits are stable on any machine locale.
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var cut = Render<TmPdfViewer>(p => p
                .Add(c => c.Url, DemoUrl)
                .Add(c => c.ShowHeader, true));

            await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
            await cut.InvokeAsync(() => cut.Instance.OnDocumentLengthKnown(1_500_000));
            cut.Render();

            var subtitle = cut.Find(".tm-pdf-viewer__header-subtitle").TextContent;
            subtitle.Should().Contain("Pages: 10");
            subtitle.Should().Contain("1.4 MB");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── FZE-11: an explicit host FileSize wins over the JS-reported length ────

    [Fact]
    public async Task Header_ExplicitFileSize_WinsOverJsReportedLength()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var cut = Render<TmPdfViewer>(p => p
                .Add(c => c.Url, DemoUrl)
                .Add(c => c.ShowHeader, true)
                .Add(c => c.FileSize, 2048L));

            await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
            // The JS report arrives too — the host value must still win.
            await cut.InvokeAsync(() => cut.Instance.OnDocumentLengthKnown(1_500_000));
            cut.Render();

            var subtitle = cut.Find(".tm-pdf-viewer__header-subtitle").TextContent;
            subtitle.Should().Contain("2.0 KB");
            subtitle.Should().NotContain("1.4 MB");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    // ── FZE-11: an unknown length keeps the pages-only subtitle ──────────────

    [Fact]
    public async Task Header_FileSizeUnknown_KeepsPagesOnlySubtitle()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, DemoUrl)
            .Add(c => c.ShowHeader, true));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        cut.Render();

        cut.Find(".tm-pdf-viewer__header-subtitle").TextContent
            .Should().Be("Pages: 10");
    }

    // ── FZE-11: an explicit Subtitle is never augmented with the size ─────────

    [Fact]
    public async Task Header_ExplicitSubtitle_IsNotAugmented()
    {
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, DemoUrl)
            .Add(c => c.ShowHeader, true)
            .Add(c => c.Subtitle, "Q3 draft"));

        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(10));
        await cut.InvokeAsync(() => cut.Instance.OnDocumentLengthKnown(1_500_000));
        cut.Render();

        cut.Find(".tm-pdf-viewer__header-subtitle").TextContent
            .Should().Be("Q3 draft");
    }

    // ── FZE-11: the formatter speaks the culture and the localized units ──────

    [Theory]
    [InlineData(500, "en-US", "500 B")]
    [InlineData(1024, "en-US", "1.0 KB")]
    [InlineData(1_500_000, "en-US", "1.4 MB")]
    [InlineData(1_500_000, "cs-CZ", "1,4 MB")]
    [InlineData(3_500_000_000, "en-US", "3.3 GB")]
    public void FileSizeFormatter_UsesCultureAndLocalizedUnits(long bytes, string culture, string expected)
    {
        var formatted = PdfFileSizeFormatter.Format(bytes, BuildEnglishLocalizer(),
            new CultureInfo(culture));

        formatted.Should().Be(expected);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Search presentation (FZE-11)
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>Drives the search into the "everything visible" state (query + matches).</summary>
    private async Task<IRenderedComponent<TmPdfViewer>> RenderLoadedSearchAsync(
        Action<ComponentParameterCollectionBuilder<TmPdfViewer>>? extra = null)
    {
        var cut = Render<TmPdfViewer>((ComponentParameterCollectionBuilder<TmPdfViewer> p) =>
        {
            p.Add(c => c.Url, DemoUrl)
             .Add(c => c.ShowSearch, true);
            extra?.Invoke(p);
        });
        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        // The search input binds through oninput (plus the key handlers), so it feeds via .Input.
        cut.Find("[data-testid='pdf-search-input']").Input("doc");
        await cut.InvokeAsync(() => cut.Instance.OnSearchResults(5, [1, 2, 3, 4, 5]));
        cut.Render();
        return cut;
    }

    // ── FZE-11: the default Floating presentation renders a card over the body ──

    [Fact]
    public async Task SearchPresentation_Floating_RendersCardOverBody_NotAboveToolbar()
    {
        var cut = await RenderLoadedSearchAsync();

        var card = cut.Find("[data-testid='pdf-search-card']");
        card.GetAttribute("role").Should().Be("search");

        // The card is a sibling of the body — never inside the scroll area and never
        // between the header and the toolbar (which would push the chrome down).
        card.ParentElement.ClassName.Should().Contain("tm-pdf-viewer__body",
            "the card is absolutely anchored over the body, outside the scroll wrap");
        cut.FindAll(".tm-pdf-viewer__search-bar").Should().BeEmpty();

        // DOM order: the toolbar renders before the card's host body — a renderer must
        // never paint the card above the toolbar.
        var root = cut.Find(".tm-pdf-viewer");
        var childClasses = root.Children.Select(c => c.ClassName ?? string.Empty).ToList();
        var toolbarIndex = childClasses.FindIndex(c => c.Contains("tm-pdf-viewer__toolbar"));
        var bodyIndex = childClasses.FindIndex(c => c.Contains("tm-pdf-viewer__body"));
        (toolbarIndex, bodyIndex).Should().Match<(int, int)>(t => t.Item1 >= 0 && t.Item2 > t.Item1,
            "the toolbar renders before the body hosting the floating card");
    }

    // ── FZE-11: Escape closes the card and restores focus to the search toggle ──

    [Fact]
    public async Task SearchCard_Escape_ClosesAndRestoresFocusToSearchToggle()
    {
        var cut = await RenderLoadedSearchAsync();

        await cut.InvokeAsync(async () =>
        {
            var input = cut.Find("[data-testid='pdf-search-input']");
            await input.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
        });
        cut.Render();

        cut.FindAll("[data-testid='pdf-search-card']").Should().BeEmpty("Escape closes the card");
        cut.WaitForAssertion(() =>
            JSInterop.Invocations["tmPdfViewer.focusSearchToggle"].Should().NotBeEmpty(
                "the search toggle regains focus so keyboard users keep their place"));
    }

    // ── FZE-11: Enter walks forward, Shift+Enter walks backward ──────────────

    [Fact]
    public async Task SearchCard_Enter_NextMatch_ShiftEnter_PreviousMatch()
    {
        var cut = await RenderLoadedSearchAsync();

        await cut.InvokeAsync(() =>
        {
            var input = cut.Find("[data-testid='pdf-search-input']");
            // The first Enter performs the search (the query changed since the last one)…
            input.KeyUp(new KeyboardEventArgs { Key = "Enter" });
            // …the second walks forward.
            input.KeyUp(new KeyboardEventArgs { Key = "Enter" });
        });
        InvocationsOf(this, "tmPdfViewer.searchNext").Should().NotBeEmpty();

        await cut.InvokeAsync(() =>
        {
            var input = cut.Find("[data-testid='pdf-search-input']");
            input.KeyUp(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });
        });
        InvocationsOf(this, "tmPdfViewer.searchPrev").Should().NotBeEmpty();
    }

    // ── FZE-11: the card keeps the stable pdf-search-* test ids ──────────────

    [Fact]
    public async Task SearchCard_KeepsPdfSearchTestIds()
    {
        var cut = await RenderLoadedSearchAsync();

        cut.Find("[data-testid='pdf-search-input']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-count']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-prev']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-next']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-clear']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-close']").Should().NotBeNull();
    }

    // ── FZE-11: Inline renders today's bar, never the card ────────────────────

    [Fact]
    public async Task SearchPresentation_Inline_IsMarkupIdenticalToToday()
    {
        var cut = await RenderLoadedSearchAsync(p => p
            .Add(c => c.SearchPresentation, PdfSearchPresentation.Inline));

        cut.Find(".tm-pdf-viewer__search-bar").Should().NotBeNull();
        cut.FindAll("[data-testid='pdf-search-card']").Should().BeEmpty();
        // The stable ids live in the bar exactly as today.
        cut.Find("[data-testid='pdf-search-input']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-count']").Should().NotBeNull();
    }

    // ── FZE-11: a mobile layout keeps the sheet-style bar in both presentations ──

    [Fact]
    public void SearchPresentation_Mobile_StaysBottomSheet()
    {
        var cut = RenderInLayout(TmLayoutMode.Mobile, p => p
            .Add(c => c.ShowSearch, true));

        cut.Find(".tm-pdf-viewer__search-bar").Should().NotBeNull();
        cut.FindAll("[data-testid='pdf-search-card']").Should().BeEmpty(
            "mobile never floats the card — it stays the in-flow sheet");

        var cutInline = RenderInLayout(TmLayoutMode.Mobile, p => p
            .Add(c => c.ShowSearch, true)
            .Add(c => c.SearchPresentation, PdfSearchPresentation.Inline));
        cutInline.Find(".tm-pdf-viewer__search-bar").Should().NotBeNull();
    }

    // ── Review: Escape works from anywhere in the card (handler on the card div) ──

    [Fact]
    public async Task SearchCard_Escape_FromPrevNextClearClose_ClosesCard()
    {
        var cut = await RenderLoadedSearchAsync();

        // The handler lives on the card div: a keydown on any child bubbles to it and
        // closes the card (the focus restore follows the shared CloseSearchCardAsync).
        foreach (var testId in new[] { "pdf-search-next", "pdf-search-prev", "pdf-search-clear", "pdf-search-close" })
        {
            var button = cut.Find($"[data-testid='{testId}']");
            await cut.InvokeAsync(() => button.KeyDown(new KeyboardEventArgs { Key = "Escape" }));
            cut.Render();
            cut.FindAll("[data-testid='pdf-search-card']").Should().BeEmpty($"Escape on {testId} closes the card");

            // Reopen for the next iteration (the query survives a close, so only the
            // card is re-rendered; the matches state stays until a new search runs).
            await cut.InvokeAsync(() =>
            {
                var toggle = cut.Find("[data-testid='pdf-search-toggle']");
                toggle.Click();
            });
            cut.Render();
            cut.FindAll("[data-testid='pdf-search-card']").Should().HaveCount(1, $"{testId} loop reopens the card");
        }
    }

    // ── Review: a zero-match search renders the verdict and flags the input ──

    [Fact]
    public async Task SearchCard_NoMatches_RendersVerdictInCountSlot_AndAriaInvalid()
    {
        var cut = await RenderLoadedSearchAsync();

        await cut.InvokeAsync(() => cut.Instance.OnSearchResults(0, []));
        cut.Render();

        var count = cut.Find("[data-testid='pdf-search-count']");
        count.TextContent.Should().Be("No matches");
        cut.Find("[data-testid='pdf-search-input']").GetAttribute("aria-invalid").Should().Be("true");
        // The live region wrapper is present even before any search ran.
        cut.Find(".tm-pdf-viewer__search-card [role='status']").Should().NotBeNull();

        // Clearing the query (the ⊗ button) resets the verdict and the flag.
        await cut.InvokeAsync(() => cut.Find("[data-testid='pdf-search-clear']").Click());
        cut.Render();
        cut.FindAll("[data-testid='pdf-search-count']").Should().BeEmpty();
        cut.Find("[data-testid='pdf-search-input']").GetAttribute("aria-invalid").Should().BeNull(
            "a cleared query clears the no-match verdict");
    }

    [Fact]
    public async Task SearchBar_Inline_NoMatches_RendersVerdictInCountSlot()
    {
        var cut = await RenderLoadedSearchAsync(p => p
            .Add(c => c.SearchPresentation, PdfSearchPresentation.Inline));

        await cut.InvokeAsync(() => cut.Instance.OnSearchResults(0, []));
        cut.Render();

        cut.Find("[data-testid='pdf-search-count']").TextContent.Should().Be("No matches");
        cut.Find("[data-testid='pdf-search-input']").GetAttribute("aria-invalid").Should().Be("true");
    }

    // ── Review: opening the card moves focus into the input (best-effort in bUnit) ──

    [Fact]
    public async Task SearchCard_Open_FocusMoveIsBestEffort_NoThrowInBUnit()
    {
        // bUnit has no live element behind the ElementReference — FocusAsync throws and
        // the viewer must swallow it (the live-browser proof is the search-card E2E).
        var cut = Render<TmPdfViewer>(p => p
            .Add(c => c.Url, DemoUrl)
            .Add(c => c.ShowSearch, true));
        await cut.InvokeAsync(() => cut.Instance.OnPdfLoaded(5));
        cut.Render();

        // ShowSearch starts the card open — close it, then reopen via the toolbar toggle:
        // the pending-focus pass runs in OnAfterRenderAsync after the fresh card renders.
        await cut.InvokeAsync(() => cut.Find("[data-testid='pdf-search-close']").Click());
        cut.FindAll("[data-testid='pdf-search-card']").Should().BeEmpty();

        await cut.InvokeAsync(() => cut.Find("[data-testid='pdf-search-toggle']").Click());

        cut.Find("[data-testid='pdf-search-card']").Should().NotBeNull();
        cut.Find("[data-testid='pdf-search-input']").Should().NotBeNull();
    }
}
