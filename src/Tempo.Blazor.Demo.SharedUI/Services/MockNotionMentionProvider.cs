using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// People provider for the Notion editor demo. Users are in-memory; page search
/// is handled by <see cref="MockNotionSearchProvider"/>.
/// </summary>
public class MockNotionMentionProvider : TmPeopleProviderBase
{
    private static readonly List<TmUser> _users =
    [
        new() { Id = "alice", UserName = "alice", DisplayName = "Alice Johnson", AvatarUrl = "https://i.pravatar.cc/150?u=alice", Email = "alice@demo.com" },
        new() { Id = "ada", UserName = "ada", DisplayName = "Ada Lovelace", AvatarUrl = "https://i.pravatar.cc/150?u=ada", Email = "ada@demo.com" },
        new() { Id = "grace", UserName = "grace", DisplayName = "Grace Hopper", AvatarUrl = "https://i.pravatar.cc/150?u=grace", Email = "grace@demo.com" },
        new() { Id = "linus", UserName = "linus", DisplayName = "Linus Torvalds", AvatarUrl = "https://i.pravatar.cc/150?u=linus", Email = "linus@demo.com" },
        new() { Id = "margaret", UserName = "margaret", DisplayName = "Margaret Hamilton", AvatarUrl = "https://i.pravatar.cc/150?u=margaret", Email = "margaret@demo.com" },
        new() { Id = "alan", UserName = "alan", DisplayName = "Alan Turing", AvatarUrl = "https://i.pravatar.cc/150?u=alan", Email = "alan@demo.com" },
        new() { Id = "zaneta", UserName = "zaneta", DisplayName = "Žaneta Černá", Email = "zaneta.cerna@demo.com" },
        new() { Id = "bob", UserName = "bob", DisplayName = "Bob Smith", AvatarUrl = "https://i.pravatar.cc/150?u=bob", Email = "bob@demo.com" },
        new() { Id = "charlie", UserName = "charlie", DisplayName = "Charlie Brown", Email = "charlie@demo.com" },
        new() { Id = "diana", UserName = "diana", DisplayName = "Diana Prince", AvatarUrl = "https://i.pravatar.cc/150?u=diana", Email = "diana@demo.com" },
        new() { Id = "demo", UserName = "demo", DisplayName = "Demo User", Email = "demo@demo.com" },
    ];

    /// <inheritdoc />
    public override Task<IReadOnlyList<TmUser>> SearchAsync(TmPeopleQuery query, CancellationToken cancellationToken = default)
    {
        var searchText = query.SearchText ?? string.Empty;
        IEnumerable<TmUser> results = string.IsNullOrWhiteSpace(searchText)
            ? _users
            : _users.Where(u =>
                u.Id.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                (u.UserName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                u.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                (u.Email?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false));

        if (query.Ids.Count > 0)
        {
            var ids = query.Ids.ToHashSet(StringComparer.Ordinal);
            results = results.Where(user => ids.Contains(user.Id));
        }

        var take = query.Take <= 0 ? _users.Count : query.Take;
        return Task.FromResult<IReadOnlyList<TmUser>>(results.Take(take).ToArray());
    }
}
