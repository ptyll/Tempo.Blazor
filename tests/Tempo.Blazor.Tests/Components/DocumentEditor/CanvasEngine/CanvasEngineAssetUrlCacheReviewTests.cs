using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Components.DocumentEditor.Performance;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor.CanvasEngine;

/// <summary>
/// Fáze 21 (code review N8.3): ResolvedAssetUrlCache je STATIC, ale klíčoval se jen
/// (documentId, assetId) a ignoroval per-instance ImageUrlResolver — v Blazor Serveru se podepsaná
/// URL sdílela napříč circuity/tenanty (uživatel B dostal URL podepsané pro uživatele A a jeho
/// resolver se nezavolal). Navíc ImageResolveCache.Set při opakovaném zápisu téhož klíče
/// neodstranil starý LRU node: interní list rostl bez omezení a evikce mohla přes zastaralý
/// duplikát odstranit ČERSTVÝ záznam, zatímco skutečně nejstarší klíč přežil.
/// </summary>
public sealed class CanvasEngineAssetUrlCacheReviewTests : LocalizationTestBase
{
    private const string InteropModulePath = "./_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs";

    // ─── ImageResolveCache LRU ───────────────────────────────────────────────

    [Fact]
    public void RepeatedSetOfSameKey_DoesNotGrowInternalLruList()
    {
        var cache = new ImageResolveCache(capacity: 8, ttl: TimeSpan.FromMinutes(5));
        for (var i = 0; i < 100; i++)
        {
            cache.Set("doc", "asset", $"url-{i}");
        }

        cache.Count.Should().Be(1);
        cache.LruCountForTests.Should().Be(1,
            "opakovaný Set téhož klíče nesmí leakovat zastaralé LRU nody");
    }

    [Fact]
    public void EvictionAfterRepeatedSets_EvictsActuallyOldestKey()
    {
        var cache = new ImageResolveCache(capacity: 2, ttl: TimeSpan.FromMinutes(5));
        cache.Set("doc", "a", "url-a-1");
        cache.Set("doc", "b", "url-b");
        cache.Set("doc", "a", "url-a-2"); // refresh 'a' — bez fixu zůstal starý node 'a' na tailu
        cache.Set("doc", "c", "url-c");   // evikce: pryč musí jít 'b' (skutečně nejstarší), ne čerstvé 'a'

        cache.TryGet("doc", "a", out var aUrl).Should().BeTrue("'a' bylo čerstvě přepsáno a musí přežít evikci");
        aUrl.Should().Be("url-a-2");
        cache.TryGet("doc", "b", out _).Should().BeFalse("'b' je skutečně nejstarší a musí být evikováno");
        cache.TryGet("doc", "c", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ExpiredEntry_IsAMiss_AndFreshSetServesNextBuild()
    {
        // TTL kontrakt hostu: EnsureAssetUrlsResolvedAsync běží PŘED BuildCanvasModel v obou
        // call-sites — expirovaný záznam je miss, Ensure ho znovu resolvne a Set proběhne dřív,
        // než se model staví ⇒ obrázek se neztratí ani na jeden render.
        var cache = new ImageResolveCache(capacity: 8, ttl: TimeSpan.FromMilliseconds(40));
        cache.Set("doc", "asset", "signed-url-1");

        // Bariéra je stav, který TTL produkuje — expirovaný záznam hlásí miss — ne fixní
        // čekací doba, kterou zatížený stroj předběhne.
        var deadline = Environment.TickCount64 + 5000;
        while (cache.TryGet("doc", "asset", out _) && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        cache.TryGet("doc", "asset", out _).Should().BeFalse("po TTL je záznam miss → vynutí re-resolve");
        cache.Set("doc", "asset", "signed-url-2");
        cache.TryGet("doc", "asset", out var url).Should().BeTrue();
        url.Should().Be("signed-url-2");
    }

    // ─── Izolace resolverů na statickém cache ────────────────────────────────

    [Fact]
    public void TwoHostsWithDifferentResolvers_DoNotShareResolvedUrls()
    {
        var documentId = $"asset-cache-isolation-{Guid.NewGuid():N}";
        var resolverA = new RecordingResolver("https://tenant-a.example/signed");
        var resolverB = new RecordingResolver("https://tenant-b.example/signed");

        SetupCanvasModule();
        Render<TmDocumentCanvasEngineHost>(parameters => parameters
            .Add(p => p.Document, BuildDocumentWithAsset(documentId))
            .Add(p => p.ImageUrlResolver, resolverA));
        resolverA.Invocations.Should().Be(1, "první host musí URL rozřešit svým resolverem");

        var mountsBefore = CountMountModelJsonContaining("https://tenant-a.example/signed");
        mountsBefore.Should().BeGreaterThan(0, "model hostu A nese URL tenantu A");

        Render<TmDocumentCanvasEngineHost>(parameters => parameters
            .Add(p => p.Document, BuildDocumentWithAsset(documentId))
            .Add(p => p.ImageUrlResolver, resolverB));

        resolverB.Invocations.Should().Be(1,
            "STATIC cache nesmí přeskočit resolver druhé instance — jinak tenant B dostane URL podepsané pro tenant A");
        CountMountModelJsonContaining("https://tenant-b.example/signed").Should().BeGreaterThan(0,
            "model hostu B musí nést URL tenantu B, ne sdílenou URL tenantu A");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private int CountMountModelJsonContaining(string fragment)
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "mount")
            .Count(invocation => invocation.Arguments.Any(arg => arg is string s && s.Contains(fragment, StringComparison.Ordinal)));

    private static DocumentEditorDocument BuildDocumentWithAsset(string documentId)
    {
        var document = DocumentEditorDocument.Empty(documentId);
        document.Assets.Add(new DocumentImageAsset
        {
            Id = "asset-1",
            DocumentId = documentId,
            Url = null,
        });
        return document;
    }

    private void SetupCanvasModule()
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.Setup<string>("mount", _ => true).SetResult("canvas-host-test-handle");
        module.Setup<bool>("isDirty", _ => true).SetResult(false);
        module.SetupVoid("markSaved", _ => true).SetVoidResult();
        module.SetupVoid("focus", _ => true).SetVoidResult();
        module.Setup<string?>("getFormattingStateJson", _ => true).SetResult("""{"bold":false,"alignment":"left"}""");
        module.Setup<string?>("getUndoStateJson", _ => true).SetResult("""{"canUndo":false,"canRedo":false}""");
        module.Setup<string?>("getSelectionStateJson", _ => true).SetResult("""{"isCollapsed":true}""");
        module.Setup<string?>("getDiagnosticsJson", _ => true).SetResult("""{"architectureName":"CanvasDocumentEngine"}""");
        module.SetupVoid("dispose", _ => true).SetVoidResult();
    }

    private sealed class RecordingResolver(string baseUrl) : Tempo.Blazor.DocumentEditor.Interfaces.IDocumentImageUrlResolver
    {
        public int Invocations { get; private set; }

        public Task<string> ResolveUrlAsync(string documentId, string assetId, CancellationToken cancellationToken = default)
        {
            Invocations++;
            return Task.FromResult($"{baseUrl}/{assetId}");
        }
    }
}
