using System.Text.Json;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockNotionBlockStore
{
    private readonly Dictionary<Guid, PageBlock> _blocks = new();

    // ── Fixed IDs for blocks that need stable cross-references ────────────────

    private static readonly Guid _pageId        = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _columnListId  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid _col1Id        = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid _col2Id        = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public MockNotionBlockStore()
    {
        InitializeMockBlocks();
    }

    private void InitializeMockBlocks()
    {
        var now = DateTime.UtcNow;

        // ── Phase 1 blocks ────────────────────────────────────────────────────

        Add(BlockType.Heading1,   0, new HeadingBlockContent { Level = 1, Html = "Welcome to Notion Editor" });
        Add(BlockType.Paragraph,  1, new TextBlockContent
        {
            Html = "This is a demo paragraph. You can edit this text, use slash commands to add new blocks, and test various features."
        });
        Add(BlockType.Heading2,   2, new HeadingBlockContent { Level = 2, Html = "Features to Test" });
        Add(BlockType.BulletList, 3, new ListBlockContent { Html = "Press / to open slash commands" });
        Add(BlockType.BulletList, 4, new ListBlockContent { Html = "Press Enter to create a new block" });
        Add(BlockType.BulletList, 5, new ListBlockContent { Html = "Drag blocks to reorder them" });
        Add(BlockType.Heading2,   6, new HeadingBlockContent { Level = 2, Html = "Text Formatting" });
        Add(BlockType.Paragraph,  7, new TextBlockContent
        {
            Html = "Select text to see the inline toolbar. Try making text <strong>bold</strong> or <em>italic</em>."
        });
        Add(BlockType.Divider,    8, new DividerBlockContent());
        Add(BlockType.Paragraph,  9, new TextBlockContent
        {
            Html = "Try pressing <kbd>Ctrl+Z</kbd> to undo or <kbd>Ctrl+Y</kbd> to redo."
        });
        Add(BlockType.Heading2,  10, new HeadingBlockContent { Level = 2, Html = "PDF Viewer" });
        Add(BlockType.Pdf,       11, new PdfBlockContent
        {
            Url     = "https://mozilla.github.io/pdf.js/web/compressed.tracemonkey-pldi-09.pdf",
            Caption = "TraceMonkey — demo PDF (Mozilla)"
        });

        // ── Phase 2: Media Blocks ─────────────────────────────────────────────

        Add(BlockType.Heading2, 12, new HeadingBlockContent { Level = 2, Html = "Phase 2: Media Blocks" });

        Add(BlockType.Image, 13, new ImageBlockContent
        {
            Url     = "https://images.unsplash.com/photo-1555949963-ff9fe0c870eb?w=1200&q=80",
            AltText = "Software developer at laptop",
            Caption = "Click the image to resize or change alignment",
            Width   = 700
        });

        Add(BlockType.Video, 14, new VideoBlockContent
        {
            Provider = VideoProvider.YouTube,
            Url      = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            Caption  = "YouTube embed demo — click to play",
            Width    = 700
        });

        Add(BlockType.File, 15, new FileBlockContent
        {
            FileName      = "sample-report.pdf",
            FileSizeBytes = 2_457_600,
            ContentType   = "application/pdf",
            Url           = "https://mozilla.github.io/pdf.js/web/compressed.tracemonkey-pldi-09.pdf",
            Caption       = "Sample PDF report (2.4 MB)"
        });

        // ── Phase 2: Embeds ───────────────────────────────────────────────────

        Add(BlockType.Heading2, 16, new HeadingBlockContent { Level = 2, Html = "Phase 2: Embeds" });

        Add(BlockType.Bookmark, 17, new BookmarkBlockContent
        {
            Url          = "https://github.com/anthropics/anthropic-sdk-python",
            Title        = "anthropics/anthropic-sdk-python",
            Description  = "The official Python library for the Anthropic API. Access Claude and other Anthropic models via a typed Python client.",
            Domain       = "github.com",
            FaviconUrl   = "https://github.com/favicon.ico",
            CoverImageUrl = "https://opengraph.githubassets.com/1/anthropics/anthropic-sdk-python",
            Caption      = "Anthropic Python SDK on GitHub"
        });

        Add(BlockType.Embed, 18, new EmbedBlockContent
        {
            Provider = EmbedProvider.CodePen,
            Url      = "https://codepen.io/alvaromontoro/embed/vYexLGV",
            Width    = 700,
            Height   = 400,
            Caption  = "CodePen embed — interactive CSS demo"
        });

        // ── Phase 2: Layout — 2 Columns ───────────────────────────────────────

        Add(BlockType.Heading2, 19, new HeadingBlockContent { Level = 2, Html = "Phase 2: Layout — 2 Columns" });

        // ColumnList root block (fixed ID so Column children can reference it)
        _blocks[_columnListId] = MakeBlock(
            id:           _columnListId,
            pageId:       _pageId,
            parentBlockId: null,
            type:         BlockType.ColumnList,
            order:        20,
            content:      new ColumnListBlockContent { ColumnCount = 2 }
        );

        // Column 1 — child of ColumnList
        _blocks[_col1Id] = MakeBlock(
            id:           _col1Id,
            pageId:       _pageId,
            parentBlockId: _columnListId,
            type:         BlockType.Column,
            order:        0,
            content:      new ColumnBlockContent { ColumnIndex = 0, WidthPercent = 50 }
        );

        // Content in Column 1
        var col1Para1 = Guid.NewGuid();
        _blocks[col1Para1] = MakeBlock(
            id:           col1Para1,
            pageId:       _pageId,
            parentBlockId: _col1Id,
            type:         BlockType.Heading3,
            order:        0,
            content:      new HeadingBlockContent { Level = 3, Html = "Left Column" }
        );
        var col1Para2 = Guid.NewGuid();
        _blocks[col1Para2] = MakeBlock(
            id:           col1Para2,
            pageId:       _pageId,
            parentBlockId: _col1Id,
            type:         BlockType.Paragraph,
            order:        1,
            content:      new TextBlockContent
            {
                Html = "This content is in the <strong>left column</strong>. Drag the divider between columns to resize. You can add any block type inside a column."
            }
        );
        var col1Todo = Guid.NewGuid();
        _blocks[col1Todo] = MakeBlock(
            id:           col1Todo,
            pageId:       _pageId,
            parentBlockId: _col1Id,
            type:         BlockType.TodoItem,
            order:        2,
            content:      new TodoBlockContent { Html = "Task in left column", IsChecked = true }
        );

        // Column 2 — child of ColumnList
        _blocks[_col2Id] = MakeBlock(
            id:           _col2Id,
            pageId:       _pageId,
            parentBlockId: _columnListId,
            type:         BlockType.Column,
            order:        1,
            content:      new ColumnBlockContent { ColumnIndex = 1, WidthPercent = 50 }
        );

        // Content in Column 2
        var col2Para1 = Guid.NewGuid();
        _blocks[col2Para1] = MakeBlock(
            id:           col2Para1,
            pageId:       _pageId,
            parentBlockId: _col2Id,
            type:         BlockType.Heading3,
            order:        0,
            content:      new HeadingBlockContent { Level = 3, Html = "Right Column" }
        );
        var col2Para2 = Guid.NewGuid();
        _blocks[col2Para2] = MakeBlock(
            id:           col2Para2,
            pageId:       _pageId,
            parentBlockId: _col2Id,
            type:         BlockType.Paragraph,
            order:        1,
            content:      new TextBlockContent
            {
                Html = "This content is in the <strong>right column</strong>. Columns support the full set of block types including nested toggles and lists."
            }
        );
        var col2Bullet1 = Guid.NewGuid();
        _blocks[col2Bullet1] = MakeBlock(
            id:           col2Bullet1,
            pageId:       _pageId,
            parentBlockId: _col2Id,
            type:         BlockType.BulletList,
            order:        2,
            content:      new ListBlockContent { Html = "Feature A" }
        );
        var col2Bullet2 = Guid.NewGuid();
        _blocks[col2Bullet2] = MakeBlock(
            id:           col2Bullet2,
            pageId:       _pageId,
            parentBlockId: _col2Id,
            type:         BlockType.BulletList,
            order:        3,
            content:      new ListBlockContent { Html = "Feature B" }
        );

        // ── Phase 2: Equation (LaTeX) ─────────────────────────────────────────

        Add(BlockType.Heading2, 21, new HeadingBlockContent { Level = 2, Html = "Phase 2: Equation (LaTeX)" });

        Add(BlockType.Equation, 22, new EquationBlockContent
        {
            Expression = @"E = mc^2"
        });

        Add(BlockType.Equation, 23, new EquationBlockContent
        {
            Expression = @"\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}"
        });

        Add(BlockType.Equation, 24, new EquationBlockContent
        {
            Expression = @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}"
        });

        // ── Phase 2: Callout & Code ───────────────────────────────────────────

        Add(BlockType.Heading2, 25, new HeadingBlockContent { Level = 2, Html = "Phase 2: Code & Callout" });

        Add(BlockType.Callout, 26, new CalloutBlockContent
        {
            IconEmoji       = "💡",
            Html            = "This is a callout block. Use it to highlight important information.",
            BackgroundColor = "blue"
        });

        Add(BlockType.Code, 27, new CodeBlockContent
        {
            Language = "C#",
            Code     = "var equation = new EquationBlockContent\n{\n    Expression = @\"E = mc^2\"\n};\nconsole.WriteLine(equation.Expression);"
        });

        // ── Phase 2: Diagram & Wireframe ──────────────────────────────────────

        Add(BlockType.Heading2, 28, new HeadingBlockContent { Level = 2, Html = "Phase 2: Diagram &amp; Wireframe" });

        Add(BlockType.Paragraph, 29, new TextBlockContent
        {
            Html = "The blocks below show the Diagram and Wireframe editors. Click <strong>Create Diagram</strong> or <strong>Create Wireframe</strong> to open the full-screen editor. After saving, an SVG preview is shown inline."
        });

        Add(BlockType.Diagram, 30, new DiagramBlockContent());

        Add(BlockType.Wireframe, 31, new WireframeBlockContent());

        // ── Phase 2: Spreadsheet ──────────────────────────────────────────────

        Add(BlockType.Heading2, 31_3, new HeadingBlockContent { Level = 2, Html = "Phase 2: Spreadsheet Block" });

        Add(BlockType.Paragraph, 31_4, new TextBlockContent
        {
            Html = "The block below shows the Spreadsheet editor. Click <strong>Create Spreadsheet</strong> to open the full-screen editor. After saving, an embedded live spreadsheet is shown inline."
        });

        Add(BlockType.Spreadsheet, 31_5, new SpreadsheetBlockContent());

        // ── Phase 3: Table ────────────────────────────────────────────────────

        Add(BlockType.Heading2, 31_1, new HeadingBlockContent { Level = 2, Html = "Phase 3: Table" });

        var tableId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        _blocks[tableId] = MakeBlock(
            id:           tableId,
            pageId:       _pageId,
            parentBlockId: null,
            type:         BlockType.Table,
            order:        31_2,
            content:      new TableBlockContent { ColumnCount = 3, HasHeaderRow = true, HasHeaderColumn = false }
        );

        var tableRow1 = Guid.NewGuid();
        _blocks[tableRow1] = MakeBlock(
            id:           tableRow1,
            pageId:       _pageId,
            parentBlockId: tableId,
            type:         BlockType.TableRow,
            order:        0,
            content:      new TableRowBlockContent { RichCells = ToRichCells("Name", "Status", "Priority") }
        );

        var tableRow2 = Guid.NewGuid();
        _blocks[tableRow2] = MakeBlock(
            id:           tableRow2,
            pageId:       _pageId,
            parentBlockId: tableId,
            type:         BlockType.TableRow,
            order:        1,
            content:      new TableRowBlockContent { RichCells = ToRichCells("Auth refactor", "In Progress", "High") }
        );

        var tableRow3 = Guid.NewGuid();
        _blocks[tableRow3] = MakeBlock(
            id:           tableRow3,
            pageId:       _pageId,
            parentBlockId: tableId,
            type:         BlockType.TableRow,
            order:        2,
            content:      new TableRowBlockContent { RichCells = ToRichCells("Dark mode", "Done", "Low") }
        );

        // ── Phase 3: Inline Database ──────────────────────────────────────────

        Add(BlockType.Heading2, 32, new HeadingBlockContent { Level = 2, Html = "Phase 3: Inline Database" });

        Add(BlockType.Paragraph, 33, new TextBlockContent
        {
            Html = "The database below is fully functional. Switch views using the tabs, filter and sort records, edit fields, open record details, and test import/export."
        });

        Add(BlockType.InlineDatabase, 34, new InlineDatabaseBlockContent
        {
            DatabaseId   = MockNotionDatabaseStore.DbId,
            Title        = "Project Tasks",
            IconEmoji    = "📋",
            ActiveViewId = Guid.Parse("e0000000-0000-0000-0000-000000000001")
        });

        // ── Phase 4: Comments, Search, History hints ──────────────────────────

        Add(BlockType.Heading2, 35, new HeadingBlockContent { Level = 2, Html = "Phase 4: Comments &amp; History" });
        Add(BlockType.Callout, 36, new CalloutBlockContent
        {
            IconEmoji = "💬",
            Html = "Try the comment panel: hover a block and click the comment icon. Page history is available via the ⚙️ settings button (top-right). Use <kbd>Ctrl+P</kbd> to search pages.",
            BackgroundColor = "blue"
        });

        // ── Page 2 — Product Roadmap ──────────────────────────────────────────
        AddTo(MockNotionDataStore.Page2Id, BlockType.Heading1, 0,
            new HeadingBlockContent { Level = 1, Html = "Product Roadmap" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.Callout, 1,
            new CalloutBlockContent { IconEmoji = "📌", Html = "Living document — updated every sprint. Last reviewed by <strong>Alice Johnson</strong>.", BackgroundColor = "yellow" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.Heading2, 2,
            new HeadingBlockContent { Level = 2, Html = "Q1 2025 — In Progress" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.TodoItem, 3,
            new TodoBlockContent { Html = "Notion-style block editor — Phase 4 providers", IsChecked = false });
        AddTo(MockNotionDataStore.Page2Id, BlockType.TodoItem, 4,
            new TodoBlockContent { Html = "Collaborative cursors & real-time sync", IsChecked = false });
        AddTo(MockNotionDataStore.Page2Id, BlockType.TodoItem, 5,
            new TodoBlockContent { Html = "Export to PDF via QuestPDF", IsChecked = true });
        AddTo(MockNotionDataStore.Page2Id, BlockType.Heading2, 6,
            new HeadingBlockContent { Level = 2, Html = "Q2 2025 — Planned" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.BulletList, 7,
            new ListBlockContent { Html = "Mobile-responsive layouts" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.BulletList, 8,
            new ListBlockContent { Html = "Notion API integration bridge" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.BulletList, 9,
            new ListBlockContent { Html = "AI-powered block suggestions" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.Divider, 10,
            new DividerBlockContent());
        AddTo(MockNotionDataStore.Page2Id, BlockType.Paragraph, 11,
            new TextBlockContent { Html = "Questions? Ping <strong>Alice Johnson</strong> on Slack." });

        // ── Page 3 — Meeting Notes ────────────────────────────────────────────
        AddTo(MockNotionDataStore.Page3Id, BlockType.Heading1, 0,
            new HeadingBlockContent { Level = 1, Html = "Weekly Team Meeting" });
        AddTo(MockNotionDataStore.Page3Id, BlockType.Paragraph, 1,
            new TextBlockContent { Html = "<em>Date: May 5, 2025 · Attendees: Alice, Bob, Charlie, Diana</em>" });
        AddTo(MockNotionDataStore.Page3Id, BlockType.Heading2, 2,
            new HeadingBlockContent { Level = 2, Html = "Agenda" });
        AddTo(MockNotionDataStore.Page3Id, BlockType.NumberedList, 3,
            new ListBlockContent { Html = "Sprint retrospective" });
        AddTo(MockNotionDataStore.Page3Id, BlockType.NumberedList, 4,
            new ListBlockContent { Html = "Phase 4 demo walkthrough" });
        AddTo(MockNotionDataStore.Page3Id, BlockType.NumberedList, 5,
            new ListBlockContent { Html = "Q2 planning kick-off" });
        AddTo(MockNotionDataStore.Page3Id, BlockType.Heading2, 6,
            new HeadingBlockContent { Level = 2, Html = "Action Items" });
        AddTo(MockNotionDataStore.Page3Id, BlockType.TodoItem, 7,
            new TodoBlockContent { Html = "Bob: merge comment provider PR by EOD", IsChecked = false });
        AddTo(MockNotionDataStore.Page3Id, BlockType.TodoItem, 8,
            new TodoBlockContent { Html = "Alice: update roadmap with Q2 items", IsChecked = true });
        AddTo(MockNotionDataStore.Page3Id, BlockType.TodoItem, 9,
            new TodoBlockContent { Html = "Charlie: write Architecture Guide first draft", IsChecked = false });

        // ── Phase 12: Navigation blocks on Page 1 ────────────────────────────

        Add(BlockType.Heading2, 37, new HeadingBlockContent { Level = 2, Html = "Phase 12: Navigation Blocks" });

        Add(BlockType.ChildPage, 38, new ChildPageBlockContent
        {
            ChildPageId = MockNotionDataStore.Page2Id,
            Title       = "Product Roadmap",
            IconEmoji   = "📌"
        });

        Add(BlockType.LinkedPage, 39, new LinkedPageBlockContent
        {
            LinkedPageId = MockNotionDataStore.Page3Id,
            Title        = "Meeting Notes",
            IconEmoji    = "🗒️"
        });

        Add(BlockType.Breadcrumb, 40, new BreadcrumbBlockContent());

        // ── Phase 13: Special blocks on Page 1 ───────────────────────────────

        Add(BlockType.TableOfContents, 41, new TableOfContentsBlockContent { MaxLevel = 3 });

        Add(BlockType.TemplateButton, 42, new TemplateButtonBlockContent
        {
            Label = "Demo Template",
            TemplateBlocks = new List<PageBlock>
            {
                new PageBlock
                {
                    Id           = Guid.NewGuid(),
                    PageId       = _pageId,
                    Type         = BlockType.Heading2,
                    Order        = 0,
                    Content      = new HeadingBlockContent { Level = 2, Html = "Section Title" },
                    CreatedAt    = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                },
                new PageBlock
                {
                    Id           = Guid.NewGuid(),
                    PageId       = _pageId,
                    Type         = BlockType.Paragraph,
                    Order        = 1,
                    Content      = new TextBlockContent { Html = "Write your content here." },
                    CreatedAt    = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                }
            }
        });

        Add(BlockType.Image, 43, new ImageBlockContent
        {
            Url = string.Empty,
            Caption = "Upload target"
        });

        // ── Page 4 — Engineering Wiki ─────────────────────────────────────────
        AddTo(MockNotionDataStore.Page4Id, BlockType.Heading1, 0,
            new HeadingBlockContent { Level = 1, Html = "Engineering Wiki" });
        AddTo(MockNotionDataStore.Page4Id, BlockType.Paragraph, 1,
            new TextBlockContent { Html = "Central hub for technical documentation, architecture decisions, and developer guides." });
        AddTo(MockNotionDataStore.Page4Id, BlockType.Heading2, 2,
            new HeadingBlockContent { Level = 2, Html = "Sub-pages" });
        AddTo(MockNotionDataStore.Page4Id, BlockType.BulletList, 3,
            new ListBlockContent { Html = "🏗️ Architecture Guide — system design and patterns" });
        AddTo(MockNotionDataStore.Page4Id, BlockType.BulletList, 4,
            new ListBlockContent { Html = "🔧 Development Setup — local env and tooling" });
        AddTo(MockNotionDataStore.Page4Id, BlockType.Heading2, 5,
            new HeadingBlockContent { Level = 2, Html = "Tech Stack" });
        AddTo(MockNotionDataStore.Page4Id, BlockType.Code, 6,
            new CodeBlockContent { Language = "yaml", Code = "frontend:\n  framework: Blazor (.NET 9)\n  component-lib: Tempo.Blazor\nbackend:\n  api: ASP.NET Core Minimal API\n  db: SQLite (demo) / PostgreSQL (prod)" });

        // ── Page 5 — Architecture Guide ───────────────────────────────────────
        AddTo(MockNotionDataStore.Page5Id, BlockType.Heading1, 0,
            new HeadingBlockContent { Level = 1, Html = "Architecture Guide" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.Callout, 1,
            new CalloutBlockContent { IconEmoji = "🏗️", Html = "This document describes the high-level architecture of the Tempo.Blazor component library.", BackgroundColor = "gray" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.Heading2, 2,
            new HeadingBlockContent { Level = 2, Html = "Layer Overview" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.BulletList, 3,
            new ListBlockContent { Html = "<strong>Abstractions</strong> — interfaces, models, enums (no Blazor dependency)" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.BulletList, 4,
            new ListBlockContent { Html = "<strong>Tempo.Blazor</strong> — Razor components, scoped CSS, JS interop" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.BulletList, 5,
            new ListBlockContent { Html = "<strong>Demo.Api</strong> — minimal API with in-memory mock stores" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.BulletList, 6,
            new ListBlockContent { Html = "<strong>Demo.SharedUI</strong> — HTTP providers + demo pages" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.Heading2, 7,
            new HeadingBlockContent { Level = 2, Html = "Design Principles" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.NumberedList, 8,
            new ListBlockContent { Html = "Interface-first: all providers are defined as interfaces in Abstractions" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.NumberedList, 9,
            new ListBlockContent { Html = "Cascading context: <code>NotionEditorContext</code> propagates providers to all children" });
        AddTo(MockNotionDataStore.Page5Id, BlockType.NumberedList, 10,
            new ListBlockContent { Html = "Scoped CSS: every component has its own <code>.razor.css</code>" });

        AddTo(MockNotionDataStore.Page5Id, BlockType.Breadcrumb, 11, new BreadcrumbBlockContent());

        // ── Page 6 — Development Setup ────────────────────────────────────────
        AddTo(MockNotionDataStore.Page6Id, BlockType.Heading1, 0,
            new HeadingBlockContent { Level = 1, Html = "Development Setup" });
        AddTo(MockNotionDataStore.Page6Id, BlockType.Paragraph, 1,
            new TextBlockContent { Html = "Follow these steps to get the Tempo.Blazor demo running locally." });
        AddTo(MockNotionDataStore.Page6Id, BlockType.Heading2, 2,
            new HeadingBlockContent { Level = 2, Html = "Prerequisites" });
        AddTo(MockNotionDataStore.Page6Id, BlockType.BulletList, 3,
            new ListBlockContent { Html = ".NET 9 SDK or later" });
        AddTo(MockNotionDataStore.Page6Id, BlockType.BulletList, 4,
            new ListBlockContent { Html = "Node.js 20+ (for JS tooling)" });
        AddTo(MockNotionDataStore.Page6Id, BlockType.Heading2, 5,
            new HeadingBlockContent { Level = 2, Html = "Quick Start" });
        AddTo(MockNotionDataStore.Page6Id, BlockType.Code, 6,
            new CodeBlockContent { Language = "bash", Code = "git clone https://github.com/your-org/Tempo.Blazor2\ncd Tempo.Blazor2\n\n# Start API\ndotnet run --project src/Tempo.Blazor.Demo.Api\n\n# Start Server (separate terminal)\ndotnet run --project src/Tempo.Blazor.Demo.Server" });
        AddTo(MockNotionDataStore.Page6Id, BlockType.Callout, 7,
            new CalloutBlockContent { IconEmoji = "💡", Html = "The API runs on <code>https://localhost:5100</code> and the Server on <code>https://localhost:7106</code> by default.", BackgroundColor = "blue" });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Add(BlockType type, int order, IBlockContent content)
    {
        var id = Guid.NewGuid();
        _blocks[id] = MakeBlock(id, _pageId, null, type, order, content);
    }

    private void AddTo(Guid pageId, BlockType type, int order, IBlockContent content)
    {
        var id = Guid.NewGuid();
        _blocks[id] = MakeBlock(id, pageId, null, type, order, content);
    }

    private void AddTo(Guid id, Guid pageId, BlockType type, int order, IBlockContent content)
    {
        _blocks[id] = MakeBlock(id, pageId, null, type, order, content);
    }

    private void AddChildTo(Guid id, Guid pageId, Guid parentBlockId, BlockType type, int order, IBlockContent content)
    {
        _blocks[id] = MakeBlock(id, pageId, parentBlockId, type, order, content);
    }

    private static PageBlock MakeBlock(Guid id, Guid pageId, Guid? parentBlockId,
        BlockType type, int order, IBlockContent content) => new()
    {
        Id            = id,
        PageId        = pageId,
        ParentBlockId = parentBlockId,
        Type          = type,
        Order         = order,
        Content       = content,
        CreatedAt     = DateTime.UtcNow,
        LastEditedAt  = DateTime.UtcNow
    };

    // ── Public API ────────────────────────────────────────────────────────────

    public void SeedE2ESearchPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page3Id, MockNotionDataStore.Page4Id);

        AddTo(Guid.Parse("cf220000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "The beacon rollout verifies engineering search filters across author, label, date, content type, and space."
        });
        AddTo(Guid.Parse("cf220000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Heading2, 1, new HeadingBlockContent
        {
            Level = 2,
            Html = "Operational notes"
        });

        AddTo(Guid.Parse("cf220000-0000-0000-0000-000000000102"), MockNotionDataStore.Page2Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "žluťoučký zlutoucky produktový souhrn pokrývá lokalizované dotazy bez ztráty diakritiky."
        });

        AddTo(Guid.Parse("cf220000-0000-0000-0000-000000000202"), MockNotionDataStore.Page3Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Support space overview for triage knowledge."
        });
        AddTo(Guid.Parse("cf220000-0000-0000-0000-000000000302"), MockNotionDataStore.Page4Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Customer escalation contains the customer timeline, owner notes, and next response window."
        });

        var searchPageIds = new HashSet<Guid>
        {
            MockNotionDataStore.Page1Id,
            MockNotionDataStore.Page2Id,
            MockNotionDataStore.Page3Id,
            MockNotionDataStore.Page4Id
        };
        var searchBlocks = _blocks.Values
            .Where(block => searchPageIds.Contains(block.PageId))
            .ToArray();
        foreach (var block in searchBlocks)
        {
            block.CreatedAt = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);
            block.LastEditedAt = new DateTime(2026, 1, 20, 9, 0, 0, DateTimeKind.Utc);
        }
    }

    public void SeedE2EBulkPages()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page3Id, MockNotionDataStore.Page4Id);

        AddTo(Guid.Parse("cf240001-0000-0000-0000-000000000001"), MockNotionDataStore.Page1Id, BlockType.Heading1, 0, new HeadingBlockContent { Level = 1, Html = "CF24 Source Root" });
        AddTo(Guid.Parse("cf240001-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent { Html = "Root copy source content." });

        AddTo(Guid.Parse("cf240002-0000-0000-0000-000000000001"), MockNotionDataStore.Page2Id, BlockType.Heading2, 0, new HeadingBlockContent { Level = 2, Html = "CF24 Child A" });
        AddTo(Guid.Parse("cf240002-0000-0000-0000-000000000002"), MockNotionDataStore.Page2Id, BlockType.TodoItem, 1, new TodoBlockContent { Html = "Child action", IsChecked = false });

        AddTo(Guid.Parse("cf240003-0000-0000-0000-000000000001"), MockNotionDataStore.Page3Id, BlockType.Paragraph, 0, new TextBlockContent { Html = "Grandchild content." });
        AddTo(Guid.Parse("cf240004-0000-0000-0000-000000000001"), MockNotionDataStore.Page4Id, BlockType.Paragraph, 0, new TextBlockContent { Html = "Target destination." });
    }

    public void SeedE2ERestrictionsPage()
    {
        SeedE2EBulkPages();

        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page3Id);
        AddTo(MockNotionDataStore.Page1Id, BlockType.Heading1, 0, new HeadingBlockContent { Level = 1, Html = "CF20 Restricted Workspace" });
        AddTo(MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent { Html = "Root page used to verify explicit page restrictions, inherited read-only access, and no-view states." });

        AddTo(MockNotionDataStore.Page2Id, BlockType.Heading2, 0, new HeadingBlockContent { Level = 2, Html = "CF20 Child Inherits Restrictions" });
        AddTo(MockNotionDataStore.Page2Id, BlockType.Paragraph, 1, new TextBlockContent { Html = "This child page inherits the root restrictions." });

        AddTo(MockNotionDataStore.Page3Id, BlockType.Paragraph, 0, new TextBlockContent { Html = "Grandchild inherited restriction content." });
    }

    public void SeedE2EPageInfoLikePage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page4Id);

        AddTo(Guid.Parse("cf160000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "One two three four five six seven eight nine ten"
        });
        AddTo(Guid.Parse("cf160000-0000-0000-0000-000000000003"), MockNotionDataStore.Page2Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Secondary analytics page content."
        });
        AddTo(Guid.Parse("cf160000-0000-0000-0000-000000000004"), MockNotionDataStore.Page4Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Team metrics page content."
        });
    }

    public void SeedE2EPublicSharePage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf330000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "This page is available through a public read-only link."
        });
        AddTo(Guid.Parse("cf330000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Callout, 1, new CalloutBlockContent
        {
            Html = "Visitors can read and comment without seeing the private workspace sidebar.",
            IconEmoji = "🔒",
            Variant = CalloutVariant.Info
        });
    }

    public void SeedE2EEmptyPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb000000-0000-0000-0000-000000000001"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = string.Empty
        });
    }

    public void SeedE2ECommentsPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb100010-0000-0000-0000-000000000001"), MockNotionDataStore.Page1Id, BlockType.Heading1, 0, new HeadingBlockContent
        {
            Level = 1,
            Html = "EB10 Comments Workspace"
        });
        AddTo(Guid.Parse("eb100010-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "This paragraph is used for block and inline comment recovery screenshots with enough text to anchor a visible selection."
        });
        AddTo(Guid.Parse("eb100010-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 2, new TextBlockContent
        {
            Html = "The second paragraph keeps margin threads, resolved badges and long nested discussions visually separated from the page-level comments panel."
        });
        AddTo(Guid.Parse("eb100010-0000-0000-0000-000000000004"), MockNotionDataStore.Page1Id, BlockType.Callout, 3, new CalloutBlockContent
        {
            Html = "Comments should remain readable, actionable and close to the reviewed content.",
            IconEmoji = "💬",
            Variant = CalloutVariant.Note
        });
    }

    public void SeedE2EExportPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page3Id);

        AddTo(Guid.Parse("cf250000-0000-0000-0000-000000000001"), MockNotionDataStore.Page1Id, BlockType.Heading1, 0, new HeadingBlockContent
        {
            Level = 1,
            Html = "CF25 Export Bridge"
        });
        AddTo(Guid.Parse("cf250000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "Root export content proves Markdown, HTML, PDF, DOCX, and ODT generation through the demo API."
        });

        var tableId = Guid.Parse("cf250000-0000-0000-0000-000000000010");
        AddTo(tableId, MockNotionDataStore.Page1Id, BlockType.Table, 2, new TableBlockContent
        {
            ColumnCount = 3,
            HasHeaderRow = true
        });
        AddChildTo(Guid.Parse("cf250000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 0, new TableRowBlockContent
        {
            RichCells = ToRichCells("Format", "Status", "Evidence")
        });
        AddChildTo(Guid.Parse("cf250000-0000-0000-0000-000000000012"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 1, new TableRowBlockContent
        {
            RichCells = ToRichCells("DOCX", "Ready", "Table survives export")
        });

        AddTo(Guid.Parse("cf250000-0000-0000-0000-000000000101"), MockNotionDataStore.Page2Id, BlockType.Heading2, 0, new HeadingBlockContent
        {
            Level = 2,
            Html = "CF25 Export Child"
        });
        AddTo(Guid.Parse("cf250000-0000-0000-0000-000000000102"), MockNotionDataStore.Page2Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "Child page content is included when the export menu enables subpages."
        });

        AddTo(Guid.Parse("cf250000-0000-0000-0000-000000000201"), MockNotionDataStore.Page3Id, BlockType.Heading2, 0, new HeadingBlockContent
        {
            Level = 2,
            Html = "CF25 Export Grandchild"
        });
        AddTo(Guid.Parse("cf250000-0000-0000-0000-000000000202"), MockNotionDataStore.Page3Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "Grandchild page content proves recursive subtree export."
        });
    }

    public void SeedE2EEmptyPageInfoPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);
    }

    public void SeedE2ETextFormattingPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "<strong><em><u><s><code>Combined active inline toolbar state for bold, italic, underline, strikethrough, and inline code.</code></s></u></em></strong>"
        });
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000004"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "SupercalifragilisticexpialidociousTempoBlazorNotionEditorLongUnbrokenLineWithoutSpacesForWrappingAndOverflowRegressionCoverage"
        });
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000005"), MockNotionDataStore.Page1Id, BlockType.Heading1, 2, new HeadingBlockContent
        {
            Level = 1,
            Html = "EB1 Heading One Baseline"
        });
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000006"), MockNotionDataStore.Page1Id, BlockType.Heading2, 3, new HeadingBlockContent
        {
            Level = 2,
            Html = "EB1 Heading Two Baseline"
        });
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000007"), MockNotionDataStore.Page1Id, BlockType.Heading3, 4, new HeadingBlockContent
        {
            Level = 3,
            Html = "EB1 Heading Three Baseline"
        });
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000008"), MockNotionDataStore.Page1Id, BlockType.Quote, 5, new TextBlockContent
        {
            Html = "Quoted decision note with enough copy to prove readable spacing, left rule contrast, and multiline text rhythm."
        });
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000009"), MockNotionDataStore.Page1Id, BlockType.Callout, 6, new CalloutBlockContent
        {
            IconEmoji = "i",
            Html = "Callout baseline checks icon alignment, background contrast, body wrapping, and spacing against neighboring text blocks.",
            BackgroundColor = "blue"
        });
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.Divider, 7, new DividerBlockContent());
        AddTo(Guid.Parse("eb100000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, BlockType.Code, 8, new CodeBlockContent
        {
            Language = "C#",
            Code = """
public sealed class Eb1BaselineRenderer
{
    public string Render() => "The quick baseline line keeps code text readable while horizontal scrolling protects the editor shell from overflow regression.";
}
"""
        });
    }

    public void SeedE2EInlineToolbarPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb400000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "<strong><em><u><s><code>Inline toolbar active state target</code></s></u></em></strong>"
        });
        AddTo(Guid.Parse("eb400000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "Right edge selection target for color and turn into panels.",
            Alignment = TextAlignment.Right
        });

        for (var i = 0; i < 10; i++)
        {
            AddTo(Guid.Parse($"eb400000-0000-0000-0000-{i + 100:000000000000}"), MockNotionDataStore.Page1Id, BlockType.Paragraph, i + 2, new TextBlockContent
            {
                Html = $"Inline toolbar spacer paragraph {i + 1:00} keeps the bottom-edge selection deterministic."
            });
        }

        AddTo(Guid.Parse("eb400000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 12, new TextBlockContent
        {
            Html = "Bottom edge selection target for toolbar placement."
        });
    }

    public void SeedE2EListTodoPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        // The 1→2→3 chain comes first so the standalone item below it can legally Tab-indent
        // through the levels — a list item may sit at most one level deeper than the item above
        // it, and the first block of a page has nothing to nest under at all.
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.BulletList, 0, new ListBlockContent
        {
            Html = "EB2 bullet child item",
            IndentLevel = 1
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000004"), MockNotionDataStore.Page1Id, BlockType.BulletList, 1, new ListBlockContent
        {
            Html = "EB2 bullet grandchild item",
            IndentLevel = 2
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000005"), MockNotionDataStore.Page1Id, BlockType.BulletList, 2, new ListBlockContent
        {
            Html = "EB2 bullet third-level item",
            IndentLevel = 3
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.BulletList, 3, new ListBlockContent
        {
            Html = "EB2 bullet parent item",
            IndentLevel = 0
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000006"), MockNotionDataStore.Page1Id, BlockType.BulletList, 4, new ListBlockContent
        {
            Html = "Convert this list item",
            IndentLevel = 0
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000007"), MockNotionDataStore.Page1Id, BlockType.NumberedList, 5, new ListBlockContent
        {
            Html = "Numbered sequence first",
            IndentLevel = 0
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000008"), MockNotionDataStore.Page1Id, BlockType.NumberedList, 6, new ListBlockContent
        {
            Html = "Numbered sequence nested",
            IndentLevel = 1
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000009"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 7, new TodoBlockContent
        {
            Html = "Unchecked checklist item",
            IsChecked = false
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.Toggle, 8, new ToggleBlockContent
        {
            Html = "Release notes checklist",
            IsOpen = true
        });
        AddChildTo(Guid.Parse("eb200000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, Guid.Parse("eb200000-0000-0000-0000-000000000010"), BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Child paragraph inside the expanded toggle."
        });
        AddChildTo(Guid.Parse("eb200000-0000-0000-0000-000000000012"), MockNotionDataStore.Page1Id, Guid.Parse("eb200000-0000-0000-0000-000000000010"), BlockType.TodoItem, 1, new TodoBlockContent
        {
            Html = "Child todo inside the expanded toggle",
            IsChecked = true
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.Toggle, 9, new ToggleBlockContent
        {
            Html = "Empty toggle ready for notes",
            IsOpen = false
        });
        AddTo(Guid.Parse("eb200000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 10, new TodoBlockContent
        {
            Html = "Checked checklist item with a visible completed state",
            IsChecked = true
        });

        for (var i = 0; i < 26; i++)
        {
            AddTo(Guid.Parse($"eb200000-0000-0000-0000-{i + 100:000000000000}"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 11 + i, new TodoBlockContent
            {
                Html = $"Long checklist row {i + 1:00} with enough text to verify wrapping, checkbox alignment, and vertical rhythm across a dense todo list.",
                IsChecked = i % 5 == 0
            });
        }
    }

    public void SeedE2EActionItemsPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf300000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 0, new TodoBlockContent
        {
            Html = "Assign launch owner",
            IsChecked = false
        });
        AddTo(Guid.Parse("cf300000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 1, new TodoBlockContent
        {
            Html = "Overdue task with an owner",
            AssigneeId = "alice",
            AssigneeDisplayName = "Alice Morgan",
            DueDate = DateTime.Today.AddDays(-2),
            IsChecked = false,
            IsOverdue = true
        });
        AddTo(Guid.Parse("cf300000-0000-0000-0000-000000000004"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 2, new TodoBlockContent
        {
            Html = "Prepare tomorrow handoff",
            AssigneeId = "bob",
            AssigneeDisplayName = "Bob Stone",
            DueDate = DateTime.Today.AddDays(1),
            IsChecked = false
        });
        AddTo(Guid.Parse("cf300000-0000-0000-0000-000000000005"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 3, new TodoBlockContent
        {
            Html = "Completed historical action item",
            AssigneeId = "clara",
            AssigneeDisplayName = "Clara Dvorak",
            DueDate = DateTime.Today.AddDays(-14),
            IsChecked = true
        });
    }

    public void SeedE2EMentionTokenPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb500000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Release status target"
        });
        AddTo(Guid.Parse("eb500000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "Invoice deadline <span contenteditable=\"false\" class=\"tm-notion-token tm-notion-token--unknown\" data-key=\"unknown.invoice_deadline\">{{unknown.invoice_deadline}}</span>"
        });
    }

    public void SeedE2ETasksPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf400000-0000-0000-0000-000000000101"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 0, new TodoBlockContent
        {
            Html = "Prepare customer launch checklist",
            AssigneeId = "demo",
            AssigneeDisplayName = "Demo User",
            DueDate = DateTime.Today.AddDays(-2),
            IsChecked = false
        });
        AddTo(Guid.Parse("cf400000-0000-0000-0000-000000000102"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 1, new TodoBlockContent
        {
            Html = "Review onboarding copy",
            AssigneeId = "demo",
            AssigneeDisplayName = "Demo User",
            DueDate = DateTime.Today,
            IsChecked = false
        });
        AddTo(Guid.Parse("cf400000-0000-0000-0000-000000000103"), MockNotionDataStore.Page1Id, BlockType.TodoItem, 2, new TodoBlockContent
        {
            Html = "Archive completed launch note",
            AssigneeId = "demo",
            AssigneeDisplayName = "Demo User",
            DueDate = DateTime.Today.AddDays(-7),
            IsChecked = true
        });
    }

    public void SeedE2EEmptyTasksPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf401000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "No assigned tasks on this page."
        });
    }

    public void SeedE2EManyTasksPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        for (var i = 0; i < 12; i++)
        {
            AddTo(Guid.Parse($"cf402000-0000-0000-0000-{i + 100:000000000000}"), MockNotionDataStore.Page1Id, BlockType.TodoItem, i, new TodoBlockContent
            {
                Html = $"Follow-up task {i + 1:00}",
                AssigneeId = "demo",
                AssigneeDisplayName = "Demo User",
                DueDate = i < 7 ? DateTime.Today.AddDays(-i - 1) : DateTime.Today.AddDays(i - 6),
                IsChecked = false
            });
        }
    }

    public void SeedE2EWorkItemsPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf500000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.WorkItem, 0, new WorkItemBlockContent
        {
            SourceKey = "demo",
            ExternalId = "DEMO-101",
            DisplayMode = WorkItemDisplayMode.Card
        });
        AddTo(Guid.Parse("cf500000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.WorkItem, 1, new WorkItemBlockContent
        {
            SourceKey = "demo",
            ExternalId = "DEMO-202",
            DisplayMode = WorkItemDisplayMode.List
        });
        AddTo(Guid.Parse("cf500000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.WorkItem, 2, new WorkItemBlockContent
        {
            SourceKey = "demo",
            ExternalId = "DEMO-303",
            DisplayMode = WorkItemDisplayMode.Inline
        });
        AddTo(Guid.Parse("cf500000-0000-0000-0000-000000000040"), MockNotionDataStore.Page1Id, BlockType.WorkItem, 3, new WorkItemBlockContent
        {
            SourceKey = "demo",
            ExternalId = "DEMO-999",
            DisplayMode = WorkItemDisplayMode.Card
        });
        AddTo(Guid.Parse("cf500000-0000-0000-0000-000000000050"), MockNotionDataStore.Page1Id, BlockType.WorkItem, 4, new WorkItemBlockContent
        {
            SourceKey = "offline",
            ExternalId = "OFFLINE-1",
            DisplayMode = WorkItemDisplayMode.Card,
            CachedSnapshot = new Tempo.Blazor.Abstractions.WorkItems.TmWorkItem
            {
                Id = "OFFLINE-1",
                SourceKey = "offline",
                ExternalId = "OFFLINE-1",
                Url = "https://tracker.demo.local/work/OFFLINE-1",
                Title = "Cached fallback survives provider outage",
                StatusLabel = "Cached",
                StatusColor = "#64748b",
                TypeLabel = "Incident",
                Assignees = [new Tempo.Blazor.Abstractions.WorkItems.TmWorkItemAssignee { Id = "demo-user", Name = "Demo User" }],
                PriorityLabel = "High",
                UpdatedAt = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc)
            }
        });
    }

    public void SeedE2ESmartLinksPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf800000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = string.Empty
        });
        AddTo(Guid.Parse("cf800000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = string.Empty
        });
        AddTo(Guid.Parse("cf800000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 2, new TextBlockContent
        {
            Html = string.Empty
        });
    }

    public void SeedE2EContentByLabelPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf700000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.ContentByLabel, 0, new ContentByLabelBlockContent());
        AddTo(Guid.Parse("cf700000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.ContentByLabel, 1, new ContentByLabelBlockContent
        {
            Labels = ["release"],
            MaxItems = 1,
            SortBy = ContentByLabelSortBy.LastEditedDescending
        });
        AddTo(Guid.Parse("cf700000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.ContentByLabel, 2, new ContentByLabelBlockContent
        {
            Labels = ["missing"],
            MaxItems = 5,
            SortBy = ContentByLabelSortBy.TitleAscending
        });
    }

    public void SeedE2ECollaborationPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);
        AddTo(Guid.Parse("eb140000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Collaboration presence anchor paragraph."
        });
        AddTo(Guid.Parse("eb140000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "Long collaborator names remain readable on this paragraph."
        });
        AddTo(Guid.Parse("eb140000-0000-0000-0000-000000000004"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 2, new TextBlockContent
        {
            Html = "Overlapping remote cursors stack into one readable indicator."
        });
    }

    public void SeedE2ESpecialBlocksPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page5Id);

        var syncId = Guid.Parse("eb150000-0000-0000-0000-000000000900");
        var originBlockId = Guid.Parse("eb150000-0000-0000-0000-000000000080");

        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000001"), MockNotionDataStore.Page1Id, BlockType.Heading1, 0, new HeadingBlockContent
        {
            Level = 1,
            Html = "EB15 Special Blocks"
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "This page verifies equation, bookmark, embed, Tempo, synced and navigation blocks through the HTTPS Demo API."
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Heading2, 2, new HeadingBlockContent
        {
            Level = 2,
            Html = "Equations"
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.Equation, 3, new EquationBlockContent
        {
            Expression = @"\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}"
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, BlockType.Equation, 4, new EquationBlockContent
        {
            Expression = @"\invalidcommand{missing"
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.Bookmark, 5, new BookmarkBlockContent
        {
            Url = "https://docs.tempo.local/notion/special-blocks",
            Title = "Tempo Notion special blocks",
            Description = "Production-ready blocks for equations, embeds, synced content and rich navigation.",
            Domain = "docs.tempo.local",
            FaviconUrl = "https://docs.tempo.local/favicon.ico",
            CoverImageUrl = "https://docs.tempo.local/assets/notion-special-blocks.png",
            Caption = "Resolved via Demo API bookmark provider."
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000021"), MockNotionDataStore.Page1Id, BlockType.Bookmark, 6, new BookmarkBlockContent
        {
            Url = "https://fallback.tempo.local/provider-timeout",
            Domain = "fallback.tempo.local",
            Caption = "Provider fallback keeps the URL usable."
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000022"), MockNotionDataStore.Page1Id, BlockType.Bookmark, 7, new BookmarkBlockContent
        {
            Url = "https://static.tempo.local/release-notes",
            Title = "Static fallback release notes",
            Domain = "static.tempo.local",
            Caption = "Static metadata remains readable without a cover image."
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.Embed, 8, new EmbedBlockContent
        {
            Provider = EmbedProvider.CodePen,
            Url = "https://codepen.io/alvaromontoro/embed/vYexLGV",
            Width = 720,
            Height = 320,
            Caption = "CodePen provider is detected and embedded."
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000031"), MockNotionDataStore.Page1Id, BlockType.Embed, 9, new EmbedBlockContent
        {
            Provider = EmbedProvider.Generic,
            Url = "https://unknown-provider.tempo.local/embed/demo",
            Width = 720,
            Height = 220,
            Caption = "Unknown providers remain available as generic embeds."
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000040"), MockNotionDataStore.Page1Id, BlockType.Diagram, 10, new DiagramBlockContent());
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000050"), MockNotionDataStore.Page1Id, BlockType.Wireframe, 11, new WireframeBlockContent());
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000060"), MockNotionDataStore.Page1Id, BlockType.Spreadsheet, 12, new SpreadsheetBlockContent());
        AddTo(originBlockId, MockNotionDataStore.Page1Id, BlockType.SyncedBlockOrigin, 13, new SyncedBlockOriginContent
        {
            SyncId = syncId
        });
        AddChildTo(Guid.Parse("eb150000-0000-0000-0000-000000000081"), MockNotionDataStore.Page1Id, originBlockId, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Synced origin content shared with references."
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000090"), MockNotionDataStore.Page1Id, BlockType.SyncedBlockRef, 14, new SyncedBlockRefContent
        {
            SyncId = syncId,
            OriginPageId = MockNotionDataStore.Page1Id,
            OriginBlockId = originBlockId
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000100"), MockNotionDataStore.Page1Id, BlockType.ChildPage, 15, new ChildPageBlockContent
        {
            ChildPageId = MockNotionDataStore.Page2Id,
            Title = "Product Roadmap",
            IconEmoji = "P"
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000101"), MockNotionDataStore.Page1Id, BlockType.LinkedPage, 16, new LinkedPageBlockContent
        {
            LinkedPageId = MockNotionDataStore.Page3Id,
            Title = "Meeting Notes",
            IconEmoji = "M"
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000102"), MockNotionDataStore.Page1Id, BlockType.Breadcrumb, 17, new BreadcrumbBlockContent());
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000110"), MockNotionDataStore.Page1Id, BlockType.TableOfContents, 18, new TableOfContentsBlockContent
        {
            MaxLevel = 3
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000120"), MockNotionDataStore.Page1Id, BlockType.TemplateButton, 19, new TemplateButtonBlockContent
        {
            Label = "Insert release checklist",
            TemplateBlocks =
            [
                new PageBlock
                {
                    Id = Guid.Parse("eb150000-0000-0000-0000-000000000121"),
                    PageId = MockNotionDataStore.Page1Id,
                    Type = BlockType.Heading2,
                    Order = 0,
                    Content = new HeadingBlockContent { Level = 2, Html = "Release checklist" },
                    CreatedAt = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                },
                new PageBlock
                {
                    Id = Guid.Parse("eb150000-0000-0000-0000-000000000122"),
                    PageId = MockNotionDataStore.Page1Id,
                    Type = BlockType.TodoItem,
                    Order = 1,
                    Content = new TodoBlockContent { Html = "Verify screenshots and UX review notes.", IsChecked = false },
                    CreatedAt = DateTime.UtcNow,
                    LastEditedAt = DateTime.UtcNow
                }
            ]
        });

        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000501"), MockNotionDataStore.Page5Id, BlockType.Heading1, 0, new HeadingBlockContent
        {
            Level = 1,
            Html = "Architecture Guide"
        });
        AddTo(Guid.Parse("eb150000-0000-0000-0000-000000000502"), MockNotionDataStore.Page5Id, BlockType.Breadcrumb, 1, new BreadcrumbBlockContent());
    }

    public void SeedE2EDragDropPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        var columnListId = Guid.Parse("eb160000-0000-0000-0000-000000000010");
        var leftColumnId = Guid.Parse("eb160000-0000-0000-0000-000000000011");
        var rightColumnId = Guid.Parse("eb160000-0000-0000-0000-000000000012");

        AddTo(Guid.Parse("eb160000-0000-0000-0000-000000000001"), MockNotionDataStore.Page1Id, BlockType.Heading1, 0, new HeadingBlockContent
        {
            Level = 1,
            Html = "EB16 Drag and Drop Recovery"
        });
        AddTo(Guid.Parse("eb160000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "Alpha top-level block stays readable after reorder."
        });
        AddTo(Guid.Parse("eb160000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 2, new TextBlockContent
        {
            Html = "Bravo top-level block moves down and into a column."
        });
        AddTo(Guid.Parse("eb160000-0000-0000-0000-000000000004"), MockNotionDataStore.Page1Id, BlockType.Callout, 3, new CalloutBlockContent
        {
            Html = "Context menu target for duplicate, delete and copy-link affordance.",
            Variant = CalloutVariant.Info,
            IconEmoji = "⋯"
        });

        AddTo(columnListId, MockNotionDataStore.Page1Id, BlockType.ColumnList, 4, new ColumnListBlockContent
        {
            ColumnCount = 2
        });
        AddChildTo(leftColumnId, MockNotionDataStore.Page1Id, columnListId, BlockType.Column, 0, new ColumnBlockContent
        {
            ColumnIndex = 0,
            WidthPercent = 50
        });
        AddChildTo(rightColumnId, MockNotionDataStore.Page1Id, columnListId, BlockType.Column, 1, new ColumnBlockContent
        {
            ColumnIndex = 1,
            WidthPercent = 50
        });
        AddChildTo(Guid.Parse("eb160000-0000-0000-0000-000000000101"), MockNotionDataStore.Page1Id, leftColumnId, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Left column child block accepts incoming top-level blocks."
        });
        AddChildTo(Guid.Parse("eb160000-0000-0000-0000-000000000102"), MockNotionDataStore.Page1Id, leftColumnId, BlockType.TodoItem, 1, new TodoBlockContent
        {
            Html = "Left column task remains stable during cross-column moves.",
            IsChecked = false
        });
        AddChildTo(Guid.Parse("eb160000-0000-0000-0000-000000000201"), MockNotionDataStore.Page1Id, rightColumnId, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Right column child block receives cross-column drags."
        });

        AddTo(Guid.Parse("eb160000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 5, new TextBlockContent
        {
            Html = "Charlie top-level block receives content moved out of a column."
        });
    }

    public void SeedE2EPageReactionsPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf170000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Page reaction anchor paragraph."
        });
    }

    public void SeedE2ELayoutPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddColumnList(
            Guid.Parse("eb800000-0000-0000-0000-000000000010"),
            0,
            [
                ("Column one content", 50d),
                ("Column two content", 50d)
            ]);
        AddChildTo(
            Guid.Parse("eb800000-0000-0000-0002-000000000100"),
            MockNotionDataStore.Page1Id,
            Guid.Parse("eb800000-0000-0000-0000-000000000012"),
            BlockType.Paragraph,
            1,
            new TextBlockContent { Html = string.Empty });
        AddColumnList(
            Guid.Parse("eb800000-0000-0000-0000-000000000030"),
            1,
            [
                ("Mobile column one", 25d),
                ("Mobile column two", 25d),
                ("Mobile column three", 25d),
                ("Mobile column four", 25d)
            ]);

        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000100"), MockNotionDataStore.Page1Id, BlockType.TableOfContents, 2, new TableOfContentsBlockContent
        {
            MaxLevel = 3
        });

        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000110"), MockNotionDataStore.Page1Id, BlockType.Heading1, 3, new HeadingBlockContent
        {
            Level = 1,
            Html = "Layout Review"
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000111"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 4, new TextBlockContent
        {
            Html = "Column layout checks use production column blocks and the table of contents below tracks headings while the user scrolls."
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000112"), MockNotionDataStore.Page1Id, BlockType.Heading2, 5, new HeadingBlockContent
        {
            Level = 2,
            Html = "Desktop Columns"
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000113"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 6, new TextBlockContent
        {
            Html = "Desktop widths must stay balanced, resizable, and readable after adding a third column."
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000114"), MockNotionDataStore.Page1Id, BlockType.Heading2, 7, new HeadingBlockContent
        {
            Level = 2,
            Html = "Responsive Stacking"
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000115"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 8, new TextBlockContent
        {
            Html = "Small screens stack every column vertically and hide dividers to avoid cramped touch targets."
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000116"), MockNotionDataStore.Page1Id, BlockType.Heading3, 9, new HeadingBlockContent
        {
            Level = 3,
            Html = "Resize Divider"
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000117"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 10, new TextBlockContent
        {
            Html = "The divider grip should remain visible without adding document-level horizontal overflow."
        });
        AddTo(Guid.Parse("eb800000-0000-0000-0000-000000000118"), MockNotionDataStore.Page1Id, BlockType.Heading3, 11, new HeadingBlockContent
        {
            Level = 3,
            Html = "Table of Contents Scroll Spy"
        });

        for (var i = 0; i < 7; i++)
        {
            AddTo(Guid.Parse($"eb800000-0000-0000-0000-{i + 400:000000000000}"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 12 + i, new TextBlockContent
            {
                Html = $"Scroll-spy spacer paragraph {i + 1:00} keeps the EB8 page tall enough for a deterministic active table-of-contents state."
            });
        }
    }

    public void SeedE2EEmptyTocPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb810000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.TableOfContents, 0, new TableOfContentsBlockContent
        {
            MaxLevel = 3
        });
        AddTo(Guid.Parse("eb810000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "This page intentionally has no heading blocks, so the table of contents renders its empty state."
        });
    }

    public void SeedE2ETablePage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);
        var tableId = Guid.Parse("eb700000-0000-0000-0000-000000000010");
        var wideTableId = Guid.Parse("eb700000-0000-0000-0000-000000000020");
        var emptyTableId = Guid.Parse("eb700000-0000-0000-0000-000000000030");
        var advancedTableId = Guid.Parse("cf110000-0000-0000-0000-000000000010");
        var securityTableId = Guid.Parse("cf120000-0000-0000-0000-000000000010");

        AddTo(tableId, MockNotionDataStore.Page1Id, BlockType.Table, 0, new TableBlockContent
        {
            ColumnCount = 4,
            HasHeaderRow = true
        });
        AddChildTo(Guid.Parse("eb700000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 0, new TableRowBlockContent
        {
            RichCells = ToRichCells("Name", "Status", "Owner", "Notes")
        });
        AddChildTo(Guid.Parse("eb700000-0000-0000-0000-000000000012"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 1, new TableRowBlockContent
        {
            RichCells = ToRichCells("Customer import", "In progress", "Nora", "Validates rows and columns against production table interactions.")
        });
        AddChildTo(Guid.Parse("eb700000-0000-0000-0000-000000000013"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 2, new TableRowBlockContent
        {
            RichCells = ToRichCells(
                "Billing audit",
                "Blocked",
                "Ivan",
                "The content in this cell is intentionally long enough to wrap onto multiple lines in a fixed-width table cell while staying readable and preserving row controls."
            )
        });

        AddTo(wideTableId, MockNotionDataStore.Page1Id, BlockType.Table, 1, new TableBlockContent
        {
            ColumnCount = 8,
            HasHeaderRow = true,
            HasHeaderColumn = true
        });
        AddChildTo(Guid.Parse("eb700000-0000-0000-0000-000000000021"), MockNotionDataStore.Page1Id, wideTableId, BlockType.TableRow, 0, new TableRowBlockContent
        {
            RichCells = ToRichCells("Metric", "Q1", "Q2", "Q3", "Q4", "Owner", "Risk", "Decision")
        });
        AddChildTo(Guid.Parse("eb700000-0000-0000-0000-000000000022"), MockNotionDataStore.Page1Id, wideTableId, BlockType.TableRow, 1, new TableRowBlockContent
        {
            RichCells = ToRichCells("Revenue", "$120k", "$144k", "$151k", "$172k", "Finance", "Low", "Scale")
        });
        AddChildTo(Guid.Parse("eb700000-0000-0000-0000-000000000023"), MockNotionDataStore.Page1Id, wideTableId, BlockType.TableRow, 2, new TableRowBlockContent
        {
            RichCells = ToRichCells("Support load", "42", "51", "58", "49", "Care", "Medium", "Watch")
        });

        AddTo(emptyTableId, MockNotionDataStore.Page1Id, BlockType.Table, 2, new TableBlockContent
        {
            ColumnCount = 3,
            HasHeaderRow = false
        });

        AddTo(advancedTableId, MockNotionDataStore.Page1Id, BlockType.Table, 3, new TableBlockContent
        {
            ColumnCount = 6,
            HasHeaderRow = true,
            HasHeaderColumn = true
        });
        AddChildTo(Guid.Parse("cf110000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, advancedTableId, BlockType.TableRow, 0, new TableRowBlockContent
        {
            RichCells =
            [
                Cell("Roadmap summary", 2, 1, "rgba(59, 130, 246, 0.18)"),
                Hidden(0, 0),
                Cell("Status", 1, 1, "rgba(59, 130, 246, 0.12)"),
                Cell("Owner", 1, 1, "rgba(59, 130, 246, 0.12)"),
                Cell("Risk", 1, 1, "rgba(59, 130, 246, 0.12)"),
                Cell("Decision", 1, 1, "rgba(59, 130, 246, 0.12)")
            ]
        });
        AddChildTo(Guid.Parse("cf110000-0000-0000-0000-000000000012"), MockNotionDataStore.Page1Id, advancedTableId, BlockType.TableRow, 1, new TableRowBlockContent
        {
            RichCells =
            [
                Cell("Discovery"),
                Cell("Scope"),
                Cell("Done", 1, 2, "rgba(34, 197, 94, 0.16)"),
                Cell("Nora"),
                Cell("Low", 1, 1, "rgba(34, 197, 94, 0.12)"),
                Cell("Ship")
            ]
        });
        AddChildTo(Guid.Parse("cf110000-0000-0000-0000-000000000013"), MockNotionDataStore.Page1Id, advancedTableId, BlockType.TableRow, 2, new TableRowBlockContent
        {
            RichCells =
            [
                Cell("Implementation"),
                Cell("API"),
                Hidden(1, 2),
                Cell("Ivan"),
                Cell("Medium", 1, 1, "rgba(245, 158, 11, 0.16)"),
                Cell("Watch")
            ]
        });
        AddChildTo(Guid.Parse("cf110000-0000-0000-0000-000000000014"), MockNotionDataStore.Page1Id, advancedTableId, BlockType.TableRow, 3, new TableRowBlockContent
        {
            RichCells =
            [
                Cell("Launch"),
                Cell("Comms"),
                Cell("Blocked", 1, 1, "rgba(239, 68, 68, 0.14)"),
                Cell("Sara"),
                Cell("High", 1, 1, "rgba(239, 68, 68, 0.12)"),
                Cell("Escalate")
            ]
        });

        AddTo(securityTableId, MockNotionDataStore.Page1Id, BlockType.Table, 4, new TableBlockContent
        {
            ColumnCount = 1
        });
        AddChildTo(Guid.Parse("cf120000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, securityTableId, BlockType.TableRow, 0, new TableRowBlockContent
        {
            RichCells =
            [
                Cell(
                    """Safe historical content <strong>remains visible</strong><img src=x onerror="window.__notionXssTriggered=true">""",
                    backgroundColor: "red;position:fixed")
            ]
        });
    }

    public void SeedE2EAtomicTablePage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);
        var tableId = Guid.Parse("f6000000-0000-0000-0000-000000000010");
        AddTo(tableId, MockNotionDataStore.Page1Id, BlockType.Table, 0, new TableBlockContent
        {
            ColumnCount = 3,
            HasHeaderRow = true,
            HasHeaderColumn = true,
            ColumnWidths = [220, 150, 180]
        });
        AddChildTo(Guid.Parse("f6000000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 0, new TableRowBlockContent
        {
            RichCells =
            [
                new NotionTableCell
                {
                    Html = "<strong>Atomic authoring</strong>",
                    Inlines =
                    [
                        new NotionRichTextInline
                        {
                            Text = "Atomic authoring",
                            Bold = true,
                            TextColor = "#1d4ed8"
                        }
                    ],
                    ColSpan = 2,
                    BackgroundColor = "#dbeafe",
                    TextColor = "#1d4ed8",
                    VerticalAlignment = NotionTableVerticalAlignment.Middle,
                    Borders = new NotionTableCellBorders
                    {
                        Bottom = new NotionTableBorder
                        {
                            Style = NotionTableBorderStyle.Solid,
                            Color = "#60a5fa",
                            Width = 2
                        }
                    }
                },
                Hidden(0, 0),
                new NotionTableCell
                {
                    Html = "State",
                    BackgroundColor = "#dbeafe",
                    TextColor = "#1f2937"
                }
            ]
        });
        AddChildTo(Guid.Parse("f6000000-0000-0000-0000-000000000012"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 1, new TableRowBlockContent
        {
            RichCells =
            [
                new NotionTableCell { Html = "Batch save" },
                new NotionTableCell
                {
                    Html = "<strong>One request</strong>",
                    HorizontalAlignment = NotionTableHorizontalAlignment.Center,
                    BackgroundColor = "#dcfce7",
                    TextColor = "#1f2937"
                },
                new NotionTableCell { Html = "Ready" }
            ]
        });
        AddChildTo(Guid.Parse("f6000000-0000-0000-0000-000000000013"), MockNotionDataStore.Page1Id, tableId, BlockType.TableRow, 2, new TableRowBlockContent
        {
            RichCells =
            [
                new NotionTableCell { Html = "Conflict recovery" },
                new NotionTableCell
                {
                    Html = "Reload / reapply",
                    HorizontalAlignment = NotionTableHorizontalAlignment.Center,
                    BackgroundColor = "#fef3c7",
                    TextColor = "#1f2937"
                },
                new NotionTableCell { Html = "Optimistic token" }
            ]
        });
    }

    public void SeedE2EKrFidelityPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);
        var thresholdTableId =
            Guid.Parse("f7000000-0000-0000-0000-000000000010");
        AddTo(
            thresholdTableId,
            MockNotionDataStore.Page1Id,
            BlockType.Table,
            0,
            new TableBlockContent
            {
                ColumnCount = 8,
                HasHeaderRow = true,
                ColumnWidths = [125, 76, 79, 77, 73, 76, 75, 81]
            });
        AddChildTo(
            Guid.Parse("f7000000-0000-0000-0000-000000000011"),
            MockNotionDataStore.Page1Id,
            thresholdTableId,
            BlockType.TableRow,
            0,
            new TableRowBlockContent
            {
                RichCells =
                [
                    FidelityCell("Threshold / Typ udalosti", bold: true),
                    FidelityCell("EL01", bold: true),
                    FidelityCell("EL02", bold: true),
                    FidelityCell("EL03", bold: true),
                    FidelityCell("EL04", bold: true),
                    FidelityCell("EL05", bold: true),
                    FidelityCell("EL06", bold: true),
                    FidelityCell("EL07", bold: true)
                ]
            });
        AddChildTo(
            Guid.Parse("f7000000-0000-0000-0000-000000000012"),
            MockNotionDataStore.Page1Id,
            thresholdTableId,
            BlockType.TableRow,
            1,
            new TableRowBlockContent
            {
                RichCells =
                [
                    FidelityCell("mio", "#FDE9D9"),
                    FidelityCell("mio", "#FCD5B4"),
                    FidelityCell("mio", "#FCD5B4"),
                    FidelityCell("mio", "#FCD5B4"),
                    FidelityCell("mio", "#FCD5B4"),
                    FidelityCell("mio", "#FCD5B4"),
                    FidelityCell("mio", "#FCD5B4"),
                    FidelityCell("mio", "#FCD5B4")
                ]
            });
        AddChildTo(
            Guid.Parse("f7000000-0000-0000-0000-000000000013"),
            MockNotionDataStore.Page1Id,
            thresholdTableId,
            BlockType.TableRow,
            2,
            new TableRowBlockContent
            {
                RichCells =
                [
                    FidelityCell("mio"),
                    FidelityCell(
                        "Nezmenená hodnota",
                        bold: true,
                        horizontalAlignment:
                        NotionTableHorizontalAlignment.Center,
                        colSpan: 7,
                        rowSpan: 4),
                    Hidden(2, 1),
                    Hidden(2, 1),
                    Hidden(2, 1),
                    Hidden(2, 1),
                    Hidden(2, 1),
                    Hidden(2, 1)
                ]
            });
        for (var rowIndex = 3; rowIndex < 6; rowIndex++)
        {
            AddChildTo(
                Guid.Parse(
                    $"f7000000-0000-0000-0000-{rowIndex + 11:000000000000}"),
                MockNotionDataStore.Page1Id,
                thresholdTableId,
                BlockType.TableRow,
                rowIndex,
                new TableRowBlockContent
                {
                    RichCells =
                    [
                        FidelityCell("mio"),
                        Hidden(2, 1),
                        Hidden(2, 1),
                        Hidden(2, 1),
                        Hidden(2, 1),
                        Hidden(2, 1),
                        Hidden(2, 1),
                        Hidden(2, 1)
                    ]
                });
        }

        var impactTableId =
            Guid.Parse("f7000000-0000-0000-0000-000000000020");
        AddTo(
            impactTableId,
            MockNotionDataStore.Page1Id,
            BlockType.Table,
            1,
            new TableBlockContent
            {
                ColumnCount = 2,
                ColumnWidths = [123, 247]
            });
        var impactRows = new[]
        {
            ("Combo-box", string.Empty, null, null),
            ("Very high", "3 mil €", "#FF0000", "#FF3300"),
            ("High", "> 0.5 - <= 3.0 mil €", "#FFC000", "#FFC000"),
            ("Medium", "> - <= mil €", "#FFFF00", "#FFFF00"),
            ("Low", "> 50 - < thd €", "#76933C", "#76933C"),
            ("Very low", "thd €", "#76933C", "#76933C")
        };
        for (var rowIndex = 0; rowIndex < impactRows.Length; rowIndex++)
        {
            var row = impactRows[rowIndex];
            AddChildTo(
                Guid.Parse(
                    $"f7000000-0000-0000-0000-{rowIndex + 21:000000000000}"),
                MockNotionDataStore.Page1Id,
                impactTableId,
                BlockType.TableRow,
                rowIndex,
                new TableRowBlockContent
                {
                    RichCells =
                    [
                        FidelityCell(
                            row.Item1,
                            row.Item3,
                            bold: rowIndex > 0),
                        FidelityCell(
                            row.Item2,
                            row.Item4,
                            bold: rowIndex > 0,
                            horizontalAlignment:
                            NotionTableHorizontalAlignment.Center)
                    ]
                });
        }
    }

    private static NotionTableCell FidelityCell(
        string text,
        string? backgroundColor = null,
        bool bold = false,
        NotionTableHorizontalAlignment horizontalAlignment =
            NotionTableHorizontalAlignment.Left,
        int colSpan = 1,
        int rowSpan = 1)
        => new()
        {
            Html = bold ? $"<strong>{text}</strong>" : text,
            Inlines =
            [
                new NotionRichTextInline
                {
                    Text = text,
                    Bold = bold,
                    TextColor = "#000000"
                }
            ],
            BackgroundColor = backgroundColor,
            TextColor = "#000000",
            HorizontalAlignment = horizontalAlignment,
            ColSpan = colSpan,
            RowSpan = rowSpan
        };

    private static NotionTableCell Cell(string html, int colSpan = 1, int rowSpan = 1, string? backgroundColor = null) => new()
    {
        Html = html,
        ColSpan = colSpan,
        RowSpan = rowSpan,
        BackgroundColor = backgroundColor
    };

    private static NotionTableCell Hidden(int originRow, int originColumn) => new()
    {
        IsMergeHidden = true,
        MergeOriginRow = originRow,
        MergeOriginColumn = originColumn
    };

    public void SeedE2EMediaPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);
        const string visiblePdfDataUrl = "data:application/pdf;base64,JVBERi0xLjQKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyAvUGFnZXMgMiAwIFIgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL1R5cGUgL1BhZ2VzIC9LaWRzIFszIDAgUl0gL0NvdW50IDEgPj4KZW5kb2JqCjMgMCBvYmoKPDwgL1R5cGUgL1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCA2MTIgNzkyXSAvUmVzb3VyY2VzIDw8IC9Gb250IDw8IC9GMSA0IDAgUiA+PiA+PiAvQ29udGVudHMgNSAwIFIgPj4KZW5kb2JqCjQgMCBvYmoKPDwgL1R5cGUgL0ZvbnQgL1N1YnR5cGUgL1R5cGUxIC9CYXNlRm9udCAvSGVsdmV0aWNhID4+CmVuZG9iago1IDAgb2JqCjw8IC9MZW5ndGggMzE3ID4+CnN0cmVhbQpCVAovRjEgMjQgVGYKNzIgNzMwIFRkCihFQjYgTWVkaWEgUERGIGJhc2VsaW5lKSBUagovRjEgMTIgVGYKMCAtMzYgVGQKKExvYWRlZCBQREYgYmxvY2sgd2l0aCB2aXNpYmxlIGNvbnRlbnQgZm9yIHNjcmVlbnNob3QgcmV2aWV3LikgVGoKMCAtMjQgVGQKKFVwbG9hZCwgb3BlbiBhbmQgZG93bmxvYWQgYWN0aW9ucyByZW1haW4gYXZhaWxhYmxlLikgVGoKRVQKMC4xMCAwLjQ1IDAuNzAgcmcKNzIgNjIwIDQ2OCAxOCByZSBmCjAuODggMC45NCAwLjk4IHJnCjcyIDU4MCAzMzAgMjQgcmUgZgo3MiA1NDAgNDEwIDI0IHJlIGYKNzIgNTAwIDI2MCAyNCByZSBmCmVuZHN0cmVhbQplbmRvYmoKeHJlZgowIDYKMDAwMDAwMDAwMCA2NTUzNSBmIAowMDAwMDAwMDA5IDAwMDAwIG4gCjAwMDAwMDAwNTggMDAwMDAgbiAKMDAwMDAwMDExNSAwMDAwMCBuIAowMDAwMDAwMjQxIDAwMDAwIG4gCjAwMDAwMDAzMTEgMDAwMDAgbiAKdHJhaWxlcgo8PCAvU2l6ZSA2IC9Sb290IDEgMCBSID4+CnN0YXJ0eHJlZgo2NzgKJSVFT0YK";

        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000011"), MockNotionDataStore.Page1Id, BlockType.Image, 0, new ImageBlockContent
        {
            Url = string.Empty,
            Caption = "Upload target"
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000021"), MockNotionDataStore.Page1Id, BlockType.Image, 1, new ImageBlockContent
        {
            Url = "data:image/svg+xml,%3Csvg%20xmlns%3D%27http%3A//www.w3.org/2000/svg%27%20viewBox%3D%270%200%20900%20520%27%3E%3Cdefs%3E%3ClinearGradient%20id%3D%27g%27%20x1%3D%270%27%20x2%3D%271%27%20y1%3D%270%27%20y2%3D%271%27%3E%3Cstop%20stop-color%3D%27%231f2937%27/%3E%3Cstop%20offset%3D%271%27%20stop-color%3D%27%230f766e%27/%3E%3C/linearGradient%3E%3C/defs%3E%3Crect%20width%3D%27900%27%20height%3D%27520%27%20rx%3D%2732%27%20fill%3D%27url%28%23g%29%27/%3E%3Crect%20x%3D%2790%27%20y%3D%2770%27%20width%3D%27720%27%20height%3D%27380%27%20rx%3D%2722%27%20fill%3D%27%23f8fafc%27%20opacity%3D%27.94%27/%3E%3Crect%20x%3D%27130%27%20y%3D%27120%27%20width%3D%27380%27%20height%3D%2730%27%20rx%3D%2715%27%20fill%3D%27%2314b8a6%27/%3E%3Crect%20x%3D%27130%27%20y%3D%27185%27%20width%3D%27640%27%20height%3D%2726%27%20rx%3D%2713%27%20fill%3D%27%23cbd5e1%27/%3E%3Crect%20x%3D%27130%27%20y%3D%27235%27%20width%3D%27570%27%20height%3D%2726%27%20rx%3D%2713%27%20fill%3D%27%23e2e8f0%27/%3E%3Crect%20x%3D%27130%27%20y%3D%27305%27%20width%3D%27170%27%20height%3D%2788%27%20rx%3D%2716%27%20fill%3D%27%23dbeafe%27/%3E%3Crect%20x%3D%27330%27%20y%3D%27305%27%20width%3D%27170%27%20height%3D%2788%27%20rx%3D%2716%27%20fill%3D%27%23ccfbf1%27/%3E%3Crect%20x%3D%27530%27%20y%3D%27305%27%20width%3D%27170%27%20height%3D%2788%27%20rx%3D%2716%27%20fill%3D%27%23fef3c7%27/%3E%3C/svg%3E",
            AltText = "Developer workstation",
            Caption = "Rendered image seed",
            Width = 640
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000031"), MockNotionDataStore.Page1Id, BlockType.Video, 2, new VideoBlockContent
        {
            Url = string.Empty,
            Caption = "Upload video target"
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000041"), MockNotionDataStore.Page1Id, BlockType.Video, 3, new VideoBlockContent
        {
            Provider = VideoProvider.YouTube,
            Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            Caption = "Rendered video seed",
            Width = 640
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000051"), MockNotionDataStore.Page1Id, BlockType.Audio, 4, new AudioBlockContent
        {
            Url = string.Empty,
            Caption = "Upload audio target"
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000061"), MockNotionDataStore.Page1Id, BlockType.Audio, 5, new AudioBlockContent
        {
            Provider = AudioProvider.Generic,
            Url = "data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAESsAACJWAAACABAAZGF0YQAAAAA=",
            Caption = "Rendered audio seed"
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000071"), MockNotionDataStore.Page1Id, BlockType.File, 6, new FileBlockContent
        {
            Url = string.Empty,
            Caption = "Upload file target"
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000081"), MockNotionDataStore.Page1Id, BlockType.File, 7, new FileBlockContent
        {
            FileName = "EB6-media-brief.pdf",
            FileSizeBytes = 861,
            ContentType = "application/pdf",
            Url = visiblePdfDataUrl,
            Caption = "Rendered file seed"
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000091"), MockNotionDataStore.Page1Id, BlockType.Pdf, 8, new PdfBlockContent
        {
            Url = string.Empty,
            Caption = "Upload PDF target"
        });
        AddTo(Guid.Parse("eb600000-0000-0000-0000-000000000101"), MockNotionDataStore.Page1Id, BlockType.Pdf, 9, new PdfBlockContent
        {
            Url = visiblePdfDataUrl,
            Caption = "Rendered PDF seed"
        });
    }

    public void SeedE2EIncludePage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page3Id, MockNotionDataStore.Page4Id, MockNotionDataStore.Page5Id);

        AddTo(Guid.Parse("cf120000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.IncludePage, 0, new IncludePageBlockContent());
        AddTo(Guid.Parse("cf120000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.IncludePage, 1, new IncludePageBlockContent { SourcePageId = MockNotionDataStore.Page5Id });
        AddTo(Guid.Parse("cf120000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.IncludePage, 2, new IncludePageBlockContent { SourcePageId = MockNotionDataStore.Page1Id });
        AddTo(Guid.Parse("cf120000-0000-0000-0000-000000000040"), MockNotionDataStore.Page1Id, BlockType.IncludePage, 3, new IncludePageBlockContent { SourcePageId = MockNotionDataStore.Page3Id });
        AddTo(Guid.Parse("cf120000-0000-0000-0000-000000000050"), MockNotionDataStore.Page1Id, BlockType.IncludePage, 4, new IncludePageBlockContent { SourcePageId = MockNotionDataStore.Page4Id });

        AddTo(Guid.Parse("cf120000-0000-0000-0001-000000000001"), MockNotionDataStore.Page2Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Included source paragraph"
        });
        AddTo(Guid.Parse("cf120000-0000-0000-0001-000000000002"), MockNotionDataStore.Page4Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "CF12 deep child content"
        });
        AddTo(Guid.Parse("cf120000-0000-0000-0001-000000000003"), MockNotionDataStore.Page4Id, BlockType.IncludePage, 1, new IncludePageBlockContent
        {
            SourcePageId = MockNotionDataStore.Page2Id
        });
    }

    public void SeedE2EChildrenDisplayPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("cf130000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.ChildrenDisplay, 0, new ChildrenDisplayBlockContent());
        AddTo(Guid.Parse("cf130000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.ChildrenDisplay, 1, new ChildrenDisplayBlockContent
        {
            RootPageId = MockNotionDataStore.Page5Id
        });
        AddTo(Guid.Parse("cf130000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.ChildrenDisplay, 2, new ChildrenDisplayBlockContent
        {
            RootPageId = MockNotionDataStore.Page2Id,
            Depth = 1
        });
        AddTo(Guid.Parse("cf130000-0000-0000-0000-000000000040"), MockNotionDataStore.Page1Id, BlockType.ChildrenDisplay, 3, new ChildrenDisplayBlockContent
        {
            RootPageId = MockNotionDataStore.Page2Id,
            Depth = 0
        });
        AddTo(Guid.Parse("cf130000-0000-0000-0000-000000000050"), MockNotionDataStore.Page1Id, BlockType.ChildrenDisplay, 4, new ChildrenDisplayBlockContent
        {
            RootPageId = MockNotionDataStore.Page6Id,
            Depth = 1,
            ShowIcons = false
        });
    }

    public void SeedE2EExcerptPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page3Id, MockNotionDataStore.Page4Id);

        AddTo(Guid.Parse("cf140000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.Excerpt, 0, new ExcerptBlockContent
        {
            Html = "CF14 target page reusable excerpt"
        });
        AddTo(Guid.Parse("cf140000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.ExcerptInclude, 1, new ExcerptIncludeBlockContent { SourcePageId = MockNotionDataStore.Page2Id });
        AddTo(Guid.Parse("cf140000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.ExcerptInclude, 2, new ExcerptIncludeBlockContent { SourcePageId = MockNotionDataStore.Page3Id });
        AddTo(Guid.Parse("cf140000-0000-0000-0000-000000000040"), MockNotionDataStore.Page1Id, BlockType.ExcerptInclude, 3, new ExcerptIncludeBlockContent { SourcePageId = MockNotionDataStore.Page4Id });

        AddTo(Guid.Parse("cf140000-0000-0000-0001-000000000001"), MockNotionDataStore.Page2Id, BlockType.Excerpt, 0, new ExcerptBlockContent
        {
            Html = "CF14 reusable source excerpt"
        });
        AddTo(Guid.Parse("cf140000-0000-0000-0001-000000000002"), MockNotionDataStore.Page2Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "This body paragraph must not be rendered"
        });
        AddTo(Guid.Parse("cf140000-0000-0000-0002-000000000001"), MockNotionDataStore.Page3Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "Source without excerpt body."
        });
    }

    public void SeedE2EPagePropertiesPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id, MockNotionDataStore.Page2Id, MockNotionDataStore.Page3Id, MockNotionDataStore.Page4Id);

        AddTo(Guid.Parse("cf150000-0000-0000-0000-000000000010"), MockNotionDataStore.Page1Id, BlockType.PageProperties, 0, new PagePropertiesBlockContent
        {
            Rows =
            [
                new PagePropertyRow { Key = "Status", ValueHtml = "Green" },
                new PagePropertyRow { Key = "Owner", ValueHtml = "Docs team" },
                new PagePropertyRow { Key = "Risk", ValueHtml = "Low" }
            ]
        });
        AddTo(Guid.Parse("cf150000-0000-0000-0000-000000000020"), MockNotionDataStore.Page1Id, BlockType.PageProperties, 1, new PagePropertiesBlockContent());
        AddTo(Guid.Parse("cf150000-0000-0000-0000-000000000030"), MockNotionDataStore.Page1Id, BlockType.PagePropertiesReport, 2, new PagePropertiesReportBlockContent
        {
            Labels = ["cf15-report"],
            Columns = ["Status", "Owner", "Risk"]
        });
        AddTo(Guid.Parse("cf150000-0000-0000-0000-000000000040"), MockNotionDataStore.Page1Id, BlockType.PagePropertiesReport, 3, new PagePropertiesReportBlockContent
        {
            Labels = ["cf15-empty"],
            Columns = ["Status"]
        });

        AddTo(Guid.Parse("cf150000-0000-0000-0000-000000000101"), MockNotionDataStore.Page2Id, BlockType.PageProperties, 0, new PagePropertiesBlockContent
        {
            Rows =
            [
                new PagePropertyRow { Key = "Status", ValueHtml = "<strong>Green</strong>" },
                new PagePropertyRow { Key = "Owner", ValueHtml = "Platform" },
                new PagePropertyRow { Key = "Risk", ValueHtml = "Low" }
            ]
        });
        AddTo(Guid.Parse("cf150000-0000-0000-0000-000000000102"), MockNotionDataStore.Page3Id, BlockType.PageProperties, 0, new PagePropertiesBlockContent
        {
            Rows =
            [
                new PagePropertyRow { Key = "Status", ValueHtml = "Amber" },
                new PagePropertyRow { Key = "Risk", ValueHtml = "Medium" }
            ]
        });
        AddTo(Guid.Parse("cf150000-0000-0000-0000-000000000103"), MockNotionDataStore.Page4Id, BlockType.PageProperties, 0, new PagePropertiesBlockContent
        {
            Rows =
            [
                new PagePropertyRow { Key = "Status", ValueHtml = "Archived" },
                new PagePropertyRow { Key = "Owner", ValueHtml = "Records" }
            ]
        });
    }

    public void CopyBlocksForPages(IReadOnlyDictionary<Guid, Guid> pageIdMap)
    {
        var sourceBlocks = _blocks.Values
            .Where(block => pageIdMap.ContainsKey(block.PageId))
            .OrderBy(block => block.PageId)
            .ThenBy(block => block.ParentBlockId.HasValue)
            .ThenBy(block => block.Order)
            .ToArray();

        var blockIdMap = sourceBlocks.ToDictionary(block => block.Id, _ => Guid.NewGuid());

        foreach (var source in sourceBlocks)
        {
            var clonedParentId = source.ParentBlockId is { } parentId && blockIdMap.TryGetValue(parentId, out var mappedParentId)
                ? mappedParentId
                : source.ParentBlockId;

            var clone = new PageBlock
            {
                Id = blockIdMap[source.Id],
                PageId = pageIdMap[source.PageId],
                ParentBlockId = clonedParentId,
                Type = source.Type,
                Order = source.Order,
                Content = source.Content,
                CreatedAt = DateTime.UtcNow,
                LastEditedAt = DateTime.UtcNow
            };

            _blocks[clone.Id] = clone;
        }
    }

    public async Task<IEnumerable<IPageBlock>> GetBlocksAsync(string pageId)
    {
        if (Guid.TryParse(pageId, out var id))
        {
            var blocks = _blocks.Values
                .Where(b => b.PageId == id && b.ParentBlockId == null)
                .OrderBy(b => b.Order)
                .Cast<IPageBlock>();
            return await Task.FromResult(blocks);
        }
        return await Task.FromResult(Array.Empty<IPageBlock>());
    }

    public IReadOnlyList<IPageBlock> GetAllPageBlocks(Guid pageId)
        => _blocks.Values
            .Where(block => block.PageId == pageId)
            .OrderBy(block => block.ParentBlockId)
            .ThenBy(block => block.Order)
            .ThenBy(block => block.Id)
            .Cast<IPageBlock>()
            .ToList();

    public IPageBlock? GetBlock(Guid blockId)
        => _blocks.TryGetValue(blockId, out var block) ? block : null;

    public void ApplyAggregateSnapshot(NotionPageSnapshot snapshot)
    {
        var pageIds = snapshot.Blocks.Select(block => block.Id).ToHashSet();
        var replacements = snapshot.Blocks.Select(block => new PageBlock
        {
            Id = block.Id,
            PageId = block.PageId,
            ParentBlockId = block.ParentBlockId,
            Type = block.Type,
            Order = block.Order,
            CreatedAt = block.CreatedAt,
            LastEditedAt = block.LastEditedAt,
            Content = FromAggregateContent(block)
        }).ToList();

        foreach (var id in _blocks.Values
                     .Where(block => block.PageId == snapshot.Page.Id && !pageIds.Contains(block.Id))
                     .Select(block => block.Id)
                     .ToList())
        {
            _blocks.Remove(id);
        }
        foreach (var replacement in replacements)
        {
            _blocks[replacement.Id] = replacement;
        }
    }

    private IBlockContent FromAggregateContent(NotionBlockSnapshot block)
    {
        if (block.Type == BlockType.Table)
        {
            var table = block.Content.Deserialize<NotionAuthoringTable>(
                NotionAggregateJson.Options)!;
            return new TableBlockContent
            {
                ColumnCount = table.ColumnCount,
                HasHeaderRow = table.HasHeaderRow,
                HasHeaderColumn = table.HasHeaderColumn,
                ColumnAlignments = table.ColumnAlignments.Select(alignment => alignment switch
                {
                    NotionTableHorizontalAlignment.Center =>
                        Tempo.Blazor.DocumentEditor.Models.TableColumnAlignment.Center,
                    NotionTableHorizontalAlignment.Right =>
                        Tempo.Blazor.DocumentEditor.Models.TableColumnAlignment.Right,
                    _ => Tempo.Blazor.DocumentEditor.Models.TableColumnAlignment.Left
                }).ToList(),
                ColumnWidths = table.ColumnWidths
            };
        }
        if (block.Type == BlockType.TableRow)
        {
            var row = block.Content.Deserialize<NotionAuthoringTableRow>(
                NotionAggregateJson.Options)!;
            return new TableRowBlockContent
            {
                RichCells = row.Cells.Select(cell => new NotionTableCell
                {
                    Html = cell.Html,
                    Inlines = cell.Inlines,
                    BackgroundColor = cell.BackgroundColor,
                    TextColor = cell.TextColor,
                    HorizontalAlignment = cell.HorizontalAlignment,
                    VerticalAlignment = cell.VerticalAlignment,
                    RowSpan = cell.RowSpan,
                    ColSpan = cell.ColumnSpan,
                    Width = cell.Width,
                    Borders = cell.Borders
                }).ToList()
            };
        }
        if (_blocks.TryGetValue(block.Id, out var existing))
        {
            return existing.Content;
        }

        return block.Type switch
        {
            BlockType.Heading1 or BlockType.Heading2 or BlockType.Heading3 =>
                block.Content.Deserialize<HeadingBlockContent>(NotionAggregateJson.Options)!,
            BlockType.BulletList or BlockType.NumberedList =>
                block.Content.Deserialize<ListBlockContent>(NotionAggregateJson.Options)!,
            BlockType.Code =>
                block.Content.Deserialize<CodeBlockContent>(NotionAggregateJson.Options)!,
            _ => block.Content.Deserialize<TextBlockContent>(NotionAggregateJson.Options)!
        };
    }

    public async Task<IEnumerable<IPageBlock>> GetChildBlocksAsync(string parentBlockId)
    {
        if (Guid.TryParse(parentBlockId, out var id))
        {
            var children = _blocks.Values
                .Where(b => b.ParentBlockId == id)
                .OrderBy(b => b.Order)
                .Cast<IPageBlock>();
            return await Task.FromResult(children);
        }
        return await Task.FromResult(Array.Empty<IPageBlock>());
    }

    public async Task<IPageBlock> CreateBlockAsync(string pageId, IPageBlock block, string? afterBlockId)
    {
        var pageGuid = Guid.Parse(pageId);
        var newBlock = new PageBlock
        {
            Id            = Guid.NewGuid(),
            PageId        = pageGuid,
            ParentBlockId = block.ParentBlockId,
            Type          = block.Type,
            Order         = InsertOrderAfter(pageGuid, block.ParentBlockId, afterBlockId, count: 1),
            Content       = block.Content,
            CreatedAt     = DateTime.UtcNow,
            LastEditedAt  = DateTime.UtcNow
        };

        _blocks[newBlock.Id] = newBlock;
        return await Task.FromResult(newBlock);
    }

    /// <summary>
    /// Order of the first of <paramref name="count"/> blocks inserted after <paramref name="afterBlockId"/>,
    /// shifting the following siblings out of the way. Without the shift a block split off with Enter
    /// jumps to the end of the page on the next reload, because it silently got the next free order.
    /// </summary>
    private int InsertOrderAfter(Guid pageId, Guid? parentBlockId, string? afterBlockId, int count)
    {
        if (!Guid.TryParse(afterBlockId, out var anchorId)
            || !_blocks.TryGetValue(anchorId, out var anchor)
            || anchor.PageId != pageId
            || anchor.ParentBlockId != parentBlockId)
        {
            return GetNextOrder(pageId, parentBlockId);
        }

        var insertAt = anchor.Order + 1;
        foreach (var sibling in GetSiblingBlocks(pageId, parentBlockId).Where(s => s.Order >= insertAt))
        {
            sibling.Order += count;
        }

        return insertAt;
    }

    public async Task<IEnumerable<IPageBlock>> CreateBlocksAsync(string pageId, IEnumerable<IPageBlock> blocks, string? afterBlockId)
    {
        var pageGuid = Guid.Parse(pageId);
        var created  = new List<IPageBlock>();

        // The store owns the ids, so a child that names a parent from this same batch must follow
        // it to its new id. A parent that is not in the batch already lives on the page; leave it.
        var batch = blocks.ToList();

        // Callers may leave Id unset; only real ids can be remapped.
        var idMap = new Dictionary<Guid, Guid>();
        var newIds = new Guid[batch.Count];
        for (var i = 0; i < batch.Count; i++)
        {
            newIds[i] = Guid.NewGuid();
            if (batch[i].Id != Guid.Empty) idMap[batch[i].Id] = newIds[i];
        }

        for (var i = 0; i < batch.Count; i++)
        {
            var block = batch[i];
            var parentId = block.ParentBlockId is { } parent && idMap.TryGetValue(parent, out var mapped)
                ? mapped
                : block.ParentBlockId;

            var newBlock = new PageBlock
            {
                Id            = newIds[i],
                PageId        = pageGuid,
                ParentBlockId = parentId,
                Type          = block.Type,
                Order         = 0, // assigned below, once the whole batch is known
                Content       = block.Content,
                CreatedAt     = DateTime.UtcNow,
                LastEditedAt  = DateTime.UtcNow
            };

            _blocks[newBlock.Id] = newBlock;
            created.Add(newBlock);
        }

        // Batch-inserted siblings keep their relative order behind the anchor. Children are ordered
        // within their own parent, which was created in this same batch.
        AssignBatchOrder(pageGuid, created, afterBlockId);

        return await Task.FromResult(created);
    }

    private void AssignBatchOrder(Guid pageId, List<IPageBlock> created, string? afterBlockId)
    {
        foreach (var group in created.Cast<PageBlock>().GroupBy(block => block.ParentBlockId))
        {
            var siblings = group.ToList();
            var isTopLevel = group.Key is null;
            var start = isTopLevel
                ? InsertOrderAfterExcluding(pageId, null, afterBlockId, siblings)
                : GetNextOrderExcluding(pageId, group.Key, siblings);

            for (var i = 0; i < siblings.Count; i++) siblings[i].Order = start + i;
        }
    }

    private int InsertOrderAfterExcluding(Guid pageId, Guid? parentBlockId, string? afterBlockId, List<PageBlock> exclude)
    {
        if (!Guid.TryParse(afterBlockId, out var anchorId)
            || !_blocks.TryGetValue(anchorId, out var anchor)
            || anchor.PageId != pageId
            || anchor.ParentBlockId != parentBlockId)
        {
            return GetNextOrderExcluding(pageId, parentBlockId, exclude);
        }

        var insertAt = anchor.Order + 1;
        foreach (var sibling in GetSiblingBlocks(pageId, parentBlockId)
                     .Where(s => s.Order >= insertAt && !exclude.Contains(s)))
        {
            sibling.Order += exclude.Count;
        }

        return insertAt;
    }

    private int GetNextOrderExcluding(Guid pageId, Guid? parentBlockId, List<PageBlock> exclude)
    {
        var max = _blocks.Values
            .Where(b => b.PageId == pageId && b.ParentBlockId == parentBlockId && !exclude.Contains(b))
            .Max(b => (int?)b.Order) ?? -1;
        return max + 1;
    }

    public Task<IReadOnlyList<PageBlock>> CreateImportedBlocksAsync(
        string pageId,
        IEnumerable<IPageBlock> blocks,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pageGuid = Guid.Parse(pageId);
        var imported = new List<PageBlock>();
        var nextOrder = GetNextOrder(pageGuid, null);

        foreach (var source in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id;
            while (_blocks.ContainsKey(id))
                id = Guid.NewGuid();

            var block = new PageBlock
            {
                Id = id,
                PageId = pageGuid,
                ParentBlockId = source.ParentBlockId,
                Type = source.Type,
                Order = source.ParentBlockId is null ? nextOrder++ : source.Order,
                Content = source.Content,
                CreatedAt = DateTime.UtcNow,
                LastEditedAt = DateTime.UtcNow
            };

            _blocks[block.Id] = block;
            imported.Add(block);
        }

        return Task.FromResult<IReadOnlyList<PageBlock>>(imported);
    }

    public async Task UpdateBlockAsync(IPageBlock block)
    {
        if (block is PageBlock pageBlock && _blocks.ContainsKey(pageBlock.Id))
        {
            pageBlock.LastEditedAt = DateTime.UtcNow;
            _blocks[pageBlock.Id] = pageBlock;
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Writes blocks back with the ids, parents and order they had. Undo of a delete uses this:
    /// recreating them would mint new ids and the restored container would no longer own its rows.
    /// </summary>
    public async Task RestoreBlocksAsync(IEnumerable<IPageBlock> blocks)
    {
        foreach (var block in blocks)
        {
            _blocks[block.Id] = new PageBlock
            {
                Id            = block.Id,
                PageId        = block.PageId,
                ParentBlockId = block.ParentBlockId,
                Type          = block.Type,
                Order         = block.Order,
                Content       = block.Content,
                CreatedAt     = block.CreatedAt,
                LastEditedAt  = DateTime.UtcNow
            };
        }

        await Task.CompletedTask;
    }

    public async Task DeleteBlockAsync(string blockId)
    {
        if (!Guid.TryParse(blockId, out var id) || !_blocks.TryGetValue(id, out var block))
        {
            await Task.CompletedTask;
            return;
        }

        // Delete the whole subtree. A surviving TableRow, Column or toggle child would have no
        // parent to render it and would surface on the page as a stray block.
        foreach (var descendant in DescendantsOf(id)) _blocks.Remove(descendant.Id);
        _blocks.Remove(id);

        // The gap the block leaves in the sibling order is deliberate: everything sorts by Order, and
        // undo restores the block at exactly the position it had. Renumbering would collide with it.
        await Task.CompletedTask;
    }

    /// <summary>Every block below <paramref name="rootId"/>, deepest last.</summary>
    private List<PageBlock> DescendantsOf(Guid rootId)
    {
        var result = new List<PageBlock>();
        var frontier = new Queue<Guid>();
        frontier.Enqueue(rootId);

        while (frontier.Count > 0)
        {
            var parentId = frontier.Dequeue();
            foreach (var child in _blocks.Values.Where(block => block.ParentBlockId == parentId))
            {
                result.Add(child);
                frontier.Enqueue(child.Id);
            }
        }

        return result;
    }

    public void Reset()
    {
        _blocks.Clear();
        InitializeMockBlocks();
    }

    public IReadOnlyList<PageBlock> GetAllBlocksSnapshot()
        => _blocks.Values
            .OrderBy(block => block.PageId)
            .ThenBy(block => block.ParentBlockId.HasValue)
            .ThenBy(block => block.Order)
            .ToArray();

    public IReadOnlyList<PageBlock> GetSyncedChildBlocks(Guid syncId)
    {
        var originIds = _blocks.Values
            .Where(block => block.Content is SyncedBlockOriginContent origin && origin.SyncId == syncId)
            .Select(block => block.Id)
            .ToHashSet();

        return _blocks.Values
            .Where(block => block.ParentBlockId.HasValue && originIds.Contains(block.ParentBlockId.Value))
            .OrderBy(block => block.Order)
            .ToArray();
    }

    public IReadOnlyList<SyncedBlockRefLocation> GetSyncedRefs(Guid syncId)
        => _blocks.Values
            .Where(block => block.Content is SyncedBlockRefContent reference && reference.SyncId == syncId)
            .OrderBy(block => block.PageId)
            .ThenBy(block => block.Order)
            .Select(block => new SyncedBlockRefLocation(block.PageId.ToString("D"), block.Id.ToString("D")))
            .ToArray();

    public void UpdateSyncedChildBlocks(Guid syncId, IEnumerable<PageBlock> children)
    {
        var origin = _blocks.Values.FirstOrDefault(block => block.Content is SyncedBlockOriginContent content && content.SyncId == syncId);
        if (origin is null)
            throw new KeyNotFoundException($"Synced origin {syncId:D} not found.");

        foreach (var id in _blocks.Values.Where(block => block.ParentBlockId == origin.Id).Select(block => block.Id).ToArray())
            _blocks.Remove(id);

        var order = 0;
        foreach (var child in children)
        {
            var id = child.Id == Guid.Empty ? Guid.NewGuid() : child.Id;
            while (_blocks.ContainsKey(id))
                id = Guid.NewGuid();

            _blocks[id] = new PageBlock
            {
                Id = id,
                PageId = origin.PageId,
                ParentBlockId = origin.Id,
                Type = child.Type,
                Order = order++,
                Content = child.Content,
                CreatedAt = child.CreatedAt == default ? DateTime.UtcNow : child.CreatedAt,
                LastEditedAt = DateTime.UtcNow
            };
        }
    }

    public PageBlock CreateSyncedRef(Guid syncId, Guid targetPageId, Guid? afterBlockId)
    {
        var origin = _blocks.Values.FirstOrDefault(block => block.Content is SyncedBlockOriginContent content && content.SyncId == syncId);
        if (origin is null)
            throw new KeyNotFoundException($"Synced origin {syncId:D} not found.");

        var order = afterBlockId.HasValue && _blocks.TryGetValue(afterBlockId.Value, out var after)
            ? after.Order + 1
            : GetNextOrder(targetPageId, null);

        var block = MakeBlock(Guid.NewGuid(), targetPageId, null, BlockType.SyncedBlockRef, order, new SyncedBlockRefContent
        {
            SyncId = syncId,
            OriginPageId = origin.PageId,
            OriginBlockId = origin.Id
        });
        _blocks[block.Id] = block;
        return block;
    }

    public PageBlock UnsyncSyncedRef(Guid blockId)
    {
        if (!_blocks.TryGetValue(blockId, out var source) || source.Content is not SyncedBlockRefContent reference)
            throw new KeyNotFoundException($"Synced ref {blockId:D} not found.");

        var firstChild = GetSyncedChildBlocks(reference.SyncId).FirstOrDefault();
        var replacement = MakeBlock(
            source.Id,
            source.PageId,
            source.ParentBlockId,
            firstChild?.Type ?? BlockType.Paragraph,
            source.Order,
            firstChild?.Content ?? new TextBlockContent());
        _blocks[source.Id] = replacement;
        return replacement;
    }

    public void ReplacePageBlocks(Guid pageId, IEnumerable<PageBlock> blocks)
    {
        RemoveBlocksForPages(pageId);

        foreach (var source in blocks.OrderBy(block => block.ParentBlockId.HasValue).ThenBy(block => block.Order))
        {
            var id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id;
            while (_blocks.ContainsKey(id))
                id = Guid.NewGuid();

            _blocks[id] = new PageBlock
            {
                Id = id,
                PageId = pageId,
                ParentBlockId = source.ParentBlockId,
                Type = source.Type,
                Order = source.Order,
                Content = source.Content,
                CreatedAt = source.CreatedAt,
                LastEditedAt = DateTime.UtcNow
            };
        }
    }

    public async Task ReorderBlocksAsync(string pageId, IEnumerable<string> orderedBlockIds)
    {
        var order = 0;
        foreach (var blockId in orderedBlockIds)
        {
            if (Guid.TryParse(blockId, out var id) && _blocks.TryGetValue(id, out var block))
            {
                block.Order = order++;
                _blocks[id] = block;
            }
        }
        await Task.CompletedTask;
    }

    public async Task MoveBlockAsync(MoveNotionBlockRequest request)
    {
        if (!Guid.TryParse(request.BlockId, out var blockId) || !_blocks.TryGetValue(blockId, out var block))
            throw new KeyNotFoundException($"Block {request.BlockId} not found.");

        if (!Guid.TryParse(request.TargetPageId, out var targetPageId))
            throw new ArgumentException("TargetPageId must be a valid GUID.", nameof(request));

        var sourceParentId = TryParseOptionalGuid(request.SourceParentBlockId);
        var targetParentId = TryParseOptionalGuid(request.TargetParentBlockId);

        if (targetParentId == blockId || IsDescendantOf(targetParentId, blockId))
            throw new InvalidOperationException("A block cannot be moved into itself or one of its descendants.");

        if (targetParentId.HasValue && !_blocks.ContainsKey(targetParentId.Value))
            throw new KeyNotFoundException($"Target parent block {targetParentId.Value:D} not found.");

        var sourcePageId = block.PageId;
        var sourceParentFromStore = block.ParentBlockId;
        var sourceChanged = sourcePageId != targetPageId || sourceParentFromStore != targetParentId;

        var targetSiblings = GetSiblingBlocks(targetPageId, targetParentId)
            .Where(candidate => candidate.Id != blockId)
            .ToList();
        var targetIndex = Math.Clamp(request.TargetIndex, 0, targetSiblings.Count);

        var descendants = DescendantsOf(blockId);

        block.PageId = targetPageId;
        block.ParentBlockId = targetParentId;
        block.Order = targetIndex;
        block.LastEditedAt = DateTime.UtcNow;

        // The subtree travels with the block; a child left on the old page is unreachable.
        foreach (var descendant in descendants) descendant.PageId = targetPageId;

        targetSiblings.Insert(targetIndex, block);
        RenumberSiblings(targetSiblings);

        // The caller's SourceParentBlockId is a hint, not a fact: renumber the parent the block
        // actually came from.
        if (sourceChanged)
            RenumberSiblings(GetSiblingBlocks(sourcePageId, sourceParentFromStore).Where(candidate => candidate.Id != blockId));

        await Task.CompletedTask;
    }

    public async Task MoveBlockToPageAsync(string blockId, string targetPageId, string? afterBlockId)
    {
        if (!Guid.TryParse(targetPageId, out var pid))
            throw new ArgumentException("Target page id must be a valid GUID.", nameof(targetPageId));

        var targetIndex = GetSiblingBlocks(pid, null).Count;
        if (Guid.TryParse(afterBlockId, out var afterId))
        {
            var ordered = GetSiblingBlocks(pid, null);
            var afterIndex = ordered.FindIndex(block => block.Id == afterId);
            if (afterIndex >= 0)
                targetIndex = afterIndex + 1;
        }

        await MoveBlockAsync(new MoveNotionBlockRequest(blockId, targetPageId, null, null, targetIndex));
    }

    public async Task<IPageBlock> DuplicateBlockAsync(string blockId)
    {
        if (!Guid.TryParse(blockId, out var id) || !_blocks.TryGetValue(id, out var src))
            throw new KeyNotFoundException($"Block {blockId} not found");

        // Deep copy: a shared Content instance would make editing the copy edit the original, and a
        // table duplicated without its rows renders empty.
        var idMap = new Dictionary<Guid, Guid> { [src.Id] = Guid.NewGuid() };
        var subtree = DescendantsOf(src.Id);
        foreach (var descendant in subtree) idMap[descendant.Id] = Guid.NewGuid();

        var dup = new PageBlock
        {
            Id            = idMap[src.Id],
            PageId        = src.PageId,
            ParentBlockId = src.ParentBlockId,
            Type          = src.Type,
            Order         = InsertOrderAfter(src.PageId, src.ParentBlockId, src.Id.ToString(), count: 1),
            Content       = CloneContent(src.Content),
            CreatedAt     = DateTime.UtcNow,
            LastEditedAt  = DateTime.UtcNow
        };
        _blocks[dup.Id] = dup;

        foreach (var descendant in subtree)
        {
            var copy = new PageBlock
            {
                Id            = idMap[descendant.Id],
                PageId        = descendant.PageId,
                ParentBlockId = idMap[descendant.ParentBlockId!.Value],
                Type          = descendant.Type,
                Order         = descendant.Order,
                Content       = CloneContent(descendant.Content),
                CreatedAt     = DateTime.UtcNow,
                LastEditedAt  = DateTime.UtcNow
            };
            _blocks[copy.Id] = copy;
        }

        return await Task.FromResult(dup);
    }

    /// <summary>Round-trips the content through JSON; IBlockContent is polymorphic, so the concrete type survives.</summary>
    private static IBlockContent CloneContent(IBlockContent content) =>
        System.Text.Json.JsonSerializer.Deserialize<IBlockContent>(
            System.Text.Json.JsonSerializer.Serialize(content))!;

    public Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType)
        => ConvertBlockTypeAsync(blockId, newType, currentHtml: null);

    /// <summary>
    /// Converts a block. When <paramref name="currentHtml"/> is supplied the editor's live
    /// contenteditable value wins over the stored content, so text typed since the last blur survives.
    /// </summary>
    public async Task<IPageBlock> ConvertBlockTypeAsync(string blockId, BlockType newType, string? currentHtml)
    {
        if (!Guid.TryParse(blockId, out var id) || !_blocks.TryGetValue(id, out var block))
        {
            throw new KeyNotFoundException($"Block {blockId} not found");
        }

        var previousType = block.Type;
        var previousContent = block.Content;

        // Remember the typed data so a later conversion back to this type can restore it.
        _conversionMemory[(id, previousType)] = previousContent;

        CascadeChildren(block, previousType, newType);

        block.Type          = newType;
        block.Content       = CreateConvertedContent(newType, previousContent, currentHtml);
        block.LastEditedAt  = DateTime.UtcNow;

        if (_conversionMemory.TryGetValue((id, newType), out var remembered))
        {
            RestoreTypedFields(block.Content, remembered);
        }

        _blocks[id] = block;

        if (newType == BlockType.Table)
        {
            EnsureTableRows(block, TextOf(previousContent, currentHtml));
        }

        return await Task.FromResult<IPageBlock>(block);
    }

    /// <summary>Remembers each block's content per source type so reverse conversions can restore typed data.</summary>
    private readonly Dictionary<(Guid BlockId, BlockType Type), IBlockContent> _conversionMemory = [];

    /// <summary>
    /// A block that stops being a container must not leave its children dangling: table rows are
    /// meaningless without their table and are deleted, everything else moves up to the block's own parent.
    /// </summary>
    private void CascadeChildren(PageBlock block, BlockType previousType, BlockType newType)
    {
        if (previousType == newType)
        {
            return;
        }

        var children = _blocks.Values
            .Where(candidate => candidate.ParentBlockId == block.Id)
            .OrderBy(candidate => candidate.Order)
            .ToList();

        if (children.Count == 0)
        {
            return;
        }

        if (previousType == BlockType.Table)
        {
            foreach (var row in children.Where(child => child.Type == BlockType.TableRow))
            {
                _blocks.Remove(row.Id);
            }

            children = children.Where(child => child.Type != BlockType.TableRow).ToList();
        }

        if (CanHoldChildren(newType) || children.Count == 0)
        {
            return;
        }

        // Re-parent onto the block's own parent, appended after the existing siblings.
        var nextOrder = _blocks.Values
            .Where(candidate => candidate.ParentBlockId == block.ParentBlockId && candidate.Id != block.Id)
            .Select(candidate => candidate.Order)
            .DefaultIfEmpty(block.Order)
            .Max() + 1;

        foreach (var child in children)
        {
            child.ParentBlockId = block.ParentBlockId;
            child.Order = nextOrder++;
            _blocks[child.Id] = child;
        }
    }

    private static bool CanHoldChildren(BlockType type) => type
        is BlockType.Toggle
        or BlockType.Callout
        or BlockType.Table
        or BlockType.ColumnList
        or BlockType.Column
        or BlockType.SyncedBlockOrigin
        or BlockType.SyncedBlockRef
        or BlockType.Quote;

    /// <summary>Creates the two default rows only when the table has none, so repeated conversions stay idempotent.</summary>
    private void EnsureTableRows(PageBlock block, string headerText)
    {
        if (block.Content is not TableBlockContent table)
        {
            return;
        }

        if (table.ColumnCount <= 0)
        {
            table.ColumnCount = 3;
        }

        var existingRows = _blocks.Values.Count(candidate =>
            candidate.ParentBlockId == block.Id && candidate.Type == BlockType.TableRow);

        if (existingRows > 0)
        {
            return;
        }

        for (var rowIndex = 0; rowIndex < 2; rowIndex++)
        {
            var cells = Enumerable.Range(0, table.ColumnCount).Select(_ => string.Empty).ToList();
            if (rowIndex == 0 && !string.IsNullOrEmpty(headerText))
            {
                cells[0] = headerText;
            }

            var rowId = Guid.NewGuid();
            _blocks[rowId] = new PageBlock
            {
                Id            = rowId,
                PageId        = block.PageId,
                ParentBlockId = block.Id,
                Type          = BlockType.TableRow,
                Order         = rowIndex,
                Content       = new TableRowBlockContent { RichCells = ToRichCells(cells) },
                CreatedAt     = DateTime.UtcNow,
                LastEditedAt  = DateTime.UtcNow
            };
        }
    }

    public async Task<string> GetBlockLinkAsync(string blockId)
    {
        return await Task.FromResult($"https://notion.demo/block/{blockId}");
    }

    private static IReadOnlyList<NotionTableCell> ToRichCells(params string[] cells)
        => ToRichCells((IEnumerable<string>)cells);

    private static IReadOnlyList<NotionTableCell> ToRichCells(
        IEnumerable<string> cells)
        => cells.Select(cell => new NotionTableCell { Html = cell }).ToList();

    public Task SetTodoCompletedAsync(string taskId, bool completed, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParse(taskId, out var id) || !_blocks.TryGetValue(id, out var block))
            throw new KeyNotFoundException($"Task block {taskId} not found");

        if (block.Content is not TodoBlockContent todo)
            throw new InvalidOperationException($"Block {taskId} is not a todo block.");

        todo.IsChecked = completed;
        block.LastEditedAt = DateTime.UtcNow;
        _blocks[id] = block;
        return Task.CompletedTask;
    }

    public void SeedE2EPageSettingsPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb120000-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 0, new TextBlockContent
        {
            Html = "This editable block is used to verify that locking the page switches content into read-only mode."
        });
        AddTo(Guid.Parse("eb120000-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Heading2, 1, new HeadingBlockContent
        {
            Level = 2,
            Html = "Settings states"
        });
        AddTo(Guid.Parse("eb120000-0000-0000-0000-000000000004"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 2, new TextBlockContent
        {
            Html = "Full width, small text and locked state should preserve a readable page header and clear menu feedback."
        });
    }

    public void SeedE2EHistoryPage()
    {
        RemoveBlocksForPages(MockNotionDataStore.Page1Id);

        AddTo(Guid.Parse("eb130100-0000-0000-0000-000000000001"), MockNotionDataStore.Page1Id, BlockType.Heading1, 0, new HeadingBlockContent
        {
            Level = 1,
            Html = "EB13 Version History"
        });
        AddTo(Guid.Parse("eb130100-0000-0000-0000-000000000002"), MockNotionDataStore.Page1Id, BlockType.Paragraph, 1, new TextBlockContent
        {
            Html = "This page verifies history timelines, previews, scrolling version lists, and restore flows through the HTTPS Demo API."
        });
        AddTo(Guid.Parse("eb130100-0000-0000-0000-000000000003"), MockNotionDataStore.Page1Id, BlockType.Callout, 2, new CalloutBlockContent
        {
            IconEmoji = "i",
            Html = "Select an older version from Page history to preview and restore deterministic content.",
            BackgroundColor = "blue"
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private int GetNextOrder(Guid pageId, Guid? parentBlockId)
    {
        var max = _blocks.Values
            .Where(b => b.PageId == pageId && b.ParentBlockId == parentBlockId)
            .Max(b => (int?)b.Order) ?? -1;
        return max + 1;
    }

    private void RemoveBlocksForPages(params Guid[] pageIds)
    {
        var set = pageIds.ToHashSet();
        foreach (var id in _blocks.Values.Where(block => set.Contains(block.PageId)).Select(block => block.Id).ToArray())
            _blocks.Remove(id);
    }

    private static Guid? TryParseOptionalGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : null;

    private List<PageBlock> GetSiblingBlocks(Guid pageId, Guid? parentBlockId)
        => [.. _blocks.Values
            .Where(block => block.PageId == pageId && block.ParentBlockId == parentBlockId)
            .OrderBy(block => block.Order)];

    private void RenumberSiblings(IEnumerable<PageBlock> siblings)
    {
        var order = 0;
        foreach (var sibling in siblings.OrderBy(block => block.Order).ToList())
        {
            sibling.Order = order++;
            sibling.LastEditedAt = DateTime.UtcNow;
            _blocks[sibling.Id] = sibling;
        }
    }

    private bool IsDescendantOf(Guid? candidateParentId, Guid ancestorId)
    {
        var current = candidateParentId;
        while (current.HasValue)
        {
            if (current.Value == ancestorId)
                return true;

            current = _blocks.TryGetValue(current.Value, out var parent)
                ? parent.ParentBlockId
                : null;
        }

        return false;
    }

    private void AddColumnList(Guid columnListId, int order, IReadOnlyList<(string Text, double Width)> columns)
    {
        AddTo(columnListId, MockNotionDataStore.Page1Id, BlockType.ColumnList, order, new ColumnListBlockContent
        {
            ColumnCount = columns.Count
        });

        for (var i = 0; i < columns.Count; i++)
        {
            var idPrefix = columnListId.ToString("D")[..24];
            var suffixBase = Convert.ToInt64(columnListId.ToString("N")[20..], 16);
            var columnId = Guid.Parse($"{idPrefix}{suffixBase + i + 1:x12}");
            AddChildTo(columnId, MockNotionDataStore.Page1Id, columnListId, BlockType.Column, i, new ColumnBlockContent
            {
                ColumnIndex = i,
                WidthPercent = columns[i].Width
            });
            var paragraphId = Guid.Parse($"{idPrefix}{suffixBase + i + 101:x12}");
            AddChildTo(paragraphId, MockNotionDataStore.Page1Id, columnId, BlockType.Paragraph, 0, new TextBlockContent
            {
                Html = columns[i].Text
            });
        }
    }

    private static IBlockContent CreateDefaultContent(BlockType type) => type switch
    {
        BlockType.Heading1                                          => new HeadingBlockContent { Level = 1 },
        BlockType.Heading2                                          => new HeadingBlockContent { Level = 2 },
        BlockType.Heading3                                          => new HeadingBlockContent { Level = 3 },
        BlockType.Paragraph                                         => new TextBlockContent(),
        BlockType.BulletList or BlockType.NumberedList              => new ListBlockContent(),
        BlockType.TodoItem                                          => new TodoBlockContent(),
        BlockType.Toggle                                            => new ToggleBlockContent(),
        BlockType.Divider                                           => new DividerBlockContent(),
        BlockType.Code                                              => new CodeBlockContent { Language = "Plain Text" },
        BlockType.Image                                             => new ImageBlockContent(),
        BlockType.Video                                             => new VideoBlockContent(),
        BlockType.Audio                                             => new AudioBlockContent(),
        BlockType.File                                              => new FileBlockContent(),
        BlockType.Pdf                                               => new PdfBlockContent(),
        BlockType.Equation                                          => new EquationBlockContent(),
        BlockType.Bookmark                                          => new BookmarkBlockContent(),
        BlockType.Embed                                             => new EmbedBlockContent(),
        BlockType.Table                                             => new TableBlockContent { ColumnCount = 3 },
        BlockType.ColumnList                                        => new ColumnListBlockContent { ColumnCount = 2 },
        BlockType.Column                                            => new ColumnBlockContent(),
        BlockType.Diagram                                           => new DiagramBlockContent(),
        BlockType.Wireframe                                         => new WireframeBlockContent(),
        BlockType.Spreadsheet                                       => new SpreadsheetBlockContent(),
        BlockType.WorkItem                                          => new WorkItemBlockContent(),
        BlockType.ContentByLabel                                    => new ContentByLabelBlockContent(),
        _                                                           => new TextBlockContent()
    };

    /// <summary>Reads a block's text regardless of whether it stores it as HTML or as code.</summary>
    private static string TextOf(IBlockContent source, string? currentHtml = null) => currentHtml ?? source switch
    {
        ITextBlockContent text => text.Html,
        ICodeBlockContent code => code.Code,
        _ => string.Empty
    };

    private static IBlockContent CreateConvertedContent(BlockType type, IBlockContent source, string? currentHtml = null)
    {
        var html = TextOf(source, currentHtml);
        var backgroundColor = source is ITextBlockContent bg ? bg.BackgroundColor : null;
        var textColor = source is ITextBlockContent color ? color.TextColor : null;
        var alignment = source is ITextBlockContent aligned ? aligned.Alignment : Tempo.Blazor.NotionEditor.Enums.TextAlignment.Left;

        return type switch
        {
            BlockType.Heading1 => new HeadingBlockContent { Level = 1, Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.Heading2 => new HeadingBlockContent { Level = 2, Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.Heading3 => new HeadingBlockContent { Level = 3, Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.Paragraph => new TextBlockContent { Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.BulletList or BlockType.NumberedList => new ListBlockContent { Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.TodoItem => new TodoBlockContent { Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.Toggle => new ToggleBlockContent { Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.Quote => new TextBlockContent { Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment },
            BlockType.Callout => new CalloutBlockContent { Html = html, BackgroundColor = backgroundColor, TextColor = textColor, Alignment = alignment, IconEmoji = DefaultCalloutIcon },
            BlockType.Code => new CodeBlockContent { Code = html },
            _ => CreateDefaultContent(type)
        };
    }

    private const string DefaultCalloutIcon = "💡";

    /// <summary>
    /// Copies data that only the target type can hold from the block's last content of that type.
    /// The text always comes from the conversion itself — only the typed extras are restored.
    /// </summary>
    private static void RestoreTypedFields(IBlockContent target, IBlockContent remembered)
    {
        switch (target)
        {
            case TodoBlockContent todo when remembered is ITodoBlockContent previousTodo:
                todo.IsChecked = previousTodo.IsChecked;
                todo.AssigneeId = previousTodo.AssigneeId;
                todo.AssigneeDisplayName = previousTodo.AssigneeDisplayName;
                todo.DueDate = previousTodo.DueDate;
                break;

            case CalloutBlockContent callout when remembered is ICalloutBlockContent previousCallout:
                callout.IconEmoji = previousCallout.IconEmoji ?? DefaultCalloutIcon;
                callout.IconImageUrl = previousCallout.IconImageUrl;
                callout.Variant = previousCallout.Variant;
                break;

            case CodeBlockContent code when remembered is ICodeBlockContent previousCode:
                code.Language = previousCode.Language;
                code.ShowLineNumbers = previousCode.ShowLineNumbers;
                code.WrapLines = previousCode.WrapLines;
                code.Caption = previousCode.Caption;
                break;

            case ToggleBlockContent toggle when remembered is IToggleBlockContent previousToggle:
                toggle.IsOpen = previousToggle.IsOpen;
                break;

            case TableBlockContent table when remembered is ITableBlockContent previousTable:
                table.ColumnCount = previousTable.ColumnCount;
                table.HasHeaderRow = previousTable.HasHeaderRow;
                table.HasHeaderColumn = previousTable.HasHeaderColumn;
                table.ColumnAlignments = previousTable.ColumnAlignments;
                break;
        }
    }
}
