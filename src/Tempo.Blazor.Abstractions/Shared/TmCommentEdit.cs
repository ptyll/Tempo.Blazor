namespace Tempo.Blazor.Abstractions.Shared;

/// <summary>
/// Payload of an inline edit save from <c>TmActivityComments</c>. Raised through
/// <c>CommentEditHandler</c> when the host needs to know whether the save succeeded; the
/// legacy <c>OnCommentEdited</c> callback carries the same data but cannot report failure.
/// </summary>
/// <param name="CommentId">Id of the comment being edited (<c>TmCommentEntry.Id</c>).</param>
/// <param name="Html">Edited comment HTML, exactly as the inline editor held it.</param>
public sealed record TmCommentEdit(string CommentId, string Html);
