using FluentAssertions;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// F4 review round 1 (H6, H8, H13, H18): the toolbar documentation carries the contracts a consumer and the
/// DocumentEditor migration (DE-1.2) rely on - a prose promise a test can read, so it cannot rot silently.
/// </summary>
public class ToolbarDocumentationTests
{
    private static string Read(params string[] relative)
        => File.ReadAllText(Path.Combine([FindRoot(), .. relative])).Replace("\r\n", "\n");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("TempoBlazor.slnx");
    }

    [Fact]
    public void ToolbarDoc_HasTheDe12MappingTable_AndTheCorePrerequisites()
    {
        var doc = Read("docs", "toolbar.md");

        doc.Should().Contain("| `ToolbarItemPriority` | `ToolbarButtonPriority`");
        doc.Should().Contain("toolbar-overflow.mjs");
        doc.Should().Contain("TmDocumentToolbarOverflowMenu");
        doc.Should().Contain("OverflowPresentation=\"PanelPresentation.Popover\"");
        doc.Should().Contain("Core prerequisites DE-1.2");
        doc.Should().Contain("`TmToolbarButton.Pressed`").And.Contain("`TmActionItem.Checked`").And.Contain("menuitemcheckbox");
        doc.Should().Contain("Group headers and separators");
        // F13/C3: the group prerequisites are done and the component documented.
        doc.Should().Contain("`TmToolbarGroup`").And.Contain("Groups survive into More");
        doc.Should().Contain("done (F13/C3)");
        // F14/C1: the non-button item contract is done and the component documented.
        doc.Should().Contain("`TmToolbarItem`").And.Contain("Non-button items").And.Contain("MenuItems");
        doc.Should().Contain("done (F14/C1)");
        // F14/C4-a: PreserveFocus is documented.
        doc.Should().Contain("PreserveFocus").And.Contain("pointerdown");
    }

    [Fact]
    public void ToolbarDoc_DocumentsTheItemTabStopContract()
    {
        var doc = Read("docs", "toolbar.md");

        // A text-entry control inside a TmToolbarItem stays its own Tab stop (Q-C2 = A).
        doc.Should().Contain("own Tab stop");
        // C6: a TmDropdown/TmSplitButton joins the roving set automatically.
        doc.Should().Contain("joins the roving set").And.Contain("automatically");
        // C5: the wrapper margin / wider-gap measurement is conservative.
        doc.Should().Contain("never over-commits").And.Contain("over-reserved");
    }

    [Fact]
    public void ToolbarDoc_DocumentsTheHooksTheLayoutContractAndTheDomOrder()
    {
        var doc = Read("docs", "toolbar.md");

        doc.Should().Contain("data-tm-toolbar-control").And.Contain("data-tm-toolbar-row").And.Contain("data-tm-toolbar-group");
        doc.Should().Contain("no intrinsic").And.Contain("flex: 1 1 auto; min-width: 0");
        doc.Should().Contain("OnFitChanged(maxVisible, orderedIds)");
        doc.Should().NotContain("Known limitation", "H3 fixed the registration-order limitation");
        doc.Should().Contain("`Pinned`").And.Contain("clamp");
        doc.Should().Contain("never compare or cast");
        doc.Should().Contain("TmBreakpoints.Sm");
    }

    [Fact]
    public void OverlaysDoc_HasTheMigrationRule_AboutDeletingOwnArrowHandlers()
    {
        Read("docs", "overlays.md").Should().Contain("deletes its own arrow handlers");
    }

    [Fact]
    public void Changelog_RecordsPinned_ZeroIntrinsicWidth_AndTheFirstItemFocus()
    {
        var changelog = Read("CHANGELOG.md");

        changelog.Should().Contain("ToolbarButtonPriority.Pinned");
        changelog.Should().Contain("Zero intrinsic width");
        changelog.Should().Contain("`TmDropdown`, `TmSplitButton` and `TmContextMenu` now move focus to their first enabled item");
        changelog.Should().NotContain("get it with no\n  per-component code", "the claim is true only because each host wires OnOpened -> focusFirst");
    }

    [Fact]
    public void LabelsDocumentation_IsUnified_OnTheToolbarWidthAndTmBreakpointsSm()
    {
        var enumDoc = Read("src", "Tempo.Blazor", "Components", "Toolbar", "ToolbarLabels.cs");
        var parameterDoc = Read("src", "Tempo.Blazor", "Components", "Toolbar", "TmToolbar.razor");

        enumDoc.Should().Contain("TmBreakpoints.Sm").And.Contain("640px");
        parameterDoc.Should().Contain("TmBreakpoints.Sm").And.Contain("640px");
    }
}