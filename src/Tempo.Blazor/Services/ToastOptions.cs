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
    /// <see cref="ToastSeverity.Warning"/> and <see cref="ToastSeverity.Error"/> are
    /// <see cref="ToastPoliteness.Assertive"/>, everything else <see cref="ToastPoliteness.Polite"/>.
    /// </summary>
    public ToastPoliteness? Politeness { get; init; }

    /// <summary>
    /// Loading state (the promise-toast pattern): the card renders a spinner instead of the
    /// severity icon and no progress bar. A loading toast is sticky regardless of
    /// <see cref="Duration"/> — it stays until updated or removed. Defaults to <c>false</c>.
    /// </summary>
    public bool IsLoading { get; init; }
}

/// <summary>
/// Configuration for <see cref="ToastService.PromiseAsync{T}"/>: the three phases of a
/// long-running operation surfaced through ONE toast — a sticky loading state, an in-place update
/// to <see cref="ToastSeverity.Success"/> (or <see cref="ToastSeverity.Error"/>) when the
/// operation settles. The service stays localization-free: the caller supplies every message.
/// </summary>
public sealed record ToastPromiseOptions
{
    /// <summary>Message of the sticky loading toast shown while the operation runs.</summary>
    public string LoadingMessage { get; init; } = string.Empty;

    /// <summary>Optional title of the loading toast.</summary>
    public string? LoadingTitle { get; init; }

    /// <summary>
    /// Success message builder; receives the operation result. <c>null</c> keeps the loading
    /// message on success.
    /// </summary>
    public Func<object?, string>? SuccessMessage { get; init; }

    /// <summary>Optional title set on the success update.</summary>
    public string? SuccessTitle { get; init; }

    /// <summary>
    /// Duration (milliseconds) of the success phase. Defaults to 5000 (the standard reading time).
    /// A value &lt;= 0 makes the success toast sticky.
    /// </summary>
    public int SuccessDuration { get; init; } = 5000;

    /// <summary>
    /// Error message builder; receives the thrown exception. <c>null</c> keeps the loading message
    /// on error.
    /// </summary>
    public Func<Exception, string>? ErrorMessage { get; init; }

    /// <summary>Optional title set on the error update.</summary>
    public string? ErrorTitle { get; init; }

    /// <summary>
    /// Duration (milliseconds) of the error phase. Defaults to 8000. A value &lt;= 0 makes the
    /// error toast sticky.
    /// </summary>
    public int ErrorDuration { get; init; } = 8000;

    /// <summary>
    /// Optional dedupe key of the whole flow: a second <see cref="ToastService.PromiseAsync{T}"/>
    /// with the same key replaces the pending toast in place instead of stacking a second one.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Live-region politeness applied to every phase. <c>null</c> derives from severity per phase
    /// (the error phase is announced assertively).
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
