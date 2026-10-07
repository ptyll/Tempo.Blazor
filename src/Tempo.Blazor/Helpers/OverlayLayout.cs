using Microsoft.Extensions.Logging;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// The layout an overlay renders. A modal, a dialog and a drawer all resolve the same way, so the
/// decision lives here instead of in three copies.
/// </summary>
public sealed class OverlayLayout
{
    private static bool _hinted;
    private TmLayoutMode? _lastReported;
    private TmLayoutMode? _probed;

    /// <summary>The mode to render, and the context the overlay's own markup reads.</summary>
    public TmLayoutContext Resolve(TmLayoutMode layoutMode, TmLayoutContext? viewport, TmLayoutMode initial)
    {
        // The probe stands in for a missing viewport. A cascaded viewport still wins, so a forced
        // desktop container cannot hide a phone-width overlay that measured the viewport itself.
        var resolved = TmLayout.Resolve(layoutMode, viewport, viewport is null ? _probed : null, initial);
        return new TmLayoutContext(layoutMode, resolved);
    }

    /// <summary>Records a probe measurement. The first resolution is not a change.</summary>
    public void Measured(TmLayoutMode mode) => _probed = mode;

    /// <summary>
    /// Reports a change of the resolved mode. The first resolution is not reported: a host that
    /// renders before any measurement must not see a spurious change.
    /// </summary>
    public async Task ReportAsync(TmLayoutMode resolved, Func<TmLayoutMode, Task> changed, ILogger logger, bool hint)
    {
        if (hint) HintOnce(logger);
        if (_lastReported == resolved) return;
        var first = _lastReported is null;
        _lastReported = resolved;
        if (!first) await changed(resolved);
    }

    /// <summary>Logs the missing-viewport hint once per process, at Debug.</summary>
    public static void HintOnce(ILogger logger)
    {
        if (_hinted) return;
        _hinted = true;
        logger.LogDebug("An overlay has no viewport scope, so it measured the viewport itself. Add <TmLayoutObserver IsViewportScope=\"true\" IsContainer=\"false\"> to your layout for flash-free mobile presentation.");
    }
}
