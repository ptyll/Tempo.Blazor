using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 6 — <c>TmMemoryPanel</c> (Tempo.Blazor.AI): what an agent remembers, grouped by category, with a
/// master switch, inline editing, pause / resume, forgetting that can be undone, the review of what the agent proposes
/// to remember, a search and a filter, and bulk actions over a selection.
/// <para>
/// The facts read behaviour: a group is a real disclosure that keeps the focus, a row reports its status as a WORD plus
/// an icon (never a colour alone), an edit commits on Enter and rolls back on Escape with the focus handed back to the
/// Edit button, a forgotten memory is recoverable for the undo window and is reported to the host exactly once when that
/// window closes, a callback that is still running puts the row in a busy state and one that throws prints a localized
/// error instead of losing the change, and the surface owns exactly ONE polite live region.
/// </para>
/// </summary>
public class TmMemoryPanelTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private readonly FakeTimeProvider _clock = new(Ten);

    private bool? _enabled;
    private MemoryItem? _edited;
    private MemoryItem? _paused;
    private MemoryItem? _resumed;
    private MemoryItem? _forgotten;
    private MemoryReview? _review;
    private MemoryBulkActionRequest? _bulk;
    private string? _filter;
    private MemoryStatusFilter? _statusFilter;
    private IReadOnlyList<string>? _collapsed;
    private Func<MemoryItem, Task>? _pauseHandler;
    private Func<MemoryItem, Task>? _editHandler;
    private int _forgetCount;

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<MemoryItem> Items() =>
    [
        new("m-1", "Preferences", "Prefers metric units and 24-hour time.")
        {
            Confidence = 0.82,
            Source = "Support ticket #4211",
            CreatedAt = Ten.AddDays(-30),
            UpdatedAt = Ten.AddDays(-2),
        },
        new("m-2", "Preferences", "Works in the CET time zone.")
        {
            Status = MemoryItemStatus.Paused,
            CreatedAt = Ten.AddDays(-20),
        },
        new("m-3", "People", "Reports to Dana Kral.")
        {
            Status = MemoryItemStatus.PendingReview,
            Confidence = 0.61,
            CreatedAt = Ten.AddDays(-1),
        },
        new("m-4", "Security", "Never sends mail without approval.")
        {
            CreatedAt = Ten.AddDays(-90),
        },
    ];

    private IRenderedComponent<TmMemoryPanel> RenderPanel(
        IReadOnlyList<MemoryItem>? items = null,
        bool enabled = true,
        string filterText = "",
        MemoryStatusFilter statusFilter = MemoryStatusFilter.All,
        bool selectable = true,
        MemoryForgetMode forgetMode = MemoryForgetMode.UndoWindow,
        TimeSpan? undoWindow = null,
        IReadOnlyList<string>? collapsed = null,
        bool showTitle = true,
        string? title = null,
        int headingLevel = 3,
        CultureInfo? culture = null)
    {
        _enabled = null;
        _edited = null;
        _paused = null;
        _resumed = null;
        _forgotten = null;
        _review = null;
        _bulk = null;
        _filter = null;
        _statusFilter = null;
        _collapsed = null;
        _forgetCount = 0;

        return Render<TmMemoryPanel>(parameters =>
        {
            parameters.Add(component => component.Items, items ?? Items());
            parameters.Add(component => component.Enabled, enabled);
            parameters.Add(component => component.FilterText, filterText);
            parameters.Add(component => component.StatusFilter, statusFilter);
            parameters.Add(component => component.Selectable, selectable);
            parameters.Add(component => component.ForgetMode, forgetMode);
            parameters.Add(component => component.UndoWindow, undoWindow ?? TimeSpan.FromSeconds(5));
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.Culture, culture ?? English);
            parameters.Add(component => component.TimeProvider, _clock);
            parameters.Add(component => component.EnabledChanged, EventCallback.Factory.Create<bool>(this,
                next => _enabled = next));
            parameters.Add(component => component.FilterTextChanged, EventCallback.Factory.Create<string>(this,
                next => _filter = next));
            parameters.Add(component => component.StatusFilterChanged, EventCallback.Factory.Create<MemoryStatusFilter>(
                this, next => _statusFilter = next));
            parameters.Add(component => component.CollapsedCategoriesChanged,
                EventCallback.Factory.Create<IReadOnlyList<string>>(this, next => _collapsed = next));
            parameters.Add(component => component.OnEdit, EventCallback.Factory.Create<MemoryItem>(this,
                async (MemoryItem item) =>
                {
                    _edited = item;
                    if (_editHandler is not null)
                    {
                        await _editHandler(item);
                    }
                }));
            parameters.Add(component => component.OnPause, EventCallback.Factory.Create<MemoryItem>(this,
                async (MemoryItem item) =>
                {
                    _paused = item;
                    if (_pauseHandler is not null)
                    {
                        await _pauseHandler(item);
                    }
                }));
            parameters.Add(component => component.OnResume, EventCallback.Factory.Create<MemoryItem>(this,
                (MemoryItem item) => _resumed = item));
            parameters.Add(component => component.OnForget, EventCallback.Factory.Create<MemoryItem>(this,
                (MemoryItem item) =>
                {
                    _forgotten = item;
                    _forgetCount++;
                }));
            parameters.Add(component => component.OnReview, EventCallback.Factory.Create<MemoryReview>(this,
                (MemoryReview review) => _review = review));
            parameters.Add(component => component.OnBulkAction, EventCallback.Factory.Create<MemoryBulkActionRequest>(this,
                (MemoryBulkActionRequest request) => _bulk = request));

            if (collapsed is not null)
            {
                parameters.Add(component => component.CollapsedCategories, collapsed);
            }

            if (title is not null)
            {
                parameters.Add(component => component.Title, title);
            }
        });
    }

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<TmMemoryPanel> cut)
        => [.. cut.FindAll("[data-testid='memory-item']")];

    private static string StatusOf(IRenderedComponent<TmMemoryPanel> cut, string itemId)
        => cut.Find($"[data-testid='memory-item'][data-item-id='{itemId}']").GetAttribute("data-status") ?? string.Empty;

    private static string GroupOf(IRenderedComponent<TmMemoryPanel> cut, string category)
        => cut.Find($"[data-testid='memory-group'][data-category='{category}']").TextContent;

    private static void Click(IRenderedComponent<TmMemoryPanel> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").Click();

    private static void ClickIn(IRenderedComponent<TmMemoryPanel> cut, string rowSelector, string testId)
        => cut.Find($"{rowSelector} [data-testid='{testId}']").Click();

    // ── Groups ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheMemories_AreGroupedByCategory_InCategoryOrder_WithCounts()
    {
        var cut = RenderPanel();

        var groups = cut.FindAll("[data-testid='memory-group']");
        groups.Select(group => group.GetAttribute("data-category"))
            .Should().Equal("People", "Preferences", "Security");
        groups.Select(group => group.QuerySelector("[data-testid='memory-group-count']")!.TextContent)
            .Should().Equal("1", "2", "1");
        groups[1].TextContent.Should().Contain("Preferences");
        Rows(cut).Should().HaveCount(4);
    }

    [Fact]
    public void AMemoryWithoutACategory_IsGroupedUnderTheLocalizedName()
    {
        var cut = RenderPanel(items:
        [
            new MemoryItem("m-1", "", "Prefers metric units.") { CreatedAt = Ten },
            new MemoryItem("m-2", "   ", "Works in CET.") { CreatedAt = Ten },
        ]);

        var group = cut.FindAll("[data-testid='memory-group']").Should().ContainSingle().Subject;
        group.TextContent.Should().Contain("Uncategorized");
        group.QuerySelector("[data-testid='memory-group-count']")!.TextContent.Should().Be("2",
            "two uncategorised memories are one group, not two identical headings");
    }

    [Fact]
    public void AGroup_IsADisclosure_ThatKeepsTheFocusOnItsButton()
    {
        var cut = RenderPanel();

        var toggle = cut.Find("[data-testid='memory-group'][data-category='Preferences'] [data-testid='memory-group-toggle']");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        var listId = toggle.GetAttribute("aria-controls");
        listId.Should().NotBeNullOrEmpty();
        cut.Find("#" + listId).Should().NotBeNull();

        toggle.Click();

        cut.Find("[data-testid='memory-group'][data-category='Preferences'] [data-testid='memory-group-toggle']")
            .GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("#" + listId).Should().BeEmpty("a collapsed group keeps nothing in the DOM to read");
        _collapsed.Should().Equal("Preferences");
        _forgetCount.Should().Be(0, "nothing else was reported by collapsing a group");
    }

    [Fact]
    public void AHostCollapsedGroup_StartsCollapsed()
    {
        var cut = RenderPanel(collapsed: ["Security"]);

        cut.Find("[data-testid='memory-group'][data-category='Security'] [data-testid='memory-group-toggle']")
            .GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='memory-item'][data-item-id='m-4']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-item']").Should().HaveCount(3);
    }

    // ── A row ────────────────────────────────────────────────────────────────

    [Fact]
    public void ARow_StatesItsStatusInWords_NotInColour()
    {
        var cut = RenderPanel();

        var active = cut.Find("[data-testid='memory-item'][data-item-id='m-1']");
        active.QuerySelector("[data-testid='memory-item-text']")!.TextContent
            .Should().Be("Prefers metric units and 24-hour time.");
        active.QuerySelector("[data-testid='memory-item-status']")!.TextContent.Should().Contain("Active");
        active.QuerySelector("[data-testid='memory-item-status'] [data-testid='memory-item-status-icon']")!
            .GetAttribute("aria-hidden").Should().Be("true");

        cut.Find("[data-testid='memory-item'][data-item-id='m-2'] [data-testid='memory-item-status']")
            .TextContent.Should().Contain("Paused");
        cut.Find("[data-testid='memory-item'][data-item-id='m-3'] [data-testid='memory-item-status']")
            .TextContent.Should().Contain("To review");
        StatusOf(cut, "m-2").Should().Be("paused");
        StatusOf(cut, "m-3").Should().Be("pending-review");
    }

    [Fact]
    public void ARow_ReportsItsConfidence_ItsSourceAndWhenItWasUpdated()
    {
        var cut = RenderPanel();

        var row = cut.Find("[data-testid='memory-item'][data-item-id='m-1']");
        row.QuerySelector("[data-testid='memory-item-confidence']")!.TextContent
            .Should().Be("Confidence 82%");
        row.QuerySelector("[data-testid='memory-item-source']")!.TextContent
            .Should().Be("From: Support ticket #4211");
        row.QuerySelector("[data-testid='memory-item-updated']")!.TextContent
            .Should().Be("Updated " + Ten.AddDays(-2).ToLocalTime().ToString("g", English),
                "the bound culture formats the timestamp");
    }

    [Fact]
    public void AMemoryThatReportedNoConfidenceAndNoSource_RendersNeither()
    {
        var cut = RenderPanel();

        var row = cut.Find("[data-testid='memory-item'][data-item-id='m-4']");
        row.QuerySelector("[data-testid='memory-item-confidence']").Should().BeNull();
        row.QuerySelector("[data-testid='memory-item-source']").Should().BeNull();
        row.QuerySelector("[data-testid='memory-item-updated']").Should().BeNull(
            "the host never updated it and no timestamp was invented");
    }

    [Fact]
    public void TheRowTimestamp_FollowsTheBoundCulture()
    {
        var cut = RenderPanel(culture: CultureInfo.GetCultureInfo("cs-CZ"));

        var czech = CultureInfo.GetCultureInfo("cs-CZ");
        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-updated']")
            .TextContent.Should().Be("Updated " + Ten.AddDays(-2).ToLocalTime().ToString("g", czech));
    }

    // ── The master switch ────────────────────────────────────────────────────

    [Fact]
    public void TheMasterSwitch_IsANativeSwitch_ThatReportsItsState()
    {
        var cut = RenderPanel(enabled: true);

        var input = cut.Find("[data-testid='memory-enabled'] input");
        input.GetAttribute("role").Should().Be("switch");
        input.GetAttribute("aria-checked").Should().Be("true");

        input.Change(false);

        _enabled.Should().BeFalse();
        cut.Find("[data-testid='memory-panel']").GetAttribute("data-enabled").Should().Be("false");
        cut.Find("[data-testid='memory-off-note']").TextContent
            .Should().Be("Memory is off. The agent neither stores new memories nor uses the stored ones.");
    }

    [Fact]
    public void WithMemoryOff_TheMemoriesStayReadable_ButNoActionIsOffered()
    {
        var cut = RenderPanel(enabled: false);

        Rows(cut).Should().HaveCount(4, "what the agent already knows is still worth reading");
        cut.FindAll("[data-testid='memory-item-edit']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-item-pause']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-item-forget']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-item-select']").Should().BeEmpty();
        cut.Find("[data-testid='memory-enabled'] input").HasAttribute("disabled").Should().BeFalse(
            "the reader must be able to turn memory back on");
    }

    [Fact]
    public void AHostThatReRendersTheSameSwitchValue_CannotUndoTheReadersPick()
    {
        var cut = RenderPanel(enabled: true);

        cut.Find("[data-testid='memory-enabled'] input").Change(false);
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmMemoryPanel.Enabled)] = true,
                [nameof(TmMemoryPanel.Items)] = Items(),
            })));

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-enabled").Should().Be("false",
            "the reader wins until the parameter actually MOVES");
    }

    [Fact]
    public void WhenTheParameterMoves_TheHostWins()
    {
        var cut = RenderPanel(enabled: true);

        var items = Items();
        cut.Find("[data-testid='memory-enabled'] input").Change(false);
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmMemoryPanel.Enabled)] = false,
                [nameof(TmMemoryPanel.Items)] = items,
            })));
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmMemoryPanel.Enabled)] = true,
                [nameof(TmMemoryPanel.Items)] = items,
            })));

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-enabled").Should().Be("true");
    }

    // ── Inline editing ───────────────────────────────────────────────────────

    [Fact]
    public void EditingARow_OpensATextareaWithItsText_AndTheFocusMovesIntoIt()
    {
        var cut = RenderPanel();
        var focusBefore = JSInterop.Invocations[FocusInvocation].Count;

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");

        var box = cut.Find("[data-testid='memory-item-edit-box']");
        box.TagName.Should().Be("TEXTAREA");
        box.GetAttribute("aria-label").Should().Be("Edit the memory");
        box.TextContent.Should().Be("Prefers metric units and 24-hour time.");
        JSInterop.Invocations[FocusInvocation].Count.Should().BeGreaterThan(focusBefore,
            "typing must start where the reader already is");
    }

    [Fact]
    public void Enter_CommitsTheEdit_AndKeepsTheFocusInTheRow()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").Input("Prefers metric units only.");
        var focusBefore = JSInterop.Invocations[FocusInvocation].Count;

        cut.Find("[data-testid='memory-item-edit-box']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _edited.Should().NotBeNull();
        _edited!.Id.Should().Be("m-1");
        _edited.Text.Should().Be("Prefers metric units only.");
        cut.FindAll("[data-testid='memory-item-edit-box']").Should().BeEmpty();
        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-text']").TextContent
            .Should().Be("Prefers metric units only.", "the reader sees the change before the host round-trip");
        JSInterop.Invocations[FocusInvocation].Count.Should().Be(focusBefore + 1,
            "the control that replaced the textarea never drops the focus onto the document");
        cut.Find("[data-testid='memory-item'][data-item-id='m-1']").GetAttribute("tabindex").Should().Be("-1",
            "the saved row is the focus target, because a focused BUTTON would be activated by the Enter keysequence still in flight");
        cut.Find("[data-testid='memory-announcement']").TextContent.Should().Be("Updated the memory.");
    }

    [Fact]
    public void Escape_RollsTheEditBack_AndReportsNothing()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").Input("Something else entirely.");
        cut.Find("[data-testid='memory-item-edit-box']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _edited.Should().BeNull();
        cut.FindAll("[data-testid='memory-item-edit-box']").Should().BeEmpty();
        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-text']").TextContent
            .Should().Be("Prefers metric units and 24-hour time.");
    }

    [Fact]
    public void ShiftEnter_InsertsANewLineInsteadOfCommitting()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        _edited.Should().BeNull("a multi-line memory is written, not sent");
        cut.Find("[data-testid='memory-item-edit-box']").Should().NotBeNull();
    }

    [Fact]
    public void AnUnchangedEdit_ClosesWithoutReportingASecondTime()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _edited.Should().BeNull("nothing changed, so nothing is saved");
        cut.FindAll("[data-testid='memory-item-edit-box']").Should().BeEmpty();
    }

    [Fact]
    public void AFailedEdit_KeepsTheDraftInTheEditor_AndPrintsTheError()
    {
        _editHandler = _ => throw new InvalidOperationException("Store offline");

        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").Input("Prefers metric units only.");
        cut.Find("[data-testid='memory-item-edit-box']").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("[data-testid='memory-error']").TextContent
            .Should().Be("The change could not be saved: Store offline");
        cut.FindAll("[data-testid='memory-item-edit-box']").Should().ContainSingle(
            "a failed save must not throw the reader's work away");
        cut.Find("[data-testid='memory-item-edit-box']").TextContent.Should().Be("Prefers metric units only.");

        cut.Find("[data-testid='memory-item-edit-box']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-text']")
            .TextContent.Should().Be("Prefers metric units and 24-hour time.", "the stored text is untouched");
    }

    [Fact]
    public void TheSaveAndCancelButtons_AreTheTapAlternative()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").Input("Prefers metric units.");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-save");
        _edited!.Text.Should().Be("Prefers metric units.");

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").Input("Never sends mail.");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-cancel");
        cut.Find("[data-testid='memory-item'][data-item-id='m-4'] [data-testid='memory-item-text']").TextContent
            .Should().Be("Never sends mail without approval.");
    }

    [Fact]
    public void OpeningASecondEdit_DiscardsTheFirstDraft()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").Input("Draft one");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-edit");

        cut.FindAll("[data-testid='memory-item-edit-box']").Should().ContainSingle();
        _edited.Should().BeNull("closing a draft by opening another one is not a save");
    }

    // ── Pause and resume ─────────────────────────────────────────────────────

    [Fact]
    public void PausingAndResuming_AreReported_AndTheRowFollowsImmediately()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-pause");

        _paused.Should().NotBeNull();
        _paused!.Id.Should().Be("m-1");
        StatusOf(cut, "m-1").Should().Be("paused");
        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-pause']").TextContent
            .Should().Be("Resume");

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-pause");

        _resumed!.Id.Should().Be("m-1");
        StatusOf(cut, "m-1").Should().Be("active");
        cut.Find("[data-testid='memory-announcement']").TextContent.Should().Be("Resumed the memory.");
    }

    [Fact]
    public void APendingReviewRow_OffersApproveAndReject_AndNoPause()
    {
        var cut = RenderPanel();

        var row = "[data-testid='memory-item'][data-item-id='m-3']";
        cut.FindAll($"{row} [data-testid='memory-item-approve']").Should().ContainSingle();
        cut.FindAll($"{row} [data-testid='memory-item-reject']").Should().ContainSingle();
        cut.FindAll($"{row} [data-testid='memory-item-pause']").Should().BeEmpty(
            "a memory the agent proposed cannot be paused before it is accepted");
    }

    [Fact]
    public void ApprovingAReview_ReportsIt_AndTheRowBecomesActive()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-3']", "memory-item-approve");

        _review!.Item.Id.Should().Be("m-3");
        _review.Decision.Should().Be(MemoryReviewDecision.Approve);
        StatusOf(cut, "m-3").Should().Be("active");
        cut.FindAll("[data-testid='memory-item'][data-item-id='m-3'] [data-testid='memory-item-approve']")
            .Should().BeEmpty();
        cut.Find("[data-testid='memory-announcement']").TextContent.Should().Be("Approved the memory.");
    }

    [Fact]
    public void RejectingAReview_ReportsIt_AndTheRowIsForgotten()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-3']", "memory-item-reject");

        _review!.Decision.Should().Be(MemoryReviewDecision.Reject);
        StatusOf(cut, "m-3").Should().Be("forgotten");
        cut.Find("[data-testid='memory-announcement']").TextContent.Should().Be("Rejected the memory.");
    }

    // ── Forgetting with an undo window ───────────────────────────────────────

    [Fact]
    public void ForgettingWaitsForTheUndoWindow_BeforeTheHostIsTold()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");

        _forgotten.Should().BeNull("the memory is not gone until the reader can no longer take it back");
        cut.FindAll("[data-testid='memory-item'][data-item-id='m-1']").Should().BeEmpty();
        cut.Find("[data-testid='memory-undo']").Should().NotBeNull();
        cut.Find("[data-testid='memory-undo-text']").TextContent
            .Should().Contain("Prefers metric units and 24-hour time.");
        cut.Find("[data-testid='memory-undo-timer']").TextContent.Should().Be("5 s");

        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(2)));
        cut.Find("[data-testid='memory-undo-timer']").TextContent.Should().Be("3 s");

        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(3)));

        _forgotten.Should().NotBeNull();
        _forgotten!.Id.Should().Be("m-1");
        _forgetCount.Should().Be(1, "the host is told exactly once");
        cut.FindAll("[data-testid='memory-undo']").Should().BeEmpty();
    }

    [Fact]
    public void UndoBringsTheMemoryBack_AndTheHostIsNeverTold()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(2)));
        Click(cut, "memory-undo-action");

        cut.Find("[data-testid='memory-item'][data-item-id='m-1']").Should().NotBeNull();
        cut.FindAll("[data-testid='memory-undo']").Should().BeEmpty();
        _forgetCount.Should().Be(0);

        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(30)));
        _forgetCount.Should().Be(0, "a restored memory is never reported as forgotten");
    }

    [Fact]
    public void AnImmediateForgetMode_TellsTheHostAtOnce_AndOffersNoUndo()
    {
        var cut = RenderPanel(forgetMode: MemoryForgetMode.Immediate);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");

        _forgotten!.Id.Should().Be("m-1");
        cut.FindAll("[data-testid='memory-undo']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-item'][data-item-id='m-1']").Should().BeEmpty();
    }

    [Fact]
    public void AZeroLengthUndoWindow_BehavesLikeImmediate_SoNothingIsSilentlyLost()
    {
        var cut = RenderPanel(undoWindow: TimeSpan.Zero);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");

        _forgotten!.Id.Should().Be("m-1");
        cut.FindAll("[data-testid='memory-undo']").Should().BeEmpty();
    }

    [Fact]
    public void ASecondForget_CommitsTheFirstOneImmediately()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        _forgotten.Should().BeNull();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-forget");

        _forgotten!.Id.Should().Be("m-1", "the pending window is closed before the next one opens");
        _forgetCount.Should().Be(1);
        cut.Find("[data-testid='memory-undo-text']").TextContent
            .Should().Contain("Never sends mail without approval.");
    }

    [Fact]
    public async Task DisposingThePanel_CommitsAPendingForget_SoTheMemoryIsNotLostSilently()
    {
        // Navigating away closes the undo window the reader can no longer reach: the confirmed removal must reach the
        // host instead of quietly coming back on the next load.
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        _forgotten.Should().BeNull("the undo window is still open");

        await DisposeComponentsAsync();

        _forgotten.Should().NotBeNull("leaving the page still forgets the memory the reader removed");
        _forgotten!.Id.Should().Be("m-1");
        _forgetCount.Should().Be(1, "the host is told exactly once");
    }

    [Fact]
    public async Task DisposingThePanel_AfterAnUndo_ReportsNothing()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        Click(cut, "memory-undo-action");

        await DisposeComponentsAsync();

        _forgetCount.Should().Be(0, "a restored memory is never reported as forgotten");
    }

    [Fact]
    public async Task DisposingThePanel_AfterTheWindowClosed_ReportsNothingTwice()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(5)));
        _forgetCount.Should().Be(1);

        await DisposeComponentsAsync();

        _forgetCount.Should().Be(1, "a closed window has nothing left to commit");
    }

    // ── Search and filter ────────────────────────────────────────────────────

    [Fact]
    public void TheSearch_NarrowsTheRowsAndHidesTheEmptyGroups()
    {
        var cut = RenderPanel();

        var search = cut.Find("[data-testid='memory-search']");
        search.GetAttribute("type").Should().Be("search");
        search.GetAttribute("aria-label").Should().Be("Search memory");
        cut.Find("[data-testid='memory-search']").Input("mail");

        cut.FindAll("[data-testid='memory-item']").Should().ContainSingle();
        cut.Find("[data-testid='memory-item']").GetAttribute("data-item-id").Should().Be("m-4");
        cut.FindAll("[data-testid='memory-group']").Should().ContainSingle();
        cut.Find("[data-testid='memory-announcement']").TextContent
            .Should().Be("Showing 1 memory of 4.");
        _filter.Should().Be("mail", "the search is two-way bound");
    }

    [Fact]
    public void TheSearch_ReadsTheCategoryAndTheSourceToo()
    {
        var cut = RenderPanel();
        cut.Find("[data-testid='memory-search']").Input("4211");

        cut.FindAll("[data-testid='memory-item']").Should().ContainSingle()
            .Which.GetAttribute("data-item-id").Should().Be("m-1");

        cut.Find("[data-testid='memory-search']").Input("Preferences");
        cut.FindAll("[data-testid='memory-item']").Should().HaveCount(2);
    }

    [Fact]
    public void TheStatusFilter_KeepsOnlyTheMatchingStatus()
    {
        var cut = RenderPanel(statusFilter: MemoryStatusFilter.Active);

        cut.FindAll("[data-testid='memory-item']").Should().HaveCount(2);
        cut.FindAll("[data-testid='memory-item'][data-status='active']").Should().HaveCount(2);

        cut.Find("[data-testid='memory-status-filter'] [role='radio'][data-value='pending-review']").Click();

        _statusFilter.Should().Be(MemoryStatusFilter.PendingReview);
        cut.FindAll("[data-testid='memory-item']").Should().ContainSingle()
            .Which.GetAttribute("data-item-id").Should().Be("m-3");
    }

    [Fact]
    public void TheStatusFilter_OffersEveryStatusAndAll()
    {
        var cut = RenderPanel();

        cut.FindAll("[data-testid='memory-status-filter'] [role='radio']")
            .Select(radio => radio.GetAttribute("data-value"))
            .Should().Equal("all", "active", "paused", "pending-review", "forgotten");
    }

    [Fact]
    public void AnEmptyPanel_ReportsThatNothingIsStored()
    {
        var cut = RenderPanel(items: []);

        cut.Find("[data-testid='memory-empty']").TextContent.Should().Be("No memory yet.");
        cut.FindAll("[data-testid='memory-group']").Should().BeEmpty();
    }

    [Fact]
    public void AFilterWithNoHits_ReportsThatNothingMatches()
    {
        var cut = RenderPanel();
        cut.Find("[data-testid='memory-search']").Input("zzz");

        cut.Find("[data-testid='memory-empty']").TextContent
            .Should().Be("No memory matches the search or the filter.");
        cut.FindAll("[data-testid='memory-group']").Should().BeEmpty();
    }

    // ── Bulk selection and actions ───────────────────────────────────────────

    [Fact]
    public void SelectingRows_ShowsTheBulkBar_AndReportsTheSelection()
    {
        var cut = RenderPanel();

        cut.FindAll("[data-testid='memory-bulk']").Should().BeEmpty("nothing is selected at load");

        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-select']").Change(true);
        cut.Find("[data-testid='memory-item'][data-item-id='m-2'] [data-testid='memory-item-select']").Change(true);

        cut.Find("[data-testid='memory-bulk']").Should().NotBeNull();
        cut.Find("[data-testid='memory-bulk-count']").TextContent.Should().Be("2 selected");

        Click(cut, "memory-bulk-pause");

        _bulk.Should().NotBeNull();
        _bulk!.Action.Should().Be(MemoryBulkAction.Pause);
        _bulk.Items.Select(item => item.Id).Should().Equal("m-1", "m-2");
        StatusOf(cut, "m-1").Should().Be("paused");
        cut.FindAll("[data-testid='memory-bulk']").Should().BeEmpty("the acted-on selection is cleared");
    }

    [Fact]
    public void TheGroupCheckbox_SelectsEveryVisibleRowOfItsGroup()
    {
        var cut = RenderPanel();

        cut.Find("[data-testid='memory-group'][data-category='Preferences'] [data-testid='memory-group-select']")
            .Change(true);

        cut.Find("[data-testid='memory-bulk-count']").TextContent.Should().Be("2 selected");

        cut.Find("[data-testid='memory-group'][data-category='Preferences'] [data-testid='memory-group-select']")
            .Change(false);

        cut.FindAll("[data-testid='memory-bulk']").Should().BeEmpty();
    }

    [Fact]
    public void ABulkForget_IsReportedOnce_AndTheRowsLeave()
    {
        var cut = RenderPanel();

        cut.Find("[data-testid='memory-item'][data-item-id='m-2'] [data-testid='memory-item-select']").Change(true);
        cut.Find("[data-testid='memory-item'][data-item-id='m-4'] [data-testid='memory-item-select']").Change(true);
        Click(cut, "memory-bulk-forget");

        _bulk!.Action.Should().Be(MemoryBulkAction.Forget);
        _bulk.Items.Select(item => item.Id).Should().Equal("m-2", "m-4");
        cut.FindAll("[data-testid='memory-item']").Should().HaveCount(2);
        _forgetCount.Should().Be(0, "a bulk action is one request, not one forget per row");
    }

    [Fact]
    public void ASelectableFalsePanel_OffersNoSelectionAtAll()
    {
        var cut = RenderPanel(selectable: false);

        cut.FindAll("[data-testid='memory-item-select']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-group-select']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-bulk']").Should().BeEmpty();
    }

    [Fact]
    public void ASelectionThatTheFilterHides_IsNotActedOn()
    {
        var cut = RenderPanel();

        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-select']").Change(true);
        cut.Find("[data-testid='memory-search']").Input("Security");

        cut.FindAll("[data-testid='memory-bulk']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-bulk-count']").Should().BeEmpty();
    }

    // ── Async callbacks: pending and failure ─────────────────────────────────

    [Fact]
    public void ASlowCallback_PutsTheRowInABusyState_UntilItCompletes()
    {
        var gate = new TaskCompletionSource();
        _pauseHandler = _ => gate.Task;

        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-pause");

        cut.Find("[data-testid='memory-item'] [data-testid='memory-item-busy']").TextContent.Should().Be("Saving…");
        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-pause']")
            .HasAttribute("disabled").Should().BeTrue("a second press must not double the request");
        cut.Find("[data-testid='memory-panel']").GetAttribute("data-busy").Should().Be("true");

        gate.SetResult();
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-busy']")
                .Should().BeEmpty();
            cut.Find("[data-testid='memory-panel']").GetAttribute("data-busy").Should().Be("false");
        });
        StatusOf(cut, "m-1").Should().Be("paused", "the row follows the reported change once it is saved");
    }

    [Fact]
    public void ACallbackThatThrows_PrintsALocalizedError_AndLeavesTheRowAlone()
    {
        _pauseHandler = _ => throw new InvalidOperationException("Disk full");

        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-pause");

        cut.Find("[data-testid='memory-error']").TextContent
            .Should().Be("The change could not be saved: Disk full");
        StatusOf(cut, "m-1").Should().Be("active", "a failed request changes nothing");
        cut.Find("[data-testid='memory-announcement']").TextContent
            .Should().Be("The change could not be saved: Disk full");
        cut.FindAll("[role='status']").Should().ContainSingle("one live region, even while reporting a failure");
    }

    // ── The live region and localization ─────────────────────────────────────

    [Fact]
    public void TheSurfaceOwnsExactlyOnePoliteLiveRegion()
    {
        var cut = RenderPanel();

        var regions = cut.FindAll("[role='status']");
        regions.Should().ContainSingle();
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].GetAttribute("data-testid").Should().Be("memory-announcement");
    }

    [Fact]
    public void EveryUserFacingString_ComesFromTheLocalizer()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-3']", "memory-item-reject");

        cut.Markup.Should().NotMatchRegex("\\[Tm[A-Za-z0-9_]+\\]",
            "a bracket-wrapped key is an unresolved string, never a label a reader should see");
    }

    [Fact]
    public void TheTitle_UsesTheRequestedHeadingLevel_AndTheHostsWords()
    {
        var cut = RenderPanel(headingLevel: 4, title: "What the agent remembers");

        var title = cut.Find("[data-testid='memory-title']");
        title.TagName.Should().Be("H4");
        title.TextContent.Should().Be("What the agent remembers");
    }

    [Fact]
    public void TheHeadingLevel_IsClampedAndCanBeHidden()
    {
        RenderPanel(headingLevel: 1).Find("[data-testid='memory-title']").TagName.Should().Be("H2");
        RenderPanel(showTitle: false).FindAll("[data-testid='memory-title']").Should().BeEmpty();
    }

    [Fact]
    public void ARepeatedAnnouncement_IsStillAnnounced()
    {
        var cut = RenderPanel();

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-pause");
        var first = cut.Find("[data-testid='memory-announcement']").TextContent;

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-pause");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-pause");

        cut.Find("[data-testid='memory-announcement']").TextContent.Replace("\u200B", string.Empty)
            .Should().Be(first, "the same sentence is re-announced through an invisible marker");

        // Keep the static analyzer honest about the helper being used for more than one group.
        GroupOf(cut, "People").Should().Contain("People");
        Rows(cut).Should().HaveCount(4);
    }

    /// <summary>
    /// The presentation rules stay INSIDE the package: every sibling <c>*Presentation</c> helper in
    /// <c>Tempo.Blazor.AI</c> is <c>internal</c>, and a public static class would turn shapes like <c>FilterSlug</c> into a
    /// 2.10.0 API promise no host asked for. The enums a host must name stay public.
    /// </summary>
    [Fact]
    public void ThePresentationHelper_IsInternal_WhileThePresentationEnumsStayPublic()
    {
        var helper = typeof(TmMemoryPanel).Assembly
            .GetType("Tempo.Blazor.Components.AI.MemoryPresentation", throwOnError: false);

        helper.Should().NotBeNull("MemoryPresentation still backs the panel");
        helper!.IsPublic.Should().BeFalse("it is a presentation detail, not a public API promise");
        typeof(MemoryForgetMode).IsPublic.Should().BeTrue("a host names the forget mode it wants");
        typeof(MemoryStatusFilter).IsPublic.Should().BeTrue("a host names the filter it wants");
    }
}
