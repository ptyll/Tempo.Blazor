using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// The compare dialog must offer a redline export once a comparison with changes exists:
/// the button raises <c>OnExportRedline</c> with the full compare result so the editor can build
/// the tracked-changes document and ship it through the format provider.
/// </summary>
public class TmDocumentCompareDialogRedlineTests : LocalizationTestBase
{
    [Fact]
    public async Task Compare_WithChanges_ShowsExportButtonAndRaisesCallbackWithResult()
    {
        var provider = new InMemoryDocumentEditorProvider();
        await Seed(provider, "doc-v1", "Cena je 100 Kč.");
        DocumentCompareResult? exported = null;

        var cut = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, Document("current", "Cena je 200 Kč."))
            .Add(p => p.DocumentProvider, provider)
            .Add(p => p.OnExportRedline, EventCallback.Factory.Create<DocumentCompareResult>(this, result => exported = result)));

        cut.Find("[data-testid='document-compare-target-document-id']").Input("doc-v1");
        cut.Find("[data-testid='document-compare-run']").Click();

        var exportButton = cut.WaitForElement("[data-testid='document-compare-export-redline']");
        exportButton.Click();

        cut.WaitForAssertion(() =>
        {
            exported.Should().NotBeNull("clicking the export button must hand the compare result to the host");
            exported!.Summary.HasChanges.Should().BeTrue();
        });
    }

    [Fact]
    public async Task Compare_WithoutChanges_HidesExportButton()
    {
        var provider = new InMemoryDocumentEditorProvider();
        await Seed(provider, "doc-same", "Stejný text.");

        var cut = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, Document("current", "Stejný text."))
            .Add(p => p.DocumentProvider, provider));

        cut.Find("[data-testid='document-compare-target-document-id']").Input("doc-same");
        cut.Find("[data-testid='document-compare-run']").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='document-compare-loading']").Should().BeEmpty());
        cut.FindAll("[data-testid='document-compare-export-redline']")
            .Should().BeEmpty("a comparison without changes has nothing to redline");
    }

    private static async Task Seed(InMemoryDocumentEditorProvider provider, string documentId, string text)
    {
        provider.SeedEmptyDocument(documentId);
        await provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = documentId,
            Document = Document(documentId, text),
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force,
        });
    }

    private static DocumentEditorDocument Document(string documentId, string text)
    {
        var document = DocumentEditorDocument.Empty();
        document.DocumentId = documentId;
        document.Blocks =
        [
            new DocumentBlock
            {
                Id = "b1",
                Type = DocumentBlockType.Paragraph,
                Order = 1,
                Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = text }] },
            },
        ];
        return document;
    }
}
