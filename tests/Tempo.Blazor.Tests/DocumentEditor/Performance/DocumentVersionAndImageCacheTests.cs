using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor.Performance;
using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Tests.DocumentEditor.Performance;

/// <summary>Phase C — verifies the Document.Version counter (C1) and the ImageResolveCache (C4)
/// behave correctly. The host integration (C2) is tested indirectly via the Version field.</summary>
public sealed class DocumentVersionAndImageCacheTests
{
    [Fact]
    public void PhaseC1_BumpVersionIncrementsAndReturnsNewValue()
    {
        var doc = DocumentEditorDocument.Empty("doc-1");
        doc.Version.Should().Be(0);
        doc.BumpVersion().Should().Be(1);
        doc.Version.Should().Be(1);
        doc.BumpVersion().Should().Be(2);
        doc.Version.Should().Be(2);
    }

    [Fact]
    public void PhaseC1_VersionIsJsonIgnoredAndNotRoundTripped()
    {
        var doc = DocumentEditorDocument.Empty("doc-2");
        doc.BumpVersion();
        doc.BumpVersion();
        doc.BumpVersion();
        doc.Version.Should().Be(3);

        var json = System.Text.Json.JsonSerializer.Serialize(doc);
        json.Should().NotContain("\"Version\"", "Version is JsonIgnore — must not appear in payload");

        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<DocumentEditorDocument>(json);
        roundTrip!.Version.Should().Be(0, "deserialized docs start fresh");
    }

    [Fact]
    public void PhaseC4_ImageResolveCache_StoresAndReturnsUrl()
    {
        var cache = new ImageResolveCache(capacity: 8, ttl: TimeSpan.FromMinutes(5));
        cache.Set("doc-1", "asset-1", "https://cdn/asset-1.png");

        cache.TryGet("doc-1", "asset-1", out var url).Should().BeTrue();
        url.Should().Be("https://cdn/asset-1.png");
    }

    [Fact]
    public void PhaseC4_ImageResolveCache_MissReturnsFalse()
    {
        var cache = new ImageResolveCache(capacity: 8, ttl: TimeSpan.FromMinutes(5));
        cache.TryGet("doc-1", "asset-1", out var url).Should().BeFalse();
        url.Should().BeNull();
    }

    [Fact]
    public void PhaseC4_ImageResolveCache_EvictsLeastRecentlyUsedWhenFull()
    {
        var cache = new ImageResolveCache(capacity: 2, ttl: TimeSpan.FromMinutes(5));
        cache.Set("doc", "a", "url-a");
        cache.Set("doc", "b", "url-b");
        cache.Set("doc", "c", "url-c");  // evicts least-recently-used

        cache.Count.Should().Be(2);
        // 'a' was least recently set/touched → evicted
        cache.TryGet("doc", "a", out _).Should().BeFalse();
        cache.TryGet("doc", "b", out var bUrl).Should().BeTrue();
        bUrl.Should().Be("url-b");
        cache.TryGet("doc", "c", out var cUrl).Should().BeTrue();
        cUrl.Should().Be("url-c");
    }

    [Fact]
    public void PhaseC4_ImageResolveCache_MarksRecentlyAccessedAsNotEvictable()
    {
        var cache = new ImageResolveCache(capacity: 2, ttl: TimeSpan.FromMinutes(5));
        cache.Set("doc", "a", "url-a");
        cache.Set("doc", "b", "url-b");
        cache.TryGet("doc", "a", out _); // touch 'a' → moves to front
        cache.Set("doc", "c", "url-c");  // should evict 'b' (now LRU)

        cache.TryGet("doc", "a", out _).Should().BeTrue("'a' was touched, must survive");
        cache.TryGet("doc", "b", out _).Should().BeFalse("'b' was LRU after touch, should evict");
        cache.TryGet("doc", "c", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PhaseC4_ImageResolveCache_ExpiresEntriesAfterTtl()
    {
        var cache = new ImageResolveCache(capacity: 8, ttl: TimeSpan.FromMilliseconds(50));
        cache.Set("doc-1", "asset-1", "url-1");
        cache.TryGet("doc-1", "asset-1", out _).Should().BeTrue();

        // The barrier is the expired entry reporting a miss — the state the TTL produces — not a
        // fixed wall-clock margin a loaded machine can outrun.
        await WaitUntilAsync(() => !cache.TryGet("doc-1", "asset-1", out _));

        cache.TryGet("doc-1", "asset-1", out var url).Should().BeFalse("entry must expire after TTL");
        url.Should().BeNull();
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. A cache expiry produces no event, so the only
    /// honest barrier is the state itself — and a fixed delay can observe the pre-expiry state on a
    /// slow machine and read it as green.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue("the awaited state should hold within the timeout");
    }

    [Fact]
    public void PhaseC4_ImageResolveCache_NullUrlIsCachedAndReturnedAsNegativeHit()
    {
        var cache = new ImageResolveCache(capacity: 8, ttl: TimeSpan.FromMinutes(5));
        cache.Set("doc-1", "missing", null);

        cache.TryGet("doc-1", "missing", out var url).Should().BeTrue("null entry must register as a hit");
        url.Should().BeNull();
    }

    [Fact]
    public void PhaseC4_InvalidateDocument_RemovesAllEntriesForDocument()
    {
        var cache = new ImageResolveCache(capacity: 8, ttl: TimeSpan.FromMinutes(5));
        cache.Set("doc-1", "a", "url-a");
        cache.Set("doc-1", "b", "url-b");
        cache.Set("doc-2", "a", "other-url-a");

        cache.InvalidateDocument("doc-1");

        cache.TryGet("doc-1", "a", out _).Should().BeFalse();
        cache.TryGet("doc-1", "b", out _).Should().BeFalse();
        cache.TryGet("doc-2", "a", out var doc2Url).Should().BeTrue();
        doc2Url.Should().Be("other-url-a");
    }
}
