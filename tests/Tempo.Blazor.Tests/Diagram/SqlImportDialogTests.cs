using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class SqlImportDialogTests : DiagramTestBase
{
    private readonly FakeTimeProvider _time = new();

    public SqlImportDialogTests() => Services.AddSingleton<TimeProvider>(_time);

    /// <summary>Types, crosses the debounce deadline and drains the InvokeAsync hop.</summary>
    private async Task TypeAndSettleAsync(IRenderedComponent<TmDiagramSqlImportDialog> cut, string text)
    {
        cut.Find("textarea").Input(text);
        _time.Advance(TimeSpan.FromMilliseconds(TmDiagramSqlImportDialog.PreviewDebounceMs + 1));
        await cut.InvokeAsync(() => { });
    }

    [Fact]
    public async Task SqlImportDialog_DisablesImportForSqlWithoutTables()
    {
        var cut = Render<TmDiagramSqlImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (SqlImportResult r) => { }));

        var textarea = cut.Find("textarea");
        textarea.Input("NOT A VALID SQL");

        _time.Advance(TimeSpan.FromMilliseconds(TmDiagramSqlImportDialog.PreviewDebounceMs + 1));
        await cut.InvokeAsync(() => { });

        // Live preview reports zero tables and the import stays disabled
        cut.Find(".tm-diagram-sql-import__preview").TextContent.Should().Contain("Tables: 0");
        var importBtn = cut.FindAll("button").First(b => b.TextContent == "Import");
        importBtn.HasAttribute("disabled").Should().BeTrue(
            "importing must be impossible while the parsed SQL has zero tables");
    }

    [Fact]
    public async Task SqlImportDialog_RendersPreviewForValidSql()
    {
        var cut = Render<TmDiagramSqlImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (SqlImportResult r) => { }));

        var sql = "CREATE TABLE Users (Id INT PRIMARY KEY, Name VARCHAR(255));";
        await TypeAndSettleAsync(cut, sql);

        var title = cut.Find(".tm-modal-title");
        title.TextContent.Should().Be("Import SQL to ER Diagram");

        // Debounced live preview parses the typed DDL once typing pauses.
        cut.Find(".tm-diagram-sql-import__preview").TextContent.Should().Contain("Tables: 1");
    }
}
