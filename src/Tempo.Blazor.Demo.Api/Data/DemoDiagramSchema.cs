using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Tempo.Blazor.Demo.Api.Data;

/// <summary>
/// Creates the demo diagram schema in a way that survives another host doing the same thing to the same
/// file at the same moment.
/// </summary>
/// <remarks>
/// <para>
/// THE DEFECT THIS EXISTS FOR, stated as the mechanism rather than as a symptom.
/// <c>DatabaseFacade.EnsureCreated()</c> is a CHECK AND AN ACT with nothing between them holding the file:
/// it asks whether the tables are there and, if they are not, runs the CREATE statements. Two hosts that
/// open the same schema-less SQLite database therefore both decide to create it, and the one that gets
/// there second is handed
/// <c>SqliteException: SQLite Error 1: 'table "DiagramSnapshots" already exists'</c>. Nothing about that is
/// specific to a test: the demo host is started concurrently by the test lane (many
/// <c>WebApplicationFactory</c> hosts from parallel collections in ONE process) and by the e2e lane (the
/// demo started as its OWN process against a fresh per-run directory).
/// </para>
/// <para>
/// WHY IT WENT UNSEEN, AND WHY A COUNT OF GREEN RUNS CANNOT CLOSE IT. The committed
/// <c>diagrams.db</c> already contained the schema, so every <c>EnsureCreated</c> was a no-op and the race
/// had nothing to lose; the tracked file was MASKING it. The moment a run started from a database without
/// tables the masking stopped and 7 of 209 tests failed with that message, across seven unrelated classes.
/// The test lane now prepares its per-run database single-threaded before any host exists, which removes
/// the only way THAT lane reaches the dangerous state — so its runs are green by construction and counting
/// them would close the question without anyone looking at the defect. What is measured here instead is the
/// thing itself: hosts released together against a database that genuinely has no schema.
/// </para>
/// <para>
/// WHY A NAMED MUTEX AND NOT A RETRY. A retry answers "the create failed because someone else was
/// creating" with "do it again", which is the same guess as "the create failed" — it cannot tell the race
/// apart from a database that is genuinely broken, and it turns a defect into a delay whose worst case
/// nobody has measured. The lock removes the window instead of surviving it. It is NAMED, therefore shared
/// between processes as well as between threads, and keyed on the FULL PATH of the database, so two hosts
/// opening two different files never wait on each other while two hosts opening one file always do.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DO. It does not make schema creation transactional — a host killed midway through
/// the CREATE statements still leaves a partial database, exactly as before; SQLite's own DDL transaction
/// is what covers that, not this lock. It does not serialise anything after startup: the tables are created
/// once and every later reader and writer runs unsynchronised, as they always did. And it says nothing
/// about a second machine sharing the file over a network mount, where a mutex is not shared
/// at all.
/// </para>
/// </remarks>
public static class DemoDiagramSchema
{
    /// <summary>
    /// How long a host waits for another host to finish creating the schema before going ahead anyway.
    /// Generous by design: schema creation is milliseconds, so reaching this is not congestion but a hung
    /// or killed neighbour, and blocking startup for ever on one of those is worse than the race the wait
    /// was protecting against.
    /// </summary>
    private static readonly TimeSpan WaitForTheOtherHost = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ensures the diagram schema exists in <paramref name="databasePath"/>, letting only one host at a
    /// time do it.
    /// </summary>
    /// <param name="context">The context whose model describes the schema to create.</param>
    /// <param name="databasePath">
    /// The database file the lock is keyed on. It is passed in rather than read back off the connection so
    /// the caller that CHOSE the path is the one that names it — a path resolved twice by two different
    /// routes is two paths as far as a lock is concerned.
    /// </param>
    public static void EnsureCreated(DbContext context, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        using var oneCreatorAtATime = new Mutex(initiallyOwned: false, LockNameFor(databasePath));
        var owned = false;
        try
        {
            try
            {
                owned = oneCreatorAtATime.WaitOne(WaitForTheOtherHost);
            }
            catch (AbandonedMutexException)
            {
                // A host that died holding the lock leaves it abandoned. The wait SUCCEEDED — this process
                // owns it now — and treating that as a failure would let one crashed neighbour keep every
                // later host from starting.
                owned = true;
            }

            context.Database.EnsureCreated();
        }
        finally
        {
            if (owned)
            {
                oneCreatorAtATime.ReleaseMutex();
            }
        }
    }

    /// <summary>
    /// The mutex name for a database file: a hash of its full path, because a mutex name may not contain a
    /// path separator on every platform this runs on, and because the name has to be identical in two
    /// processes that spelled the same file differently.
    /// </summary>
    private static string LockNameFor(string databasePath)
    {
        var canonical = Path.GetFullPath(databasePath);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"tempo-blazor-demo-diagram-schema-{Convert.ToHexString(digest)[..32]}");
    }
}
