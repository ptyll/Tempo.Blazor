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
        badge.TextContent.Trim().Should().Be("3", "the visible badge text is the bare count");
        badge.GetAttribute("aria-label").Should().Be("Issues: 3", "the localized formulation lives on the aria-label");
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
        cursorRules.Should().AllSatisfy(selector => selector
            .Should().Contain("tm-modeling-issue-panel__item--navigable",
                "sourceless issues must not get a pointer cursor — it implies navigation that does not exist"));
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
