using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.Signing;

/// <content>
/// Layout classification and signing-runner.js plumbing of the runner (phase 5 review split — a
/// pure move from <c>TmSigningFormRunner.razor.cs</c>): the F1 self-classification
/// (<see cref="LayoutMode"/> / <see cref="ResolvedLayoutChanged"/> and the observer callbacks),
/// the SI3 signature-mode resolution over the coarse-pointer probe, and the active-field
/// auto-scroll. The scroll is REQUESTED by the navigation handlers (they only record the pending
/// field uuid) and PERFORMED by <c>OnAfterRenderAsync</c> after the render has applied the new
/// sheet state — measuring mid-handler would read the sheet before the class flip.
/// </content>
public partial class TmSigningFormRunner
{
    // Layout classification (F1, Signing redesign phase 5): the observer's resolved mode, written
    // from the markup's LayoutContent on every render BEFORE any part reads it — the same
    // reconciliation the Gantt uses, so the first measurement of a page mounted in a narrow
    // container still lands without waiting for ResolvedLayoutChanged.
    private TmLayoutMode _layout = TmLayoutMode.Desktop;

    // signing-runner.js interop (phase 5): created lazily, so a never-scrolled / never-probed
    // runner never imports the module. Disposed by the runner's DisposeAsync.
    private SigningRunnerInterop? _runnerInterop;

    // SI3: the coarse-pointer probe result (Auto → Draw on a finger). Resolved once after the
    // first render; an unresolved probe reads "fine pointer", which is today's behaviour.
    private bool _coarsePointer;
    private bool _coarsePointerResolved;

    // The scroll REQUESTED by a step landing (uuid of the field to centre above the sheet) and
    // performed ONCE by OnAfterRenderAsync after the render applied the sheet state (phase 5
    // review: handlers never measure — they set the pending uuid, the after-render pass scrolls).
    private string? _pendingScrollFieldUuid;

    /// <summary>
    /// The layout the runner renders (Signing redesign phase 5). <see cref="TmLayoutMode.Auto"/>
    /// measures the runner's own container — a container narrower than the mobile breakpoint
    /// presents the steps rail as the bottom sheet (peek / expanded / full) with radio-card choice
    /// fields (SI1); wider containers keep the docked desktop panel. A forced value never
    /// measures, so a test (or a host that already knows the width) can pin the presentation
    /// without a DOM.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// Raised when the resolved layout changes. The initial value is not a change. Never pair
    /// <see cref="LayoutMode"/> with this callback: it reports the resolved mode, and a two-way
    /// bind would pin that mode.
    /// </summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <summary>What the observer is asked for: only the forced value — the runner has no legacy
    /// width parameter, so Auto always means "measure my own container".</summary>
    private TmLayoutMode LayoutForObserver => LayoutMode;

    /// <summary>The resolved mode the steps hand to their parts (never Auto).</summary>
    private TmLayoutMode ResolvedLayout => _layout;

    /// <summary>
    /// The layout the markup is about to draw. Runs from <c>LayoutContent</c> on every render, so a
    /// flip is reconciled HERE even when the observer's <see cref="OnResolvedLayoutChanged"/> did
    /// not deliver it (the first measurement of a runner mounted in a narrow container).
    /// </summary>
    private void ApplyLayout(TmLayoutMode resolved)
    {
        _layout = resolved;
    }

    private Task OnResolvedLayoutChanged(TmLayoutMode mode)
    {
        ApplyLayout(mode);
        return ResolvedLayoutChanged.InvokeAsync(mode);
    }

    /// <summary>The BEM modifier for the resolved layout, folded into the root class list (desktop
    /// renders no modifier). The sheet presentation itself stays container-query driven.</summary>
    private string LayoutClass => _layout switch
    {
        TmLayoutMode.Mobile => "tm-signing-form-runner--mobile",
        TmLayoutMode.Tablet => "tm-signing-form-runner--tablet",
        _ => string.Empty
    };

    /// <summary>
    /// The coarse-pointer probe (SI3), run once after the first render: <see cref="TmSigningDefaultSignatureMode.Auto"/>
    /// resolves to Draw on a finger, so the probe result may flip the CURRENT signature step's
    /// presentation — a re-render is queued only when it actually changed.
    /// </summary>
    private async Task ResolvePointerAsync(bool firstRender)
    {
        if (!firstRender || _coarsePointerResolved)
        {
            return;
        }

        _coarsePointerResolved = true;
        var field = CurrentStep?.Field;
        var before = field is null ? default : (TmSignatureCaptureMode?)GetSignatureCaptureMode(field);
        try
        {
            _coarsePointer = await (_runnerInterop ??= new SigningRunnerInterop(JS)).IsCoarsePointerAsync();
        }
        catch (Exception ex) when (ex is JSException
            or JSDisconnectedException
            or ObjectDisposedException
            or TaskCanceledException
            or InvalidOperationException)
        {
            _coarsePointer = false;
        }

        if (_coarsePointer && field is not null && GetSignatureCaptureMode(field) != before)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// The signature step's START mode (Signing redesign phase 5, SI3): the signer's own pick
    /// first, then the template's stored <c>Preferences.Format</c>, then the host's
    /// <see cref="DefaultSignatureMode"/>, and <c>Auto</c> resolves Draw on a coarse pointer —
    /// resolved once the JS runtime answered (<see cref="_coarsePointer"/>), otherwise today's
    /// Typed default.
    /// </summary>
    private TmSignatureCaptureMode GetSignatureCaptureMode(SigningField field)
    {
        if (_signatureCaptureModes.TryGetValue(field.Uuid, out var mode))
        {
            return mode;
        }

        switch (field.Preferences.Format?.Trim().ToLowerInvariant())
        {
            case "draw" or "drawn" or "handwritten":
                return TmSignatureCaptureMode.Draw;
            case "upload":
                return TmSignatureCaptureMode.Upload;
            case "typed" or "text":
                return TmSignatureCaptureMode.Typed;
        }

        return DefaultSignatureMode switch
        {
            TmSigningDefaultSignatureMode.Draw => TmSignatureCaptureMode.Draw,
            TmSigningDefaultSignatureMode.Upload => TmSignatureCaptureMode.Upload,
            TmSigningDefaultSignatureMode.Typed => TmSignatureCaptureMode.Typed,
            _ => _coarsePointer ? TmSignatureCaptureMode.Draw : TmSignatureCaptureMode.Typed
        };
    }

    /// <summary>
    /// Records the intent to centre the ACTIVE field above the sheet (Signing redesign phase 5).
    /// Handlers only set the pending uuid — the actual scroll happens in
    /// <see cref="ApplyPendingScrollAsync"/> once the render has applied the new step and sheet
    /// state, so the JS side measures the settled geometry.
    /// </summary>
    private void RequestScrollToActiveField()
    {
        _pendingScrollFieldUuid = CurrentStep?.Field?.Uuid;
    }

    /// <summary>
    /// Performs the pending auto-scroll (phase 5 review): asks <c>signing-runner.js</c> to centre
    /// the recorded field's document overlay and lift it above the mobile sheet. Best-effort — a
    /// circuit without the module skips silently, so the desktop presentation without JS behaves
    /// exactly as before.
    /// </summary>
    private async Task ApplyPendingScrollAsync()
    {
        if (_pendingScrollFieldUuid is null)
        {
            return;
        }

        var fieldUuid = _pendingScrollFieldUuid;
        _pendingScrollFieldUuid = null;
        await (_runnerInterop ??= new SigningRunnerInterop(JS)).ScrollToFieldAsync(fieldUuid);
    }
}
