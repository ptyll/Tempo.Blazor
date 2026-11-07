using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Phase 5 review (U8): the inspector's list actions own their focus movement and their polite
/// announcement. Adding an item moves focus to the NEW row's first input; removing one moves it to
/// the next row's remove button (else back to the add button) — so a keyboard user never loses their
/// place when a row disappears under them. The move rides the shared CSP-safe
/// <c>modeling-editor.js</c> <c>focusWithin</c> export, exactly like the editor's other focus paths.
/// </summary>
public partial class TmModelingInspector
{
    /// <summary>The inspector ROOT — the focusWithin scope of every list-action focus move.</summary>
    private ElementReference _inspectorRoot;

    /// <summary>The DOM id of the input an add action just created (focus target of the next render).</summary>
    private string? _pendingItemAddFocus;

    /// <summary>The DOM id of the control a remove action wants focused (next remove button / add).</summary>
    private string? _pendingItemRemoveFocus;

    /// <summary>The polite announcement of the last list action (localized; reuses one live region).</summary>
    private string? _itemAnnouncement;

    private ModelingInteropModule? _interopModule;

    /// <summary>Lazily imported <c>modeling-editor.js</c> (progressive enhancement — a failure is not
    /// cached and never breaks the component).</summary>
    private ModelingInteropModule Interop => _interopModule ??= new ModelingInteropModule(JS);

    /// <summary>The JS runtime the focus move needs (injected; a bUnit test leaves it loose).</summary>
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Test seam (InternalsVisibleTo): the announcement text of the last list action.</summary>
    internal string? TestItemAnnouncement => _itemAnnouncement;

    /// <summary>The focus hook of U7's "already seeded errors are quiet" rule plus U8's list focus: the
    /// inspector renders once, then moves focus where the action asked.</summary>
    private async Task ApplyPendingListFocusAsync()
    {
        var selectorId = _pendingItemAddFocus ?? _pendingItemRemoveFocus;
        if (selectorId is null)
        {
            return;
        }

        _pendingItemAddFocus = null;
        _pendingItemRemoveFocus = null;
        await Interop.InvokeVoidSafeAsync("focusWithin", _inspectorRoot, $"#{selectorId}");
    }

    /// <summary>Releases the interop module and cancels in-flight label re-resolutions / lookups.</summary>
    public async ValueTask DisposeAsync()
    {
        CancelReferenceResolution();

        foreach (var state in _referenceLookup.Values)
        {
            state.Cancellation?.Cancel();
            state.Cancellation?.Dispose();
        }

        _referenceLookup.Clear();

        if (_interopModule is not null)
        {
            await _interopModule.DisposeAsync();
        }
    }
}
