using System.Globalization;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The wait budgets of <see cref="PlaywrightTestBase.WaitForAppReadyAsync"/>, resolved from one number.
/// <para>
/// A cold boot of a self-hosted WASM demo under full-lane CPU contention can take well over the former fixed
/// 60 s / 15 s / 45 s windows without anything being broken; every wait here is on a STATE (a selector, the
/// <c>data-blazor-ready</c> attribute), so a longer cap only extends the wait of a slow boot, it never sleeps.
/// <c>TM_E2E_READY_TIMEOUT_MS</c> sets the authoritative <c>data-blazor-ready</c> cap; the pre-gates scale with
/// it and stay inside it.
/// </para>
/// </summary>
internal readonly record struct ReadinessTimeouts(int DomContentLoadedMs, int SelectorMs, int FallbackMs, int ReadyMs)
{
    /// <summary>Default <c>data-blazor-ready</c> cap (the former value was 60 s).</summary>
    public const int DefaultReadyMs = 120_000;

    /// <summary>Upper bound of a sane override (30 minutes) — a typo must not hang a shard for hours.</summary>
    public const int MaxReadyMs = 1_800_000;

    /// <summary>The environment variable that overrides the readiness cap (milliseconds).</summary>
    public const string EnvironmentVariable = "TM_E2E_READY_TIMEOUT_MS";

    /// <summary>
    /// Builds the budgets from the raw environment value. A missing, non-numeric, non-positive or absurdly large
    /// value is ignored — an unusable override must never disable or zero the readiness gate.
    /// </summary>
    public static ReadinessTimeouts Resolve(string? raw)
    {
        var readyMs = DefaultReadyMs;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0 && parsed <= MaxReadyMs)
        {
            readyMs = parsed;
        }

        var scale = readyMs / (double)DefaultReadyMs;
        return new ReadinessTimeouts(
            DomContentLoadedMs: readyMs,
            SelectorMs: (int)(30_000 * scale),
            FallbackMs: (int)(90_000 * scale),
            ReadyMs: readyMs);
    }

    /// <summary>The budgets for the current process environment.</summary>
    public static ReadinessTimeouts FromEnvironment() => Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable));
}
