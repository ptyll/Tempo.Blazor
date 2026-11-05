using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Components.NotionEditor.Blocks.Database;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 4 (DB-3): every database dialog rides the core <see cref="TmModal"/> (anchored
/// <see cref="TmOverlayPanel"/> for the properties/template popovers) — visible title, ×, Escape,
/// focus return, fixed footer, destructive actions separated, mobile always a sheet. No own
/// backdrop or z-index anywhere.
/// </summary>
public sealed class TmNotionDbDialogsTests : LocalizationTestBase
{
    public TmNotionDbDialogsTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Cancel"] = "Cancel",
            ["Tm_Delete"] = "Delete",
            ["TmNotionDbFieldEditor_Title"] = "Edit field",
            ["TmNotionDbFieldEditor_Close"] = "Close",
            ["TmNotionDbFieldEditor_FieldName"] = "Name",
            ["TmNotionDbFieldEditor_FieldType"] = "Type",
            ["TmNotionDbFieldEditor_Options"] = "Options",
            ["TmNotionDbFieldEditor_AddOption"] = "Add option",
            ["TmNotionDbFieldEditor_RemoveOption"] = "Remove option",
            ["TmNotionDbFieldEditor_Duplicate"] = "Duplicate",
            ["TmNotionDbFieldEditor_Delete"] = "Delete",
            ["TmNotionDbFieldEditor_Save"] = "Save",
            ["TmNotionDbFieldEditor_TypeWarning"] = "Type change warning",
            ["TmNotionDbFieldEditor_MoveOptionUp"] = "Move up",
            ["TmNotionDbFieldEditor_MoveOptionDown"] = "Move down",
            ["TmNotionDbFieldEditor_Color_Gray"] = "Gray",
            ["TmNotionDbFieldEditor_Color_Red"] = "Red",
            ["TmNotionDbFieldEditor_Color_Orange"] = "Orange",
            ["TmNotionDbFieldEditor_Color_Yellow"] = "Yellow",
            ["TmNotionDbFieldEditor_Color_Green"] = "Green",
            ["TmNotionDbFieldEditor_Color_Blue"] = "Blue",
            ["TmNotionDbFieldEditor_Color_Purple"] = "Purple",
            ["TmNotionDbFieldEditor_Color_Pink"] = "Pink",
            ["TmNotionDbFieldEditor_Color_Brown"] = "Brown",
            ["TmNotionDb_Done"] = "Done",
            ["TmNotionDbImportExport_Title"] = "Import / Export",
            ["TmNotionDbImportExport_Close"] = "Close",
            ["TmNotionDbImportExport_Import"] = "Import",
            ["TmNotionDbImportExport_Export"] = "Export",
            ["TmNotionDbImportExport_ImportTab"] = "Import",
            ["TmNotionDbImportExport_ExportTab"] = "Export",
            ["TmNotionDbImportExport_ChooseFile"] = "Choose file",
            ["TmNotionDbRecordDetail_Close"] = "Close",
            ["TmNotionDbRecordDetail_Delete"] = "Delete",
            ["TmNotionDbRecordDetail_DeleteConfirm"] = "Delete this record?",
            ["TmNotionDbRecordDetail_OpenAsPage"] = "Open as full page",
            ["TmNotionDbRecordDetail_Properties"] = "Properties",
            ["TmNotionDbRecordDetail_Untitled"] = "Untitled",
            ["TmNotionDbTemplateEditor_Breadcrumb"] = "Template",
            ["TmNotionDbTemplateEditor_EditTitle"] = "Edit template",
            ["TmNotionDbTemplateEditor_Close"] = "Close",
            ["TmNotionDatabaseBlock_Properties"] = "Properties",
            ["TmNotionDatabaseBlock_PropertiesSubtitle"] = "Show, hide and reorder fields",
            ["TmNotionDatabaseBlock_ShowAll"] = "Show all",
            ["TmNotionDatabaseBlock_HideAll"] = "Hide all",
            ["TmNotionDatabaseBlock_FieldLocked"] = "Primary field is always visible",
        });
    }

    // ── Field editor ─────────────────────────────────────────────────────────

    [Fact]
    public void FieldEditor_RidesTheCoreModal_WithTitleAndFooter()
    {
        var cut = RenderFieldEditor();

        cut.FindComponent<TmModal>().Instance.Title.Should().Be("Edit field");
        cut.Find(".tm-modal-close").Should().NotBeNull();
        var footer = cut.Find(".tm-modal-footer");
        footer.TextContent.Should().Contain("Save");
    }

    [Fact]
    public void FieldEditor_Escape_RaisesOnClose()
    {
        var closed = 0;
        var cut = RenderFieldEditor(onClose: () => closed++);

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        closed.Should().Be(1);
    }

    [Fact]
    public void FieldEditor_Mobile_PresentsAsSheet()
    {
        var cut = RenderFieldEditor(mode: TmLayoutMode.Mobile);

        cut.Find(".tm-modal--sheet").Should().NotBeNull();
    }

    [Fact]
    public void FieldEditor_ReadOnly_HidesDestructiveActions()
    {
        var cut = RenderFieldEditor(readOnly: true);

        cut.FindAll(".tm-dbfe__btn--danger").Should().BeEmpty();
        cut.Markup.Should().NotContain("Duplicate");
    }

    [Fact]
    public void FieldEditor_ArrowButtons_ReorderOptions_WithoutDragging()
    {
        var field = SelectField();
        var cut = RenderFieldEditor(field: field);

        var rows = cut.FindAll(".tm-dbfe__opt-row");
        rows.Should().HaveCount(3);

        // Move the second option ("Beta") up — the non-drag alternative to DnD.
        rows[1].QuerySelector(".tm-dbfe__opt-up")!.Click();

        var names = cut.FindAll(".tm-dbfe__opt-chip").Select(c => c.TextContent.Trim()).ToList();
        names.Should().Equal("Beta", "Alpha", "Gamma");
    }

    [Fact]
    public void FieldEditor_ColorDots_HaveLocalizedColorNames()
    {
        var cut = RenderFieldEditor(field: SelectField());

        var dot = cut.Find(".tm-dbfe__color-dot--red");
        dot.GetAttribute("title").Should().Be("Red");
    }

    // ── Import / export ──────────────────────────────────────────────────────

    [Fact]
    public void ImportExport_RidesTheCoreModal_WithTitleAndFooter()
    {
        var cut = RenderImportExport();

        cut.FindComponent<TmModal>().Instance.Title.Should().Be("Import / Export");
        cut.Find(".tm-modal-footer").Should().NotBeNull();
    }

    [Fact]
    public void ImportExport_Escape_RaisesOnClose()
    {
        var closed = 0;
        var cut = RenderImportExport(onClose: () => closed++);

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        closed.Should().Be(1);
    }

    [Fact]
    public void ImportExport_Mobile_PresentsAsSheet_WithCardMapping()
    {
        var cut = RenderImportExport(mode: TmLayoutMode.Mobile);

        cut.Find(".tm-modal--sheet").Should().NotBeNull();

        var css = File.ReadAllText(CssPath("TmNotionDbImportExport.razor.css"));
        css.Should().Contain("tm-dbie__map-row", "the mapping rows exist")
            .And.Contain("max-width: 600px", "the mapping rows stack as cards under 600 px");
    }

    // ── Record detail ────────────────────────────────────────────────────────

    [Fact]
    public void RecordDetail_RidesTheCoreModalLarge_AndEscapeCloses()
    {
        var closed = 0;
        var cut = RenderRecordDetail(onClose: () => closed++);

        var modal = cut.FindComponent<TmModal>();
        modal.Instance.Size.Should().Be(ModalSize.Large);
        cut.Find(".tm-modal-footer").Should().NotBeNull();

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();
        closed.Should().Be(1);
    }

    [Fact]
    public void RecordDetail_FooterCloseCommitsThePendingTitle_BeforeOnClose()
    {
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<IDatabaseRecord>())
            .Returns(call => call.Arg<IDatabaseRecord>());
        var closed = 0;
        var cut = RenderRecordDetail(provider, onClose: () => closed++);

        cut.Find(".tm-dbrd__title-input").Input("Renamed record");
        cut.Find(".tm-dbrd__footer-main button").Click();

        cut.WaitForAssertion(() =>
        {
            provider.Received(1).UpdateRecordAsync(Arg.Any<string>(), Arg.Any<IDatabaseRecord>());
            closed.Should().Be(1);
        });
    }

    [Fact]
    public void RecordDetail_EscapeCommitsAPendingTitle_AndRaisesOnRecordUpdated()
    {
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<IDatabaseRecord>())
            .Returns(call => call.Arg<IDatabaseRecord>());
        IDatabaseRecord? updated = null;
        var cut = RenderRecordDetail(provider, onRecordUpdated: r => updated = r);

        cut.Find(".tm-dbrd__title-input").Input("Typed but never blurred");

        // Escape never blurs the textarea — the close path must commit the pending title itself.
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            provider.Received(1).UpdateRecordAsync(Arg.Any<string>(), Arg.Any<IDatabaseRecord>());
            updated.Should().NotBeNull();
        });
    }

    [Fact]
    public void RecordDetail_WhileACellEdits_EscapeDoesNotCloseTheDialog()
    {
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.UpdateRecordAsync(Arg.Any<string>(), Arg.Any<IDatabaseRecord>())
            .Returns(call => call.Arg<IDatabaseRecord>());
        var closed = 0;
        var cut = RenderRecordDetail(provider, withSelectField: true, onClose: () => closed++);

        // Enter cell edit mode — the modal must hand Escape to the cell (cancel), not close.
        cut.Find(".tm-dbrd__prop-value").Click();
        cut.WaitForAssertion(() =>
            cut.FindComponent<TmModal>().Instance.CloseOnEscape.Should().BeFalse());

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();
        closed.Should().Be(0);
    }

    [Fact]
    public void RecordDetail_Mobile_PresentsAsFullscreenSheet()
    {
        var cut = RenderRecordDetail(mode: TmLayoutMode.Mobile);

        cut.Find(".tm-modal--fullscreen-presentation").Should().NotBeNull();
    }

    // ── Template editor ──────────────────────────────────────────────────────

    [Fact]
    public void TemplateEditor_RidesTheCoreModal_WithEditTemplateTitle()
    {
        var cut = RenderTemplateEditor();

        var modal = cut.FindComponent<TmModal>();
        modal.Instance.Title.Should().Be("Edit template");
        modal.Instance.Subtitle.Should().Be("Hiring DB");
    }

    [Fact]
    public void TemplateEditor_EscapeCommitsAPendingName_BeforeOnClose()
    {
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.UpdateTemplateAsync(Arg.Any<string>(), Arg.Any<IDatabaseRecordTemplate>())
            .Returns(call => call.Arg<IDatabaseRecordTemplate>());
        IDatabaseRecordTemplate? saved = null;
        var closed = 0;
        var cut = RenderTemplateEditor(provider, onTemplateSaved: t => saved = t, onClose: () => closed++);

        cut.Find(".tm-dbte__name-input").Input("Weekly sync");

        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync())
            .GetAwaiter().GetResult();

        cut.WaitForAssertion(() =>
        {
            provider.Received(1).UpdateTemplateAsync(Arg.Any<string>(), Arg.Any<IDatabaseRecordTemplate>());
            saved.Should().NotBeNull();
            closed.Should().Be(1);
        });
    }

    [Fact]
    public void FieldEditor_Footer_GhostCloseBeforeSave_DangerDeleteSeparated()
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Tags", Type = DatabaseFieldType.Select, IsPrimary = false };
        var cut = RenderFieldEditor(field);

        var footer = cut.Find(".tm-dbfe__footer");
        // Left group: the destructive action, separated from the confirm group.
        footer.QuerySelector(".tm-dbfe__footer-danger .tm-dbfe__btn-delete").Should().NotBeNull();
        // Right group: Ghost Close renders BEFORE the primary Save.
        var right = footer.QuerySelector(".tm-dbfe__footer-right");
        right.Should().NotBeNull();
        var rightButtons = right.QuerySelectorAll("button");
        rightButtons.Should().HaveCount(2);
        rightButtons[0].ClassList.Should().Contain("tm-dbfe__btn-close");
        rightButtons[1].ClassList.Should().Contain("tm-dbfe__btn-save");
    }

    [Fact]
    public void RecordDetail_AutosaveSurfaces_ReadDone_NotClose()
    {
        var cut = RenderRecordDetail();

        cut.Find(".tm-dbrd__footer-main button").TextContent.Should().Be("Done");
    }

    // ── Properties popover ───────────────────────────────────────────────────

    [Fact]
    public void PropertiesPanel_IsAnAnchoredOverlayPopover_WithTogglesAndLockedPrimary()
    {
        var dbId = Guid.NewGuid();
        var titleField = new DatabaseField
        {
            Id = Guid.NewGuid(), Name = "Name", Type = DatabaseFieldType.Text, IsPrimary = true, IsVisible = true
        };
        var statusField = new DatabaseField
        {
            Id = Guid.NewGuid(), Name = "Status", Type = DatabaseFieldType.Select, IsPrimary = false, IsVisible = true
        };
        var provider = Substitute.For<INotionDatabaseProvider>();
        provider.GetFieldsAsync(Arg.Any<string>()).Returns(new IDatabaseField[] { titleField, statusField });
        provider.GetViewsAsync(Arg.Any<string>()).Returns(new IDatabaseView[]
        {
            new DatabaseViewStub { Id = Guid.NewGuid(), Name = "All", Type = DatabaseViewType.Table }
        });
        provider.GetRecordsAsync(Arg.Any<string>(), Arg.Any<INotionDatabaseFilter?>(),
                Arg.Any<IEnumerable<NotionDatabaseSort>?>(), Arg.Any<NotionDatabaseGrouping?>(),
                Arg.Any<int>(), Arg.Any<int>())
            .Returns(new PagedResult<IDatabaseRecord> { Items = [], TotalCount = 0, Page = 1, PageSize = 50 });

        var cut = Render<TmNotionDatabaseBlock>(p => p
            .Add(x => x.Block, new PageBlock
            {
                Id = Guid.NewGuid(),
                Type = BlockType.InlineDatabase,
                Content = new InlineDatabaseBlockContent { DatabaseId = dbId }
            })
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = Substitute.For<INotionEditorBlockService>(),
                DatabaseProvider = provider
            }));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid=notion-db-view-tab]").Should().NotBeEmpty());
        cut.Find(".tm-db__tool-btn[data-tool='properties']").Click();

        cut.WaitForAssertion(() => cut.FindComponent<TmOverlayPanel>().Should().NotBeNull());
        var panel = cut.Find(".tm-db__panel--fields");
        panel.Should().NotBeNull();
        panel.GetAttribute("aria-label").Should().Be("Properties");

        // TmToggle drives visibility; the primary field renders locked.
        cut.FindComponents<TmToggle>().Should().HaveCountGreaterThan(0);
        cut.Find(".tm-db__field-row--primary .tm-db__field-lock").Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class DatabaseViewStub : IDatabaseView
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public DatabaseViewType Type { get; init; }
        public INotionDatabaseFilter? Filter => null;
        public IReadOnlyList<NotionDatabaseSort> Sorts => [];
        public NotionDatabaseGrouping? Grouping => null;
        public IReadOnlyList<Guid> VisibleFieldIds => [];
        public IDatabaseViewConfig? Config => null;
    }

    private sealed class EmptyBlockService : INotionEditorBlockService
    {
        public Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId) => Task.FromResult(Enumerable.Empty<IPageBlock>());
        public Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId) => Task.FromResult(Enumerable.Empty<IPageBlock>());
        public Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId) => throw new NotSupportedException();
        public Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId) => throw new NotSupportedException();
        public Task UpdateBlockAsync(IPageBlock block) => Task.CompletedTask;
        public Task DeleteBlockAsync(string blockId) => Task.CompletedTask;
        public Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds) => Task.CompletedTask;
        public Task MoveBlockAsync(MoveNotionBlockRequest request) => Task.CompletedTask;
        public Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId) => Task.CompletedTask;
        public Task<IPageBlock> DuplicateBlockAsync(string blockId) => throw new NotSupportedException();
        public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType) => throw new NotSupportedException();
        public Task<string> GetBlockLinkAsync(string blockId) => Task.FromResult(string.Empty);
    }

    private static DatabaseField SelectField() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Tags",
        Type = DatabaseFieldType.Select,
        Config = new SelectFieldConfig
        {
            Options =
            [
                new SelectFieldOption("a", "Alpha", "gray"),
                new SelectFieldOption("b", "Beta", "red"),
                new SelectFieldOption("g", "Gamma", "blue"),
            ]
        }
    };

    private IRenderedComponent<TmNotionDbFieldEditor> RenderFieldEditor(
        DatabaseField? field = null, bool readOnly = false, TmLayoutMode mode = TmLayoutMode.Desktop, Action? onClose = null)
    {
        field ??= new DatabaseField { Id = Guid.NewGuid(), Name = "Title", Type = DatabaseFieldType.Text, IsPrimary = true };
        if (mode == TmLayoutMode.Desktop)
            return Render<TmNotionDbFieldEditor>(p => p
                .Add(x => x.Field, field)
                .Add(x => x.Fields, new[] { field })
                .Add(x => x.ReadOnly, readOnly)
                .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke())));
        return Render<CascadingValue<TmLayoutContext>>(p => p
            .Add(p => p.Value, new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile))
            .Add(p => p.Name, TmLayoutScopes.Viewport)
            .AddChildContent<CascadingValue<TmLayoutContext>>(viewport => viewport
                .Add(x => x.Value, new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile))
                .AddChildContent<TmNotionDbFieldEditor>(child => child
                    .Add(x => x.Field, field)
                    .Add(x => x.Fields, new[] { field })
                    .Add(x => x.ReadOnly, readOnly)
                    .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke())))))
            .FindComponent<TmNotionDbFieldEditor>();
    }

    private IRenderedComponent<TmNotionDbImportExport> RenderImportExport(
        TmLayoutMode mode = TmLayoutMode.Desktop, Action? onClose = null)
    {
        if (mode == TmLayoutMode.Desktop)
            return Render<TmNotionDbImportExport>(p => p
                .Add(x => x.DatabaseId, Guid.NewGuid())
                .Add(x => x.Fields, Array.Empty<IDatabaseField>())
                .Add(x => x.Views, Array.Empty<IDatabaseView>())
                .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke())));
        return Render<CascadingValue<TmLayoutContext>>(p => p
            .Add(p => p.Value, new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile))
            .Add(p => p.Name, TmLayoutScopes.Viewport)
            .AddChildContent<CascadingValue<TmLayoutContext>>(viewport => viewport
                .Add(x => x.Value, new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile))
                .AddChildContent<TmNotionDbImportExport>(child => child
                    .Add(x => x.DatabaseId, Guid.NewGuid())
                    .Add(x => x.Fields, Array.Empty<IDatabaseField>())
                    .Add(x => x.Views, Array.Empty<IDatabaseView>())
                    .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke())))))
            .FindComponent<TmNotionDbImportExport>();
    }

    private IRenderedComponent<TmNotionDbRecordDetail> RenderRecordDetail(
        INotionDatabaseProvider? databaseProvider = null,
        bool withSelectField = false,
        TmLayoutMode mode = TmLayoutMode.Desktop,
        Action? onClose = null,
        Action<IDatabaseRecord>? onRecordUpdated = null)
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Name", Type = DatabaseFieldType.Text, IsPrimary = true };
        var fields = new List<IDatabaseField> { field };
        if (withSelectField)
            fields.Add(new DatabaseField
            {
                Id = Guid.NewGuid(), Name = "Status", Type = DatabaseFieldType.Select, IsPrimary = false,
                Config = new SelectFieldConfig { Options = [new SelectFieldOption("a", "Active", "green")] }
            });
        var record = new DatabaseRecord
        {
            Id = Guid.NewGuid(),
            DatabaseId = Guid.NewGuid(),
            Fields = new Dictionary<string, object?> { [field.Id.ToString()] = "Record A" }
        };
        var context = new NotionEditorContext
        {
            DataProvider = Substitute.For<INotionDataProvider>(),
            BlockService = new EmptyBlockService(),
            DatabaseProvider = databaseProvider
        };
        if (mode == TmLayoutMode.Desktop)
            return Render<TmNotionDbRecordDetail>(p => p
                .Add(x => x.Record, record)
                .Add(x => x.Fields, fields)
                .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke()))
                .Add(x => x.OnRecordUpdated, EventCallback.Factory.Create(this, (IDatabaseRecord r) => onRecordUpdated?.Invoke(r)))
                .AddCascadingValue(context));
        return Render<CascadingValue<TmLayoutContext>>(p => p
            .Add(p => p.Value, new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile))
            .Add(p => p.Name, TmLayoutScopes.Viewport)
            .AddChildContent<CascadingValue<TmLayoutContext>>(viewport => viewport
                .Add(x => x.Value, new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile))
                .AddChildContent<TmNotionDbRecordDetail>(child => child
                    .Add(x => x.Record, record)
                    .Add(x => x.Fields, fields)
                    .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke()))
                    .AddCascadingValue(context))))
            .FindComponent<TmNotionDbRecordDetail>();
    }

    private IRenderedComponent<TmNotionDbTemplateEditor> RenderTemplateEditor(
        INotionDatabaseProvider? databaseProvider = null,
        Action<IDatabaseRecordTemplate>? onTemplateSaved = null,
        Action? onClose = null)
    {
        var field = new DatabaseField { Id = Guid.NewGuid(), Name = "Name", Type = DatabaseFieldType.Text, IsPrimary = true };
        var template = Substitute.For<IDatabaseRecordTemplate>();
        template.Id.Returns(Guid.NewGuid());
        template.Name.Returns("Weekly");
        template.DatabaseId.Returns(Guid.NewGuid());
        template.DefaultFields.Returns(new Dictionary<string, object?>());
        template.TemplateBlocks.Returns([]);
        return Render<TmNotionDbTemplateEditor>(p => p
            .Add(x => x.Template, template)
            .Add(x => x.Fields, new IDatabaseField[] { field })
            .Add(x => x.DatabaseName, "Hiring DB")
            .Add(x => x.OnClose, EventCallback.Factory.Create(this, () => onClose?.Invoke()))
            .Add(x => x.OnTemplateSaved, EventCallback.Factory.Create(this, (IDatabaseRecordTemplate t) => onTemplateSaved?.Invoke(t)))
            .AddCascadingValue(new NotionEditorContext
            {
                DataProvider = Substitute.For<INotionDataProvider>(),
                BlockService = new EmptyBlockService(),
                DatabaseProvider = databaseProvider
            }));
    }

    private static string CssPath(string file)
    {
        var root = FindRepositoryRoot();
        return Path.Combine(root, "src", "Tempo.Blazor.NotionEditor", "Components", "NotionEditor", "Blocks", "Database", file);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find TempoBlazor.slnx.");
    }
}
