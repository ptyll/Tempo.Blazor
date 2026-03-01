namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents basic user information used by TmAvatar, TmUserAvatar, and TmTopBar.
/// </summary>
public interface IUserInfo
{
    /// <summary>Unique user identifier.</summary>
    string Id { get; }

    /// <summary>Full display name (used for initials generation if AvatarSrc is null).</summary>
    string DisplayName { get; }

    /// <summary>Optional avatar image URL.</summary>
    string? AvatarSrc { get; }

    /// <summary>Optional email address.</summary>
    string? Email { get; }
}
