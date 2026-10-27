using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingIssuePanelTests : LocalizationTestBase
{
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

        var accessibleName = button.GetAttribute("aria-label");
        accessibleName.Should().NotBeNullOrWhiteSpace("a navigable issue is a button and needs an accessible name");
        accessibleName.Should().Contain("Error");
        accessibleName.Should().Contain("Broken mapping");
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
