using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign plan 146c7e1b, phase 6 (DE-2.2 .. DE-2.5, decision DE2): on a desktop layout a selected image or table, and an open find,
/// replace the inspector's tab bar with a contextual header (title, back arrow, X) - no new <see cref="DocumentSidePanelTab"/>; tablet and
/// phone keep the tabs. The version preview is a strip of the doc region.
/// </summary>
public sealed class TmDocumentEditorInspectorContextTests : LocalizationTestBase
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// The editor's debounced toolbar/format sync (200 ms) reads the engine state back; under bUnit's loose JS
    /// interop that readback reports "no image selected" and would forget a dismissal / the selected image. A fake
    /// clock keeps that background sync from ever firing on wall-clock time, so every step of a test is decided
    /// by the test alone, not by how long the machine took between two statements.
    /// </summary>
    private readonly FakeTimeProvider _clock = new();

    public TmDocumentEditorInspectorContextTests() => Services.AddSingleton<TimeProvider>(_clock);

    private IRenderedComponent<TmDocumentEditor> RenderEditor(TmLayoutMode mode, Action<DocumentEditorDocument>? seed = null)
    {
        var provider = new InMemoryDocumentEditorProvider();
        var document = provider.SeedContractDocument("doc-1");
        if (seed is not null)
        {
            seed(document);
            provider.SaveAsync(new DocumentEditorSaveRequest
            {
                DocumentId = document.DocumentId,
                Document = document,
                ConcurrencyMode = DocumentEditorConcurrencyMode.Force
            }).GetAwaiter().GetResult();
        }

        var cut = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "doc-1")
            .Add(p => p.Provider, provider)
            .Add(p => p.LayoutMode, mode));
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        var host = cut.FindComponent<TmDocumentCanvasEngineHost>();
        cut.InvokeAsync(() => host.Instance.OnCanvasEngineReady("{}")).GetAwaiter().GetResult();
        return cut;
    }

    private static async Task SelectImageAsync(IRenderedComponent<TmDocumentEditor> cut, string objectId = "drawing-1")
    {
        var sync = typeof(TmDocumentEditor).GetMethod("SyncCanvasActiveImage", Instance)!;
        var image = new TmDocumentCanvasEngineHost.CanvasEngineImageState
        {
            ObjectId = objectId,
            BlockId = "block-1",
            RunId = "run-1",
            Url = "https://example.test/picture.png",
            AltText = "A picture",
            Width = 144,
            Height = 96,
            WrapMode = "square"
        };
        await cut.InvokeAsync(() => sync.Invoke(cut.Instance, [image]));
        cut.Render();
    }

    private static async Task DeselectImageAsync(IRenderedComponent<TmDocumentEditor> cut)
    {
        var sync = typeof(TmDocumentEditor).GetMethod("SyncCanvasActiveImage", Instance)!;
        await cut.InvokeAsync(() => sync.Invoke(cut.Instance, [null]));
        cut.Render();
    }

    private static DocumentBlock TableBlock() => new()
    {
        Id = "tbl-1",
        Type = DocumentBlockType.Table,
        Content = new TableBlockContent
        {
            Rows =
            [
                new TableRowContent
                {
                    Cells =
                    [
                        new TableCellContent
                        {
                            Id = "cell-1",
                            Blocks = [new DocumentBlock { Id = "cell-p", Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = "x" }] } }]
                        }
                    ]
                }
            ]
        }
    };

    private static async Task SelectTableCellAsync(IRenderedComponent<TmDocumentEditor> cut)
    {
        var field = typeof(TmDocumentEditor).GetField("_selection", Instance)!;
        await cut.InvokeAsync(() => field.SetValue(cut.Instance, new DocumentEditorSelectionState { ActiveTableId = "tbl-1", ActiveTableCellId = "cell-1" }));
        cut.Render();
    }

    // ── image ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Desktop_ImageSelection_HidesTheTabBar_AndShowsTheContextHeader()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.FindAll("[data-testid='document-side-panel'] [role='tablist']").Should().HaveCount(1);

        await SelectImageAsync(cut);

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.GetAttribute("data-context").Should().Be("image");
        panel.QuerySelectorAll("[role='tablist']").Should().BeEmpty("the context replaces the tab bar");
        var header = panel.QuerySelector("[data-testid='document-side-panel-context-header']")!;
        header.QuerySelector("h2")!.TextContent.Should().Be("Image tools");
        header.QuerySelector("[data-testid='document-side-panel-context-back']").Should().NotBeNull();
        header.QuerySelector("[data-testid='document-side-panel-context-close']").Should().NotBeNull();
        panel.QuerySelector("[data-testid='document-image-inspector']").Should().NotBeNull();
        panel.QuerySelectorAll(".tm-document-inspector-header").Should().ContainSingle("the inspector adds no header of its own inside the context");
        cut.Find("[data-testid='document-image-properties-panel']").GetAttribute("data-active-object-id").Should().Be("drawing-1");
    }

    [Fact]
    public async Task Desktop_Back_ReturnsTheTabs_WhileTheImageStaysSelected()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);

        cut.Find("[data-testid='document-side-panel-context-back']").Click();

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.HasAttribute("data-context").Should().BeFalse();
        panel.QuerySelectorAll("[role='tablist']").Should().HaveCount(1);
        panel.QuerySelector("[data-testid='document-image-inspector']").Should().NotBeNull("a dismissed context must not leave the selected image without an inspector: the Properties tab carries it");
        panel.QuerySelector("[data-testid='document-side-panel-context-header']").Should().BeNull();
    }

    private static Task ReportCanvasSelectionAsync(IRenderedComponent<TmDocumentEditor> cut, string? reason, string? source = null)
    {
        var report = typeof(TmDocumentEditor).GetMethod("HandleCanvasMiniToolbarChangedAsync", Instance)!;
        return cut.InvokeAsync(() => (Task)report.Invoke(cut.Instance, [new WysiwygMiniToolbarRequest { Reason = reason, ObjectSelectionSource = source }])!);
    }

    private static Task OpenTabAsync(IRenderedComponent<TmDocumentEditor> cut, DocumentSidePanelTab tab)
    {
        var open = typeof(TmDocumentEditor).GetMethod("OpenSidePanel", Instance)!;
        return cut.InvokeAsync(() => open.Invoke(cut.Instance, [tab, true, true]));
    }

    [Fact]
    public async Task Desktop_ClickingTheSameImageAgain_ReopensTheDismissedContext()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();

        // A caret move or an empty report (a blur) is not a decision to look at the image again ...
        await ReportCanvasSelectionAsync(cut, "canvas-selection-collapsed");
        await ReportCanvasSelectionAsync(cut, null);
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();

        // ... a deliberate click on the image (the engine reports an object selection) is.
        await ReportCanvasSelectionAsync(cut, "canvas-object-selection", "pointer");
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
    }

    [Fact]
    public async Task Desktop_ADeliberateObjectSelectionScrolledOutOfTheFrame_StillOpensTheInspector()
    {
        // Phase 13: the engine hides the object toolbar of an object that sits outside the bounded frame's scroller (reason
        // 'canvas-selection-scrolled-out'); the reader still CHOSE this object, so the inspector opens all the same.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();

        var report = typeof(TmDocumentEditor).GetMethod("HandleCanvasMiniToolbarChangedAsync", Instance)!;
        var request = new WysiwygMiniToolbarRequest
        {
            IsVisible = false,
            Reason = "canvas-selection-scrolled-out",
            ObjectSelectionSource = "pointer",
            Selection = new WysiwygSelectionSnapshot { ObjectSelection = new WysiwygObjectSelectionSnapshot { ObjectId = "drawing-1" } },
        };
        await cut.InvokeAsync(() => (Task)report.Invoke(cut.Instance, [request])!);
        cut.Render();

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");

        // A scroll re-report ('layout') of the same hidden object never reopens a panel the reader closed again.
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        await cut.InvokeAsync(() => (Task)report.Invoke(cut.Instance, [new WysiwygMiniToolbarRequest { Reason = "canvas-selection-scrolled-out", ObjectSelectionSource = "layout", Selection = request.Selection }])!);
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();
    }

    [Fact]
    public async Task Desktop_ALayoutSourceScrolledOutReport_NeverOpensTheInspector()
    {
        // The scroll re-report of a hidden object carries the 'layout' source: it is no decision to look at the image again.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();

        var report = typeof(TmDocumentEditor).GetMethod("HandleCanvasMiniToolbarChangedAsync", Instance)!;
        await cut.InvokeAsync(() => (Task)report.Invoke(cut.Instance, [new WysiwygMiniToolbarRequest
        {
            Reason = "canvas-selection-scrolled-out",
            ObjectSelectionSource = "layout",
            Selection = new WysiwygSelectionSnapshot { ObjectSelection = new WysiwygObjectSelectionSnapshot { ObjectId = "drawing-1" } },
        }])!);
        cut.Render();

        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();
    }

    [Fact]
    public void AHiddenScrolledOutReport_IsNeverSwallowedByTheOpenColorPicker()
    {
        // The colour picker of the mini toolbar keeps the toolbar alive across a hidden report; a selection that scrolled out of the frame is
        // the one hidden report that must still hide it (the picker would float over the ribbon).
        var cut = RenderEditor(TmLayoutMode.Desktop);
        typeof(TmDocumentEditor).GetField("_miniToolbarColorPickerOpen", Instance)!.SetValue(cut.Instance, true);
        var preserve = typeof(TmDocumentEditor).GetMethod("ShouldPreserveMiniToolbarDuringFloatingInteraction", Instance)!;
        var selection = new WysiwygSelectionSnapshot { IsCollapsed = false };
        typeof(TmDocumentEditor).GetField("_lastMiniToolbarRequest", Instance)!.SetValue(cut.Instance, new WysiwygMiniToolbarRequest { IsVisible = true, Selection = selection });

        ((bool)preserve.Invoke(cut.Instance, [new WysiwygMiniToolbarRequest { IsVisible = false, Reason = "canvas-selection-collapsed", Selection = selection }])!)
            .Should().BeTrue("control: an ordinary hidden report is preserved while the picker is open");
        ((bool)preserve.Invoke(cut.Instance, [new WysiwygMiniToolbarRequest { IsVisible = false, Reason = "canvas-selection-scrolled-out", Selection = selection }])!)
            .Should().BeFalse();
    }

    [Fact]
    public async Task Desktop_Close_ClosesTheContext_AndTheSameImageDoesNotReopenIt()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);

        cut.Find("[data-testid='document-side-panel-context-close']").Click();

        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();
        cut.FindAll("[data-testid='document-side-panel'] [role='tablist']").Should().HaveCount(1);

        // The same object, selected again by a later state sync, stays dismissed ...
        await SelectImageAsync(cut, "drawing-1");
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();

        // ... a different object opens it afresh ...
        await SelectImageAsync(cut, "drawing-2");
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
    }

    [Fact]
    public async Task Desktop_ClosingThePanel_WhileTheImageStaysSelected_IsNotUndoneByALaterStateSync()
    {
        // The engine state read-back (the debounced toolbar sync) re-reports the SAME selected image after every edit or caret move. That
        // report is not a new selection: a reader who closed the inspector (the context X, then the panel X) must not see it pop up again
        // 200 ms later - on a loaded machine that late reopen raced the E2E "panel hidden" check (Phase10_PropertiesCommands).
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        await SelectImageAsync(cut, "drawing-1");

        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("the same image re-reported is not a new selection");
    }

    [Fact]
    public async Task Desktop_AnotherClickOnTheSameImage_ReopensAPanelTheReaderClosed()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        // A deliberate click on the image (the engine reports an object selection) is a decision to look at it again.
        await ReportCanvasSelectionAsync(cut, "canvas-object-selection", "pointer");
        cut.Render();

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
    }

    [Fact]
    public async Task Desktop_ALayoutReportOfTheSelectedImage_NeverReopensAPanelTheReaderClosed()
    {
        // The engine re-reports the selected object whenever it re-lays out or re-renders its overlay (a drag preview, an edit elsewhere,
        // the host's state read-back): that is not the reader looking at the object again.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        await ReportCanvasSelectionAsync(cut, "canvas-object-selection", "layout");
        await ReportCanvasSelectionAsync(cut, "canvas-object-selection", null);
        cut.Render();

        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();
    }

    [Fact]
    public async Task Desktop_AKeyboardSelectionOfTheImage_ReopensAClosedPanel()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        await ReportCanvasSelectionAsync(cut, "canvas-object-selection", "keyboard");
        cut.Render();

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
    }

    [Fact]
    public async Task Tablet_ADeliberateClickOnTheImage_SwitchesAnOpenPanelToProperties_ALayoutReportDoesNot()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);
        await SelectImageAsync(cut);
        await OpenTabAsync(cut, DocumentSidePanelTab.Comments);
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab").Should().Be("comments");

        await ReportCanvasSelectionAsync(cut, "canvas-object-selection", "layout");
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab").Should().Be("comments", "a re-report keeps the tab the reader stands on");

        await ReportCanvasSelectionAsync(cut, "canvas-object-selection", "pointer");
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-active-tab").Should().Be("properties");
    }

    [Fact]
    public async Task Desktop_TheDebouncedStateReadBack_OfTheSameImage_DoesNotReopenAClosedPanel()
    {
        // The real debounce: a caret report schedules the 200 ms toolbar sync on the editor's clock; its read-back reports the SAME selected
        // image again and must leave the closed panel closed (the pre-fix code called OpenSidePanel on every read-back).
        SetDocumentCanvasFormattingStateJson("""{"image":{"objectId":"drawing-1","blockId":"block-1","runId":"run-1","url":"https://example.test/picture.png","altText":"A picture","width":144,"height":96,"wrapMode":"square"}}""");
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        var module = SetupDocumentCanvasModule();
        var readBacksBefore = module.Invocations["getFormattingStateJson"].Count;
        await ReportCanvasSelectionAsync(cut, "canvas-selection-collapsed");
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        // The continuation of the fake-timer delay runs on the thread pool: wait for the read-back itself, not for wall-clock time.
        cut.WaitForAssertion(
            () => module.Invocations["getFormattingStateJson"].Count.Should().BeGreaterThan(readBacksBefore, "the debounced sync must really have run"),
            TimeSpan.FromSeconds(5));
        cut.WaitForState(() => true);
        cut.Render();

        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("the read-back re-reported the same image, which is not a new selection");
    }

    [Fact]
    public async Task Desktop_LoadingADocument_ForgetsTheSelectedImage_SoTheSameObjectIdOpensTheInspectorAgain()
    {
        // The object id of the previous document's image can equal one of the next document ("drawing-1" in every contract demo): the
        // selection memory belongs to the document that is gone.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        var reset = typeof(TmDocumentEditor).GetMethod("ResetInspectorSelectionMemory", Instance)!;
        await cut.InvokeAsync(() => reset.Invoke(cut.Instance, null));
        await SelectImageAsync(cut, "drawing-1");

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
    }

    [Fact]
    public async Task Desktop_ADifferentImage_OpensAClosedPanelAfresh()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        await SelectImageAsync(cut, "drawing-2");

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
    }

    [Fact]
    public async Task Desktop_DeselectingTheImage_EndsTheContext_AndForgetsTheDismissal()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-back']").Click();

        await DeselectImageAsync(cut);
        await SelectImageAsync(cut, "drawing-1");

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image", "a fresh selection starts afresh");
    }

    [Fact]
    public async Task Tablet_ImageSelection_InTheOpenSheet_IsTheContext_LikeTheDesktopDock()
    {
        // Phase 8: a tablet's side sheet is the same surface as the desktop dock, only presented over the document - an image replaces its
        // tab bar with the contextual header.
        var cut = RenderEditor(TmLayoutMode.Tablet);
        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();

        await SelectImageAsync(cut);

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.GetAttribute("data-context").Should().Be("image");
        panel.QuerySelector("[data-testid='document-image-inspector']").Should().NotBeNull();
    }

    [Fact]
    public async Task Mobile_ImageSelection_KeepsTheTabsInTheInspectorSheet_AndTheInspectorInTheTab()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);
        await SelectImageAsync(cut);
        cut.FindAll("[data-testid='document-context-sheet']").Should().BeEmpty("a finger on an object never pops a modal sheet by itself");
        cut.Find("[data-testid='document-mobile-comments']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.HasAttribute("data-context").Should().BeFalse("DE2: the context mode is a desktop / tablet inspector");
        panel.QuerySelectorAll("[role='tablist']").Should().HaveCount(1);
        cut.Find("[data-testid='document-side-panel-tab-properties']").Click();
        cut.WaitForAssertion(() =>
        {
            panel = cut.Find("[data-testid='document-side-panel']");
            panel.QuerySelector("[data-testid='document-image-inspector']").Should().NotBeNull();
            panel.QuerySelector("[data-testid='document-inspector-header']").Should().NotBeNull("outside the context the inspector carries its own header");
        });
    }

    [Fact]
    public async Task Desktop_ImageAnchor_ShowsTheParagraphText_NotTheBlockId()
    {
        var provider = new InMemoryDocumentEditorProvider();
        var document = provider.SeedContractDocument("doc-1");
        var paragraph = document.Blocks.First(block => block.Content is ParagraphBlockContent);
        var cut = RenderEditor(TmLayoutMode.Desktop);
        var sync = typeof(TmDocumentEditor).GetMethod("SyncCanvasActiveImage", Instance)!;
        await cut.InvokeAsync(() => sync.Invoke(cut.Instance, [new TmDocumentCanvasEngineHost.CanvasEngineImageState
        {
            ObjectId = "drawing-1", BlockId = paragraph.Id, RunId = "run-1", Url = "https://example.test/a.png", Width = 10, Height = 10
        }]));
        cut.Render();

        var anchor = cut.Find("[data-testid='document-image-inspector-anchor-paragraph']");
        anchor.TextContent.Should().NotContain(paragraph.Id);
        anchor.TextContent.Should().Contain("This agreement is made with", "the seeded paragraph starts like that");
    }

    // ── table ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Desktop_TableSelection_IsOneInspector_WithTwoFoldableSections()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, document => document.Blocks.Add(TableBlock()));

        await SelectTableCellAsync(cut);

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.GetAttribute("data-context").Should().Be("table");
        panel.QuerySelectorAll("[role='tablist']").Should().BeEmpty();
        panel.QuerySelector("[data-testid='document-side-panel-context-header'] h2")!.TextContent.Should().Be("Table");
        var inspector = panel.QuerySelector("[data-testid='document-table-selection-properties-panel']")!;
        inspector.QuerySelector("[data-testid='document-table-properties-panel']").Should().NotBeNull();
        inspector.QuerySelector("[data-testid='document-cell-properties-panel']").Should().NotBeNull();
        inspector.QuerySelectorAll(".tm-document-inspector-form__section-toggle").Should().HaveCount(2, "one foldable section per object");

        panel.QuerySelector("[data-testid='document-side-panel-context-back']")!.Click();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();
    }

    [Fact]
    public async Task Mobile_TableInspector_CarriesTheSharedHeaderWithATitleAndNoX()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile, document => document.Blocks.Add(TableBlock()));

        await SelectTableCellAsync(cut);
        cut.Find("[data-testid='document-mobile-comments']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-side-panel']").Should().NotBeNull());
        cut.Find("[data-testid='document-side-panel-tab-properties']").Click();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find("[data-testid='document-side-panel']");
            panel.HasAttribute("data-context").Should().BeFalse();
            var shared = panel.QuerySelector("[data-testid='document-inspector-header']")!;
            shared.QuerySelector("h3")!.TextContent.Should().Be("Table");
            shared.QuerySelector(".tm-document-inspector-header__close").Should().BeNull("the tab host owns the close button");
        });
    }

    [Fact]
    public async Task Tablet_TableSelection_InTheOpenSheet_IsTheContext_WithTheFullSet()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet, document => document.Blocks.Add(TableBlock()));
        cut.Find("[data-testid='document-side-panel-edge-toggle']").Click();

        await SelectTableCellAsync(cut);

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("table");
        cut.Find("[data-testid='document-table-properties-width']").Should().NotBeNull("a tablet keeps the whole table inspector");
        cut.Find("[data-testid='document-cell-properties-panel']").Should().NotBeNull("the cell properties are a tablet-and-up surface");
    }

    [Fact]
    public async Task Desktop_TableContext_SurvivesTheEmptySelectionTheCanvasReportsOnBlur_UntilSomethingElseIsSelected()
    {
        // Every panel edit hands the focus back to the canvas and clicking a field takes it away: the engine reports a NULL selection then.
        // The table inspector must not vanish after its first edit.
        var cut = RenderEditor(TmLayoutMode.Desktop, document => document.Blocks.Add(TableBlock()));
        var handle = typeof(TmDocumentEditor).GetMethod("HandleWysiwygSelectionChangedAsync", Instance)!;

        await cut.InvokeAsync(() => (Task)handle.Invoke(cut.Instance, [new WysiwygSelectionSnapshot
        {
            Region = "TableCell", AnchorBlockId = "cell-p", FocusBlockId = "cell-p", ActiveTableId = "tbl-1", ActiveTableCellId = "cell-1", IsCollapsed = true
        }])!);
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("table");

        await cut.InvokeAsync(() => (Task)handle.Invoke(cut.Instance, [null])!);
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("table", "a null selection is a blur, not a decision");

        var paragraph = cut.Instance.GetType().GetField("_document", Instance)!.GetValue(cut.Instance) as DocumentEditorDocument;
        var otherBlock = paragraph!.Blocks.First(block => block.Content is ParagraphBlockContent).Id;
        await cut.InvokeAsync(() => (Task)handle.Invoke(cut.Instance, [new WysiwygSelectionSnapshot
        {
            Region = "Body", AnchorBlockId = otherBlock, FocusBlockId = otherBlock, IsCollapsed = true
        }])!);
        await cut.InvokeAsync(() => (Task)handle.Invoke(cut.Instance, [null])!);
        cut.Render();

        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse("selecting text elsewhere ends the table context for good");
    }

    [Fact]
    public async Task Desktop_DismissedTableContext_KeepsTheTableInspectorInThePropertiesTab()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, document => document.Blocks.Add(TableBlock()));
        await SelectTableCellAsync(cut);

        cut.Find("[data-testid='document-side-panel-context-back']").Click();
        cut.Find("[data-testid='document-side-panel-tab-properties']").Click();

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.HasAttribute("data-context").Should().BeFalse();
        panel.QuerySelector("[data-testid='document-table-selection-properties-panel']").Should().NotBeNull();
    }

    [Fact]
    public async Task Desktop_ResolvingTheContext_NeverWritesDuringRender()
    {
        // A dismissal is forgotten where the SELECTION changes, never as a side effect of a render: re-rendering with no object must not
        // clear it (the old render-time write made the dismissal depend on how many renders happened in between).
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-close']").Click();
        var key = typeof(TmDocumentEditor).GetField("_inspectorContextDismissedKey", Instance)!;
        key.GetValue(cut.Instance).Should().Be("image:drawing-1");

        cut.Render();
        cut.Render();

        key.GetValue(cut.Instance).Should().Be("image:drawing-1");
    }

    [Fact]
    public async Task Desktop_PreviewingAVersion_ForgetsTheTableTheReaderStoodIn()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop, document => document.Blocks.Add(TableBlock()));
        var handle = typeof(TmDocumentEditor).GetMethod("HandleWysiwygSelectionChangedAsync", Instance)!;
        await cut.InvokeAsync(() => (Task)handle.Invoke(cut.Instance, [new WysiwygSelectionSnapshot
        {
            Region = "TableCell", AnchorBlockId = "cell-p", FocusBlockId = "cell-p", ActiveTableId = "tbl-1", ActiveTableCellId = "cell-1", IsCollapsed = true
        }])!);
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("table");

        var version = new DocumentVersion
        {
            Id = "v1",
            Label = "1.0",
            Kind = DocumentVersionKind.Minor,
            Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" },
            Snapshot = new DocumentVersionSnapshot { Json = DocumentEditorJson.Serialize(new DocumentEditorDocument { DocumentId = "doc-1" }) }
        };
        var preview = typeof(TmDocumentEditor).GetMethod("PreviewVersionAsync", Instance)!;
        await cut.InvokeAsync(() => (Task)preview.Invoke(cut.Instance, [version])!);
        await cut.InvokeAsync(() => (Task)handle.Invoke(cut.Instance, [null])!);
        cut.Render();

        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse("a table of the document that was swapped out is gone");
    }

    // ── find ───────────────────────────────────────────────────────────────

    private static Task OpenFindAsync(IRenderedComponent<TmDocumentEditor> cut, bool replace = false)
    {
        var open = typeof(TmDocumentEditor).GetMethod("OpenFindPanelAsync", Instance)!;
        return cut.InvokeAsync(() => (Task)open.Invoke(cut.Instance, [replace])!);
    }

    [Fact]
    public async Task Desktop_Find_IsTheInspectorContext_NotAFloatingPanel()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);

        await OpenFindAsync(cut);

        var inspector = cut.Find("[data-region='inspector']");
        var panel = inspector.QuerySelector("[data-testid='document-side-panel']")!;
        panel.GetAttribute("data-context").Should().Be("find");
        panel.QuerySelector("[data-testid='document-side-panel-context-header'] h2")!.TextContent.Should().Be("Find and replace");
        inspector.QuerySelector("[data-testid='document-find-panel']").Should().NotBeNull();
        cut.FindAll("[data-testid='document-find-panel']").Should().ContainSingle();
        cut.FindAll("[data-testid='document-floating-root'] [data-testid='document-find-panel']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-find-close']").Should().ContainSingle("the context header's X carries the find close test id");
        cut.Find("[data-testid='document-find-close']").ClassList.Should().Contain("tm-document-inspector-header__close");
    }

    [Fact]
    public async Task Desktop_Find_OpensAClosedInspector_AndClosesItAgain()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        await OpenFindAsync(cut);
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("find");

        cut.Find("[data-testid='document-find-close']").Click();

        cut.FindAll("[data-testid='document-find-panel']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty("the inspector the find opened closes with it");
    }

    [Fact]
    public async Task Desktop_Find_HasNoBackArrow_ItsXClosesIt_AndTheTabsReturn()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await OpenFindAsync(cut);

        cut.FindAll("[data-testid='document-side-panel-context-back']").Should().BeEmpty("find has no tabs to go back to");
        cut.Find("[data-testid='document-find-close']").Click();

        cut.FindAll("[data-testid='document-find-panel']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-side-panel'] [role='tablist']").Should().HaveCount(1);
    }

    [Fact]
    public async Task Desktop_Find_WinsOverASelectedImage_AndTheImageContextComesBackWithIt()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        await OpenFindAsync(cut);

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("find");

        cut.Find("[data-testid='document-find-close']").Click();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
    }

    [Fact]
    public async Task Tablet_Find_IsTheContextOfTheSideSheet_NotASecondSurface()
    {
        var cut = RenderEditor(TmLayoutMode.Tablet);

        await OpenFindAsync(cut);

        cut.FindAll("[data-testid='document-find-panel']").Should().ContainSingle();
        cut.Find("[data-region='inspector']").QuerySelector("[data-testid='document-find-panel']").Should().NotBeNull();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("find");
        cut.FindComponents<TmDocumentFloatingPanel>().Where(panel => panel.Instance.Kind == "find").Should().BeEmpty("one side sheet, no floating copy of find");
    }

    [Fact]
    public async Task Mobile_Find_IsAFullHeightSheetOfTheFloatingRoot()
    {
        var cut = RenderEditor(TmLayoutMode.Mobile);

        await OpenFindAsync(cut);

        cut.FindAll("[data-testid='document-find-panel']").Should().ContainSingle();
        cut.FindAll("[data-region='inspector']").Should().BeEmpty("a phone has no inspector region");
        var floating = cut.FindComponents<TmDocumentFloatingPanel>().Single(panel => panel.Instance.Kind == "find");
        floating.Instance.CloseOnOutsidePointerDown.Should().BeFalse("the reader clicks into the document while searching");
        floating.Instance.CloseTestId.Should().Be("document-find-close");
        floating.Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile, "a phone container is a sheet whatever the viewport is");
    }

    // ── version preview strip ──────────────────────────────────────────────

    [Fact]
    public async Task VersionPreviewStrip_LivesInTheDocRegion_AndShowsWithTheSidePanelClosed()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        cut.Find("[data-testid='document-side-panel-close']").Click();
        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();

        var field = typeof(TmDocumentEditor).GetField("_previewVersion", Instance)!;
        await cut.InvokeAsync(() => field.SetValue(cut.Instance, new DocumentVersion
        {
            Id = "v1",
            Label = "1.3",
            Kind = DocumentVersionKind.Minor,
            Author = new DocumentEditorAuthor { Id = "a", DisplayName = "Ada" }
        }));
        cut.Render();

        var strip = cut.Find("[data-region='doc'] [data-testid='document-doc-banners'] [data-testid='document-version-preview-state']");
        strip.TextContent.Should().Contain("You are viewing version 1.3");
        cut.FindAll("[data-testid='document-version-preview-state']").Should().ContainSingle();
        cut.Find("[data-testid='document-version-current']").Should().NotBeNull();
        cut.Find("[data-testid='document-version-restore']").Should().NotBeNull();
    }

    // ── revision preview (default resolver) ────────────────────────────────

    [Fact]
    public void RevisionPanel_DefaultResolver_ReadsTheRevisionTextFromTheDocumentByRange()
    {
        string paragraphId = string.Empty;
        string paragraphText = string.Empty;
        var cut = RenderEditor(TmLayoutMode.Desktop, document =>
        {
            var paragraph = document.Blocks.First(block => block.Content is ParagraphBlockContent);
            paragraphId = paragraph.Id;
            paragraphText = string.Concat(((ParagraphBlockContent)paragraph.Content).Inlines.OfType<TextRun>().Select(run => run.Text));
            document.Revisions.Add(new DocumentRevision
            {
                Id = "revision-range",
                Type = DocumentRevisionType.Insertion,
                Range = new DocumentRevisionRange { BlockId = paragraph.Id, StartOffset = 0, EndOffset = 5 },
                Author = new DocumentRevisionAuthor { Id = "a", DisplayName = "Ada" },
                PayloadJson = null
            });
        });

        cut.Find("[data-testid='document-side-panel-tab-revisions']").Click();

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find("[data-revision-id='revision-range']");
            paragraphText.Length.Should().BeGreaterThan(5);
            item.QuerySelector("ins")!.TextContent.Should().Be(paragraphText[..5].Trim());
        });
        paragraphId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Desktop_AnOpenCommandPalette_SurvivesTheDebouncedStateSync_WithItsQueryAndItems()
    {
        // The engine's selection report is followed by the debounced (200 ms) state read-back, which re-renders the editor chrome and refreshes
        // the command registry. A palette opened in between must come through it intact - the E2E "wait until the editor settled" helper
        // existed only because that was unproven.
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await ReportCanvasSelectionAsync(cut, "canvas-selection-collapsed");
        var open = typeof(TmDocumentEditor).GetMethod("OpenCommandPaletteAsync", Instance)!;
        await cut.InvokeAsync(() => (Task)open.Invoke(cut.Instance, null)!);
        cut.WaitForAssertion(() => cut.Find("[data-testid='document-command-palette']"));
        cut.Find("[data-testid='document-command-palette-search']").Input("table");
        var itemsBefore = cut.FindAll("[data-testid='document-command-palette-item']").Count;

        var module = SetupDocumentCanvasModule();
        var readBacksBefore = module.Invocations["getFormattingStateJson"].Count;
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        cut.WaitForAssertion(
            () => module.Invocations["getFormattingStateJson"].Count.Should().BeGreaterThan(readBacksBefore, "the debounced sync must really have run"),
            TimeSpan.FromSeconds(5));
        cut.Render();

        cut.Find("[data-testid='document-command-palette']").Should().NotBeNull();
        var search = cut.Find("[data-testid='document-command-palette-search']");
        search.GetAttribute("value").Should().Be("table", "the re-render must not reset the typed query");
        cut.FindAll("[data-testid='document-command-palette-item']").Count.Should().Be(itemsBefore);
    }
}
