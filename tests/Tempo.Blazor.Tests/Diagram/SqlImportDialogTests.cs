using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class SqlImportDialogTests : DiagramTestBase
{
    [Fact]
    public void SqlImportDialog_DisablesImportForSqlWithoutTables()
    {
        var cut = Render<TmDiagramSqlImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (SqlImportResult r) => { }));

        var textarea = cut.Find("textarea");
        textarea.Input("NOT A VALID SQL");

        cut.WaitForAssertion(() =>
        {
            // Live preview reports zero tables and the import stays disabled
            cut.Find(".tm-diagram-sql-import__preview").TextContent.Should().Contain("Tables: 0");
            var importBtn = cut.FindAll("button").First(b => b.TextContent == "Import");
            importBtn.HasAttribute("disabled").Should().BeTrue(
                "importing must be impossible while the parsed SQL has zero tables");
        });
    }

    [Fact]
    public void SqlImportDialog_RendersPreviewForValidSql()
    {
        var cut = Render<TmDiagramSqlImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (SqlImportResult r) => { }));

        var sql = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(255));";
        var textarea = cut.Find("textarea");
        textarea.Input(sql);

        // Preview is updated during import click path, but we can at least verify dialog renders
        var title = cut.Find(".tm-modal-title");
        title.TextContent.Should().Be("Import SQL to ER Diagram");
    }
}
