using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// TDD tests for <c>TmOnboardingChecklist</c> — the setup-guide / progress-tracker checklist
/// (plan 5d396592, Fáze 5). The component is a plain Blazor list (no JS): native checkboxes own
/// the activation, an APG disclosure button owns each group, one polite live region announces the
/// task transitions and a celebration block replaces confetti when the required steps are done.
/// These tests pin the markup contract, the progress grammar, the group state, the dismissal
/// rules and the accessibility behaviour (keyboard, focus move out of a collapsing group, single
/// live region, no duplicate announcements).
/// </summary>
public class TmOnboardingChecklistTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static OnboardingTask Step(
        string id,
        string title,
        bool isDone = false,
        bool isOptional = false,
        string? group = null,
        string? description = null,
        string? actionLabel = null,
        string? actionHref = null,
        Func<OnboardingTask, Task>? onAction = null)
        => new()
        {
            Id = id,
            Title = title,
            IsDone = isDone,
            IsOptional = isOptional,
            Group = group,
            Description = description,
            ActionLabel = actionLabel,
            ActionHref = actionHref,
            OnAction = onAction,
        };

    /// <summary>Seven ungrouped steps, three of them done — the phase's „3 z 7 hotovo“ fixture.</summary>
    private static OnboardingTask[] SevenSteps() =>
    [
        Step("a", "Create a workspace", isDone: true),
        Step("b", "Invite a teammate", isDone: true),
        Step("c", "Connect a data source", isDone: true),
        Step("d", "Add your first dashboard"),
        Step("e", "Set up notifications"),
        Step("f", "Review the security settings"),
        Step("g", "Install the browser extension"),
    ];

    private IRenderedComponent<TmOnboardingChecklist> RenderChecklist(
        IReadOnlyList<OnboardingTask>? tasks = null,
        Action<ComponentParameterCollectionBuilder<TmOnboardingChecklist>>? configure = null)
    {
        return Render<TmOnboardingChecklist>(p =>
        {
            p.Add(x => x.Tasks, tasks ?? SevenSteps());
            configure?.Invoke(p);
        });
    }

    private IReadOnlyList<string> FocusInvocations()
        => JSInterop.Invocations.Where(i => i.Identifier == FocusInvocation)
            .Select(i => ((ElementReference)i.Arguments[0]!).Id).ToList();

    private static string TextOf(IRenderedComponent<TmOnboardingChecklist> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    // ── Root, title and landmarks ───────────────────────────────────────────────

    [Fact]
    public void Root_IsANamedSection_NotALandmarkCollision()
    {
        var cut = RenderChecklist();

        var root = cut.Find("section.tm-onboarding-checklist");
        root.GetAttribute("data-testid").Should().Be("onboarding-checklist");
        root.GetAttribute("aria-label").Should().Be("Onboarding checklist",
            "a checklist without a title still has to be a NAMED region");
        root.HasAttribute("aria-labelledby").Should().BeFalse();
        cut.FindAll("main").Should().BeEmpty("a component never owns the main landmark");
    }

    [Fact]
    public void Title_RendersAHeading_ThatNamesTheRegion()
    {
        var cut = RenderChecklist(configure: p => p.Add(x => x.Title, "Get started"));

        var title = cut.Find("[data-testid='onboarding-checklist-title']");
        title.TextContent.Trim().Should().Be("Get started");
        title.GetAttribute("role").Should().Be("heading");
        title.GetAttribute("aria-level").Should().Be("2");

        var id = title.GetAttribute("id");
        id.Should().NotBeNullOrWhiteSpace();
        cut.Find("section.tm-onboarding-checklist").GetAttribute("aria-labelledby").Should().Be(id,
            "the visible title is the region's accessible name");
        cut.Find("section.tm-onboarding-checklist").HasAttribute("aria-label").Should().BeFalse(
            "the title names the region; two names would fight");
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(3, "3")]
    [InlineData(6, "6")]
    public void HeadingLevel_IsHonoured(int level, string expected)
    {
        var cut = RenderChecklist(configure: p => p
            .Add(x => x.Title, "Get started")
            .Add(x => x.HeadingLevel, level));

        cut.Find("[data-testid='onboarding-checklist-title']").GetAttribute("aria-level").Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "2")]
    [InlineData(7, "2")]
    public void HeadingLevel_OutsideOneToSix_FallsBackToTwo(int level, string expected)
    {
        var cut = RenderChecklist(configure: p => p
            .Add(x => x.Title, "Get started")
            .Add(x => x.HeadingLevel, level));

        cut.Find("[data-testid='onboarding-checklist-title']").GetAttribute("aria-level").Should().Be(expected);
    }

    [Fact]
    public void Root_AppliesTheHostClass_AndSplatsAdditionalAttributes()
    {
        var cut = RenderChecklist(configure: p => p
            .Add(x => x.Class, "my-checklist")
            .AddUnmatched("data-host", "x1"));

        var root = cut.Find("section.tm-onboarding-checklist");
        root.ClassList.Should().Contain("my-checklist");
        root.GetAttribute("data-host").Should().Be("x1");
    }

    // ── Tasks ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EachTask_RendersANativeCheckbox_WhoseLabelIsTheTaskTitle()
    {
        var cut = RenderChecklist();

        var item = cut.Find("[data-testid='onboarding-task-d']");
        var checkbox = item.QuerySelector("input[type='checkbox']")!;
        checkbox.HasAttribute("checked").Should().BeFalse();
        checkbox.GetAttribute("id").Should().NotBeNullOrWhiteSpace();

        var label = item.QuerySelector("label")!;
        label.GetAttribute("for").Should().Be(checkbox.GetAttribute("id"),
            "the checkbox and its label are associated explicitly, so the accessible name is the title");
        label.TextContent.Should().Contain("Add your first dashboard");
        cut.FindAll("[data-testid='onboarding-task-a'] input[checked]").Should().ContainSingle(
            "a done task renders a checked box");
    }

    [Fact]
    public void DoneTask_IsStruckThrough_ByItsModifierClass()
    {
        var cut = RenderChecklist();

        cut.Find("[data-testid='onboarding-task-a']").ClassList.Should().Contain("tm-onboarding-checklist__item--done");
        cut.Find("[data-testid='onboarding-task-d']").ClassList.Should()
            .NotContain("tm-onboarding-checklist__item--done");
        cut.Find("[data-testid='onboarding-task-a'] .tm-onboarding-checklist__task-title").Should().NotBeNull();
    }

    [Fact]
    public void OptionalTask_CarriesALocalizedBadge()
    {
        var cut = RenderChecklist([Step("a", "Read the changelog", isOptional: true)]);

        cut.Find("[data-testid='onboarding-task-a'] .tm-onboarding-checklist__badge").TextContent.Trim()
            .Should().Be("Optional");
        cut.Find("[data-testid='onboarding-task-a']").ClassList
            .Should().Contain("tm-onboarding-checklist__item--optional");
    }

    [Fact]
    public void Description_IsDescribedByTheCheckbox()
    {
        var cut = RenderChecklist([Step("a", "Connect data", description: "Postgres, MSSQL or a CSV file.")]);

        var item = cut.Find("[data-testid='onboarding-task-a']");
        var description = item.QuerySelector(".tm-onboarding-checklist__description")!;
        description.TextContent.Trim().Should().Be("Postgres, MSSQL or a CSV file.");
        item.QuerySelector("input")!.GetAttribute("aria-describedby").Should().Be(description.GetAttribute("id"));
    }

    [Fact]
    public void TaskWithoutDescription_HasNoAriaDescribedBy()
    {
        var cut = RenderChecklist([Step("a", "Connect data")]);

        cut.Find("[data-testid='onboarding-task-a'] input").HasAttribute("aria-describedby").Should().BeFalse();
        cut.FindAll(".tm-onboarding-checklist__description").Should().BeEmpty();
    }

    [Fact]
    public void TogglingATask_FlipsItsState_AndRaisesOnTaskToggle()
    {
        var toggled = new List<OnboardingTask>();
        var tasks = SevenSteps();
        var cut = RenderChecklist(tasks, p => p
            .Add(x => x.OnTaskToggle, (OnboardingTask t) => toggled.Add(t)));

        cut.Find("[data-testid='onboarding-task-d'] input").Change(true);

        toggled.Should().ContainSingle();
        toggled[0].Id.Should().Be("d");
        tasks.Single(t => t.Id == "d").IsDone.Should().BeTrue(
            "the checklist is usable without a bound handler, so it writes the instance it was given");
        cut.Find("[data-testid='onboarding-task-d'] input").HasAttribute("checked").Should().BeTrue();
        cut.Find("[data-testid='onboarding-task-d']").ClassList
            .Should().Contain("tm-onboarding-checklist__item--done");
    }

    [Fact]
    public void UntogglingATask_RaisesTheCallbackToo()
    {
        var toggled = new List<OnboardingTask>();
        var tasks = SevenSteps();
        var cut = RenderChecklist(tasks, p => p
            .Add(x => x.OnTaskToggle, (OnboardingTask t) => toggled.Add(t)));

        cut.Find("[data-testid='onboarding-task-a'] input").Change(false);

        toggled.Should().ContainSingle().Which.Id.Should().Be("a");
        tasks.Single(t => t.Id == "a").IsDone.Should().BeFalse();
    }

    [Fact]
    public void EmptyTaskList_RendersTheLocalizedEmptyState_AndNoProgress()
    {
        var cut = RenderChecklist([]);

        cut.Find("[data-testid='onboarding-checklist-empty']").TextContent.Trim().Should().Be("No steps yet.");
        cut.FindAll("[data-testid='onboarding-checklist-progress']").Should().BeEmpty();
        cut.FindAll("[data-testid='onboarding-checklist-celebration']").Should().BeEmpty();
    }

    // ── Progress ────────────────────────────────────────────────────────────────

    [Fact]
    public void Progress_ShowsTheCountedSentence_AndABar()
    {
        var cut = RenderChecklist();

        var progress = cut.Find("[data-testid='onboarding-checklist-progress']");
        progress.QuerySelector(".tm-progress-bar")!.GetAttribute("aria-valuenow").Should().Be("43",
            "a whole percentage — the bar forwards the value verbatim as aria-valuenow");
        progress.QuerySelector(".tm-progress-bar")!.GetAttribute("aria-valuemax").Should().Be("100");
        progress.QuerySelector(".tm-progress-bar")!.GetAttribute("aria-label").Should().Be("Progress",
            "the bar is named by its role, not by the sentence next to it (no duplicate reading)");
        TextOf(cut, "onboarding-checklist-progress-text").Should().Be("3 of 7 tasks done");
    }

    [Fact]
    public void Progress_UsesTheGrammaticalPluralOfTheCount()
    {
        // The plural family keys on the TOTAL (what the noun agrees with in cs/sk/fr), the done
        // count is the extra argument: "1 of 1 task done", "3 of 7 tasks done".
        var single = RenderChecklist([Step("a", "First step")]);
        TextOf(single, "onboarding-checklist-progress-text").Should().Be("0 of 1 task done");
        single.Find("[data-testid='onboarding-task-a'] input").Change(true);
        TextOf(single, "onboarding-checklist-progress-text").Should().Be("1 of 1 task done");
    }

    [Fact]
    public void CzechProgress_UsesTheCzechPluralForms()
    {
        UseCzechLocalization();

        var three = RenderChecklist([Step("a", "A", isDone: true), Step("b", "B"), Step("c", "C")]);
        TextOf(three, "onboarding-checklist-progress-text").Should().Be("1 ze 3 úkolů hotovo");

        var seven = RenderChecklist(SevenSteps());
        TextOf(seven, "onboarding-checklist-progress-text").Should().Be("3 z 7 úkolů hotovo");
    }

    [Fact]
    public void Progress_CanBeHidden()
    {
        var cut = RenderChecklist(configure: p => p.Add(x => x.ShowProgress, false));

        cut.FindAll("[data-testid='onboarding-checklist-progress']").Should().BeEmpty();
        cut.Find("[data-testid='onboarding-task-a'] [type='checkbox']").Should().NotBeNull(
            "hiding the progress block hides only the progress");
    }

    // ── Groups (APG disclosure) ─────────────────────────────────────────────────

    private static OnboardingTask[] Grouped() =>
    [
        Step("a", "Create a workspace", group: "Basics"),
        Step("b", "Invite a teammate", group: "Basics", isDone: true),
        Step("c", "Connect a data source", group: "Data"),
        Step("d", "Install the extension", group: "Data"),
        Step("e", "Read the changelog"),
    ];

    [Fact]
    public void Groups_RenderAsADisclosure_ExpandedByDefault()
    {
        var cut = RenderChecklist(Grouped());

        var toggle = cut.Find("[data-testid='onboarding-group-toggle-0']");
        toggle.TagName.Should().Be("BUTTON", "a native button is activated by Enter/Space exactly once");
        toggle.GetAttribute("type").Should().Be("button");
        toggle.GetAttribute("aria-expanded").Should().Be("true");

        var body = cut.Find("[data-testid='onboarding-group-body-0']");
        toggle.GetAttribute("aria-controls").Should().Be(body.GetAttribute("id"));
        body.HasAttribute("hidden").Should().BeFalse();
        body.QuerySelectorAll("input[type='checkbox']").Count.Should().Be(2);
        toggle.TextContent.Should().Contain("Basics");
    }

    [Fact]
    public void ClickingAGroupHeader_CollapsesIt_AndExpandsItAgain()
    {
        var cut = RenderChecklist(Grouped());

        cut.Find("[data-testid='onboarding-group-toggle-0']").Click();
        cut.Find("[data-testid='onboarding-group-toggle-0']").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("[data-testid='onboarding-group-body-0']").HasAttribute("hidden").Should().BeTrue(
            "the body stays in the DOM so aria-controls always resolves, but hidden removes it from AT and the tab order");
        cut.FindAll("[data-testid='onboarding-group-body-0'] input").Count.Should().Be(2);

        cut.Find("[data-testid='onboarding-group-toggle-0']").Click();
        cut.Find("[data-testid='onboarding-group-toggle-0']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='onboarding-group-body-0']").HasAttribute("hidden").Should().BeFalse();
    }

    [Fact]
    public void GroupHeader_SaysHowManyOfItsStepsAreDone()
    {
        var cut = RenderChecklist(Grouped());

        cut.Find("[data-testid='onboarding-group-toggle-0'] .tm-onboarding-checklist__group-count")
            .TextContent.Trim().Should().Be("1 of 2 tasks done");
        cut.Find("[data-testid='onboarding-group-toggle-1'] .tm-onboarding-checklist__group-count")
            .TextContent.Trim().Should().Be("0 of 2 tasks done");
    }

    [Fact]
    public void UngroupedTasks_RenderWithoutAnyDisclosure()
    {
        var cut = RenderChecklist(Grouped());

        var toggles = cut.FindAll(".tm-onboarding-checklist__group-toggle");
        toggles.Count.Should().Be(2, "the two named groups are disclosures; the ungrouped step is not");
        cut.Find("[data-testid='onboarding-task-e']").Should().NotBeNull(
            "a step without a group still renders — in a plain list of its own, outside any disclosure");
        cut.FindAll("[data-testid='onboarding-checklist'] .tm-onboarding-checklist__group")
            .Should().HaveCount(2);
    }

    [Fact]
    public void TasksWithoutAnyGroup_RenderAsOneFlatList()
    {
        var cut = RenderChecklist(SevenSteps());

        cut.FindAll(".tm-onboarding-checklist__group").Should().BeEmpty();
        cut.FindAll("[data-testid='onboarding-checklist'] input[type='checkbox']").Count.Should().Be(7);
    }

    [Fact]
    public void CollapseCompletedGroups_IsOffByDefault_SoNothingHidesWhileTheUserWorks()
    {
        var cut = RenderChecklist(Grouped());

        cut.Find("[data-testid='onboarding-task-a'] input").Change(true);

        cut.Find("[data-testid='onboarding-group-toggle-0']").GetAttribute("aria-expanded").Should().Be("true");
        FocusInvocations().Should().BeEmpty("focus never leaves the checkbox when nothing collapses");
    }

    [Fact]
    public void CollapseCompletedGroups_HidesTheFinishedGroup_AndMovesFocusToItsHeader()
    {
        var cut = RenderChecklist(Grouped(), p => p.Add(x => x.CollapseCompletedGroups, true));

        // Snapshot BEFORE the interaction: bUnit clears the serialized blazor:elementreference
        // attribute on any later render, the ElementReference's own id stays stable.
        var toggleReference = cut.Find("[data-testid='onboarding-group-toggle-0']")
            .GetAttribute("blazor:elementreference");

        cut.Find("[data-testid='onboarding-task-a'] input").Change(true);

        cut.Find("[data-testid='onboarding-group-toggle-0']").GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("[data-testid='onboarding-group-body-0']").HasAttribute("hidden").Should().BeTrue();
        cut.WaitForAssertion(() => FocusInvocations().Should().Equal(new[] { toggleReference },
            "the checkbox that had focus is now inside a hidden body — focus has to move out to the header"));
    }

    [Fact]
    public void CollapseCompletedGroups_LeavesAnIncompleteGroupOpen_AndKeepsFocus()
    {
        var cut = RenderChecklist(Grouped(), p => p.Add(x => x.CollapseCompletedGroups, true));

        cut.Find("[data-testid='onboarding-task-c'] input").Change(true);

        cut.Find("[data-testid='onboarding-group-toggle-1']").GetAttribute("aria-expanded").Should().Be("true");
        FocusInvocations().Should().BeEmpty();
    }

    [Fact]
    public void AGroupHeader_DoesNotEmulateEnter_OverTheNativeButton()
    {
        var cut = RenderChecklist(Grouped());

        // bUnit does not synthesise a click from keydown, and a component-level @onkeydown would
        // give the header a handler at all — which is exactly the duplicate activation to avoid.
        var act = () => cut.Find("[data-testid='onboarding-group-toggle-0']").KeyDown("Enter");

        act.Should().Throw<MissingEventHandlerException>(
            "exactly one activation per key press: the native button owns Enter/Space, the component adds no keydown");
        cut.Find("[data-testid='onboarding-group-toggle-0']").GetAttribute("aria-expanded").Should().Be("true");
    }

    // ── Actions ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TaskAction_WithHref_RendersALink()
    {
        var cut = RenderChecklist([Step("a", "Connect data", actionLabel: "Open settings", actionHref: "/settings")]);

        var action = cut.Find("[data-testid='onboarding-action-a']");
        action.TagName.Should().Be("A");
        action.GetAttribute("href").Should().Be("/settings");
        action.TextContent.Trim().Should().Be("Open settings");
    }

    [Fact]
    public void TaskAction_WithAHandler_RendersAButtonThatRunsIt()
    {
        var runs = 0;
        OnboardingTask? ran = null;
        var step = Step("a", "Connect data", actionLabel: "Connect now",
            onAction: t => { ran = t; runs++; return Task.CompletedTask; });
        var cut = RenderChecklist([step]);

        var action = cut.Find("[data-testid='onboarding-action-a']");
        action.TagName.Should().Be("BUTTON");
        action.GetAttribute("type").Should().Be("button");
        action.Click();

        runs.Should().Be(1);
        ran!.Id.Should().Be("a");
        step.IsDone.Should().BeFalse("running the action does not check the step — the host decides");
    }

    [Fact]
    public void TaskAction_WithoutALabelOrTarget_RendersNothing()
    {
        var cut = RenderChecklist([Step("a", "Connect data", actionLabel: "Connect"), Step("b", "Invite")]);

        cut.FindAll(".tm-onboarding-checklist__action").Should().BeEmpty(
            "an action label without an href or handler has nowhere to go");
    }

    // ── Dismissal ───────────────────────────────────────────────────────────────

    [Fact]
    public void DismissButton_IsOptIn_AndLocalized()
    {
        RenderChecklist().FindAll("[data-testid='onboarding-checklist-dismiss']").Should().BeEmpty();

        var cut = RenderChecklist(configure: p => p.Add(x => x.Dismissible, true));
        var dismiss = cut.Find("[data-testid='onboarding-checklist-dismiss']");
        dismiss.TagName.Should().Be("BUTTON");
        dismiss.GetAttribute("aria-label").Should().Be("Dismiss checklist");
        dismiss.QuerySelector("svg").Should().NotBeNull("the button is an icon; its name comes from aria-label");
    }

    [Fact]
    public void Dismissing_RaisesOnDismiss_AndHidesTheChecklist()
    {
        var dismissed = 0;
        var cut = RenderChecklist(configure: p => p
            .Add(x => x.Dismissible, true)
            .Add(x => x.OnDismiss, () => dismissed++));

        cut.Find("[data-testid='onboarding-checklist-dismiss']").Click();

        dismissed.Should().Be(1);
        cut.FindAll("[data-testid='onboarding-checklist']").Should().BeEmpty(
            "an unbound host must not be left with a checklist it cannot get rid of");
    }

    [Fact]
    public void Dismissing_WithoutAHandler_StillHidesTheChecklist()
    {
        var cut = RenderChecklist(configure: p => p.Add(x => x.Dismissible, true));

        cut.Find("[data-testid='onboarding-checklist-dismiss']").Click();

        cut.FindAll("[data-testid='onboarding-checklist']").Should().BeEmpty();
    }

    [Fact]
    public void Dismissing_DoesNotTouchTheHostTasks()
    {
        var tasks = SevenSteps();
        var cut = RenderChecklist(tasks, p => p.Add(x => x.Dismissible, true));

        cut.Find("[data-testid='onboarding-checklist-dismiss']").Click();

        tasks.Should().OnlyContain(t => t.IsDone == (t.Id == "a" || t.Id == "b" || t.Id == "c"),
            "dismissal hides the surface; it never edits the model");
    }

    [Fact]
    public void ANewTaskList_BringsADismissedChecklistBack()
    {
        var cut = RenderChecklist(SevenSteps(), p => p.Add(x => x.Dismissible, true));
        cut.Find("[data-testid='onboarding-checklist-dismiss']").Click();
        cut.FindAll("[data-testid='onboarding-checklist']").Should().BeEmpty();

        cut.Render(p => p.Add(x => x.Tasks, Grouped()));

        cut.FindAll("[data-testid='onboarding-checklist']").Should().ContainSingle(
            "a fresh list is a new onboarding run — the checklist returns");
    }

    // ── Layout ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Layout_DefaultsToList()
    {
        var cut = RenderChecklist();

        cut.Find("section.tm-onboarding-checklist").ClassList.Should().Contain("tm-onboarding-checklist--list");
    }

    [Theory]
    [InlineData(OnboardingChecklistLayout.List, "tm-onboarding-checklist--list")]
    [InlineData(OnboardingChecklistLayout.Card, "tm-onboarding-checklist--card")]
    [InlineData(OnboardingChecklistLayout.Grid, "tm-onboarding-checklist--grid")]
    public void Layout_RendersItsModifierClass(OnboardingChecklistLayout layout, string expected)
    {
        var cut = RenderChecklist(configure: p => p.Add(x => x.Layout, layout));

        cut.Find("section.tm-onboarding-checklist").ClassList.Should().Contain(expected);
    }

    // ── Celebration ─────────────────────────────────────────────────────────────

    [Fact]
    public void Celebration_AppearsOnlyWhenEveryRequiredStepIsDone()
    {
        var cut = RenderChecklist([Step("a", "First", isDone: true), Step("b", "Second")]);
        cut.FindAll("[data-testid='onboarding-checklist-celebration']").Should().BeEmpty();

        cut.Find("[data-testid='onboarding-task-b'] input").Change(true);

        var celebration = cut.Find("[data-testid='onboarding-checklist-celebration']");
        celebration.TextContent.Should().Contain("All done!");
        celebration.TextContent.Should().Contain("You have completed every step");
        celebration.QuerySelector(".tm-onboarding-checklist__celebration-icon svg").Should().NotBeNull(
            "the celebration is an icon plus text — no confetti, no motion-only signal");
        celebration.QuerySelector(".tm-onboarding-checklist__celebration-icon")!
            .GetAttribute("aria-hidden").Should().Be("true", "the text carries the meaning");
    }

    [Fact]
    public void OptionalSteps_NeverBlockTheCelebration()
    {
        var cut = RenderChecklist([Step("a", "First", isDone: true), Step("b", "Nice to have", isOptional: true)]);

        cut.FindAll("[data-testid='onboarding-checklist-celebration']").Should().ContainSingle();
    }

    [Fact]
    public void Celebration_ShowsWhenNothingIsRequired()
    {
        var cut = RenderChecklist([Step("a", "Nice to have", isOptional: true)]);

        cut.FindAll("[data-testid='onboarding-checklist-celebration']").Should().ContainSingle(
            "nothing is required, so the checklist is already complete");
    }

    [Fact]
    public void CelebrationText_IsOverridable_AndCanBeSwitchedOff()
    {
        var cut = RenderChecklist([Step("a", "First", isDone: true)], p => p
            .Add(x => x.CompletedTitle, "Setup finished")
            .Add(x => x.CompletedText, "Everything is ready."));
        cut.Find("[data-testid='onboarding-checklist-celebration']").TextContent.Should()
            .Contain("Setup finished").And.Contain("Everything is ready.");

        var hidden = RenderChecklist([Step("a", "First", isDone: true)],
            p => p.Add(x => x.ShowCelebration, false));
        hidden.FindAll("[data-testid='onboarding-checklist-celebration']").Should().BeEmpty();
    }

    // ── Live region ─────────────────────────────────────────────────────────────

    [Fact]
    public void ThereIsExactlyOnePoliteLiveRegion_AndItStartsEmpty()
    {
        var cut = RenderChecklist();

        var liveRegions = cut.FindAll("[aria-live]");
        liveRegions.Should().ContainSingle("one live region per checklist — a second one double-announces");
        liveRegions[0].GetAttribute("aria-live").Should().Be("polite");
        liveRegions[0].GetAttribute("role").Should().Be("status");
        liveRegions[0].GetAttribute("aria-atomic").Should().Be("true");
        liveRegions[0].TextContent.Should().BeEmpty("nothing is announced before the user acts");
        cut.FindAll("[data-testid='onboarding-checklist-celebration'][aria-live]").Should().BeEmpty(
            "the visible celebration is not a second announcement channel");
    }

    [Fact]
    public void CompletingATask_AnnouncesItInTheLiveRegion()
    {
        var cut = RenderChecklist();

        cut.Find("[data-testid='onboarding-task-d'] input").Change(true);

        TextOf(cut, "onboarding-checklist-announcer").Should()
            .Be("Add your first dashboard — done. 4 of 7 tasks done");
    }

    [Fact]
    public void ReopeningATask_AnnouncesTheReopenedState()
    {
        var cut = RenderChecklist();

        cut.Find("[data-testid='onboarding-task-a'] input").Change(false);

        TextOf(cut, "onboarding-checklist-announcer").Should()
            .Be("Create a workspace — reopened. 2 of 7 tasks done");
    }

    [Fact]
    public void CompletingTheLastStep_AnnouncesTheCompletion()
    {
        var cut = RenderChecklist([Step("a", "First", isDone: true), Step("b", "Second")]);

        cut.Find("[data-testid='onboarding-task-b'] input").Change(true);

        TextOf(cut, "onboarding-checklist-announcer").Should()
            .Be("Second — done. 2 of 2 tasks done. All steps are complete.");
    }

    [Fact]
    public void GroupToggle_DoesNotAnnounceAnything()
    {
        var cut = RenderChecklist(Grouped());

        cut.Find("[data-testid='onboarding-group-toggle-0']").Click();

        TextOf(cut, "onboarding-checklist-announcer").Should().BeEmpty(
            "aria-expanded already reports the disclosure state — a second announcement would duplicate it");
    }

    // ── Test id convention ──────────────────────────────────────────────────────

    [Fact]
    public void TestIdPrefix_NamespacesTheInternalIds()
    {
        var cut = RenderChecklist(configure: p => p.Add(x => x.TestIdPrefix, "setup"));

        cut.Find("[data-testid='setup-onboarding-checklist']").Should().NotBeNull();
        cut.Find("[data-testid='setup-onboarding-task-a']").Should().NotBeNull();
        cut.Find("[data-testid='setup-onboarding-checklist-announcer']").Should().NotBeNull();
    }

    [Fact]
    public void TwoInstances_ProduceDistinctElementIds()
    {
        var first = RenderChecklist([Step("a", "First")]);
        var second = RenderChecklist([Step("a", "First")]);

        first.Find("[data-testid='onboarding-task-a'] input").GetAttribute("id").Should()
            .NotBe(second.Find("[data-testid='onboarding-task-a'] input").GetAttribute("id"),
                "two checklists on one page must not share label/checkbox ids");
    }

    // ── DOM ids are positional (review finding, code reviewer #1) ───────────────

    [Fact]
    public void StepsWhoseIdsWouldSlugAlike_GetDistinctElementIds()
    {
        var cut = RenderChecklist(
        [
            Step("a.b", "Dotted id", description: "First description"),
            Step("a-b", "Dashed id", description: "Second description"),
        ]);

        var boxes = cut.FindAll("input[type='checkbox']");
        boxes.Select(box => box.GetAttribute("id")).Should().OnlyHaveUniqueItems(
            "the DOM ids come from the step's POSITION: 'a.b' and 'a-b' both slug to 'a-b', and a shared id makes a label point at the neighbour's box");

        cut.FindAll(".tm-onboarding-checklist__description")
            .Select(description => description.GetAttribute("id"))
            .Should().OnlyHaveUniqueItems("two descriptions must not share the aria-describedby target");

        foreach (var item in cut.FindAll(".tm-onboarding-checklist__item"))
        {
            var box = item.QuerySelector("input")!;
            item.QuerySelector("label")!.GetAttribute("for").Should().Be(box.GetAttribute("id"),
                "every label is associated with its OWN checkbox");
            if (item.QuerySelector(".tm-onboarding-checklist__description") is { } description)
            {
                box.GetAttribute("aria-describedby").Should().Be(description.GetAttribute("id"));
            }
        }
    }

    [Fact]
    public void GroupsWhoseNamesWouldSlugAlike_GetDistinctBodyIds()
    {
        var cut = RenderChecklist([Step("a", "Dotted", group: "Data.1"), Step("b", "Dashed", group: "Data-1")]);

        var bodies = cut.FindAll(".tm-onboarding-checklist__group-body")
            .Select(body => body.GetAttribute("id")).ToList();
        bodies.Should().OnlyHaveUniqueItems("two group names that slug alike must not share a body id");

        var toggles = cut.FindAll(".tm-onboarding-checklist__group-toggle");
        toggles.Should().HaveCount(bodies.Count);
        for (var index = 0; index < toggles.Count; index++)
        {
            toggles[index].GetAttribute("aria-controls").Should().Be(bodies[index],
                "each header controls its OWN body");
        }
    }

    // ── Action href hardening (review finding, code reviewer #2) ────────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData(" javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    public void AnUnsafeActionHref_RendersNoLink(string href)
    {
        var cut = RenderChecklist([Step("a", "Connect data", actionLabel: "Open settings", actionHref: href)]);

        cut.FindAll("a").Should().BeEmpty("a host-supplied script URL must never reach an href");
        cut.FindAll(".tm-onboarding-checklist__action").Should().BeEmpty(
            "a refused href is no target at all, so the action renders nothing");
    }

    [Theory]
    [InlineData("/settings")]
    [InlineData("settings")]
    [InlineData("#setup")]
    [InlineData("https://example.com/setup")]
    [InlineData("http://example.com/setup")]
    [InlineData("mailto:support@example.com")]
    public void ASafeActionHref_StillRendersTheLink(string href)
    {
        var cut = RenderChecklist([Step("a", "Connect data", actionLabel: "Open settings", actionHref: href)]);

        cut.Find("[data-testid='onboarding-action-a']").GetAttribute("href").Should().Be(href);
    }

    [Fact]
    public void AnUnsafeActionHref_DoesNotSuppressTheHandler()
    {
        OnboardingTask? ran = null;
        var cut = RenderChecklist([Step("a", "Connect data", actionLabel: "Connect now",
            actionHref: "javascript:alert(1)", onAction: task => { ran = task; return Task.CompletedTask; })]);

        var action = cut.Find("[data-testid='onboarding-action-a']");
        action.TagName.Should().Be("BUTTON", "OnAction is a target of its own — it wins over the refused href");
        action.Click();
        ran!.Id.Should().Be("a");
    }
}
