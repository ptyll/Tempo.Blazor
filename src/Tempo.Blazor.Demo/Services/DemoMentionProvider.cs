using Tempo.Blazor.Interfaces;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// Demo mention data provider for testing mentions in rich text editors.
/// </summary>
public class DemoMentionProvider : IMentionDataProvider
{
    private readonly List<DemoMentionUser> _users = new()
    {
        new DemoMentionUser("u1", "alice", "Alice Johnson", null),
        new DemoMentionUser("u2", "bob", "Bob Smith", "https://i.pravatar.cc/150?u=1"),
        new DemoMentionUser("u3", "charlie", "Charlie Brown", null),
        new DemoMentionUser("u4", "diana", "Diana Prince", "https://i.pravatar.cc/150?u=2"),
        new DemoMentionUser("u5", "eve", "Eve Davis", null),
    };

    public Task<IEnumerable<IMentionUser>> SearchUsersAsync(string query, CancellationToken ct = default)
    {
        var results = string.IsNullOrWhiteSpace(query)
            ? _users.Cast<IMentionUser>()
            : _users.Where(u => 
                u.UserName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Cast<IMentionUser>();

        return Task.FromResult(results);
    }

    public Task<IMentionUser?> GetUserByIdAsync(string id, CancellationToken ct = default)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        return Task.FromResult<IMentionUser?>(user);
    }
}

/// <summary>
/// Demo mention user implementation.
/// </summary>
public class DemoMentionUser : IMentionUser
{
    public DemoMentionUser(string id, string userName, string displayName, string? avatarUrl)
    {
        Id = id;
        UserName = userName;
        DisplayName = displayName;
        AvatarUrl = avatarUrl;
    }

    public string Id { get; }
    public string UserName { get; }
    public string DisplayName { get; }
    public string? AvatarUrl { get; }
}
