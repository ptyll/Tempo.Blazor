using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>
/// Renders a pill-bar of active collaborator avatars in the editor topbar and drives
/// per-block cursor highlights via JS DOM manipulation (tmNotionEditor.updateCollabCursors).
/// Subscribes to NotionCollaborationSync.RemoteCursorMoved; no-op when CollaborationProvider absent.
/// </summary>
public partial class TmNotionCollaborationCursors : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── State ─────────────────────────────────────────────────────────────────

    // userId → latest cursor — only remote users (own cursor filtered by sync)
    private readonly Dictionary<string, CollaboratorCursor> _cursors = new();
    private string? _loadedPageId;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnInitializedAsync()
    {
        if (Context.CollaborationSync is not { } sync) return;

        sync.RemoteCursorMoved += HandleCursorMoved;
        await LoadActiveCollaboratorsAsync();
    }

    protected override async Task OnParametersSetAsync()
        => await LoadActiveCollaboratorsAsync();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_cursors.Count > 0)
            await PushCursorsToJsAsync();
        else
            await LoadActiveCollaboratorsAsync();
    }

    private async Task LoadActiveCollaboratorsAsync()
    {
        if (Context.CollaborationProvider is not { } provider)
            return;

        var pageId = Context.CurrentPageId;
        if (string.IsNullOrWhiteSpace(pageId) ||
            string.Equals(_loadedPageId, pageId, StringComparison.OrdinalIgnoreCase) && _cursors.Count > 0)
            return;

        _cursors.Clear();
        try
        {
            var active = (await provider.GetActiveCollaboratorsAsync(pageId)).ToArray();
            if (active.Length == 0)
            {
                await Task.Delay(250);
                active = (await provider.GetActiveCollaboratorsAsync(pageId)).ToArray();
            }

            foreach (var c in active.Where(c => !string.Equals(c.UserId, Context.CurrentUserId, StringComparison.OrdinalIgnoreCase)))
                _cursors[c.UserId] = c;

            if (_cursors.Count > 0)
                _loadedPageId = pageId;

            if (_cursors.Count > 0)
            {
                StateHasChanged();
                await PushCursorsToJsAsync();
            }
        }
        catch { }
    }

    // ── Cursor handling ───────────────────────────────────────────────────────

    private async void HandleCursorMoved(CollaboratorCursor cursor)
    {
        // Event-handler signature: an exception escaping an async void handler is unhandled and
        // takes the whole circuit down, so the render + JS push is fenced.
        try
        {
            _cursors[cursor.UserId] = cursor;
            await InvokeAsync(async () =>
            {
                StateHasChanged();
                await PushCursorsToJsAsync();
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException or JSDisconnectedException or ObjectDisposedException)
        {
            // The pill bar is mid-dispose; the next page load re-subscribes.
        }
    }

    private async Task PushCursorsToJsAsync()
    {
        try
        {
            var dtos = _cursors.Select(kv => new
            {
                blockId     = kv.Value.BlockId.ToString(),
                displayName = kv.Value.DisplayName,
                color       = GetColor(kv.Key)
            }).ToArray();

            await JS.InvokeVoidAsync("tmNotionEditor.updateCollabCursors", (object)dtos);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly string[] _palette =
    [
        "var(--tm-collab-color-1)",
        "var(--tm-collab-color-2)",
        "var(--tm-collab-color-3)",
        "var(--tm-collab-color-4)",
        "var(--tm-collab-color-5)",
        "var(--tm-collab-color-6)",
        "var(--tm-collab-color-7)",
        "var(--tm-collab-color-8)"
    ];

    private static string GetColor(string userId)
    {
        var idx = StablePaletteIndex(userId);
        return _palette[idx];
    }

    private static int StablePaletteIndex(string userId)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var ch in userId)
        {
            hash ^= char.ToUpperInvariant(ch);
            hash *= prime;
        }

        return (int)(hash % _palette.Length);
    }

    private static string GetInitials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "?";
        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[^1][0]}"
            : displayName[..1];
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (Context.CollaborationSync is { } sync)
            sync.RemoteCursorMoved -= HandleCursorMoved;

        try
        {
            await JS.InvokeVoidAsync("tmNotionEditor.clearCollabCursors");
        }
        catch { }
    }
}
