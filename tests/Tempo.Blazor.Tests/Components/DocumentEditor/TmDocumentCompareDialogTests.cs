using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Interfaces;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign 146c7e1b, phase 7 (DE-2.6): the compare dialog shows its two sources as CARDS ("Change..." opens the picker: current / history
/// version / document id / uploaded DOCX), and its result as a summary plus the list of block changes - the diff itself is a mode of the
/// document region now ("Open comparison"). The old redline tests stay as they were apart from opening the picker first.
/// </summary>
public class TmDocumentCompareDialogTests : LocalizationTestBase
{
    [Fact]
    public void Sources_AreCards_WithTheirRoleAsTitle_AndAChangeButton()
    {
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters.Add(p => p.CurrentDocument, Document("current", "Hello")));

        var baseCard = cut.Find("[data-testid='document-compare-base-card']");
        var targetCard = cut.Find("[data-testid='document-compare-target-card']");
        baseCard.QuerySelector("h4")!.TextContent.Should().Be("Original document");
        targetCard.QuerySelector("h4")!.TextContent.Should().Be("New document");
        cut.Find("[data-testid='document-compare-base-label']").TextContent.Should().Be("Current document");
        cut.Find("[data-testid='document-compare-base-change']").TextContent.Trim().Should().Be("Change…");
        cut.FindAll("[data-testid='document-compare-base-source']").Should().BeEmpty("the picker is closed until Change… is pressed");
    }

    [Fact]
    public async Task UploadedDocx_NameShowsOnTheCard()
    {
        var provider = new FakeFormatProvider();
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, Document("current", "Hello"))
            .Add(p => p.FormatProvider, provider));

        cut.Find("[data-testid='document-compare-target-change']").Click();
        cut.Find("[data-testid='document-compare-target-source']").Change("JsonSnapshot");
        var input = cut.FindComponent<InputFile>();
        await cut.InvokeAsync(() => input.Instance.OnChange.InvokeAsync(new InputFileChangeEventArgs([new FakeBrowserFile("Proposal v2.docx")])));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-compare-target-label']").TextContent.Should().Be("Proposal v2.docx"));
    }

    [Fact]
    public void ChangePicker_OffersCurrentHistoryVersionDocumentIdAndUpload()
    {
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, Document("current", "Hello"))
            .Add(p => p.Versions, [Version("v1", "Draft", "One")]));

        cut.Find("[data-testid='document-compare-base-change']").Click();

        var options = cut.Find("[data-testid='document-compare-base-source']").QuerySelectorAll("option").Select(option => option.GetAttribute("value"));
        options.Should().Equal("Current", "Version", "DocumentId", "JsonSnapshot");
    }

    [Fact]
    public void ChangePicker_WithoutVersions_DoesNotOfferHistory()
    {
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters.Add(p => p.CurrentDocument, Document("current", "Hello")));

        cut.Find("[data-testid='document-compare-base-change']").Click();

        cut.Find("[data-testid='document-compare-base-source']").QuerySelectorAll("option").Select(option => option.GetAttribute("value"))
            .Should().Equal("Current", "DocumentId", "JsonSnapshot");
    }

    [Fact]
    public void VersionChoice_OffersThePassedVersions_AndTheCardNamesTheChosenOne()
    {
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, Document("current", "Hello"))
            .Add(p => p.Versions, [Version("v1", "First draft", "One"), Version("v2", "Final", "Two")]));

        cut.Find("[data-testid='document-compare-target-change']").Click();
        cut.Find("[data-testid='document-compare-target-source']").Change("Version");

        var versions = cut.Find("[data-testid='document-compare-target-version']").QuerySelectorAll("option").Select(option => option.TextContent).ToList();
        versions.Should().Contain("First draft").And.Contain("Final");

        cut.Find("[data-testid='document-compare-target-version']").Change("v2");
        cut.Find("[data-testid='document-compare-target-label']").TextContent.Should().Be("Final");
    }

    [Fact]
    public void Result_ShowsTheSummaryNumbers_AndOneItemPerChange()
    {
        var cut = RenderWithResult(
            Document("current", "Same", ("b2", "Old second"), ("b3", "To be removed")),
            [Version("v1", "Base", "ignored")],
            Document("other", "Same", ("b2", "New second"), ("b4", "Added block")));

        cut.Find("[data-testid='document-compare-summary-added']").TextContent.Should().Be("1");
        cut.Find("[data-testid='document-compare-summary-removed']").TextContent.Should().Be("1");
        cut.Find("[data-testid='document-compare-summary-changed']").TextContent.Should().Be("1");
        cut.FindAll("[data-testid='document-compare-change']").Should().HaveCount(3);
        cut.FindAll("[data-testid='document-diff-viewer']").Should().BeEmpty("the dialog lists the changes; the diff is the document region's mode");
    }

    [Fact]
    public void Result_WithoutChanges_ShowsAnEmptyState()
    {
        var cut = RenderWithResult(Document("current", "Same text"), [], Document("other", "Same text"));

        cut.FindAll("[data-testid='document-compare-change']").Should().BeEmpty();
        cut.Find("[data-testid='document-compare-no-differences']").TextContent.Should().Contain("No differences found");
        cut.FindAll("[data-testid='document-compare-open-comparison']").Should().BeEmpty();
    }

    [Fact]
    public void OpenComparison_AndAChangeClick_RaiseTheRequest_WithTheBlockOfTheClickedChange()
    {
        var requests = new List<DocumentCompareOpenRequest>();
        var cut = RenderWithResult(
            Document("current", "Same", ("b2", "Old second")),
            [],
            Document("other", "Same", ("b2", "New second")),
            EventCallback.Factory.Create<DocumentCompareOpenRequest>(this, requests.Add));

        cut.Find("[data-testid='document-compare-open-comparison']").Click();
        cut.Find("[data-testid='document-compare-change'] button").Click();

        requests.Should().HaveCount(2);
        requests[0].BlockId.Should().BeNull();
        requests[0].Result.Summary.ChangedBlocks.Should().Be(1);
        requests[1].BlockId.Should().Be("b2");
    }

    [Fact]
    public void Footer_HasCompareFirst_ThenRedlineExport_AndCancelLast()
    {
        var cut = RenderWithResult(
            Document("current", "Same", ("b2", "Old second")),
            [],
            Document("other", "Same", ("b2", "New second")),
            exportRedline: true);

        var ids = cut.FindAll(".tm-modal-footer button, [class*='footer'] button").Select(button => button.GetAttribute("data-testid")).Where(id => id is not null).ToList();
        ids.Should().ContainInOrder("document-compare-run", "document-compare-export-redline", "document-compare-cancel");
    }

    [Fact]
    public void ExportRedline_NeedsADelegateAndChanges()
    {
        var withoutDelegate = RenderWithResult(Document("current", "Same", ("b2", "Old")), [], Document("other", "Same", ("b2", "New")));
        withoutDelegate.FindAll("[data-testid='document-compare-export-redline']").Should().BeEmpty();

        var noChanges = RenderWithResult(Document("current", "Same"), [], Document("other", "Same"), exportRedline: true);
        noChanges.FindAll("[data-testid='document-compare-export-redline']").Should().BeEmpty();

        var withBoth = RenderWithResult(Document("current", "Same", ("b2", "Old")), [], Document("other", "Same", ("b2", "New")), exportRedline: true);
        withBoth.FindAll("[data-testid='document-compare-export-redline']").Should().HaveCount(1);
    }

    [Fact]
    public void Compare_IsDisabled_UntilBothSourcesNameSomething()
    {
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters.Add(p => p.CurrentDocument, Document("current", "Hello")));

        var run = cut.Find("[data-testid='document-compare-run']");
        run.HasAttribute("disabled").Should().BeTrue("the default target is an empty document id");
        run.GetAttribute("title").Should().Be("Choose both sources to compare.");

        cut.Find("[data-testid='document-compare-target-change']").Click();
        cut.Find("[data-testid='document-compare-target-document-id']").Input("doc-x");

        cut.Find("[data-testid='document-compare-run']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='document-compare-target-document-id']").Input("   ");
        cut.Find("[data-testid='document-compare-run']").HasAttribute("disabled").Should().BeTrue("a blank id names nothing");
    }

    [Fact]
    public void Picker_Collapses_AfterCurrentOrAVersionIsChosen_ButStaysOpenForADocumentId()
    {
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, Document("current", "Hello"))
            .Add(p => p.Versions, [Version("v1", "Draft", "One")]));

        cut.Find("[data-testid='document-compare-target-change']").Click();
        cut.Find("[data-testid='document-compare-target-document-id']").Input("typing");
        cut.FindAll("[data-testid='document-compare-target-editor']").Should().HaveCount(1, "a typed id keeps the picker open");

        cut.Find("[data-testid='document-compare-target-source']").Change("Version");
        cut.FindAll("[data-testid='document-compare-target-editor']").Should().HaveCount(1, "the version is not chosen yet");
        cut.Find("[data-testid='document-compare-target-version']").Change("v1");
        cut.FindAll("[data-testid='document-compare-target-editor']").Should().BeEmpty("choosing a version finishes the picker");
        cut.Find("[data-testid='document-compare-target-label']").TextContent.Should().Be("Draft");

        cut.Find("[data-testid='document-compare-base-change']").Click();
        cut.Find("[data-testid='document-compare-base-source']").Change("DocumentId");
        cut.Find("[data-testid='document-compare-base-source']").Change("Current");
        cut.FindAll("[data-testid='document-compare-base-editor']").Should().BeEmpty("choosing the current document finishes the picker");
    }

    [Fact]
    public void ABadVersionSnapshot_FailsTheComparisonWithTheLocalizedMessage()
    {
        var broken = Version("v1", "Broken", "x");
        broken.Snapshot.Json = "{bad";
        var cut = Render<TmDocumentCompareDialog>(parameters => parameters
            .Add(p => p.CurrentDocument, Document("current", "Hello"))
            .Add(p => p.Versions, [broken]));

        cut.Find("[data-testid='document-compare-target-change']").Click();
        cut.Find("[data-testid='document-compare-target-source']").Change("Version");
        cut.Find("[data-testid='document-compare-target-version']").Change("v1");
        cut.Find("[data-testid='document-compare-run']").Click();

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-compare-message']").TextContent.Should().Be("Document comparison failed."));
        cut.FindAll("[data-testid='document-compare-result']").Should().BeEmpty();
    }

    private IRenderedComponent<TmDocumentCompareDialog> RenderWithResult(
        DocumentEditorDocument current,
        IReadOnlyList<DocumentVersion> versions,
        DocumentEditorDocument other,
        EventCallback<DocumentCompareOpenRequest>? onOpen = null,
        bool exportRedline = false)
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedEmptyDocument(other.DocumentId);
        provider.SaveAsync(new DocumentEditorSaveRequest
        {
            DocumentId = other.DocumentId,
            Document = other,
            ConcurrencyMode = DocumentEditorConcurrencyMode.Force,
        }).GetAwaiter().GetResult();

        var cut = Render<TmDocumentCompareDialog>(parameters =>
        {
            parameters.Add(p => p.CurrentDocument, current)
                      .Add(p => p.DocumentProvider, provider)
                      .Add(p => p.Versions, versions);
            if (onOpen is { } callback)
            {
                parameters.Add(p => p.OnOpenComparison, callback);
            }

            if (exportRedline)
            {
                parameters.Add(p => p.OnExportRedline, EventCallback.Factory.Create<DocumentCompareResult>(this, _ => { }));
            }
        });

        cut.Find("[data-testid='document-compare-target-change']").Click();
        cut.Find("[data-testid='document-compare-target-document-id']").Input(other.DocumentId);
        cut.Find("[data-testid='document-compare-run']").Click();
        cut.WaitForElement("[data-testid='document-compare-result']");
        return cut;
    }

    private static DocumentEditorDocument Document(string documentId, string firstText, params (string Id, string Text)[] more)
    {
        var document = DocumentEditorDocument.Empty();
        document.DocumentId = documentId;
        var blocks = new List<(string Id, string Text)> { ("b1", firstText) };
        blocks.AddRange(more);
        document.Blocks = blocks
            .Select((block, index) => new DocumentBlock
            {
                Id = block.Id,
                Type = DocumentBlockType.Paragraph,
                Order = index + 1,
                Content = new ParagraphBlockContent { Inlines = [new TextRun { Text = block.Text }] },
            })
            .ToList();
        return document;
    }

    private static DocumentVersion Version(string id, string label, string text)
    {
        var document = Document("current", text);
        var snapshot = new DocumentVersionSnapshot
        {
            DocumentId = document.DocumentId,
            SchemaVersion = document.SchemaVersion,
            Json = DocumentEditorJson.Serialize(document)
        };
        snapshot.Hash = DocumentVersionHashHelper.ComputeSnapshotHash(snapshot);
        return new DocumentVersion { Id = id, DocumentId = document.DocumentId, Label = label, CreatedAt = DateTimeOffset.UtcNow, Snapshot = snapshot };
    }

    private sealed class FakeBrowserFile(string name) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => 4;
        public string ContentType => "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream([1, 2, 3, 4]);
    }

    private sealed class FakeFormatProvider : IDocumentFormatProvider
    {
        public Task<IReadOnlyList<DocumentFormatProviderCapability>> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DocumentFormatProviderCapability>>([]);

        public Task<DocumentFormatImportProviderResult> ImportAsync(DocumentFormatImportProviderRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new DocumentFormatImportProviderResult { Success = true, Document = Document("uploaded", "From docx") });

        public Task<DocumentFormatExportProviderResult> ExportAsync(DocumentFormatExportProviderRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
