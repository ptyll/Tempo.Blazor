using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Feedback;

namespace Tempo.Blazor.Services;

/// <summary>
/// Configuration for a toast shown through <see cref="ToastService.Show"/>. Purely additive over
/// the classic <c>ShowSuccess/ShowInfo/ShowWarning/ShowError</c> helpers: every property is
/// optional and the defaults reproduce a classic info toast.
/// </summary>
public sealed record ToastOptions
{
    /// <summary>Message content of the toast.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Optional title rendered above the message.</summary>
    public string? Title { get; init; }

    /// <summary>Severity level of the toast. Defaults to <see cref="ToastSeverity.Info"/>.</summary>
    public ToastSeverity Severity { get; init; } = ToastSeverity.Info;

    /// <summary>
    /// Duration in milliseconds before auto-dismiss. Default is 5000ms.
    /// A value &lt;= 0 makes the toast "sticky" (never auto-dismisses).
    /// </summary>
    public int Duration { get; init; } = 5000;

    /// <summary>
    /// Optional dedupe key: showing another toast with the same key REPLACES the existing one in
    /// place — same id, same queue position — and restarts its auto-dismiss timer. Useful for
    /// progress-style notifications ("Saving…" → "Saved").
    /// </summary>
    public string? Key { get; init; }

    /// <summary>Optional action button (e.g. Undo) rendered inside the toast body.</summary>
    public ToastAction? Action { get; init; }

    /// <summary>Optional custom <see cref="RenderFragment"/> rendered below the message.</summary>
    public RenderFragment? Content { get; init; }

    /// <summary>
    /// Live-region politeness of the announcement. <c>null</c> derives from severity:
    /// <see cref="ToastSeverity.Error"/> is <see cref="ToastPoliteness.Assertive"/>, everything
    /// else <see cref="ToastPoliteness.Polite"/>.
    /// </summary>
    public ToastPoliteness? Politeness { get; init; }
}

/// <summary>
/// An action button rendered inside a toast (the classic use is Undo). The label resolves through
/// <c>ITmLocalizer</c> when <see cref="LabelKey"/> is set, so services stay localization-free.
/// </summary>
public sealed record ToastAction
{
    /// <summary>The localization key of the built-in Undo label ("TmToast_Undo", en/cs/sk/fr).</summary>
    public const string UndoLabelKey = "TmToast_Undo";

    /// <summary>Plain-text label. Ignored when <see cref="LabelKey"/> is set.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// Localization key resolved through <c>ITmLocalizer</c> at render time. Wins over
    /// <see cref="Label"/>.
    /// </summary>
    public string? LabelKey { get; init; }

    /// <summary>
    /// Delegate invoked when the user activates the action; the toast closes afterwards. The
    /// button is a native <c>&lt;button&gt;</c>, so keyboard activation needs no emulation.
    /// </summary>
    public Func<Task>? OnClick { get; init; }
}
