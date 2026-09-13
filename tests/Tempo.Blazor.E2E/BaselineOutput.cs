using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Decides WHERE a captured screenshot lands. Without an explicit opt-in it lands in the run's
/// TestResults directory; only with <see cref="BaselineGeneratorTestBase.WritesAllowed"/> does it
/// land under the baseline root at <c>artifacts/baseline/</c>.
/// <para>
/// This is the same shape as <c>Demo__DiagramsDbPath</c>: REDIRECT the target, do not skip the
/// test. The Notion captures are not generators — the tests around them assert behaviour, and
/// skipping them to protect the PNGs would trade a working-tree problem for a coverage hole. What
/// makes them dangerous is only the destination.
/// </para>
/// <para>
/// The root is <c>artifacts/baseline/</c> — gitignored, outside the index — since 2.8.26: baseline
/// PNGs are run artefacts, not source. <c>BaselineWriteSweep</c> holds the line the other way: any
/// PNG staged under the old <c>tests/Tempo.Blazor.E2E/__baseline__/</c> path fails the run closed.
/// </para>
/// <para>
/// Five classes carried a byte-identical private copy of this path calculation
/// (<see cref="NotionE2ETestBase"/> plus four Notion test classes), and 276 call sites reach them,
/// so the opt-in has to live HERE rather than on any of the classes: a per-class guard is one
/// forgotten copy away from being false. That is also why <see cref="BaselineWriteSweep"/> measures
/// the write TARGET instead of trusting a naming convention — a guard keyed on the class name could
/// not see any of these five, none of which is named <c>*BaselineScreenshots</c>.
/// </para>
/// </summary>
internal static class BaselineOutput
{
    /// <summary>
    /// The baseline write root, <c>artifacts/baseline/</c> at the repository root. Named
    /// <c>Root</c>, not "committed root": nothing under it is tracked any more.
    /// </summary>
    public static string Root { get; } = Path.Combine(FindRepositoryRoot(), "artifacts", "baseline");

    /// <summary>
    /// The directory a capture should be written to, created if missing.
    /// <paramref name="segments"/> are appended below the baseline root (e.g. <c>notion</c>,
    /// <c>comments</c>).
    /// </summary>
    public static string DirectoryFor(TestContext? context, params string[] segments)
    {
        var root = BaselineGeneratorTestBase.WritesAllowed
            ? Root
            : Path.Combine(
                context?.TestResultsDirectory ?? Path.GetTempPath(),
                "baseline-sandbox");

        var directory = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segments).ToArray()));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
