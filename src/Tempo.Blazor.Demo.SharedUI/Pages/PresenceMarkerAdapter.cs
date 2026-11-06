using System.Globalization;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// Demo-only adapter from the Notion editor's <see cref="CollaboratorCursor"/> to the AI package's
/// <see cref="PresenceMarker"/>.
/// </summary>
/// <remarks>
/// The adapter lives in the DEMO on purpose: <c>Tempo.Blazor.AI</c> must not reference the Notion editor (nor any other
/// package's transport), so a host that already receives collaborator cursors writes this small mapping itself —
/// exactly as the demo does. Everything the overlay needs is a generic <see cref="AgentRef"/> plus a position, and this
/// class is the proof that a real collaboration feed fits that shape without a single change to the component.
/// </remarks>
internal static class PresenceMarkerAdapter
{
    /// <summary>The anchor value the demo document puts on a block's element.</summary>
    /// <param name="blockId">The cursor's block.</param>
    /// <returns>The <c>data-presence-anchor</c> value for that block.</returns>
    public static string AnchorOf(Guid blockId)
        => "cursor-block-" + blockId.ToString("n", CultureInfo.InvariantCulture);

    /// <summary>
    /// Maps one collaborator cursor to one presence marker. The user id is the agent id (so two cursors of one user are
    /// one agent), the display name is the name and the user id picks a stable slot of the agent palette, because the
    /// cursor's own colour string belongs to the editor's theme rather than to this package's tokens.
    /// </summary>
    /// <param name="cursor">The cursor the collaboration feed reported.</param>
    /// <returns>The marker the overlay paints.</returns>
    public static PresenceMarker ToMarker(CollaboratorCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        return new PresenceMarker("cursor-" + cursor.BlockId.ToString("n", CultureInfo.InvariantCulture), new AgentRef(
            cursor.DisplayName,
            cursor.UserId)
        {
            AvatarUrl = cursor.AvatarUrl,
            ColorIndex = StableColorIndex(cursor.UserId),
        })
        {
            AnchorId = AnchorOf(cursor.BlockId),
            Activity = PresenceActivity.Typing,
        };
    }

    /// <summary>Maps a whole feed, keeping the order the feed reported.</summary>
    /// <param name="cursors">The cursors, or null for an empty list.</param>
    /// <returns>The markers, one per cursor.</returns>
    public static IReadOnlyList<PresenceMarker> ToMarkers(IEnumerable<CollaboratorCursor>? cursors)
        => cursors is null ? [] : [.. cursors.Where(cursor => cursor is not null).Select(ToMarker)];

    /// <summary>A stable palette slot for a user id: the same user keeps the same colour across renders.</summary>
    /// <param name="userId">The user the cursor belongs to.</param>
    /// <returns>A slot in <c>0 … 7</c>.</returns>
    public static int StableColorIndex(string? userId)
    {
        var hash = 0;
        foreach (var character in userId ?? string.Empty)
        {
            hash = ((hash * 31) + character) & 0x7FFFFFFF;
        }

        return AgentRef.NormalizeColorIndex(hash);
    }
}
