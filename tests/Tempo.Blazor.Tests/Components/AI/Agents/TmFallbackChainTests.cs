using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 5 — <c>TmFallbackChain</c> (Tempo.Blazor.AI): the models tried for ONE request, in order, as a
/// flow that ends in an Answer node, plus the edit mode a host opens over the chain.
/// <para>
/// The tests read behaviour: every attempt is a labelled node with its result as an icon AND a word AND a tone, a failed
/// or skipped node prints the reason it never answered, the chain is joined by decorative SVG arrows and the first
/// successful attempt branches to the Answer node ("no answer yet" otherwise), the table view carries the same facts,
/// and the edit mode reorders the chain through <c>TmSortableList</c>, adds a model from <c>AvailableModels</c>, removes
/// one, and reports every change through <c>ChainChanged</c> — announcing the add and the remove exactly once.
/// </para>
/// </summary>
public class TmFallbackChainTests : LocalizationTestBase
{
    /// <summary>
    /// How bUnit surfaces an <c>ElementReference.FocusAsync</c>. The focused reference's
    /// <see cref="ElementReference.Id"/> is compared to the target's <c>blazor:elementReference</c>
    /// attribute, so a fact can prove WHICH control took the focus.
    /// </summary>
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static readonly DateTimeOffset Ten = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private FallbackChainViewMode? _viewMode;
    private IReadOnlyList<FallbackChainEntry>? _chain;
    private FallbackResolutionPolicy? _policy;
    private int _chainChangeCount;

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static FallbackAttempt Attempt(
        string modelId,
        FallbackAttemptStatus status = FallbackAttemptStatus.Succeeded,
        string? label = null,
        string? reason = null,
        long? latency = 800,
        DateTimeOffset? at = null)
        => new(modelId)
        {
            Label = label ?? string.Empty,
            Status = status,
            FailureReason = reason,
            LatencyMs = latency,
            At = at ?? Ten,
        };

    /// <summary>Three attempts: the first failed on a rate limit, the second was skipped, the third answered.</summary>
    private static IReadOnlyList<FallbackAttempt> Attempts() =>
    [
        Attempt("gpt-4o", FallbackAttemptStatus.Failed, "GPT-4o",
            reason: "Provider rate limit", latency: 1200),
        Attempt("claude-3-5", FallbackAttemptStatus.Skipped, "Claude 3.5 Sonnet",
            reason: "No vision support", latency: null),
        Attempt("gpt-4o-mini", FallbackAttemptStatus.Succeeded, "GPT-4o mini", latency: 640),
    ];

    private static IReadOnlyList<AiModelInfo> Models() =>
    [
        new("gpt-4o", "GPT-4o"),
        new("claude-3-5", "Claude 3.5 Sonnet"),
        new("gpt-4o-mini", "GPT-4o mini"),
        new("o3", "o3"),
    ];

    private IRenderedComponent<TmFallbackChain> RenderChain(
        IReadOnlyList<FallbackAttempt>? attempts = null,
        FallbackChainMode mode = FallbackChainMode.View,
        FallbackChainViewMode viewMode = FallbackChainViewMode.Flow,
        IReadOnlyList<FallbackChainEntry>? chain = null,
        IReadOnlyList<AiModelInfo>? models = null,
        FallbackResolutionPolicy policy = FallbackResolutionPolicy.Fallback,
        bool showPolicy = true,
        bool showTitle = true,
        int headingLevel = 3,
        string? title = null,
        CultureInfo? culture = null)
    {
        _viewMode = null;
        _chain = null;
        _policy = null;
        _chainChangeCount = 0;

        return Render<TmFallbackChain>(parameters =>
        {
            parameters.Add(component => component.Attempts, attempts ?? Attempts());
            parameters.Add(component => component.Mode, mode);
            parameters.Add(component => component.ViewMode, viewMode);
            parameters.Add(component => component.AvailableModels, models ?? Models());
            parameters.Add(component => component.Policy, policy);
            parameters.Add(component => component.ShowPolicy, showPolicy);
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.Culture, culture ?? CultureInfo.GetCultureInfo("en-US"));
            parameters.Add(component => component.ViewModeChanged, (FallbackChainViewMode next) => _viewMode = next);
            parameters.Add(component => component.PolicyChanged, (FallbackResolutionPolicy next) => _policy = next);
            parameters.Add(component => component.ChainChanged, (IReadOnlyList<FallbackChainEntry> next) =>
            {
                _chain = next;
                _chainChangeCount++;
            });

            if (chain is not null)
            {
                parameters.Add(component => component.Chain, chain);
            }

            if (title is not null)
            {
                parameters.Add(component => component.Title, title);
            }
        });
    }

    private static Task UpdateAsync(
        IRenderedComponent<TmFallbackChain> cut,
        params (string Name, object? Value)[] values)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            values.ToDictionary(value => value.Name, value => value.Value))));

    private static AngleSharp.Dom.IElement Node(IRenderedComponent<TmFallbackChain> cut, string modelId)
        => cut.Find($"[data-testid='fallback-chain-node'][data-model-id='{modelId}']");

    private static string TextOf(IRenderedComponent<TmFallbackChain> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    private static string Announcement(IRenderedComponent<TmFallbackChain> cut)
        => cut.Find("[data-testid='fallback-chain-announcement']").TextContent;

    private static string[] ChainIds(IRenderedComponent<TmFallbackChain> cut)
        => [.. cut.Instance.ChainEntries.Select(entry => entry.ModelId)];

    /// <summary>The <c>@ref</c> id of a rendered element, i.e. what a focus call must name to land on it.</summary>
    private static string ElementId(IRenderedComponent<TmFallbackChain> cut, string cssSelector)
        => cut.Find(cssSelector).GetAttribute("blazor:elementReference")!;

    /// <summary>The <c>@ref</c> id of a chain row, addressed by its painted position.</summary>
    private static string RowElementId(IRenderedComponent<TmFallbackChain> cut, int position)
        => cut.FindAll(".tm-sortable-list__item")[position].GetAttribute("blazor:elementReference")!;

    /// <summary>The focus calls bUnit recorded for <see cref="FocusInvocation"/>, in order.</summary>
    private IReadOnlyList<Bunit.JSRuntimeInvocation> FocusCalls()
        => [.. JSInterop.Invocations.Where(invocation => invocation.Identifier == FocusInvocation)];

    // ── Header ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheHeader_RendersTheHeadingAtTheRequestedLevel()
    {
        var cut = RenderChain(title: "Invoice run", headingLevel: 2);

        var heading = cut.Find("[data-testid='fallback-chain-title']");
        heading.TagName.Should().Be("H2");
        heading.TextContent.Should().Be("Invoice run");
    }

    [Fact]
    public void TheDefaultTitle_IsLocalized_AndTheLevelIsClamped()
    {
        var cut = RenderChain(headingLevel: 1);

        var heading = cut.Find("[data-testid='fallback-chain-title']");
        heading.TagName.Should().Be("H2");
        heading.TextContent.Should().Be("Fallback chain");
    }

    [Fact]
    public void TheHeadingCanBeHidden_WithoutLosingTheFlowLabel()
    {
        var cut = RenderChain(showTitle: false);

        cut.FindAll("[data-testid='fallback-chain-title']").Should().BeEmpty();
        cut.Find("[data-testid='fallback-chain-flow']").GetAttribute("aria-label")
            .Should().Be("Fallback attempts");
    }

    // ── The flow ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheFlow_IsAnOrderedListOfIdentifiedAttempts()
    {
        var cut = RenderChain();

        var flow = cut.Find("[data-testid='fallback-chain-flow']");
        flow.TagName.Should().Be("OL");
        flow.GetAttribute("role").Should().Be("list");
        flow.GetAttribute("aria-describedby").Should().NotBeNullOrEmpty(
            "the keyboard instructions describe the flow");

        var nodes = cut.FindAll("[data-testid='fallback-chain-node']");
        nodes.Should().HaveCount(3);
        nodes.Select(node => node.GetAttribute("data-model-id")).Should()
            .Equal("gpt-4o", "claude-3-5", "gpt-4o-mini");
        nodes.Select(node => node.GetAttribute("data-status")).Should()
            .Equal("failed", "skipped", "succeeded");
        nodes.Select(node => node.GetAttribute("data-position")).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void EachAttempt_NamesItsModelPositionAndResult()
    {
        var cut = RenderChain();

        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-position']")!.TextContent
            .Should().Be("Attempt 1");
        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-label']")!.TextContent
            .Should().Be("GPT-4o");
        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-status']")!.TextContent.Trim()
            .Should().Be("Failed");
        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-status']")!
            .QuerySelector("svg").Should().NotBeNull("a result is an icon and a word, never a colour");
    }

    [Fact]
    public void AModelWithoutALabel_IsNamedByItsId()
    {
        var cut = RenderChain([Attempt("o3", FallbackAttemptStatus.Succeeded, latency: 100)]);

        Node(cut, "o3").QuerySelector("[data-testid='fallback-chain-node-label']")!.TextContent
            .Should().Be("o3");
    }

    [Fact]
    public void AFailedOrSkippedAttempt_PrintsTheReasonItNeverAnswered()
    {
        var cut = RenderChain();

        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-reason']")!.TextContent
            .Should().Contain("Provider rate limit");
        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-reason']")!.TextContent
            .Should().Contain("Reason");
        Node(cut, "claude-3-5").QuerySelector("[data-testid='fallback-chain-node-reason']")!.TextContent
            .Should().Contain("No vision support");
        Node(cut, "gpt-4o-mini").QuerySelectorAll("[data-testid='fallback-chain-node-reason']").Should().BeEmpty();
    }

    [Fact]
    public void TheLatencyIsFormatted_AndAMissingOneIsOmitted()
    {
        var cut = RenderChain();

        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-latency']")!.TextContent
            .Should().Contain("1.2 s");
        Node(cut, "gpt-4o-mini").QuerySelector("[data-testid='fallback-chain-node-latency']")!.TextContent
            .Should().Contain("640 ms");
        Node(cut, "claude-3-5").QuerySelectorAll("[data-testid='fallback-chain-node-latency']").Should().BeEmpty(
            "an unmeasured latency is not a zero");
    }

    [Fact]
    public void TheTimeIsCultureFormatted()
    {
        var culture = CultureInfo.GetCultureInfo("cs-CZ");
        var cut = RenderChain(culture: culture);

        Node(cut, "gpt-4o").QuerySelector("[data-testid='fallback-chain-node-at']")!.TextContent
            .Should().Contain(Ten.ToLocalTime().ToString("t", culture));
    }

    [Fact]
    public void ThePositionAndTheLatency_FollowTheBoundCulture()
    {
        var culture = CultureInfo.GetCultureInfo("cs");
        var cut = RenderChain(
            [Attempt("a", FallbackAttemptStatus.Failed, "A", reason: "timeout", latency: 1500)],
            culture: culture);

        // A machine with an English default would print "1.5 s": every number on the surface comes from the
        // culture the host bound, never from CultureInfo.CurrentCulture.
        Node(cut, "a").QuerySelector("[data-testid='fallback-chain-node-latency']")!.TextContent
            .Should().Contain("1,5 s");
        Node(cut, "a").QuerySelector("[data-testid='fallback-chain-node-position']")!.TextContent
            .Should().Be("Attempt 1");
    }

    [Theory]
    [InlineData(FallbackAttemptStatus.NotTried, "not-tried", "Not tried")]
    [InlineData(FallbackAttemptStatus.Skipped, "skipped", "Skipped")]
    [InlineData(FallbackAttemptStatus.Failed, "failed", "Failed")]
    [InlineData(FallbackAttemptStatus.Succeeded, "succeeded", "Succeeded")]
    public void AStatus_IsAnIconAWordAndATone(FallbackAttemptStatus status, string tone, string word)
    {
        var cut = RenderChain([Attempt("m", status, latency: 10)]);

        var chip = Node(cut, "m").QuerySelector("[data-testid='fallback-chain-node-status']")!;
        chip.GetAttribute("data-status").Should().Be(tone);
        chip.QuerySelector("svg").Should().NotBeNull();
        chip.TextContent.Trim().Should().Be(word);
    }

    [Fact]
    public void AnUnknownStatus_FallsBackToNotTried()
    {
        var cut = RenderChain([Attempt("m", (FallbackAttemptStatus)31, latency: 10)]);

        Node(cut, "m").GetAttribute("data-status").Should().Be("not-tried");
        Node(cut, "m").QuerySelector("[data-testid='fallback-chain-node-status']")!.TextContent.Trim()
            .Should().Be("Not tried");
    }

    [Fact]
    public void TheBranchesAreDecorativeSvgArrows()
    {
        var cut = RenderChain();

        var arrows = cut.FindAll("[data-testid='fallback-chain-arrow']");
        arrows.Should().HaveCount(2, "three attempts are joined by two arrows");
        arrows.Should().OnlyContain(arrow => arrow.GetAttribute("aria-hidden") == "true");
        arrows.Should().OnlyContain(arrow => arrow.QuerySelector("svg") != null);
    }

    // ── The answer node ──────────────────────────────────────────────────────

    [Fact]
    public void TheSuccessfulAttempt_ContinuesToTheAnswerNode()
    {
        var cut = RenderChain();

        var answer = cut.Find("[data-testid='fallback-chain-answer']");
        answer.GetAttribute("data-answered").Should().Be("true");
        answer.TextContent.Should().Contain("Answer");
        answer.QuerySelector("[data-testid='fallback-chain-answer-model']")!.TextContent
            .Should().Be("Answered by GPT-4o mini",
                "the FIRST successful attempt ends the chain; a later one could not have answered");

        cut.Find("[data-testid='fallback-chain-answer-arrow']").GetAttribute("data-success")
            .Should().Be("true");
    }

    [Fact]
    public void AChainWithoutASuccess_SaysThereIsNoAnswerYet()
    {
        var cut = RenderChain(
        [
            Attempt("a", FallbackAttemptStatus.Failed, reason: "timeout"),
            Attempt("b", FallbackAttemptStatus.NotTried, latency: null),
        ]);

        var answer = cut.Find("[data-testid='fallback-chain-answer']");
        answer.GetAttribute("data-answered").Should().Be("false");
        answer.TextContent.Should().Contain("No answer yet");
        cut.Find("[data-testid='fallback-chain-answer-arrow']").GetAttribute("data-success")
            .Should().Be("false");
    }

    [Fact]
    public void TheFirstSuccess_IsTheOneTheAnswerNames()
    {
        var cut = RenderChain(
        [
            Attempt("a", FallbackAttemptStatus.Failed, "A", reason: "timeout"),
            Attempt("b", FallbackAttemptStatus.Succeeded, "B"),
            Attempt("c", FallbackAttemptStatus.NotTried, "C", latency: null),
        ]);

        cut.Find("[data-testid='fallback-chain-answer-model']").TextContent.Should().Be("Answered by B");
    }

    // ── The policy ───────────────────────────────────────────────────────────

    [Fact]
    public void ThePolicy_IsAReadOutInTheRunView()
    {
        var cut = RenderChain(policy: FallbackResolutionPolicy.Auto);

        var policy = cut.Find("[data-testid='fallback-chain-policy']");
        policy.TextContent.Trim().Should().Be("Auto");
        cut.FindAll("[data-testid='fallback-chain-policy-label']").Should().NotBeEmpty();
        cut.FindComponents<TmSegmentedControl<FallbackResolutionPolicy>>().Should().BeEmpty(
            "the run view reports the policy; it does not offer to change it");
    }

    [Fact]
    public void ThePolicyReadOutCanBeHidden()
    {
        var cut = RenderChain(showPolicy: false);

        cut.FindAll("[data-testid='fallback-chain-policy']").Should().BeEmpty();
    }

    // ── The table alternative ────────────────────────────────────────────────

    [Fact]
    public void TheTableView_ReplacesTheFlow_AndCarriesTheSameFacts()
    {
        var cut = RenderChain(viewMode: FallbackChainViewMode.Table);

        cut.FindAll("[data-testid='fallback-chain-flow']").Should().BeEmpty("the table replaces the drawing");
        cut.Find("[data-testid='fallback-chain-table']").QuerySelector("caption")!.TextContent.Trim()
            .Should().Be("Fallback attempts");

        var rows = cut.FindAll("[data-testid='fallback-chain-table-row']");
        rows.Should().HaveCount(3);
        rows[0].TextContent.Should().Contain("GPT-4o");
        rows[0].TextContent.Should().Contain("Failed");
        rows[0].TextContent.Should().Contain("Provider rate limit");
        rows[1].TextContent.Should().Contain("Not measured", "an unmeasured latency is stated, never invented");
        rows[2].TextContent.Should().Contain("640 ms");
    }

    [Fact]
    public void TheTableRows_AreIdentifiedByModel()
    {
        var cut = RenderChain(viewMode: FallbackChainViewMode.Table);

        cut.FindAll("[data-testid='fallback-chain-table-row']")
            .Select(row => row.GetAttribute("data-model-id")).Should().Equal("gpt-4o", "claude-3-5", "gpt-4o-mini");
    }

    [Fact]
    public void TheViewSwitcher_ReportsTheNewPresentation()
    {
        var cut = RenderChain();

        cut.FindAll("[data-testid='fallback-chain-view'] button")[1].Click();

        _viewMode.Should().Be(FallbackChainViewMode.Table);
        cut.FindAll("[data-testid='fallback-chain-table']").Should().NotBeEmpty(
            "the reader's pick paints the table before the host echoes it back");
        cut.FindAll("[data-testid='fallback-chain-flow']").Should().BeEmpty();
    }

    [Fact]
    public void TheViewModeParameter_MovesThePresentation()
    {
        var cut = RenderChain(viewMode: FallbackChainViewMode.Table);

        cut.FindAll("[data-testid='fallback-chain-table']").Should().NotBeEmpty();

        UpdateAsync(cut, (nameof(TmFallbackChain.ViewMode), FallbackChainViewMode.Flow));

        cut.FindAll("[data-testid='fallback-chain-flow']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='fallback-chain-table']").Should().BeEmpty();
    }

    // ── Empty states ─────────────────────────────────────────────────────────

    [Fact]
    public void NoAttempts_RenderTheLocalizedEmptyState()
    {
        var cut = RenderChain(attempts: []);

        TextOf(cut, "fallback-chain-empty").Should().Be("No fallback attempts to show yet.");
        cut.FindAll("[data-testid='fallback-chain-flow']").Should().BeEmpty();
        cut.FindAll("[data-testid='fallback-chain-answer']").Should().BeEmpty();
        cut.FindAll("[data-testid='fallback-chain-view']").Should().BeEmpty();
    }

    [Fact]
    public void NullAttempts_ReadAsAnEmptyChain()
    {
        var cut = Render<TmFallbackChain>(parameters =>
            parameters.Add(component => component.Attempts, (IReadOnlyList<FallbackAttempt>?)null));

        cut.FindAll("[data-testid='fallback-chain-empty']").Should().NotBeEmpty();
    }

    // ── Edit mode ────────────────────────────────────────────────────────────

    [Fact]
    public void TheEditMode_RendersTheChainThroughTheSortableList()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        cut.FindAll("[data-testid='fallback-chain-flow']").Should().BeEmpty("the editor replaces the drawing");
        cut.Find("[data-testid='fallback-chain-editor']").Should().NotBeNull();

        var list = cut.FindComponents<TmSortableList<FallbackChainEntry>>().Single();
        list.Instance.Items!.Select(entry => entry.ModelId).Should()
            .Equal("gpt-4o", "claude-3-5", "gpt-4o-mini");
        list.Instance.KeySelector.Should().NotBeNull("a reorder must keep its row keyed by the model");
        list.Instance.AriaLabel.Should().Be("Models in fallback order");

        cut.FindAll("[data-testid='fallback-chain-editor-row']").Should().HaveCount(3);
        cut.FindAll("[data-testid='fallback-chain-editor-remove']").Should().HaveCount(3);
    }

    [Fact]
    public void TheChainParameter_PinsTheEditorRows()
    {
        var cut = RenderChain(
            mode: FallbackChainMode.Edit,
            chain:
            [
                new FallbackChainEntry("o3", "o3"),
                new FallbackChainEntry("gpt-4o", "GPT-4o"),
            ]);

        ChainIds(cut).Should().Equal("o3", "gpt-4o");
        cut.FindAll("[data-testid='fallback-chain-editor-row']").Should().HaveCount(2);
    }

    [Fact]
    public void RemovingAModel_ReportsTheShorterChain_AndAnnouncesItOnce()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        cut.FindAll("[data-testid='fallback-chain-editor-remove']")[1].Click();

        _chain.Should().NotBeNull();
        _chain!.Select(entry => entry.ModelId).Should().Equal("gpt-4o", "gpt-4o-mini");
        _chainChangeCount.Should().Be(1);
        Announcement(cut).Should().Contain("Claude 3.5 Sonnet removed from the chain.");
        cut.FindAll("[data-testid='fallback-chain-editor-row']").Should().HaveCount(2,
            "the editor paints the chain it now holds");
    }

    [Fact]
    public void RemovingAModelWithoutAHostBinding_StillUpdatesTheEditor()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        cut.FindAll("[data-testid='fallback-chain-editor-remove']")[0].Click();

        ChainIds(cut).Should().Equal("claude-3-5", "gpt-4o-mini");
    }

    [Fact]
    public void AddingAModel_OffersOnlyWhatIsNotInTheChain_AndAppendsIt()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        var options = cut.FindAll("[data-testid='fallback-chain-editor-add'] option");
        options.Select(option => option.GetAttribute("value")).Should().Equal(new[] { "o3" },
            "the three chain models are already in it");
        options[0].TextContent.Trim().Should().Be("o3");

        cut.Find("[data-testid='fallback-chain-editor-add-button']").Click();

        _chainChangeCount.Should().Be(1);
        _chain!.Select(entry => entry.ModelId).Should().Equal("gpt-4o", "claude-3-5", "gpt-4o-mini", "o3");
        _chain![3].Label.Should().Be("o3");
        Announcement(cut).Should().Contain("o3 added to the chain.");
        cut.FindAll("[data-testid='fallback-chain-editor-row']").Should().HaveCount(4);
    }

    [Fact]
    public void AModelCanBePickedFromTheCatalogue()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        cut.Find("[data-testid='fallback-chain-editor-add']").Change("o3");
        cut.Find("[data-testid='fallback-chain-editor-add-button']").Click();

        _chain!.Select(entry => entry.ModelId).Should().Equal("gpt-4o", "claude-3-5", "gpt-4o-mini", "o3");
    }

    [Fact]
    public void AChainThatAlreadyHoldsEveryModel_SaysSo_AndCannotAdd()
    {
        var cut = RenderChain(
            mode: FallbackChainMode.Edit,
            chain:
            [
                new FallbackChainEntry("gpt-4o", "GPT-4o"),
                new FallbackChainEntry("claude-3-5", "Claude 3.5 Sonnet"),
                new FallbackChainEntry("gpt-4o-mini", "GPT-4o mini"),
                new FallbackChainEntry("o3", "o3"),
            ]);

        cut.FindAll("[data-testid='fallback-chain-editor-add'] option").Should().BeEmpty();
        cut.Find("[data-testid='fallback-chain-editor-add-button']").HasAttribute("disabled").Should().BeTrue();
        TextOf(cut, "fallback-chain-editor-note")
            .Should().Be("Every available model is already in the chain.");
    }

    [Fact]
    public void ACatalogueWithNoModels_SaysSo()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit, models: []);

        cut.Find("[data-testid='fallback-chain-editor-add-button']").HasAttribute("disabled").Should().BeTrue();
        TextOf(cut, "fallback-chain-editor-note").Should().Be("No models are available to add.");
    }

    // ── The focus hand-back (WCAG 2.4.3) ─────────────────────────────────────

    [Fact]
    public void RemovingAModel_HandsTheFocusToTheRowThatTookItsPlace()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);
        // Captured before the removal, while the element reference id is in the markup: the row that
        // MOVES into the removed one's place is the element the focus must land on.
        var expected = RowElementId(cut, 2);

        cut.FindAll("[data-testid='fallback-chain-editor-remove']")[1].Click();

        cut.WaitForAssertion(() => FocusCalls().Should().ContainSingle());
        ((ElementReference)FocusCalls()[0].Arguments[0]).Id.Should().Be(expected,
            "a keyboard reader continues where the removed row was instead of starting over");
    }

    [Fact]
    public void RemovingTheLastRow_HandsTheFocusToTheRowAboveIt()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);
        // Removing the tail leaves [gpt-4o, claude-3-5], so the focus walks back to the row that was
        // already above the removed one.
        var expected = RowElementId(cut, 1);

        cut.FindAll("[data-testid='fallback-chain-editor-remove']")[2].Click();

        cut.WaitForAssertion(() => FocusCalls().Should().ContainSingle());
        ((ElementReference)FocusCalls()[0].Arguments[0]).Id.Should().Be(expected,
            "the last row has no successor, so the focus walks back to the one that is left");
    }

    [Fact]
    public void EmptyingTheChain_HandsTheFocusToTheAddControl()
    {
        var cut = RenderChain(
            mode: FallbackChainMode.Edit,
            chain: [new FallbackChainEntry("gpt-4o", "GPT-4o")]);

        var addControl = ElementId(cut, "[data-testid='fallback-chain-editor-add']");

        cut.Find("[data-testid='fallback-chain-editor-remove']").Click();

        cut.WaitForAssertion(() => FocusCalls().Should().ContainSingle());
        ((ElementReference)FocusCalls()[0].Arguments[0]).Id.Should()
            .Be(addControl, "with no rows left the only way forward is adding one");
    }

    [Fact]
    public void EmptyingTheChainWithNoCatalogue_HandsTheFocusToTheEditorHint()
    {
        var cut = RenderChain(
            mode: FallbackChainMode.Edit,
            chain: [new FallbackChainEntry("gpt-4o", "GPT-4o")],
            models: []);

        var hint = cut.Find("[data-testid='fallback-chain-editor-hint']");
        hint.GetAttribute("tabindex").Should().Be("-1", "a paragraph is only focusable when it is told to be");
        var hintId = ElementId(cut, "[data-testid='fallback-chain-editor-hint']");

        cut.Find("[data-testid='fallback-chain-editor-remove']").Click();

        cut.WaitForAssertion(() => FocusCalls().Should().ContainSingle());
        ((ElementReference)FocusCalls()[0].Arguments[0]).Id.Should()
            .Be(hintId, "the add control is disabled too, so the focus stops on the explanation");
    }

    [Fact]
    public void AddingTheLastAvailableModel_HandsTheFocusToTheNewRow()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        cut.Find("[data-testid='fallback-chain-editor-add-button']").Click();

        // o3 was the only model left, so the button the reader was on is now disabled: the focus moves to
        // the row the add created rather than dropping to the body.
        cut.WaitForAssertion(() => FocusCalls().Should().ContainSingle());
        var expected = cut.FindAll(".tm-sortable-list__item")[3].GetAttribute("blazor:elementReference");
        ((ElementReference)FocusCalls()[0].Arguments[0]).Id.Should().Be(expected);
    }

    [Fact]
    public void AddingAModelThatLeavesOthersAvailable_KeepsTheFocusOnTheButton()
    {
        var cut = RenderChain(
            mode: FallbackChainMode.Edit,
            models: [.. Models(), new AiModelInfo("o4-mini", "o4-mini")]);

        cut.Find("[data-testid='fallback-chain-editor-add-button']").Click();

        cut.Find("[data-testid='fallback-chain-editor-add-button']").HasAttribute("disabled").Should().BeFalse(
            "another model is still addable");
        FocusCalls().Should().BeEmpty("an enabled control keeps the focus it has");
    }

    [Fact]
    public void AnEmptyChain_SaysItIsEmpty_AndTheFirstModelCanBeAdded()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit, attempts: []);

        TextOf(cut, "fallback-chain-editor-empty").Should().Be("The chain is empty. Add a model from the list.");
        cut.FindAll("[data-testid='fallback-chain-editor-row']").Should().BeEmpty();

        cut.Find("[data-testid='fallback-chain-editor-add-button']").Click();

        _chain!.Select(entry => entry.ModelId).Should().Equal("gpt-4o");
        cut.FindAll("[data-testid='fallback-chain-editor-row']").Should().HaveCount(1);
    }

    [Fact]
    public void RemovingEveryModel_LeavesTheChainEmpty_InsteadOfFallingBackToTheRun()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        cut.FindAll("[data-testid='fallback-chain-editor-remove']")[0].Click();
        cut.FindAll("[data-testid='fallback-chain-editor-remove']")[0].Click();
        cut.FindAll("[data-testid='fallback-chain-editor-remove']")[0].Click();

        ChainIds(cut).Should().BeEmpty("emptying the chain is an EDIT, so the run must not refill it");
        TextOf(cut, "fallback-chain-editor-empty").Should().Be("The chain is empty. Add a model from the list.");

        // A host render that does not move the parameter keeps the reader's (empty) chain.
        UpdateAsync(cut, (nameof(TmFallbackChain.AvailableModels), Models()));

        ChainIds(cut).Should().BeEmpty();
    }

    [Fact]
    public void AKeyboardReorder_ReportsTheNewOrder()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        // Grab the first row, move it down one place and drop it — the shared list's keyboard path.
        cut.FindAll(".tm-sortable-list__item")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-sortable-list__item")[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.FindAll(".tm-sortable-list__item")[0].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _chainChangeCount.Should().Be(1);
        _chain!.Select(entry => entry.ModelId).Should().Equal("claude-3-5", "gpt-4o", "gpt-4o-mini");
    }

    [Fact]
    public void TheEditMode_OffersThePolicyAsARadioGroup()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit, policy: FallbackResolutionPolicy.Pin);

        var control = cut.FindComponents<TmSegmentedControl<FallbackResolutionPolicy>>().Single();
        control.Instance.Options.Select(option => option.Value).Should().Equal(
            FallbackResolutionPolicy.Pin, FallbackResolutionPolicy.Fallback, FallbackResolutionPolicy.Auto);
        control.Instance.Role.Should().Be(SegmentedControlRole.RadioGroup);

        cut.FindAll("[data-testid='fallback-chain-editor-policy'] button")[2].Click();

        _policy.Should().Be(FallbackResolutionPolicy.Auto);
    }

    [Fact]
    public void TheEditorNamesItsPolicyGroupWithAnInstruction_NotTheReadOutLabel()
    {
        var readOut = RenderChain(policy: FallbackResolutionPolicy.Pin);
        var readOutLabel = TextOf(readOut, "fallback-chain-policy-label");

        var editor = RenderChain(mode: FallbackChainMode.Edit, policy: FallbackResolutionPolicy.Pin);
        var control = editor.FindComponents<TmSegmentedControl<FallbackResolutionPolicy>>().Single();

        control.Instance.Label.Should().Be("Choose how the chain resolves",
            "the editor asks the reader to choose; the run view only names the value it reports");
        control.Instance.Label.Should().NotBe(readOutLabel,
            "one noun phrase reused as a group label makes the edit mode read as a second read-out");
        readOutLabel.Should().Be("Resolution policy");
    }

    [Fact]
    public void APinnedPolicy_WinsOnTheNextParameterSet()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit, policy: FallbackResolutionPolicy.Pin);

        cut.FindAll("[data-testid='fallback-chain-editor-policy'] button")[1].Click();
        _policy.Should().Be(FallbackResolutionPolicy.Fallback);

        UpdateAsync(cut, (nameof(TmFallbackChain.Policy), FallbackResolutionPolicy.Auto));

        cut.FindComponents<TmSegmentedControl<FallbackResolutionPolicy>>().Single()
            .Instance.Value.Should().Be(FallbackResolutionPolicy.Auto);
    }

    // ── Hooks ────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRootCarriesTheHostHooks()
    {
        var cut = Render<TmFallbackChain>(parameters =>
        {
            parameters.Add(component => component.Attempts, Attempts());
            parameters.Add(component => component.Class, "my-chain");
            parameters.Add(component => component.DataTestId, "chain-under-test");
            parameters.Add(component => component.TestIdPrefix, "carrier");
            parameters.AddUnmatched("data-scenario", "review");
        });

        var root = cut.Find("[data-testid='chain-under-test']");
        root.ClassList.Should().Contain("tm-ai-fallback");
        root.ClassList.Should().Contain("my-chain");
        root.GetAttribute("data-scenario").Should().Be("review");
        root.GetAttribute("data-mode").Should().Be("view");
        cut.FindAll("[data-testid='carrier-fallback-chain-node']").Should().HaveCount(3);
    }

    [Fact]
    public void TheRunViewHasOnePoliteLiveRegion()
    {
        var cut = RenderChain();

        cut.FindAll("[aria-live]").Should().HaveCount(1);
        cut.Find("[aria-live]").GetAttribute("aria-live").Should().Be("polite");
        cut.Find("[aria-live]").GetAttribute("role").Should().Be("status");
        cut.Find("[aria-live]").GetAttribute("data-testid").Should().Be("fallback-chain-announcement");
    }

    [Fact]
    public void TheEditorKeepsOneRegionForItsOwnAnnouncements()
    {
        var cut = RenderChain(mode: FallbackChainMode.Edit);

        var announcement = cut.Find("[data-testid='fallback-chain-announcement']");
        announcement.GetAttribute("aria-live").Should().Be("polite");
        announcement.GetAttribute("role").Should().Be("status");
        cut.FindAll("[aria-live]").Should().HaveCount(2,
            "the shared sortable list narrates a REORDER in its own region; this surface announces an add and a "
            + "remove, so the two regions never speak about the same event");
    }
}
