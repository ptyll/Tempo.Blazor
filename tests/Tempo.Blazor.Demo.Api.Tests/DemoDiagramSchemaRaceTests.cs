using System.Globalization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tempo.Blazor.Demo.Api.Data;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// Releases several schema creators at one instant against ONE database that has no tables yet, and
/// requires every one of them to come through.
/// <para>
/// WHAT THIS MEASURES, AND WHY IT IS NOT THE QUESTION <see cref="DemoDatabaseRedirectTests"/> ASKS. The
/// redirect moved this lane's DESTINATION off the committed database. It did not touch what that
/// destination was hiding: <c>Program</c> creates the schema on startup, and a bare
/// <c>EnsureCreated()</c> is a check and an act with nothing between them holding the file, so two hosts
/// opening the same schema-less database both decide to create it and the second is handed
/// <c>SqliteException: SQLite Error 1: 'table "DiagramSnapshots" already exists'</c>. The demo host really
/// is started that way: many <c>WebApplicationFactory</c> hosts from parallel collections inside one test
/// process, and the demo as its own process from the e2e lane. Measured in this repository the first time a
/// run started from a database WITHOUT tables: 7 of 209 failed with that message, across seven unrelated
/// classes.
/// </para>
/// <para>
/// WHY A COUNT OF GREEN SUITE RUNS COULD NOT HAVE CLOSED THIS, and why the tooth is shaped like this
/// instead. <see cref="DemoDatabaseRedirect"/> prepares the per-run database single-threaded before any
/// host exists, which removes the only way THIS lane can reach the dangerous state. Every suite run is
/// therefore green by construction, and ten of them would have shut the question without anybody looking at
/// the defect. So this test refuses the prepared file, builds an empty one, and puts the creators on a
/// barrier — the state the e2e lane genuinely reaches, since its host launcher points at a fresh per-run
/// directory and copies nothing into it.
/// </para>
/// <para>
/// THE POSITIVE CONTROL IS INSIDE THE TEST, not a remark beside it. Handed a database that already has the
/// table, every creator would be a no-op and this would pass while measuring nothing — which is exactly how
/// the committed file hid the defect. So the table's ABSENCE is asserted before the creators start and its
/// PRESENCE after: the needle is shown to point at a database that really did need creating, and at one
/// that really did get created.
/// </para>
/// <para>
/// WHAT A GREEN HERE DOES NOT PROVE. Not that six creators are enough to lose an unheld race — a race not
/// lost in one run was never shown to be impossible, which is why the fix is a lock rather than a retry
/// count, and why the arm that gives this tooth its meaning is the MUTATION: with
/// <see cref="DemoDiagramSchema"/> reduced to a bare <c>EnsureCreated()</c> this test fails, naming that
/// SQLite message. It also does not prove that <c>Program</c> routes through the seam — that is a property
/// of a call site, and the assertion that would "check" it could only read source text, which proves a line
/// exists and never that it runs.
/// </para>
/// <para>
/// AND IT DOES NOT PROVE THE HALF THE SEAM HAD TO LIVE IN THE HOST FOR. The creators below are released
/// inside ONE PROCESS, and over that population an UNNAMED mutex would pass exactly the same, so what a
/// green here measures is the within-process half. THE CROSS-PROCESS HALF IS HELD BY CONSTRUCTION, NOT BY
/// MEASUREMENT — and nothing in this repository contends it today. The only lane that starts the demo as
/// its OWN process starts its hosts sequentially behind a lock, one demo API among them, into a directory
/// keyed on the process id; it therefore ENGAGES that half and never contends it. Contending it would take
/// two hosts over one file, which nothing arranges. The single place it can be observed at all is the
/// watch whose cheap check reads an e2e run's <c>.trx</c> for that same SQLite message — an observation
/// point, not a proof, and named here by what it does rather than by an id that would rot.
/// </para>
/// </summary>
public sealed class DemoDiagramSchemaRaceTests
{
    private const int ConcurrentCreators = 6;
    private const string DiagramSnapshotsTable = "DiagramSnapshots";

    [Fact]
    public void ConcurrentCreators_OverADatabaseWithNoSchema_AllSucceed()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "tempo-blazor-demo-api-schema-race",
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "diagrams.db");

        try
        {
            TableExists(databasePath, DiagramSnapshotsTable).Should().BeFalse(
                "every creator below is meant to find a database that still needs its schema; if the file "
                + "arrived with the table already in it, each of them would be a no-op and this test would "
                + "pass without one of them ever racing another — which is precisely how the committed "
                + "database hid this defect for as long as it did. Looked in: " + databasePath);

            var failures = CreateConcurrently(databasePath);

            failures.Should().BeEmpty(
                "the demo host creates this schema on startup and is started concurrently against one "
                + "database, both inside a process and across processes. A start that throws because a "
                + "NEIGHBOUR created the tables first is the defect this holds; it was measured as 7 of "
                + "209 the first time a run began from a database without tables");

            TableExists(databasePath, DiagramSnapshotsTable).Should().BeTrue(
                "the creators are required to have CREATED the schema, not merely to have survived. "
                + "Without this half a seam that quietly skipped creation altogether would satisfy the "
                + "assertion above and move the failure to every later reader of the database");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static IReadOnlyList<Exception> CreateConcurrently(string databasePath)
    {
        // A barrier, not a plain parallel loop: without one the first creator is normally far enough ahead
        // to have finished before the last one opens the file, and the window this test exists for is never
        // entered. Releasing them together is what makes an empty failure list mean something.
        using var atTheGate = new Barrier(ConcurrentCreators);
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        var creators = Enumerable.Range(0, ConcurrentCreators)
            .Select(_ => new Thread(() =>
            {
                try
                {
                    using var context = new DemoDiagramDbContext(
                        new DbContextOptionsBuilder<DemoDiagramDbContext>()
                            .UseSqlite($"Data Source={databasePath}")
                            .Options);

                    atTheGate.SignalAndWait(TimeSpan.FromSeconds(30));
                    DemoDiagramSchema.EnsureCreated(context, databasePath);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    // A creator that threw never reaches the barrier again; releasing its slot keeps the
                    // others from waiting out the timeout and reporting the wrong failure.
                    atTheGate.RemoveParticipant();
                }
            }))
            .ToList();

        // Real threads rather than Task.Run: a barrier of six blocking waits can outlast the thread pool's
        // willingness to inject threads, and a test that deadlocks on its own harness measures the harness.
        foreach (var creator in creators)
        {
            creator.IsBackground = true;
            creator.Start();
        }

        foreach (var creator in creators)
        {
            creator.Join(TimeSpan.FromSeconds(60)).Should().BeTrue(
                "a creator that never finished is not a pass; the lock is supposed to make them wait for "
                + "each other for milliseconds, so one still running after a minute means the wait itself "
                + "is the defect");
        }

        return failures.ToList();
    }

    private static bool TableExists(string databasePath, string table)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is noise, not a failure; the operating system reclaims it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
