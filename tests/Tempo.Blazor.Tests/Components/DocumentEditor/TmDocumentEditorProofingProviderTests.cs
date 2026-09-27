using Bunit;
using FluentAssertions;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Phase 7: async proofing provider wiring. When <c>TmDocumentEditor.ProofingProvider</c> is set,
/// the editor extracts the loaded document's plain text, calls the provider, and pushes the
/// materialized word lists into the canvas engine (mount/setOptions), where the existing squiggle
/// overlay and spelling context menu take over. Provider failures are fail-open: the editor keeps
/// working with whatever proofing options it already had.
/// </summary>
public class TmDocumentEditorProofingProviderTests : LocalizationTestBase
{
    [Fact]
    public void Editor_WithProofingProvider_PushesProviderFindingsToCanvasEngine()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-proofing");
        var proofing = new FakeProofingProvider(new DocumentProofingCheckResult
        {
            Issues = [new DocumentProofingIssue { Word = "smlouvva", Suggestions = ["smlouva"] }]
        });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-proofing")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ProofingProvider, proofing)
                      .Add(p => p.ProofingOptions, new DocumentProofingOptions { DefaultLanguage = "cs-CZ" }));
        cut.WaitForElement("[data-testid='document-canvas-engine-host']");

        cut.WaitForAssertion(() =>
        {
            proofing.LastRequest.Should().NotBeNull("the editor must run a proofing pass after load");
            CanvasOptionsPayloads().Should().Contain(json => json.Contains("smlouvva"),
                "provider findings must reach the canvas engine options");
        });

        proofing.LastRequest!.Text.Should().NotBeNullOrWhiteSpace("the extracted document text is the check input");
        proofing.LastRequest.Language.Should().Be("cs-CZ");
        proofing.LastRequest.DocumentId.Should().Be("doc-proofing");
    }

    [Fact]
    public void Editor_MergesHostBaseOptionsWithProviderFindings()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-proofing-merge");
        var proofing = new FakeProofingProvider(new DocumentProofingCheckResult
        {
            Issues = [new DocumentProofingIssue { Word = "smlouvva", Suggestions = ["smlouva"] }]
        });

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-proofing-merge")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ProofingProvider, proofing)
                      .Add(p => p.ProofingOptions, new DocumentProofingOptions
                      {
                          DefaultLanguage = "cs-CZ",
                          FlaggedWords = ["wrngg"],
                          Suggestions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                          {
                              ["wrngg"] = ["wrong"]
                          }
                      }));
        cut.WaitForElement("[data-testid='document-canvas-engine-host']");

        cut.WaitForAssertion(() =>
        {
            var merged = CanvasOptionsPayloads().Where(json => json.Contains("smlouvva")).ToList();
            merged.Should().NotBeEmpty();
            merged[^1].Should().Contain("wrngg", "host base word lists survive the provider merge");
        });
    }

    [Fact]
    public void Editor_ProofingProviderFailure_IsFailOpen()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedContractDocument("doc-proofing-fail");
        var proofing = new FakeProofingProvider(failWith: new HttpRequestException("LanguageTool unreachable"));

        var cut = RenderDocumentEditor(parameters =>
            parameters.Add(p => p.DocumentId, "doc-proofing-fail")
                      .Add(p => p.Provider, provider)
                      .Add(p => p.ProofingProvider, proofing));

        cut.WaitForElement("[data-testid='document-canvas-engine-host']");
        cut.WaitForAssertion(() => proofing.LastRequest.Should().NotBeNull());

        // The editor keeps rendering and no raw error leaks into the surface.
        cut.FindAll("[data-testid='document-canvas-engine-host']").Should().ContainSingle();
        cut.Markup.Should().NotContain("LanguageTool unreachable");
    }

    private IReadOnlyList<string> CanvasOptionsPayloads()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier is "mount" or "setOptions")
            .SelectMany(invocation => invocation.Arguments)
            .OfType<string>()
            .Where(argument => argument.Contains("proofing", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private sealed class FakeProofingProvider(
        DocumentProofingCheckResult? result = null,
        Exception? failWith = null) : ITempoProofingProvider
    {
        public DocumentProofingCheckRequest? LastRequest { get; private set; }

        public Task<DocumentProofingCheckResult> CheckAsync(
            DocumentProofingCheckRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return failWith is not null
                ? Task.FromException<DocumentProofingCheckResult>(failWith)
                : Task.FromResult(result ?? DocumentProofingCheckResult.Empty);
        }
    }
}
