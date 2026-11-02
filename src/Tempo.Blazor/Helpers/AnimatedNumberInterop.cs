using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.DataDisplay;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Per-instance wrapper around the <c>tm-animated-number</c> ES module (requestAnimationFrame
/// ticker on one element plus the reduced-motion probe). Lazily imports the module on first use,
/// so nothing touches the JavaScript runtime before the first animation — the component is safe to
/// construct during prerender. The import/guard boilerplate is delegated to
/// <see cref="LazyJsModule"/>.
/// </summary>
/// <remarks>
/// Every interop is guarded: where JavaScript is unavailable (prerender, a torn-down circuit, a
/// host that never loads the module) the component keeps rendering its final value and nothing
/// throws into the render loop. The animation is a visual effect only.
/// </remarks>
internal sealed class AnimatedNumberInterop : IAsyncDisposable
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    private readonly LazyJsModule _lazy;

    /// <summary>Creates the wrapper. No interop happens here — safe during prerender.</summary>
    /// <param name="js">The runtime the module is imported from.</param>
    public AnimatedNumberInterop(IJSRuntime js)
    {
        _lazy = new LazyJsModule(js, ModulePath);
    }

    /// <summary>
    /// Whether the machine asked for reduced motion. False when the module cannot answer — the
    /// conservative answer is a still number, never a ticker.
    /// </summary>
    public async Task<bool> PrefersReducedMotionAsync()
    {
        var (ok, value) = await _lazy.TryInvokeAsync<bool>("prefersReducedMotion");
        return ok && value;
    }

    /// <summary>
    /// Starts (or retargets) the ticker on <paramref name="element"/>. A null
    /// <paramref name="from"/> tells the module to continue from the number it is currently
    /// painting, which is what makes a mid-animation value change jump straight onto the new
    /// target instead of replaying from the start.
    /// </summary>
    /// <param name="element">The value span the component rendered (aria-hidden only when it announced).</param>
    /// <param name="from">First-frame value, or null to continue from the painted number.</param>
    /// <param name="to">The target value.</param>
    /// <param name="duration">Animation length in milliseconds.</param>
    /// <param name="format">The <c>Intl.NumberFormat</c> shape of the intermediate frames.</param>
    /// <param name="prefix">Optional text before the number (e.g. a currency symbol).</param>
    /// <param name="suffix">Optional text after the number (e.g. " %").</param>
    public async ValueTask AnimateAsync(
        ElementReference element,
        double? from,
        double to,
        int duration,
        AnimatedNumberFormat format,
        string? prefix,
        string? suffix)
        => await _lazy.TryInvokeVoidAsync(
            "animate",
            element,
            from,
            to,
            duration,
            new
            {
                locale = format.Locale,
                style = format.Style,
                minimumFractionDigits = format.MinimumFractionDigits,
                maximumFractionDigits = format.MaximumFractionDigits,
                useGrouping = true,
                prefix,
                suffix
            });

    /// <summary>
    /// Stops the ticker on <paramref name="element"/> — the cleanup a dispose owes the browser, so
    /// a removed component never leaves a requestAnimationFrame loop running. A module that was
    /// never imported is not imported for the sake of cancelling.
    /// </summary>
    /// <param name="element">The element the ticker was attached to.</param>
    public async ValueTask CancelAsync(ElementReference element)
    {
        if (!_lazy.IsImported)
        {
            return;
        }

        await _lazy.TryInvokeVoidAsync("cancel", element);
    }

    /// <summary>Releases the imported module. Idempotent.</summary>
    public ValueTask DisposeAsync() => _lazy.DisposeAsync();
}
