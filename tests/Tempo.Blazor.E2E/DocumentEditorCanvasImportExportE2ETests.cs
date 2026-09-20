using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentFormats;
using Tempo.Blazor.DocumentFormats.Docx;
using Tempo.Blazor.E2E.CanvasEngine;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 19 E2E coverage for canvas-backed provider import/export boundaries.</summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasImportExportE2ETests : WasmTestBase
{
    private const string Phase12DocumentId = "phase-12-canvas-history-save";
    private const string Phase16DocumentId = "phase-16-canvas-headers-footers-notes";
    private const string PhaseE1NumberingListsDocumentId = "phase-e1-canvas-numbering-lists";
    private const string PhaseE3SectionsColumnsDocumentId = "phase-e3-canvas-sections-columns";
    private const string PhaseE4StylesDocumentId = "phase-e4-canvas-styles";
    private const string PhaseE5FieldsDocumentId = "phase-e5-canvas-fields";
    private const string PhaseE8MathEquationsDocumentId = "phase-e8-canvas-math-equations";
    private const string PhaseE9ContentControlsDocumentId = "phase-e9-canvas-content-controls";

    [TestMethod]
    public async Task Phase19_CanvasDocxPdfImportExport_UsesCurrentCanvasModelAndKeepsFirstPaintSane()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        var marker = $"Phase19export{DateTimeOffset.UtcNow:HHmmssfff}";
        var output = CreateOutputDirectory(nameof(Phase19_CanvasDocxPdfImportExport_UsesCurrentCanvasModelAndKeepsFirstPaintSane));

        await OpenCanvasDocumentAsync(page, Phase12DocumentId);
        await ClickCanvasBlockAsync(page, "canvas-history-text", await ReadBlockEndOffsetAsync(page, "canvas-history-text"));
        await FocusHiddenCanvasInputAsync(page);
        await page.Keyboard.TypeAsync($" {marker}");
        await WaitForA11yTextAsync(page, marker);

        var docxPath = await ExportDocxThroughToolbarAsync(page);
        var imported = await ImportDownloadedDocxAsync(docxPath);
        GetDocumentText(imported.Document).Should().Contain(marker);
        imported.Document.Blocks.Select(block => block.Content).OfType<TableBlockContent>().Should().NotBeEmpty();
        HasImageContent(imported.Document.Blocks).Should().BeTrue();
        imported.Document.Comments.Should().NotBeEmpty();
        imported.Document.Revisions.Should().NotBeEmpty();

        var externalFormatResults = await ExportAndImportExternalFormatsThroughApiAsync(imported.Document, marker);

        var pdfPath = await ExportPdfThroughToolbarAsync(page);
        var pdf = await File.ReadAllBytesAsync(pdfPath);
        Encoding.ASCII.GetString(pdf, 0, Math.Min(pdf.Length, 8)).Should().StartWith("%PDF");

        // The Skia PDF backend embeds text as subset glyph ids inside compressed content
        // streams plus a ToUnicode CMap, so a raw UTF-8 byte scan can never find the marker.
        // Extract the real text layer through PDF.js — the same probe DocumentEditorPdfExportE2ETests
        // uses — and assert the typed marker survives into selectable/searchable text.
        var pdfBase64 = Convert.ToBase64String(pdf);
        var pdfText = await page.EvaluateAsync<string>(
            """
            async base64 => {
                const pdfjs = await import('/_content/Tempo.Blazor.PdfViewer/js/pdf.min.mjs');
                pdfjs.GlobalWorkerOptions.workerSrc = '/_content/Tempo.Blazor.PdfViewer/js/pdf.worker.min.mjs';
                const bytes = Uint8Array.from(atob(base64), ch => ch.charCodeAt(0));
                const doc = await pdfjs.getDocument({ data: bytes }).promise;
                const parts = [];
                for (let p = 1; p <= doc.numPages; p++) {
                    const pdfPage = await doc.getPage(p);
                    const content = await pdfPage.getTextContent();
                    parts.push(content.items.map(i => i.str).join(' '));
                }
                return parts.join(' ');
            }
            """,
            pdfBase64);
        pdfText.Should().Contain(marker, "the typed marker must survive into the PDF text layer");

        await ImportDocxThroughToolbarAsync(page, docxPath);
        await WaitForA11yTextAsync(page, marker);
        await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
        await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
        var metrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

        var screenshotPath = Path.Combine(output, "phase19-canvas-imported-first-paint.png");
        await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png
        });

        var compareScreenshotPath = Path.Combine(output, "phase19-canvas-compare-smoke.png");
        await RunCompareSmokeAsync(page, Phase12DocumentId, marker, compareScreenshotPath);

        await OpenCanvasDocumentAsync(page, Phase16DocumentId);
        var headerFooterDocxPath = await ExportDocxThroughToolbarAsync(page);
        var headerFooterImport = await ImportDownloadedDocxAsync(headerFooterDocxPath);
        headerFooterImport.Document.HeadersFooters.Should().NotBeEmpty();

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase19_CanvasDocxPdfImportExport_UsesCurrentCanvasModelAndKeepsFirstPaintSane),
            marker,
            phase12DocxPath = docxPath,
            phase12PdfPath = pdfPath,
            phase16DocxPath = headerFooterDocxPath,
            screenshotPath,
            compareScreenshotPath,
            externalFormatResults,
            contentMetrics = metrics,
            expectedModelChanges = "Unsaved canvas text, table, image, comment and revision anchors are exported through DOCX/PDF providers; ODT/HTML/Markdown provider exports import back with the live marker; compare uses the current canvas snapshot."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

        TestContext.AddResultFile(screenshotPath);
        TestContext.AddResultFile(compareScreenshotPath);
        TestContext.AddResultFile(manifestPath);
    }

    [TestMethod]
    public async Task Phase19_CanvasDocxExtendedEPhaseSmoke_PreservesAdvancedAnchorsAndScreenshots()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        var output = CreateOutputDirectory(nameof(Phase19_CanvasDocxExtendedEPhaseSmoke_PreservesAdvancedAnchorsAndScreenshots));
        var screenshots = new List<string>();
        var results = new List<object>();

        foreach (var scenario in CreateExtendedDocxSmokeScenarios())
        {
            await OpenCanvasDocumentAsync(page, scenario.DocumentId);
            var docxPath = await ExportDocxThroughToolbarAsync(page);
            var imported = await ImportDownloadedDocxAsync(docxPath);
            scenario.AssertDocument(imported.Document);

            await ImportDocxThroughToolbarAsync(page, docxPath);
            await WaitForA11yTextAsync(page, scenario.ExpectedText);
            await DocumentEditorCanvasVisualAssert.AssertNoTextOverlapAsync(page);
            await DocumentEditorCanvasVisualAssert.AssertNoUiOverlapAsync(page);
            var metrics = await DocumentEditorCanvasVisualAssert.AssertCanvasNonBlankAsync(page.Locator("[data-canvas-layer='content']").First);

            var screenshotPath = Path.Combine(output, $"phase19-docx-ephase-{scenario.Key}-first-paint.png");
            await page.GetByTestId("document-editor-demo").ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = screenshotPath,
                Type = ScreenshotType.Png
            });
            screenshots.Add(screenshotPath);
            TestContext.AddResultFile(screenshotPath);

            results.Add(new
            {
                scenario.Key,
                scenario.DocumentId,
                scenario.ExpectedText,
                docxPath,
                screenshotPath,
                metrics
            });
        }

        var manifestPath = Path.Combine(output, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            testName = nameof(Phase19_CanvasDocxExtendedEPhaseSmoke_PreservesAdvancedAnchorsAndScreenshots),
            screenshots,
            results
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        TestContext.AddResultFile(manifestPath);
    }

    private async Task OpenCanvasDocumentAsync(IPage page, string documentId)
    {
        await page.GotoAsync($"{BaseUrl}/canvas-engine-host?documentId={documentId}&showToolbar=true&preferLocalDraft=false", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await page.WaitForFunctionAsync(
            """
            documentId => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelector('[data-testid="document-save"]')
                && document.querySelector('[data-testid="document-canvas-page"]')
                    ?.getAttribute('data-canvas-model-document-id') === documentId
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
            """,
            documentId,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
    }

    private static async Task<string> ExportDocxThroughToolbarAsync(IPage page)
    {
        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-export-docx")).ToBeEnabledAsync(new() { Timeout = 10_000 });
        var download = await page.RunAndWaitForDownloadAsync(
            async () => await page.GetByTestId("document-export-docx").ClickAsync());
        await Assertions.Expect(page.GetByTestId("document-format-message")).ToContainTextAsync(new Regex("DOCX exported|Exportováno"), new() { Timeout = 15_000 });
        return await AssertDownloadedFileAsync(download, ".docx", 500, "DOCX export");
    }

    private static async Task<string> ExportPdfThroughToolbarAsync(IPage page)
    {
        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-export-pdf")).ToBeEnabledAsync(new() { Timeout = 10_000 });
        var download = await page.RunAndWaitForDownloadAsync(
            async () => await page.GetByTestId("document-export-pdf").ClickAsync());
        await Assertions.Expect(page.GetByTestId("document-save-message")).ToContainTextAsync(new Regex("PDF exported|PDF exportováno"), new() { Timeout = 15_000 });
        return await AssertDownloadedFileAsync(download, ".pdf", 64, "PDF export");
    }

    private static async Task ImportDocxThroughToolbarAsync(IPage page, string docxPath)
    {
        await page.GetByTestId("document-ribbon-tab-references").ClickAsync();
        await page.GetByTestId("document-import-docx-label").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-import-docx-panel")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await page.GetByTestId("document-import-docx").SetInputFilesAsync(docxPath);
        await Assertions.Expect(page.GetByTestId("document-format-message")).ToContainTextAsync(new Regex("Imported|Importováno"), new() { Timeout = 15_000 });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-testid="document-canvas-page"]').length >= 1
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
            """,
            options: new PageWaitForFunctionOptions { Timeout = 15_000 });
    }

    private static IReadOnlyList<ExtendedDocxSmokeScenario> CreateExtendedDocxSmokeScenarios()
        =>
        [
            new("e1-numbering", PhaseE1NumberingListsDocumentId, "Canvas numbering and list styles", AssertNumberingDocxSmoke),
            new("e3-sections", PhaseE3SectionsColumnsDocumentId, "Canvas sections, columns and line numbering", AssertSectionsDocxSmoke),
            new("e4-styles", PhaseE4StylesDocumentId, "Styles drive document-wide typography", AssertStylesDocxSmoke),
            new("e5-fields", PhaseE5FieldsDocumentId, "Reference targets and generated fields", AssertFieldsDocxSmoke),
            new("e8-math", PhaseE8MathEquationsDocumentId, "Canvas Math Equations", AssertMathDocxSmoke),
            new("e9-content-controls", PhaseE9ContentControlsDocumentId, "Canvas Content Controls", AssertContentControlsDocxSmoke)
        ];

    private static void AssertNumberingDocxSmoke(DocumentEditorDocument document)
    {
        document.NumberingDefinitions.Should().Contain(definition => definition.Id == "canvas-e1-legal-numbering" && definition.Levels.Count >= 3);
        document.NumberingDefinitions.Should().Contain(definition => definition.Id == "canvas-e1-bullet-numbering" && definition.Levels.Any(level => level.Format == "bullet"));
        document.ListStyles.Should().Contain(style => style.Id == "canvas-e1-legal-list-style");
        document.Blocks.Select(block => block.Content).OfType<ListBlockContent>()
            .Should().Contain(list => list.NumberingValue == 7 && list.RestartNumbering);
    }

    private static void AssertSectionsDocxSmoke(DocumentEditorDocument document)
    {
        document.Sections.Should().Contain(section => section.Id == "canvas-e3-columns-section" && section.Properties.Columns.Count == 2 && section.Properties.Columns.SeparatorLine);
        document.Sections.Should().Contain(section => section.Id == "canvas-e3-landscape-section" && section.Properties.PageSettings.Landscape && section.Properties.LineNumbering.Enabled);
        document.Blocks.Select(block => block.Content).OfType<PageBreakBlockContent>()
            .Should().Contain(pageBreak => pageBreak.BreakType == DocumentSectionBreakType.Column);
        document.Blocks.Select(block => block.Content).OfType<PageBreakBlockContent>()
            .Should().Contain(pageBreak => pageBreak.BreakType == DocumentSectionBreakType.NextPage && pageBreak.NextSectionId == "canvas-e3-landscape-section");
    }

    private static void AssertStylesDocxSmoke(DocumentEditorDocument document)
    {
        document.Styles.Should().Contain(style => style.Id == "heading-1" && style.HeadingLevel == 1 && style.IsQuickStyle);
        document.Styles.Should().Contain(style => style.Id == "normal" && style.Type == DocumentStyleType.Paragraph);
    }

    private static void AssertFieldsDocxSmoke(DocumentEditorDocument document)
    {
        var fields = EnumerateInlines(document.Blocks).OfType<DocumentFieldRun>().ToList();
        fields.Should().Contain(field => field.Id == "canvas-e5-page" && field.FieldType == DocumentFieldType.PageNumber);
        fields.Should().Contain(field => field.Id == "canvas-e5-date" && field.FieldType == DocumentFieldType.Date);
        fields.Should().Contain(field => field.Id == "canvas-e5-styleref" && field.FieldType == DocumentFieldType.StyleRef);
    }

    private static void AssertMathDocxSmoke(DocumentEditorDocument document)
    {
        var mathRuns = EnumerateInlines(document.Blocks).OfType<DocumentMathRun>().ToList();
        mathRuns.Should().Contain(math => math.MathId == "canvas-math-inline-equation" && math.Content.Elements.Any(element => element.Type == "fraction"));
        mathRuns.Should().Contain(math => math.MathId == "canvas-math-display-equation" && math.DisplayMode == DocumentMathDisplayMode.Display);
        mathRuns.Should().Contain(math => math.MathId == "canvas-math-matrix-equation" && math.Content.Elements.Any(element => element.Type == "matrix"));
    }

    private static void AssertContentControlsDocxSmoke(DocumentEditorDocument document)
    {
        var contentControls = EnumerateInlines(document.Blocks).OfType<DocumentContentControlRun>().ToList();
        contentControls.Should().Contain(control => control.Control.ControlId == "canvas-form-name" && control.Control.Kind == DocumentContentControlKind.PlainText);
        contentControls.Should().Contain(control => control.Control.ControlId == "canvas-form-approved" && control.Control.Kind == DocumentContentControlKind.Checkbox);
        contentControls.Should().Contain(control => control.Control.ControlId == "canvas-form-plan" && control.Control.Kind == DocumentContentControlKind.DropDown && control.Control.Items.Count == 3);
        contentControls.Should().Contain(control => control.Control.ControlId == "canvas-form-renewal" && control.Control.Kind == DocumentContentControlKind.Date);
        document.Blocks.Select(block => block.Content).OfType<ContentControlBlockContent>()
            .Should().Contain(control => control.Control.ControlId == "canvas-form-addresses" && control.Control.Kind == DocumentContentControlKind.RepeatingSection);
    }

    private static async Task<DocumentFormatImportResult> ImportDownloadedDocxAsync(string docxPath)
    {
        await using var stream = File.OpenRead(docxPath);
        return await new DocumentDocxImporter().ImportAsync(stream);
    }

    private static async Task<IReadOnlyList<ExternalFormatResult>> ExportAndImportExternalFormatsThroughApiAsync(
        DocumentEditorDocument document,
        string expectedMarker)
    {
        using var http = CreateApiClient();
        var results = new List<ExternalFormatResult>();
        foreach (var format in new[]
        {
            DocumentFormatProviderKind.Odt,
            DocumentFormatProviderKind.Html,
            DocumentFormatProviderKind.Markdown
        })
        {
            var exported = await ExportFormatThroughApiAsync(http, document, format);
            exported.Success.Should().BeTrue();
            exported.Content.Should().NotBeEmpty();
            exported.Format.Should().Be(format);
            AssertExportContent(format, exported.Content, expectedMarker);

            var imported = await ImportFormatThroughApiAsync(http, exported, format);
            imported.Success.Should().BeTrue();
            imported.Document.Should().NotBeNull();
            imported.Format.Should().Be(format);
            GetDocumentText(imported.Document!).Should().Contain(expectedMarker);

            results.Add(new ExternalFormatResult
            {
                Format = format.ToString(),
                FileName = exported.FileName,
                ContentType = exported.ContentType,
                ByteLength = exported.Content.Length,
                ImportedBlockCount = imported.Document!.Blocks.Count
            });
        }

        return results;
    }

    private static async Task<DocumentFormatExportProviderResult> ExportFormatThroughApiAsync(
        HttpClient http,
        DocumentEditorDocument document,
        DocumentFormatProviderKind format)
    {
        using var response = await http.PostAsJsonAsync("/api/document-editor/formats/export", new DocumentFormatExportProviderRequest
        {
            DocumentId = document.DocumentId,
            Document = document,
            Format = format,
            FileName = $"phase19-{format.ToString().ToLowerInvariant()}-export"
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DocumentFormatExportProviderResult>()
            ?? throw new InvalidOperationException($"Missing {format} export response.");
    }

    private static async Task<DocumentFormatImportProviderResult> ImportFormatThroughApiAsync(
        HttpClient http,
        DocumentFormatExportProviderResult exported,
        DocumentFormatProviderKind format)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(exported.Content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(exported.ContentType);
        form.Add(file, "file", exported.FileName);
        using var response = await http.PostAsync($"/api/document-editor/formats/import?format={format}", form);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DocumentFormatImportProviderResult>()
            ?? throw new InvalidOperationException($"Missing {format} import response.");
    }

    private static void AssertExportContent(DocumentFormatProviderKind format, byte[] content, string expectedMarker)
    {
        if (format == DocumentFormatProviderKind.Odt)
        {
            content.Take(2).Should().Equal((byte)'P', (byte)'K');
            return;
        }

        var text = Encoding.UTF8.GetString(content);
        text.Should().Contain(expectedMarker);
        if (format == DocumentFormatProviderKind.Html)
        {
            text.Should().Contain("<main");
        }
    }

    private static async Task RunCompareSmokeAsync(
        IPage page,
        string targetDocumentId,
        string expectedMarker,
        string screenshotPath)
    {
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        await page.GetByTestId("document-compare-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-compare-target-document-id").FillAsync(targetDocumentId);
        await page.GetByTestId("document-compare-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("document-diff-side-by-side")).ToContainTextAsync(expectedMarker, new() { Timeout = 10_000 });
        await page.GetByTestId("document-compare-dialog").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = screenshotPath,
            Type = ScreenshotType.Png
        });
        await page.GetByTestId("document-compare-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeHiddenAsync(new() { Timeout = 5_000 });
    }

    private static HttpClient CreateApiClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri("https://localhost:5100") };
    }

    private static async Task<string> AssertDownloadedFileAsync(IDownload download, string expectedExtension, long minBytes, string label)
    {
        var path = await download.PathAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(path), $"{label} must provide a downloaded file path.");
        Assert.IsTrue(File.Exists(path), $"{label} must exist at '{path}'.");
        Assert.IsTrue(new FileInfo(path).Length >= minBytes, $"{label} must contain at least {minBytes} bytes.");
        Assert.AreEqual(expectedExtension, Path.GetExtension(download.SuggestedFilename), ignoreCase: true, $"{label} suggested filename should use {expectedExtension}.");
        return path!;
    }

    private static async Task ClickCanvasBlockAsync(IPage page, string blockId, int offset)
    {
        var point = await ReadCanvasPointAsync(page, blockId, offset);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        await page.WaitForFunctionAsync(
            """
            blockId => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-focus-block-id') === blockId
            """,
            blockId,
            new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private static Task<int> ReadBlockEndOffsetAsync(IPage page, string blockId)
        => page.EvaluateAsync<int>(
            """
            blockId => Math.max(...Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                .map(node => Number(node.getAttribute('data-canvas-end-offset') || '0')))
            """,
            blockId);

    private static Task<CanvasPoint> ReadCanvasPointAsync(IPage page, string blockId, int offset)
        => page.EvaluateAsync<CanvasPoint>(
            """
            ([blockId, offset]) => {
                const rects = Array.from(document.querySelectorAll(`[data-canvas-text-rect][data-block-id="${blockId}"]`))
                    .map(node => {
                        const rect = node.getBoundingClientRect();
                        const start = Number(node.getAttribute('data-canvas-start-offset') || '0');
                        const end = Number(node.getAttribute('data-canvas-end-offset') || '0');
                        return { rect, start, end };
                    })
                    .filter(item => item.end > item.start);
                if (!rects.length) throw new Error(`No canvas text rects found for ${blockId}.`);
                const target = rects.find(item => offset >= item.start && offset <= item.end) || rects.at(-1);
                const ratio = Math.max(0, Math.min(1, (offset - target.start) / Math.max(1, target.end - target.start)));
                return {
                    x: target.rect.left + Math.max(2, target.rect.width * ratio),
                    y: target.rect.top + Math.max(2, target.rect.height / 2)
                };
            }
            """,
            new object[] { blockId, offset });

    private static Task FocusHiddenCanvasInputAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                const input = document.querySelector('[data-testid="document-canvas-hidden-input"]');
                input?.focus();
            }
            """);

    private static Task WaitForA11yTextAsync(IPage page, string text)
        => Assertions.Expect(page.GetByTestId("document-canvas-a11y-mirror"))
            .ToContainTextAsync(text, new() { Timeout = 10_000 });

    private static string GetDocumentText(DocumentEditorDocument document)
        => string.Join('\n', document.Blocks.Select(GetBlockText));

    private static string GetBlockText(DocumentBlock block)
        => block.Content switch
        {
            ParagraphBlockContent paragraph => GetInlineText(paragraph.Inlines),
            HeadingBlockContent heading => GetInlineText(heading.Inlines),
            ListBlockContent list => GetInlineText(list.Inlines),
            QuoteBlockContent quote => GetInlineText(quote.Inlines),
            TableBlockContent table => string.Join('\n', table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Blocks).Select(GetBlockText)),
            ImageBlockContent image => image.Caption ?? image.AltText ?? string.Empty,
            _ => string.Empty
        };

    private static string GetInlineText(IEnumerable<InlineContent> inlines)
        => string.Concat(inlines.Select(inline => inline switch
        {
            TextRun run => run.Text,
            TokenRun token => string.IsNullOrWhiteSpace(token.DisplayName) ? token.Key : token.DisplayName,
            _ => string.Empty
        }));

    private static IEnumerable<InlineContent> EnumerateInlines(IEnumerable<DocumentBlock> blocks)
    {
        foreach (var block in blocks)
        {
            foreach (var inline in EnumerateBlockInlines(block))
            {
                yield return inline;
            }
        }
    }

    private static IEnumerable<InlineContent> EnumerateBlockInlines(DocumentBlock block)
    {
        var inlines = block.Content switch
        {
            ParagraphBlockContent paragraph => paragraph.Inlines,
            HeadingBlockContent heading => heading.Inlines,
            ListBlockContent list => list.Inlines,
            QuoteBlockContent quote => quote.Inlines,
            _ => []
        };

        foreach (var inline in inlines)
        {
            yield return inline;
            if (inline is DocumentContentControlRun contentControl)
            {
                foreach (var childInline in contentControl.Inlines)
                {
                    yield return childInline;
                }
            }
        }

        if (block.Content is TableBlockContent table)
        {
            foreach (var cellBlock in table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Blocks))
            {
                foreach (var inline in EnumerateBlockInlines(cellBlock))
                {
                    yield return inline;
                }
            }
        }
        else if (block.Content is ContentControlBlockContent contentControl)
        {
            foreach (var childBlock in contentControl.Blocks)
            {
                foreach (var inline in EnumerateBlockInlines(childBlock))
                {
                    yield return inline;
                }
            }
        }
    }

    private static bool HasImageContent(IEnumerable<DocumentBlock> blocks)
        => blocks.Any(block => block.Content switch
        {
            ImageBlockContent => true,
            ParagraphBlockContent paragraph => paragraph.Inlines.OfType<DocumentDrawingRun>().Any(IsImageDrawing),
            HeadingBlockContent heading => heading.Inlines.OfType<DocumentDrawingRun>().Any(IsImageDrawing),
            ListBlockContent list => list.Inlines.OfType<DocumentDrawingRun>().Any(IsImageDrawing),
            QuoteBlockContent quote => quote.Inlines.OfType<DocumentDrawingRun>().Any(IsImageDrawing),
            TableBlockContent table => table.Rows.SelectMany(row => row.Cells).Any(cell => HasImageContent(cell.Blocks)),
            _ => false
        });

    private static bool IsImageDrawing(DocumentDrawingRun drawing)
        => !string.IsNullOrWhiteSpace(drawing.AssetId)
            || !string.IsNullOrWhiteSpace(drawing.Url)
            || !string.IsNullOrWhiteSpace(drawing.AltText)
            || !string.IsNullOrWhiteSpace(drawing.Caption);

    private static string CreateOutputDirectory(string testName)
    {
        var output = Path.Combine(
            FindRepositoryRoot().FullName,
            "tests",
            "Tempo.Blazor.E2E",
            "TestResults",
            "document-editor-canvas",
            nameof(DocumentEditorCanvasImportExportE2ETests),
            SanitizePathSegment(testName));
        Directory.CreateDirectory(output);
        return output;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx from test output directory.");
    }

    private sealed class CanvasPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    private sealed record ExtendedDocxSmokeScenario(
        string Key,
        string DocumentId,
        string ExpectedText,
        Action<DocumentEditorDocument> AssertDocument);

    private sealed class ExternalFormatResult
    {
        public string Format { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;

        public int ByteLength { get; set; }

        public int ImportedBlockCount { get; set; }
    }
}
