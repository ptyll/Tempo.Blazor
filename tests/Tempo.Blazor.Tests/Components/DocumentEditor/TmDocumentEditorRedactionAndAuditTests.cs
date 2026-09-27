using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Models;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Phase 10: document security. (1) The export gate really removes redacted content — every
/// document leaving the editor through the export bridge has redaction-marked text replaced with
/// block characters. (2) The audit hook is optionally blocking: with
/// DocumentEditorAuditFailureMode.Blocking a failing activity provider fails the save workflow
/// (compliance mode), while the NonBlocking default keeps the workflow running.
/// </summary>
public class TmDocumentEditorRedactionAndAuditTests : LocalizationTestBase
{
    [Fact]
    public void PrepareDocumentForExport_RemovesRedactedContent()
    {
        var document = DocumentEditorDocument.Empty("redact-export");
        document.Blocks.Add(new DocumentBlock
        {
            Id = "p1",
            Type = DocumentBlockType.Paragraph,
            Order = 10,
            Content = new ParagraphBlockContent
            {
                Inlines =
                [
                    new TextRun { Text = "Public " },
                    new TextRun { Text = "top-secret", Marks = [new InlineMark { Type = InlineMarkType.Redaction }] }
                ]
            }
        });

        var exported = TmDocumentEditor.PrepareDocumentForExport(document);

        var runs = ((ParagraphBlockContent)exported.Blocks[0].Content!).Inlines.OfType<TextRun>().ToList();
        runs[1].Text.Should().Be(new string('█', "top-secret".Length));
        // The live editing model keeps the original text.
        ((ParagraphBlockContent)document.Blocks[0].Content!).Inlines.OfType<TextRun>()
            .Last().Text.Should().Be("top-secret");
    }

    [Fact]
    public void PrepareDocumentForExport_WithoutRedactions_ReturnsSameInstance()
    {
        var document = DocumentEditorDocument.Empty("no-redactions");

        TmDocumentEditor.PrepareDocumentForExport(document).Should().BeSameAs(document);
    }

    [Fact]
    public void BlockingAuditMode_FailingProvider_FailsTheSaveWorkflow()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-audit-blocking");
        // The open audit succeeds so the document loads; the save audit then fails.
        var audit = new ThrowingActivityProvider { SucceedFirst = 1 };

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-audit-blocking")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ActivityProvider, audit)
                      .Add(p => p.AuditFailureMode, DocumentEditorAuditFailureMode.Blocking));
        cut.WaitForElement("[data-testid='document-canvas-engine-host']");

        cut.WaitForElement("[data-testid='document-save']").Click();

        // Compliance mode: the failed audit surfaces as a failed save (no silent success).
        cut.WaitForAssertion(() =>
        {
            var message = cut.Find("[data-testid='document-save-message']").TextContent;
            message.Should().NotContain("Saved", "a save whose audit trail could not be persisted must not report success");
        });
        audit.Attempts.Should().BeGreaterThan(0);
    }

    [Fact]
    public void NonBlockingAuditMode_FailingProvider_KeepsSaveWorking()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-audit-nonblocking");
        var audit = new ThrowingActivityProvider();

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-audit-nonblocking")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ShowToolbar, true)
                      .Add(p => p.ActivityProvider, audit)
                      .Add(p => p.AuditFailureMode, DocumentEditorAuditFailureMode.NonBlocking));
        cut.WaitForElement("[data-testid='document-canvas-engine-host']");

        cut.WaitForElement("[data-testid='document-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var message = cut.Find("[data-testid='document-save-message']").TextContent;
            message.Should().Contain("Saved", "audit failures must not block workflows by default");
        });
        audit.Attempts.Should().BeGreaterThan(0);
    }

    private sealed class ThrowingActivityProvider : ITmActivityProvider
    {
        public int Attempts { get; private set; }

        public int SucceedFirst { get; init; }

        public TmActivityProviderCapabilities Capabilities => new();

        public Task<TmActivityEntry> AppendAsync(TmActivityEntry entry, CancellationToken cancellationToken = default)
        {
            Attempts++;
            if (Attempts <= SucceedFirst)
            {
                return Task.FromResult(entry);
            }

            throw new InvalidOperationException("audit store unavailable");
        }

        public Task<IReadOnlyList<TmActivityEntry>> GetForEntityAsync(TmEntityRef entityRef, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TmActivityEntry>>([]);

        public Task<PagedResult<TmActivityEntry>> QueryAsync(TmActivityQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<TmActivityEntry>());
    }
}
