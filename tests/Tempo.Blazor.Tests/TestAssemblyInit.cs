using System.Globalization;
using System.Runtime.CompilerServices;

namespace Tempo.Blazor.Tests;

/// <summary>
/// Assembly-wide test runtime tuning.
/// </summary>
internal static class TestAssemblyInit
{
    /// <summary>The environment variable overriding <see cref="Bunit.TestContext.DefaultWaitTimeout"/>.</summary>
    internal const string WaitBudgetEnvironmentVariable = "TEMPO_BUNIT_WAIT_SECONDS";

    /// <summary>
    /// Default wait budget in seconds when the variable is not set. Fáze 19 (N91) lowered it
    /// from 10 to 2 after FOUR consecutive full-suite runs at 2 s under parallel
    /// <c>dotnet test</c> load finished with zero WaitFor*/state timeouts — the helpers wait on
    /// state, so a wider budget only hides real stalls; 10 s stays reachable through
    /// <see cref="WaitBudgetEnvironmentVariable"/> for a slower box.
    /// </summary>
    internal const double DefaultWaitSeconds = 2;

    /// <summary>
    /// bUnit's default wait budget is 1 s, which a shared CI box exhausts whenever a render
    /// continuation lands behind parallel collections — every full-suite red of the 2.8.26 gate
    /// was a different WaitFor*/state timeout of that exact shape (WaitForFailedException with
    /// near-zero check counts), never a product assertion. The helpers stay state-based; only
    /// the patience grows, which is the knob bUnit's own failure text recommends for contended
    /// or slower hardware. <see cref="WaitBudgetEnvironmentVariable"/> narrows it for the
    /// load-measurement runs of Fáze 19 (N91) without editing code; a set-but-unreadable value
    /// throws here rather than silently falling back, so a mistyped budget cannot masquerade as
    /// a measurement of the default one.
    /// </summary>
    [ModuleInitializer]
    internal static void RaiseDefaultWaitBudget()
    {
        double seconds = DefaultWaitSeconds;
        if (Environment.GetEnvironmentVariable(WaitBudgetEnvironmentVariable) is { Length: > 0 } raw)
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                || parsed <= 0)
            {
                throw new InvalidOperationException(
                    $"{WaitBudgetEnvironmentVariable} is set to '{raw}', which is not a positive "
                    + "number of seconds — the wait budget is a measurement knob, so a misread of "
                    + "it must not silently run at the default.");
            }

            seconds = parsed;
        }

        Bunit.TestContext.DefaultWaitTimeout = TimeSpan.FromSeconds(seconds);
    }
}
