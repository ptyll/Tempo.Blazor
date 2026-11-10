using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 7 — the two surfaces whose narrow-container behaviour changed: the conflict radar's card
/// presentation (a phone gets a card per file instead of a file × agent table scrolled sideways) and the presence
/// overlay's measured label placement (a cursor label never covers the sentence it points at).
/// <para>
/// The specs read GEOMETRY, not classes (WASM-only: a phone layout is the design reference, not a lane contract — the
/// keyboard and pointer walks every lane must prove live in <see cref="AiAgentVisualizationsE2EBase"/>): the cards are stacked (an agent's change sits below its file), nothing
/// overflows the surface or the page at 375 px, a mirrored/stacked label stays inside the content box and clear of the
/// anchored paragraph's own text lines, and a folded directory survives the section's round trip through its table
/// presentation because the page — not the component — owns that state. The live presence cursors are driven until the
/// anchors move while the page's own run stays paused.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class AiAgentVisualizationsPhoneE2ETests : AiAgentVisualizationsPageBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";

    /// <summary>The distinct paths the three demo branches touch — one card per path.</summary>
    private const int DemoFileCount = 25;

    private const string Radar = "[data-testid='ai-viz-conflict']";
    private const string Overlay = "[data-testid='ai-viz-presence']";

    private static async Task WaitForAsync(IPage page, string script, int timeoutMs = 20000)
        => await page.WaitForFunctionAsync(script, new PageWaitForFunctionOptions { Timeout = timeoutMs });

    // ── the phone presentation of the conflict grid ──────────────────────────

    [TestMethod]
    public async Task ThePhoneDrawsACardPerFile_InsteadOfAScrollingTable()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToTopAsync(page, ConflictSection);

        await WaitForAsync(page, $"() => document.querySelector(\"{Radar}\")?.dataset.presentation === 'cards'");

        Assert.AreEqual(0, await page.Locator($"{Radar} [data-testid='conflict-radar-grid']").CountAsync(),
            "a file × agent table on a 375 px phone is the defect this presentation removes");
        Assert.AreEqual(DemoFileCount, await page.Locator($"{Radar} [data-testid='radar-card']").CountAsync());

        // The cards are UNDER the directory headers and the agent changes are UNDER their file — in real geometry.
        var first = page.Locator($"{Radar} [data-testid='radar-card']").First;
        var fileBox = await first.Locator(".tm-ai-radar__file-name").First.BoundingBoxAsync();
        var changeBox = await first.Locator("[data-testid='radar-card-change']").First.BoundingBoxAsync();
        Assert.IsNotNull(fileBox, "the file name is laid out");
        Assert.IsNotNull(changeBox, "the agent's change is laid out");
        Assert.IsTrue(changeBox.Y >= fileBox.Y + fileBox.Height - 1,
            $"an agent's change is stacked below its file (file bottom {fileBox.Y + fileBox.Height}, " +
            $"change top {changeBox.Y})");
        Assert.IsTrue(changeBox.X >= fileBox.X - 1, "and it starts at the same reading edge");

        // Nothing overflows: not the surface, not the section, not the page.
        var overflow = await page.Locator(Radar).EvaluateAsync<double>(
            "element => element.scrollWidth - element.clientWidth");
        Assert.IsTrue(overflow <= 1, $"the card list does not scroll sideways (overflow {overflow} px)");

        var sectionBox = await page.Locator(ConflictSection).BoundingBoxAsync();
        Assert.IsNotNull(sectionBox);
        var sectionContent = await page.Locator(ConflictSection).EvaluateAsync<double>("element => element.scrollWidth");
        Assert.IsTrue(sectionContent <= sectionBox.Width + 1,
            $"the section keeps its own content inside its box ({sectionContent} vs {sectionBox.Width})");

        var pageWidth = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(pageWidth <= 376, $"the page itself never scrolls sideways at 375 px (width {pageWidth})");

        // A conflicting card keeps its side choice and its word, so the phone can still settle the merge.
        var conflicting = page.Locator($"{Radar} [data-testid='radar-card'][data-conflict='true']");
        Assert.AreEqual(4, await conflicting.CountAsync());
        Assert.AreEqual(3, await conflicting.First.Locator("[role='radiogroup'] input[type='radio']").CountAsync());
        StringAssert.Contains(await conflicting.First.InnerTextAsync(), "Conflict");

        await ShootAsync(page, "ai-viz-conflict-cards-phone-375");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-conflict-section");
    }

    [TestMethod]
    public async Task TheDesktopKeepsTheTable_WithItsHonestCountsAndAScrollHint()
    {
        var page = await OpenAsync();
        await ScrollToTopAsync(page, ConflictSection);

        await WaitForAsync(page, $"() => document.querySelector(\"{Radar}\")?.dataset.presentation === 'grid'");

        var table = page.Locator($"{Radar} [data-testid='conflict-radar-grid']");
        Assert.AreEqual(1, await table.CountAsync());
        Assert.AreEqual(0, await page.Locator($"{Radar} [data-testid='radar-card']").CountAsync(),
            "the two presentations are alternatives, never duplicates");

        var rowCount = int.Parse((await table.GetAttributeAsync("aria-rowcount")) ?? "0");
        var columnCount = int.Parse((await table.GetAttributeAsync("aria-colcount")) ?? "0");
        Assert.AreEqual(4, columnCount, "the file column plus one column per agent");
        Assert.AreEqual(await table.Locator("tr").CountAsync(), rowCount,
            "the declared row count is the number of rows actually rendered");

        // The scroller carries a paint-only shadow so a reader knows a column is hidden when the table is narrow.
        var background = await page.Locator($"{Radar} [data-testid='radar-grid-wrap']")
            .EvaluateAsync<string>("element => getComputedStyle(element).backgroundImage");
        StringAssert.Contains(background, "radial-gradient",
            "the widened grid hints at the hidden column instead of clipping it silently");

        await ShootAsync(page, "ai-viz-conflict-grid-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-conflict-section");
    }

    [TestMethod]
    public async Task AFoldedDirectorySurvivesTheSectionsRoundTripThroughItsTable()
    {
        var page = await OpenAsync();
        await ScrollToTopAsync(page, ConflictSection);
        await WaitForAsync(page, $"() => document.querySelector(\"{Radar}\")?.dataset.presentation === 'grid'");

        var rowsBefore = await page.Locator($"{Radar} [data-testid='radar-row']").CountAsync();
        var group = page.Locator($"{Radar} [data-testid='radar-directory'][data-directory='docs']");
        await group.Locator("[data-testid='radar-directory-toggle']").ClickAsync();
        await WaitForAsync(page,
            $"() => document.querySelector(\"{Radar} [data-directory='docs']\")?.dataset.collapsed === 'true'");

        var rowsFolded = await page.Locator($"{Radar} [data-testid='radar-row']").CountAsync();
        Assert.IsTrue(rowsFolded < rowsBefore, "folding a directory hides its file rows");

        // The section's table presentation UNMOUNTS the radar; the page owns the fold, so coming back restores it.
        await page.Locator("[data-testid='ai-viz-conflict-view']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='ai-viz-conflict-table']");
        await page.Locator("[data-testid='ai-viz-conflict-view']").ClickAsync();
        await page.WaitForSelectorAsync($"{Radar} [data-testid='conflict-radar-grid']");

        Assert.AreEqual("true",
            await page.Locator($"{Radar} [data-directory='docs']").First.GetAttributeAsync("data-collapsed"),
            "a fold a host persists survives the component being re-created");
        Assert.AreEqual(rowsFolded, await page.Locator($"{Radar} [data-testid='radar-row']").CountAsync());

        await ShootAsync(page, "ai-viz-conflict-folded-1440");
    }

    [TestMethod]
    public async Task ThePhoneShootsTheFoldInCardsAndInDark()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToTopAsync(page, ConflictSection);
        await WaitForAsync(page, $"() => document.querySelector(\"{Radar}\")?.dataset.presentation === 'cards'");

        await page.Locator($"{Radar} [data-directory='docs'] [data-testid='radar-directory-toggle']").First.ClickAsync();
        await WaitForAsync(page,
            $"() => document.querySelectorAll(\"{Radar} [data-testid='radar-card-directory'][data-collapsed='true']\").length >= 1");
        await ShootAsync(page, "ai-viz-conflict-cards-folded-phone-375");

        await ToggleThemeAsync(page);
        await ScrollToTopAsync(page, ConflictSection);
        await ShootAsync(page, "ai-viz-conflict-cards-folded-phone-375-dark");
    }

    // ── the presence label placement ─────────────────────────────────────────

    /// <summary>
    /// Measures every cursor's label against the paragraph it is anchored to: the label must stay inside the content
    /// box and must not cover a single TEXT LINE of the paragraph it points at (the mirrored label used to).
    /// </summary>
    private static async Task<LabelProbe[]> ProbeLabelsAsync(IPage page)
    {
        await WaitForAsync(page,
            $"() => document.querySelectorAll(\"{Overlay} [data-presence-marker][data-presence-placed='true']\").length >= 4");

        return await page.Locator(Overlay).EvaluateAsync<LabelProbe[]>(
            """
            (overlay) => {
                const content = overlay.querySelector('.tm-ai-presence__content');
                const contentRect = content.getBoundingClientRect();
                return [...overlay.querySelectorAll('[data-presence-marker]')].map(marker => {
                    const anchorId = marker.getAttribute('data-presence-anchor');
                    const anchor = anchorId ? content.querySelector(`[data-presence-anchor="${anchorId}"]`) : null;
                    const label = marker.querySelector('.tm-ai-presence__label').getBoundingClientRect();
                    const lines = [];
                    const paragraph = anchor ? anchor.parentElement : null;
                    if (paragraph) {
                        const range = document.createRange();
                        range.selectNodeContents(paragraph);
                        for (const rect of range.getClientRects()) {
                            lines.push({ left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom });
                        }
                    }

                    const overlaps = lines.filter(line =>
                        label.left < line.right - 1 && label.right > line.left + 1 &&
                        label.top < line.bottom - 1 && label.bottom > line.top + 1).length;

                    return {
                        anchorId,
                        place: marker.getAttribute('data-presence-place'),
                        stack: marker.getAttribute('data-presence-stack'),
                        labelLeft: label.left,
                        labelRight: label.right,
                        labelTop: label.top,
                        contentLeft: contentRect.left,
                        contentRight: contentRect.right,
                        overlappingLines: overlaps,
                        lines: lines.length,
                    };
                });
            }
            """);
    }

    [TestMethod]
    public async Task ThePhoneLabelsStayInsideTheContent_AndOffTheTextTheyPointAt()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToAsync(page, PresenceSection);

        var labels = await ProbeLabelsAsync(page);
        Assert.AreEqual(4, labels.Length, "one cursor per agent of the run");

        foreach (var label in labels)
        {
            Assert.IsTrue(label.AnchorId is not null, "every cursor names the anchor it sits on");
            Assert.IsTrue(label.Lines > 0, $"the anchor {label.AnchorId} is inside a paragraph with text");
            Assert.AreEqual(0, label.OverlappingLines,
                $"the label of {label.AnchorId} (place={label.Place ?? "end"}, stack={label.Stack ?? "none"}) covers "
                + "text of the very paragraph it points at");
            Assert.IsTrue(label.LabelLeft >= label.ContentLeft - 1,
                $"the label of {label.AnchorId} starts inside the content box ({label.LabelLeft} vs {label.ContentLeft})");
            Assert.IsTrue(label.LabelRight <= label.ContentRight + 1,
                $"the label of {label.AnchorId} ends inside the content box ({label.LabelRight} vs {label.ContentRight})");
        }

        // The demo document justifies ONE paragraph's last line, so its anchor sits at the content's inline end: the
        // label has no room beside it and is STACKED under the line instead of mirrored over the words — the placement
        // this phase introduced. The other paragraphs keep the beside placement, so one screenshot shows both.
        Assert.IsTrue(labels.Any(label => label.Stack is "below" or "above"),
            "the justification edge case must produce a stacked label: "
            + string.Join(", ", labels.Select(label => $"{label.AnchorId}:{label.Place ?? "end"}/{label.Stack ?? "none"}")));
        Assert.IsTrue(labels.Any(label => label.Stack is null),
            "and the ordinary paragraphs keep the label beside their point: "
            + string.Join(", ", labels.Select(label => $"{label.AnchorId}:{label.Place ?? "end"}/{label.Stack ?? "none"}")));

        // A stacked label starts BELOW the anchored line box, which is the measurement that keeps it off the text.
        var stacked = await page.Locator(Overlay).EvaluateAsync<bool>(
            """
            (overlay) => {
                const content = overlay.querySelector('.tm-ai-presence__content');
                return [...overlay.querySelectorAll('[data-presence-marker]')]
                    .filter(marker => marker.getAttribute('data-presence-stack'))
                    .every(marker => {
                        const anchorId = marker.getAttribute('data-presence-anchor');
                        const anchor = anchorId ? content.querySelector(`[data-presence-anchor="${anchorId}"]`) : null;
                        if (!anchor) return true;
                        const line = anchor.getBoundingClientRect();
                        const label = marker.querySelector('.tm-ai-presence__label').getBoundingClientRect();
                        return marker.getAttribute('data-presence-stack') === 'above'
                            ? label.bottom <= line.top + 1
                            : label.top >= line.bottom - 1;
                    });
            }
            """);
        Assert.IsTrue(stacked, "every stacked label clears the anchored line box");

        await ShootAsync(page, "ai-viz-presence-labels-phone-375");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-viz-presence-section");
    }

    [TestMethod]
    public async Task TheDesktopLabelsStayInsideTheContent_AndOffTheText()
    {
        var page = await OpenAsync(1440, 1100);
        await ScrollToAsync(page, PresenceSection);

        var labels = await ProbeLabelsAsync(page);
        foreach (var label in labels)
        {
            Assert.AreEqual(0, label.OverlappingLines,
                $"no label covers the text of its paragraph at 1440 px ({label.AnchorId})");
            Assert.IsTrue(label.LabelRight <= label.ContentRight + 1,
                $"no label widens the page at 1440 px ({label.AnchorId})");
        }

        await ShootAsync(page, "ai-viz-presence-labels-1440");
    }

    // ── the live cursors ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task TheLiveCursorsMoveWhileThePagesRunStaysPaused()
    {
        var page = await OpenAsync();
        await ScrollToAsync(page, PresenceSection);

        async Task<string?[]> AnchorsAsync()
            => await page.Locator($"{Overlay} [data-presence-marker]")
                .EvaluateAllAsync<string?[]>(
                    "markers => markers.map(marker => marker.getAttribute('data-presence-anchor'))");

        var before = await AnchorsAsync();
        Assert.AreEqual("Paused", await TextAsync(page, "ai-viz-state"), "the section never starts the page's run");

        var seeded = System.Text.Json.JsonSerializer.Serialize(string.Join("|", before));
        await page.Locator("[data-testid='ai-viz-presence-play']").ClickAsync();
        await WaitForAsync(page,
            $"() => [...document.querySelectorAll(\"{Overlay} [data-presence-marker]\")]"
            + ".map(marker => marker.getAttribute('data-presence-anchor')).join('|')"
            + $" !== {seeded}");

        Assert.AreEqual("Paused", await TextAsync(page, "ai-viz-state"),
            "the live cursors are the section's own stream, not the page's run");
        Assert.AreEqual("0", await TextAsync(page, "ai-viz-events"),
            "a moving cursor never advances the page's counters");

        await page.Locator("[data-testid='ai-viz-presence-play']").ClickAsync();
        var paused = await AnchorsAsync();
        await page.WaitForTimeoutAsync(800);
        CollectionAssert.AreEqual(paused, await AnchorsAsync(), "a paused stream applies nothing");

        await ShootAsync(page, "ai-viz-presence-live-1440");
        await ToggleThemeAsync(page);
        await ScrollToAsync(page, PresenceSection);
        await ShootAsync(page, "ai-viz-presence-live-1440-dark");
    }

    /// <summary>One cursor's measured label, as the browser sees it.</summary>
    public sealed class LabelProbe
    {
        public string? AnchorId { get; set; }

        public string? Place { get; set; }

        public string? Stack { get; set; }

        public double LabelLeft { get; set; }

        public double LabelRight { get; set; }

        public double LabelTop { get; set; }

        public double ContentLeft { get; set; }

        public double ContentRight { get; set; }

        public int OverlappingLines { get; set; }

        public int Lines { get; set; }
    }
}
