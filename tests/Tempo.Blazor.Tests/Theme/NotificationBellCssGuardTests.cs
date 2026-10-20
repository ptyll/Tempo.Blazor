using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the TmNotificationBell focus indicator (M1, 2.9.1 review): the rows are flush with the
/// panel edge and the dropdown clips overflow, so the focus indicator must draw INSIDE the row —
/// an outside ring is invisible on the first/last row exactly where a keyboard user lands.
/// </summary>
public sealed class NotificationBellCssGuardTests
{
    [Fact]
    public void ItemFocusIndicator_IsInsetInTheRow_NotAnOutsideRing()
    {
        ThemeCss.TryProperty("_notification-bell.css", ".tm-notification-bell__item:focus-visible", "outline")
            .Should().Be("2px solid var(--tm-color-primary)",
            "the visible indicator is the primary outline, not a transparent forced-colors stub");
        ThemeCss.TryProperty("_notification-bell.css", ".tm-notification-bell__item:focus-visible", "outline-offset")
            .Should().Be("-2px",
            "a negative offset draws the outline INSIDE the row — the panel clips an outside ring");
        ThemeCss.TryProperty("_notification-bell.css", ".tm-notification-bell__item:focus-visible", "box-shadow")
            .Should().Be("none",
            "the old outside box-shadow ring must be gone, not stacked under the outline");
    }
}
