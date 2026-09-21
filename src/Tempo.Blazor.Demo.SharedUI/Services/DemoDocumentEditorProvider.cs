using System.Globalization;
using System.Net.Http.Json;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;

namespace Tempo.Blazor.Demo.Services;

/// <summary>Demo provider for <c>TmDocumentEditor</c>.</summary>
public class DemoDocumentEditorProvider : InMemoryDocumentEditorProvider
{
    /// <summary>Stable document id used by the canvas render pipeline E2E gate.</summary>
    public const string CanvasRenderDocumentId = "phase-5-canvas-render";

    /// <summary>Document id of the document-assembly demo template (conditions, repeating items, computed total).</summary>
    public const string AssemblyContractDocumentId = "assembly-contract-demo";

    /// <summary>Stable document id for the large (1000-paragraph) performance-budget E2E gate.</summary>
    public const string LargePerfDocumentId = "large-perf-1000";

    /// <summary>Stable document id used by the canvas text layout and pagination E2E gate.</summary>
    public const string CanvasTextLayoutDocumentId = "phase-6-canvas-text-layout";

    /// <summary>Stable document id used by the canvas caret and selection E2E gate.</summary>
    public const string CanvasCaretSelectionDocumentId = "phase-7-canvas-caret-selection";

    /// <summary>Stable document id used by the canvas typing and IME E2E gate.</summary>
    public const string CanvasTypingDocumentId = "phase-8-canvas-typing-ime";

    /// <summary>Stable document id used by the canvas collaboration and offline E2E gate.</summary>
    public const string CanvasCollaborationOfflineDocumentId = "phase-20-canvas-collaboration-offline";

    /// <summary>Stable document id used by the canvas inline formatting E2E gate.</summary>
    public const string CanvasInlineFormatDocumentId = "phase-9-canvas-inline-format";

    /// <summary>Stable document id used by the canvas paragraph and ruler E2E gate.</summary>
    public const string CanvasParagraphDocumentId = "phase-10-canvas-paragraph";

    /// <summary>Stable document id used by the canvas tab stops and interactive ruler E2E gate.</summary>
    public const string CanvasTabStopsRulerDocumentId = "phase-e2-canvas-tabstops-ruler";

    /// <summary>Stable document id used by the canvas clipboard E2E gate.</summary>
    public const string CanvasClipboardDocumentId = "phase-11-canvas-clipboard";

    /// <summary>Stable document id used by the canvas history, save, and autosave E2E gate.</summary>
    public const string CanvasHistorySaveDocumentId = "phase-12-canvas-history-save";

    /// <summary>Stable document id used by the canvas toolbar, context menu, and spellcheck E2E gate.</summary>
    public const string CanvasToolbarSpellcheckDocumentId = "phase-13-canvas-toolbar-spellcheck";

    /// <summary>Stable document id used by the canvas table layout and editing E2E gate.</summary>
    public const string CanvasTablesDocumentId = "phase-14-canvas-tables";

    /// <summary>Stable document id used by the Czech LanguageTool proofing E2E gate.</summary>
    public const string ProofingCzechDocumentId = "phase-7-proofing-czech";

    /// <summary>Stable document id used by the role-permissions and external-comment E2E gate.</summary>
    public const string RoleCommentsDocumentId = "phase-8-canvas-role-comments";

    /// <summary>Stable document id used by the legal-filing (line numbering + č.l. header) E2E gate.</summary>
    public const string LegalFilingDocumentId = "phase-9-canvas-legal-filing";

    /// <summary>Stable document id used by the redaction (real content removal) E2E gate.</summary>
    public const string RedactionDocumentId = "phase-10-canvas-redaction";

    /// <summary>Stable document id used by the canvas image and drawing object E2E gate.</summary>
    public const string CanvasImagesDocumentId = "phase-15-canvas-images";

    /// <summary>Stable document id used by the canvas header, footer, fields, notes, and page setup E2E gate.</summary>
    public const string CanvasHeadersFootersNotesDocumentId = "phase-16-canvas-headers-footers-notes";

    /// <summary>Stable document id used by the canvas comments, revisions, and restricted editing E2E gate.</summary>
    public const string CanvasCommentsRevisionsDocumentId = "phase-17-canvas-comments-revisions";

    /// <summary>Stable document id used by the canvas numbering and list style E2E gate.</summary>
    public const string CanvasNumberingListsDocumentId = "phase-e1-canvas-numbering-lists";

    /// <summary>Stable document id used by the canvas section, columns, and line numbering E2E gate.</summary>
    public const string CanvasSectionsColumnsDocumentId = "phase-e3-canvas-sections-columns";

    /// <summary>Stable document id used by the canvas document styles E2E gate.</summary>
    public const string CanvasStylesDocumentId = "phase-e4-canvas-styles";

    /// <summary>Stable document id used by the canvas fields and cross-reference E2E gate.</summary>
    public const string CanvasFieldsDocumentId = "phase-e5-canvas-fields";

    /// <summary>Stable document id used by the canvas advanced character formatting E2E gate.</summary>
    public const string CanvasAdvancedCharacterDocumentId = "phase-e6-canvas-advanced-char";

    /// <summary>Stable document id used by the canvas shapes, text boxes, lines, connectors, and charts E2E gate.</summary>
    public const string CanvasShapesDrawingsDocumentId = "phase-e7-canvas-shapes-drawings";

    /// <summary>Stable document id used by the canvas math equation E2E gate.</summary>
    public const string CanvasMathEquationsDocumentId = "phase-e8-canvas-math-equations";

    /// <summary>Stable document id used by the canvas content controls and forms E2E gate.</summary>
    public const string CanvasContentControlsDocumentId = "phase-e9-canvas-content-controls";

    /// <summary>Stable document id used by the canvas autocorrect, autoformat, format painter, and symbols E2E gate.</summary>
    public const string CanvasAutocorrectFormatPainterDocumentId = "phase-e10-canvas-autocorrect-formatpainter";

    /// <summary>Stable document id used by the canvas view modes, zoom, and print preview E2E gate.</summary>
    public const string CanvasViewModesPrintDocumentId = "phase-e11-canvas-viewmodes-print";

    /// <summary>Stable document id used by the canvas hyphenation, page background, and advanced table E2E gate.</summary>
    public const string CanvasHyphenationAdvancedTablesDocumentId = "phase-e12-canvas-hyphenation-advanced-tables";

    /// <summary>Stable document id used by the canvas search, outline, bookmarks, and TOC E2E gate.</summary>
    public const string CanvasSearchOutlineTocDocumentId = InMemoryDocumentEditorProvider.CanvasSearchOutlineTocDocumentId;

    /// <summary>Stable document id used by the canvas performance and large document E2E gate.</summary>
    public const string CanvasPerformanceDocumentId = "phase-22-canvas-performance";

    private const string DemoImageUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAUAAAAC0CAYAAADl5PURAAACsUlEQVR42u3ZzQ2CQBCAUYuwFLuxDjuwBDqgAK9UZfwB9ECC04JGdJd5hzeZ80C+y252+9MMkNHGEQABBBBAAAEEEEAAAQQQQAABBBBAAAEEEEAAAQQQQAABBBBAAAEEEEAAAQQQQAABBBBAAAEEEEAAAQQQQAABBBBAAAEEEEBAAB0CEEAAAQQQQAABBBBAAAEEEEAAAQQQQAABBBBAgAIDOD6nGSAjAQQEEEAAAQQQQAABBBBAAAEEEKDiAA4xADISQEAAAQQQQAABBBBAAAEEEEAAAQQQQAB/pWk74MsEUABBAAVQAEEABVAAQQAFUABBAAUQYBUBfMQCkJAAAgIIIIAAAggggAACCCCAAAIIUHEA+xgAGQkgIIAAAliJ8zgDhRNAAQQBFEABBAEUQAEEARRAAQQBFEABBAEUQAEEARRAAQQBFEABBAEUQAEEARRAAQQBFEBAAAUQEEABBARQAAEBFEBAAAUQEEABBARQAAEBFEBAAAUQEEABBHIE8D5Oc438XFC+0jsigIAACiAggAIICKAAAgIogIAACiAggAIICKAAAgIogIAACiAggAIIAiiAAggCKIACCAIogAIIAiiAAggCKIACCAIogAIIAiiAAggCKIACCAIogAIIAiiAAggCKICAAAogIIACCAjgGwG8xViT7fGwiLXdCag4gEIHpAig2AECKHSAAIodkDiAPh6Q9hEEQAABPg7gEAtAQgIICCCAAAIIIIAAAggggAACCCCAAAIIIIAAAgjgMpq2g2KIiQAKIAKIAAogAogACiACiAAKIAJIgQG8xgDISAABAQQQQAABBBBAAAEEEEAAAQQQQAABBBBAAAEEEEAAAQQQQIC/BrCPBSAhAQQEEEAAAQQQQAABBBBAAAEEEKDiAF5iAGQkgIAAAgggQBIvAt6vRwtbqO0AAAAASUVORK5CYII=";
    private const string ContractAssetId = DemoDocumentImageUrlResolver.ContractAssetId;
    private static readonly DateTimeOffset CanonicalDemoTimestamp = new(2026, 5, 22, 6, 0, 0, TimeSpan.Zero);
    private readonly HttpClient? _http;

    /// <summary>Creates the demo provider with sample legal documents.</summary>
    public DemoDocumentEditorProvider(IHttpClientFactory? factory = null)
    {
        _http = factory?.CreateClient("DemoApi");
        var contract = SeedContractDocument("contract-demo");
        var filing = SeedFilingDocument("filing-demo");
        var exhibits = CreateExhibitsDocument("exhibits-demo");
        var table = CreateTablePropertiesDocument("table-demo");
        var recovery = SeedRecoveryDocument();
        var onlyOfficeParity = SeedOnlyOfficeParityDocument();
        var canvasSearchOutlineToc = SeedCanvasSearchOutlineTocDocument();
        var largePerf = SeedLargePerfDocument();

        PrepareContractDemo(contract);

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = contract.DocumentId,
            Document = contract,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = filing.DocumentId,
            Document = filing,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = exhibits.DocumentId,
            Document = exhibits,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = table.DocumentId,
            Document = table,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = recovery.DocumentId,
            Document = recovery,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = onlyOfficeParity.DocumentId,
            Document = onlyOfficeParity,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = canvasSearchOutlineToc.DocumentId,
            Document = canvasSearchOutlineToc,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        _ = base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = largePerf.DocumentId,
            Document = largePerf,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }).GetAwaiter().GetResult();

        StoreVersion(CreateCanonicalContractVersion(contract));
    }

    /// <summary>Forces demo saves to return a recoverable provider error.</summary>
    public bool FailDemoSaves { get; set; }

    /// <summary>Forces demo loads to fail through the same error boundary used by unavailable providers.</summary>
    public bool FailDemoLoads { get; set; }

    /// <summary>Additional demo load latency used to keep the editor loading state observable in visual gates.</summary>
    public TimeSpan DemoLoadDelay { get; set; }

    /// <summary>
    /// Seeds the document-assembly demo template: a contract with an IF/ELSE conditional chain over
    /// contract.amount, a repeating items section, and a computed currency total.
    /// </summary>
    public DocumentEditorDocument SeedAssemblyContractDocument(string documentId = AssemblyContractDocumentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "assembly-contract-section-main";
        document.Metadata.Title = "Šablona smlouvy s logikou";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Sections[0].Id = sectionId;

        DocumentBlock Paragraph(string id, params InlineContent[] inlines) => new()
        {
            Id = id,
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Content = new ParagraphBlockContent { Inlines = inlines.ToList() },
        };

        document.Blocks.Add(new DocumentBlock
        {
            Id = "assembly-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 1,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Text = "Smlouva o dílo" }],
            },
        });
        document.Blocks.Add(Paragraph(
            "assembly-client",
            new TextRun { Text = "Objednatel: " },
            new TokenRun { Key = "contract.client", DisplayName = "Objednatel" }));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "assembly-if",
            SectionId = sectionId,
            Type = DocumentBlockType.ContentControl,
            Order = 3,
            Content = new ContentControlBlockContent
            {
                Control = DocumentAssemblyMetadata.CreateConditionalBlock("if", "contract.amount > 10000", "assembly-approval"),
                Blocks =
                [
                    Paragraph(
                        "assembly-if-clause",
                        new TextRun { Text = "Smlouva podléhá schválení ředitele — hodnota plnění přesahuje 10 000 Kč." }),
                ],
            },
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "assembly-else",
            SectionId = sectionId,
            Type = DocumentBlockType.ContentControl,
            Order = 4,
            Content = new ContentControlBlockContent
            {
                Control = DocumentAssemblyMetadata.CreateConditionalBlock("else", null, "assembly-approval"),
                Blocks =
                [
                    Paragraph(
                        "assembly-else-clause",
                        new TextRun { Text = "Smlouvu schvaluje vedoucí oddělení v běžném režimu." }),
                ],
            },
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "assembly-items",
            SectionId = sectionId,
            Type = DocumentBlockType.ContentControl,
            Order = 5,
            Content = new ContentControlBlockContent
            {
                Control = DocumentAssemblyMetadata.CreateRepeatingSection("items"),
                Blocks =
                [
                    Paragraph(
                        "assembly-item-row",
                        new TextRun { Text = "• " },
                        new TokenRun { Key = "name", DisplayName = "Položka" },
                        new TextRun { Text = " — " },
                        new TokenRun { Key = "price", DisplayName = "Cena" },
                        new TextRun { Text = " Kč" }),
                ],
            },
        });

        document.Blocks.Add(Paragraph(
            "assembly-total",
            new TextRun { Text = "Cena celkem: " },
            new TokenRun
            {
                Key = "contract.total",
                DisplayName = "Celkem",
                Expression = "CURRENCY(SUM(items, 'price'), 'cs-CZ', 'CZK')",
            }));
        document.Blocks.Add(Paragraph(
            "assembly-due",
            new TextRun { Text = "Splatnost: " },
            new TokenRun
            {
                Key = "contract.due",
                DisplayName = "Splatnost",
                Expression = "DATEADD(TODAY(), 14)",
            }));

        for (var i = 0; i < document.Blocks.Count; i++)
        {
            document.Blocks[i].Order = i + 1;
        }

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a compact document that exercises the canvas render pipeline.</summary>
    public DocumentEditorDocument SeedCanvasRenderDocument(string documentId = CanvasRenderDocumentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-render-section-main";
        document.Metadata.Title = "Canvas render pipeline";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.2,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 72, Bottom = 72, Left = 72 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-render-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties
            {
                SpacingAfter = 12
            },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines =
                [
                    new TextRun
                    {
                        Id = "canvas-render-heading-run",
                        Text = "Canvas Render Pipeline"
                    }
                ]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-render-marks",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            ParagraphProperties = new DocumentParagraphProperties
            {
                LineSpacing = 1.25,
                SpacingAfter = 10
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "canvas-render-bold",
                        Text = "Bold ",
                        Marks = [new InlineMark { Type = InlineMarkType.Bold }]
                    },
                    new TextRun
                    {
                        Id = "canvas-render-italic",
                        Text = "italic ",
                        Marks = [new InlineMark { Type = InlineMarkType.Italic }]
                    },
                    new TextRun
                    {
                        Id = "canvas-render-decorated",
                        Text = "underlined highlighted text",
                        Marks =
                        [
                            new InlineMark { Type = InlineMarkType.Underline },
                            new InlineMark { Type = InlineMarkType.Strikethrough },
                            new InlineMark { Type = InlineMarkType.Highlight, Value = "#fde68a" },
                            new InlineMark { Type = InlineMarkType.TextColor, Value = "#1d4ed8" }
                        ]
                    }
                ]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-render-body",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 30,
            ParagraphProperties = new DocumentParagraphProperties
            {
                LineSpacing = 1.2,
                SpacingAfter = 8
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "canvas-render-body-run",
                        Text = "The content layer is painted from a deterministic display list."
                    }
                ]
            }
        });

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a large (1000-paragraph) document used by the performance-budget E2E gate.</summary>
    public DocumentEditorDocument SeedLargePerfDocument(string documentId = LargePerfDocumentId, int paragraphCount = 150)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "large-perf-section-main";
        document.Metadata.Title = "Large performance document";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.2,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 72, Bottom = 72, Left = 72 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "large-perf-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 0,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 12 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "large-perf-heading-run", Text = "Large Performance Document" }]
            }
        });

        // Deterministic, multi-line paragraphs so the document spans many pages — the worst case the
        // incremental layout/command caches and the paint virtualizer must keep fast.
        for (var i = 0; i < paragraphCount; i++)
        {
            document.Blocks.Add(TextParagraph(
                sectionId,
                $"large-perf-p{i}",
                i + 1,
                DocumentTextAlignment.Left,
                $"Paragraph {i + 1} of the large performance document deliberately carries enough deterministic "
                + "descriptive contract language to wrap across several visual lines. It exercises the incremental "
                + "layout cache, the per-block display-command cache, and the page virtualizer so that scrolling, "
                + "typing, and re-rendering stay responsive even when the document is very long.",
                spacingAfter: 9));
        }

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a production-shaped document that exercises canvas text layout, lists, and pagination.</summary>
    public DocumentEditorDocument SeedCanvasTextLayoutDocument(string documentId = CanvasTextLayoutDocumentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-text-layout-section-main";
        document.Metadata.Title = "Canvas text layout and pagination";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.25,
            BodyLineHeight = 1.18,
            ParagraphSpacingAfter = 8
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-layout-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 14 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-layout-heading-run", Text = "Canvas Text Layout" }]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-layout-intro",
            20,
            DocumentTextAlignment.Left,
            "This document is rendered from measured canvas line boxes. The paragraph is intentionally long enough to wrap over several lines while preserving word boundaries, inline marks, and natural document rhythm.",
            spacingAfter: 12));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-layout-center",
            30,
            DocumentTextAlignment.Center,
            "Centered text keeps its own visual measure and remains aligned inside the page body.",
            spacingBefore: 6,
            spacingAfter: 12,
            leftIndent: 24,
            rightIndent: 24));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-layout-right",
            40,
            DocumentTextAlignment.Right,
            "Right aligned text resolves against the paragraph width without drifting into the margin.",
            spacingAfter: 12));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-layout-justify",
            50,
            DocumentTextAlignment.Justify,
            "Justified paragraphs expand spaces on non-final lines so the text reads like a document page instead of a single canvas demo line. The last line remains natural and avoids stretched words.",
            spacingAfter: 14));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-layout-list-one",
            SectionId = sectionId,
            Type = DocumentBlockType.List,
            Order = 60,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.15, SpacingAfter = 8 },
            Content = new ListBlockContent
            {
                Ordered = true,
                IndentLevel = 0,
                StartNumber = 1,
                Inlines = [new TextRun { Id = "canvas-layout-list-one-run", Text = "List labels are measured separately and the wrapped item text uses a clean hanging layout." }]
            }
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-layout-list-two",
            SectionId = sectionId,
            Type = DocumentBlockType.List,
            Order = 70,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.15, SpacingAfter = 12 },
            Content = new ListBlockContent
            {
                Ordered = false,
                IndentLevel = 1,
                StartNumber = 1,
                Inlines = [new TextRun { Id = "canvas-layout-list-two-run", Text = "Nested bullets keep the marker outside the editable text run while preserving the document body grid." }]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-layout-long",
            80,
            DocumentTextAlignment.Left,
            string.Join(' ', Enumerable.Repeat(
                "Pagination keeps long text flowing through measured page bodies without overlapping adjacent line rectangles, and it carries the same font resolver, cache, alignment, spacing, and wrapping rules onto every page.",
                16)),
            spacingAfter: 10,
            lineSpacing: 1.12));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-layout-manual-page-break",
            SectionId = sectionId,
            Type = DocumentBlockType.PageBreak,
            Order = 90,
            Content = new PageBreakBlockContent()
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-layout-after-break",
            100,
            DocumentTextAlignment.Left,
            "After a manual page break, the renderer starts from a fresh page body and keeps the same deterministic measurement cache.",
            spacingAfter: 0));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document with wrapped text used to exercise canvas caret and selection behavior.</summary>
    public DocumentEditorDocument SeedCanvasCaretSelectionDocument(string documentId = CanvasCaretSelectionDocumentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-caret-selection-section-main";
        document.Metadata.Title = "Canvas caret and selection";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-selection-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 14 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-selection-heading-run", Text = "Canvas Caret Selection" }]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-selection-body",
            20,
            DocumentTextAlignment.Left,
            "Clicking inside this measured canvas paragraph places a model-owned caret, arrow keys move through grapheme-safe stops, and drag selection paints a separate overlay without repainting the content cache.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-selection-second",
            30,
            DocumentTextAlignment.Left,
            "A second wrapped paragraph provides cross-line and cross-block selection geometry for pointer gestures and keyboard extension.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a compact document used to exercise canvas text input and IME behavior.</summary>
    public DocumentEditorDocument SeedCanvasTypingDocument(string documentId = CanvasTypingDocumentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-typing-section-main";
        document.Metadata.Title = "Canvas typing pipeline";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-typing-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 14 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-typing-heading-run", Text = "Canvas Typing Pipeline" }]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-typing-body",
            20,
            DocumentTextAlignment.Left,
            "Start",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-typing-second",
            30,
            DocumentTextAlignment.Left,
            "Delete boundary",
            spacingAfter: 12,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a compact document used to exercise canvas inline formatting commands.</summary>
    public DocumentEditorDocument SeedCanvasInlineFormatDocument(string documentId = CanvasInlineFormatDocumentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-inline-format-section-main";
        document.Metadata.Title = "Canvas inline formatting";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-format-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 14 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-format-heading-run", Text = "Canvas Inline Formatting" }]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-format-body",
            20,
            DocumentTextAlignment.Left,
            "Format this canvas text with the unified command dispatcher.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a compact document used to exercise canvas paragraph commands, lists, heading styles, and ruler state.</summary>
    public DocumentEditorDocument SeedCanvasParagraphDocument(string documentId = CanvasParagraphDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found
            && existing.Document is not null
            && existing.Document.NumberingDefinitions.Count > 0
            && existing.Document.ListStyles.Count > 0)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-paragraph-section-main";
        document.Metadata.Title = "Canvas paragraph formatting";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-paragraph-heading-candidate",
            10,
            DocumentTextAlignment.Left,
            "Quarterly operating summary",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-paragraph-body",
            20,
            DocumentTextAlignment.Left,
            "Paragraph formatting should update alignment, spacing before and after, and indentation without moving selection out of the canvas.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-paragraph-list",
            30,
            DocumentTextAlignment.Left,
            "List command target",
            spacingAfter: 8,
            lineSpacing: 1.12));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-paragraph-quote",
            40,
            DocumentTextAlignment.Left,
            "Quote style target for visual block differentiation.",
            spacingBefore: 6,
            spacingAfter: 12,
            leftIndent: 18,
            lineSpacing: 1.18));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a compact document used to exercise tab stops, leaders, and ruler tab interactions.</summary>
    public DocumentEditorDocument SeedCanvasTabStopsRulerDocument(string documentId = CanvasTabStopsRulerDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found
            && existing.Document is not null
            && existing.Document.Blocks.Any(block => block.Id == "canvas-e2-tabstops-decimal"
                && block.ParagraphProperties.TabStops.Any(stop => stop.Alignment == DocumentTabStopAlignment.Decimal)))
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-e2-tabstops-section";
        document.Metadata.Title = "Canvas tab stops and ruler";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 10
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 72, Bottom = 72, Left = 72 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e2-tabstops-heading",
            10,
            DocumentTextAlignment.Left,
            "Tab stops and ruler",
            spacingAfter: 12,
            lineSpacing: 1.16));

        var decimalParagraph = TextParagraph(
            sectionId,
            "canvas-e2-tabstops-decimal",
            20,
            DocumentTextAlignment.Left,
            "Revenue\t1234.50\nCosts\t98.75\nOperating result\t1135.75",
            spacingAfter: 14,
            lineSpacing: 1.2);
        decimalParagraph.ParagraphProperties.DefaultTabWidth = 36;
        decimalParagraph.ParagraphProperties.TabStops =
        [
            new DocumentTabStop
            {
                Position = 250,
                Alignment = DocumentTabStopAlignment.Decimal,
                Leader = DocumentTabStopLeader.Dots
            }
        ];
        document.Blocks.Add(decimalParagraph);

        var variants = TextParagraph(
            sectionId,
            "canvas-e2-tabstops-variants",
            30,
            DocumentTextAlignment.Left,
            "Left\tAlpha\tCenter\tRight\tBar",
            spacingAfter: 12,
            lineSpacing: 1.2);
        variants.ParagraphProperties.TabStops =
        [
            new DocumentTabStop { Position = 90, Alignment = DocumentTabStopAlignment.Left, Leader = DocumentTabStopLeader.None },
            new DocumentTabStop { Position = 190, Alignment = DocumentTabStopAlignment.Center, Leader = DocumentTabStopLeader.Dash },
            new DocumentTabStop { Position = 300, Alignment = DocumentTabStopAlignment.Right, Leader = DocumentTabStopLeader.Underline },
            new DocumentTabStop { Position = 380, Alignment = DocumentTabStopAlignment.Bar, Leader = DocumentTabStopLeader.None }
        ];
        document.Blocks.Add(variants);

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e2-tabstops-ruler-target",
            40,
            DocumentTextAlignment.Left,
            "Ruler target\t45.60",
            spacingAfter: 12,
            lineSpacing: 1.2));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a compact document used to exercise canvas clipboard operations.</summary>
    public DocumentEditorDocument SeedCanvasClipboardDocument(string documentId = CanvasClipboardDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-clipboard-section-main";
        document.Metadata.Title = "Canvas clipboard";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-clipboard-source",
            10,
            DocumentTextAlignment.Left,
            "Copy this formatted clause into the canvas target.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-clipboard-cut-source",
            20,
            DocumentTextAlignment.Left,
            "Cut this sentence with one undo transaction.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-clipboard-target",
            30,
            DocumentTextAlignment.Left,
            "Paste target: ",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-clipboard-rich-target",
            40,
            DocumentTextAlignment.Left,
            "Rich paste target: ",
            spacingAfter: 12,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas history, save, and cross-category round-trip state.</summary>
    public DocumentEditorDocument SeedCanvasHistorySaveDocument(string documentId = CanvasHistorySaveDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-history-section-main";
        document.Metadata.Title = "Canvas history save";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-history-text",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.16, SpacingAfter = 12 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "canvas-history-text-run",
                        Text = "History save text target with comment and revision markers.",
                        Marks =
                        [
                            new InlineMark
                            {
                                Type = InlineMarkType.CommentAnchor,
                                CommentAnchor = new CommentAnchorMarkData { CommentId = "canvas-history-comment" }
                            },
                            new InlineMark
                            {
                                Type = InlineMarkType.Revision,
                                RevisionId = "canvas-history-revision"
                            }
                        ]
                    }
                ]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-history-format",
            20,
            DocumentTextAlignment.Left,
            "Formatting save target keeps toolbar command state.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-history-table",
            SectionId = sectionId,
            Type = DocumentBlockType.Table,
            Order = 30,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent
                {
                    Width = 420,
                    Alignment = TableHorizontalAlignment.Center,
                    CellPadding = 7,
                    Borders = new TableCellBorders
                    {
                        Top = "1px solid #cbd5e1",
                        Right = "1px solid #cbd5e1",
                        Bottom = "1px solid #cbd5e1",
                        Left = "1px solid #cbd5e1"
                    }
                },
                Rows =
                [
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateCanvasHistoryCell("canvas-history-table-h-category", "Category", true),
                            CreateCanvasHistoryCell("canvas-history-table-h-state", "State", true)
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateCanvasHistoryCell("canvas-history-table-c-save", "Save"),
                            CreateCanvasHistoryCell("canvas-history-table-c-persisted", "Persisted")
                        ]
                    }
                ]
            }
        });

        document.Blocks.Add(CreateImageDrawingParagraph(
            "canvas-history-image",
            40,
            DocumentImageSource.Url,
            DemoImageUrl,
            null,
            "Canvas history save image",
            "Canvas image persists through save and reload",
            180,
            120,
            DocumentImageAlignment.Center,
            DocumentObjectLayout.Inline(),
            sectionId));

        document.Comments.Add(new DocumentComment
        {
            Id = "canvas-history-comment",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "canvas-history-text",
                StartInlineIndex = 0,
                EndInlineIndex = 0,
                StartOffset = 0,
                EndOffset = 12
            },
            Visibility = DocumentCommentVisibility.Internal,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "canvas-history-comment-entry",
                    Author = DemoAuthor,
                    Text = "Canvas history save comment",
                    CreatedAt = CanonicalDemoTimestamp.AddMinutes(12)
                }
            ]
        });

        document.Revisions.Add(new DocumentRevision
        {
            Id = "canvas-history-revision",
            Type = DocumentRevisionType.Formatting,
            Range = new DocumentRevisionRange
            {
                BlockId = "canvas-history-text",
                StartInlineIndex = 0,
                EndInlineIndex = 0,
                StartOffset = 0,
                EndOffset = 12
            },
            Author = new DocumentRevisionAuthor
            {
                Id = "demo-reviewer",
                DisplayName = "Demo Reviewer",
                Email = "reviewer@example.local"
            },
            CreatedAt = CanonicalDemoTimestamp.AddMinutes(15),
            Action = DocumentRevisionAction.Pending,
            PayloadJson = """{"mark":"commentAnchor"}"""
        });

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas comments, tracked revisions, and protected editing regions.</summary>
    public DocumentEditorDocument SeedCanvasCommentsRevisionsDocument(string documentId = CanvasCommentsRevisionsDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-comments-revisions-section-main";
        document.Metadata.Title = "Canvas comments and revisions";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 }
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-phase17-review",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.16, SpacingAfter = 12 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "canvas-phase17-comment-run",
                        Text = "Commented clause ",
                        Marks =
                        [
                            new InlineMark
                            {
                                Type = InlineMarkType.CommentAnchor,
                                CommentAnchor = new CommentAnchorMarkData { CommentId = "canvas-phase17-comment" }
                            }
                        ]
                    },
                    new TextRun
                    {
                        Id = "canvas-phase17-insertion-run",
                        Text = "inserted text ",
                        Marks =
                        [
                            new InlineMark { Type = InlineMarkType.Revision, RevisionId = "canvas-phase17-revision-insert", Value = "Insertion" }
                        ]
                    },
                    new TextRun
                    {
                        Id = "canvas-phase17-deletion-run",
                        Text = "deleted text ",
                        Marks =
                        [
                            new InlineMark { Type = InlineMarkType.Revision, RevisionId = "canvas-phase17-revision-delete", Value = "Deletion" }
                        ]
                    },
                    new TextRun
                    {
                        Id = "canvas-phase17-format-run",
                        Text = "formatted text.",
                        Marks =
                        [
                            new InlineMark { Type = InlineMarkType.Bold },
                            new InlineMark { Type = InlineMarkType.Revision, RevisionId = "canvas-phase17-revision-format", Value = "Formatting" }
                        ]
                    }
                ]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-phase17-protected",
            20,
            DocumentTextAlignment.Left,
            "Locked prefix editable island locked suffix.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Comments.Add(new DocumentComment
        {
            Id = "canvas-phase17-comment",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "canvas-phase17-review",
                StartInlineIndex = 0,
                EndInlineIndex = 0,
                StartOffset = 0,
                EndOffset = 16
            },
            Visibility = DocumentCommentVisibility.Internal,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "canvas-phase17-comment-entry",
                    Author = DemoAuthor,
                    Text = "Canvas phase 17 comment",
                    CreatedAt = CanonicalDemoTimestamp.AddMinutes(17)
                }
            ]
        });

        document.Revisions.Add(CreateCanvasPhase17Revision("canvas-phase17-revision-insert", DocumentRevisionType.Insertion, "canvas-phase17-review", 17, 31, "Inserted text pending review"));
        document.Revisions.Add(CreateCanvasPhase17Revision("canvas-phase17-revision-delete", DocumentRevisionType.Deletion, "canvas-phase17-review", 31, 44, "Deleted text pending review"));
        document.Revisions.Add(CreateCanvasPhase17Revision("canvas-phase17-revision-format", DocumentRevisionType.Formatting, "canvas-phase17-review", 44, 59, """{"markType":"Bold","newActive":true}"""));
        document.IsProtected = true;
        document.RestrictedMarkers.Add(new DocumentRestrictedMarker
        {
            Id = "canvas-phase17-editable-region",
            StartBlockId = "canvas-phase17-protected",
            StartOffset = 14,
            EndBlockId = "canvas-phase17-protected",
            EndOffset = 29,
            Label = "Editable island"
        });

        StoreDocument(document);
        return document;
    }

    private static DocumentRevision CreateCanvasPhase17Revision(string id, DocumentRevisionType type, string blockId, int startOffset, int endOffset, string payload)
        => new()
        {
            Id = id,
            Type = type,
            Range = new DocumentRevisionRange
            {
                BlockId = blockId,
                StartInlineIndex = 0,
                EndInlineIndex = 0,
                StartOffset = startOffset,
                EndOffset = endOffset
            },
            Author = new DocumentRevisionAuthor
            {
                Id = "demo-reviewer",
                DisplayName = "Demo Reviewer",
                Email = "reviewer@example.local"
            },
            CreatedAt = CanonicalDemoTimestamp.AddMinutes(18),
            Action = DocumentRevisionAction.Pending,
            PayloadJson = payload
        };

    /// <summary>
    /// Seeds a Czech contract document containing known misspellings from the demo LanguageTool
    /// dictionary (smlouvva, chybbou) plus correct Czech text, for the async proofing provider E2E.
    /// </summary>
    public DocumentEditorDocument SeedProofingCzechDocument(string documentId = ProofingCzechDocumentId, bool reset = false)
    {
        if (!reset)
        {
            var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
            if (existing.Found && existing.Document is not null)
            {
                return existing.Document;
            }
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "proofing-czech-section-main";
        document.Metadata.Title = "Česká smlouva s překlepy";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Sections[0].Id = sectionId;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "proofing-czech-target",
            10,
            DocumentTextAlignment.Left,
            "Tato smlouvva byla uzavřena s chybbou.",
            spacingAfter: 12));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "proofing-czech-correct",
            20,
            DocumentTextAlignment.Left,
            "Dodavatel dodá zboží v dohodnutém termínu a kupující zaplatí kupní cenu.",
            spacingAfter: 12));

        StoreDocument(document);
        return document;
    }

    /// <summary>
    /// Seeds a document with a redaction-marked bank account: rendered as a black bar in the
    /// editor and destroyed (block characters) in every export — the redaction E2E gate.
    /// </summary>
    public DocumentEditorDocument SeedRedactionDocument(string documentId = RedactionDocumentId, bool reset = false)
    {
        if (!reset)
        {
            var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
            if (existing.Found && existing.Document is not null)
            {
                return existing.Document;
            }
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "redaction-section-main";
        document.Metadata.Title = "Smlouva s redigovanými údaji";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Sections[0].Id = sectionId;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "redaction-account",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.16, SpacingAfter = 12 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "redaction-account-label", Text = "Kupní cena bude uhrazena na účet " },
                    new TextRun
                    {
                        Id = "redaction-account-secret",
                        Text = "123456789/0100",
                        Marks = [new InlineMark { Type = InlineMarkType.Redaction }]
                    },
                    new TextRun { Id = "redaction-account-tail", Text = " vedený u Komerční banky." }
                ]
            }
        });

        document.Blocks.Add(TextParagraph(sectionId, "redaction-public", 20, DocumentTextAlignment.Left,
            "Ostatní ujednání smlouvy zůstávají veřejná a exportují se beze změny.",
            spacingAfter: 12));

        StoreDocument(document);
        return document;
    }

    /// <summary>
    /// Seeds a Czech court filing (podání): per-page line numbering in the left margin and the
    /// case-file margin note (č.l.) in the header — the legal-format verification E2E.
    /// </summary>
    public DocumentEditorDocument SeedLegalFilingDocument(string documentId = LegalFilingDocumentId, bool reset = false)
    {
        if (!reset)
        {
            var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
            if (existing.Found && existing.Document is not null)
            {
                return existing.Document;
            }
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "legal-filing-section-main";
        document.Metadata.Title = "Žaloba o zaplacení 250 000 Kč";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 72, Bottom = 72, Left = 100 }
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;
        document.Sections[0].Properties.LineNumbering = new DocumentLineNumbering
        {
            Enabled = true,
            StartAt = 1,
            Increment = 1,
            DistanceFromText = 14,
            Restart = DocumentLineNumberingRestart.Page
        };

        document.HeadersFooters.Add(HeaderFooter(
            sectionId,
            "legal-filing-header-primary",
            DocumentHeaderFooterType.Header,
            DocumentHeaderFooterScope.Primary,
            "legal-filing-header-block",
            DocumentTextAlignment.Right,
            [new TextRun { Id = "legal-filing-header-cl", Text = "č.l. ______" }]));
        document.Sections[0].Properties.HeaderFooterReferences =
        [
            new DocumentHeaderFooterReference
            {
                HeaderFooterId = "legal-filing-header-primary",
                Type = DocumentHeaderFooterType.Header,
                Scope = DocumentHeaderFooterScope.Primary
            }
        ];

        document.Blocks.Add(TextParagraph(sectionId, "legal-filing-court", 10, DocumentTextAlignment.Left,
            "Okresnímu soudu v Praze", spacingAfter: 6));
        document.Blocks.Add(TextParagraph(sectionId, "legal-filing-spzn", 20, DocumentTextAlignment.Left,
            "Sp. zn.: 12 C 34/2026", spacingAfter: 14));
        document.Blocks.Add(TextParagraph(sectionId, "legal-filing-point-1", 30, DocumentTextAlignment.Justify,
            "I. Žalobce se podanou žalobou domáhá zaplacení částky 250 000 Kč s příslušenstvím z titulu smlouvy o dílo uzavřené dne 1. 3. 2026, jejíž předmět žalovaný převzal bez výhrad a cenu díla přes opakované výzvy neuhradil.",
            spacingAfter: 10));
        document.Blocks.Add(TextParagraph(sectionId, "legal-filing-point-2", 40, DocumentTextAlignment.Justify,
            "II. Nárok žalobce vyplývá z ustanovení § 2586 a násl. občanského zákoníku; splatnost byla sjednána do čtrnácti dnů od předání díla a marně uplynula dne 29. 3. 2026.",
            spacingAfter: 10));
        document.Blocks.Add(TextParagraph(sectionId, "legal-filing-petit", 50, DocumentTextAlignment.Justify,
            "S ohledem na výše uvedené žalobce navrhuje, aby soud uložil žalovanému povinnost zaplatit žalobci částku 250 000 Kč s úrokem z prodlení a nahradit náklady řízení.",
            spacingAfter: 10));

        StoreDocument(document);
        return document;
    }

    /// <summary>
    /// Seeds a contract document with comment threads from two participants — an internal author
    /// and an external client (IsExternalAuthor) — for the role-permissions / comment-colors E2E.
    /// </summary>
    public DocumentEditorDocument SeedRoleCommentsDocument(string documentId = RoleCommentsDocumentId, bool reset = false)
    {
        if (!reset)
        {
            var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
            if (existing.Found && existing.Document is not null)
            {
                return existing.Document;
            }
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "role-comments-section-main";
        document.Metadata.Title = "Smlouva ke klientské revizi";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Sections[0].Id = sectionId;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "role-comments-clause",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.16, SpacingAfter = 12 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "role-comments-internal-run",
                        Text = "Smluvní strany se dohodly na ceně díla ",
                        Marks =
                        [
                            new InlineMark
                            {
                                Type = InlineMarkType.CommentAnchor,
                                CommentAnchor = new CommentAnchorMarkData { CommentId = "role-comments-internal-thread" }
                            }
                        ]
                    },
                    new TextRun
                    {
                        Id = "role-comments-client-run",
                        Text = "ve výši 250 000 Kč bez DPH.",
                        Marks =
                        [
                            new InlineMark
                            {
                                Type = InlineMarkType.CommentAnchor,
                                CommentAnchor = new CommentAnchorMarkData { CommentId = "role-comments-client-thread" }
                            }
                        ]
                    }
                ]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "role-comments-terms",
            20,
            DocumentTextAlignment.Left,
            "Dílo bude předáno do třiceti dnů od podpisu této smlouvy.",
            spacingAfter: 12));

        document.Comments.Add(new DocumentComment
        {
            Id = "role-comments-internal-thread",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "role-comments-clause",
                StartInlineIndex = 0,
                EndInlineIndex = 0,
                StartOffset = 0,
                EndOffset = 38
            },
            Visibility = DocumentCommentVisibility.Internal,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "role-comments-internal-entry",
                    Author = new DocumentEditorAuthor { Id = "author-anna", DisplayName = "Anna Právník" },
                    Text = "Cena odpovídá schválenému rozpočtu.",
                    CreatedAt = CanonicalDemoTimestamp.AddMinutes(5)
                }
            ]
        });

        document.Comments.Add(new DocumentComment
        {
            Id = "role-comments-client-thread",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "role-comments-clause",
                StartInlineIndex = 1,
                EndInlineIndex = 1,
                StartOffset = 0,
                EndOffset = 27
            },
            Visibility = DocumentCommentVisibility.Client,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "role-comments-client-entry",
                    Author = new DocumentEditorAuthor { Id = "client-novak", DisplayName = "Klient Novák" },
                    IsExternalAuthor = true,
                    Text = "Prosíme o rozpad ceny na etapy.",
                    CreatedAt = CanonicalDemoTimestamp.AddMinutes(12)
                }
            ]
        });

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas toolbar focus, context menu actions, and spellcheck diagnostics.</summary>
    public DocumentEditorDocument SeedCanvasToolbarSpellcheckDocument(string documentId = CanvasToolbarSpellcheckDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-toolbar-spellcheck-section-main";
        document.Metadata.Title = "Canvas toolbar spellcheck";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-toolbar-selection",
            10,
            DocumentTextAlignment.Left,
            "Toolbar focus target keeps this selected phrase alive while ribbon commands run.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-spellcheck-target",
            20,
            DocumentTextAlignment.Left,
            "The proofing overlay marks wrngg and offers a real replacement from host proofing options.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-context-table",
            SectionId = sectionId,
            Type = DocumentBlockType.Table,
            Order = 30,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent { Width = 420, Alignment = TableHorizontalAlignment.Left, CellPadding = 6 },
                Rows =
                [
                    new TableRowContent
                    {
                        Cells =
                        [
                            new TableCellContent
                            {
                                Id = "canvas-context-table-cell-a",
                                Blocks = [TextParagraph(sectionId, "canvas-context-table-cell-a-text", 0, DocumentTextAlignment.Left, "Table context")]
                            },
                            new TableCellContent
                            {
                                Id = "canvas-context-table-cell-b",
                                Blocks = [TextParagraph(sectionId, "canvas-context-table-cell-b-text", 0, DocumentTextAlignment.Left, "Column action")]
                            }
                        ]
                    }
                ]
            }
        });

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas table layout, selection, operations, and persistence.</summary>
    public DocumentEditorDocument SeedCanvasTablesDocument(string documentId = CanvasTablesDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-tables-section-main";
        document.Metadata.Title = "Canvas tables";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-tables-intro",
            10,
            DocumentTextAlignment.Left,
            "Canvas table editing keeps the caret inside cells and persists structural operations.",
            spacingAfter: 14,
            lineSpacing: 1.16));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-table-phase14",
            SectionId = sectionId,
            Type = DocumentBlockType.Table,
            Order = 20,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent
                {
                    Width = 520,
                    Alignment = TableHorizontalAlignment.Left,
                    CellPadding = 7,
                    BackgroundColor = "#f8fafc",
                    Borders = new TableCellBorders
                    {
                        Top = "#94a3b8",
                        Right = "#94a3b8",
                        Bottom = "#94a3b8",
                        Left = "#94a3b8"
                    }
                },
                Rows =
                [
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-h-feature", "Feature", true, "#e0f2fe", DocumentTextAlignment.Center),
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-h-state", "State", true, "#e0f2fe", DocumentTextAlignment.Center),
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-h-notes", "Notes", true, "#e0f2fe", DocumentTextAlignment.Center)
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-c-layout", "Canvas grid", false, "#ffffff", DocumentTextAlignment.Left),
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-c-state", "Rendered", false, "#ecfdf5", DocumentTextAlignment.Center, TableCellVerticalAlignment.Middle),
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-c-notes", "Long wrapped text remains inside the cell content rectangle without leaking into neighboring columns.", false, "#ffffff", DocumentTextAlignment.Left)
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-c-ops", "Operations", false, "#ffffff", DocumentTextAlignment.Left),
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-c-undo", "Undoable", false, "#fef3c7", DocumentTextAlignment.Center, TableCellVerticalAlignment.Middle),
                            CreateCanvasTableCell(sectionId, "canvas-table-phase14-c-persist", "Insert row, insert column, format cell and reload all use the real provider save path.", false, "#ffffff", DocumentTextAlignment.Left)
                        ]
                    }
                ]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-tables-after",
            30,
            DocumentTextAlignment.Left,
            "The paragraph below the table verifies table height participates in normal pagination.",
            spacingBefore: 12,
            spacingAfter: 8,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas image layout, drawing runs, object handles, and persistence.</summary>
    public DocumentEditorDocument SeedCanvasImagesDocument(string documentId = CanvasImagesDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-images-section-main";
        document.Metadata.Title = "Canvas images and drawings";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-images-intro",
            10,
            DocumentTextAlignment.Left,
            "Canvas image objects render through the real canvas pipeline with object handles, captions, alt text, wrap exclusions, undoable movement and provider-backed save.",
            spacingAfter: 14,
            lineSpacing: 1.16));

        document.Blocks.Add(CreateImageDrawingParagraph(
            "canvas-image-phase15-main",
            20,
            DocumentImageSource.Url,
            DemoImageUrl,
            null,
            "Canvas phase 15 exhibit",
            "Phase 15 square wrapped image caption",
            168,
            96,
            DocumentImageAlignment.Start,
            new DocumentObjectLayout
            {
                Kind = DocumentObjectLayoutKind.Anchored,
                Anchor = { BlockId = "canvas-images-wrap-text", Offset = 0, MoveWithText = true },
                Wrap = { Mode = DocumentWrapMode.Square, DistanceLeft = 12, DistanceRight = 14, DistanceTop = 6, DistanceBottom = 8 },
                Position = { X = 0, Y = 34, HorizontalAlignment = DocumentImageHorizontalPosition.Left },
                Transform = { Width = 168, Height = 96, LockAspectRatio = true },
                Stacking = { ZIndex = 2 }
            },
            sectionId));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-images-wrap-text",
            30,
            DocumentTextAlignment.Left,
            "The paragraph deliberately starts beside the square image and contains enough text to prove that each visual line avoids the image footprint while the rest of the paragraph continues naturally below the object.",
            spacingAfter: 16,
            lineSpacing: 1.16));

        document.Blocks.Add(CreateImageDrawingParagraph(
            "canvas-images-drawing",
            40,
            DocumentImageSource.Url,
            DemoImageUrl,
            null,
            null,
            string.Empty,
            128,
            72,
            DocumentImageAlignment.Center,
            new DocumentObjectLayout
            {
                Kind = DocumentObjectLayoutKind.Inline,
                Anchor = { BlockId = "canvas-images-drawing", Offset = 0, MoveWithText = true },
                Wrap = { Mode = DocumentWrapMode.Inline },
                Position = { X = 0, Y = 0, HorizontalAlignment = DocumentImageHorizontalPosition.Center },
                Transform = { Width = 128, Height = 72, LockAspectRatio = true },
                Stacking = { ZIndex = 0 }
            },
            sectionId));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-images-after",
            50,
            DocumentTextAlignment.Left,
            "Saving the document after a mouse resize and move must preserve the image transform, position and metadata when the canvas model is rebuilt.",
            spacingBefore: 8,
            spacingAfter: 8,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas vector drawings, text boxes, lines, connectors, charts, and persistence.</summary>
    public DocumentEditorDocument SeedCanvasShapesDrawingsDocument(string documentId = CanvasShapesDrawingsDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-shapes-section-main";
        document.Metadata.Title = "Canvas shapes and charts";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-shapes-intro",
            10,
            DocumentTextAlignment.Left,
            "Canvas drawing objects below are persisted as drawing runs and rendered by the canvas object pipeline.",
            spacingAfter: 12,
            lineSpacing: 1.16));

        document.Blocks.Add(CreateDrawingParagraph(
            sectionId,
            "canvas-shape-rounded",
            20,
            DocumentDrawingKind.Shape,
            176,
            92,
            CreateDrawingLayout("canvas-shapes-intro", DocumentWrapMode.Square, 0, 22, 3),
            shape: new DocumentDrawingShape
            {
                Preset = "roundRectangle",
                Fill = new DocumentDrawingFill { Color = "#dbeafe", Opacity = 1 },
                Stroke = new DocumentDrawingStroke { Color = "#2563eb", Width = 2 },
                Shadow = new DocumentDrawingShadow { Blur = 8, OffsetY = 3 }
            }));

        document.Blocks.Add(CreateDrawingParagraph(
            sectionId,
            "canvas-textbox-callout",
            30,
            DocumentDrawingKind.TextBox,
            260,
            96,
            CreateDrawingLayout("canvas-shapes-intro", DocumentWrapMode.InFrontOfText, 216, 20, 4),
            shape: new DocumentDrawingShape
            {
                Preset = "rectangle",
                Fill = new DocumentDrawingFill { Color = "#fef3c7", Opacity = 1 },
                Stroke = new DocumentDrawingStroke { Color = "#d97706", Width = 1.5 }
            },
            textBody: new DocumentDrawingTextBody
            {
                Paragraphs =
                [
                    new DocumentDrawingTextParagraph
                    {
                        Text = "E7 text box",
                        Alignment = "center",
                        Style = new DocumentDrawingTextStyle { FontSize = 16, Bold = true, Color = "#78350f" }
                    },
                    new DocumentDrawingTextParagraph
                    {
                        Text = "Editable drawing text is stored in the model.",
                        Alignment = "center",
                        Style = new DocumentDrawingTextStyle { FontSize = 12, Color = "#92400e" }
                    }
                ]
            }));

        document.Blocks.Add(CreateDrawingParagraph(
            sectionId,
            "canvas-line-arrow",
            40,
            DocumentDrawingKind.Line,
            240,
            32,
            CreateDrawingLayout("canvas-shapes-intro", DocumentWrapMode.InFrontOfText, 38, 140, 5),
            shape: new DocumentDrawingShape
            {
                Preset = "line",
                Stroke = new DocumentDrawingStroke { Color = "#16a34a", Width = 3, EndArrow = "triangle" },
                Fill = new DocumentDrawingFill { Type = "none", Color = "#ffffff" }
            }));

        document.Blocks.Add(CreateDrawingParagraph(
            sectionId,
            "canvas-chart-bar",
            50,
            DocumentDrawingKind.Chart,
            320,
            210,
            CreateDrawingLayout("canvas-shapes-intro", DocumentWrapMode.TopBottom, 72, 192, 6),
            chart: new DocumentDrawingChart
            {
                Type = "bar",
                Title = "Quarterly trend",
                Categories = ["Q1", "Q2", "Q3", "Q4"],
                Palette = ["#2563eb", "#16a34a"],
                Series =
                [
                    new DocumentDrawingChartSeries { Name = "Plan", Values = [12, 16, 14, 20], Color = "#2563eb" },
                    new DocumentDrawingChartSeries { Name = "Actual", Values = [10, 18, 17, 24], Color = "#16a34a" }
                ]
            }));

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-shapes-after",
            60,
            DocumentTextAlignment.Left,
            "The following paragraph verifies that anchored drawings participate in layout and survive save/reload.",
            spacingBefore: 14,
            spacingAfter: 8,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas math equation model, layout, rendering, commands, and persistence.</summary>
    public DocumentEditorDocument SeedCanvasMathEquationsDocument(string documentId = CanvasMathEquationsDocumentId, bool forceReset = false)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (!forceReset && existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-math-section-main";
        document.Metadata.Title = "Canvas math equations";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.18,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-math-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 12 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-math-heading-run", Text = "Canvas Math Equations" }]
            }
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-math-inline",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.22, SpacingAfter = 12 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "canvas-math-inline-prefix", Text = "Inline equation: " },
                    new DocumentMathRun
                    {
                        Id = "canvas-math-inline-run",
                        MathId = "canvas-math-inline-equation",
                        DisplayMode = DocumentMathDisplayMode.Inline,
                        AltText = "(a+b)/(c) + x^2 + sqrt(y)",
                        Content = new DocumentMathContent
                        {
                            Elements =
                            [
                                Fraction(RunContent("a+b"), RunContent("c")),
                                Run(" + ", "normal"),
                                Sup(RunContent("x"), RunContent("2")),
                                Run(" + ", "normal"),
                                Radical(RunContent("y"))
                            ]
                        }
                    },
                    new TextRun { Id = "canvas-math-inline-suffix", Text = " is laid out without DOM contenteditable authority." }
                ]
            }
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-math-display",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 30,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Center,
                LineSpacing = 1.32,
                SpacingBefore = 8,
                SpacingAfter = 14
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new DocumentMathRun
                    {
                        Id = "canvas-math-display-run",
                        MathId = "canvas-math-display-equation",
                        DisplayMode = DocumentMathDisplayMode.Display,
                        AltText = "sum i=1 to n of i",
                        Content = new DocumentMathContent
                        {
                            Elements =
                            [
                                new DocumentMathElement
                                {
                                    Type = "nary",
                                    Operator = "∑",
                                    LowerLimit = RunContent("i=1"),
                                    UpperLimit = RunContent("n"),
                                    Base = RunContent("i")
                                },
                                Run(" = ", "normal"),
                                Fraction(RunContent("n(n+1)"), RunContent("2"))
                            ]
                        }
                    }
                ]
            }
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-math-matrix",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 40,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.25, SpacingAfter = 12 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "canvas-math-matrix-prefix", Text = "Matrix equation: " },
                    new DocumentMathRun
                    {
                        Id = "canvas-math-matrix-run",
                        MathId = "canvas-math-matrix-equation",
                        DisplayMode = DocumentMathDisplayMode.Inline,
                        AltText = "[1,0;0,1]",
                        Content = new DocumentMathContent
                        {
                            Elements =
                            [
                                new DocumentMathElement
                                {
                                    Type = "matrix",
                                    Rows =
                                    [
                                        new DocumentMathMatrixRow { Cells = [RunContent("1"), RunContent("0")] },
                                        new DocumentMathMatrixRow { Cells = [RunContent("0"), RunContent("1")] }
                                    ]
                                }
                            ]
                        }
                    }
                ]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-math-command-target",
            50,
            DocumentTextAlignment.Left,
            "E8 equation target:",
            spacingBefore: 8,
            spacingAfter: 8,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;

        static DocumentMathContent RunContent(string text) => new()
        {
            Elements = [Run(text)]
        };

        static DocumentMathElement Run(string text, string style = "italic") => new()
        {
            Type = "run",
            Text = text,
            Style = style
        };

        static DocumentMathElement Fraction(DocumentMathContent numerator, DocumentMathContent denominator) => new()
        {
            Type = "fraction",
            Numerator = numerator,
            Denominator = denominator
        };

        static DocumentMathElement Sup(DocumentMathContent @base, DocumentMathContent superscript) => new()
        {
            Type = "sup",
            Base = @base,
            Superscript = superscript
        };

        static DocumentMathElement Radical(DocumentMathContent radicand) => new()
        {
            Type = "radical",
            Radicand = radicand
        };
    }

    /// <summary>Seeds a document that exercises canvas content controls, form fill commands, locks, and persistence.</summary>
    public DocumentEditorDocument SeedCanvasContentControlsDocument(string documentId = CanvasContentControlsDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-content-controls-section-main";
        document.Metadata.Title = "Canvas content controls";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.5,
            BodyLineHeight = 1.18,
            ParagraphSpacingAfter = 9
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-content-controls-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 12 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-content-controls-heading-run", Text = "Canvas Content Controls" }]
            }
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-content-controls-form",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.24, SpacingAfter = 14 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "canvas-content-controls-name-label", Text = "Customer: " },
                    ContentControlRun("canvas-form-name-run", "canvas-form-name", DocumentContentControlKind.PlainText, new DocumentContentControlValue { Text = string.Empty }, alias: "Customer name", tag: "customer.name", placeholderText: "Customer name", isRequired: true),
                    new TextRun { Id = "canvas-content-controls-approved-label", Text = " Approved: " },
                    ContentControlRun("canvas-form-approved-run", "canvas-form-approved", DocumentContentControlKind.Checkbox, new DocumentContentControlValue { Checked = false }, alias: "Approval", tag: "approval"),
                    new TextRun { Id = "canvas-content-controls-plan-label", Text = " Plan: " },
                    ContentControlRun("canvas-form-plan-run", "canvas-form-plan", DocumentContentControlKind.DropDown, new DocumentContentControlValue { SelectedValue = "basic" }, alias: "Plan", tag: "subscription.plan", items:
                    [
                        new DocumentContentControlItem { DisplayText = "Basic", Value = "basic" },
                        new DocumentContentControlItem { DisplayText = "Professional", Value = "pro" },
                        new DocumentContentControlItem { DisplayText = "Enterprise", Value = "enterprise" }
                    ]),
                    new TextRun { Id = "canvas-content-controls-date-label", Text = " Renewal: " },
                    ContentControlRun("canvas-form-renewal-run", "canvas-form-renewal", DocumentContentControlKind.Date, new DocumentContentControlValue { DateIso = "2026-06-05" }, alias: "Renewal date", tag: "renewal.date", formatMask: "yyyy-MM-dd"),
                    new TextRun { Id = "canvas-content-controls-contact-label", Text = " Contact: " },
                    ContentControlRun("canvas-form-contact-run", "canvas-form-contact", DocumentContentControlKind.ComboBox, new DocumentContentControlValue { Text = string.Empty }, alias: "Contact method", tag: "contact.method", placeholderText: "Contact method", items:
                    [
                        new DocumentContentControlItem { DisplayText = "Email", Value = "email" },
                        new DocumentContentControlItem { DisplayText = "Phone", Value = "phone" },
                        new DocumentContentControlItem { DisplayText = "Portal", Value = "portal" }
                    ]),
                    new TextRun { Id = "canvas-content-controls-photo-label", Text = " Photo: " },
                    ContentControlRun("canvas-form-photo-run", "canvas-form-photo", DocumentContentControlKind.Picture, new DocumentContentControlValue { AssetId = string.Empty }, alias: "Profile photo", tag: "profile.photo", placeholderText: "Profile photo")
                ]
            }
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-content-controls-locked",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 30,
            ParagraphProperties = new DocumentParagraphProperties { LineSpacing = 1.18, SpacingAfter = 14 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "canvas-content-controls-locked-label", Text = "Locked reference: " },
                    ContentControlRun("canvas-form-locked-run", "canvas-form-locked", DocumentContentControlKind.PlainText, new DocumentContentControlValue { Text = "Readonly value" }, alias: "Locked reference", tag: "locked.reference", lockContent: true)
                ]
            }
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-content-controls-repeating",
            SectionId = sectionId,
            Type = DocumentBlockType.ContentControl,
            Order = 40,
            ParagraphProperties = new DocumentParagraphProperties { SpacingBefore = 8, SpacingAfter = 12 },
            Content = new ContentControlBlockContent
            {
                Control = new DocumentContentControl
                {
                    ControlId = "canvas-form-addresses",
                    Kind = DocumentContentControlKind.RepeatingSection,
                    Scope = DocumentContentControlScope.Block,
                    Alias = "Addresses",
                    Tag = "addresses"
                },
                Blocks =
                [
                    TextParagraph(sectionId, "canvas-content-controls-address-line", 41, DocumentTextAlignment.Left, "Address line item", spacingAfter: 8, lineSpacing: 1.16)
                ]
            }
        });

        StoreDocument(document);
        return document;

        static DocumentContentControlRun ContentControlRun(
            string id,
            string controlId,
            DocumentContentControlKind kind,
            DocumentContentControlValue value,
            string? alias = null,
            string? tag = null,
            string? placeholderText = null,
            bool isRequired = false,
            bool lockContent = false,
            string? formatMask = null,
            List<DocumentContentControlItem>? items = null)
            => new()
            {
                Id = id,
                Control = new DocumentContentControl
                {
                    ControlId = controlId,
                    Kind = kind,
                    Scope = DocumentContentControlScope.Inline,
                    Alias = alias,
                    Tag = tag,
                    PlaceholderText = placeholderText,
                    IsRequired = isRequired,
                    LockContent = lockContent,
                    FormatMask = formatMask,
                    Value = value,
                    Items = items ?? []
                }
            };
    }

    /// <summary>Seeds a document that exercises canvas headers, footers, fields, footnotes, endnotes, and page geometry.</summary>
    public DocumentEditorDocument SeedCanvasHeadersFootersNotesDocument(string documentId = CanvasHeadersFootersNotesDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-phase16-section-main";
        document.Metadata.Title = "Canvas headers fields and notes";
        document.Metadata.Author = DemoAuthor;
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp.AddHours(3);
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.3,
            BodyLineHeight = 1.15,
            ParagraphSpacingAfter = 8
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 82, Right = 76, Bottom = 84, Left = 76 },
            HeaderDistanceFromTop = 34,
            FooterDistanceFromBottom = 34
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;
        document.Sections[0].Properties.DifferentFirstPage = true;
        document.Sections[0].Properties.DifferentOddAndEvenPages = true;

        document.HeadersFooters.AddRange(CreatePhase16HeadersFooters(sectionId));
        document.Sections[0].Properties.HeaderFooterReferences =
        [
            .. document.HeadersFooters.Select(item => new DocumentHeaderFooterReference
            {
                HeaderFooterId = item.Id,
                Type = item.Type,
                Scope = item.Scope
            })
        ];

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-phase16-intro",
            10,
            DocumentTextAlignment.Left,
            "Phase 16 verifies that the canvas engine paints headers, footers, automatic fields and note areas from the canonical document model.",
            spacingAfter: 12,
            lineSpacing: 1.15));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-phase16-note-source",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            ParagraphProperties = new DocumentParagraphProperties { Alignment = DocumentTextAlignment.Left, SpacingAfter = 10, LineSpacing = 1.15 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "canvas-phase16-note-source-a", Text = "This paragraph has a footnote" },
                    new DocumentNoteReferenceRun { Id = "canvas-phase16-footnote-ref", NoteId = "canvas-phase16-footnote", NoteType = DocumentNoteType.Footnote, DisplayMarker = "1" },
                    new TextRun { Id = "canvas-phase16-note-source-b", Text = " and an endnote" },
                    new DocumentNoteReferenceRun { Id = "canvas-phase16-endnote-ref", NoteId = "canvas-phase16-endnote", NoteType = DocumentNoteType.Endnote, DisplayMarker = "i" },
                    new TextRun { Id = "canvas-phase16-note-source-c", Text = " so reference markers and note bodies stay connected through save and reload." }
                ]
            }
        });

        for (var index = 0; index < 72; index++)
        {
            document.Blocks.Add(TextParagraph(
                sectionId,
                $"canvas-phase16-flow-{index + 1}",
                30 + index * 10,
                DocumentTextAlignment.Left,
                $"Pagination line {index + 1} keeps the body text away from the professional header and footer margin areas while total page fields resolve after layout.",
                spacingAfter: 8,
                lineSpacing: 1.15));
        }

        document.Notes.Add(new DocumentNote
        {
            Id = "canvas-phase16-footnote",
            Type = DocumentNoteType.Footnote,
            SectionId = sectionId,
            Marker = "1",
            ReferenceIds = ["canvas-phase16-footnote-ref"],
            Blocks =
            [
                TextParagraph(sectionId, "canvas-phase16-footnote-body", 10, DocumentTextAlignment.Left, "Footnote body rendered in the page note area.", spacingAfter: 0)
            ]
        });
        document.Notes.Add(new DocumentNote
        {
            Id = "canvas-phase16-endnote",
            Type = DocumentNoteType.Endnote,
            SectionId = sectionId,
            Marker = "i",
            ReferenceIds = ["canvas-phase16-endnote-ref"],
            Blocks =
            [
                TextParagraph(sectionId, "canvas-phase16-endnote-body", 10, DocumentTextAlignment.Left, "Endnote body rendered at the end of the document.", spacingAfter: 0)
            ]
        });

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas numbering definitions, multilevel labels, restarts, and list styles.</summary>
    public DocumentEditorDocument SeedCanvasNumberingListsDocument(string documentId = CanvasNumberingListsDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-e1-numbering-section";
        const string legalNumberingId = "canvas-e1-legal-numbering";
        const string legalStyleId = "canvas-e1-legal-list-style";
        const string bulletNumberingId = "canvas-e1-bullet-numbering";
        document.Metadata.Title = "Canvas numbering and lists";
        document.Metadata.Author = DemoAuthor;
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.3,
            BodyLineHeight = 1.15,
            ParagraphSpacingAfter = 7
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 58, Right = 60, Bottom = 62, Left = 64 },
            HeaderDistanceFromTop = 30,
            FooterDistanceFromBottom = 30
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Title = "Numbering";
        document.Sections[0].Properties.PageSettings = document.PageSettings;
        document.NumberingDefinitions.Add(new DocumentNumberingDefinition
        {
            Id = legalNumberingId,
            AbstractId = "canvas-e1-legal-abstract",
            Name = "Legal multilevel numbering",
            StyleId = legalStyleId,
            Levels = Enumerable.Range(0, 9)
                .Select(level => new DocumentNumberingLevel
                {
                    Level = level,
                    Format = "decimal",
                    Text = string.Join('.', Enumerable.Range(1, level + 1).Select(index => $"%{index}")) + ".",
                    StartAt = 1,
                    Suffix = "tab",
                    Indent = level * 18,
                    Hanging = 18
                })
                .ToList()
        });
        document.NumberingDefinitions.Add(new DocumentNumberingDefinition
        {
            Id = bulletNumberingId,
            AbstractId = "canvas-e1-bullet-abstract",
            Name = "Bullet briefing markers",
            StyleId = "canvas-e1-bullet-list-style",
            Levels =
            [
                new DocumentNumberingLevel { Level = 0, Format = "bullet", Text = "\u2022", Bullet = "\u2022", Suffix = "tab", Indent = 0, Hanging = 18 },
                new DocumentNumberingLevel { Level = 1, Format = "bullet", Text = "\u25e6", Bullet = "\u25e6", Suffix = "tab", Indent = 18, Hanging = 18 },
                new DocumentNumberingLevel { Level = 2, Format = "bullet", Text = "\u25aa", Bullet = "\u25aa", Suffix = "tab", Indent = 36, Hanging = 18 }
            ]
        });
        document.ListStyles.Add(new DocumentListStyle
        {
            Id = legalStyleId,
            Name = "Legal clauses",
            NumberingId = legalNumberingId,
            IsQuickStyle = true
        });

        document.Blocks.Add(TextParagraph(sectionId, "canvas-e1-heading", 10, DocumentTextAlignment.Left, "Canvas numbering and list styles", spacingAfter: 8, lineSpacing: 1.12));
        document.Blocks.Add(TextParagraph(sectionId, "canvas-e1-intro", 20, DocumentTextAlignment.Left, "The canvas engine computes list labels from numbering definitions so insert, delete, move, save and reload keep clause numbers stable.", spacingAfter: 10, lineSpacing: 1.13));
        document.Blocks.Add(ListBlock("canvas-e1-clause-1", 30, 0, "Definitions and interpretation use the first legal level.", legalNumberingId, legalStyleId));
        document.Blocks.Add(ListBlock("canvas-e1-clause-1-1", 40, 1, "Nested definitions inherit the parent counter and render as a combined legal label.", legalNumberingId, legalStyleId));
        document.Blocks.Add(ListBlock("canvas-e1-clause-1-1-1", 50, 2, "A third level clause uses the same hanging indent rules while long text wraps without colliding with the label.", legalNumberingId, legalStyleId));
        document.Blocks.Add(ListBlock("canvas-e1-clause-2", 60, 0, "The next top-level clause continues the primary sequence after nested levels.", legalNumberingId, legalStyleId));
        document.Blocks.Add(ListBlock("canvas-e1-clause-7", 70, 0, "The remedies article restarts at seven by explicit numbering value.", legalNumberingId, legalStyleId, restart: true, numberingValue: 7));
        document.Blocks.Add(ListBlock("canvas-e1-clause-7-1", 80, 1, "Subclauses continue beneath the restarted article and persist through save and reload.", legalNumberingId, legalStyleId, continueNumbering: true));
        document.Blocks.Add(ListBlock("canvas-e1-bullet-1", 90, 0, "Operational note rendered by a bullet numbering definition.", bulletNumberingId, "canvas-e1-bullet-list-style", ordered: false));
        document.Blocks.Add(ListBlock("canvas-e1-bullet-2", 100, 1, "Nested bullet note uses the second-level glyph and the same label spacing engine.", bulletNumberingId, "canvas-e1-bullet-list-style", ordered: false));

        StoreDocument(document);
        return document;

        DocumentBlock ListBlock(
            string id,
            double order,
            int level,
            string text,
            string numberingId,
            string styleId,
            bool ordered = true,
            bool restart = false,
            bool continueNumbering = false,
            int? numberingValue = null) =>
            new()
            {
                Id = id,
                SectionId = sectionId,
                Type = DocumentBlockType.List,
                Order = order,
                ParagraphProperties = new DocumentParagraphProperties { Alignment = DocumentTextAlignment.Left, LineSpacing = 1.12, SpacingAfter = 6 },
                Content = new ListBlockContent
                {
                    Ordered = ordered,
                    IndentLevel = level,
                    StartNumber = numberingValue ?? 1,
                    NumberingId = numberingId,
                    AbstractNumberingId = numberingId,
                    ListStyleId = styleId,
                    NumberFormat = ordered ? "legal" : "bullet",
                    LevelText = ordered ? string.Join('.', Enumerable.Range(1, level + 1).Select(index => $"%{index}")) + "." : null,
                    Suffix = "tab",
                    LabelIndent = level * 18,
                    HangingIndent = 18,
                    RestartNumbering = restart,
                    ContinueNumbering = continueNumbering,
                    NumberingValue = numberingValue,
                    Inlines =
                    [
                        new TextRun { Id = $"{id}-run", Text = text }
                    ]
                }
            };
    }

    /// <summary>Seeds a document that exercises canvas sections, multi-column flow, line numbering, and mixed page geometry.</summary>
    public DocumentEditorDocument SeedCanvasSectionsColumnsDocument(string documentId = CanvasSectionsColumnsDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var columnSectionId = "canvas-e3-columns-section";
        var landscapeSectionId = "canvas-e3-landscape-section";
        document.Metadata.Title = "Canvas sections and columns";
        document.Metadata.Author = DemoAuthor;
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11.2,
            BodyLineHeight = 1.14,
            ParagraphSpacingAfter = 6
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 58, Right = 54, Bottom = 60, Left = 58 },
            HeaderDistanceFromTop = 30,
            FooterDistanceFromBottom = 30
        };
        document.Sections[0].Id = columnSectionId;
        document.Sections[0].Title = "Two column brief";
        document.Sections[0].Properties.PageSettings = document.PageSettings;
        document.Sections[0].Properties.Columns = new DocumentSectionColumns
        {
            Count = 2,
            Spacing = 24,
            SeparatorLine = true,
            Balance = true,
            Preset = "two"
        };
        document.Sections[0].Properties.LineNumbering = new DocumentLineNumbering
        {
            Enabled = true,
            StartAt = 1,
            Increment = 1,
            DistanceFromText = 10,
            Restart = DocumentLineNumberingRestart.Page
        };
        document.Sections.Add(new DocumentSection
        {
            Id = landscapeSectionId,
            Order = 1,
            Title = "Landscape exhibit",
            Properties = new DocumentSectionProperties
            {
                PageSettings = new DocumentPageSettings
                {
                    Size = DocumentPageSize.A4,
                    Landscape = true,
                    Margins = new DocumentPageMargins { Top = 50, Right = 48, Bottom = 50, Left = 48 }
                },
                Columns = new DocumentSectionColumns { Count = 1, Preset = "one" },
                LineNumbering = new DocumentLineNumbering
                {
                    Enabled = true,
                    StartAt = 20,
                    Increment = 2,
                    DistanceFromText = 12,
                    Restart = DocumentLineNumberingRestart.Section
                }
            }
        });

        document.Blocks.Add(TextParagraph(
            columnSectionId,
            "canvas-e3-heading",
            10,
            DocumentTextAlignment.Left,
            "Canvas sections, columns and line numbering",
            spacingAfter: 8,
            lineSpacing: 1.12));

        document.Blocks.Add(TextParagraph(
            columnSectionId,
            "canvas-e3-column-story",
            20,
            DocumentTextAlignment.Justify,
            string.Join(' ', Enumerable.Repeat("The first section uses a two-column text frame with a separator, page-based line numbering and deterministic flow from the left column into the right column.", 6)),
            spacingAfter: 8,
            lineSpacing: 1.08));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e3-column-break",
            SectionId = columnSectionId,
            Type = DocumentBlockType.PageBreak,
            Order = 30,
            Content = new PageBreakBlockContent { BreakType = DocumentSectionBreakType.Column }
        });

        document.Blocks.Add(TextParagraph(
            columnSectionId,
            "canvas-e3-after-column-break",
            40,
            DocumentTextAlignment.Left,
            "A deliberate column break starts this paragraph at the top of the next text column while keeping the same physical page and section geometry.",
            spacingAfter: 10,
            lineSpacing: 1.08));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e3-landscape-break",
            SectionId = columnSectionId,
            Type = DocumentBlockType.PageBreak,
            Order = 50,
            Content = new PageBreakBlockContent
            {
                BreakType = DocumentSectionBreakType.NextPage,
                NextSectionId = landscapeSectionId
            }
        });

        document.Blocks.Add(TextParagraph(
            landscapeSectionId,
            "canvas-e3-landscape-heading",
            60,
            DocumentTextAlignment.Left,
            "Landscape section geometry",
            spacingAfter: 8,
            lineSpacing: 1.12));

        document.Blocks.Add(TextParagraph(
            landscapeSectionId,
            "canvas-e3-landscape-body",
            70,
            DocumentTextAlignment.Left,
            "The second section switches to landscape page setup, restarts section-based line numbering and keeps text inside a single wide body frame after save and reload.",
            spacingAfter: 8,
            lineSpacing: 1.12));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas document styles and quick-style modification.</summary>
    public DocumentEditorDocument SeedCanvasStylesDocument(string documentId = CanvasStylesDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        document.Metadata.Title = "Canvas document styles";
        document.Metadata.Author = DemoAuthor;
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11,
            BodyLineHeight = 1.12,
            ParagraphSpacingAfter = 7
        };
        document.Styles.Add(new DocumentStyleDefinition
        {
            Id = "normal",
            Name = "Normal",
            Type = DocumentStyleType.Paragraph,
            Next = "normal",
            IsQuickStyle = true,
            IsPrimary = true,
            ParagraphFormat = { ["spacingAfter"] = 7 },
            CharacterFormat = { ["fontSize"] = 11, ["fontWeight"] = "400" }
        });
        document.Styles.Add(new DocumentStyleDefinition
        {
            Id = "heading-1",
            Name = "Heading 1",
            Type = DocumentStyleType.Paragraph,
            BasedOn = "normal",
            Next = "normal",
            IsQuickStyle = true,
            IsPrimary = true,
            HeadingLevel = 1,
            OutlineLevel = 1,
            ParagraphFormat = { ["spacingAfter"] = 12 },
            CharacterFormat = { ["fontSize"] = 20, ["fontWeight"] = "700" }
        });

        document.Blocks.Add(StyledHeading("canvas-e4-heading-a", 10, "Styles drive document-wide typography"));
        document.Blocks.Add(TextParagraph(
            document.Sections[0].Id,
            "canvas-e4-body-a",
            20,
            DocumentTextAlignment.Left,
            "Both headings in this document reference the same Heading 1 style, so modifying the style updates every matching block through the canvas style resolver.",
            spacingAfter: 8,
            lineSpacing: 1.12));
        document.Blocks.Add(StyledHeading("canvas-e4-heading-b", 30, "A second heading shares the style"));
        document.Blocks.Add(TextParagraph(
            document.Sections[0].Id,
            "canvas-e4-body-b",
            40,
            DocumentTextAlignment.Left,
            "The style definition is stored on the document model, serialized through the canvas model, and restored after save and reload.",
            spacingAfter: 8,
            lineSpacing: 1.12));

        StoreDocument(document);
        return document;

        DocumentBlock StyledHeading(string id, int order, string text)
            => new()
            {
                Id = id,
                SectionId = document.Sections[0].Id,
                Type = DocumentBlockType.Heading,
                Order = order,
                Content = new HeadingBlockContent
                {
                    Level = 1,
                    Inlines = [new TextRun { Id = $"{id}-run", Text = text }]
                }
            };
    }

    /// <summary>Seeds a document that exercises canvas fields, cross-references, captions, and bibliography.</summary>
    public DocumentEditorDocument SeedCanvasFieldsDocument(string documentId = CanvasFieldsDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = document.Sections[0].Id;
        document.Metadata.Title = "Canvas fields and references";
        document.Metadata.Author = DemoAuthor;
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11,
            BodyLineHeight = 1.14,
            ParagraphSpacingAfter = 8
        };
        document.BibliographySources.Add(new DocumentBibliographySource
        {
            Id = "canvas-e5-source-onlyoffice",
            SourceType = "article",
            Author = "Elena Novak",
            Title = "Canvas editing engines in legal documents",
            Container = "Tempo Journal",
            Year = 2026,
            Url = "https://example.test/tempo-journal/canvas-editing"
        });

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e5-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-e5-heading-run", Text = "Reference targets and generated fields" }]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e5-field-paragraph",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 10, LineSpacing = 1.14 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "canvas-e5-field-prefix",
                        Text = "Document field snapshot: ",
                        Marks = [new InlineMark { Type = InlineMarkType.Bookmark, Value = "canvas-e5-bookmark" }]
                    },
                    FieldRun("canvas-e5-page", DocumentFieldType.PageNumber, fallback: "1"),
                    new TextRun { Id = "canvas-e5-field-separator-a", Text = " / " },
                    FieldRun("canvas-e5-pages", DocumentFieldType.PageCount, fallback: "1"),
                    new TextRun { Id = "canvas-e5-field-separator-b", Text = " · " },
                    FieldRun("canvas-e5-date", DocumentFieldType.Date, "yyyy-MM-dd", "2026-06-04"),
                    new TextRun { Id = "canvas-e5-field-separator-c", Text = " · " },
                    FieldRun("canvas-e5-time", DocumentFieldType.Time, fallback: "08:00"),
                    new TextRun { Id = "canvas-e5-field-separator-d", Text = " · " },
                    FieldRun("canvas-e5-file", DocumentFieldType.FileName, fallback: "canvas-fields-and-references.docx"),
                    new TextRun { Id = "canvas-e5-field-separator-e", Text = " · " },
                    FieldRun("canvas-e5-author", DocumentFieldType.Author, fallback: "Demo User"),
                    new TextRun { Id = "canvas-e5-field-separator-f", Text = " · " },
                    new DocumentFieldRun
                    {
                        Id = "canvas-e5-styleref",
                        FieldType = DocumentFieldType.StyleRef,
                        TargetId = "Heading 1",
                        ReferenceKind = "Heading 1",
                        FallbackText = "Reference targets and generated fields"
                    }
                ]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e5-numbered-target",
            SectionId = sectionId,
            Type = DocumentBlockType.List,
            Order = 30,
            Content = new ListBlockContent
            {
                Ordered = true,
                StartNumber = 1,
                NumberingId = "canvas-e5-numbering",
                Inlines = [new TextRun { Id = "canvas-e5-numbered-target-run", Text = "Numbered reference target" }]
            }
        });
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e5-body",
            40,
            DocumentTextAlignment.Left,
            "Use the References ribbon to insert a caption, cross-reference, table of figures, bibliography, and update fields. The generated fields remain editable after save and reload.",
            spacingAfter: 8,
            lineSpacing: 1.14));

        StoreDocument(document);
        return document;

        static DocumentFieldRun FieldRun(string id, DocumentFieldType type, string? format = null, string? fallback = null)
            => new()
            {
                Id = id,
                FieldType = type,
                Format = format,
                FallbackText = fallback
            };
    }

    /// <summary>Seeds a document that exercises canvas advanced character formatting commands.</summary>
    public DocumentEditorDocument SeedCanvasAdvancedCharacterDocument(string documentId = CanvasAdvancedCharacterDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = document.Sections[0].Id;
        document.Metadata.Title = "Canvas advanced character formatting";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 12,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 8
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e6-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-e6-heading-run", Text = "Advanced character controls" }]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e6-formula",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 8, LineSpacing = 1.16 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "canvas-e6-h", Text = "H" },
                    new TextRun { Id = "canvas-e6-subscript", Text = "2", Marks = [new InlineMark { Type = InlineMarkType.Subscript }] },
                    new TextRun { Id = "canvas-e6-o", Text = "O  " },
                    new TextRun { Id = "canvas-e6-x", Text = "x" },
                    new TextRun { Id = "canvas-e6-superscript", Text = "2", Marks = [new InlineMark { Type = InlineMarkType.Superscript }] },
                    new TextRun { Id = "canvas-e6-formula-tail", Text = " baseline proof" }
                ]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e6-preformatted",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 30,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 8, LineSpacing = 1.16 },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Id = "canvas-e6-smallcaps", Text = "small caps sample", Marks = [new InlineMark { Type = InlineMarkType.SmallCaps }] },
                    new TextRun { Id = "canvas-e6-gap-a", Text = "  " },
                    new TextRun { Id = "canvas-e6-expanded", Text = "expanded", Marks = [new InlineMark { Type = InlineMarkType.CharacterSpacing, Value = "2" }] },
                    new TextRun { Id = "canvas-e6-gap-b", Text = "  " },
                    new TextRun { Id = "canvas-e6-scaled", Text = "scaled", Marks = [new InlineMark { Type = InlineMarkType.CharacterScale, Value = "125" }] },
                    new TextRun { Id = "canvas-e6-gap-c", Text = "  " },
                    new TextRun { Id = "canvas-e6-double", Text = "double strike", Marks = [new InlineMark { Type = InlineMarkType.DoubleStrikethrough }] }
                ]
            }
        });
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e6-command-target",
            40,
            DocumentTextAlignment.Left,
            "phase e6 command target for toolbar case and undo",
            spacingAfter: 8,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas autocorrect, autoformat, format painter, and symbol commands.</summary>
    public DocumentEditorDocument SeedCanvasAutocorrectFormatPainterDocument(string documentId = CanvasAutocorrectFormatPainterDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = document.Sections[0].Id;
        document.Metadata.Title = "Canvas autocorrect and format painter";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 12,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 8
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e10-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 10, LineSpacing = 1.16 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-e10-heading-run", Text = "Autocorrect and format painter" }]
            }
        });
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e10-autocorrect-target",
            20,
            DocumentTextAlignment.Left,
            "Dash: ",
            spacingAfter: 8,
            lineSpacing: 1.16));
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e10-list-target",
            30,
            DocumentTextAlignment.Left,
            "1.",
            spacingAfter: 8,
            lineSpacing: 1.16));
        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e10-painter-source",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = 40,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Right,
                SpacingAfter = 14,
                LineSpacing = 1.22,
                LeftIndent = 18
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = "canvas-e10-painter-source-run",
                        Text = "Styled",
                        Marks =
                        [
                            new InlineMark { Type = InlineMarkType.Bold },
                            new InlineMark { Type = InlineMarkType.TextColor, Value = "#1155cc" },
                            new InlineMark { Type = InlineMarkType.Highlight, Value = "#fde68a" }
                        ]
                    },
                    new TextRun { Id = "canvas-e10-painter-source-tail", Text = " source" }
                ]
            }
        });
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e10-painter-target",
            50,
            DocumentTextAlignment.Left,
            "Target formatting",
            spacingAfter: 8,
            lineSpacing: 1.16));
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e10-symbol-target",
            60,
            DocumentTextAlignment.Left,
            "Symbols: ",
            spacingAfter: 8,
            lineSpacing: 1.16));
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e10-link-target",
            70,
            DocumentTextAlignment.Left,
            "https://example.test",
            spacingAfter: 8,
            lineSpacing: 1.16));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas view modes, zoom presets, and print preview.</summary>
    public DocumentEditorDocument SeedCanvasViewModesPrintDocument(string documentId = CanvasViewModesPrintDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = document.Sections[0].Id;
        document.Metadata.Title = "Canvas view modes and print";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 12,
            BodyLineHeight = 1.22,
            ParagraphSpacingAfter = 8
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 72, Right = 78, Bottom = 72, Left = 78 },
            HeaderDistanceFromTop = 36,
            FooterDistanceFromBottom = 36
        };
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e11-heading",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 10,
            ParagraphProperties = new DocumentParagraphProperties { SpacingAfter = 12, LineSpacing = 1.16 },
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-e11-heading-run", Text = "View modes and print preview" }]
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e11-reading-intro",
            20,
            DocumentTextAlignment.Left,
            "Reading mode keeps the same document model while giving the canvas a quieter workspace, hidden toolbar, and stable caret geometry.",
            spacingAfter: 10,
            lineSpacing: 1.22));
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e11-zoom-target",
            30,
            DocumentTextAlignment.Justify,
            "Zoom presets are calculated from the real page and viewport dimensions. Fit width, fit page, multiple pages, custom percent, Ctrl-wheel, and pinch commands all flow through the command dispatcher and repaint the same display list.",
            spacingAfter: 10,
            lineSpacing: 1.22));
        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e11-print-target",
            40,
            DocumentTextAlignment.Left,
            "Print preview is generated from the current canvas display list, including page metrics, text runs, and printable command counts.",
            spacingAfter: 12,
            lineSpacing: 1.22));

        for (var index = 0; index < 12; index++)
        {
            document.Blocks.Add(TextParagraph(
                sectionId,
                $"canvas-e11-body-{index}",
                50 + index * 10,
                index % 3 == 0 ? DocumentTextAlignment.Justify : DocumentTextAlignment.Left,
                $"Preview paragraph {index + 1}: the same stored document remains editable while view state changes. The paragraph contains enough prose to wrap naturally, keep hit testing meaningful after zoom, and provide visible printed content for the preview snapshot.",
                spacingAfter: 9,
                lineSpacing: 1.2));
        }

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a document that exercises canvas hyphenation, page background, and advanced tables.</summary>
    public DocumentEditorDocument SeedCanvasHyphenationAdvancedTablesDocument(string documentId = CanvasHyphenationAdvancedTablesDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null && IsCurrentCanvasHyphenationAdvancedTablesSeed(existing.Document))
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = "canvas-e12-section-main";
        document.Metadata.Title = "Canvas hyphenation and advanced tables";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11,
            BodyLineHeight = 1.16,
            ParagraphSpacingAfter = 7
        };
        document.Hyphenation = new DocumentHyphenationOptions
        {
            Enabled = true,
            Mode = "manual",
            ConsecutiveLimit = 2,
            MinPrefix = 3,
            MinSuffix = 3,
            Zone = 24
        };
        document.PageBackground = new DocumentPageBackgroundOptions
        {
            Color = "#f8fafc",
            Watermark = new DocumentWatermarkOptions
            {
                Enabled = true,
                Kind = "text",
                Text = "E12",
                Color = "rgba(37, 99, 235, 0.46)",
                Opacity = 0.18,
                Rotation = -32
            },
            Border = new DocumentPageBorderOptions
            {
                Enabled = true,
                Color = "#2563eb",
                Width = 2,
                Margin = 18,
                AlignTo = "page",
                Dash = [8, 4]
            }
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = new DocumentPageSize { Name = "A5", Width = 419.53, Height = 595.28 },
            Margins = new DocumentPageMargins { Top = 44, Right = 44, Bottom = 44, Left = 44 },
            HeaderDistanceFromTop = 28,
            FooterDistanceFromBottom = 28
        };
        document.Sections[0].Id = sectionId;
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e12-hyphenation-paragraph",
            10,
            DocumentTextAlignment.Left,
            "Manual hyphenation keeps international\u00ADization readable inside a narrow line while the canvas still preserves source text for save and reload.",
            spacingAfter: 10,
            rightIndent: 250,
            lineSpacing: 1.15));

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-e12-advanced-table",
            SectionId = sectionId,
            Type = DocumentBlockType.Table,
            Order = 20,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent
                {
                    Width = 470,
                    Alignment = TableHorizontalAlignment.Left,
                    CellPadding = 6,
                    RepeatHeaderRows = true,
                    HeaderRow = true,
                    TotalRow = true,
                    BandedRows = true,
                    CellSpacing = 0,
                    Borders = new TableCellBorders
                    {
                        Top = "#2563eb",
                        Right = "#2563eb",
                        Bottom = "#2563eb",
                        Left = "#2563eb"
                    },
                    Style = new TableStyleOptions
                    {
                        HeaderBackgroundColor = "#dbeafe",
                        BandedRowBackgroundColor = "#f8fafc",
                        TotalBackgroundColor = "#dcfce7",
                        BorderColor = "#2563eb"
                    }
                },
                Rows = CreateCanvasE12Rows(sectionId)
            }
        });

        document.Blocks.Add(TextParagraph(
            sectionId,
            "canvas-e12-after-table",
            30,
            DocumentTextAlignment.Left,
            "The paragraph after the table confirms the table exposes its last page and end position back to normal document pagination.",
            spacingBefore: 8,
            spacingAfter: 6,
            lineSpacing: 1.15));

        StoreDocument(document);
        return document;
    }

    /// <summary>Seeds a large deterministic document used by the canvas performance gate.</summary>
    public DocumentEditorDocument SeedCanvasPerformanceDocument(string documentId = CanvasPerformanceDocumentId)
    {
        var existing = base.LoadAsync(documentId).GetAwaiter().GetResult();
        if (existing.Found && existing.Document is not null)
        {
            return existing.Document;
        }

        var document = DocumentEditorDocument.Empty(documentId);
        var sectionId = document.Sections[0].Id;
        document.Metadata.Title = "Canvas performance large document";
        document.Metadata.CreatedAt = CanonicalDemoTimestamp;
        document.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        document.Theme = new DocumentEditorTheme
        {
            BodyFontFamily = "Aptos, Arial, sans-serif",
            BodyFontSize = 11,
            BodyLineHeight = 1.14,
            ParagraphSpacingAfter = 7
        };
        document.PageSettings = new DocumentPageSettings
        {
            Size = DocumentPageSize.A4,
            Margins = new DocumentPageMargins { Top = 64, Right = 72, Bottom = 64, Left = 72 }
        };
        document.Sections[0].Properties.PageSettings = document.PageSettings;

        document.Blocks.Add(new DocumentBlock
        {
            Id = "canvas-phase22-title",
            SectionId = sectionId,
            Type = DocumentBlockType.Heading,
            Order = 0,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Id = "canvas-phase22-title-run", Text = "Canvas performance large document" }]
            }
        });

        for (var index = 0; index < 180; index++)
        {
            var chapter = (index / 12) + 1;
            var paragraphText = string.Create(CultureInfo.InvariantCulture, $"Performance paragraph {index + 1:D3}. Chapter {chapter:D2} keeps enough deterministic words for wrapping, pagination, measurement cache reuse, incremental dirty block tracking, and smooth visible-page virtualization without relying on generated placeholder content. The sentence intentionally repeats stable vocabulary so text measurement cache hits are observable while each block keeps a unique identifier for repaint diagnostics.");
            document.Blocks.Add(TextParagraph(
                sectionId,
                $"canvas-phase22-p{index:D3}",
                10 + index,
                DocumentTextAlignment.Left,
                paragraphText,
                spacingAfter: index % 6 == 5 ? 12 : 7,
                lineSpacing: 1.14));
        }

        StoreDocument(document);
        return document;
    }

    /// <inheritdoc />
    public override async Task<DocumentEditorLoadResult> LoadAsync(
        string documentId,
        DocumentEditorLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (DemoLoadDelay > TimeSpan.Zero)
        {
            await Task.Delay(DemoLoadDelay, cancellationToken);
        }

        if (FailDemoLoads)
        {
            throw new InvalidOperationException("Demo document load failure was requested.");
        }

        if (IsCanvasSeedDocumentId(documentId) && !IsCanvasCollaborationOfflineDocumentId(documentId))
        {
            var localResult = await base.LoadAsync(documentId, options, cancellationToken);
            if (localResult.Found && localResult.Document is not null)
            {
                return localResult;
            }
        }

        if (_http is not null)
        {
            try
            {
                var result = await _http.GetFromJsonAsync<DocumentEditorLoadResult>(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}",
                    cancellationToken);

                if (result?.Document is not null)
                {
                    if (IsCanvasHyphenationAdvancedTablesDocumentId(documentId)
                        && !IsCurrentCanvasHyphenationAdvancedTablesSeed(result.Document))
                    {
                        return await base.LoadAsync(documentId, options, cancellationToken);
                    }

                    return result;
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.LoadAsync(documentId, options, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<DocumentEditorSaveResult> SaveAsync(
        DocumentEditorSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (FailDemoSaves)
        {
            return new DocumentEditorSaveResult
            {
                Success = false,
                ErrorKind = DocumentEditorSaveErrorKind.Recoverable,
                ErrorMessage = "Demo autosave provider failed."
            };
        }

        if (_http is not null)
        {
            try
            {
                var apiRequest = CreateApiSaveRequest(request);
                var response = await _http.PutAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(apiRequest.DocumentId)}",
                    apiRequest,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<DocumentEditorSaveResult>(
                        cancellationToken);
                    if (result is not null)
                    {
                        if (IsCanvasSeedDocumentId(request.DocumentId)
                            && !IsCanvasCollaborationOfflineDocumentId(request.DocumentId))
                        {
                            await MirrorSuccessfulSaveLocallyAsync(request, cancellationToken);
                        }

                        return result;
                    }
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.SaveAsync(request, cancellationToken);
    }

    private static DocumentEditorSaveRequest CreateApiSaveRequest(DocumentEditorSaveRequest request)
    {
        if (!IsCanvasSeedDocumentId(request.DocumentId)
            || IsCanvasCollaborationOfflineDocumentId(request.DocumentId))
        {
            return request;
        }

        return new DocumentEditorSaveRequest
        {
            DocumentId = request.DocumentId,
            Document = request.Document,
            JsonSnapshot = request.JsonSnapshot,
            BaseConcurrencyToken = request.BaseConcurrencyToken,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force,
            Author = request.Author,
            IsAutosave = request.IsAutosave,
            VersionKind = request.VersionKind,
            PreserveImageBlocks = request.PreserveImageBlocks,
            NormalizeJson = request.NormalizeJson
        };
    }

    private static bool IsCanvasHyphenationAdvancedTablesDocumentId(string documentId)
        => string.Equals(documentId, CanvasHyphenationAdvancedTablesDocumentId, StringComparison.Ordinal);

    private static bool IsCanvasSeedDocumentId(string documentId)
        => documentId.StartsWith("phase-", StringComparison.Ordinal)
        && documentId.Contains("-canvas-", StringComparison.Ordinal);

    private static bool IsCanvasCollaborationOfflineDocumentId(string documentId)
        => documentId.StartsWith(CanvasCollaborationOfflineDocumentId, StringComparison.Ordinal);

    private static bool IsCurrentCanvasHyphenationAdvancedTablesSeed(DocumentEditorDocument document)
    {
        var hyphenation = document.Hyphenation;
        var pageBackground = document.PageBackground;
        var watermark = pageBackground?.Watermark;
        var border = pageBackground?.Border;
        return hyphenation?.Enabled == true
            && string.Equals(hyphenation.Mode, "manual", StringComparison.OrdinalIgnoreCase)
            && pageBackground is not null
            && string.Equals(pageBackground.Color, "#f8fafc", StringComparison.OrdinalIgnoreCase)
            && watermark?.Enabled == true
            && string.Equals(watermark.Text, "E12", StringComparison.Ordinal)
            && border?.Enabled == true
            && document.Blocks.Any(block => block.Id == "canvas-e12-advanced-table"
                && block.Content is TableBlockContent table
                && table.Layout.RepeatHeaderRows
                && table.Layout.HeaderRow
                && table.Layout.TotalRow
                && table.Layout.BandedRows
                && table.Layout.Style is not null);
    }

    private async Task MirrorSuccessfulSaveLocallyAsync(DocumentEditorSaveRequest request, CancellationToken cancellationToken)
    {
        var localRequest = new DocumentEditorSaveRequest
        {
            DocumentId = request.DocumentId,
            Document = request.Document,
            JsonSnapshot = request.JsonSnapshot,
            NormalizeJson = request.NormalizeJson,
            PreserveImageBlocks = request.PreserveImageBlocks,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        };

        await base.SaveAsync(localRequest, cancellationToken);
    }

    /// <summary>
    /// Mirrors an API-authoritative comment mutation into the local in-memory store. Canvas seed
    /// documents read comments local-first (<see cref="GetCommentsAsync"/>), so without the mirror a
    /// successful API reply/edit/resolve never becomes visible to local reads such as the shared
    /// comment-provider bridge used by the editor rail.
    /// </summary>
    private async Task MirrorCommentToLocalStoreAsync(
        string documentId,
        DocumentComment comment,
        CancellationToken cancellationToken)
    {
        if (!IsCanvasSeedDocumentId(documentId) || IsCanvasCollaborationOfflineDocumentId(documentId))
        {
            return;
        }

        var local = await base.LoadAsync(documentId, cancellationToken: cancellationToken);
        if (!local.Found || local.Document is null)
        {
            return;
        }

        var clone = System.Text.Json.JsonSerializer.Deserialize<DocumentComment>(
            System.Text.Json.JsonSerializer.Serialize(comment, DocumentEditorJson.Options),
            DocumentEditorJson.Options) ?? comment;
        var index = local.Document.Comments.FindIndex(item => string.Equals(item.Id, clone.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            local.Document.Comments[index] = clone;
        }
        else
        {
            local.Document.Comments.Add(clone);
        }

        await base.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = documentId,
            Document = local.Document,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force
        }, cancellationToken);
    }

    /// <summary>Mirrors an API-side comment delete into the local in-memory store for canvas seed documents.</summary>
    private async Task RemoveCommentFromLocalStoreAsync(
        string documentId,
        string commentId,
        CancellationToken cancellationToken)
    {
        if (!IsCanvasSeedDocumentId(documentId) || IsCanvasCollaborationOfflineDocumentId(documentId))
        {
            return;
        }

        var local = await base.LoadAsync(documentId, cancellationToken: cancellationToken);
        if (!local.Found || local.Document is null)
        {
            return;
        }

        if (local.Document.Comments.RemoveAll(item => string.Equals(item.Id, commentId, StringComparison.Ordinal)) > 0)
        {
            await base.SaveAsync(new DocumentEditorSaveRequest
            {
                DocumentId = documentId,
                Document = local.Document,
                ConcurrencyMode = DocumentEditorConcurrencyMode.Force
            }, cancellationToken);
        }
    }

    /// <inheritdoc />
    public override async Task<DocumentVersion> CreateVersionAsync(
        DocumentVersionCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_http is not null)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(request.DocumentId)}/versions",
                    request,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var version = await response.Content.ReadFromJsonAsync<DocumentVersion>(cancellationToken);
                    if (version is not null)
                    {
                        return version;
                    }
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.CreateVersionAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<DocumentVersion>> GetVersionsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (_http is not null)
        {
            try
            {
                var versions = await _http.GetFromJsonAsync<List<DocumentVersion>>(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/versions",
                    cancellationToken);
                if (versions is not null)
                {
                    return versions;
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.GetVersionsAsync(documentId, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<DocumentComment>> GetCommentsAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        if (IsCanvasSeedDocumentId(documentId))
        {
            var localComments = await base.GetCommentsAsync(documentId, cancellationToken);
            if (localComments.Count > 0)
            {
                return localComments;
            }
        }

        if (_http is not null)
        {
            try
            {
                var comments = await _http.GetFromJsonAsync<List<DocumentComment>>(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/comments",
                    cancellationToken);
                if (comments is not null)
                {
                    return comments;
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.GetCommentsAsync(documentId, cancellationToken);
    }

    /// <inheritdoc />
    /// <summary>Demo rule: entries authored by the client persona (id "client-*") are external.</summary>
    private static void MarkClientEntriesExternal(IEnumerable<DocumentCommentEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.Author?.Id?.StartsWith("client-", StringComparison.OrdinalIgnoreCase) == true)
            {
                entry.IsExternalAuthor = true;
            }
        }
    }

    public override async Task<DocumentComment> CreateCommentAsync(
        string documentId,
        DocumentComment comment,
        CancellationToken cancellationToken = default)
    {
        MarkClientEntriesExternal(comment.Entries);
        if (_http is not null)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/comments",
                    comment,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var created = await response.Content.ReadFromJsonAsync<DocumentComment>(cancellationToken);
                    if (created is not null)
                    {
                        await MirrorCommentToLocalStoreAsync(documentId, created, cancellationToken);
                        return created;
                    }
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.CreateCommentAsync(documentId, comment, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<DocumentComment> AddCommentReplyAsync(
        string documentId,
        string commentId,
        DocumentCommentEntry entry,
        CancellationToken cancellationToken = default)
    {
        MarkClientEntriesExternal([entry]);
        if (_http is not null)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/comments/{Uri.EscapeDataString(commentId)}/replies",
                    entry,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var updated = await response.Content.ReadFromJsonAsync<DocumentComment>(cancellationToken);
                    if (updated is not null)
                    {
                        await MirrorCommentToLocalStoreAsync(documentId, updated, cancellationToken);
                        return updated;
                    }
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.AddCommentReplyAsync(documentId, commentId, entry, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<DocumentComment> UpdateCommentEntryAsync(
        string documentId,
        string commentId,
        string entryId,
        string text,
        DocumentEditorAuthor updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (_http is not null)
        {
            try
            {
                var response = await _http.PutAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/comments/{Uri.EscapeDataString(commentId)}/entries/{Uri.EscapeDataString(entryId)}",
                    new DocumentCommentEntryUpdateRequest { Text = text, UpdatedBy = updatedBy },
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var updated = await response.Content.ReadFromJsonAsync<DocumentComment>(cancellationToken);
                    if (updated is not null)
                    {
                        await MirrorCommentToLocalStoreAsync(documentId, updated, cancellationToken);
                        return updated;
                    }
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.UpdateCommentEntryAsync(documentId, commentId, entryId, text, updatedBy, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<DocumentComment> ResolveCommentAsync(
        string documentId,
        string commentId,
        DocumentEditorAuthor resolvedBy,
        CancellationToken cancellationToken = default)
    {
        if (_http is not null)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/comments/{Uri.EscapeDataString(commentId)}/resolve",
                    resolvedBy,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var updated = await response.Content.ReadFromJsonAsync<DocumentComment>(cancellationToken);
                    if (updated is not null)
                    {
                        await MirrorCommentToLocalStoreAsync(documentId, updated, cancellationToken);
                        return updated;
                    }
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.ResolveCommentAsync(documentId, commentId, resolvedBy, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task<DocumentComment> ReopenCommentAsync(
        string documentId,
        string commentId,
        DocumentEditorAuthor reopenedBy,
        CancellationToken cancellationToken = default)
    {
        if (_http is not null)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/comments/{Uri.EscapeDataString(commentId)}/reopen",
                    reopenedBy,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var updated = await response.Content.ReadFromJsonAsync<DocumentComment>(cancellationToken);
                    if (updated is not null)
                    {
                        await MirrorCommentToLocalStoreAsync(documentId, updated, cancellationToken);
                        return updated;
                    }
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        return await base.ReopenCommentAsync(documentId, commentId, reopenedBy, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task DeleteCommentAsync(
        string documentId,
        string commentId,
        DocumentEditorAuthor deletedBy,
        CancellationToken cancellationToken = default)
    {
        if (_http is not null)
        {
            try
            {
                var response = await _http.PostAsJsonAsync(
                    $"api/document-editor/documents/{Uri.EscapeDataString(documentId)}/comments/{Uri.EscapeDataString(commentId)}/delete",
                    deletedBy,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    await RemoveCommentFromLocalStoreAsync(documentId, commentId, cancellationToken);
                    return;
                }
            }
            catch
            {
                // Demo applications remain usable when the optional Demo API is not running.
            }
        }

        await base.DeleteCommentAsync(documentId, commentId, deletedBy, cancellationToken);
    }

    private static DocumentEditorAuthor DemoAuthor => new()
    {
        Id = "demo-user",
        DisplayName = "Demo User",
        Email = "demo@example.local"
    };

    private static void PrepareContractDemo(DocumentEditorDocument contract)
    {
        contract.Metadata.CreatedAt = CanonicalDemoTimestamp;
        contract.Metadata.ModifiedAt = CanonicalDemoTimestamp;
        contract.Metadata.Author = DemoAuthor;
        contract.Metadata.Status = DocumentEditorStatus.Review;
        contract.Metadata.Description = "Stable engine quality demo document.";

        contract.Assets =
        [
            CreateImageAsset(ContractAssetId, contract.DocumentId, "contract-provider-evidence.png", "Provider-managed exhibit", "Image resolved through IDocumentImageUrlResolver"),
            CreateImageAsset(DemoDocumentImageUrlResolver.ExhibitAssetId, contract.DocumentId, "exhibit-provider-evidence.png", "Provider exhibit", "Provider-backed exhibit image")
        ];

        var clientToken = contract.Blocks
            .SelectMany(GetInlineContent)
            .FirstOrDefault(inline => inline.Id == "contract-client-token");
        if (clientToken is not null)
        {
            clientToken.Marks.Add(new InlineMark
            {
                Type = InlineMarkType.CommentAnchor,
                CommentAnchor = new CommentAnchorMarkData
                {
                    CommentId = "contract-comment-client-token",
                    AnchorId = "contract-comment-client-token-anchor"
                }
            });
        }

        contract.Blocks.Add(CreateParagraph(
            "contract-normal-overview",
            28,
            "The agreement keeps a compact first page with realistic contract text, review markup, image wrapping, captions, an accessibility warning, and a small pricing table. Every block uses stable identifiers so E2E tests can compare the canonical reset without being disturbed by random demo data.",
            spacingAfter: 14));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-left-wrap-image",
            31,
            DocumentImageSource.Url,
            "/document-editor-evidence.svg",
            null,
            "URL evidence preview",
            "Evidence preview loaded from a URL",
            148,
            84,
            DocumentImageAlignment.Start,
            CreateLeftWrappedImageLayout(148, 84, "contract-left-wrap-text")));

        contract.Blocks.Add(CreateParagraph(
            "contract-left-wrap-text",
            32,
            "This paragraph demonstrates a left positioned evidence preview. Text must start beside the image, wrap around its square contour, remain editable on every visual line, and continue below the object without colliding with the caption.",
            spacingAfter: 16));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-right-wrap-image",
            41,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Right aligned appendix preview",
            "Right wrapped exhibit preview",
            148,
            84,
            DocumentImageAlignment.End,
            CreateRightWrappedImageLayout(148, 84, "contract-right-wrap-text")));

        contract.Blocks.Add(CreateParagraph(
            "contract-right-wrap-text",
            42,
            "This paragraph proves the opposite wrap direction. The image is anchored to the same paragraph on the right, while the text remains readable and clickable on the left. The demo intentionally keeps enough words here to exercise multiple wrapped lines.",
            spacingAfter: 16));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-center-wrap-image",
            45,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Centered square evidence preview",
            "Centered square wrapped exhibit preview",
            132,
            74,
            DocumentImageAlignment.Center,
            CreateCenterWrappedImageLayout(132, 74, "contract-center-wrap-text")));

        contract.Blocks.Add(CreateParagraph(
            "contract-center-wrap-text",
            46,
            "This center square scenario deliberately contains enough words to fill both sides of the centered preview. The first lines should split into a left interval and a right interval, continue around the object, and then return to a normal full-width line below the image without becoming a top-and-bottom band.",
            spacingAfter: 16));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-offset-wrap-image",
            48,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Offset square evidence preview",
            "Square image with arbitrary drag-like offset",
            118,
            66,
            DocumentImageAlignment.Start,
            CreateOffsetWrappedImageLayout(118, 66, "contract-offset-wrap-text")));

        contract.Blocks.Add(CreateParagraph(
            "contract-offset-wrap-text",
            49,
            "This paragraph is anchored to an arbitrary drag-like offset rather than a preset left, center, or right alignment. Text should adapt to the actual rectangle position, preserve ordinary word spacing, and remain easy to select before, beside, and after the image.",
            spacingAfter: 16));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-top-bottom-image",
            50,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Provider-managed exhibit",
            "Image resolved through IDocumentImageUrlResolver",
            220,
            124,
            DocumentImageAlignment.Center,
            CreateTopBottomImageLayout(220, 124, "contract-top-bottom-text")));

        contract.Blocks.Add(CreateParagraph(
            "contract-top-bottom-text",
            51,
            "Top and bottom wrapping should reserve the full object band. No text line is allowed to slide horizontally through this image because that would make the page feel unpredictable, especially after saving, resetting, and reloading the demo document.",
            spacingAfter: 16));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-tight-wrap-image",
            52,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Tight contour evidence preview",
            "Tight wrapped image with a custom diamond contour",
            128,
            72,
            DocumentImageAlignment.Center,
            CreateTightWrappedImageLayout(128, 72, "contract-tight-wrap-text")));

        contract.Blocks.Add(CreateParagraph(
            "contract-tight-wrap-text",
            53,
            "This tight wrapping paragraph uses a custom diamond contour so the available line intervals differ from a plain rectangle. The text is intentionally stable and descriptive, giving E2E checks predictable words while proving that polygon metadata can survive save, reload, and DOCX export workflows.",
            spacingAfter: 16));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-in-front-image",
            54,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "In front floating evidence badge",
            "In front of text object positioned in the page margin",
            96,
            54,
            DocumentImageAlignment.End,
            CreateInFrontImageLayout(96, 54, "contract-layering-text")));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-behind-text-image",
            54.25,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Behind text evidence watermark",
            "Behind text object positioned in the page margin",
            96,
            54,
            DocumentImageAlignment.End,
            CreateBehindTextImageLayout(96, 54, "contract-layering-text")));

        contract.Blocks.Add(CreateParagraph(
            "contract-layering-text",
            54.5,
            "This layering scenario keeps one image in front of text and another behind text without hiding the paragraph itself. Both badges are deliberately positioned in the page margin so the demo exercises front and behind layer serialization while keeping the contract copy readable and overlap-free.",
            spacingAfter: 16));

        contract.Blocks.Add(CreatePageBreak("contract-engine-scenarios-page-break", 55));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-inline-image",
            60,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Inline evidence thumbnail",
            "Inline evidence image with caption",
            156,
            88,
            DocumentImageAlignment.Center,
            DocumentObjectLayout.Inline()));

        contract.Blocks.Add(CreateImageDrawingParagraph(
            "contract-missing-alt-image",
            70,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            null,
            "Accessibility sample: missing alt text",
            156,
            88,
            DocumentImageAlignment.Center,
            DocumentObjectLayout.Inline()));

        contract.Blocks.Add(CreateContractTable());
        AddContractHeaderFooterDrawingRuns(contract);
        contract.Comments.Add(CreateCanonicalComment());
        AddCanonicalDeletionRevision(contract);
        DocumentImagePersistence.Sanitize(contract);
    }

    private static DocumentEditorDocument CreateExhibitsDocument(string documentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        document.Metadata.Title = "Evidence exhibit";
        document.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Heading,
            Order = 10,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Text = "Evidence exhibit" }]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Text = "This demo document keeps image drawing runs in the editor JSON model." }]
            }
        });
        document.Blocks.Add(CreateImageDrawingParagraph(
            "exhibits-url-image",
            30,
            DocumentImageSource.Asset,
            null,
            DemoDocumentImageUrlResolver.ExhibitAssetId,
            "Provider exhibit",
            "Image inserted from the demo provider",
            220,
            124,
            DocumentImageAlignment.Start,
            CreateLeftWrappedImageLayout(220, 124),
            sectionId: null));
        document.Blocks.Add(CreateImageDrawingParagraph(
            "exhibits-provider-image",
            40,
            DocumentImageSource.Asset,
            null,
            DemoDocumentImageUrlResolver.ExhibitAssetId,
            "Provider exhibit",
            "Image resolved through the demo image provider",
            240,
            135,
            DocumentImageAlignment.Center,
            CreateTopBottomImageLayout(240, 135),
            sectionId: null));
        DocumentImagePersistence.Sanitize(document);
        return document;
    }

    private static DocumentObjectLayout CreateLeftWrappedImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = DocumentImageHorizontalPosition.Left
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.Square,
                DistanceRight = 16,
                DistanceBottom = 12
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentObjectLayout CreateRightWrappedImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = DocumentImageHorizontalPosition.Right
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.Square,
                DistanceLeft = 16,
                DistanceBottom = 12
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentObjectLayout CreateCenterWrappedImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = DocumentImageHorizontalPosition.Center,
                Y = 0
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.Square,
                DistanceLeft = 10,
                DistanceRight = 10,
                DistanceBottom = 10
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentObjectLayout CreateOffsetWrappedImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                X = 214,
                Y = 4
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.Square,
                DistanceLeft = 10,
                DistanceRight = 12,
                DistanceBottom = 10
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentObjectLayout CreateTopBottomImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = DocumentImageHorizontalPosition.Center
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.TopBottom,
                DistanceTop = 10,
                DistanceBottom = 12
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentObjectLayout CreateTightWrappedImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = true,
                FixedOnPage = false
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = DocumentImageHorizontalPosition.Center
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.Tight,
                Side = DocumentObjectWrapSide.Largest,
                DistanceLeft = 8,
                DistanceRight = 8,
                DistanceTop = 4,
                DistanceBottom = 8,
                WrapContourPoints =
                [
                    new() { X = 0.5, Y = 0 },
                    new() { X = 1, Y = 0.45 },
                    new() { X = 0.62, Y = 1 },
                    new() { X = 0, Y = 0.55 }
                ]
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static DocumentObjectLayout CreateInFrontImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Fixed,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = false,
                FixedOnPage = true
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Page,
                X = -124,
                Y = 250
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.InFrontOfText
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            },
            Stacking = new DocumentObjectStacking
            {
                ZIndex = 20,
                AllowOverlap = true
            }
        };

    private static DocumentObjectLayout CreateBehindTextImageLayout(double width, double height, string? anchorBlockId = null) =>
        new()
        {
            Kind = DocumentObjectLayoutKind.Fixed,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = anchorBlockId,
                MoveWithText = false,
                FixedOnPage = true
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Page,
                VerticalRelativeTo = DocumentRelativePosition.Page,
                X = -124,
                Y = 318
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.BehindText
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            },
            Stacking = new DocumentObjectStacking
            {
                ZIndex = 0,
                AllowOverlap = true
            }
        };

    private static DocumentBlock CreateParagraph(string id, double order, string text, double spacingAfter = 10) =>
        new()
        {
            Id = id,
            SectionId = "contract-section-main",
            Type = DocumentBlockType.Paragraph,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Left,
                LineSpacing = 1.25,
                SpacingAfter = spacingAfter
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = $"{id}-text",
                        Text = text
                    }
                ]
            }
        };

    private static DocumentBlock TextParagraph(
        string sectionId,
        string id,
        double order,
        DocumentTextAlignment alignment,
        string text,
        double spacingBefore = 0,
        double spacingAfter = 8,
        double leftIndent = 0,
        double rightIndent = 0,
        double firstLineIndent = 0,
        double lineSpacing = 1.18) =>
        new()
        {
            Id = id,
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = alignment,
                LineSpacing = lineSpacing,
                SpacingBefore = spacingBefore,
                SpacingAfter = spacingAfter,
                LeftIndent = leftIndent,
                RightIndent = rightIndent,
                FirstLineIndent = firstLineIndent
            },
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun
                    {
                        Id = $"{id}-run",
                        Text = text
                    }
                ]
            }
        };

    private static List<TableRowContent> CreateCanvasE12Rows(string sectionId)
    {
        var rows = new List<TableRowContent>
        {
            new()
            {
                Cells =
                [
                    CreateCanvasTableCell(sectionId, "canvas-e12-table-h-item", "Item", true, "#dbeafe", DocumentTextAlignment.Center),
                    CreateCanvasTableCell(sectionId, "canvas-e12-table-h-owner", "Owner", true, "#dbeafe", DocumentTextAlignment.Center),
                    CreateCanvasTableCell(sectionId, "canvas-e12-table-h-score", "Score", true, "#dbeafe", DocumentTextAlignment.Center)
                ]
            }
        };

        for (var index = 1; index <= 28; index++)
        {
            rows.Add(new TableRowContent
            {
                Cells =
                [
                    CreateCanvasTableCell(sectionId, $"canvas-e12-table-r{index}-item", $"Milestone {index}", false, "#ffffff", DocumentTextAlignment.Left),
                    CreateCanvasTableCell(sectionId, $"canvas-e12-table-r{index}-owner", index % 2 == 0 ? "Design" : "Engineering", false, "#ffffff", DocumentTextAlignment.Left),
                    CreateCanvasTableCell(sectionId, $"canvas-e12-table-r{index}-score", (index + 2).ToString(CultureInfo.InvariantCulture), false, "#ffffff", DocumentTextAlignment.Center, TableCellVerticalAlignment.Middle)
                ]
            });
        }

        rows.Add(new TableRowContent
        {
            Cells =
            [
                CreateCanvasTableCell(sectionId, "canvas-e12-table-total-label", "Total", false, "#dcfce7", DocumentTextAlignment.Left),
                CreateCanvasTableCell(sectionId, "canvas-e12-table-total-owner", "Formula", false, "#dcfce7", DocumentTextAlignment.Left),
                CreateCanvasTableCell(sectionId, "canvas-e12-table-total-score", "462", false, "#dcfce7", DocumentTextAlignment.Center, TableCellVerticalAlignment.Middle)
            ]
        });

        return rows;
    }

    private static IReadOnlyList<DocumentHeaderFooter> CreatePhase16HeadersFooters(string sectionId) =>
    [
        HeaderFooter(sectionId, "canvas-phase16-header-primary", DocumentHeaderFooterType.Header, DocumentHeaderFooterScope.Primary,
            "canvas-phase16-header-primary-block", DocumentTextAlignment.Left,
            [
                new DocumentFieldRun { Id = "canvas-phase16-header-title-field", FieldType = DocumentFieldType.DocumentTitle, FallbackText = "Document title" },
                new TextRun { Id = "canvas-phase16-header-primary-spacer", Text = "  |  " },
                new DocumentFieldRun { Id = "canvas-phase16-header-date-field", FieldType = DocumentFieldType.Date, Format = "yyyy-MM-dd", FallbackText = "2026-06-04" }
            ]),
        HeaderFooter(sectionId, "canvas-phase16-header-first", DocumentHeaderFooterType.Header, DocumentHeaderFooterScope.FirstPage,
            "canvas-phase16-header-first-block", DocumentTextAlignment.Center,
            [
                new TextRun { Id = "canvas-phase16-header-first-text", Text = "First page header" },
                new TextRun { Id = "canvas-phase16-header-first-spacer", Text = " - " },
                new DocumentFieldRun { Id = "canvas-phase16-header-author-field", FieldType = DocumentFieldType.Author, FallbackText = "Author" }
            ]),
        HeaderFooter(sectionId, "canvas-phase16-header-even", DocumentHeaderFooterType.Header, DocumentHeaderFooterScope.EvenPages,
            "canvas-phase16-header-even-block", DocumentTextAlignment.Left,
            [new TextRun { Id = "canvas-phase16-header-even-text", Text = "Even page header" }]),
        HeaderFooter(sectionId, "canvas-phase16-header-odd", DocumentHeaderFooterType.Header, DocumentHeaderFooterScope.OddPages,
            "canvas-phase16-header-odd-block", DocumentTextAlignment.Right,
            [new TextRun { Id = "canvas-phase16-header-odd-text", Text = "Odd page header" }]),
        HeaderFooter(sectionId, "canvas-phase16-footer-primary", DocumentHeaderFooterType.Footer, DocumentHeaderFooterScope.Primary,
            "canvas-phase16-footer-primary-block", DocumentTextAlignment.Center,
            [
                new TextRun { Id = "canvas-phase16-footer-page-label", Text = "Page " },
                new DocumentFieldRun { Id = "canvas-phase16-footer-page-xofy", FieldType = DocumentFieldType.PageXOfY, FallbackText = "1 / 1" }
            ]),
        HeaderFooter(sectionId, "canvas-phase16-footer-first", DocumentHeaderFooterType.Footer, DocumentHeaderFooterScope.FirstPage,
            "canvas-phase16-footer-first-block", DocumentTextAlignment.Center,
            [new TextRun { Id = "canvas-phase16-footer-first-text", Text = "First page footer" }]),
        HeaderFooter(sectionId, "canvas-phase16-footer-even", DocumentHeaderFooterType.Footer, DocumentHeaderFooterScope.EvenPages,
            "canvas-phase16-footer-even-block", DocumentTextAlignment.Center,
            [
                new TextRun { Id = "canvas-phase16-footer-even-label", Text = "Even " },
                new DocumentFieldRun { Id = "canvas-phase16-footer-even-page", FieldType = DocumentFieldType.PageNumber, FallbackText = "2" },
                new TextRun { Id = "canvas-phase16-footer-even-separator", Text = "/" },
                new DocumentFieldRun { Id = "canvas-phase16-footer-even-count", FieldType = DocumentFieldType.PageCount, FallbackText = "2" }
            ]),
        HeaderFooter(sectionId, "canvas-phase16-footer-odd", DocumentHeaderFooterType.Footer, DocumentHeaderFooterScope.OddPages,
            "canvas-phase16-footer-odd-block", DocumentTextAlignment.Center,
            [
                new TextRun { Id = "canvas-phase16-footer-odd-label", Text = "Odd " },
                new DocumentFieldRun { Id = "canvas-phase16-footer-odd-page", FieldType = DocumentFieldType.PageNumber, FallbackText = "1" },
                new TextRun { Id = "canvas-phase16-footer-odd-separator", Text = "/" },
                new DocumentFieldRun { Id = "canvas-phase16-footer-odd-count", FieldType = DocumentFieldType.PageCount, FallbackText = "1" }
            ])
    ];

    private static DocumentHeaderFooter HeaderFooter(
        string sectionId,
        string id,
        DocumentHeaderFooterType type,
        DocumentHeaderFooterScope scope,
        string blockId,
        DocumentTextAlignment alignment,
        IReadOnlyList<InlineContent> inlines) =>
        new()
        {
            Id = id,
            Type = type,
            Scope = scope,
            SectionId = sectionId,
            Blocks =
            [
                new DocumentBlock
                {
                    Id = blockId,
                    SectionId = sectionId,
                    Type = DocumentBlockType.Paragraph,
                    Order = 10,
                    ParagraphProperties = new DocumentParagraphProperties
                    {
                        Alignment = alignment,
                        LineSpacing = 1.05,
                        SpacingAfter = 0
                    },
                    Content = new ParagraphBlockContent { Inlines = [.. inlines] }
                }
            ]
        };

    private static TableCellContent CreateCanvasHistoryCell(string id, string text, bool isHeader = false) =>
        new()
        {
            Id = id,
            IsHeader = isHeader,
            BackgroundColor = isHeader ? "#eef2ff" : "#ffffff",
            Padding = 7,
            Blocks =
            [
                new DocumentBlock
                {
                    Id = $"{id}-paragraph",
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines =
                        [
                            new TextRun
                            {
                                Id = $"{id}-run",
                                Text = text
                            }
                        ]
                    }
                }
            ]
        };

    private static TableCellContent CreateCanvasTableCell(
        string sectionId,
        string id,
        string text,
        bool isHeader,
        string backgroundColor,
        DocumentTextAlignment alignment,
        TableCellVerticalAlignment verticalAlignment = TableCellVerticalAlignment.Top) =>
        new()
        {
            Id = id,
            IsHeader = isHeader,
            BackgroundColor = backgroundColor,
            Padding = 7,
            VerticalAlignment = verticalAlignment,
            Borders = new TableCellBorders
            {
                Top = "#94a3b8",
                Right = "#94a3b8",
                Bottom = "#94a3b8",
                Left = "#94a3b8"
            },
            Blocks =
            [
                TextParagraph(
                    sectionId,
                    $"{id}-paragraph",
                    0,
                    alignment,
                    text,
                    spacingAfter: 0,
                    lineSpacing: 1.08)
            ]
        };

    private static DocumentBlock CreatePageBreak(string id, double order) =>
        new()
        {
            Id = id,
            SectionId = "contract-section-main",
            Type = DocumentBlockType.PageBreak,
            Order = order
        };

    private static DocumentBlock CreateImageDrawingParagraph(
        string id,
        double order,
        DocumentImageSource source,
        string? url,
        string? assetId,
        string? altText,
        string caption,
        double width,
        double height,
        DocumentImageAlignment alignment,
        DocumentObjectLayout layout,
        string? sectionId = "contract-section-main")
    {
        var drawing = CreateImageDrawingRun(id, source, url, assetId, altText, caption, width, height, layout);
        return new DocumentBlock
        {
            Id = id,
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = ToTextAlignment(alignment),
                LineSpacing = 1.25,
                SpacingAfter = 9
            },
            Content = new ParagraphBlockContent
            {
                Inlines = [drawing]
            }
        };
    }

    private static DocumentBlock CreateDrawingParagraph(
        string sectionId,
        string objectId,
        double order,
        DocumentDrawingKind kind,
        double width,
        double height,
        DocumentObjectLayout layout,
        DocumentDrawingShape? shape = null,
        DocumentDrawingTextBody? textBody = null,
        DocumentDrawingChart? chart = null,
        DocumentDrawingGroup? group = null)
    {
        layout.Anchor ??= new DocumentObjectAnchor();
        layout.Position ??= new DocumentObjectPosition();
        layout.Wrap ??= new DocumentObjectWrap();
        layout.Transform ??= new DocumentObjectTransform();
        layout.Stacking ??= new DocumentObjectStacking();
        layout.Transform.Width ??= width;
        layout.Transform.Height ??= height;
        layout.Transform.NaturalWidth ??= width;
        layout.Transform.NaturalHeight ??= height;

        var drawing = new DocumentDrawingRun
        {
            Id = $"{objectId}-drawing",
            ObjectId = objectId,
            Kind = kind,
            AltText = kind == DocumentDrawingKind.Chart ? chart?.Title : null,
            Caption = string.Empty,
            Size = new DocumentImageSize { Width = width, Height = height },
            NaturalSize = new DocumentImageSize { Width = width, Height = height },
            Layout = layout,
            Shape = shape,
            TextBody = textBody,
            Chart = chart,
            Group = group
        };
        DocumentImagePersistence.Sanitize(drawing);

        return new DocumentBlock
        {
            Id = $"{objectId}-paragraph",
            SectionId = sectionId,
            Type = DocumentBlockType.Paragraph,
            Order = order,
            ParagraphProperties = new DocumentParagraphProperties
            {
                Alignment = DocumentTextAlignment.Left,
                LineSpacing = 1.16,
                SpacingAfter = 8
            },
            Content = new ParagraphBlockContent
            {
                Inlines = [drawing]
            }
        };
    }

    private static DocumentObjectLayout CreateDrawingLayout(
        string anchorBlockId,
        DocumentWrapMode wrapMode,
        double x,
        double y,
        int zIndex)
        => new()
        {
            Kind = wrapMode == DocumentWrapMode.Inline ? DocumentObjectLayoutKind.Inline : DocumentObjectLayoutKind.Anchored,
            Anchor =
            {
                BlockId = anchorBlockId,
                Offset = 0,
                MoveWithText = true,
                LockAnchor = false
            },
            Wrap =
            {
                Mode = wrapMode,
                DistanceLeft = 12,
                DistanceRight = 12,
                DistanceTop = 8,
                DistanceBottom = 8
            },
            Position =
            {
                X = x,
                Y = y,
                HorizontalAlignment = DocumentImageHorizontalPosition.Left,
                VerticalAlignment = DocumentObjectVerticalAlignment.Top
            },
            Stacking =
            {
                AllowOverlap = true,
                ZIndex = zIndex
            }
        };

    private static DocumentDrawingRun CreateImageDrawingRun(
        string objectId,
        DocumentImageSource source,
        string? url,
        string? assetId,
        string? altText,
        string caption,
        double width,
        double height,
        DocumentObjectLayout layout)
    {
        layout.Anchor ??= new DocumentObjectAnchor();
        layout.Position ??= new DocumentObjectPosition();
        layout.Wrap ??= new DocumentObjectWrap();
        layout.Transform ??= new DocumentObjectTransform();
        layout.Stacking ??= new DocumentObjectStacking();
        layout.Anchor.BlockId = string.IsNullOrWhiteSpace(layout.Anchor.BlockId) ? objectId : layout.Anchor.BlockId;
        layout.Anchor.InlineIndex ??= 0;
        layout.Anchor.Offset ??= 0;
        layout.Transform.Width ??= width;
        layout.Transform.Height ??= height;
        layout.Transform.NaturalWidth ??= width;
        layout.Transform.NaturalHeight ??= height;

        var drawing = new DocumentDrawingRun
        {
            Id = $"{objectId}-drawing",
            ObjectId = objectId,
            Source = source,
            Url = source == DocumentImageSource.Url ? url : null,
            AssetId = source == DocumentImageSource.Asset ? assetId : null,
            AltText = altText,
            Caption = caption,
            Size = new DocumentImageSize { Width = width, Height = height },
            NaturalSize = new DocumentImageSize { Width = width, Height = height },
            Layout = layout
        };
        DocumentImagePersistence.Sanitize(drawing);
        return drawing;
    }

    private static DocumentTextAlignment ToTextAlignment(DocumentImageAlignment alignment)
        => alignment switch
        {
            DocumentImageAlignment.Center => DocumentTextAlignment.Center,
            DocumentImageAlignment.End => DocumentTextAlignment.Right,
            _ => DocumentTextAlignment.Left
        };

    private static DocumentBlock CreateContractTable() =>
        new()
        {
            Id = "contract-pricing-table",
            SectionId = "contract-section-main",
            Type = DocumentBlockType.Table,
            Order = 80,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent
                {
                    Width = 560,
                    Alignment = TableHorizontalAlignment.Center,
                    CellPadding = 7,
                    BackgroundColor = "#ffffff",
                    Borders = new TableCellBorders
                    {
                        Top = "1px solid #cbd5e1",
                        Right = "1px solid #cbd5e1",
                        Bottom = "1px solid #cbd5e1",
                        Left = "1px solid #cbd5e1"
                    }
                },
                Rows =
                [
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateTableCell("Item", isHeader: true, backgroundColor: "#eef2ff", id: "contract-pricing-table-h-item"),
                            CreateTableCell("Responsibility", isHeader: true, backgroundColor: "#eef2ff", id: "contract-pricing-table-h-responsibility"),
                            CreateTableCell("Status", isHeader: true, backgroundColor: "#eef2ff", id: "contract-pricing-table-h-status"),
                            CreateTableCell("Evidence", isHeader: true, backgroundColor: "#eef2ff", id: "contract-pricing-table-h-evidence")
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateTableCell("Implementation", id: "contract-pricing-table-r1-item"),
                            CreateTableCell("Provider", id: "contract-pricing-table-r1-responsibility"),
                            CreateTableCell("Ready for review", id: "contract-pricing-table-r1-status"),
                            CreateTableCellWithImage("contract-pricing-table", "contract-pricing-table-r1-evidence")
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateTableCell("Client data", id: "contract-pricing-table-r2-item"),
                            CreateTableCell("Client", id: "contract-pricing-table-r2-responsibility"),
                            CreateTableCell("Pending confirmation", id: "contract-pricing-table-r2-status"),
                            CreateTableCell("Awaiting upload", id: "contract-pricing-table-r2-evidence")
                        ]
                    }
                ]
            }
        };

    private static TableCellContent CreateTableCellWithImage(string tableId, string cellId)
    {
        const string blockId = "contract-pricing-table-r1-evidence-block";
        var drawing = CreateImageDrawingRun(
            "contract-table-cell-image",
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            "Table cell evidence image",
            "Image wrapped inside a pricing table cell",
            76,
            43,
            CreateTableCellImageLayout(tableId, cellId, blockId, 76, 43));
        drawing.Layout.Anchor.Region = DocumentRenditionAnchorScope.TableCell;
        drawing.Layout.Anchor.TableId = tableId;
        drawing.Layout.Anchor.CellId = cellId;
        drawing.Layout.Anchor.BlockId = blockId;
        drawing.Docx = new DocumentDocxDrawingMetadata { LayoutInCell = true };

        return new TableCellContent
        {
            Id = cellId,
            Padding = 8,
            Blocks =
            [
                new DocumentBlock
                {
                    Id = blockId,
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines =
                        [
                            drawing,
                            new TextRun
                            {
                                Id = "contract-pricing-table-r1-evidence-text",
                                Text = " Stable table-cell image text proves local wrapping without affecting neighboring cells."
                            }
                        ]
                    }
                }
            ]
        };
    }

    private static DocumentObjectLayout CreateTableCellImageLayout(string tableId, string cellId, string blockId, double width, double height)
        => new()
        {
            Kind = DocumentObjectLayoutKind.Anchored,
            Anchor = new DocumentObjectAnchor
            {
                BlockId = blockId,
                Region = DocumentRenditionAnchorScope.TableCell,
                TableId = tableId,
                CellId = cellId,
                MoveWithText = true
            },
            Position = new DocumentObjectPosition
            {
                HorizontalRelativeTo = DocumentRelativePosition.Column,
                VerticalRelativeTo = DocumentRelativePosition.Paragraph,
                HorizontalAlignment = DocumentImageHorizontalPosition.Left
            },
            Wrap = new DocumentObjectWrap
            {
                Mode = DocumentWrapMode.Square,
                DistanceRight = 6,
                DistanceBottom = 4
            },
            Transform = new DocumentObjectTransform
            {
                Width = width,
                Height = height,
                NaturalWidth = width,
                NaturalHeight = height,
                LockAspectRatio = true
            }
        };

    private static void AddContractHeaderFooterDrawingRuns(DocumentEditorDocument document)
    {
        AddDrawingRunToHeaderFooter(
            document,
            "contract-header-primary",
            "contract-header-primary-block",
            CreateHeaderFooterDrawingRun(
                "contract-header-logo-image",
                "Header logo evidence",
                DocumentRenditionAnchorScope.Header,
                "contract-header-primary",
                "contract-header-primary-block",
                52,
                29));
        AddDrawingRunToHeaderFooter(
            document,
            "contract-footer-primary",
            "contract-footer-primary-block",
            CreateHeaderFooterDrawingRun(
                "contract-footer-logo-image",
                "Footer logo evidence",
                DocumentRenditionAnchorScope.Footer,
                "contract-footer-primary",
                "contract-footer-primary-block",
                44,
                25));
    }

    private static DocumentDrawingRun CreateHeaderFooterDrawingRun(
        string objectId,
        string altText,
        DocumentRenditionAnchorScope region,
        string headerFooterId,
        string blockId,
        double width,
        double height)
    {
        var drawing = CreateImageDrawingRun(
            objectId,
            DocumentImageSource.Asset,
            null,
            ContractAssetId,
            altText,
            altText,
            width,
            height,
            DocumentObjectLayout.Inline());
        drawing.Layout.Anchor.Region = region;
        drawing.Layout.Anchor.HeaderFooterId = headerFooterId;
        drawing.Layout.Anchor.BlockId = blockId;
        return drawing;
    }

    private static void AddDrawingRunToHeaderFooter(
        DocumentEditorDocument document,
        string headerFooterId,
        string blockId,
        DocumentDrawingRun drawing)
    {
        var paragraph = document.HeadersFooters
            .FirstOrDefault(headerFooter => string.Equals(headerFooter.Id, headerFooterId, StringComparison.Ordinal))
            ?.Blocks.FirstOrDefault(block => string.Equals(block.Id, blockId, StringComparison.Ordinal))
            ?.Content as ParagraphBlockContent;
        if (paragraph is null)
        {
            return;
        }

        drawing.Layout.Anchor.InlineIndex = paragraph.Inlines.Count;
        paragraph.Inlines.Add(drawing);
    }

    private static DocumentComment CreateCanonicalComment() =>
        new()
        {
            Id = "contract-comment-client-token",
            Anchor = new DocumentCommentAnchor
            {
                Type = DocumentCommentAnchorType.TextRange,
                BlockId = "contract-intro",
                StartInlineIndex = 1,
                EndInlineIndex = 1,
                StartOffset = 0,
                EndOffset = "Client name".Length,
                ExternalAnchorId = "contract-comment-client-token-anchor"
            },
            Visibility = DocumentCommentVisibility.Internal,
            Entries =
            [
                new DocumentCommentEntry
                {
                    Id = "contract-comment-client-token-entry-1",
                    Author = DemoAuthor,
                    Text = "Check whether the client token is resolved before export.",
                    CreatedAt = CanonicalDemoTimestamp
                }
            ]
        };

    private static void AddCanonicalDeletionRevision(DocumentEditorDocument contract)
    {
        var scope = contract.Blocks.FirstOrDefault(block => block.Id == "contract-scope");
        if (scope?.Content is not ParagraphBlockContent paragraph)
        {
            return;
        }

        paragraph.Inlines.Add(new TextRun
        {
            Id = "contract-scope-deleted-run",
            Text = " Legacy onboarding language will be removed.",
            Marks =
            [
                new InlineMark
                {
                    Type = InlineMarkType.Revision,
                    RevisionId = "contract-revision-deletion",
                    Value = "Deletion"
                }
            ]
        });

        contract.Revisions.Add(new DocumentRevision
        {
            Id = "contract-revision-deletion",
            Type = DocumentRevisionType.Deletion,
            Range = new DocumentRevisionRange
            {
                BlockId = "contract-scope",
                StartInlineIndex = paragraph.Inlines.Count - 1,
                EndInlineIndex = paragraph.Inlines.Count - 1,
                StartOffset = 0,
                EndOffset = " Legacy onboarding language will be removed.".Length
            },
            Author = new DocumentRevisionAuthor
            {
                Id = "demo-reviewer",
                DisplayName = "Demo Reviewer",
                Email = "reviewer@example.local"
            },
            CreatedAt = CanonicalDemoTimestamp.AddMinutes(5),
            Action = DocumentRevisionAction.Pending,
            PayloadJson = "Legacy onboarding language will be removed."
        });
    }

    private static DocumentImageAsset CreateImageAsset(
        string id,
        string documentId,
        string fileName,
        string altText,
        string caption)
    {
        var bytes = DecodeDataUri(DemoImageUrl);
        return new DocumentImageAsset
        {
            Id = id,
            DocumentId = documentId,
            Source = DocumentImageSource.Asset,
            ContentType = "image/png",
            FileName = fileName,
            SizeBytes = bytes.LongLength,
            AltText = altText,
            Caption = caption,
            ImageSize = new DocumentImageSize { Width = 240, Height = 135 }
        };
    }

    private static DocumentVersion CreateCanonicalContractVersion(DocumentEditorDocument contract)
    {
        var json = DocumentEditorJson.Serialize(contract);
        var snapshot = new DocumentVersionSnapshot
        {
            DocumentId = contract.DocumentId,
            SchemaVersion = contract.SchemaVersion,
            Json = json
        };
        snapshot.Hash = DocumentVersionHashHelper.ComputeSnapshotHash(snapshot);

        return new DocumentVersion
        {
            Id = "contract-version-1-0",
            DocumentId = contract.DocumentId,
            Kind = DocumentVersionKind.Major,
            Label = "1.0",
            Description = "Initial demo version",
            Author = DemoAuthor,
            CreatedAt = CanonicalDemoTimestamp,
            Snapshot = snapshot
        };
    }

    private static IEnumerable<InlineContent> GetInlineContent(DocumentBlock block)
        => block.Content switch
        {
            ParagraphBlockContent paragraph => paragraph.Inlines,
            HeadingBlockContent heading => heading.Inlines,
            ListBlockContent list => list.Inlines,
            QuoteBlockContent quote => quote.Inlines,
            _ => []
        };

    private static byte[] DecodeDataUri(string dataUri)
    {
        const string marker = "base64,";
        var index = dataUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? []
            : Convert.FromBase64String(dataUri[(index + marker.Length)..]);
    }

    private static DocumentEditorDocument CreateTablePropertiesDocument(string documentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        document.Metadata.Title = "Table properties demo";
        document.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Heading,
            Order = 10,
            Content = new HeadingBlockContent
            {
                Level = 1,
                Inlines = [new TextRun { Text = "Table properties demo" }]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Paragraph,
            Order = 20,
            Content = new ParagraphBlockContent
            {
                Inlines = [new TextRun { Text = "Select a table cell to open row, column, table, and cell property controls." }]
            }
        });
        document.Blocks.Add(new DocumentBlock
        {
            Type = DocumentBlockType.Table,
            Order = 30,
            Content = new TableBlockContent
            {
                Layout = new TableLayoutContent
                {
                    Width = 640,
                    Alignment = TableHorizontalAlignment.Center,
                    CellPadding = 8,
                    BackgroundColor = "#f8fafc",
                    Borders = new TableCellBorders
                    {
                        Top = "1px solid #94a3b8",
                        Right = "1px solid #94a3b8",
                        Bottom = "1px solid #94a3b8",
                        Left = "1px solid #94a3b8"
                    }
                },
                Rows =
                [
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateTableCell("Feature", isHeader: true, backgroundColor: "#e2e8f0"),
                            CreateTableCell("Demo value", isHeader: true, backgroundColor: "#e2e8f0"),
                            CreateTableCell("UX check", isHeader: true, backgroundColor: "#e2e8f0")
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateTableCell("Width"),
                            CreateTableCell("640 px"),
                            CreateTableCell("Resize from the properties panel")
                        ]
                    },
                    new TableRowContent
                    {
                        Cells =
                        [
                            CreateTableCell("Alignment"),
                            CreateTableCell("Centered"),
                            CreateTableCell("Switch left, center, or right")
                        ]
                    }
                ]
            }
        });
        return document;
    }

    private static TableCellContent CreateTableCell(string text, bool isHeader = false, string? backgroundColor = null, string? id = null)
    {
        return new TableCellContent
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            IsHeader = isHeader,
            BackgroundColor = backgroundColor,
            Padding = 8,
            Blocks =
            [
                new DocumentBlock
                {
                    Id = id is null ? Guid.NewGuid().ToString("N") : $"{id}-block",
                    Type = DocumentBlockType.Paragraph,
                    Content = new ParagraphBlockContent
                    {
                        Inlines = [new TextRun { Id = id is null ? null : $"{id}-text", Text = text }]
                    }
                }
            ]
        };
    }
}
