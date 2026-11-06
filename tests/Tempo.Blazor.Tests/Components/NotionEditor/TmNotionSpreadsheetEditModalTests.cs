using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Components.NotionEditor.Blocks.TempoBlocks;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// SS-MOD-01..05: TmNotionSpreadsheetEditModal — loading, save, discard.
/// </summary>
public class TmNotionSpreadsheetEditModalTests : LocalizationTestBase
{
    private static SpreadsheetWorkbook MakeWorkbook()
    {
        var wb = new SpreadsheetWorkbook();
        wb.AddSheet("Sheet1");
        return wb;
    }

    // ── SS-MOD-01: Zobrazí spinner při načítání ───────────────────────────

    [Fact]
    public void Render_InitialState_ShowsLoadingSpinner()
    {
        var provider = Substitute.For<ISpreadsheetDocumentProvider>();
        var tcs = new TaskCompletionSource<SpreadsheetWorkbook?>();
        provider.GetSpreadsheetDocumentAsync(Arg.Any<Guid>()).Returns(tcs.Task);

        var cut = Render<TmNotionSpreadsheetEditModal>(p => p
            .Add(x => x.SpreadsheetDocumentId, Guid.NewGuid())
            .Add(x => x.Provider, provider));

        cut.Find(".tm-notion-spreadsheet-edit-modal__loading").Should().NotBeNull();
        cut.FindComponents<TmSpreadsheet>().Should().BeEmpty();
    }

    // ── SS-MOD-02: Po načtení zobrazí TmSpreadsheet ──────────────────────

    [Fact]
    public async Task Render_AfterLoad_ShowsSpreadsheet()
    {
        var provider = Substitute.For<ISpreadsheetDocumentProvider>();
        provider.GetSpreadsheetDocumentAsync(Arg.Any<Guid>())
                .Returns(Task.FromResult<SpreadsheetWorkbook?>(MakeWorkbook()));

        var cut = Render<TmNotionSpreadsheetEditModal>(p => p
            .Add(x => x.SpreadsheetDocumentId, Guid.NewGuid())
            .Add(x => x.Provider, provider));

        await cut.InvokeAsync(() => { }); // flush async init

        cut.FindAll(".tm-notion-spreadsheet-edit-modal__loading").Should().BeEmpty();
        cut.FindComponents<TmSpreadsheet>().Should().HaveCount(1);
    }

    // ── SS-MOD-03: Save zavolá providera a invokuje OnSaved ──────────────

    [Fact]
    public async Task Save_CallsProviderAndInvokesCallback()
    {
        var documentId = Guid.NewGuid();
        var provider = Substitute.For<ISpreadsheetDocumentProvider>();
        var workbook = MakeWorkbook();
        provider.GetSpreadsheetDocumentAsync(documentId)
                .Returns(Task.FromResult<SpreadsheetWorkbook?>(workbook));
        provider.SaveSpreadsheetDocumentAsync(documentId, Arg.Any<SpreadsheetWorkbook>())
                .Returns(ci => Task.FromResult(ci.Arg<SpreadsheetWorkbook>()));

        SpreadsheetWorkbook? saved = null;
        var cut = Render<TmNotionSpreadsheetEditModal>(p => p
            .Add(x => x.SpreadsheetDocumentId, documentId)
            .Add(x => x.Provider, provider)
            .Add(x => x.OnSaved, EventCallback.Factory.Create<SpreadsheetWorkbook>(this, wb => saved = wb)));

        await cut.InvokeAsync(() => { });

        var btn = cut.Find(".tm-notion-spreadsheet-edit-modal__btn--primary");
        await cut.InvokeAsync(() => btn.Click());

        await provider.Received(1).SaveSpreadsheetDocumentAsync(documentId, Arg.Any<SpreadsheetWorkbook>());
        saved.Should().NotBeNull();
    }

    // ── SS-MOD-04: Discard nezavolá providera, invokuje OnDiscarded ──────

    [Fact]
    public async Task Discard_DoesNotCallProvider_InvokesCallback()
    {
        var provider = Substitute.For<ISpreadsheetDocumentProvider>();
        provider.GetSpreadsheetDocumentAsync(Arg.Any<Guid>())
                .Returns(Task.FromResult<SpreadsheetWorkbook?>(MakeWorkbook()));

        var discardCalled = false;
        var cut = Render<TmNotionSpreadsheetEditModal>(p => p
            .Add(x => x.SpreadsheetDocumentId, Guid.NewGuid())
            .Add(x => x.Provider, provider)
            .Add(x => x.OnDiscarded, EventCallback.Factory.Create(this, () => discardCalled = true)));

        await cut.InvokeAsync(() => { });

        var btn = cut.Find(".tm-notion-spreadsheet-edit-modal__btn--discard");
        await cut.InvokeAsync(() => btn.Click());

        await provider.DidNotReceive().SaveSpreadsheetDocumentAsync(Arg.Any<Guid>(), Arg.Any<SpreadsheetWorkbook>());
        discardCalled.Should().BeTrue();
    }

    // ── SS-MOD-05: Null provider nevyhodí výjimku ─────────────────────────

    [Fact]
    public async Task Render_NullProvider_RendersWithEmptyWorkbook()
    {
        var cut = Render<TmNotionSpreadsheetEditModal>(p => p
            .Add(x => x.SpreadsheetDocumentId, Guid.NewGuid())
            .Add(x => x.Provider, (ISpreadsheetDocumentProvider?)null));

        await cut.InvokeAsync(() => { });

        cut.FindComponents<TmSpreadsheet>().Should().HaveCount(1);
    }

    [Fact]
    public async Task Footer_PutsDiscardBeforeSave_AndSaveStaysEnabled()
    {
        var cut = Render<TmNotionSpreadsheetEditModal>(p => p
            .Add(x => x.SpreadsheetDocumentId, Guid.NewGuid()));

        await cut.InvokeAsync(() => { });

        var footer = cut.Find("[data-testid='notion-tempo-modal-footer']").InnerHtml;
        footer.IndexOf("notion-tempo-modal-discard", StringComparison.Ordinal)
            .Should().BeLessThan(footer.IndexOf("notion-tempo-modal-save", StringComparison.Ordinal));
        cut.Find("[data-testid='notion-tempo-modal-save']").HasAttribute("disabled").Should().BeFalse();
    }
}
