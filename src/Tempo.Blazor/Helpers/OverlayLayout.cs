using Microsoft.Extensions.DependencyInjection;
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
    public async Task ReportAsync(TmLayoutMode resolved, Func<TmLayoutMode, Task> changed, ILogger logger, bool hint, IServiceProvider? services = null)
    {
        if (hint) HintOnce(logger, services);
        if (_lastReported == resolved) return;
        var first = _lastReported is null;
        _lastReported = resolved;
        if (!first) await changed(resolved);
    }

    /// <summary>
    /// Logs the missing-viewport hint once per process. Development logs at Information, so a host
    /// that forgot the scope sees it; every other environment stays at Debug.
    /// </summary>
    public static void HintOnce(ILogger logger, IServiceProvider? services = null)
    {
        if (_hinted) return;
        _hinted = true;
        const string message = "An overlay has no viewport scope, so it measured the viewport itself. Add <TmLayoutObserver IsViewportScope=\"true\" IsContainer=\"false\"> to your layout for flash-free mobile presentation.";
        if (IsDevelopment(services)) logger.LogInformation(message);
        else logger.LogDebug(message);
    }

    private static bool IsDevelopment(IServiceProvider? services)
    {
        if (services is null) return false;
        // Resolved by type name. The helper sits in the core package, which references neither
        // Microsoft.Extensions.Hosting nor the WebAssembly host.
        foreach (var service in services.GetServices<object>())
        {
            var type = service.GetType();
            if (type.GetProperty("EnvironmentName") is not { } property) continue;
            if (property.GetValue(service) is string name
                && string.Equals(name, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
