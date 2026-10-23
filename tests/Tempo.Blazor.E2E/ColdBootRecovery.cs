using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The ONE controlled recovery from a slow cold boot: when the readiness gate times out, reload the SAME page
/// once (the app and its cached framework assets are up by then; only the first boot was slow) and gate again.
/// A second timeout, or any non-timeout failure, is a real failure and propagates.
/// </summary>
internal static class ColdBootRecovery
{
    /// <summary>Runs <paramref name="waitReady"/>; on a readiness timeout runs <paramref name="reload"/> and waits once more.</summary>
    public static async Task RunAsync(Func<Task> waitReady, Func<Task> reload)
    {
        try
        {
            await waitReady();
        }
        catch (Exception ex) when (IsTimeout(ex))
        {
            await reload();
            await waitReady();
        }
    }

    /// <summary>
    /// Playwright reports a wait timeout as <c>TimeoutException</c> (or, depending on the surface, as a
    /// <see cref="PlaywrightException"/> whose message reads "Timeout …ms exceeded"); both are a cold boot here.
    /// </summary>
    internal static bool IsTimeout(Exception ex) =>
        ex is TimeoutException
        || ex.GetType().Name == "TimeoutException"
        || (ex is PlaywrightException
            && ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("exceeded", StringComparison.OrdinalIgnoreCase));
}
