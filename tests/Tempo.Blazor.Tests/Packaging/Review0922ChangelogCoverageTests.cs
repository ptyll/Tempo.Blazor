using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// Coverage guard for the two breaking changes the 2026-09-22 review found already landed in
/// code but undocumented in the changelog (N199, N200 — owner decision F8,
/// <c>DEC-TEMPO-POPOVER-LAZY-MOUNT</c>): <see cref="TmOverlayPanel"/>'s lazy content mount and
/// <c>TmColorPicker</c>'s removed JSInvokable/JS surface. Unlike
/// <see cref="ChangelogReferencesExistingCommitsTests"/> this guard checks prose, not commit
/// claims — the changes it documents predate this entry, so no commit hash belongs in it.
/// </summary>
public class Review0922ChangelogCoverageTests
{
    private static string Changelog => File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, "CHANGELOG.md"));

    [Fact]
    public void BreakingMigration_Documents_OverlayPanelLazyMount()
        => Changelog.Should().Contain("mounts only while the panel is open",
            "F8 (DEC-TEMPO-POPOVER-LAZY-MOUNT) requires this in CHANGELOG, not just decided");

    [Fact]
    public void BreakingMigration_Documents_TmColorPickerRemovedApi()
        => Changelog.Should().Contain("CloseFromGlobalAsync",
            "the removed TmColorPicker JSInvokable API must be named so a consumer upgrading can grep for it");
}
