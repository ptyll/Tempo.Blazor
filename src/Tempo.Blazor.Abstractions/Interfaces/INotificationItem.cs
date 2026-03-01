namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a notification item displayed in TmNotificationBell dropdown.
/// </summary>
public interface INotificationItem
{
    /// <summary>Unique identifier for the notification.</summary>
    string Id { get; }

    /// <summary>Notification title (short summary).</summary>
    string Title { get; }

    /// <summary>Optional detailed message body.</summary>
    string? Body { get; }

    /// <summary>When this notification was created.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>Whether the user has already read this notification.</summary>
    bool IsRead { get; }

    /// <summary>Optional icon name from IconNames constants.</summary>
    string? IconName { get; }

    /// <summary>Severity level used to pick a color/icon in the UI.</summary>
    NotificationSeverity Severity { get; }

    /// <summary>Optional navigation URL when the notification is clicked.</summary>
    string? ActionUrl { get; }
}

/// <summary>Severity level of a notification.</summary>
public enum NotificationSeverity { Info, Success, Warning, Error }
