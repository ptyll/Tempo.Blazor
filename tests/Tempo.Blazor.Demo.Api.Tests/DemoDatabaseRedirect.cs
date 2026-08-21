using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Tempo.Blazor.Demo.Api.Data;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// Points this assembly's hosts at a per-process SQLite file so a test run stops writing to the
/// COMMITTED <c>src/Tempo.Blazor.Demo.Api/diagrams.db</c>.
/// <para>
/// THE DEFECT, measured rather than assumed. At a clean HEAD (a51e833d) the file hashed
/// <c>c5221512…</c>; after <c>dotnet test</c> over this project alone — 208 passed, 0 failed — it
/// hashed <c>73967287…</c> and <c>git status --porcelain</c> reported
/// <c>M src/Tempo.Blazor.Demo.Api/diagrams.db</c>. Every green run left the tree dirty, and
/// <c>eng/pack-nuget-packages.sh</c> refuses a dirty tree, so "run the suite, then pack" could not
/// complete without a hand-run <c>git checkout</c>. Committing the churn instead is not a remedy:
/// six different contents were measured out of that one clean base, because SQLite rewrites pages
/// whose bytes carry no information anyone asserts on.
/// </para>
/// <para>
/// WHY A MODULE INITIALIZER AND NOT A FIXTURE. The writer is not one class: eighteen classes take
/// <c>WebApplicationFactory&lt;Program&gt;</c> as an <c>IClassFixture</c> and
/// <c>CapturingSenderFactory</c> derives its own, so a base class or a shared fixture would have to
/// be adopted by each of them and would be one forgotten class away from being false — the same
/// argument <c>Tempo.Blazor.E2E.BaselineOutput</c> makes for centralising its redirect. A
/// module initializer runs once, before any type in this assembly is used, and therefore before any
/// host boots. The env var is the SAME KEY the e2e lane already sets in
/// <c>PlaywrightTestBase.StartDemoHostProcess</c>; this closes the one lane that did not set it.
/// This assembly is the whole population that can boot that host — it is the only test project in
/// the repository with a <c>ProjectReference</c> to <c>Tempo.Blazor.Demo.Api</c>.
/// </para>
/// <para>
/// REDIRECT, NOT SKIP. The tests around the demo database assert behaviour; disabling them to
/// protect a file would trade a working-tree problem for a coverage hole. Only the DESTINATION is
/// dangerous, so only the destination moves.
/// </para>
/// <para>
/// THE SCHEMA IS CREATED HERE, AND THAT IS NOT TIDINESS. <c>Program</c> calls
/// <c>EnsureCreated()</c> on startup, and this lane boots many hosts CONCURRENTLY (xunit runs the
/// collections in parallel). Against the committed file that was invisible, because the schema was
/// already in it and every <c>EnsureCreated</c> was a no-op — the tracked file was MASKING a race.
/// Redirecting to a fresh file unmasked it immediately: measured on the first run after the
/// redirect, 7 of 209 tests failed with
/// <c>SqliteException: SQLite Error 1: 'table "DiagramSnapshots" already exists'</c>, spread across
/// seven unrelated classes. So the redirect seeds the file — a copy of the committed database, so
/// the lane starts from the same bytes it always did — and then creates the schema once,
/// single-threaded, before any host exists. The <c>EnsureCreated</c> below is unconditional on
/// purpose: it makes "the schema is there before the first host boots" one invariant rather than a
/// property of whether the copy succeeded.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DO, so nobody reads more into a clean tree. The race itself is NOT fixed —
/// concurrent <c>EnsureCreated</c> against a schema-less SQLite file is still what it was; this
/// removes the only way this lane can reach that state. It does not stop a host that reads the key
/// from its own configuration files rather than from the environment, and it does not touch the
/// hand-started demo, which deliberately keeps the committed file so its seeded diagrams are there.
/// </para>
/// <para>
/// THE LIMIT, STATED AS WHAT WAS AND WAS NOT MEASURED. For the suite CI actually runs — Release with
/// the publish workflows' filter — the count of tracked files a run leaves modified was measured and
/// is ZERO: <c>git status --porcelain</c> after the full suite AND after a pack is byte-for-byte the
/// status from before them, with no hand-run restore. That measurement does NOT extend to the two
/// lanes that filter excludes, <c>Tempo.Blazor.E2E</c> and <c>Tempo.ReportServer.Api.Tests.MsSql</c>;
/// for those, nobody has counted. Three known cases were never a denominator, and one measured zero
/// over a filtered population is not one either. The guard that turns this file's half from a hope
/// into a measurement is <c>DemoDatabaseRedirectTests</c>, which reads the path back out of a booted
/// host.
/// </para>
/// </summary>
internal static class DemoDatabaseRedirect
{
    /// <summary>
    /// The environment variable the demo host reads, in its double-underscore configuration
    /// spelling. This is the single place this lane spells it. The guard does NOT read this
    /// constant, and that is deliberate rather than an omission: it measures the DESTINATION a
    /// booted host actually opened, so a drift between this spelling and the one the host reads
    /// surfaces as a WRONG PATH rather than as two names agreeing with each other.
    /// </summary>
    internal const string EnvironmentVariable = "Demo__DiagramsDbPath";

    /// <summary>
    /// The per-process directory the redirected database lives in. Keyed on the process id so two
    /// runners on one machine cannot share a file, and left in the temp directory so the operating
    /// system reclaims it — the same shape as the e2e lane's own per-run directory.
    /// </summary>
    internal static string Directory { get; } = Path.Combine(
        Path.GetTempPath(),
        "tempo-blazor-demo-api-tests",
        Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    /// <summary>The redirected database file itself.</summary>
    internal static string DatabasePath { get; } = Path.Combine(Directory, "diagrams.db");

    /// <summary>
    /// The committed database this lane used to write to, and now only reads from. Located from the
    /// repository root rather than from a relative guess, so it stays right when the test binaries
    /// move.
    /// </summary>
    internal static string CommittedDatabasePath { get; } = Path.GetFullPath(Path.Combine(
        FindRepositoryRoot(), "src", "Tempo.Blazor.Demo.Api", "diagrams.db"));

    /// <summary>
    /// Sets the redirect and prepares the file, before anything in this assembly runs. An already-set
    /// value is LEFT ALONE: a caller who exported the key deliberately (a developer pointing a run at
    /// a captured database) outranks this default, and overwriting it would make that impossible.
    /// </summary>
    [ModuleInitializer]
    internal static void Redirect()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable)))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(Directory);
        Environment.SetEnvironmentVariable(EnvironmentVariable, DatabasePath);

        if (File.Exists(CommittedDatabasePath))
        {
            File.Copy(CommittedDatabasePath, DatabasePath, overwrite: true);
        }

        using var context = new DemoDiagramDbContext(
            new DbContextOptionsBuilder<DemoDiagramDbContext>()
                .UseSqlite($"Data Source={DatabasePath}")
                .Options);
        context.Database.EnsureCreated();
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
