using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// SS-BLK-01..08: TmNotionSpreadsheetBlock — placeholder, embedded view, edit button, modal lifecycle.
/// </summary>
public class TmNotionSpreadsheetBlockTests : LocalizationTestBase
{
    private NotionEditorContext BuildContext(ISpreadsheetDocumentProvider? provider = null)
        => new()
        {
            DataProvider  = Substitute.For<INotionDataProvider>(),
            BlockService = Substitute.For<INotionEditorBlockService>(),
            SpreadsheetDocumentProvider = provider
        };

    private static ISpreadsheetBlockContent MakeContent(Guid? id = null) =>
        new SpreadsheetBlockContent { SpreadsheetDocumentId = id ?? Guid.NewGuid() };

    private static ISpreadsheetDocumentProvider MockProvider(SpreadsheetWorkbook? workbook = null)
    {
        var provider = Substitute.For<ISpreadsheetDocumentProvider>();
        var wb = workbook ?? new SpreadsheetWorkbook();
        if (wb.Sheets.Count == 0) wb.AddSheet("Sheet1");
        provider.GetSpreadsheetDocumentAsync(Arg.Any<Guid>())
                .Returns(Task.FromResult<SpreadsheetWorkbook?>(wb));
        return provider;
    }

    // ── SS-BLK-01: Bez Content, ReadOnly=false → zobrazí create button ────

    [Fact]
    public void Render_NullContent_NotReadOnly_ShowsCreateButton()
    {
        var ctx = BuildContext();
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, (ISpreadsheetBlockContent?)null)
            .Add(x => x.ReadOnly, false));

        cut.Find(".tm-notion-media-upload-zone--spreadsheet").Should().NotBeNull();
    }

    // ── SS-BLK-02: Bez Content, ReadOnly=true → zobrazí empty placeholder ─

    [Fact]
    public void Render_NullContent_ReadOnly_ShowsEmptyPlaceholder()
    {
        var ctx = BuildContext();
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, (ISpreadsheetBlockContent?)null)
            .Add(x => x.ReadOnly, true));

        cut.Find(".tm-notion-media-empty-placeholder").Should().NotBeNull();
        cut.FindAll(".tm-notion-media-upload-zone--spreadsheet").Should().BeEmpty();
    }

    // ── SS-BLK-03: S Content → zavolá provider a zobrazí TmSpreadsheet ───

    [Fact]
    public async Task Render_WithContent_LoadsWorkbookAndShowsSpreadsheet()
    {
        var provider = MockProvider();
        var ctx = BuildContext(provider);
        var content = MakeContent();

        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, content)
            .Add(x => x.ReadOnly, false));

        await cut.InvokeAsync(() => { });

        await provider.Received().GetSpreadsheetDocumentAsync(content.SpreadsheetDocumentId);
        cut.FindComponents<TmSpreadsheet>().Should().HaveCount(1);
    }

    // ── SS-BLK-04: ReadOnly=false + Content → edit button je viditelný ───

    [Fact]
    public async Task Render_WithContent_NotReadOnly_ShowsEditButton()
    {
        var ctx = BuildContext(MockProvider());
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, MakeContent())
            .Add(x => x.ReadOnly, false));

        await cut.InvokeAsync(() => { });

        cut.Find(".tm-notion-spreadsheet-block__edit-btn").Should().NotBeNull();
    }

    // ── SS-BLK-05: ReadOnly=true + Content → edit button není viditelný ──

    [Fact]
    public async Task Render_WithContent_ReadOnly_HidesEditButton()
    {
        var ctx = BuildContext(MockProvider());
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, MakeContent())
            .Add(x => x.ReadOnly, true));

        await cut.InvokeAsync(() => { });

        cut.FindAll(".tm-notion-spreadsheet-block__edit-btn").Should().BeEmpty();
    }

    // ── Review round (phase 6): the edit modal keeps the container contract ─

    [Fact]
    public async Task EditModal_DoesNotPinLayoutMode_AndOwnsItsEditorWrapper()
    {
        // The modal is a FULLSCREEN surface: the editor must resolve its own container (a phone at 390, a
        // tablet/desktop at 1024+) — pinning LayoutMode here would break the container contract — and the
        // editor sits in a wrapper THIS component owns (a class handed to TmSpreadsheet would land on the
        // child's root with the child's scope id, so the scoped rule would be dead).
        var ctx = BuildContext(MockProvider());
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, MakeContent())
            .Add(x => x.ReadOnly, false));

        await cut.InvokeAsync(() => { });
        await cut.InvokeAsync(() => cut.Find(".tm-notion-spreadsheet-block__edit-btn").Click());

        var wrapper = cut.Find(".tm-notion-spreadsheet-edit-modal__editor");
        wrapper.Should().NotBeNull("the editor wrapper is an element the modal itself renders");

        // Scoped to the MODAL: the block's own preview is a DynamicComponent as well.
        var modal = cut.FindComponents<TmNotionSpreadsheetEditModal>().First();
        var dynamicComponent = modal.FindComponents<DynamicComponent>().First();
        dynamicComponent.Instance.Parameters.Should().NotContainKey("LayoutMode",
            "the fullscreen editor resolves its own container");
        dynamicComponent.Instance.Parameters.Should().NotContainKey("Class",
            "a Class passed to the child would be dead for the modal's scoped CSS");
        dynamicComponent.Instance.Parameters.Should().ContainKey("Height");
    }

    // ── SS-BLK-06: Klik na Edit → renderuje modal ──────────────────────────

    [Fact]
    public async Task ClickEdit_OpensModal()
    {
        var ctx = BuildContext(MockProvider());
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, MakeContent())
            .Add(x => x.ReadOnly, false));

        await cut.InvokeAsync(() => { });

        var editBtn = cut.Find(".tm-notion-spreadsheet-block__edit-btn");
        await cut.InvokeAsync(() => editBtn.Click());

        cut.FindComponents<TmNotionSpreadsheetEditModal>().Should().HaveCount(1);
    }

    // ── SS-BLK-07: OnDiscarded → modal se zavře ─────────────────────────────

    [Fact]
    public async Task OnDiscarded_ClosesModal()
    {
        var ctx = BuildContext(MockProvider());
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, MakeContent())
            .Add(x => x.ReadOnly, false));

        await cut.InvokeAsync(() => { });
        var editBtn = cut.Find(".tm-notion-spreadsheet-block__edit-btn");
        await cut.InvokeAsync(() => editBtn.Click());
        cut.FindComponents<TmNotionSpreadsheetEditModal>().Should().HaveCount(1);

        // Discard z modalu
        var discardBtn = cut.Find(".tm-notion-spreadsheet-edit-modal__btn--discard");
        await cut.InvokeAsync(() => discardBtn.Click());

        cut.FindComponents<TmNotionSpreadsheetEditModal>().Should().BeEmpty();
    }

    // ── SS-BLK-08: Root element má onfocus handler ─────────────────────────

    [Fact]
    public void Render_HasFocusHandler()
    {
        var ctx = BuildContext();
        var cut = Render<TmNotionSpreadsheetBlock>(p => p
            .AddCascadingValue(ctx)
            .Add(x => x.Content, (ISpreadsheetBlockContent?)null)
            .Add(x => x.ReadOnly, false));

        cut.Find(".tm-notion-spreadsheet-block").HasAttribute("blazor:onfocus").Should().BeTrue();
    }
}
