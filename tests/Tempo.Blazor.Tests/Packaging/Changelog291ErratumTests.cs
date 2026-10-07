using FluentAssertions;
using FluentAssertions.Execution;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// N327 — 2.9.0 shipped two changes that read as routine but behave as breaking: the
/// (0,1,0)→(0,2,0) selector compounding (<c>c67cd2cc</c>) silently demotes single-class host
/// overrides, and the top-layer overlay migration (<c>7196e8b2</c>) silently demotes host CSS
/// positioning of migrated panels. A changelog that calls neither breaking is the dishonesty
/// class 2.9.0 exists to remove, so the erratum's CONTENT is pinned here: both paragraphs, both
/// commit references, and both migration paths. <see cref="ChangelogReferencesExistingCommitsTests"/>
/// independently proves the backticked ids resolve — this test asserts they are printed where the
/// correction lives, inside the 2.9.1 section's Erratum heading.
/// </summary>
public sealed class Changelog291ErratumTests
{
    private static string Changelog =>
        File.ReadAllText(Path.Combine(ReleaseScriptInputReadTests.FindRepoRoot(), "CHANGELOG.md"));

    private static string Section291()
    {
        string changelog = Changelog;
        int start = changelog.IndexOf("## 2.9.1", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the 2.9.1 section must exist");
        int end = changelog.IndexOf("\n## 2.9.0", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the 2.9.1 section must be followed by 2.9.0");
        return changelog[start..end];
    }

    [Fact]
    public void Changelog291_HasErratumFor290BreakingChanges()
    {
        string section = Section291();

        using (new AssertionScope())
        {
            // The heading — inside 2.9.1, naming the release it corrects.
            int erratum = section.IndexOf("### Erratum 2.9.0", StringComparison.Ordinal);
            erratum.Should().BeGreaterThanOrEqualTo(0,
                "the 2.9.1 section must carry an Erratum 2.9.0 heading retroactively classing "
                + "the two silent-override changes as Breaking / Migration");

            // Paragraph one: the specificity change, its commit, and the migration path.
            section.Should().Contain("`c67cd2cc`",
                "the erratum must cite the compounding commit as a backticked reference");
            section.Should().Contain("(0,2,0)",
                "the erratum must state the new specificity so a host can measure its override");
            section.Should().Contain("single-class",
                "the losing host shape (a bare single-class override) must be named");
            section.Should().Contain(".tm-btn.my-btn",
                "the compound-selector migration path must be concrete");
            section.Should().Contain("--tm-",
                "the design-token migration path must be named");

            // Paragraph two: the overlay migration, its commit, and the migration path.
            section.Should().Contain("`7196e8b2`",
                "the erratum must cite the top-layer migration commit as a backticked reference");
            section.Should().Contain("top layer",
                "the erratum must state WHERE the panels moved (the browser top layer)");
            section.Should().Contain("inline",
                "the erratum must state that positioning is now inline — inline style beats "
                + "a stylesheet rule, which is exactly how the host override loses");
            section.Should().Contain("Placement",
                "the parameter-based migration path must name Placement");
            section.Should().Contain("Align",
                "the parameter-based migration path must name Align");

            // The migrated inventory must be enumerated — a host greps for its component.
            foreach (string component in new[]
            {
                "TmPopover", "TmDropdown", "TmFilterableDropdown", "TmContextMenu",
                "TmSplitButton", "TmDatePicker", "TmDateRangePicker", "TmDateTimePicker",
                "TmMultiSelect", "TmTagPicker", "TmEntityPicker", "TmNotificationBell",
                "TmColorPicker", "TmMultiColumnComboBox", "TmQueryInput", "TmDataTable",
            })
            {
                section.Should().Contain(component,
                    $"the migrated inventory must enumerate {component} so a consuming host "
                    + "can grep its own usage");
            }
        }
    }
}
