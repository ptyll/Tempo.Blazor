using System.Reflection;
using Bunit;
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

    private static Task ReportCanvasSelectionAsync(IRenderedComponent<TmDocumentEditor> cut, string? reason)
    {
        var report = typeof(TmDocumentEditor).GetMethod("HandleCanvasMiniToolbarChangedAsync", Instance)!;
        return cut.InvokeAsync(() => (Task)report.Invoke(cut.Instance, [new WysiwygMiniToolbarRequest { Reason = reason }])!);
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
        await ReportCanvasSelectionAsync(cut, "canvas-object-selection");
        cut.Render();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");
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
    public async Task Desktop_DeselectingTheImage_EndsTheContext_AndForgetsTheDismissal()
    {
        var cut = RenderEditor(TmLayoutMode.Desktop);
        await SelectImageAsync(cut);
        cut.Find("[data-testid='document-side-panel-context-back']").Click();

        await DeselectImageAsync(cut);
        await SelectImageAsync(cut, "drawing-1");

        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image", "a fresh selection starts afresh");
    }

    [Theory]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public async Task TabletAndMobile_ImageSelection_KeepsTheTabs_AndTheInspectorInTheTab(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode);

        await SelectImageAsync(cut);

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.HasAttribute("data-context").Should().BeFalse("DE2: the context mode is a desktop inspector");
        panel.QuerySelectorAll("[role='tablist']").Should().HaveCount(1);
        panel.QuerySelector("[data-testid='document-image-inspector']").Should().NotBeNull();
        panel.QuerySelector("[data-testid='document-inspector-header']").Should().NotBeNull("outside the context the inspector carries its own header");
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

    [Theory]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public async Task TabletAndMobile_TableInspector_CarriesTheSharedHeaderWithATitleAndNoX(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode, document => document.Blocks.Add(TableBlock()));

        await SelectTableCellAsync(cut);
        cut.Find("[data-testid='document-side-panel-tab-properties']").Click();

        var panel = cut.Find("[data-testid='document-side-panel']");
        panel.HasAttribute("data-context").Should().BeFalse();
        var shared = panel.QuerySelector("[data-testid='document-inspector-header']")!;
        shared.QuerySelector("h3")!.TextContent.Should().Be("Table");
        shared.QuerySelector(".tm-document-inspector-header__close").Should().BeNull("the tab host owns the close button");
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

    [Theory]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public async Task TabletAndMobile_Find_IsAFloatingPanelOfTheFloatingRoot(TmLayoutMode mode)
    {
        var cut = RenderEditor(mode);

        await OpenFindAsync(cut);

        cut.FindAll("[data-testid='document-find-panel']").Should().ContainSingle();
        cut.Find("[data-region='inspector']").QuerySelector("[data-testid='document-find-panel']").Should().BeNull();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();
        var floating = cut.FindComponents<TmDocumentFloatingPanel>().Single(panel => panel.Instance.Kind == "find");
        floating.Instance.CloseOnOutsidePointerDown.Should().BeFalse("the reader clicks into the document while searching");
        floating.Instance.CloseTestId.Should().Be("document-find-close");
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
}
