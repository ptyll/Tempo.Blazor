namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Controls when <see cref="TmChat"/> scrolls the message list to the bottom on its own (FZE-6).
/// The default is <see cref="WhenAtBottom"/> — the only documented change of TmChat's default
/// behavior (the pre-2.10 <c>ScrollToBottomAsync</c> was a no-op); hosts that need the old
/// behavior opt out with <see cref="Never"/>.
/// <para>
/// Nothing scrolls before interactivity: during prerender (and any render without a live JS
/// circuit) the <c>chat.js</c> scroller is not attached and every automatic scroll is a no-op —
/// the first INTERACTIVE render lands at the bottom. The change classification reads the
/// RENDERED root messages (thread replies render in the panel): first AND last root Id
/// unchanged → no message change; the old last root Id interior to the list → an append even
/// when a sliding-window feed trimmed the head (the unread count is the rows after the old
/// last, not the count delta); the old first root Id moved down → a prepend; only when neither
/// boundary Id survives is it a reset. A PREPEND never scrolls — the scroller restores the
/// reading position on its first-visible-row anchor (falling back to the grown-height delta
/// when the anchor row is gone). The scroller's <c>pinned</c> flag (set by every programmatic
/// scroll, released only by a user scroll upward) keeps appends that land inside one smooth
/// programmatic scroll scrolling instead of counting them as unread.
/// </para>
/// </summary>
public enum ChatAutoScroll
{
    /// <summary>
    /// Never scrolls on its own — the pre-2.10 behavior. No <c>scrollToBottom</c>, no position
    /// preservation and no unread counting: the host owns every scroll (the public
    /// <see cref="TmChat.ScrollToBottomAsync"/> / <see cref="TmChat.ScrollToMessageAsync"/>
    /// helpers still work when called explicitly, and the jump-to-new button does not render).
    /// </summary>
    Never = 0,

    /// <summary>
    /// Scrolls to the bottom on the first render and afterwards only while the user is actually
    /// reading at the bottom (the <c>chat.js</c> scroller reports the at-bottom state). Messages
    /// arriving while the user reads above the bottom are counted behind the jump-to-new button
    /// instead of stealing the reading position; the user's OWN messages always scroll. A
    /// prepend of older history never scrolls — the visual position is preserved on the
    /// scroller's first-visible-row anchor. A sliding-window feed (new messages plus a trimmed
    /// head) still counts as an append: the old last Id sits interior to the list.
    /// </summary>
    WhenAtBottom = 1,

    /// <summary>
    /// Scrolls to the bottom on every message change, regardless of the reading position
    /// (pinned-bottom chats, e.g. a moderated Q&amp;A wall). Older history is not preserved —
    /// a prepend scrolls to the bottom too.
    /// </summary>
    Always = 2,
}
