using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Demo.SharedUI.Components;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Approvals;

/// <summary>
/// bUnit contract of the <c>/human-in-the-loop</c> demo page — the page the E2E lane and the visual review
/// build on. It renders the four core decision surfaces: an <c>TmApprovalCard</c> with a countdown and an
/// Undo window, a twelve-item <c>TmReviewQueue</c> in BOTH presentations with its A/E/R/S shortcuts, an
/// <c>TmInlineDiff</c> reviewing an AI rewrite of one paragraph, and a <c>TmSuggestionCard</c> with a
/// confidence, a rationale and alternatives.
/// </summary>
/// <remarks>
/// Every assertion goes through the page (<c>[data-testid='hitl-…']</c>) rather than through a component
/// instance, because the page is what the E2E lane and the screenshots see: a section that renders a
/// correct component but wires no callback is a broken demo, and only the page can show that.
/// </remarks>
public class HumanInTheLoopDemoPageTests : DemoPageLocalizationTestBase
{
    /// <summary>
    /// The paragraph the demo's inline diff compares. The expectation is pinned HERE (instead of read from the
    /// page's sample data) on purpose: the demo's proposed text is the contract the test asserts on, and a
    /// silent edit of it has to fail somewhere.
    /// </summary>
    private const string OriginalParagraph =
        "The team reviewed the draft and decided to postpone the launch until the documentation is complete.";

    private const string ProposedParagraph =
        "The team reviewed the draft and agreed to delay the launch until the documentation is finished.";

    /// <summary>Accepting the first two changes and leaving the third pending produces this mixture.</summary>
    private const string TwoOfThreeAcceptedResult =
        "The team reviewed the draft and agreed to delay the launch until the documentation is complete.";

    private static IElement KeyboardSurface(IRenderedComponent<HumanInTheLoopPage> cut)
        => cut.Find("[data-testid='hitl-queue'] [data-testid='review-list']");

    /// <summary>The accessible name of the status list a section reports its demo state in.</summary>
    private static string StatusListLabel(IRenderedComponent<HumanInTheLoopPage> cut, string section)
        => cut.Find($"[data-testid='{section}'] dl.hitl-demo-status").GetAttribute("aria-label") ?? string.Empty;

    /// <summary>The ids of the rendered review rows, in the order the queue ordered them.</summary>
    private static IReadOnlyList<string> RowIds(IRenderedComponent<HumanInTheLoopPage> cut)
        => cut.FindAll("[data-testid='hitl-queue'] [data-testid='review-item']")
            .Select(row => row.GetAttribute("data-item-id")!)
            .ToList();

    [Fact]
    public void Page_RendersAllFourSectionsAndEveryStringResolves()
    {
        var cut = Render<HumanInTheLoopPage>();

        cut.Find("[data-testid='hitl-demo']").Should().NotBeNull();
        cut.Find("[data-testid='hitl-approval-section']").Should().NotBeNull();
        cut.Find("[data-testid='hitl-queue-section']").Should().NotBeNull();
        cut.Find("[data-testid='hitl-diff-section']").Should().NotBeNull();
        cut.Find("[data-testid='hitl-suggestion-section']").Should().NotBeNull();

        // The page is the library's showcase: the approval card, the queue, the inline diff, the suggestion
        // card and the reviews of each render for real.
        cut.Find("[data-testid='hitl-approval-section'] .tm-approval-card").Should().NotBeNull();
        cut.Find("[data-testid='hitl-queue-section'] .tm-review-queue").Should().NotBeNull();
        cut.Find("[data-testid='hitl-diff-section'] .tm-inline-diff").Should().NotBeNull();
        cut.Find("[data-testid='hitl-suggestion-section'] .tm-suggestion-card").Should().NotBeNull();

        // Every visible string comes from the demo resources: a missing key renders as "[TmDemo_…]".
        cut.Markup.Should().NotContain("[TmDemo_");
    }

    /// <summary>
    /// Each section's status list is named after its own surface. One shared name ("Demo state") would turn the
    /// three definition lists into three identically-named regions, so a screen-reader user walking the page by
    /// landmark could not tell the approval readout from the queue's or the suggestion's (WCAG 1.3.1 / 2.4.6).
    /// </summary>
    [Fact]
    public void StatusLists_AreNamedAfterTheirOwnSection()
    {
        var cut = Render<HumanInTheLoopPage>();

        var approval = StatusListLabel(cut, "hitl-approval-section");
        var queue = StatusListLabel(cut, "hitl-queue-section");
        var suggestion = StatusListLabel(cut, "hitl-suggestion-section");

        new[] { approval, queue, suggestion }.Should().OnlyHaveUniqueItems(
            "three status lists under one name are one indistinguishable region for a screen-reader user");
        new[] { approval, queue, suggestion }.Should().NotContain(string.Empty,
            "a list with an empty name is worse than one with a shared name");

        approval.Should().ContainEquivalentOf("approval", "the first list reports the approval card");
        queue.Should().ContainEquivalentOf("queue", "the second list reports the review queue");
        suggestion.Should().ContainEquivalentOf("suggestion", "the third list reports the suggestion card");
    }

    [Fact]
    public void Approval_SectionShowsTheCountdownAndTheRiskWords()
    {
        var cut = Render<HumanInTheLoopPage>();

        cut.Find("[data-testid='hitl-approval'] [data-testid='approval-countdown']").Should().NotBeNull();
        cut.Find("[data-testid='hitl-approval'] [data-testid='approval-approve']").Should().NotBeNull();
        cut.Find("[data-testid='hitl-approval'] [data-testid='approval-reject']").Should().NotBeNull();

        // Risk is spelled out, so the badge is not colour-only.
        cut.Find("[data-testid='hitl-approval'] [data-testid='approval-risk']")
            .TextContent.Should().Contain("risk", "the demo shows the High risk sample and spells the level out");

        // The keyboard contract is advertised on the buttons the page wired.
        cut.Find("[data-testid='hitl-approval'] [data-testid='approval-approve']")
            .GetAttribute("aria-keyshortcuts").Should().Contain("Control+Enter");
    }

    [Fact]
    public async Task Approval_KeyboardDecisionCollapsesTheCardAndUndoBringsItBack()
    {
        var cut = Render<HumanInTheLoopPage>();

        cut.Find("[data-testid='hitl-approval-state']").TextContent
            .Should().Contain("Pending", "the demo starts with the request undecided");

        // Ctrl+Enter anywhere inside the card approves it, exactly like the component's contract says.
        await cut.Find("[data-testid='hitl-approval']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        cut.Find("[data-testid='hitl-approval']").GetAttribute("data-state").Should().Be("approved",
            "the decided card collapses into its result row");
        cut.Find("[data-testid='hitl-approval'] [data-testid='approval-summary-text']").TextContent
            .Should().Contain("Approved");
        cut.Find("[data-testid='hitl-approval-state']").TextContent
            .Should().Contain("Approved");
        cut.Find("[data-testid='hitl-approval-decisions']").TextContent
            .Should().Be("1", "the page counts the decision its OnDecision callback received");

        // The Undo window is still open, so the row offers it and the click restores the pending request.
        await cut.Find("[data-testid='hitl-approval'] [data-testid='approval-undo']").ClickAsync();

        cut.Find("[data-testid='hitl-approval-state']").TextContent
            .Should().Contain("Pending");
        cut.Find("[data-testid='hitl-approval-undone']").TextContent
            .Should().Be("1", "the page records the undo it persisted");
    }

    [Fact]
    public async Task Queue_RendersTwelveItemsInBothPresentations()
    {
        var cut = Render<HumanInTheLoopPage>();

        RowIds(cut).Should().HaveCount(12, "the demo queue is the twelve-item one the phase asks for");
        cut.Find("[data-testid='hitl-queue-pending']").TextContent.Should().Contain("12");

        await cut.Find("[data-testid='hitl-queue-mode-focus']").ClickAsync();

        cut.FindAll("[data-testid='hitl-queue'] [data-testid='review-focus']").Should()
            .ContainSingle("the second presentation is the one-at-a-time focus view");
        cut.Find("[data-testid='hitl-queue'] [data-testid='review-position']").TextContent
            .Should().Contain("12", "the position counter names the queue length, not the page's own count");

        await cut.Find("[data-testid='hitl-queue-mode-list']").ClickAsync();
        RowIds(cut).Should().HaveCount(12, "walking back to the list keeps every item in the queue");
    }

    [Fact]
    public async Task Queue_ShortcutResolvesTheActiveItemAndAutoAdvances()
    {
        var cut = Render<HumanInTheLoopPage>();
        var before = RowIds(cut);

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        // `A` approves the active item; the item stays in the list as a collapsed result row and the
        // selection auto-advances to the next pending one.
        var rows = cut.FindAll("[data-testid='hitl-queue'] [data-testid='review-item']");
        rows.Should().HaveCount(12, "a resolved item is never removed from the host's list");
        rows.Single(row => row.GetAttribute("data-item-id") == before[0])
            .QuerySelector("[data-testid='review-item-result-label']")!.TextContent
            .Should().Contain("Approved", "an approved item keeps its result in the list");

        var selected = rows.Single(row => row.GetAttribute("aria-selected") == "true");
        selected.GetAttribute("data-item-id").Should().Be(before[1], "AutoAdvance moves to the next pending item");

        cut.Find("[data-testid='hitl-queue-pending']").TextContent.Should().Contain("11");
        cut.Find("[data-testid='hitl-queue-handled']").TextContent.Should().Contain("1");
        cut.Find("[data-testid='hitl-queue-last']").TextContent.Should().Contain("Approved");
    }

    [Fact]
    public async Task Queue_SkipKeepsTheItemPendingAndRejectResolvesIt()
    {
        var cut = Render<HumanInTheLoopPage>();
        var before = RowIds(cut);

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "s" });
        cut.Find("[data-testid='hitl-queue-pending']").TextContent.Should().Contain("12",
            "Skip is the one verb that does not resolve an item");

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "r" });
        cut.Find("[data-testid='hitl-queue-pending']").TextContent.Should().Contain("11");
        cut.Find("[data-testid='hitl-queue-last']").TextContent.Should().Contain("Rejected");

        var rows = cut.FindAll("[data-testid='hitl-queue'] [data-testid='review-item']");
        rows.Single(row => row.GetAttribute("data-item-id") == before[1])
            .QuerySelector("[data-testid='review-item-result-label']")!.TextContent
            .Should().Contain("Rejected", "the skip moved the selection to the second item, which R rejected");
    }

    [Fact]
    public async Task Queue_ResetRestartsTheQueueFromTheHostList()
    {
        var cut = Render<HumanInTheLoopPage>();

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "a" });
        cut.Find("[data-testid='hitl-queue-pending']").TextContent.Should().Contain("11");

        await cut.Find("[data-testid='hitl-queue-reset']").ClickAsync();

        cut.Find("[data-testid='hitl-queue-pending']").TextContent.Should().Contain("12");
        cut.Find("[data-testid='hitl-queue-handled']").TextContent.Should().Contain("0");
        cut.FindAll("[data-testid='hitl-queue'] [data-testid='review-item-result-label']")
            .Select(label => label.TextContent.Trim())
            .Should().OnlyContain(text => text.Length == 0,
                "a restarted queue has no resolved rows");
    }

    [Fact]
    public void Diff_RendersThreeReviewableChangesAndTheProposedText()
    {
        var cut = Render<HumanInTheLoopPage>();

        cut.FindAll("[data-testid='hitl-diff'] [data-testid='inline-diff-change']").Should().HaveCount(3,
            "the demo paragraph differs from the AI rewrite in exactly three places");
        cut.Find("[data-testid='hitl-diff'] [data-testid='inline-diff-count']").TextContent.Should().Contain("3");

        // Nothing is accepted yet, so the result is the original paragraph character for character.
        cut.Find("[data-testid='hitl-diff-result']").TextContent.Trim()
            .Should().Be(OriginalParagraph);
    }

    [Fact]
    public async Task Diff_AcceptingTwoOfThreeChangesProducesTheMixedText()
    {
        var cut = Render<HumanInTheLoopPage>();

        // Accept the first two changes through their marker + popover, the way a reader does it.
        for (var index = 0; index < 2; index++)
        {
            await cut.FindAll("[data-testid='hitl-diff'] [data-testid='inline-diff-marker']")[index].ClickAsync();
            await cut.Find("[data-testid='hitl-diff'] [data-testid='inline-diff-accept']").ClickAsync();
        }

        cut.Find("[data-testid='hitl-diff-result']").TextContent.Trim()
            .Should().Be(TwoOfThreeAcceptedResult,
                "accepting two of three changes must produce the mixture, not the old or the new text");
        cut.Find("[data-testid='hitl-diff-accepted']").TextContent.Should().Contain("2");
    }

    [Fact]
    public async Task Diff_BulkRejectRestoresTheOriginalParagraph()
    {
        var cut = Render<HumanInTheLoopPage>();

        await cut.Find("[data-testid='hitl-diff'] [data-testid='inline-diff-accept-all']").ClickAsync();
        cut.Find("[data-testid='hitl-diff-result']").TextContent.Trim()
            .Should().Be(ProposedParagraph, "accepting every change is the new text");

        await cut.Find("[data-testid='hitl-diff'] [data-testid='inline-diff-reject-all']").ClickAsync();
        cut.Find("[data-testid='hitl-diff-result']").TextContent.Trim()
            .Should().Be(OriginalParagraph);
    }

    [Fact]
    public void Suggestion_ShowsConfidenceRationaleAndAlternatives()
    {
        var cut = Render<HumanInTheLoopPage>();

        cut.Find("[data-testid='hitl-suggestion'] [data-testid='suggestion-confidence']").Should().NotBeNull();
        cut.Find("[data-testid='hitl-suggestion'] [data-testid='suggestion-confidence-percent']").TextContent
            .Should().Contain("72");
        cut.Find("[data-testid='hitl-suggestion'] [data-testid='suggestion-rationale']").Should().NotBeNull();
        cut.FindAll("[data-testid='hitl-suggestion'] [data-testid='suggestion-alternative']").Should().HaveCount(2);
        cut.Find("[data-testid='hitl-suggestion-status']").TextContent.Should().Contain("waiting");
    }

    [Fact]
    public async Task Suggestion_AcceptRecordsTheAlternativeAndOffersUndo()
    {
        var cut = Render<HumanInTheLoopPage>();

        // The alternatives are a native radiogroup: picking one raises the two-way bound value.
        var alternatives = cut.FindAll("[data-testid='hitl-suggestion'] [data-testid='suggestion-alternative']");
        var chosen = alternatives[1].ParentElement!.TextContent.Trim();
        chosen.Should().NotBeEmpty("the page offers a labelled alternative");

        alternatives[1].Change(true);
        await cut.Find("[data-testid='hitl-suggestion'] [data-testid='suggestion-accept']").ClickAsync();

        cut.Find("[data-testid='hitl-suggestion-status']").TextContent.Should().Contain("accepted");
        cut.Find("[data-testid='hitl-suggestion-status']").TextContent.Should().Contain(
            chosen, "an accepted suggestion records which alternative was chosen");

        await cut.Find("[data-testid='hitl-suggestion'] [data-testid='suggestion-undo']").ClickAsync();
        cut.Find("[data-testid='hitl-suggestion-status']").TextContent.Should().Contain("waiting");
        cut.Find("[data-testid='hitl-suggestion-undone']").TextContent.Should().Be("1");
    }

    [Fact]
    public async Task Suggestion_DismissCollapsesTheCard()
    {
        var cut = Render<HumanInTheLoopPage>();

        await cut.Find("[data-testid='hitl-suggestion'] [data-testid='suggestion-dismiss']").ClickAsync();

        cut.Find("[data-testid='hitl-suggestion-status']").TextContent.Should().Contain("dismissed");

        // The decided card collapses: the pending half stays mounted (so the height can transition) but is
        // inert, which is what keeps a decided suggestion un-decidable.
        cut.Find("[data-testid='hitl-suggestion']").GetAttribute("data-state").Should().Be("dismissed");
        cut.Find("[data-testid='hitl-suggestion'] .tm-suggestion-card__body").HasAttribute("inert")
            .Should().BeTrue("the collapsed pending half must leave the tab order");
    }
}

/// <summary>
/// bUnit contract of the <see cref="PermissionRequestDemoSection"/> the <c>/ai-conversation</c> page embeds:
/// the agent asks for one concrete permission, the reader grants it for a while, the granted row counts
/// down, and Revoke hands the permission back.
/// </summary>
public class PermissionRequestDemoSectionTests : DemoPageLocalizationTestBase
{
    [Fact]
    public void Section_RendersTheRequestAndTheScopeChoices()
    {
        var cut = Render<PermissionRequestDemoSection>();

        cut.Find("[data-testid='ai-permission-demo']").Should().NotBeNull();
        cut.Find("[data-testid='ai-permission']").Should().NotBeNull();
        cut.Find("[data-testid='ai-permission-tool']").TextContent.Should().Contain("web.search");
        cut.Find("[data-testid='ai-permission-target']").TextContent.Should().Contain("example.com");
        cut.FindAll("[data-testid='ai-permission-scopes'] input[type='radio']").Should().HaveCount(4,
            "the demo offers every scope, so the reader can pick a duration");

        cut.Markup.Should().NotContain("[TmDemo_");
    }

    [Fact]
    public async Task Section_AllowForADurationGrantsThePermissionAndRevokeGivesItBack()
    {
        var cut = Render<PermissionRequestDemoSection>();

        cut.Find("[data-testid='ai-permission-scope-for-duration']").Change(true);
        await cut.Find("[data-testid='ai-permission-allow']").ClickAsync();

        cut.Find("[data-testid='ai-permission']").GetAttribute("data-state").Should().Be("granted");
        cut.Find("[data-testid='ai-permission-demo-grant']").TextContent.Trim().Should()
            .Be("Allow for a while",
                "the page reports the scope of the grant its GrantChanged callback received");

        await cut.Find("[data-testid='ai-permission-revoke']").ClickAsync();

        cut.Find("[data-testid='ai-permission']").GetAttribute("data-state").Should().Be("pending");
        cut.Find("[data-testid='ai-permission-demo-grant']").TextContent.Should().Contain("no permission granted");
        cut.Find("[data-testid='ai-permission-demo-revoked']").TextContent.Should().Be("1");
    }

    [Fact]
    public async Task Section_DenyCollapsesTheRequestWithoutGrantingAnything()
    {
        var cut = Render<PermissionRequestDemoSection>();

        await cut.Find("[data-testid='ai-permission-deny']").ClickAsync();

        cut.Find("[data-testid='ai-permission']").GetAttribute("data-state").Should().Be("denied");
        cut.Find("[data-testid='ai-permission-demo-grant']").TextContent.Should().Contain("no permission granted");

        await cut.Find("[data-testid='ai-permission-demo-reset']").ClickAsync();
        cut.Find("[data-testid='ai-permission']").GetAttribute("data-state").Should().Be("pending");
    }

    [Fact]
    public async Task Section_ShortcutAllowedAndDeniedFromTheKeyboard()
    {
        var cut = Render<PermissionRequestDemoSection>();

        await cut.Find("[data-testid='ai-permission']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        cut.Find("[data-testid='ai-permission']").GetAttribute("data-state").Should().Be("granted");

        await cut.Find("[data-testid='ai-permission-demo-reset']").ClickAsync();
        await cut.Find("[data-testid='ai-permission']").KeyDownAsync(new KeyboardEventArgs { Key = "Backspace", CtrlKey = true });
        cut.Find("[data-testid='ai-permission']").GetAttribute("data-state").Should().Be("denied");
    }
}
