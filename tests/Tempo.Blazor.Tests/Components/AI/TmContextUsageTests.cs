using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 5 — <c>TmContextUsage</c> (Tempo.Blazor.AI): what a context window is made of. The
/// snapshot's sections become a stacked bar (or a 100-cell grid, or a treemap), the total is a
/// <c>role="meter"</c> carrying the localized value, and the legend is the table a reader (and a screen reader)
/// actually consumes: label, tokens, share, the Required / Summarized / Omitted words and — for a removable
/// section or item — the Remove action. Nothing in the component is colour-only, the charts are decorative, and
/// the ONE polite live region speaks only when the context crosses its budget.
/// </summary>
public class TmContextUsageTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private ContextUsageRemoval? _removal;
    private int _compactCalls;

    private int FocusCalls() => JSInterop.Invocations.Count(invocation => invocation.Identifier == FocusInvocation);

    /// <summary>The element references the focus calls targeted, in call order (a null id is a detached target).</summary>
    private IReadOnlyList<string?> FocusTargets() => JSInterop.Invocations
        .Where(invocation => invocation.Identifier == FocusInvocation)
        .Select(invocation => ((ElementReference)invocation.Arguments[0]!).Id)
        .ToList();

    private static ContextUsageItem Item(string id, int tokens, bool canRemove = false)
        => new(id, id) { Tokens = tokens, CanRemove = canRemove };

    /// <summary>
    /// Six sections covering every kind the plan names, all three state words and both removal shapes:
    /// 14 000 of a 16 000-token budget.
    /// </summary>
    private static ContextUsageSection[] Sections() =>
    [
        new("system", "System prompt") { Kind = ContextUsageKind.System, Tokens = 1_200, IsRequired = true },
        new("tools", "Tools")
        {
            Kind = ContextUsageKind.Tools,
            Tokens = 800,
            CanRemove = true,
            Items = [Item("search", 500, canRemove: true), Item("code", 300, canRemove: true)],
        },
        new("memory", "Memory") { Kind = ContextUsageKind.Memory, Tokens = 400, IsSummarized = true },
        new("files", "Files")
        {
            Kind = ContextUsageKind.Files,
            Tokens = 5_000,
            CanRemove = true,
            Items = [Item("contract.pdf", 4_000, canRemove: true), Item("report.pdf", 1_000)],
        },
        new("chat", "Chat history") { Kind = ContextUsageKind.Chat, Tokens = 6_000, Items = [Item("m-1", 6_000)] },
        new("results", "Tool results") { Kind = ContextUsageKind.Results, Tokens = 600, IsOmitted = true },
    ];

    private static ContextUsageSnapshot Snapshot() => new()
    {
        BudgetTokens = 16_000,
        Sections = Sections(),
        CacheHit = 0.62,
    };

    private IRenderedComponent<TmContextUsage> RenderUsage(
        Action<ComponentParameterCollectionBuilder<TmContextUsage>>? configure = null)
        => Render<TmContextUsage>(parameters =>
        {
            parameters.Add(component => component.Snapshot, Snapshot());
            parameters.Add(component => component.OnRemoveItem, removal => _removal = removal);
            parameters.Add(component => component.OnCompact, () => _compactCalls++);
            configure?.Invoke(parameters);
        });

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<TmContextUsage> cut)
        => cut.FindAll("table.tm-context-usage__table tbody tr.tm-context-usage__row").ToList();

    private static IElement Row(IRenderedComponent<TmContextUsage> cut, string key)
        => cut.Find($"tr.tm-context-usage__row[data-section-key='{key}']");

    private static IReadOnlyList<IElement> Segments(IRenderedComponent<TmContextUsage> cut)
        => cut.FindAll(".tm-context-usage__bar .tm-context-usage__segment").ToList();

    private static string InlineSize(IElement element)
    {
        var style = element.GetAttribute("style") ?? string.Empty;
        var match = System.Text.RegularExpressions.Regex.Match(style, @"inline-size:\s*([0-9.,]+)%");
        match.Success.Should().BeTrue($"the segment carries its width in the inline style: \"{style}\"");
        return match.Groups[1].Value;
    }

    // ── The stacked bar ──────────────────────────────────────────────────────

    [Fact]
    public void TheStackedBar_PaintsOneSegmentPerSection_OverTheBudget()
    {
        var cut = RenderUsage();

        var segments = Segments(cut);
        segments.Should().HaveCount(6);
        segments.Select(segment => segment.GetAttribute("data-section-key")).Should()
            .Equal("system", "tools", "memory", "files", "chat", "results");

        // 1 200 / 800 / 400 / 5 000 / 6 000 / 600 tokens of a 16 000-token BUDGET, so the bar's tail is the
        // 12 % of headroom that is still free (the same fact the grid paints).
        segments.Select(InlineSize).Should().Equal(
            "7.5", "5", "2.5", "31.25", "37.5", "3.75");
        segments.Select(segment => segment.GetAttribute("data-share")).Should()
            .Equal("8", "6", "3", "36", "43", "4");
    }

    [Fact]
    public void TheBarIsDecorative_BecauseTheTableCarriesEveryFact()
    {
        var cut = RenderUsage();

        cut.Find(".tm-context-usage__bar").GetAttribute("aria-hidden").Should().Be("true");
        Segments(cut).Select(segment => segment.GetAttribute("title")).Should().OnlyContain(title => title == null,
            "a title attribute is a mouse-only hint and the table already says it in words");
    }

    [Fact]
    public void TheSegmentToneFollowsTheSectionKind_ThroughItsOwnToken()
    {
        var cut = RenderUsage();

        foreach (var key in new[] { "system", "tools", "memory", "files", "chat", "results" })
        {
            Row(cut, key).QuerySelector(".tm-context-usage__swatch")!.ClassList
                .Should().Contain($"tm-context-usage__swatch--{key}");
        }

        Segments(cut).Select(segment => segment.GetAttribute("data-section-key"))
            .Select(key => key!)
            .Zip(Segments(cut), (key, segment) => segment.ClassList.Should()
                .Contain($"tm-context-usage__segment--{key}", "every segment names the tone of its own kind"))
            .ToList();

        Segments(cut).Select(segment => segment.GetAttribute("data-kind")).Should()
            .Equal("system", "tools", "memory", "files", "chat", "results");
    }

    [Fact]
    public void ASectionWithTokens_ButNoRoundedPercent_StillPaintsAVisibleSliver_AndSaysSoInWords()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot
            {
                BudgetTokens = 100_000,
                Sections =
                [
                    new("chat", "Chat history") { Kind = ContextUsageKind.Chat, Tokens = 99_500 },
                    new("files", "Files") { Kind = ContextUsageKind.Files, Tokens = 40 },
                ],
            }));

        var tiny = Segments(cut).Single(segment => segment.GetAttribute("data-section-key") == "files");
        InlineSize(tiny).Should().Be("0.5", "a token-bearing section is never invisible, however small its share");
        Row(cut, "files").QuerySelector(".tm-context-usage__share")!.TextContent.Trim()
            .Should().MatchRegex(@"^<\s?1\s?%$", "the rounded number is capped at the truth: it is not zero, it is under one percent");
        Row(cut, "chat").QuerySelector(".tm-context-usage__share")!.TextContent.Trim()
            .Should().MatchRegex(@"^100\s?%$");
    }

    [Fact]
    public void AZeroTokenSection_PaintsNoSegment_ButKeepsItsLegendRow()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot
            {
                BudgetTokens = 1_000,
                Sections =
                [
                    new("system", "System prompt") { Tokens = 400 },
                    new("results", "Tool results") { Tokens = 0, IsOmitted = true },
                ],
            }));

        Segments(cut).Should().HaveCount(1);
        Rows(cut).Should().HaveCount(2, "a section that reports nothing is still part of the answer");
        Row(cut, "results").QuerySelector(".tm-context-usage__share")!.TextContent.Trim().Should()
            .MatchRegex(@"^0\s?%$");
    }

    // ── The meter ────────────────────────────────────────────────────────────

    [Fact]
    public void TheTotalIsAMeter_CarryingTheMachineValueAndTheLocalizedText()
    {
        // The grouped-value assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderUsage();

        var meter = cut.Find("[data-testid='context-usage-meter']");
        meter.GetAttribute("role").Should().Be("meter");
        meter.GetAttribute("aria-valuemin").Should().Be("0");
        meter.GetAttribute("aria-valuemax").Should().Be("16000");
        meter.GetAttribute("aria-valuenow").Should().Be("14000");

        var text = meter.GetAttribute("aria-valuetext");
        text.Should().Contain("14,000").And.Contain("16,000").And.Contain("88",
            "the value text names the amount, the budget and the whole percentage");
        text.Should().NotContain("[Tm", "every sentence comes from the localizer");
    }

    [Fact]
    public void TheVisibleReadoutIsSightedOnlyDuplicate_BecauseTheMeterAlreadySpeaksIt()
    {
        // The grouped-value assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderUsage();

        var readout = cut.Find(".tm-context-usage__readout");
        readout.GetAttribute("aria-hidden").Should().Be("true");
        readout.QuerySelector(".tm-context-usage__amount")!.TextContent.Should().Contain("14,000");
        readout.QuerySelector(".tm-context-usage__percent")!.TextContent.Should().Contain("88");
    }

    [Fact]
    public void AnUnknownBudget_LeavesTheMeterUnmeasurable_WithoutDividingByZero()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot
            {
                Sections = [new("system", "System prompt") { Tokens = 400 }],
            }));

        var meter = cut.Find("[data-testid='context-usage-meter']");
        meter.GetAttribute("aria-valuemax").Should().Be("0");
        meter.GetAttribute("aria-valuenow").Should().Be("400");
        cut.Find("[data-testid='context-usage']").GetAttribute("data-over-budget").Should().BeNull(
            "an unknown budget cannot be exceeded, so no warning is claimed");
        cut.Find(".tm-context-usage__amount").TextContent.Trim().Should()
            .Be("400 tokens used", "an unknown budget has no ratio, so the read-out names only the used amount");
    }

    [Fact]
    public void TheTitleNamesTheMeter_AndTheAriaLabelCoversTheUntitledCase()
    {
        var titled = RenderUsage(parameters => parameters.Add(component => component.Title, "Context window"));
        var meter = titled.Find("[data-testid='context-usage-meter']");
        var titleId = titled.Find(".tm-context-usage__title").GetAttribute("id");
        meter.GetAttribute("aria-labelledby").Should().Be(titleId);
        meter.GetAttribute("aria-label").Should().BeNull("one control never carries two names");

        var untitled = RenderUsage(parameters => parameters.Add(component => component.AriaLabel, "Working memory"));
        untitled.Find("[data-testid='context-usage-meter']").GetAttribute("aria-label").Should().Be("Working memory");
    }

    [Fact]
    public void TheRootIsNamedByTheLocalizedDefault_WhenNothingElseNamesIt()
    {
        var cut = RenderUsage();

        cut.Find("[data-testid='context-usage']").GetAttribute("aria-label").Should().Be("Context usage");
    }

    [Theory]
    [InlineData(1, "h2")]
    [InlineData(3, "h3")]
    [InlineData(6, "h6")]
    [InlineData(9, "h6")]
    [InlineData(0, "h2")]
    public void TheTitleHeadingSitsWhereTheHostPutsItInThePageOutline(int level, string expected)
    {
        var cut = RenderUsage(parameters =>
        {
            parameters.Add(component => component.Title, "Context window");
            parameters.Add(component => component.HeadingLevel, level);
        });

        cut.Find(".tm-context-usage__title").TagName.Should().Be(expected.ToUpperInvariant());
    }

    // ── The legend table ─────────────────────────────────────────────────────

    [Fact]
    public void TheLegendListsEverySection_WithItsTokensShareAndStateWords()
    {
        // The grouped-value assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderUsage();

        var table = cut.Find("table.tm-context-usage__table");
        table.QuerySelector("caption")!.ClassList.Should().Contain("tm-sr-only");
        table.QuerySelectorAll("thead th").Select(head => head.TextContent.Trim()).Should()
            .Equal("Section", "Tokens", "Share", "Actions");

        Rows(cut).Should().HaveCount(6);
        Row(cut, "system").QuerySelector(".tm-context-usage__label")!.TextContent.Trim().Should().Be("System prompt");
        Row(cut, "system").QuerySelector(".tm-context-usage__tokens")!.TextContent.Trim().Should().Be("1,200");
        Row(cut, "system").QuerySelector(".tm-context-usage__share")!.TextContent.Trim().Should()
            .MatchRegex(@"^8\s?%$");

        Row(cut, "system").QuerySelectorAll(".tm-context-usage__tag").Select(tag => tag.TextContent.Trim()).Should()
            .Equal("Required");
        Row(cut, "memory").QuerySelectorAll(".tm-context-usage__tag").Select(tag => tag.TextContent.Trim()).Should()
            .Equal("Summarized");
        Row(cut, "results").QuerySelectorAll(".tm-context-usage__tag").Select(tag => tag.TextContent.Trim()).Should()
            .Equal("Omitted");
    }

    [Fact]
    public void TheTableIsTheReadersView_NotASecondHiddenCopy()
    {
        var cut = RenderUsage();

        cut.FindAll("table.tm-context-usage__table").Should().HaveCount(1);
        cut.FindAll("table.tm-context-usage__table[aria-hidden='true']").Should().BeEmpty(
            "the table holds the focusable Remove / expand controls, so it is the accessible view itself");
    }

    // ── Expanding ────────────────────────────────────────────────────────────

    [Fact]
    public void ASectionWithItems_ExpandsIntoItsItems_OnAButtonThatOwnsTheDisclosure()
    {
        // The tokens assertion needs the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderUsage();

        var toggle = Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!;
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.GetAttribute("aria-controls").Should().BeNull(
            "the disclosure points at a row that only exists while it is open, so a closed toggle names no id");
        toggle.TextContent.Should().Contain("Files").And.Contain("2 items");

        toggle.Click();

        var controls = Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.GetAttribute("aria-controls");
        controls.Should().NotBeNullOrWhiteSpace();
        var detail = cut.Find($"tr.tm-context-usage__detail#{controls}");
        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.GetAttribute("aria-expanded").Should().Be("true");
        detail.QuerySelectorAll(".tm-context-usage__item").Select(item => item.GetAttribute("data-item-id")).Should()
            .Equal("contract.pdf", "report.pdf");
        detail.QuerySelector(".tm-context-usage__item .tm-context-usage__item-tokens")!.TextContent.Trim()
            .Should().Be("4,000");

        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.Click();
        cut.FindAll("tr.tm-context-usage__detail").Should().BeEmpty("a second activation closes the disclosure");
    }

    [Fact]
    public void ASectionWithoutItems_HasNoDisclosureButton_AndSaysItsNameAsText()
    {
        var cut = RenderUsage();

        Row(cut, "system").QuerySelector(".tm-context-usage__toggle").Should().BeNull();
        Row(cut, "system").QuerySelector(".tm-context-usage__label").Should().NotBeNull();
    }

    [Fact]
    public void TheExpandedItemList_KeepsTheHostOrder()
    {
        var cut = RenderUsage();
        Row(cut, "tools").QuerySelector(".tm-context-usage__toggle")!.Click();

        cut.FindAll("tr.tm-context-usage__detail .tm-context-usage__item-label").Select(label => label.TextContent.Trim())
            .Should().Equal("search", "code");
    }

    // ── Removing ─────────────────────────────────────────────────────────────

    [Fact]
    public void NoRemoveActionIsOffered_WhenTheHostSuppliesNoHandler()
    {
        var cut = Render<TmContextUsage>(parameters => parameters.Add(component => component.Snapshot, Snapshot()));

        cut.FindAll(".tm-context-usage__remove").Should().BeEmpty();
        cut.FindAll(".tm-context-usage__compact").Should().BeEmpty();
    }

    [Fact]
    public void ASectionIsRemovable_OnlyWhenTheModelSaysSo()
    {
        var cut = RenderUsage();

        Row(cut, "tools").QuerySelector(".tm-context-usage__remove").Should().NotBeNull("Tools reports CanRemove");
        Row(cut, "system").QuerySelector(".tm-context-usage__remove").Should().BeNull("the system prompt is required");
        Row(cut, "chat").QuerySelector(".tm-context-usage__remove").Should().BeNull();
    }

    [Fact]
    public void RemovingASection_ReportsTheSection_AndKeepsTheReaderInPlace()
    {
        var cut = RenderUsage();
        var before = FocusCalls();

        Row(cut, "tools").QuerySelector(".tm-context-usage__remove")!.Click();

        _removal.Should().NotBeNull();
        _removal!.SectionKey.Should().Be("tools");
        _removal.Item.Should().BeNull("the whole section is the removal target");
        _removal.IsSection.Should().BeTrue();
        FocusCalls().Should().BeGreaterThan(before,
            "the pressed control disappears with the removal, so the focus is handed back to the section's own toggle");
    }

    [Fact]
    public void RemovingAnItem_ReportsTheItemInsideItsSection()
    {
        var cut = RenderUsage();
        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.Click();

        cut.Find("tr.tm-context-usage__detail [data-item-id='contract.pdf'] .tm-context-usage__item-remove").Click();

        _removal.Should().NotBeNull();
        _removal!.SectionKey.Should().Be("files");
        _removal.Item!.Id.Should().Be("contract.pdf");
        _removal.IsSection.Should().BeFalse();
    }

    [Fact]
    public void AnItemWithoutCanRemove_HasNoRemoveButton()
    {
        var cut = RenderUsage();
        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.Click();

        cut.FindAll("tr.tm-context-usage__detail [data-item-id='report.pdf'] .tm-context-usage__item-remove")
            .Should().BeEmpty("report.pdf reports CanRemove = false");
        cut.FindAll("tr.tm-context-usage__detail [data-item-id='contract.pdf'] .tm-context-usage__item-remove")
            .Should().ContainSingle();
    }

    [Fact]
    public void TheRemoveControlIsNamedByTheLocalizedAction_NotByAnIcon()
    {
        var cut = RenderUsage();

        Row(cut, "tools").QuerySelector(".tm-context-usage__remove")!.GetAttribute("aria-label")
            .Should().Be("Remove Tools");
    }

    // ── The focus hand-back after a removal ──────────────────────────────────

    // The focus hand-back runs from OnAfterRenderAsync, on the render pipeline. A real browser rejects a focus
    // whose target is no longer in the page ("Unable to focus an invalid element" → JSException) and a dropped
    // Blazor Server circuit answers with JSDisconnectedException; neither derives from InvalidOperationException,
    // so an InvalidOperationException-only catch let the exception escape — #blazor-error-ui on WASM and a dead
    // circuit on Server. `Blazor._internal.domWrapper.focus` is a plain JSInterop invocation here, so bUnit can
    // reproduce the rejection exactly.

    [Fact]
    public void AFocusTheBrowserRejects_DoesNotEscape_AndTheFallbackRootStillGetsTheFocus()
    {
        JSInterop.SetupVoid(FocusInvocation, _ => true)
            .SetException(new JSException("Unable to focus an invalid element"));

        var cut = RenderUsage();
        var rootId = cut.Find("[data-testid='context-usage']").GetAttribute("blazor:elementReference");
        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.Click();

        var act = () => cut.Find("tr.tm-context-usage__detail [data-item-id='contract.pdf'] .tm-context-usage__item-remove")
            .Click();

        act.Should().NotThrow("a rejected focus move must never escape OnAfterRenderAsync");
        FocusTargets().Last().Should().Be(rootId,
            "the section button was rejected, so the hand-back falls through to the component root");
        cut.FindAll("[data-testid='context-usage-meter']").Should().HaveCount(1, "the component survives the rejection");
    }

    [Fact]
    public void RemovingASectionWithEntries_TargetsTheRoot_NotADisclosureButtonThatLeavesWithIt()
    {
        var cut = Render<DroppingHost>();
        var rootId = cut.Find("[data-testid='context-usage-host']").GetAttribute("blazor:elementReference");
        var toggleId = cut.Find("tr.tm-context-usage__row[data-section-key='tools'] .tm-context-usage__toggle")
            .GetAttribute("blazor:elementReference");

        cut.Find("tr.tm-context-usage__row[data-section-key='tools'] .tm-context-usage__remove")!.Click();

        cut.FindAll("tr.tm-context-usage__row[data-section-key='tools']").Should().BeEmpty("the host applied the removal");
        FocusTargets().Should().ContainSingle().Which.Should().Be(rootId,
            "the pressed button AND the section's own disclosure button left in the same update, so the root is "
            + "the only target that is still on the page");
        FocusTargets().Should().NotContain(toggleId);
    }

    [Fact]
    public void AnEntryRemovalWhoseSectionTheHostThenDrops_FallsBackToTheRoot_InsteadOfAStaleReference()
    {
        var cut = Render<DroppingHost>();
        var rootId = cut.Find("[data-testid='context-usage-host']").GetAttribute("blazor:elementReference");
        var toggleId = cut.Find("tr.tm-context-usage__row[data-section-key='tools'] .tm-context-usage__toggle")
            .GetAttribute("blazor:elementReference");

        cut.Find("tr.tm-context-usage__row[data-section-key='tools'] .tm-context-usage__toggle")!.Click();
        cut.Find("tr.tm-context-usage__detail [data-item-id='search'] .tm-context-usage__item-remove")!.Click();

        cut.FindAll("tr.tm-context-usage__row[data-section-key='tools']").Should().BeEmpty("the host dropped the section");
        FocusTargets().Should().Contain(rootId)
            .And.NotContain(toggleId, "the stored disclosure reference no longer renders, so it must be pruned");
    }

    // ── Compact ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheCompactActionIsEnabled_WhenSomeSectionCanBeCompacted()
    {
        var cut = RenderUsage();

        var compact = cut.Find("[data-testid='context-usage-compact']");
        compact.HasAttribute("disabled").Should().BeFalse();
        compact.TextContent.Trim().Should().Be("Compact");

        compact.Click();
        _compactCalls.Should().Be(1);
    }

    [Fact]
    public void TheCompactActionIsDisabled_WhenNothingCanBeCompacted()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot
            {
                BudgetTokens = 4_000,
                Sections =
                [
                    new("system", "System prompt") { Tokens = 1_000, IsRequired = true },
                    new("chat", "Chat history") { Tokens = 2_000, IsSummarized = true },
                    new("results", "Tool results") { Tokens = 1_000, IsOmitted = true },
                ],
            })
            .Add(component => component.OnCompact, () => _compactCalls++));

        var compact = cut.Find("[data-testid='context-usage-compact']");
        compact.HasAttribute("disabled").Should().BeTrue();
        compact.GetAttribute("aria-disabled").Should().BeNull("a native disabled button needs no second declaration");
    }

    // ── Cache hit and the over-budget state ──────────────────────────────────

    [Fact]
    public void TheCacheHitBadge_AppearsOnlyWhenTheHostReportsOne()
    {
        var cut = RenderUsage();
        var badge = cut.Find("[data-testid='context-usage-cache']");
        badge.TextContent.Should().Contain("62").And.Contain("%");
        cut.Find("[data-testid='context-usage-meter']").GetAttribute("aria-describedby").Should().Contain(
            badge.GetAttribute("id"), "a description is not hidden from a screen reader");

        var without = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, Snapshot() with { CacheHit = null }));
        without.FindAll("[data-testid='context-usage-cache']").Should().BeEmpty();
    }

    [Fact]
    public void AnOutOfRangeCacheHit_IsClamped_InsteadOfPrintingNonsense()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, Snapshot() with { CacheHit = 1.4 }));

        cut.Find("[data-testid='context-usage-cache']").TextContent.Should().Contain("100");
    }

    [Fact]
    public void AnOverBudgetContext_IsFlaggedInWordsAndColour()
    {
        // The grouped-value assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot
            {
                BudgetTokens = 10_000,
                Sections =
                [
                    new("system", "System prompt") { Tokens = 2_000 },
                    new("chat", "Chat history") { Tokens = 9_500 },
                ],
            }));

        var root = cut.Find("[data-testid='context-usage']");
        root.GetAttribute("data-over-budget").Should().Be("true");
        root.ClassList.Should().Contain("tm-context-usage--over-budget");

        var over = cut.Find("[data-testid='context-usage-over-budget']");
        over.TextContent.Should().Contain("1,500", "the excess is named exactly");
        over.QuerySelector("svg, .tm-icon").Should().NotBeNull("the state carries an icon, so it is not colour-only");

        // ARIA does not allow aria-valuenow above aria-valuemax, and a browser may cap such a value on its own,
        // so the machine value stops at the budget while the REAL total and percentage stay in the value text.
        var meter = cut.Find("[data-testid='context-usage-meter']");
        meter.GetAttribute("aria-valuenow").Should().Be("10000", "aria-valuenow never exceeds the known budget");
        meter.GetAttribute("aria-valuemax").Should().Be("10000");
        meter.GetAttribute("aria-valuetext").Should().Contain("11,500").And.Contain("115",
            "the localized value text is what a screen reader reads, so the overflow is never rounded away");
        cut.Find(".tm-context-usage__readout").TextContent.Should().Contain("11,500",
            "the visible readout keeps the real amount");
    }

    [Fact]
    public void AFreshOverBudgetSnapshot_AnnouncesTheCrossing_Once()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot
            {
                BudgetTokens = 10_000,
                Sections = [new("chat", "Chat history") { Tokens = 9_000 }],
            }));

        var live = cut.Find("[data-testid='context-usage-announcement']");
        live.GetAttribute("role").Should().Be("status");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.TextContent.Should().BeEmpty("a first render says nothing — a page load must not talk over the reader");

        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmContextUsage.Snapshot)] = new ContextUsageSnapshot
                {
                    BudgetTokens = 10_000,
                    Sections = [new("chat", "Chat history") { Tokens = 12_000 }],
                },
            })));

        cut.WaitForAssertion(() => cut.Find("[data-testid='context-usage-announcement']").TextContent.Trim()
            .Should().BeEquivalentTo("The context window is over its budget."));

        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmContextUsage.Snapshot)] = new ContextUsageSnapshot
                {
                    BudgetTokens = 10_000,
                    Sections = [new("chat", "Chat history") { Tokens = 4_000 }],
                },
            })));

        cut.WaitForAssertion(() => cut.Find("[data-testid='context-usage-announcement']").TextContent.Trim()
            .Should().BeEquivalentTo("The context window is back within its budget."));
    }

    [Fact]
    public void ThereIsExactlyOneLiveRegion()
    {
        var cut = RenderUsage();

        cut.FindAll("[aria-live]").Should().HaveCount(1);
        cut.FindAll("[role='status'], [role='alert']").Should().HaveCount(1);
    }

    // ── Variants ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheRootNamesTheVariant()
    {
        RenderUsage().Find("[data-testid='context-usage']").GetAttribute("data-variant").Should().Be("bar");

        RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Grid))
            .Find("[data-testid='context-usage']").GetAttribute("data-variant").Should().Be("grid");

        RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Treemap))
            .Find("[data-testid='context-usage']").GetAttribute("data-variant").Should().Be("treemap");
    }

    [Fact]
    public void TheGridVariant_PaintsOneHundredCells_OverTheBudget()
    {
        var cut = RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Grid));

        var cells = cut.FindAll("[data-testid='context-usage-cell']");
        cells.Should().HaveCount(100);
        cut.Find(".tm-context-usage__grid").GetAttribute("aria-hidden").Should().Be("true");

        cells.Count(cell => cell.ClassList.Contains("tm-context-usage__cell--used")).Should().Be(88,
            "14 000 of 16 000 tokens fills 87.5 % of the budget, which is 88 whole cells");
        cells[0].GetAttribute("data-section-key").Should().Be("system");
        cells[87].GetAttribute("data-section-key").Should().Be("results");
        cells[88].GetAttribute("data-section-key").Should().BeNull("the last 12 % of the budget is still free");
        cells[88].ClassList.Should().Contain("tm-context-usage__cell--empty");
    }

    [Fact]
    public void AnOverBudgetGrid_FillsEveryCell()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot
            {
                BudgetTokens = 1_000,
                Sections = [new("chat", "Chat history") { Tokens = 1_400 }],
            })
            .Add(component => component.Variant, ContextUsageVariant.Grid));

        cut.FindAll("[data-testid='context-usage-cell']").Should()
            .OnlyContain(cell => cell.ClassList.Contains("tm-context-usage__cell--used"));
    }

    [Fact]
    public void HoveringAGridCell_HighlightsItsSection_InTheGridAndInTheLegend()
    {
        var cut = RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Grid));

        cut.FindAll("[data-testid='context-usage-cell']")[20].MouseOver();

        var highlighted = cut.FindAll(".tm-context-usage__cell--highlight");
        highlighted.Should().NotBeEmpty();
        highlighted.Should().OnlyContain(cell => cell.GetAttribute("data-section-key") == "files");
        Row(cut, "files").ClassList.Should().Contain("tm-context-usage__row--highlight");

        cut.FindAll("[data-testid='context-usage-cell']")[20].MouseOut();
        cut.FindAll(".tm-context-usage__cell--highlight").Should().BeEmpty();
        Row(cut, "files").ClassList.Should().NotContain("tm-context-usage__row--highlight");
    }

    [Fact]
    public void FocusingALegendRow_HighlightsItsCells_SoTheGridHasAKeyboardPath()
    {
        var cut = RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Grid));

        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.TriggerEventAsync("onfocusin", new FocusEventArgs());

        cut.FindAll(".tm-context-usage__cell--highlight").Should()
            .OnlyContain(cell => cell.GetAttribute("data-section-key") == "files");

        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.TriggerEventAsync("onfocusout", new FocusEventArgs());
        cut.FindAll(".tm-context-usage__cell--highlight").Should().BeEmpty();
    }

    [Fact]
    public void FocusingAnEntryRemove_HighlightsItsSection_Too()
    {
        // The section-level Remove and the disclosure button are not the only controls inside a legend row: an
        // entry's own Remove sits in the expanded detail row, and a keyboard reader who tabs to it must see the
        // same section highlighted. Without the focusin/focusout pair that path was mouse-only.
        var cut = RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Grid));

        Row(cut, "files").QuerySelector(".tm-context-usage__toggle")!.Click();
        cut.Find("tr.tm-context-usage__detail [data-item-id='contract.pdf'] .tm-context-usage__item-remove")
            .TriggerEventAsync("onfocusin", new FocusEventArgs());

        cut.FindAll(".tm-context-usage__cell--highlight").Should()
            .OnlyContain(cell => cell.GetAttribute("data-section-key") == "files");
        Row(cut, "files").ClassList.Should().Contain("tm-context-usage__row--highlight");

        cut.Find("tr.tm-context-usage__detail [data-item-id='contract.pdf'] .tm-context-usage__item-remove")
            .TriggerEventAsync("onfocusout", new FocusEventArgs());
        cut.FindAll(".tm-context-usage__cell--highlight").Should().BeEmpty();
    }

    [Fact]
    public void TheTreemapVariant_MapsSectionsToGroupsAndItemsToTiles()
    {
        var cut = RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Treemap));

        var chart = cut.Find("[data-testid='context-usage-treemap'] .tm-chart");
        chart.Should().NotBeNull();
        chart.ParentElement!.GetAttribute("aria-hidden").Should().Be("true",
            "the treemap decorates the table, so it is not announced twice");
        cut.FindAll(".tm-context-usage__bar").Should().BeEmpty();
        cut.FindAll(".tm-context-usage__grid").Should().BeEmpty();

        var paths = cut.FindAll("rect.tm-chart__treemap-tile").Select(rect => rect.GetAttribute("data-path")).ToList();
        paths.Should().Contain("Files / contract.pdf")
            .And.Contain("Files / report.pdf")
            .And.Contain("System prompt", "a section without items is one tile");
        cut.FindAll(".tm-chart__treemap-group").Should().HaveCount(6);
    }

    [Fact]
    public void TheTreemapVariant_KeepsTheMeterForReaders_AsAVisuallyHiddenMeter()
    {
        var cut = RenderUsage(parameters => parameters.Add(component => component.Variant, ContextUsageVariant.Treemap));

        var meter = cut.Find("[data-testid='context-usage-meter']");
        meter.GetAttribute("role").Should().Be("meter");
        meter.GetAttribute("aria-valuenow").Should().Be("14000");
        meter.ClassList.Should().Contain("tm-sr-only");
    }

    [Fact]
    public void EveryVariantKeepsTheSameLegend()
    {
        foreach (var variant in Enum.GetValues<ContextUsageVariant>())
        {
            var cut = RenderUsage(parameters => parameters.Add(component => component.Variant, variant));
            Rows(cut).Should().HaveCount(6, $"{variant} keeps the table");
        }
    }

    // ── Empty states ─────────────────────────────────────────────────────────

    [Fact]
    public void ANullSnapshot_RendersTheLocalizedEmptyState()
    {
        var cut = Render<TmContextUsage>();

        var empty = cut.Find("[data-testid='context-usage-empty']");
        empty.TextContent.Trim().Should().Be("No context usage is reported.");
        cut.FindAll("table.tm-context-usage__table").Should().BeEmpty();
        cut.FindAll("[data-testid='context-usage-meter']").Should().BeEmpty();
    }

    [Fact]
    public void ASnapshotWithNoSections_RendersTheSameEmptyState()
    {
        var cut = Render<TmContextUsage>(parameters => parameters
            .Add(component => component.Snapshot, new ContextUsageSnapshot { BudgetTokens = 4_000, Sections = [] }));

        cut.FindAll("[data-testid='context-usage-empty']").Should().HaveCount(1);
    }

    // ── Culture ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheCultureParameterFormatsTheNumbers_WhileTheMachineAttributesStayInvariant()
    {
        var cut = RenderUsage(parameters => parameters.Add(component => component.Culture, new CultureInfo("cs-CZ")));

        Row(cut, "files").QuerySelector(".tm-context-usage__tokens")!.TextContent.Trim().Should()
            .MatchRegex(@"^5\s?000$", "a Czech group separator is a no-break space, not a comma");
        cut.Find("[data-testid='context-usage-meter']").GetAttribute("aria-valuenow").Should().Be("14000",
            "a machine-readable attribute is read by assistive technology, so it is always invariant");
    }

    /// <summary>
    /// A host that answers a removal by dropping the reported SECTION — including an ENTRY removal. Applying the
    /// reported intent is the host's job (the component never edits the snapshot), and dropping the whole section
    /// once one of its entries goes is a legitimate answer. That update is also the one that leaves a stored
    /// disclosure-button reference without a rendered row, which the focus hand-back must survive.
    /// </summary>
    private sealed class DroppingHost : ComponentBase
    {
        private ContextUsageSnapshot _snapshot = Snapshot();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmContextUsage>(0);
            builder.AddAttribute(1, nameof(TmContextUsage.Snapshot), _snapshot);
            builder.AddAttribute(2, nameof(TmContextUsage.DataTestId), "context-usage-host");
            builder.AddAttribute(3, nameof(TmContextUsage.OnRemoveItem),
                EventCallback.Factory.Create<ContextUsageRemoval>(this, Remove));
            builder.CloseComponent();
        }

        private Task Remove(ContextUsageRemoval removal)
        {
            var sections = _snapshot.Sections ?? [];
            _snapshot = _snapshot with
            {
                Sections = sections.Where(section => section.Key != removal.SectionKey).ToList(),
            };

            return Task.CompletedTask;
        }
    }
}
