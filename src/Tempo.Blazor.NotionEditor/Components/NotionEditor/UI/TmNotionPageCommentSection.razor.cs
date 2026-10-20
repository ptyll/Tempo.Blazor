using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

public partial class TmNotionPageCommentSection : ITmDraftGuard
{
    /// <summary>Page identifier whose page-level comment threads are displayed.</summary>
    [Parameter] public string PageId { get; set; } = string.Empty;

    /// <summary>Whether the page comment section is expanded.</summary>
    [Parameter] public bool Expanded { get; set; }

    /// <summary>Raised when the expanded state changes.</summary>
    [Parameter] public EventCallback<bool> OnExpandedChanged { get; set; }

    /// <summary>Raised after the comment count may have changed.</summary>
    [Parameter] public EventCallback OnCountChanged { get; set; }

    /// <summary>Raised when a user mention inside a comment is activated.</summary>
    [Parameter] public EventCallback<string> OnMentionClicked { get; set; }

    /// <summary>
    /// Pass-through of the embedded panel's <see cref="TmNotionPageCommentPanel.OnDraftChanged"/>:
    /// none of the panel's composer texts is observable through a parameter, so this section
    /// forwards the panel's deduped <see cref="TmNotionCommentDraftState"/> transitions to the
    /// host. Pair with <see cref="HasUnsavedDraft"/> for polling and
    /// <see cref="DiscardDraftAsync"/> for the discard after the host's own confirmation.
    /// </summary>
    [Parameter] public EventCallback<TmNotionCommentDraftState> OnDraftChanged { get; set; }

    private TmNotionPageCommentPanel? _panel;

    /// <summary>
    /// Whether the embedded panel currently holds an unsaved comment draft (any composer). The
    /// same value the panel reports through <see cref="OnDraftChanged"/>, read live from the
    /// panel — <see langword="false"/> before the first render (the panel <c>@ref</c> is not yet
    /// assigned), which includes a section that has never been expanded.
    /// <para>
    /// Read it on the component's dispatcher (e.g. from an event handler or via
    /// <c>InvokeAsync</c>) when called from a background thread.
    /// </para>
    /// </summary>
    public bool HasUnsavedDraft => _panel?.HasUnsavedDraft ?? false;

    /// <summary>
    /// Discards every comment draft in the embedded panel (all composers, open reply and edit
    /// states). Intended for the host AFTER the user confirmed the discard in the host's OWN
    /// confirmation dialog. A no-op before the first render (while the panel is not rendered);
    /// safe to call from outside the render cycle.
    /// </summary>
    public Task DiscardDraftAsync()
        => _panel is null ? Task.CompletedTask : _panel.DiscardDraftAsync();
}
