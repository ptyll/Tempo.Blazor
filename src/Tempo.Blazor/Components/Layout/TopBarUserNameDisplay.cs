namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// Whether the top bar's account button shows the user's display name next to the avatar
/// (<see cref="TmTopBar.UserNameDisplay"/>).
/// </summary>
public enum TopBarUserNameDisplay
{
    /// <summary>
    /// The default: the name shows on a wide bar and hides when the bar's own box narrows below
    /// 480px (the avatar and the menu behind it stay). The breakpoint is a container query on the
    /// bar itself, never the viewport.
    /// </summary>
    Auto,

    /// <summary>
    /// The name renders at every width; on a narrow bar it ellipsises instead of hiding.</summary>
    Always,

    /// <summary>The name never renders; the account button is the avatar alone (the localized
    /// accessible name on the button is the only naming).</summary>
    Never,
}
