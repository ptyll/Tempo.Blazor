using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan 5d396592 phase 5: the documentation and the changelog describe the behaviour that shipped —
/// the model, the counted progress sentence and the plural rationale, the group disclosure, the
/// opt-in auto-collapse with its focus hand-back, the dismissal contract, the three layouts and the
/// accessibility rules (one live region, native controls, forced-colours ring, coarse targets).
/// </summary>
public class TmOnboardingChecklistDocsTests
{
    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    [Fact]
    public void Doc_DocumentsTheApi()
    {
        var doc = Read("docs", "onboarding-checklist.md");

        foreach (var term in new[]
        {
            "TmOnboardingChecklist", "OnboardingTask", "OnboardingChecklistLayout", "Tasks", "OnTaskToggle",
            "Dismissible", "OnDismiss", "Layout", "Title", "HeadingLevel", "ShowProgress",
            "CollapseCompletedGroups", "ShowCelebration", "CompletedTitle", "CompletedText", "Class",
            "Id", "IsOptional", "Group", "ActionLabel", "ActionHref", "OnAction",
            "List", "Card", "Grid", "TestIdPrefix", "data-testid",
        })
        {
            doc.Should().Contain(term, $"docs/onboarding-checklist.md must document {term}");
        }
    }

    [Fact]
    public void Doc_DocumentsTheProgressGrammarAndThePluralRationale()
    {
        var doc = Read("docs", "onboarding-checklist.md");

        doc.Should().Contain("TmProgressBar");
        doc.Should().Contain("3 of 7 tasks done");
        doc.Should().Contain("3 z 7 úkolů hotovo");
        doc.Should().Contain("3 z 7 úloh hotovo");
        doc.Should().Contain("3 sur 7 tâches terminées");
        doc.Should().Contain("total", "the plural family keys on the total — that is what the noun agrees with");
        doc.Should().Contain("aria-valuenow");
        doc.Should().Contain("ProgressLabel");
    }

    [Fact]
    public void Doc_DocumentsTheAccessibilityContract()
    {
        var doc = Read("docs", "onboarding-checklist.md");

        foreach (var term in new[]
        {
            "aria-expanded", "aria-controls", "hidden", "aria-describedby", "role=\"status\"",
            "aria-live=\"polite\"", "aria-atomic", "one polite live region", "announced twice",
            "Highlight", "forced-colors", "focus-visible", "44 px", "--tm-touch-target",
            "prefers-reduced-motion", "struck through",
        })
        {
            doc.Should().Contain(term, $"the accessibility contract must document {term}");
        }
    }

    [Fact]
    public void Doc_DocumentsTheDismissalAndFocusContract()
    {
        var doc = Read("docs", "onboarding-checklist.md");

        doc.Should().Contain("hides the checklist locally in every case");
        doc.Should().Contain("new `Tasks` list identity");
        doc.Should().Contain("owns the focus afterwards");
        doc.Should().Contain("FocusAsync",
            "the component cannot return the focus itself, so the docs have to show the host how (review finding, UX #1)");
        doc.Should().Contain("protocol-relative", "the action href allow-list is documented, not implicit");
    }

    [Fact]
    public void Changelog_RecordsTheChecklistPhase()
    {
        var changelog = Read("CHANGELOG.md");
        var unreleased = changelog[..changelog.IndexOf("## 2.9.1 ", StringComparison.Ordinal)];

        foreach (var term in new[]
        {
            "TmOnboardingChecklist", "OnboardingTask", "CollapseCompletedGroups", "Dismissible",
            "docs/onboarding-checklist.md", "onboarding-checklist", "No breaking change",
        })
        {
            unreleased.Should().Contain(term, $"the unreleased changelog section must record {term}");
        }
    }

    [Fact]
    public void AgentsGuide_ListsTheComponent()
    {
        Read("AGENTS.md").Should().Contain("TmOnboardingChecklist").And.Contain("docs/onboarding-checklist.md");
    }
}
