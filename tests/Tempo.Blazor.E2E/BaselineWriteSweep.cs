using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fails the run if a baseline PNG exists under <c>tests/Tempo.Blazor.E2E/__baseline__/</c> —
/// tracked, staged, or merely written.
/// <para>
/// THE INVERSION, 2.8.26: baseline PNGs are run artefacts and live under
/// <c>artifacts/baseline/</c> (<see cref="BaselineOutput.Root"/>), which is gitignored. The
/// repository's <c>__baseline__</c> population is ZERO, so the sweep no longer hashes 795 files to
/// detect an overwrite — it asserts the directory is empty, in the index AND on disk. Any PNG under
/// that path is a regression of the redirect: staged means somebody is about to commit a baseline
/// again, on disk means a write target still points at the old root.
/// </para>
/// <para>
/// Its predicate is the WRITE TARGET, not the name of a class. That distinction is the whole point:
/// the previous guard swept for classes called <c>*BaselineScreenshots</c> and was therefore
/// constitutionally blind to <see cref="NotionE2ETestBase"/>, which wrote 765 committed PNGs — 66%
/// of every tracked PNG in the repository — from 276 call sites, under a name matching no
/// convention and with no category. A single ordinary Notion test rewrote 12 of them. Any guard
/// keyed on how a class is spelled can be defeated by spelling it differently; a guard keyed on
/// what the run actually TOUCHED cannot.
/// </para>
/// <para>
/// REACHABILITY, so the next reader knows what this can and cannot catch: CI never runs it. Both
/// publish workflows test with <c>--filter "FullyQualifiedName!~Tempo.Blazor.E2E&amp;…"</c>
/// (<c>publish-nuget.yml</c>, <c>publish-nuget-org.yml</c>), which excludes this whole assembly.
/// This guard fires on a developer's machine or not at all.
/// </para>
/// <para>
/// It is not a test method, because it has to observe the whole run rather than one test. The
/// population is read in <c>[AssemblyInitialize]</c> and re-read from
/// <see cref="PlaywrightTestBase.AssemblyCleanup"/>, which calls
/// <see cref="AssertNoBaselinePngsExist"/> OUTSIDE its best-effort teardown loop — that loop
/// swallows exceptions, so a failure raised inside it would be silent.
/// </para>
/// </summary>
[TestClass]
public static class BaselineWriteSweep
{
    /// <summary>
    /// The git pathspec that DEFINES the measured population. It is a named constant because the
    /// run's diagnostic quotes it: a scope reported in prose next to a scope applied in code is two
    /// statements that can drift, and the drift is invisible.
    /// </summary>
    private const string BaselineRootPathspec = "tests/Tempo.Blazor.E2E/__baseline__/*.png";

    /// <summary>Repository-relative directory the sweep checks on disk.</summary>
    private const string BaselineDirectoryRelative = "tests/Tempo.Blazor.E2E/__baseline__";

    private static string? _repositoryRoot;

    /// <summary>False while the sweep could not establish where the repository is.</summary>
    private static bool _enabled;

    [AssemblyInitialize]
    public static void CaptureSnapshot(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            _repositoryRoot = FindRepositoryRoot();
            _enabled = true;

            var staged = IndexedBaselinePaths(_repositoryRoot);
            var onDisk = OnDiskBaselinePaths(_repositoryRoot);

            context.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[baseline-sweep] {BaselineRootPathspec}: indexed={staged.Count} on-disk={onDisk.Count} "
                + $"(expected 0/0); writes allowed = {BaselineGeneratorTestBase.WritesAllowed}"));

            // The starting state must already be clean — a staged or leftover PNG predates the run
            // and failing at cleanup would blame the run for what it found.
            AssertNoBaselinePngsExist();
        }
        catch (Exception ex)
        {
            // A missing git or a tarball checkout must not take the whole suite down; the sweep
            // reports it could not measure instead of pretending it measured nothing.
            _enabled = false;
            context.WriteLine($"[baseline-sweep] DISABLED: {ex.Message}");
        }
    }

    /// <summary>
    /// Throws if any PNG exists under the old baseline root — in the index or on disk. Called from
    /// the single <c>[AssemblyCleanup]</c> the assembly is allowed to have, and once at
    /// initialization so a pre-existing violation is attributed to the tree, not the run.
    /// </summary>
    public static void AssertNoBaselinePngsExist()
    {
        if (!_enabled || _repositoryRoot is null)
        {
            return;
        }

        var staged = IndexedBaselinePaths(_repositoryRoot);
        var onDisk = OnDiskBaselinePaths(_repositoryRoot);

        if (staged.Count == 0 && onDisk.Count == 0)
        {
            return;
        }

        var report = new StringBuilder()
            .Append("baseline PNGs exist under tests/Tempo.Blazor.E2E/__baseline__/ — that directory "
                + "must stay empty. Baselines are run artefacts under artifacts/baseline/ ")
            .Append("(see BaselineOutput); a staged one re-commits the index, an on-disk one means a ")
            .Append("write target still points at the old root:");

        foreach (var path in staged.Take(15))
        {
            report.Append("\n  indexed: ").Append(path);
        }

        foreach (var path in onDisk.Take(15))
        {
            report.Append("\n  on-disk: ").Append(path);
        }

        var overflow = staged.Count + onDisk.Count
            - Math.Min(staged.Count, 15) - Math.Min(onDisk.Count, 15);
        if (overflow > 0)
        {
            report.Append(string.Create(CultureInfo.InvariantCulture, $"\n  … and {overflow} more"));
        }

        throw new InvalidOperationException(report.ToString());
    }

    /// <summary>
    /// PNGs the git index knows under the baseline root — tracked or staged. <c>git ls-files</c> is
    /// what makes "in the index" mean the same thing here as it does in the acceptance criterion.
    /// </summary>
    private static List<string> IndexedBaselinePaths(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(BaselineRootPathspec);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("could not start git");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(30000);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git ls-files exited {process.ExitCode}");
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    /// <summary>A PNG on disk under the old root is a write that went to the wrong place.</summary>
    private static List<string> OnDiskBaselinePaths(string repositoryRoot)
    {
        var directory = Path.Combine(
            repositoryRoot, BaselineDirectoryRelative.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.png", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
