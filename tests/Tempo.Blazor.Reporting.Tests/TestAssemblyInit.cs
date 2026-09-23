using System.Globalization;
using System.Runtime.CompilerServices;

namespace Tempo.Blazor.Reporting.Tests;

/// <summary>
/// Duplication of <c>tests/Tempo.Blazor.Tests/TestAssemblyInit.cs</c> — <c>internal</c> does not
/// cross the assembly boundary and this suite is small enough that a shared package would cost
/// more than ~50 lines of duplication (N183). Keep the logic in sync, including the
/// <c>double.IsFinite</c> NaN/Infinity rejection (N174).
/// </summary>
internal static class TestAssemblyInit
{
    /// <summary>The environment variable overriding <see cref="Bunit.TestContext.DefaultWaitTimeout"/>.</summary>
    internal const string WaitBudgetEnvironmentVariable = "TEMPO_BUNIT_WAIT_SECONDS";

    /// <summary>
    /// Default wait budget in seconds when the variable is not set — same 2 s the
    /// <c>Tempo.Blazor.Tests</c> suite standardized on (N91): state-based waits only need enough
    /// patience for a contended CI box, and 10 s stays reachable through
    /// <see cref="WaitBudgetEnvironmentVariable"/>.
    /// </summary>
    internal const double DefaultWaitSeconds = 2;

    /// <summary>
    /// bUnit's default wait budget is 1 s — see the sibling file in <c>Tempo.Blazor.Tests</c> for
    /// the measured rationale. A set-but-unreadable value throws here rather than silently
    /// falling back, so a mistyped budget cannot masquerade as a measurement of the default one.
    /// </summary>
    [ModuleInitializer]
    internal static void RaiseDefaultWaitBudget()
    {
        double seconds = DefaultWaitSeconds;
        if (Environment.GetEnvironmentVariable(WaitBudgetEnvironmentVariable) is { Length: > 0 } raw)
        {
            // !double.IsFinite rejects NaN/±Infinity ("NaN" and "1e999" both parse successfully —
            // every comparison with NaN is false, so `parsed <= 0` alone never catches them and
            // they would sail through to TimeSpan.FromSeconds as an undocumented ArgumentException).
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                || parsed <= 0
                || !double.IsFinite(parsed))
            {
                throw new InvalidOperationException(
                    $"{WaitBudgetEnvironmentVariable} is set to '{raw}', which is not a positive "
                    + "finite number of seconds — the wait budget is a measurement knob, so a misread of "
                    + "it must not silently run at the default.");
            }

            seconds = parsed;
        }

        Bunit.TestContext.DefaultWaitTimeout = TimeSpan.FromSeconds(seconds);
    }
}
