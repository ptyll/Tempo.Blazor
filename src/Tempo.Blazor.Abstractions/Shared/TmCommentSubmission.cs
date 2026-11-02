namespace Tempo.Blazor.Abstractions.Shared;

/// <summary>
/// Payload of a newly posted comment from <c>TmActivityComments</c>.
/// Prefer <c>OnCommentSubmitted</c> when the host must know whether the composer was in
/// internal mode; <c>OnCommentAdded</c> still carries the same HTML and stays for hosts that
/// only need the body.
/// </summary>
/// <param name="Html">Submitted comment HTML, exactly as the composer held it.</param>
/// <param name="IsInternal">
/// <see langword="true"/> when the composer internal switch was on at submit. The component
/// does not hide the resulting comment — the consumer decides visibility.
/// </param>
public sealed record TmCommentSubmission(string Html, bool IsInternal);
