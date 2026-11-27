using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

public sealed class TmDocumentTableToolbarTests : LocalizationTestBase
{
    [Fact]
    public void Toolbar_RendersTableCommands()
    {
        var cut = Render<TmDocumentTableToolbar>();

        cut.Find("[data-testid='document-table-toolbar']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-insert-row-before']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-insert-row-after']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-insert-column-before']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-insert-column-after']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-delete-row']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-delete-column']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-merge-cells']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-split-cell']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-table-properties']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-toolbar-cell-properties']").Should().NotBeNull();
    }

    [Fact]
    public void TablePropertiesPanel_RaisesLayoutChanged()
    {
        TableLayoutContent? received = null;
        var cut = Render<TmDocumentTablePropertiesPanel>(parameters => parameters
            .Add(p => p.Layout, new TableLayoutContent { Width = 320, Alignment = TableHorizontalAlignment.Left })
            .Add(p => p.LayoutChanged, value => received = value));

        cut.Find("[data-testid='document-table-properties-width']").Change("480");

        received.Should().NotBeNull();
        received!.Width.Should().Be(480);
    }

    [Fact]
    public void TablePropertiesPanel_TestIdRoot_DrivesTestIds()
    {
        var cut = Render<TmDocumentTablePropertiesPanel>(parameters => parameters
            .Add(p => p.Layout, new TableLayoutContent { Width = 200 })
            .Add(p => p.TestIdRoot, "tbl"));

        cut.Find("[data-testid='tbl-panel']").Should().NotBeNull();
        cut.Find("[data-testid='tbl-width']").Should().NotBeNull();
    }

#pragma warning disable CS0618 // exercising the deprecated alias on purpose
    [Fact]
    public void TablePropertiesPanel_DeprecatedTestIdPrefix_ForwardsToTestIdRoot()
    {
        var cut = Render<TmDocumentTablePropertiesPanel>(parameters => parameters
            .Add(p => p.Layout, new TableLayoutContent { Width = 200 })
            .Add(p => p.TestIdPrefix, "legacy"));

        cut.Find("[data-testid='legacy-panel']").Should().NotBeNull();
    }
#pragma warning restore CS0618

    [Fact]
    public void TablePropertiesPanel_RendersAlignmentControls()
    {
        var cut = Render<TmDocumentTablePropertiesPanel>(parameters => parameters
            .Add(p => p.Layout, new TableLayoutContent { Alignment = TableHorizontalAlignment.Center }));

        cut.Find("[data-testid='document-table-properties-align-left']").Should().NotBeNull();
        cut.Find("[data-testid='document-table-properties-align-center']")
            .ClassList.Should().Contain("tm-document-table-properties__segment--active");
        cut.Find("[data-testid='document-table-properties-align-right']").Should().NotBeNull();
    }

    [Fact]
    public void CellPropertiesPanel_RaisesCellChanged()
    {
        TableCellContent? received = null;
        var cut = Render<TmDocumentCellPropertiesPanel>(parameters => parameters
            .Add(p => p.Cell, new TableCellContent { Id = "cell-1" })
            .Add(p => p.CellChanged, value => received = value));

        // The background is the core colour picker (phase 6): a pick raises the value through its ValueChanged.
        var picker = cut.FindComponent<Tempo.Blazor.Components.Inputs.TmColorPicker>();
        cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync("#ff0000"));

        received.Should().NotBeNull();
        received!.BackgroundColor.Should().Be("#ff0000");
    }

    [Fact]
    public void CellPropertiesPanel_RendersSpanInfo()
    {
        var cut = Render<TmDocumentCellPropertiesPanel>(parameters => parameters
            .Add(p => p.Cell, new TableCellContent { ColumnSpan = 2, RowSpan = 3 }));

        cut.Find("[data-testid='document-cell-properties-span-info']")
            .TextContent.Should().Contain("2").And.Contain("3");
    }
}
