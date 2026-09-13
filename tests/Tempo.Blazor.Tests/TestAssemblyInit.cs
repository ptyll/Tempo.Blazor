using System.Runtime.CompilerServices;

namespace Tempo.Blazor.Tests;

/// <summary>
/// Assembly-wide test runtime tuning.
/// </summary>
internal static class TestAssemblyInit
{
    /// <summary>
    /// bUnit's default wait budget is 1 s, which a shared CI box exhausts whenever a render
    /// continuation lands behind parallel collections — every full-suite red of the 2.8.26 gate
    /// was a different WaitFor*/state timeout of that exact shape (WaitForFailedException with
    /// near-zero check counts), never a product assertion. The helpers stay state-based; only
    /// the patience grows, which is the knob bUnit's own failure text recommends for contended
    /// or slower hardware.
    /// </summary>
    [ModuleInitializer]
    internal static void RaiseDefaultWaitBudget()
    {
        Bunit.TestContext.DefaultWaitTimeout = TimeSpan.FromSeconds(10);
    }
}
