using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Text.Json;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Toolbar;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// The responsive layout (FZE-8) of <see cref="TmPdfAnnotator"/>: the resolved-layout state
/// and chrome helpers, the shell bridge (snap points, collapsed panels, right-open), the
/// mobile action bar and the sheet focus helper. OnAfterRenderAsync stays in the main file.
/// Split from TmPdfAnnotator.razor.cs to keep both under the 1200-line source ceiling.
/// </summary>
public partial class TmPdfAnnotator
{
    // ── Responsive layout (FZE-8) ────────────────────────────────────────────

    /// <summary>Peek/half/full snap fractions of the shell stage (the diagram-editor G2 sheet).</summary>
    private static readonly double[] MobileSheetSnapPoints = [0.35, 0.7, 1.0];

    /// <summary>The snap the sheet lands on when it opens from a marker or a draft (phase 8
    /// review 12): the HALF snap (0.7) — the composer's input and its Reply + Resolve row must
    /// sit fully inside the sheet's visible band (the peek's band was too short and the
    /// reply/resolve pair ended below the fold).</summary>
    private const int MobileOpenSnapIndex = MobileHalfSnapIndex;

    /// <summary>The snap for the explicit "Comments" (threads) entry: half — the whole thread
    /// list is readable for browsing.</summary>
    private const int MobileHalfSnapIndex = 1;

    /// <summary>The sheet snap, recorded from the shell (a two-way shadow, never re-raised).</summary>
    private int _mobileSnapIndex = MobileHalfSnapIndex;

    /// <summary>The resolved layout. Under a forced <see cref="LayoutMode"/> the parameter is the
    /// resolution (applied in <see cref="OnParametersSetAsync"/>); under <see cref="TmLayoutMode.Auto"/>
    /// the embedded shell's callback owns it.</summary>
    private TmLayoutMode _resolved = TmLayoutMode.Desktop;

    /// <summary>Armed when the mobile sheet closes: after the render the JS focus helper returns
    /// focus to the element that opened the sheet (marker / Comments tile / draft), falling back
    /// to the first action-bar tile only when that element is gone (phase 8 review 1).</summary>
    private bool _focusOpenerAfter;

    /// <summary>Armed when the mobile sheet opens from a marker, the Comments tile or a draft:
    /// after the render the draft textarea (or the selected thread's reply input) is focused
    /// (phase 8 review 12).</summary>
    private bool _focusComposerAfter;

    /// <summary>Armed when the mobile sheet opens on a selected thread: the selected card is
    /// scrolled into view with <c>scrollIntoView({block:'nearest'})</c> (phase 8 review 11).</summary>
    private bool _scrollSelectedCardAfter;

    /// <summary>Whether the resolved layout is mobile (the G2 presentation).</summary>
    private bool IsMobileLayout => _resolved == TmLayoutMode.Mobile;

    /// <summary>Whether the resolved layout is tablet (labels below the icons, docked side panel).</summary>
    private bool IsTabletLayout => _resolved == TmLayoutMode.Tablet;

    /// <summary>The <c>data-editor-layout</c> mirror of the resolved layout (diagram-editor convention).</summary>
    private string LayoutName => _resolved.ToString().ToLowerInvariant();

    /// <summary>Whether the annotator's groups merge into the viewer toolbar: on a DESKTOP layout
    /// only — a tablet layout renders the full-width labelled tools row above the canvas (phase 8
    /// review 7) and a mobile layout renders the G2 palette and the action bar; neither injects
    /// anything into the viewer's toolbar.</summary>
    private bool InjectToolbarGroups => UseMergedToolbar && !IsMobileLayout && !IsTabletLayout;

    /// <summary>Where the annotator's tool labels sit: under the icon on a tablet layout, inline
    /// everywhere else — the exact rule of the embedded viewer's <c>ResolvedLabelPosition</c>
    /// (phase 8 review 5). <see cref="ToolbarLabels.Icons"/> keeps the buttons icon-only: inside
    /// the merged toolbar the enclosing <c>TmToolbar</c> decides that; the fallback/tablet rows
    /// render outside one, so the same visibility rule is mirrored here.</summary>
    private ToolbarLabelPosition ToolLabelPosition
        => ToolbarLabels == ToolbarLabels.Icons
            ? ToolbarLabelPosition.Hidden
            : IsTabletLayout
                ? ToolbarLabelPosition.Below
                : ToolbarLabelPosition.Inline;

    /// <summary>
    /// The annotator root height. The root OWNS the height contract (the shell's mobile stage
    /// needs a definite height to lay out against) and the embedded viewer flexes inside it —
    /// its own <see cref="Height"/> value is the preferred height it shrinks from on a phone.
    /// </summary>
    private string RootStyle => $"height:{(string.IsNullOrEmpty(Height) ? "600px" : Height)}";

    /// <summary>The viewer height: it fills the annotator root and shrinks on a mobile layout
    /// (the flex chain of the shell's canvas region), so the bound value stays the preferred one.</summary>
    private string ViewerHeight => "100%";

    private IPdfAnnotationProvider EffectiveProvider
        => AnnotationProvider ?? (_fallbackProvider ??= new InMemoryPdfAnnotationProvider());

    private string EffectiveDocumentId
        => !string.IsNullOrEmpty(DocumentId) ? DocumentId : (Url ?? string.Empty);

    /// <summary>
    /// Whether the annotator merges its groups into the viewer's single toolbar. False when
    /// the host disabled the viewer chrome (<see cref="ShowViewerToolbar"/>) or there is no
    /// document to annotate yet — then the compact fallback row renders instead.
    /// </summary>
    private bool UseMergedToolbar => ShowViewerToolbar && !string.IsNullOrEmpty(Url);

    private IReadOnlyList<string> EffectiveStampOptions
        => StampOptions is { Count: > 0 }
            ? StampOptions
            :
            [
                Loc["TmPdfAnnotator_StampApproved"],
                Loc["TmPdfAnnotator_StampRejected"],
                Loc["TmPdfAnnotator_StampDraft"],
                Loc["TmPdfAnnotator_StampConfidential"]
            ];

    private IReadOnlyList<DocumentCommentUser> _knownUsers = [];
    private Func<DocumentCommentThread, string>? _colorResolver;

    private IReadOnlyList<DocumentCommentUser> KnownUsers => _knownUsers;

    private bool SurfaceActive
        => _mode is PdfAnnotatorMode.Comment or PdfAnnotatorMode.Stamp or PdfAnnotatorMode.Draw;

    // ── Responsive layout (FZE-8) ────────────────────────────────────────────

    /// <summary>
    /// Applies a resolved layout change: the G2 rules ride along — the editing tools do
    /// not exist on a phone, so an active edit mode falls back to Browse, and the mobile
    /// sheet STARTS CLOSED on the first resolution to mobile and on every switch from
    /// desktop/tablet to mobile, unless the host explicitly supplied <see cref="ShowPanel"/>
    /// (phase 8 review 3). Under <see cref="TmLayoutMode.Auto"/> the close is DEBOUNCED: the
    /// container measurement can flap while the page settles (a full-page screenshot, a
    /// transient reflow), and hiding a panel the user opened because of an intermediate blip
    /// is worse than a 150 ms late close — a forced mode is the stable resolution and closes
    /// synchronously (phase 12 review).
    /// </summary>
    private async Task ApplyResolvedLayoutAsync(TmLayoutMode mode)
    {
        if (_resolved == mode)
        {
            return;
        }

        var previous = _resolved;
        _resolved = mode;
        ApplyMobileToolFallback();

        if (mode == TmLayoutMode.Mobile
            && previous != TmLayoutMode.Mobile
            && !_showPanelSupplied
            && _panelVisible)
        {
            if (LayoutMode != TmLayoutMode.Auto)
            {
                _panelVisible = false;
                await ShowPanelChanged.InvokeAsync(false);
            }
            else
            {
                ScheduleMobileSheetClose();
            }
        }
        else
        {
            CancelMobileSheetClose();
        }
    }

    /// <summary>Debounce for the mobile sheet auto-close under Auto (~150 ms); cancelled when
    /// the mode resolves away from mobile before it fires.</summary>
    private static readonly TimeSpan MobileSheetCloseDebounce = TimeSpan.FromMilliseconds(150);

    private CancellationTokenSource? _mobileSheetCloseCts;

    /// <summary>Schedules the mobile sheet auto-close after the debounce window; a resolution
    /// away from mobile in between cancels it (see <see cref="ApplyResolvedLayoutAsync"/>).</summary>
    private void ScheduleMobileSheetClose()
    {
        CancelMobileSheetClose();
        var cts = new CancellationTokenSource();
        _mobileSheetCloseCts = cts;
        _ = CloseMobileSheetAfterDebounceAsync(cts.Token);
    }

    private void CancelMobileSheetClose()
    {
        _mobileSheetCloseCts?.Cancel();
        _mobileSheetCloseCts?.Dispose();
        _mobileSheetCloseCts = null;
    }

    private async Task CloseMobileSheetAfterDebounceAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(MobileSheetCloseDebounce, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await InvokeAsync(async () =>
            {
                if (token.IsCancellationRequested
                    || !IsMobileLayout
                    || _showPanelSupplied
                    || !_panelVisible)
                {
                    return;
                }

                _panelVisible = false;
                await ShowPanelChanged.InvokeAsync(false);
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The annotator died while the debounce was pending — nothing to close anymore.
        }
    }

    /// <summary>
    /// G2: highlight, stamp and draw are tablet+ tools. When the resolved layout is mobile and
    /// the active mode is one of them, the internal state falls back to Browse (an in-flight
    /// stroke is dropped). The host's <see cref="Mode"/> parameter keeps its value — the
    /// fallback is a layout adaptation, not a user action, so no callback is raised.
    /// </summary>
    private void ApplyMobileToolFallback()
    {
        if (!IsMobileLayout
            || _mode is not (PdfAnnotatorMode.Highlight or PdfAnnotatorMode.Stamp or PdfAnnotatorMode.Draw))
        {
            return;
        }

        _mode = PdfAnnotatorMode.Browse;
        _activeStroke = null;
        _strokePointerId = null;
        _strokePointerType = null;
    }

    /// <summary>Receives the embedded shell's resolved layout (Auto mode) and re-renders.</summary>
    private async Task OnShellResolvedLayoutChanged(TmLayoutMode mode)
    {
        await ApplyResolvedLayoutAsync(mode);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>The F5 mobile action bar. The annotator's own entries (Export PDF | Export
    /// flattened | Print | the threads entry in the shared "More" menu) are PREPENDED by the
    /// viewer's Pages/Search entries when those features are enabled (phase 8 review 4): the
    /// embedded viewer renders no bar of its own (<c>ShowMobileActionBar=false</c>) and the
    /// entries drive it through its public <c>OpenThumbnailsSheetAsync</c>/<c>OpenSearchAsync</c>
    /// API. The bar's default budget of three tiles pushes the rest into "More".</summary>
    private List<TmActionItem> MobileBarItems
    {
        get
        {
            var items = new List<TmActionItem>(6);
            if (ShowThumbnails)
            {
                items.Add(new TmActionItem
                {
                    Id = "pages",
                    Label = Loc["TmPdfViewer_OpenPages"],
                    Icon = "panel-left",
                    OnClick = EventCallback.Factory.Create(this, () => _viewer?.OpenThumbnailsSheetAsync() ?? Task.CompletedTask),
                });
            }

            if (ShowSearch)
            {
                items.Add(new TmActionItem
                {
                    Id = "search",
                    Label = Loc["TmPdfViewer_Search"],
                    Icon = "search",
                    OnClick = EventCallback.Factory.Create(this, () => _viewer?.OpenSearchAsync() ?? Task.CompletedTask),
                });
            }

            if (AllowExport)
            {
                items.Add(new TmActionItem
                {
                    Id = "export",
                    Label = Loc["TmPdfAnnotator_Export"],
                    Icon = "file-down",
                    OnClick = EventCallback.Factory.Create(this, () => ExportAsync(flatten: false)),
                });
                items.Add(new TmActionItem
                {
                    Id = "export-flat",
                    Label = Loc["TmPdfAnnotator_ExportFlattened"],
                    Icon = "layers",
                    OnClick = EventCallback.Factory.Create(this, () => ExportAsync(flatten: true)),
                });
            }

            items.Add(new TmActionItem
            {
                Id = "print",
                Label = Loc["TmPdfAnnotator_Print"],
                Icon = "printer",
                OnClick = EventCallback.Factory.Create(this, PrintAsync),
            });
            items.Add(new TmActionItem
            {
                Id = "threads",
                Label = Loc["TmPdfAnnotator_OpenThreads"],
                Icon = "message-square",
                OnClick = EventCallback.Factory.Create(this, OpenThreadsSheetAsync),
            });
            return items;
        }
    }

    /// <summary>Opens the threads bottom sheet (from the action bar's More entry): half snap so
    /// the whole thread list is readable for browsing, the opener is remembered for the close
    /// restore, and the two-way <see cref="ShowPanel"/> callback learns the state like every
    /// other open path.</summary>
    private async Task OpenThreadsSheetAsync()
    {
        await RememberOpenerAsync();
        _panelVisible = true;
        _mobileSnapIndex = MobileHalfSnapIndex;
        await ShowPanelChanged.InvokeAsync(true);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Remembers the element that is opening the mobile sheet (a thread marker, the Comments
    /// tile or a palette tile arming a draft): the JS helper captures
    /// <c>document.activeElement</c> inside the annotator root, and
    /// <c>tmPdfAnnotator.focusOpener</c> returns focus there after the sheet closes — falling
    /// back to the first action-bar tile only when the opener is gone (phase 8 review 1).
    /// A no-op where JS is unavailable.
    /// </summary>
    private async Task RememberOpenerAsync()
    {
        if (!IsMobileLayout)
        {
            return;
        }

        try { await JS.InvokeAsync<bool>("tmPdfAnnotator.rememberFocus", _annotatorRef); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }

    /// <summary>The shell's RightOpen two-way bridge: internal state first (an unbound host never
    /// echoes the callback), then the <see cref="ShowPanelChanged"/> callback. A mobile close
    /// arms the opener focus restore (the sheet's close button unmounted with the sheet).</summary>
    private async Task HandleRightOpenChangedAsync(bool open)
    {
        _panelVisible = open;
        if (!open && IsMobileLayout)
        {
            _focusOpenerAfter = true;
        }

        await ShowPanelChanged.InvokeAsync(open);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// The shell's CollapsedPanels two-way bridge (phase 8 review 2): the collapse chevron was
    /// dead because the annotator never echoed the set back. A collapse of the right panel maps
    /// onto <see cref="HandleRightOpenChangedAsync"/> (false) — the panel hides and the shell's
    /// slim reopen toggle (the "rail") brings it back through the RightOpen callback.
    /// </summary>
    private async Task HandleCollapsedPanelsChangedAsync(EditorShellPanel collapsed)
    {
        var rightWasCollapsed = _collapsedPanels.HasFlag(EditorShellPanel.Right);
        _collapsedPanels = collapsed;

        var rightNowCollapsed = collapsed.HasFlag(EditorShellPanel.Right);
        if (!rightWasCollapsed && rightNowCollapsed)
        {
            await HandleRightOpenChangedAsync(false);
        }
        else
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>Records the sheet snap the user dragged to; never re-raised (the shell owns it).</summary>
    private Task SilentSnapChangedAsync(int index)
    {
        _mobileSnapIndex = index;
        return Task.CompletedTask;
    }

    /// <summary>The active-state class of a mobile palette tile.</summary>
    private string PaletteTileClass(PdfAnnotatorMode mode)
        => _mode == mode
            ? "tm-pdf-annotator__tile tm-pdf-annotator__tile--active"
            : "tm-pdf-annotator__tile";

    /// <summary>
    /// Focuses the sheet composer after the mobile sheet renders: the draft textarea when a
    /// draft is pending, otherwise the selected thread's reply input (phase 8 review 12).
    /// Tolerates a missing element and missing JS.
    /// </summary>
    private async Task FocusSheetComposerAsync()
    {
        try
        {
            var selector = _draft is not null
                ? "[data-testid='pdf-annotator-new-input']"
                : "[data-testid='pdf-annotator-reply-input']";
            await JS.InvokeAsync<bool>("tmPdfAnnotator.focusDescendant", _annotatorRef, selector);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
    }
}
