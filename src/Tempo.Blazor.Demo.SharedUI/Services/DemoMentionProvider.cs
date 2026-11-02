using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// Demo mention data provider for testing mentions in rich text editors.
/// </summary>
public class DemoMentionProvider : TmPeopleProviderBase
{
    private readonly List<TmUser> _users =
    [
        new()
        {
            Id = "u6",
            UserName = "alexandrina",
            DisplayName = "Alexandrina Montgomery-Featherstone of the Western Reach"
        },
        new() { Id = "u1", UserName = "alice", DisplayName = "Alice Johnson" },
        new() { Id = "u2", UserName = "bob", DisplayName = "Bob Smith", AvatarUrl = "https://i.pravatar.cc/150?u=1" },
        new() { Id = "u3", UserName = "charlie", DisplayName = "Charlie Brown" },
        new() { Id = "u4", UserName = "diana", DisplayName = "Diana Prince", AvatarUrl = "https://i.pravatar.cc/150?u=2" },
        new() { Id = "u5", UserName = "eve", DisplayName = "Eve Davis" },
    ];

    /// <inheritdoc />
    public override Task<IReadOnlyList<TmUser>> SearchAsync(TmPeopleQuery query, CancellationToken ct = default)
    {
        var searchText = query.SearchText ?? string.Empty;
        IEnumerable<TmUser> results = string.IsNullOrWhiteSpace(searchText)
            ? _users
            : _users.Where(u => 
                (u.UserName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                u.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        if (query.Ids.Count > 0)
        {
            var idSet = query.Ids.ToHashSet(StringComparer.Ordinal);
            results = results.Where(user => idSet.Contains(user.Id));
        }

        if (!query.IncludeVirtual)
        {
            results = results.Where(user => !user.IsVirtual);
        }

        var take = query.Take <= 0 ? _users.Count : query.Take;
        return Task.FromResult<IReadOnlyList<TmUser>>(results.Take(take).ToArray());
    }
}
