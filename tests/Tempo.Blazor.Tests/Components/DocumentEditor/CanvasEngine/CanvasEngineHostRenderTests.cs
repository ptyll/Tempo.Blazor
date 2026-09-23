using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Interfaces;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor.CanvasEngine;

public sealed class CanvasEngineHostRenderTests : LocalizationTestBase
{
    private const string InteropModulePath = "./_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs";

    [Fact]
    public void CanvasEngineHost_RendersCanvasStackAccessibilityMirrorAndHiddenInput()
    {
        SetupCanvasModule();
        var document = DocumentEditorDocument.Empty("canvas-host-render");

        var cut = Render<TmDocumentCanvasEngineHost>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.AriaLabel, "Document editor")
            .Add(p => p.InputAriaLabel, "Document editor"));

        cut.Find("[data-testid='document-canvas-engine-host']")
            .GetAttribute("data-canvas-engine-ready")
            .Should()
            .BeOneOf("false", "true");
        cut.Find("[data-testid='document-canvas-engine-root']").Should().NotBeNull();
        cut.Find("[data-testid='document-canvas-page']").Should().NotBeNull();
        cut.FindAll("[data-canvas-layer]").Should().HaveCount(6);
        cut.Find("[data-canvas-layer='page-background']").Should().NotBeNull();
        cut.Find("[data-canvas-layer='selection-caret']").Should().NotBeNull();
        cut.Find("[data-testid='document-canvas-a11y-mirror']")
            .GetAttribute("role")
            .Should()
            .Be("document");
        cut.Find("[data-testid='document-canvas-hidden-input']")
            .TagName
            .Should()
            .Be("TEXTAREA");
    }

    [Fact]
    public void TmDocumentEditor_CanvasEnginePreview_RendersCanvasHostOnly()
    {
        SetupCanvasModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedEmptyDocument("canvas-editor-empty");

        var cut = Render<TmDocumentEditor>(parameters => parameters
            .Add(p => p.DocumentId, "canvas-editor-empty")
            .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        cut.Find(".tm-document-editor")
            .GetAttribute("data-render-engine")
            .Should()
            .Be("CanvasEnginePreview");
        cut.FindAll("[data-testid='document-wysiwyg-host']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-core-engine-host']").Should().BeEmpty();
    }

    [Fact]
    public void TmDocumentEditor_DefaultRenderEngine_RendersCanvasHostAfterPhase25Cutover()
    {
        SetupCanvasModule();
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedEmptyDocument("canvas-editor-default");

        var cut = Render<TmDocumentEditor>(parameters => parameters
            .Add(p => p.DocumentId, "canvas-editor-default")
            .Add(p => p.Provider, provider));

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());
        cut.Find(".tm-document-editor")
            .GetAttribute("data-render-engine")
            .Should()
            .Be("CanvasEnginePreview");
        cut.Find(".tm-document-editor")
            .GetAttribute("data-render-engine-requested")
            .Should()
            .BeNull();
        cut.FindAll("[data-testid='document-wysiwyg-host']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-core-engine-host']").Should().BeEmpty();
    }

    [Fact]
    public void TmDocumentEditor_ExplicitLegacyRenderEngine_IsIgnoredAfterCanvasCutover()
    {
        var provider = new InMemoryDocumentEditorProvider();
        provider.SeedEmptyDocument("canvas-editor-legacy-compat");

#pragma warning disable CS0618
        var cut = RenderDocumentEditor(parameters => parameters
            .Add(p => p.DocumentId, "canvas-editor-legacy-compat")
            .Add(p => p.Provider, provider)
            .Add(p => p.RenderEngine, DocumentEditorRenderEngine.Legacy));
#pragma warning restore CS0618

        cut.WaitForAssertion(() =>
            cut.Find("[data-testid='document-canvas-engine-host']").Should().NotBeNull());

        cut.Find(".tm-document-editor")
            .GetAttribute("data-render-engine")
            .Should()
            .Be("CanvasEnginePreview");
        cut.Find(".tm-document-editor")
            .GetAttribute("data-render-engine-requested")
            .Should()
            .BeNull();
        cut.FindAll("[data-testid='document-wysiwyg-host']").Should().BeEmpty();
        cut.FindAll("[data-testid='document-core-engine-host']").Should().BeEmpty();
    }

    [Fact]
    public async Task CanvasEngineHost_DisposeAsync_DisposesJavaScriptHandle()
    {
        var module = SetupCanvasModule();
        var document = DocumentEditorDocument.Empty("canvas-host-dispose");

        var cut = Render<TmDocumentCanvasEngineHost>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.AriaLabel, "Document editor")
            .Add(p => p.InputAriaLabel, "Document editor"));

        cut.WaitForAssertion(() => cut.Instance.IsReady.Should().BeTrue());
        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        module.Invocations.Any(invocation =>
            invocation.Identifier == "dispose" &&
            invocation.Arguments.Count == 1 &&
            invocation.Arguments[0]?.ToString() == "canvas-host-test-handle")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void CanvasEngineHost_ResolvesProviderAssetUrls_IntoMountedModel()
    {
        var module = SetupCanvasModule();
        var resolver = new CountingImageUrlResolver { UrlToReturn = "data:image/png;base64,RESOLVEDBITMAP" };
        var document = DocumentEditorDocument.Empty("canvas-host-assets");
        document.Assets.Add(new DocumentImageAsset { Id = "asset-1", Source = DocumentImageSource.Asset });

        var cut = Render<TmDocumentCanvasEngineHost>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.ImageUrlResolver, resolver)
            .Add(p => p.AriaLabel, "Document editor")
            .Add(p => p.InputAriaLabel, "Document editor"));

        cut.WaitForAssertion(() => cut.Instance.IsReady.Should().BeTrue());

        var mountCall = module.Invocations.First(invocation => invocation.Identifier == "mount");
        var modelJson = mountCall.Arguments[2]?.ToString() ?? string.Empty;
        modelJson.Should().Contain("data:image/png;base64,RESOLVEDBITMAP",
            because: "the host must resolve provider asset URLs into the model the engine renders");
        resolver.CallCount.Should().Be(1, because: "each asset URL is resolved once and cached");
        // The seed asset carried no URL of its own — proof the value came from the resolver.
        document.Assets[0].Url.Should().BeNullOrEmpty();
    }

    [Fact]
    public void CanvasEngineHost_WithoutResolver_MountsWithoutResolvingAssets()
    {
        var module = SetupCanvasModule();
        var document = DocumentEditorDocument.Empty("canvas-host-assets-no-resolver");
        document.Assets.Add(new DocumentImageAsset { Id = "asset-1", Source = DocumentImageSource.Asset });

        var cut = Render<TmDocumentCanvasEngineHost>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.AriaLabel, "Document editor")
            .Add(p => p.InputAriaLabel, "Document editor"));

        cut.WaitForAssertion(() => cut.Instance.IsReady.Should().BeTrue());
        module.Invocations.Any(invocation => invocation.Identifier == "mount").Should().BeTrue();
    }

    [Fact]
    public void CanvasEngineHost_ReassertsTrackChanges_AgainstMountedEngine()
    {
        // Mount-time race (20D carry-forward): a TrackChangesEnabled flip landing while the
        // awaited mount was in flight was dropped by SetTrackChangesEnabledAsync (_handle
        // still null), and the engine's updateOptions() ignores trackChanges (N192) — the
        // stale mount-time value would persist for the mount's life. The host must re-assert
        // the current parameter against the now-live engine; the call is idempotent.
        var module = SetupCanvasModule();
        var document = DocumentEditorDocument.Empty("canvas-host-track-changes");

        var cut = Render<TmDocumentCanvasEngineHost>(parameters => parameters
            .Add(p => p.Document, document)
            .Add(p => p.TrackChangesEnabled, true)
            .Add(p => p.AriaLabel, "Document editor")
            .Add(p => p.InputAriaLabel, "Document editor"));

        cut.WaitForAssertion(() => cut.Instance.IsReady.Should().BeTrue());

        module.Invocations.Any(invocation =>
                invocation.Identifier == "setTrackChangesEnabled" &&
                invocation.Arguments.Count >= 2 &&
                Equals(invocation.Arguments[1], true))
            .Should().BeTrue(
                "the host must re-assert trackChanges on the mounted engine — updateOptions() ignores the flag");
        module.Invocations.Any(invocation =>
                invocation.Identifier == "setReviewDisplayMode" &&
                invocation.Arguments.Count >= 2 &&
                Equals(invocation.Arguments[1], "AllMarkup"))
            .Should().BeTrue(
                "reviewDisplayMode sits inside the same mount-only trackChanges options — same race, same re-assert");
    }

    private sealed class CountingImageUrlResolver : IDocumentImageUrlResolver
    {
        public int CallCount;

        public string UrlToReturn { get; set; } = "data:image/png;base64,RESOLVED";

        public Task<string> ResolveUrlAsync(string documentId, string assetId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            return Task.FromResult(UrlToReturn);
        }
    }

    private BunitJSModuleInterop SetupCanvasModule()
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.Setup<string>("mount", _ => true).SetResult("canvas-host-test-handle");
        module.Setup<bool>("isDirty", _ => true).SetResult(false);
        module.SetupVoid("markSaved", _ => true).SetVoidResult();
        module.SetupVoid("focus", _ => true).SetVoidResult();
        module.Setup<string?>("getFormattingStateJson", _ => true).SetResult("""{"bold":false,"italic":false,"underline":false,"alignment":"left"}""");
        module.Setup<string?>("getUndoStateJson", _ => true).SetResult("""{"canUndo":false,"canRedo":false}""");
        module.Setup<string?>("getSelectionStateJson", _ => true).SetResult("""{"isCollapsed":true,"pageIndex":0}""");
        module.Setup<string?>("getDiagnosticsJson", _ => true).SetResult("""{"architectureName":"CanvasDocumentEngine","pageSurfaceStrategy":"canvas-per-visible-page","pageCount":1}""");
        module.SetupVoid("dispose", _ => true).SetVoidResult();
        return module;
    }
}
