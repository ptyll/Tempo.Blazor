namespace Tempo.Blazor.Models;

/// <summary>
/// One entry of a user menu (the account dropdown of <c>TmTopBar</c>). An entry is either a command
/// (<see cref="Href"/> for navigation, <see cref="OnClick"/> otherwise), a destructive command
/// (<see cref="IsDanger"/>, e.g. delete the account) or a separator between groups
/// (<see cref="Separator"/>).
/// </summary>
public sealed record UserMenuItem
{
    /// <summary>Stable identifier of the entry, unique inside one menu (used for test ids and @key).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Visible label; also the accessible name of the entry.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Optional icon name from <c>IconNames</c>.</summary>
    public string? Icon { get; init; }

    /// <summary>Navigation URL. When set the entry renders as a link; otherwise it is a button that raises <see cref="OnClick"/>.</summary>
    public string? Href { get; init; }

    /// <summary>
    /// Callback of a command entry (ignored when <see cref="Href"/> is set). The menu closes BEFORE
    /// the callback runs and awaits it. A callback that changes the HOST's own state is responsible
    /// for the render (`StateHasChanged()`) — it is a delegate invoked by a child component, not a
    /// <c>EventCallback</c> owned by the host, so no automatic re-render follows. A callback that
    /// only calls a service (navigation, a toast, a save) needs nothing.
    /// </summary>
    public Func<Task>? OnClick { get; init; }

    /// <summary>Renders the entry in the destructive style (a red label) — a signal that must not be the ONLY cue for screen readers; the label has to be self-explanatory.</summary>
    public bool IsDanger { get; init; }

    /// <summary>Renders the entry as a separator line instead of a command; <see cref="Label"/> is ignored.</summary>
    public bool Separator { get; init; }

    /// <summary>Creates an empty entry.</summary>
    public UserMenuItem() { }

    /// <summary>Creates a command entry.</summary>
    /// <param name="id">Stable identifier of the entry.</param>
    /// <param name="label">Visible label; also the accessible name.</param>
    /// <param name="icon">Optional icon name from <c>IconNames</c>.</param>
    /// <param name="href">Navigation URL, or null for a callback entry.</param>
    /// <param name="onClick">Callback of a command entry; a callback that changes the host's own state calls <c>StateHasChanged()</c>.</param>
    /// <param name="isDanger">Renders the destructive style.</param>
    public UserMenuItem(
        string id,
        string label,
        string? icon = null,
        string? href = null,
        Func<Task>? onClick = null,
        bool isDanger = false)
    {
        Id = id;
        Label = label;
        Icon = icon;
        Href = href;
        OnClick = onClick;
        IsDanger = isDanger;
    }

    /// <summary>Creates a separator between groups of entries.</summary>
    /// <param name="id">Stable identifier of the separator, unique inside one menu.</param>
    public static UserMenuItem CreateSeparator(string id) => new() { Id = id, Separator = true };
}
