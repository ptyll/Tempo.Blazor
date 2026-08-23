using System.Data;
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
/// demo started as its OWN process against a fresh per-run directory). The within-process half is
/// measured by the six-creator tooth in the Demo.Api test project; the cross-process half is two Demo.Api
/// host processes over one schema-less file, on a barrier, with and without this lock.
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
/// <para>
/// THE TEST SEAMS BELOW ARE READ ONLY IN DEVELOPMENT, AND THAT IS ENFORCED HERE RATHER THAN ASKED FOR IN
/// A DOC COMMENT. Three environment variables can park this host on a barrier or walk it straight past the
/// lock; the sentence that used to guard them said they "must stay unset", which is a description of an
/// operator's habit and is enforced by nothing — and this file ships inside a host anyone can start.
/// <c>TestSeamsAreOpen</c> makes every read of those three names conditional on the host running as
/// <c>Development</c>, so a demo started any other way cannot be talked into the very race the lock exists
/// to close, and cannot be made to throw <see cref="TimeoutException"/> out of its own startup. The
/// cross-process tooth starts its hosts with <c>ASPNETCORE_ENVIRONMENT=Development</c> and is unaffected;
/// what the gate is worth is measured by an arm that changes only that one variable. What it is worth is
/// NOT a security boundary and NOT closed for a hand-started demo — <c>TestSeamsAreOpen</c> states both,
/// and neither can be read off this paragraph.
/// </para>
/// </remarks>
public static class DemoDiagramSchema
{
    /// <summary>
    /// How long a host waits for another host to finish creating the schema before going ahead anyway.
    /// Generous by design: schema creation is milliseconds, so reaching this is not congestion but a hung
    /// or killed neighbour, and blocking startup for ever on one of those is worse than the race the wait
    /// was protecting against.
    /// <para>
    /// THIS IS THE MUTEX WAIT AND NOTHING ELSE. It used to be the test barrier's deadline as well; see
    /// <see cref="TestBarrierTimeout"/> for why one constant could not honestly be both.
    /// </para>
    /// </summary>
    private static readonly TimeSpan WaitForTheOtherHost = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Test-only: how long a host armed with the barrier variables waits to be released before giving up.
    /// <para>
    /// NON-DETERMINISM SOURCE (FIXED): ONE CONSTANT SERVING AS BOTH THE MUTEX WAIT AND THIS DEADLINE.
    /// The two are different QUANTITIES that happened to share a number. The mutex wait answers "how long do I tolerate a neighbour that is
    /// already creating the schema" — milliseconds of real work, so 30 s is enormous. The barrier deadline
    /// answers "how long do I tolerate a neighbour that has not BOOTED yet", and booting a Demo.Api host on
    /// a cold or loaded runner is seconds to minutes. Sharing one constant meant the first host to arrive
    /// gave up on the second after 30 s while its own harness was still willing to wait 120 s for that host
    /// to appear — and the resulting <see cref="TimeoutException"/> reached the harness as the same
    /// unattributable red as a genuine schema failure. It also meant that TUNING THE PRODUCTION MUTEX WAIT
    /// silently retuned a test deadline, in a file where nothing said so.
    /// </para>
    /// <para>
    /// DERIVED FROM THE HARNESS'S HOST-STARTUP TIMEOUT, not from the mutex wait: the barrier may only give
    /// up AFTER the harness has already given up waiting for hosts to start, otherwise the host kills itself
    /// while the run that is watching it still considers it healthy. That harness timeout is 120 s
    /// (<c>DemoDiagramSchemaCrossProcessRaceTests.HostStartupTimeout</c>).
    /// </para>
    /// <para>
    /// WHAT IS DERIVED IS THE INEQUALITY. THE FACTOR IS A CHOICE, AND CALLING IT "1.5x" DRESSED A PICK AS A
    /// MEASUREMENT. Nothing measured 180 s and nothing can: the harness kills the run at 120 s, so 121 s,
    /// 180 s and 600 s are indistinguishable in behaviour — each of them expires only after the run that
    /// was watching has already failed, and no observer downstream can tell which was configured. The
    /// plateau is (120 s, ∞) and it has NO UPPER EDGE to find, so any experiment that claims to have found
    /// one measured something else. What is reasoned, and what is toothed, is
    /// <c>TestBarrierTimeout &gt; HostStartupTimeout</c>:
    /// <c>TheTestBarrierDeadline_OutlivesTheHarnessWaitForAHostToStart</c> asserts exactly that over the
    /// two constants, so moving either one the wrong way is red, while moving 180 to any other value above
    /// 120 is not a change in behaviour at all.
    /// </para>
    /// </summary>
    public static readonly TimeSpan TestBarrierTimeout = TimeSpan.FromSeconds(180);

    /// <summary>
    /// Test-only: directory in which a host writes a pid file when it has reached schema creation and is
    /// waiting to be released. Read only when this host runs as <c>Development</c> (see
    /// <c>TestSeamsAreOpen</c>); the cross-process race tooth is the only writer.
    /// </summary>
    public const string TestReadyDirEnvironmentVariable = "TEMPO_TEST_DIAGRAM_SCHEMA_READY_DIR";

    /// <summary>
    /// Test-only: path of the file a host polls for after writing its ready file. A named
    /// <c>EventWaitHandle</c> is not supported on this host's OS (Linux throws
    /// <c>PlatformNotSupportedException</c>); a go-file is. Read only when this host runs as
    /// <c>Development</c> (see <c>TestSeamsAreOpen</c>).
    /// </summary>
    public const string TestGoFileEnvironmentVariable = "TEMPO_TEST_DIAGRAM_SCHEMA_GO_FILE";

    /// <summary>
    /// Test-only: when set to <c>1</c>, schema creation skips the named mutex and calls
    /// <c>EnsureCreated()</c> bare. That is the MUTATION the cross-process tooth uses as its positive
    /// control — without it a green "both hosts started" is indistinguishable from "they never met".
    /// <para>
    /// IT IS READ ONLY WHEN THIS HOST RUNS AS <c>Development</c> (see <c>TestSeamsAreOpen</c>). This used
    /// to read "unset in production, and must stay unset", which asked an operator for a promise instead
    /// of denying the capability. A demo started any other way may export the name to no effect, because
    /// nothing outside Development looks at it.
    /// </para>
    /// </summary>
    public const string TestSkipLockEnvironmentVariable = "TEMPO_TEST_DIAGRAM_SCHEMA_SKIP_LOCK";

    /// <summary>
    /// The one environment in which the three variables above are read at all.
    /// </summary>
    private const string TheEnvironmentTheTestSeamsBelongTo = "Development";

    /// <summary>
    /// Whether this host may act on the three test variables above.
    /// <para>
    /// WHY A GATE AND NOT A DOC COMMENT. Each of the three names is a way of making a running host behave
    /// worse: two of them arm a barrier whose failure mode is a <see cref="TimeoutException"/> thrown out
    /// of startup, and the third replaces the named lock with a bare <c>EnsureCreated()</c> — the exact
    /// check-then-act this type exists to remove. Their doc comments asked for them to stay unset, and an
    /// asked-for property is not a property: nothing went red when the habit broke, and nothing could,
    /// because the reads were unconditional.
    /// </para>
    /// <para>
    /// IT READS THE TWO VARIABLES THE HOST ITSELF READS, NOT A RESOLVED <c>IHostEnvironment</c>, because
    /// this is a static seam with no host services in hand and giving it some would change the signature
    /// of <see cref="EnsureCreated"/> and therefore its call site. The consequence is worth naming rather
    /// than glossing: a host that selects Development some OTHER way — <c>--environment Development</c> on
    /// the command line, <c>appsettings</c> — finds the seams CLOSED, not open, because neither of those
    /// routes writes an environment variable for this method to read. THE LAUNCH PROFILE IS NOT ONE OF
    /// THOSE ROUTES, and an earlier version of this paragraph listed it as if it were.
    /// <c>Properties/launchSettings.json</c> sets <c>ASPNETCORE_ENVIRONMENT=Development</c> as a PROCESS
    /// environment variable, so <c>dotnet run --project src/Tempo.Blazor.Demo.Api</c> — the documented way
    /// of hand-starting this host — reaches this method with the seams OPEN.
    /// </para>
    /// <para>
    /// THE DIVERGENCE FROM <c>IWebHostEnvironment.IsDevelopment()</c> RUNS BOTH WAYS, which is what
    /// "it fails shut" hid by naming only one of them. FAIL-SHUT: a tooth that armed its hosts by command
    /// line or <c>appsettings</c> goes green without ever opening the seam it meant to exercise — that
    /// costs a test its arrangement. FAIL-OPEN, and this one costs a running demo its lock:
    /// <c>--environment Production</c> reaches the host through <c>WebApplication.CreateBuilder(args)</c>
    /// and through NO environment variable, so a host started from the launch profile with that switch is
    /// Production to itself and Development to this method — HOST PRODUCTION, SEAMS OPEN. Measured
    /// 2026-08-23 over 319e3c9e, <c>-c Release</c>, by reading the host's own "Hosting environment:" line:
    /// <c>ASPNETCORE_ENVIRONMENT=Development &lt;exe&gt; --environment Production</c> prints
    /// <c>Production</c> while <c>ASPNETCORE_ENVIRONMENT</c> is still <c>Development</c> in that process.
    /// THIS IS A LIMIT, NOT A FIX: the exact predicate needs a resolved <c>IHostEnvironment</c>, which
    /// changes the signature of <see cref="EnsureCreated"/> and therefore <c>Program.cs</c> — out of scope
    /// for the phase that wrote this. It is carried as a queue row.
    /// </para>
    /// <para>
    /// ONE SHAPE THAT WAS SUSPECTED AND MEASURED NOT TO DIVERGE, so the next reader does not re-raise it:
    /// an ASPNETCORE_ENVIRONMENT that is unset, empty or whitespace together with
    /// <c>DOTNET_ENVIRONMENT=Development</c>. This method falls back to the second name; so does the host,
    /// which reported <c>Hosting environment: Development</c> in all three shapes (same date, same
    /// commit, same reading). Gate and host agree there.
    /// </para>
    /// <para>
    /// WHAT THIS GATE IS WORTH, SAID SO NOBODY READS MORE INTO IT. <c>Development</c> IS NOT A SECURITY
    /// BOUNDARY HERE; IT IS A BOUNDARY AGAINST MISTAKE. What it really buys is that an ambient or
    /// inherited variable does not open the seam on its own — a host outside Development never reads the
    /// three names, so a stray <c>TEMPO_TEST_DIAGRAM_SCHEMA_*</c> in someone's shell profile or CI job is
    /// inert. Against an ADVERSARY the gain is zero: whoever can set
    /// <c>TEMPO_TEST_DIAGRAM_SCHEMA_SKIP_LOCK</c> in a host's environment can set
    /// <c>ASPNETCORE_ENVIRONMENT</c> in that same environment, in the same breath. The sentence this
    /// replaced promised a protection this does not provide.
    /// </para>
    /// </summary>
    private static bool TestSeamsAreOpen()
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(environment))
        {
            environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        }

        return string.Equals(
            environment,
            TheEnvironmentTheTestSeamsBelongTo,
            StringComparison.OrdinalIgnoreCase);
    }

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

        // Both test seams live inside this one branch, so that a host outside Development does not merely
        // ignore the three variables — it never reads them.
        if (TestSeamsAreOpen())
        {
            WaitForTestBarrierIfArmed();

            if (string.Equals(
                    Environment.GetEnvironmentVariable(TestSkipLockEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                WidenTheBareCreateSoTwoProcessesCanMeet(context);
                context.Database.EnsureCreated();
                return;
            }
        }

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
    /// When both test variables are set, writes a pid file into the ready directory and waits to be
    /// released.
    /// <para>
    /// IT IS NOT REACHED OUTSIDE DEVELOPMENT, AND THAT IS THE CALLER'S DOING. <see cref="EnsureCreated"/>
    /// calls this only inside its <c>TestSeamsAreOpen</c> branch, so a host in any other environment does
    /// not read <see cref="TestReadyDirEnvironmentVariable"/> or
    /// <see cref="TestGoFileEnvironmentVariable"/> at all: it cannot be parked on a barrier nobody intends
    /// to release, and cannot throw <see cref="TimeoutException"/> out of startup. This paragraph used to
    /// say "production never sets them, so this is a no-op on every real host", which described what an
    /// operator would do rather than what this code permits.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The wait is BEFORE the mutex on purpose: releasing two hosts together is what makes an empty
    /// failure list (or a named SQLite error) mean they actually met. A host that took the lock
    /// before its neighbour existed would finish creating while the other was still starting, and
    /// the cross-process tooth would go green without ever entering the window.
    /// </remarks>
    private static void WaitForTestBarrierIfArmed()
    {
        var readyDir = Environment.GetEnvironmentVariable(TestReadyDirEnvironmentVariable);
        var goFile = Environment.GetEnvironmentVariable(TestGoFileEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(readyDir) || string.IsNullOrWhiteSpace(goFile))
        {
            return;
        }

        Directory.CreateDirectory(readyDir);
        File.WriteAllText(
            Path.Combine(readyDir, Environment.ProcessId.ToString(CultureInfo.InvariantCulture)),
            "ready");

        var deadline = DateTime.UtcNow + TestBarrierTimeout;
        long releaseTicks = 0;
        while (releaseTicks == 0)
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    "The test schema-creation barrier was not released within "
                    + TestBarrierTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)
                    + "s.");
            }

            if (File.Exists(goFile)
                && long.TryParse(
                    File.ReadAllText(goFile).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var ticks)
                && ticks > 0)
            {
                releaseTicks = ticks;
                break;
            }

            Thread.Sleep(10);
        }

        var releaseAt = new DateTime(releaseTicks, DateTimeKind.Utc);
        while (DateTime.UtcNow < releaseAt)
        {
            Thread.SpinWait(50);
        }
    }

    /// <summary>
    /// Opens the check-then-act gap so two processes released together actually overlap. CREATE TABLE
    /// was measured at 0 ms: without this pause the second host arrives at a database that already has
    /// the table and no-ops, which is indistinguishable from "they never met".
    /// <para>
    /// NON-DETERMINISM SOURCE (LIVE): THE 100 ms IS A RESERVE, NOT A GUARANTEE. It is the whole margin the
    /// bypass arm has for its neighbour to arrive inside the widened gap, and 100 ms of it has never been
    /// measured against a loaded runner — a machine that takes longer than that to schedule the second
    /// process turns the arm's red into a green that means "they never met". This is the known flake in
    /// <c>TwoDemoApiProcesses_BypassingTheLock_NameTheSqliteRace</c>. Not fixed here; recorded as a queue
    /// row.
    /// </para>
    /// </summary>
    private static void WidenTheBareCreateSoTwoProcessesCanMeet(DbContext context)
    {
        if (DiagramSnapshotsTableExists(context))
        {
            return;
        }

        Thread.Sleep(100);
    }

    private static bool DiagramSnapshotsTableExists(DbContext context)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
            var name = command.CreateParameter();
            name.ParameterName = "$name";
            name.Value = "DiagramSnapshots";
            command.Parameters.Add(name);
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }

    /// <summary>
    /// The mutex name for a database file: a hash of its full path, because a mutex name may not contain a
    /// path separator on every platform this runs on, and because the name has to be identical in two
    /// processes that spelled the same file differently.
    /// <para>
    /// INTERNAL RATHER THAN PRIVATE BECAUSE A TOOTH HAS TO HOLD THIS VERY MUTEX. Showing that a host
    /// outside Development still WAITS for the lock means taking the lock away from it first, and a test
    /// that recomputed the name would be a second source of truth: once the two spellings drifted, the
    /// test would be holding a mutex no host shares and the arm that asserts "it walked past" would pass
    /// without anything ever being contended.
    /// </para>
    /// </summary>
    internal static string LockNameFor(string databasePath)
    {
        var canonical = Path.GetFullPath(databasePath);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"tempo-blazor-demo-diagram-schema-{Convert.ToHexString(digest)[..32]}");
    }
}
