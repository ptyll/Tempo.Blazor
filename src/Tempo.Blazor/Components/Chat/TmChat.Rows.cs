using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Row/key half of <see cref="TmChat"/>: the keyed row sequences the two markup loops render
/// and the <c>@key</c> fallback rules. The keyed <c>.tm-chat__item</c> wrapper itself is written
/// INLINE in <c>TmChat.razor</c> (the direct child of each <c>@foreach</c>) so Blazor matches
/// the wrappers across a prepend and keeps the row's component instances (a host's
/// <see cref="TmChat.MessageTemplate"/> state survives an older-page load); the helpers here
/// only compute the per-row inputs and the key. Split out of <c>TmChat.Scroll.cs</c> to keep
/// the scroll half about scrolling (the 1200-line source ceiling, N270/F13).
/// </summary>
public partial class TmChat
{
    /// <summary>
    /// The root sequence with the per-row template inputs AND the row key — the markup loop over
    /// this keeps the duplicate-Id fallback out of the .razor file. FZE-14: the rows come from
    /// <see cref="WindowedRootEntries"/> — the opt-in render window slices the keyed rows while
    /// the per-row inputs stay computed over the FULL sequence (grouping does not change at the
    /// window edge).
    /// </summary>
    private IEnumerable<(ChatMessage Message, int Index, bool IsFirstInGroup, string? Key)> RootEntriesWithKeys()
    {
        var duplicates = DuplicatedIds(RootMessages());
        foreach (var (message, index, isFirstInGroup) in WindowedRootEntries())
        {
            yield return (message, index, isFirstInGroup, MessageKey(message, duplicates));
        }
    }

    /// <summary>
    /// The thread panel sequence (root first, then replies ordered by timestamp) with the same
    /// per-row inputs and key as <see cref="RootEntriesWithKeys"/>. The sequence itself comes
    /// from <see cref="ThreadSequence"/>.
    /// </summary>
    private IEnumerable<(ChatMessage Message, int Index, bool IsFirstInGroup, string? Key)> ThreadEntriesWithKeys(ChatMessage root)
    {
        var sequence = ThreadSequence(root);
        var duplicates = DuplicatedIds(sequence);
        foreach (var (message, index, isFirstInGroup) in EntriesWithTemplateInputs(sequence))
        {
            yield return (message, index, isFirstInGroup, MessageKey(message, duplicates));
        }
    }

    /// <summary>
    /// The thread panel sequence: the root message first, then its replies ordered by timestamp.
    /// The list form lets the row-key computation see the whole rendered SEQUENCE.
    /// </summary>
    private IReadOnlyList<ChatMessage> ThreadSequence(ChatMessage root)
        => new[] { root }.Concat(RepliesOf(root.Id)).ToList();

    /// <summary>
    /// The <c>@key</c> of one message row: the message Id — but only when it is non-empty and
    /// unique among the rows rendered in the same container. The thread panel renders the root
    /// message a second time (page-wide <c>data-message-id</c> duplication), so keys are always
    /// computed per sequence, and a duplicated Id falls back to <see langword="null"/> (no key)
    /// instead of throwing.
    /// </summary>
    private static string? MessageKey(ChatMessage message, ISet<string> duplicatedIds)
        => string.IsNullOrEmpty(message.Id) || duplicatedIds.Contains(message.Id) ? null : message.Id;

    /// <summary>Ids that occur MORE than once in <paramref name="messages"/> (ignoring empty ones).</summary>
    private static ISet<string> DuplicatedIds(IEnumerable<ChatMessage> messages)
    {
        HashSet<string> duplicates = [];
        HashSet<string> seen = [];
        foreach (var message in messages)
        {
            if (string.IsNullOrEmpty(message.Id)) continue;
            if (!seen.Add(message.Id)) duplicates.Add(message.Id);
        }
        return duplicates;
    }
}
