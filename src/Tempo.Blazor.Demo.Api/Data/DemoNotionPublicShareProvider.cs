using System.Security.Cryptography;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

public sealed class DemoNotionPublicShareProvider : INotionPublicShareProvider
{
    public const string ExpiredE2EToken = "cf33-expired";

    private readonly object _syncRoot = new();
    private readonly MockNotionDataStore _dataStore;
    private readonly Dictionary<Guid, PublicShareDto> _sharesByPage = new();
    private readonly Dictionary<string, Guid> _pageIdsByToken = new(StringComparer.OrdinalIgnoreCase);

    public DemoNotionPublicShareProvider(MockNotionDataStore dataStore)
        => _dataStore = dataStore;

    public async Task<PublicShareDto> CreateShareAsync(Guid pageId, PublicShareOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        await _dataStore.GetPageAsync(pageId.ToString("D"));

        var normalized = new PublicShareDto
        {
            PageId = pageId,
            Token = CreateUniqueToken(),
            IsEnabled = true,
            AllowComments = options.AllowComments,
            ExpiresAt = NormalizeExpiry(options.ExpiresAt)
        };

        lock (_syncRoot)
        {
            RemoveExistingToken(pageId);
            _sharesByPage[pageId] = Clone(normalized);
            _pageIdsByToken[normalized.Token] = pageId;
        }

        return Clone(normalized);
    }

    public Task RevokeAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            if (_sharesByPage.TryGetValue(pageId, out var share))
            {
                share.IsEnabled = false;
                _pageIdsByToken.Remove(share.Token);
            }
        }

        return Task.CompletedTask;
    }

    public Task<PublicShareDto?> GetShareAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_syncRoot)
        {
            return Task.FromResult(_sharesByPage.TryGetValue(pageId, out var share) ? Clone(share) : null);
        }
    }

    public Task<PublicShareDto?> ResolveByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult<PublicShareDto?>(null);

        lock (_syncRoot)
        {
            if (!_pageIdsByToken.TryGetValue(token.Trim(), out var pageId) || !_sharesByPage.TryGetValue(pageId, out var share))
                return Task.FromResult<PublicShareDto?>(null);

            return Task.FromResult(IsResolvable(share) ? Clone(share) : null);
        }
    }

    public void Reset()
    {
        lock (_syncRoot)
        {
            _sharesByPage.Clear();
            _pageIdsByToken.Clear();
        }
    }

    public void SeedE2EActiveShare()
    {
        lock (_syncRoot)
        {
            Reset();
            var share = new PublicShareDto
            {
                PageId = MockNotionDataStore.Page1Id,
                Token = "cf33-active",
                IsEnabled = true,
                AllowComments = true,
                ExpiresAt = DateTime.UtcNow.AddDays(14)
            };
            _sharesByPage[share.PageId] = Clone(share);
            _pageIdsByToken[share.Token] = share.PageId;
        }
    }

    public void SeedE2EExpiredShare()
    {
        lock (_syncRoot)
        {
            Reset();
            var share = new PublicShareDto
            {
                PageId = MockNotionDataStore.Page1Id,
                Token = ExpiredE2EToken,
                IsEnabled = true,
                AllowComments = false,
                ExpiresAt = DateTime.UtcNow.AddDays(-1)
            };
            _sharesByPage[share.PageId] = Clone(share);
            _pageIdsByToken[share.Token] = share.PageId;
        }
    }

    private string CreateUniqueToken()
    {
        Span<byte> bytes = stackalloc byte[18];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var token = Convert.ToBase64String(bytes)
                .Replace("+", "-", StringComparison.Ordinal)
                .Replace("/", "_", StringComparison.Ordinal)
                .TrimEnd('=');

            lock (_syncRoot)
            {
                if (!_pageIdsByToken.ContainsKey(token))
                    return token;
            }
        }
    }

    private void RemoveExistingToken(Guid pageId)
    {
        if (_sharesByPage.TryGetValue(pageId, out var existing))
            _pageIdsByToken.Remove(existing.Token);
    }

    private static bool IsResolvable(PublicShareDto share)
        => share.IsEnabled && (share.ExpiresAt is null || share.ExpiresAt.Value > DateTime.UtcNow);

    private static DateTime? NormalizeExpiry(DateTime? expiresAt)
        => expiresAt?.Kind switch
        {
            DateTimeKind.Utc => expiresAt,
            DateTimeKind.Local => expiresAt.Value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(expiresAt.Value, DateTimeKind.Utc),
            _ => null
        };

    private static PublicShareDto Clone(PublicShareDto share)
        => new()
        {
            PageId = share.PageId,
            Token = share.Token,
            IsEnabled = share.IsEnabled,
            AllowComments = share.AllowComments,
            ExpiresAt = share.ExpiresAt
        };
}
