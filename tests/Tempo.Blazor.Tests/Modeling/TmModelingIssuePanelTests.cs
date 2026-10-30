using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingIssuePanelTests : LocalizationTestBase
{
    [Fact]
    public void Severity_icons_are_distinct_icon_names()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        cut.FindComponents<TmIcon>()
            .Select(icon => icon.Instance.Name)
            .Should().Contain(new[] { IconNames.AlertCircle, IconNames.AlertTriangle, IconNames.Info },
                "each severity carries its own distinct icon in a toned circle");

        cut.Find(".tm-modeling-issue-panel__icon--error").TextContent.Should().NotContain("!");
        cut.Find(".tm-modeling-issue-panel__icon--info").TextContent.Should().NotContain("i");
    }

    [Fact]
    public void Count_badge_shows_count_only_with_localized_aria_label()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        var badge = cut.Find("[data-testid='modeling-issue-count']");
        badge.QuerySelector("span[aria-hidden='true']")!.TextContent.Trim().Should()
            .Be("3", "the visible badge text is the bare count");
        badge.QuerySelector(".tm-sr-only")!.TextContent.Should().Be("Issues: 3",
            "the localized formulation lives on the visually-hidden span (real content = accessible name)");
        badge.ClassList.Should().Contain("tm-modeling-issue-panel__count");
    }

    [Fact]
    public void Suggested_fix_renders_localized_label()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        var fix = cut.Find("[data-testid='modeling-issue-fix-1']");
        fix.TextContent.Should().Contain("Recommendation:");
        fix.TextContent.Should().Contain("Review the mapping.");
        fix.QuerySelector(".tm-modeling-issue-panel__fix-icon svg").Should().NotBeNull(
            "the suggested fix is a toned box with an icon, not bare text");
    }

    [Fact]
    public void Navigable_issue_renders_chevron()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        cut.FindAll("button[data-testid^='modeling-issue-'] .tm-modeling-issue-panel__chevron")
            .Should().HaveCount(3, "every navigable issue row ends with a navigation chevron");
        cut.FindAll(".tm-modeling-issue-panel__chevron").Should().HaveCount(3);
    }

    [Fact]
    public void Severity_labels_and_categories_are_color_coded()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        cut.Find("[data-severity='error'] .tm-modeling-issue-panel__severity")
            .ClassList.Should().Contain("tm-modeling-issue-panel__severity--error");
        cut.Find("[data-severity='warning'] .tm-modeling-issue-panel__category").TextContent.Should().Be("Validation");
    }

    [Fact]
    public void Renders_issue_count_and_items()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        cut.Find("[data-testid='modeling-issue-panel']").GetAttribute("data-issue-count").Should().Be("3");
        cut.FindAll("button[data-testid^='modeling-issue-']").Should().HaveCount(3);
    }

    [Fact]
    public void Clicking_issue_emits_source_element_id()
    {
        ModelingIssueDto? selected = null;
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues())
            .Add(p => p.OnIssueSelected, EventCallback.Factory.Create<ModelingIssueDto>(this, issue => selected = issue)));

        cut.Find("[data-testid='modeling-issue-1']").Click();

        selected.Should().NotBeNull();
        selected!.SourceElementId.Should().Be("task-review");
    }

    [Fact]
    public void Severity_classes_match_each_issue_type()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        cut.Find("[data-severity='info']").ClassList.Should().Contain("tm-modeling-issue-panel__item--info");
        cut.Find("[data-severity='warning']").ClassList.Should().Contain("tm-modeling-issue-panel__item--warning");
        cut.Find("[data-severity='error']").ClassList.Should().Contain("tm-modeling-issue-panel__item--error");
        cut.Find(".tm-modeling-issue-panel__icon--info").Should().NotBeNull();
        cut.Find(".tm-modeling-issue-panel__icon--warning").Should().NotBeNull();
        cut.Find(".tm-modeling-issue-panel__icon--error").Should().NotBeNull();
    }

    [Fact]
    public void Empty_issues_render_positive_empty_state()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, Array.Empty<ModelingIssueDto>()));

        cut.Find("[data-testid='modeling-issue-empty']").TextContent.Should().Contain("No issues found");
        cut.FindAll("[data-testid='modeling-issue-list']").Should().BeEmpty();
    }

    [Fact]
    public void Issue_without_source_renders_non_interactive_item()
    {
        var selectedCount = 0;
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, new[]
            {
                new ModelingIssueDto
                {
                    Id = "orphan",
                    Severity = ModelingIssueSeverity.Warning,
                    Category = "Sync",
                    Message = "No source",
                    SuggestedFix = "Check the mapping."
                }
            })
            .Add(p => p.OnIssueSelected, EventCallback.Factory.Create<ModelingIssueDto>(this, _ => selectedCount++)));

        var item = cut.Find("[data-testid='modeling-issue-0']");

        // Not a button — a sourceless issue has no navigation target, so it must not
        // present a button affordance (screen-reader activatable, pointer cursor).
        item.TagName.Should().NotBe("BUTTON");
        cut.FindAll("button[data-testid^='modeling-issue-']").Should().BeEmpty();
        item.ClassList.Should().NotContain("tm-modeling-issue-panel__item--navigable");

        // The accessible text is preserved (severity, category, message, fix) — the row
        // only loses the interactive affordance, never its content.
        item.TextContent.Should().Contain("Warning");
        item.TextContent.Should().Contain("Sync");
        item.TextContent.Should().Contain("No source");
        item.TextContent.Should().Contain("Check the mapping.");

        // The row is inert: no click handler is wired, so activating it can never
        // raise OnIssueSelected (the sourceless guard in SelectIssueAsync stays as a
        // defense for direct API callers).
        item.HasAttribute("blazor:onclick").Should().BeFalse();
        selectedCount.Should().Be(0);
    }

    [Fact]
    public void Navigable_issue_renders_button_with_accessible_name()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, new[]
            {
                new ModelingIssueDto
                {
                    Id = "linked",
                    Severity = ModelingIssueSeverity.Error,
                    Category = "Validation",
                    SourceElementId = "task-review",
                    Message = "Broken mapping"
                }
            }));

        var button = cut.Find("button[data-testid='modeling-issue-0']");
        button.ClassList.Should().Contain("tm-modeling-issue-panel__item--navigable");

        // The visible content IS the accessible name: no aria-label override that could
        // drift out of sync with the row text (and duplicate what the row already says).
        button.HasAttribute("aria-label").Should().BeFalse(
            "a visible, self-describing button needs no aria-label override");

        var accessibleName = button.TextContent.Replace("\n", " ", StringComparison.Ordinal);
        accessibleName = System.Text.RegularExpressions.Regex.Replace(accessibleName, @"\s+", " ").Trim();
        accessibleName.Should().Contain("Error");
        accessibleName.Should().Contain("Validation");
        accessibleName.Should().Contain("Broken mapping");

        // The navigation affordance is announced by a visually-hidden hint span.
        button.QuerySelector(".tm-sr-only")!.TextContent.Should().Be("Show in model");
    }

    [Fact]
    public void Sourceless_issue_renders_no_navigation_hint()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, new[]
            {
                new ModelingIssueDto { Id = "orphan", Severity = ModelingIssueSeverity.Info, Message = "No source" }
            }));

        var item = cut.Find("[data-testid='modeling-issue-0']");
        item.TagName.Should().NotBe("BUTTON");
        item.QuerySelectorAll(".tm-sr-only").Should().BeEmpty(
            "a non-interactive row is not navigable and must not promise navigation");
    }

    [Fact]
    public void Button_and_div_wrappers_share_one_row_body()
    {
        // Both wrappers render from ONE RenderFragment — the visible content of a navigable
        // button and a sourceless div must be identical apart from the hidden hint.
        var navigable = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, new[]
            {
                new ModelingIssueDto { Id = "a", Severity = ModelingIssueSeverity.Warning, SourceElementId = "e1", Message = "Shared body" }
            }));
        var sourceless = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, new[]
            {
                new ModelingIssueDto { Id = "b", Severity = ModelingIssueSeverity.Warning, Message = "Shared body" }
            }));

        var navButton = navigable.Find("button[data-testid='modeling-issue-0']");
        navButton.QuerySelector(".tm-modeling-issue-panel__message")!.TextContent.Should().Be("Shared body");
        navButton.QuerySelectorAll(".tm-modeling-issue-panel__fix").Should().BeEmpty();

        var staticItem = sourceless.Find("div[data-testid='modeling-issue-0']");
        staticItem.QuerySelector(".tm-modeling-issue-panel__message")!.TextContent.Should().Be("Shared body");

        navButton.QuerySelector(".tm-modeling-issue-panel__body")!.ClassList
            .Should().BeEquivalentTo(staticItem.QuerySelector(".tm-modeling-issue-panel__body")!.ClassList);
        navButton.QuerySelector(".tm-modeling-issue-panel__icon")!.ClassList
            .Should().BeEquivalentTo(staticItem.QuerySelector(".tm-modeling-issue-panel__icon")!.ClassList);
    }

    [Fact]
    public void Pointer_cursor_is_bound_to_navigable_items_only()
    {
        var css = File.ReadAllText(FindComponentFile("TmModelingIssuePanel.razor.css"));

        var cursorRules = CursorPointerRuleSelectors(css);

        cursorRules.Should().NotBeEmpty("navigable issues keep the pointer affordance");

        // Fáze 11: the filter bar's own interactive controls (severity chip buttons and the
        // no-match clear action) are also pointer-afforded — they are real buttons, not issue
        // rows, so they are allowed beyond the navigable-row gate.
        var filterBarControls = new[] { ".tm-modeling-issue-panel__chip", ".tm-modeling-issue-panel__clear" };
        foreach (var rule in cursorRules)
        {
            rule.Should().Match(selector =>
                selector.Contains("tm-modeling-issue-panel__item--navigable", StringComparison.Ordinal)
                || filterBarControls.Any(control => selector.Contains(control, StringComparison.Ordinal)),
            "sourceless issues must not get a pointer cursor — it implies navigation that does not exist (filter bar controls excepted: they are real buttons)");
        }
    }

    private static string FindComponentFile(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName, "src", "Tempo.Blazor.Modeling", "Components", "Modeling", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent!;
        }

        throw new FileNotFoundException($"Could not locate {fileName} under src/Tempo.Blazor.Modeling.");
    }

    private static string[] CursorPointerRuleSelectors(string css)
    {
        var selectors = new List<string>();
        var index = css.IndexOf("cursor: pointer", StringComparison.Ordinal);
        while (index >= 0)
        {
            var ruleStart = css.LastIndexOf('{', index);
            var selectorStart = Math.Max(css.LastIndexOf('}', ruleStart), css.LastIndexOf('\n', ruleStart)) + 1;
            selectors.Add(css[selectorStart..ruleStart].Trim());

            index = css.IndexOf("cursor: pointer", index + 1, StringComparison.Ordinal);
        }

        return selectors.ToArray();
    }


    [Fact]
    public void Renders_large_issue_list_without_failing()
    {
        var issues = Enumerable.Range(0, 128)
            .Select(index => new ModelingIssueDto
            {
                Id = $"issue-{index}",
                Severity = ModelingIssueSeverity.Warning,
                SourceElementId = $"task-{index}",
                Message = $"Issue {index}"
            })
            .ToArray();

        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues));

        cut.Find("[data-testid='modeling-issue-panel']").GetAttribute("data-issue-count").Should().Be("128");
        cut.FindAll("button[data-testid^='modeling-issue-']").Should().HaveCount(128);
    }

    [Fact]
    public void Long_message_uses_wrapping_message_element()
    {
        var message = new string('A', 500);
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, new[] { new ModelingIssueDto { Id = "long", SourceElementId = "task-long", Message = message } }));

        var messageElement = cut.Find(".tm-modeling-issue-panel__message");
        messageElement.TextContent.Should().Be(message);
        messageElement.ClassList.Should().Contain("tm-modeling-issue-panel__message");
    }

    // ── Fáze 11: severity/text filter bar + sorting ──────────────────────────

    [Fact]
    public void Default_renders_input_order_without_filter_bar()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues()));

        cut.FindAll("[data-testid='modeling-issue-filterbar']").Should().BeEmpty(
            "the filter bar is opt-in (ShowFilterBar defaults to false)");

        var root = cut.Find("[data-testid='modeling-issue-panel']");
        root.GetAttribute("data-visible-issue-count").Should().Be("3");
        root.GetAttribute("data-total-issue-count").Should().Be("3");

        // Input-order regression (default SortMode=Input, no filter): the rows keep the
        // input sequence and keep their original input indexes as test ids.
        VisibleMessages(cut).Should().Equal("Informational issue", "Warning issue", "Error issue");
        cut.FindAll("[data-testid='modeling-issue-list'] > li > *")
            .Select(row => row.GetAttribute("data-testid"))
            .Should().Equal("modeling-issue-0", "modeling-issue-1", "modeling-issue-2");
    }

    [Fact]
    public void Severity_chips_show_counts_and_aria_pressed()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true));

        var errorChip = cut.Find("[data-testid='modeling-issue-severity-chip-error']");
        errorChip.TagName.Should().Be("BUTTON",
            "severity chips are native toggle buttons (one activation per press, keyboard reachable)");
        errorChip.GetAttribute("aria-pressed").Should().Be("false",
            "no severity filter is active when the panel renders unfiltered");
        errorChip.TextContent.Should().Contain("Error").And.Contain("2",
            "the chip shows its per-severity count");

        // Review fix (Fáze 11): the count is part of the chip's ACCESSIBLE NAME — it must not
        // be aria-hidden away from assistive tech.
        errorChip.QuerySelector(".tm-modeling-issue-panel__chip-count")!.HasAttribute("aria-hidden")
            .Should().BeFalse("the chip's accessible name includes its count");

        // Review fix: the bar wrapper is plain layout — the labelled chip group and the
        // labelled controls name the region; a group role on the wrapper announced twice.
        cut.Find("[data-testid='modeling-issue-filterbar']").HasAttribute("role").Should().BeFalse();

        cut.Find("[data-testid='modeling-issue-severity-chip-warning']").TextContent
            .Should().Contain("Warning").And.Contain("1");
        cut.Find("[data-testid='modeling-issue-severity-chip-info']").TextContent
            .Should().Contain("Info").And.Contain("1");

        // The bar lives INSIDE the collapsible body, so a collapsed dock hides it too.
        cut.Find(".tm-modeling-issue-panel__content [data-testid='modeling-issue-filterbar']")
            .Should().NotBeNull();
    }

    [Fact]
    public void Toggling_severity_chip_filters_and_raises_SeverityFilterChanged()
    {
        IReadOnlyCollection<ModelingIssueSeverity>? raised = null;
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SeverityFilterChanged, EventCallback.Factory
                .Create<IReadOnlyCollection<ModelingIssueSeverity>?>(this, value => raised = value)));

        cut.Find("[data-testid='modeling-issue-severity-chip-error']").Click();

        cut.WaitForAssertion(() =>
        {
            VisibleMessages(cut).Should().HaveCount(2, "only the error rows survive the chip filter");
            cut.Find("[data-testid='modeling-issue-severity-chip-error']")
                .GetAttribute("aria-pressed").Should().Be("true");
        });
        raised.Should().NotBeNull("the two-way binding observes every chip toggle");
        raised!.Should().Equal(ModelingIssueSeverity.Error);

        cut.Find("[data-testid='modeling-issue-severity-chip-error']").Click();

        cut.WaitForAssertion(() =>
        {
            VisibleMessages(cut).Should().HaveCount(4, "toggling the last active chip off shows every issue");
            cut.Find("[data-testid='modeling-issue-severity-chip-error']")
                .GetAttribute("aria-pressed").Should().Be("false");
        });
        raised.Should().BeNull("an empty active set is reported as null (the 'all severities' contract)");
    }

    [Fact]
    public void Severity_chips_use_core_TmChip_pressed()
    {
        // Phase 15b carry-forward: the severity chips ARE core TmChips (the pressed variant) —
        // the panel no longer hand-rolls its own aria-pressed button. The stable contract that
        // hosts and E2E key off (data-testid, counts in the accessible name) is unchanged.
        IReadOnlyCollection<ModelingIssueSeverity>? raised = null;
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SeverityFilterChanged, EventCallback.Factory
                .Create<IReadOnlyCollection<ModelingIssueSeverity>?>(this, value => raised = value)));

        var errorChip = cut.Find("[data-testid='modeling-issue-severity-chip-error']");
        errorChip.TagName.Should().Be("BUTTON", "the core TmChip pressed variant is a native button");
        errorChip.ClassList.Should().Contain("tm-chip", "the chip carries the core vocabulary");
        errorChip.ClassList.Should().Contain("tm-modeling-issue-panel__chip", "the panel keeps its own hook class");
        errorChip.ClassList.Should().Contain("tm-chip--pressed",
            "every severity chip is the toggle VARIANT (aria-pressed carries the state)");
        errorChip.GetAttribute("aria-pressed").Should().Be("false", "no severity filter is active initially");
        errorChip.TextContent.Should().Contain("Error").And.Contain("2", "the count stays in the accessible name");

        errorChip.Click();
        cut.WaitForAssertion(() =>
        {
            var active = cut.Find("[data-testid='modeling-issue-severity-chip-error']");
            active.ClassList.Should().Contain("tm-chip--pressed", "the core pressed class flips with the state");
            active.GetAttribute("aria-pressed").Should().Be("true");
        });
        raised.Should().NotBeNull();

        // Toggling through the core path keeps filtering the list.
        cut.WaitForAssertion(() => VisibleMessages(cut).Should().HaveCount(2));
    }

    [Fact]
    public void Filter_text_matches_message_category_and_source_name_case_insensitive()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateSearchIssues())
            .Add(p => p.Elements, CreateSearchElements())
            .Add(p => p.ShowFilterBar, true));

        // By message (case-insensitive).
        SetFilterText(cut, "BROKEN");
        cut.WaitForAssertion(() => VisibleMessages(cut)
            .Should().BeEquivalentTo(new[] { "Broken mapping message" }));

        // By category.
        SetFilterText(cut, "governance");
        cut.WaitForAssertion(() => VisibleMessages(cut)
            .Should().BeEquivalentTo(new[] { "Governance note" }));

        // By the SOURCE ELEMENT NAME (the element list is what makes name search possible).
        SetFilterText(cut, "review task");
        cut.WaitForAssertion(() => VisibleMessages(cut)
            .Should().BeEquivalentTo(new[] { "Review task is unmapped" }));

        // Nothing survives a filter that matches nothing → the localized no-match state.
        SetFilterText(cut, "no-such-issue-anywhere");
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-issue-no-match']").Should().NotBeNull());
    }

    [Fact]
    public void Sort_by_severity_is_stable_within_severity()
    {
        var issues = new[]
        {
            new ModelingIssueDto { Id = "i-0", Severity = ModelingIssueSeverity.Info, Message = "Info first in input" },
            new ModelingIssueDto { Id = "i-1", Severity = ModelingIssueSeverity.Error, Message = "Error one" },
            new ModelingIssueDto { Id = "i-2", Severity = ModelingIssueSeverity.Warning, Message = "Warning one" },
            new ModelingIssueDto { Id = "i-3", Severity = ModelingIssueSeverity.Error, Message = "Error two" }
        };

        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.SortMode, ModelingIssueSortMode.Severity));

        // Error > Warning > Info and the two errors keep their INPUT relative order
        // (stable sort — the original input indexes stay on the rows as test ids).
        RenderedRowTestIds(cut).Should().Equal(
            "modeling-issue-1", "modeling-issue-3", "modeling-issue-2", "modeling-issue-0");
    }

    [Fact]
    public void Sort_by_source_puts_sourceless_last()
    {
        var issues = new[]
        {
            new ModelingIssueDto { Id = "s-0", Severity = ModelingIssueSeverity.Warning, Message = "Orphan — no source" },
            new ModelingIssueDto { Id = "s-1", Severity = ModelingIssueSeverity.Warning, SourceElementId = "ghost-element", Message = "Ghost element id" },
            new ModelingIssueDto { Id = "s-2", Severity = ModelingIssueSeverity.Warning, SourceRelationshipId = "rel-beta", Message = "Relationship issue" },
            new ModelingIssueDto { Id = "s-3", Severity = ModelingIssueSeverity.Warning, SourceElementId = "el-alpha", Message = "Element issue" }
        };

        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.Elements, CreateSearchElements())
            .Add(p => p.Relationships, CreateSearchRelationships())
            .Add(p => p.SortMode, ModelingIssueSortMode.Source));

        // Source NAME order: "Alpha Element" < "Beta Relationship" < the unmatched id fallback
        // ("ghost-element") — and the sourceless orphan renders last.
        RenderedRowTestIds(cut).Should().Equal(
            "modeling-issue-3", "modeling-issue-2", "modeling-issue-1", "modeling-issue-0");
    }

    [Fact]
    public void Sort_mode_select_raises_SortModeChanged_and_re_sorts()
    {
        var issues = new[]
        {
            new ModelingIssueDto { Id = "s-0", Severity = ModelingIssueSeverity.Error, Message = "Error one" },
            new ModelingIssueDto { Id = "s-1", Severity = ModelingIssueSeverity.Info, Message = "Info one" }
        };
        ModelingIssueSortMode? raised = null;
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SortModeChanged, EventCallback.Factory
                .Create<ModelingIssueSortMode>(this, value => raised = value)));

        var sortSelect = cut.Find("[data-testid='modeling-issue-sort'] select");
        cut.Find("[data-testid='modeling-issue-sort'] label").TextContent.Should().Be("Sort",
            "the sort select carries a VISIBLE label (the plan's 'TmSelect řazení s viditelným labelem')");
        cut.FindAll("[data-testid='modeling-issue-sort'] option").Should().HaveCount(3, "Input | Severity | Source");

        var severityOption = cut.FindAll("[data-testid='modeling-issue-sort'] option")
            .Single(option => option.TextContent == "Severity");
        sortSelect.Change(severityOption.GetAttribute("value"));

        cut.WaitForAssertion(() =>
        {
            raised.Should().Be(ModelingIssueSortMode.Severity);
            RenderedRowTestIds(cut).Should().Equal("modeling-issue-0", "modeling-issue-1");
        });
    }

    [Fact]
    public void Empty_filter_result_renders_localized_no_match_state_with_clear_action()
    {
        IReadOnlyCollection<ModelingIssueSeverity>? severityRaised = null;
        string? textRaised = null;
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues())
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SeverityFilterChanged, EventCallback.Factory
                .Create<IReadOnlyCollection<ModelingIssueSeverity>?>(this, value => severityRaised = value))
            .Add(p => p.FilterTextChanged, EventCallback.Factory
                .Create<string?>(this, value => textRaised = value)));

        SetFilterText(cut, "no-such-thing");
        cut.WaitForAssertion(() =>
        {
            var noMatch = cut.Find("[data-testid='modeling-issue-no-match']");
            noMatch.TextContent.Should().Contain("No issues match the current filters");
            noMatch.QuerySelector("[data-testid='modeling-issue-clear-filters']")!
                .TextContent.Should().Contain("Clear filters");
            cut.FindAll("[data-testid='modeling-issue-list']").Should().BeEmpty();
        });

        cut.Find("[data-testid='modeling-issue-clear-filters']").Click();

        cut.WaitForAssertion(() =>
        {
            VisibleMessages(cut).Should().HaveCount(3, "clearing restores the full list");
            textRaised.Should().Be(string.Empty, "the clear action resets the text filter through the binding");
            severityRaised.Should().BeNull("the clear action resets the severity filter through the binding");
        });
    }

    [Fact]
    public void Header_count_shows_visible_of_total_when_filtered()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SeverityFilter, new[] { ModelingIssueSeverity.Error }));

        var root = cut.Find("[data-testid='modeling-issue-panel']");
        root.GetAttribute("data-visible-issue-count").Should().Be("2");
        root.GetAttribute("data-total-issue-count").Should().Be("4");

        var badge = cut.Find("[data-testid='modeling-issue-count']");
        badge.QuerySelector(".tm-sr-only")!.TextContent.Should().Be("Issues: 2 of 4",
            "the filtered header count uses the neutral visible-of-total formulation");
        badge.QuerySelector("span[aria-hidden='true']")!.TextContent.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Should().Contain("2").And.Contain("4");
    }

    [Fact]
    public void Host_severity_reset_to_null_unfilters_the_list()
    {
        var filter = new[] { ModelingIssueSeverity.Error };
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SeverityFilter, filter));

        cut.WaitForAssertion(() => VisibleMessages(cut).Should().HaveCount(2));

        // The host clears the filter: a RESET to null must re-seed even though the chips had
        // already echoed the active set back (the dc39149a host-reset contract).
        var resetTask = cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmModelingIssuePanel.Issues)] = CreateCountedIssues(),
                [nameof(TmModelingIssuePanel.ShowFilterBar)] = true,
                [nameof(TmModelingIssuePanel.SeverityFilter)] = (IReadOnlyCollection<ModelingIssueSeverity>?)null
            })));

        resetTask.Wait(TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => VisibleMessages(cut).Should().HaveCount(4));
    }

    [Fact]
    public void Duplicate_source_ids_render_and_source_sort_uses_first_name()
    {
        // Review BLOCKER (Fáze 11): the host may hand over element/relationship tables with
        // DUPLICATE ids — the name maps must keep the first entry (loop + TryAdd) instead of
        // throwing "An item with the same key has already been added" out of ToDictionary.
        var elements = new[]
        {
            new ModelingElementDto { Id = "dup", Name = "Zeta Dup" },
            new ModelingElementDto { Id = "dup", Name = "Alpha Dup" },
            new ModelingElementDto { Id = "solo", Name = "Mid Element" }
        };
        var relationships = new[]
        {
            new ModelingRelationshipDto { Id = "rel-dup", Name = "Beta Rel" },
            new ModelingRelationshipDto { Id = "rel-dup", Name = "aaa Rel" }
        };
        var issues = new[]
        {
            new ModelingIssueDto { Id = "d-0", Severity = ModelingIssueSeverity.Info, SourceElementId = "solo", Message = "Solo issue" },
            new ModelingIssueDto { Id = "d-1", Severity = ModelingIssueSeverity.Error, SourceElementId = "dup", Message = "Dup element issue" },
            new ModelingIssueDto { Id = "d-2", Severity = ModelingIssueSeverity.Warning, SourceRelationshipId = "rel-dup", Message = "Dup relationship issue" }
        };

        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.Elements, elements)
            .Add(p => p.Relationships, relationships)
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SortMode, ModelingIssueSortMode.Source));

        // No throw, all rows render — and the Source sort resolved the names from the FIRST
        // duplicate entry ("Beta Rel" < "Mid Element" < "Zeta Dup"; second-wins would order 1,2,0).
        cut.FindAll("[data-testid='modeling-issue-list'] > li > *").Should().HaveCount(3);
        RenderedRowTestIds(cut).Should().Equal("modeling-issue-2", "modeling-issue-0", "modeling-issue-1");
    }

    [Fact]
    public void Live_region_reports_filter_result_and_stays_empty_when_unfiltered()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SeverityFilter, new[] { ModelingIssueSeverity.Error }));

        var status = () => cut.Find("[data-testid='modeling-issue-filter-status']");
        status().GetAttribute("role").Should().Be("status");
        status().GetAttribute("aria-live").Should().Be("polite");
        status().TextContent.Should().Be("Issues: 2 of 4",
            "the live region carries the localized visible-of-total pair while a filter narrows the list");

        // Every row hidden → the no-match formulation (the distinct state wins over the pair).
        using var noMatch = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.FilterText, "no-such-issue-anywhere"));
        noMatch.Find("[data-testid='modeling-issue-filter-status']").TextContent
            .Should().Be("No issues match the current filters.");

        // Unfiltered → the region stays EMPTY (always rendered, never removed).
        using var unfiltered = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateCountedIssues())
            .Add(p => p.ShowFilterBar, true));
        unfiltered.Find("[data-testid='modeling-issue-filter-status']").TextContent.Should().BeEmpty();
    }

    [Fact]
    public void Clear_filters_returns_focus_to_the_search_input()
    {
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, CreateIssues())
            .Add(p => p.ShowFilterBar, true));

        SetFilterText(cut, "no-such-thing");
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-issue-no-match']").Should().NotBeNull());

        cut.Find("[data-testid='modeling-issue-clear-filters']").Click();

        // ElementReference.FocusAsync surfaces in bUnit's Loose JSInterop as
        // "Blazor._internal.domWrapper.focus" — the clear action must hand focus back to the
        // search input instead of dropping it when the no-match body unmounts (review fix).
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus")
                .Should().Be(1));
    }

    [Fact]
    public void Equal_content_severity_echo_does_not_re_seed_the_filter()
    {
        // ONE Issues instance across all passes: a fresh array would itself force a render
        // through the render gate's reference comparison and defeat the assertion.
        var issues = CreateCountedIssues();
        var filter = new[] { ModelingIssueSeverity.Error };
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.SeverityFilter, filter));
        var rendersAfterSeed = cut.Instance.RenderCount;

        // The host hands over a FRESH, equal-content collection (a rebuilt echo of the last
        // value): it must be recorded BY CONTENT, not re-seeded — no state wipe and no extra
        // render pass (M26).
        var echoTask = cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmModelingIssuePanel.Issues)] = issues,
                [nameof(TmModelingIssuePanel.ShowFilterBar)] = true,
                [nameof(TmModelingIssuePanel.SeverityFilter)] = new[] { ModelingIssueSeverity.Error }
            })));
        echoTask.Wait(TimeSpan.FromSeconds(5));
        cut.Instance.RenderCount.Should().Be(rendersAfterSeed,
            "an equal-content echo is recorded, not re-seeded");

        // A genuinely NEW host value still re-seeds (the host owns the state).
        var resetTask = cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmModelingIssuePanel.Issues)] = issues,
                [nameof(TmModelingIssuePanel.ShowFilterBar)] = true,
                [nameof(TmModelingIssuePanel.SeverityFilter)] = new[] { ModelingIssueSeverity.Error, ModelingIssueSeverity.Warning }
            })));
        resetTask.Wait(TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='modeling-issue-severity-chip-warning']").GetAttribute("aria-pressed").Should().Be("true"));
    }

    [Fact]
    public void Render_gate_re_renders_when_test_id_parameters_change()
    {
        var issues = CreateIssues();
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.TestIdPrefix, "host1"));

        cut.Find("[data-testid='host1-modeling-issue-panel']").Should().NotBeNull("TestIdPrefix namespaces the root test id");

        // TestIdPrefix is part of the render-gate snapshot: a parent pass that changes it (and
        // nothing else) must re-render, not serve the stale DOM.
        var task = cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmModelingIssuePanel.Issues)] = issues,
                [nameof(TmModelingIssuePanel.TestIdPrefix)] = "host2"
            })));
        task.Wait(TimeSpan.FromSeconds(5));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='host2-modeling-issue-panel']").Should().NotBeNull("the changed prefix re-rendered the root"));
    }

    [Fact]
    public void Filter_bar_controls_are_44px_on_coarse_pointer()
    {
        var css = File.ReadAllText(FindComponentFile("TmModelingIssuePanel.razor.css"));
        var coarseBlock = ExtractMediaQueryBlock(css, "(pointer: coarse)");

        coarseBlock.Should().NotBeNull("the filter bar owns a coarse-pointer block");
        coarseBlock.Should().Contain(".tm-modeling-issue-panel__chip", "severity chips reach the touch target");
        coarseBlock.Should().Contain(".tm-modeling-issue-panel__filter-input", "the search input reaches the touch target");
        coarseBlock.Should().Contain(".tm-modeling-issue-panel__filter-sort", "the sort select reaches the touch target");
        coarseBlock.Should().Contain("var(--tm-touch-target)", "44px comes from the shared touch token");
    }

    private static void SetFilterText(IRenderedComponent<TmModelingIssuePanel> cut, string text)
        => cut.Find("[data-testid='modeling-issue-filter-input']").Input(text);

    private static List<string> VisibleMessages(IRenderedComponent<TmModelingIssuePanel> cut)
        => cut.FindAll(".tm-modeling-issue-panel__message").Select(message => message.TextContent).ToList();

    private static List<string> RenderedRowTestIds(IRenderedComponent<TmModelingIssuePanel> cut)
        => cut.FindAll("[data-testid='modeling-issue-list'] > li > *")
            .Select(row => row.GetAttribute("data-testid")!)
            .ToList();

    /// <summary>Extracts the balanced-brace bodies of ALL top-level <c>@media</c> queries matching
    /// the feature, concatenated (the file owns several coarse-pointer blocks by phase).</summary>
    private static string? ExtractMediaQueryBlock(string css, string mediaFeature)
    {
        var matched = new System.Text.StringBuilder();
        var headerIndex = css.IndexOf("@media", StringComparison.Ordinal);
        while (headerIndex >= 0)
        {
            var braceIndex = css.IndexOf('{', headerIndex);
            if (braceIndex < 0)
            {
                break;
            }

            var header = css[headerIndex..braceIndex];
            if (header.Contains(mediaFeature, StringComparison.Ordinal))
            {
                var depth = 1;
                var cursor = braceIndex + 1;
                while (cursor < css.Length && depth > 0)
                {
                    depth += css[cursor] switch { '{' => 1, '}' => -1, _ => 0 };
                    cursor++;
                }

                matched.Append(css[(braceIndex + 1)..(cursor - 1)]);
                headerIndex = css.IndexOf("@media", cursor, StringComparison.Ordinal);
                continue;
            }

            headerIndex = css.IndexOf("@media", braceIndex, StringComparison.Ordinal);
        }

        return matched.Length == 0 ? null : matched.ToString();
    }

    private static ModelingIssueDto[] CreateCountedIssues() =>
    [
        new() { Id = "c-1", Severity = ModelingIssueSeverity.Error, SourceElementId = "el-alpha", Message = "Error one" },
        new() { Id = "c-2", Severity = ModelingIssueSeverity.Error, SourceElementId = "el-alpha", Message = "Error two" },
        new() { Id = "c-3", Severity = ModelingIssueSeverity.Warning, SourceElementId = "el-alpha", Message = "Warning one" },
        new() { Id = "c-4", Severity = ModelingIssueSeverity.Info, Message = "Info one" }
    ];

    private static ModelingIssueDto[] CreateSearchIssues() =>
    [
        new() { Id = "q-1", Severity = ModelingIssueSeverity.Error, Category = "Validation", SourceElementId = "el-alpha", Message = "Broken mapping message" },
        new() { Id = "q-2", Severity = ModelingIssueSeverity.Info, Category = "Governance", Message = "Governance note" },
        new() { Id = "q-3", Severity = ModelingIssueSeverity.Warning, SourceElementId = "el-review", Message = "Review task is unmapped" },
        new() { Id = "q-4", Severity = ModelingIssueSeverity.Warning, SourceRelationshipId = "rel-beta", Message = "Relationship note" }
    ];

    private static ModelingElementDto[] CreateSearchElements() =>
    [
        new() { Id = "el-alpha", Name = "Alpha Element" },
        new() { Id = "el-review", Name = "Review Task" }
    ];

    [Fact]
    public void ContentVersion_bump_re_renders_after_an_in_place_rename()
    {
        // ONE element list across all passes: the editor renames the DTO IN PLACE, so the
        // gate/memo reference comparison alone cannot see the change.
        var issues = CreateIssues();
        var elements = CreateSearchElements();
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.Elements, elements)
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.ContentVersion, 0));
        var rendersAfterSeed = cut.Instance.RenderCount;

        // The editor renamed the element and bumped the content version — the panel must
        // repaint (the source-name index feeds the text search and the Source sort).
        elements[0].Name = "Renamed Element";
        cut.Render(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.Elements, elements)
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.ContentVersion, 1));

        cut.Instance.RenderCount.Should().BeGreaterThan(rendersAfterSeed,
            "a ContentVersion bump invalidates the render gate even with unchanged collection references");
    }

    [Fact]
    public void ContentVersion_echo_does_not_force_a_render()
    {
        var issues = CreateIssues();
        var elements = CreateSearchElements();
        using var cut = Render<TmModelingIssuePanel>(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.Elements, elements)
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.ContentVersion, 3));
        var rendersAfterSeed = cut.Instance.RenderCount;

        // The same version with the same references is a plain parent pass — nothing renders.
        cut.Render(parameters => parameters
            .Add(p => p.Issues, issues)
            .Add(p => p.Elements, elements)
            .Add(p => p.ShowFilterBar, true)
            .Add(p => p.ContentVersion, 3));

        cut.Instance.RenderCount.Should().Be(rendersAfterSeed,
            "an unchanged version keeps the M26 render gate quiet");
    }

    private static ModelingRelationshipDto[] CreateSearchRelationships() =>
    [
        new() { Id = "rel-beta", Name = "Beta Relationship" }
    ];

    private static ModelingIssueDto[] CreateIssues() =>
    [
        new()
        {
            Id = "info",
            Category = "Governance",
            Severity = ModelingIssueSeverity.Info,
            SourceElementId = "task-approve",
            Message = "Informational issue"
        },
        new()
        {
            Id = "warning",
            Category = "Validation",
            Severity = ModelingIssueSeverity.Warning,
            SourceElementId = "task-review",
            Message = "Warning issue",
            SuggestedFix = "Review the mapping."
        },
        new()
        {
            Id = "error",
            Category = "Generator",
            Severity = ModelingIssueSeverity.Error,
            SourceElementId = "task-ship",
            Message = "Error issue"
        }
    ];
}
