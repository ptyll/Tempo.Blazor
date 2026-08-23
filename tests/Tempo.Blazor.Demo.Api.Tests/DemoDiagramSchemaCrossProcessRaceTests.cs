using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Tempo.Blazor.Demo.Api.Data;
using Xunit.Abstractions;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// Starts two Demo.Api hosts as separate OS processes against ONE schema-less database file, and
/// requires the named lock to be what lets both of them through.
/// <para>
/// WHAT THIS MEASURES THAT <see cref="DemoDiagramSchemaRaceTests"/> DOES NOT. That tooth releases
/// six creators inside ONE process. Over that population an UNNAMED mutex would pass exactly the
/// same, so a green there is the within-process half. The named half is the reason the lock lives
/// in the host at all: the e2e lane starts the demo as its OWN process. Nothing else in this
/// repository contended that half — the e2e launcher starts its hosts sequentially behind
/// <c>HostLock</c>, with a stateful guard, into a per-PID directory — so a green e2e run is green
/// by construction, not a measurement of two processes meeting. This tooth is that meeting.
/// </para>
/// <para>
/// THE ACTORS ARE DEMO.API HOST PROCESSES, not in-process <c>DbContext</c> threads and not a
/// mutation of the e2e launcher. PlaywrightTestBase's <c>HostLock</c> and per-pid directory are
/// the production e2e arrangement; changing them to manufacture contention would replace one
/// source of non-determinism with another. The hosts below are started from the Demo.Api
/// executable this project already copies into its output, pointed at a throwaway file, on
/// throwaway ports.
/// </para>
/// <para>
/// THE POSITIVE CONTROL IS A COMMITTED TEST, not a remark. A green "both hosts started" is
/// indistinguishable from "the two processes never met" unless the same arrangement, with the
/// lock bypassed, fails and names
/// <c>SQLite Error 1: 'table "DiagramSnapshots" already exists'</c>. That is
/// <see cref="TwoDemoApiProcesses_BypassingTheLock_NameTheSqliteRace"/>. Both tests put the
/// hosts on a go-file barrier (a timestamp both spin to) so the first cannot finish creating
/// before the second opens the file; without that barrier the window this exists for is never
/// entered. The bypass arm also widens the check-then-act gap — CREATE TABLE was measured at
/// 0 ms, and two processes released together then often serialize, the second no-oping. The
/// locked arm does not widen anything. Both refuse a database that already has the table, for
/// the same reason the in-process tooth does: that is how the committed file hid the defect.
/// </para>
/// <para>
/// WHAT A GREEN HERE DOES NOT PROVE. Not that <c>Program</c> routes through the seam — that is a
/// property of a call site, and an assertion that read source text would prove a line exists,
/// never that it runs. The hosts below go through <c>Program</c> because they ARE that process;
/// they do not read the call site. And it does not prove the e2e lane contends this half: it
/// still starts one API host, sequentially, into a per-pid directory. The cheap check for THAT
/// lane is a recursive grep of an e2e <c>.trx</c> directory for <c>already exists</c>, which is
/// an observation point, not this proof.
/// </para>
/// <para>
/// SEVEN NON-DETERMINISM SOURCES ARE TAGGED HERE, FOUR OF THEM LIVE — AND SEVEN IS A FLOOR, NOT A
/// DENOMINATOR. Earlier in this workstream the number was said to be four. That four was the UNION OF
/// THREE REVIEW LENSES — a list of what somebody happened to look at — and a union of lenses says nothing
/// about the document having been searched. Seven is better than that but it is still not a denominator,
/// because THE TAG IS SELF-REPORTING: the grep below counts the sites that declared themselves, so it can
/// only ever go up when somebody writes another tag, never when somebody adds an untagged timing.
/// Two untagged ones are already visible in this very class — <see cref="HostStartupTimeout"/> (120 s) and
/// <see cref="AfterReleaseTimeout"/> (60 s) are both wall-clock deadlines against a loaded runner, and
/// neither carries the tag. THE NEEDLE FOR THE GAP, therefore, is not the count but a reading: every
/// <c>TimeSpan.From</c> constant in these two files, checked one by one against the tag list —
/// <c>/usr/bin/grep -nE 'TimeSpan\.From[A-Za-z]+\(' tests/Tempo.Blazor.Demo.Api.Tests/DemoDiagramSchemaCrossProcessRaceTests.cs src/Tempo.Blazor.Demo.Api/Data/DemoDiagramSchema.cs</c>
/// (6 lines, 2026-08-23 over 319e3c9e; 4 here + 2 there). What the seven ARE is a searched-and-tagged population: each is
/// tagged at its own site with FIXED or LIVE and counted mechanically, so the floor can be re-measured
/// rather than believed. It is not a constant either: THE SEVENTH ARRIVED WITH THIS PHASE — with the arm
/// that measures the seam gate, because an arm that has to wait out a lock has a window it has to choose.
/// <code>
/// /usr/bin/grep -cE 'NON-DETERMINISM SOURCE \((LIVE|FIXED)\)' \
///   tests/Tempo.Blazor.Demo.Api.Tests/DemoDiagramSchemaCrossProcessRaceTests.cs \
///   src/Tempo.Blazor.Demo.Api/Data/DemoDiagramSchema.cs          # 5 + 2 = 7
/// /usr/bin/grep -cE 'NON-DETERMINISM SOURCE \(LIVE\)' \
///   tests/Tempo.Blazor.Demo.Api.Tests/DemoDiagramSchemaCrossProcessRaceTests.cs \
///   src/Tempo.Blazor.Demo.Api/Data/DemoDiagramSchema.cs          # 3 + 1 = 4
/// </code>
/// FIXED are the harness picking ports by check-then-act, one constant serving as both the mutex wait and
/// the barrier deadline, and the unsynchronised read of the shared output buffer. LIVE are the 100 ms
/// widening sleep the bypass arm needs, the 500 ms go-lead measured against a 10 ms host poll and a 50 ms
/// harness poll, the CONTENT of the shared buffer at the moment a reader takes it — locking the read
/// stopped a crash, it did not make a line that has not arrived yet appear — and the derived hold in
/// <see cref="ADemoApiHostAskedToSkipTheLock_ContendsTheMutexOutsideDevelopment"/>. The four live ones are
/// recorded as queue rows; none of them is fixed in this file.
/// </para>
/// </summary>
public sealed class DemoDiagramSchemaCrossProcessRaceTests
{
    private const string DiagramSnapshotsTable = "DiagramSnapshots";
    private const string SqliteRaceMessage = """SQLite Error 1: 'table "DiagramSnapshots" already exists'""";
    private const string DevelopmentEnvironment = "Development";

    /// <summary>
    /// The environment the two seam-gate arms switch to. Any value other than <c>Development</c> would do,
    /// and <c>Production</c> is named here because it is the one an unattended host actually falls back to.
    /// It is NOT what a hand-started demo has: <c>Properties/launchSettings.json</c> puts
    /// <c>ASPNETCORE_ENVIRONMENT=Development</c> into the process, so <c>dotnet run</c> on this project
    /// lands on the OTHER side of the gate. This sentence used to claim the opposite.
    /// </summary>
    private const string OutsideDevelopmentEnvironment = "Production";

    private static readonly TimeSpan HostStartupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan AfterReleaseTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Asking for this port asks KESTREL to pick one, and the harness learns which by reading the port back
    /// out of the host's own "Now listening on" line. That line is already parsed for
    /// <see cref="DemoApiHost.Listening"/>, so the port arrives from the process that actually holds it.
    /// <para>
    /// NON-DETERMINISM SOURCE (FIXED): THE HARNESS PICKING BOTH PORTS BY CHECK-THEN-ACT.
    /// The harness used to pick both ports itself by binding port 0, reading the number and CLOSING the
    /// listener — then starting a host on it. That is a check-then-act over the ephemeral port range, the
    /// same shape as the <c>EnsureCreated</c> defect this file exists for: between the close and the host's
    /// bind the port belongs to nobody, and the second call happened while the FIRST host was still booting,
    /// so the kernel was free to hand out the same number twice (and any unrelated process on the machine
    /// was free to take it). Asking for 0 and never closing anything removes that window FROM THE TWO
    /// PRODUCTION ARMS rather than making it small.
    /// </para>
    /// <para>
    /// IT WAS MOVED, NOT DELETED — AND "the window disappears entirely" WOULD BE FALSE. The identical
    /// bind-read-close still stands in <see cref="APortToCollideOn"/>, deliberately: the collision arm needs
    /// a number the machine is likely to leave alone, and it WANTS two hosts to fight over it. So the
    /// check-then-act now exists in exactly one place instead of two, the one place whose whole purpose is
    /// a collision — and that place carries the consequence in its own words.
    /// </para>
    /// </summary>
    private const int LetKestrelChooseThePort = 0;

    /// <summary>
    /// Why a host is missing when Kestrel could not have its port. Kept apart from
    /// <see cref="DiedBeforeTheBarrierReason"/> ON PURPOSE: a port collision and a schema failure kill a
    /// host at the same OBSERVABLE moment — no "Now listening on", a process that is gone — and reporting
    /// both with the schema wording is what made the old flake unattributable. The
    /// <c>TwoDemoApiProcesses_ForcedOntoOnePort_...</c> arm is the off-diagonal that proves this branch is
    /// reachable and that its text differs from the barrier one.
    /// </summary>
    private const string DiedOnItsPortReason =
        "a host died because Kestrel could not take the port it was given. That is a PORT COLLISION in the "
        + "harness, not the schema race this tooth exists for, and reading it as one would rename the "
        + "defect instead of fixing it";

    /// <summary>
    /// Why a host is missing when nothing in its output blames the port. This is the wording the old harness
    /// used for every death, including port collisions.
    /// </summary>
    private const string DiedBeforeTheBarrierReason =
        "a host that died before the barrier was released never entered the window this test exists for";

    /// <summary>
    /// Why a host is missing when the harness CANNOT TELL — the third answer, and the reason the classifier
    /// stopped having to guess. Nothing in the output blames a port and the barrier is already released, so
    /// both of the other sentences would be assertions the harness has no evidence for. An unattributed red
    /// that says so is worth more than an attributed red that is wrong, because the wrong one sends the
    /// next reader to the schema.
    /// </summary>
    private const string CouldNotTellWhyReason =
        "a host is missing and this harness CANNOT SAY WHY: nothing in the two hosts' output blames a port, "
        + "and the barrier had already been released, so naming the barrier would be a guess printed as a "
        + "finding. The cause is in the output below and has to be read from it, not inferred from here";

    /// <summary>
    /// Kestrel's two spellings for "someone else has this port", one from the hosting layer and one from the
    /// socket underneath it. Both are matched because which of them reaches stdout depends on how far the
    /// exception got unwrapped, not on what happened.
    /// </summary>
    private static readonly string[] PortCollisionSignatures =
    [
        "Failed to bind to address",
        "Address already in use"
    ];

    /// <summary>
    /// Which side of the go-file a caller of <see cref="WhyAHostIsMissing"/> is standing on. It is a
    /// parameter and not a field because it is a property of the CALL SITE, and the call sites differ:
    /// three quarters of them run after the release, where the barrier sentence is simply untrue.
    /// </summary>
    private enum BarrierState
    {
        /// <summary>The go-file has not been written yet; a host that is gone died before the window.</summary>
        NotYetReleased,

        /// <summary>The go-file has been written; the barrier explains nothing from here on.</summary>
        Released
    }

    private readonly ITestOutputHelper _output;

    public DemoDiagramSchemaCrossProcessRaceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TwoDemoApiProcesses_OverADatabaseWithNoSchema_BothSucceed()
    {
        RunTwoHosts(
            skipLock: false,
            onReleased: (hosts, databasePath, output) =>
            {
                WaitUntil(
                    AfterReleaseTimeout,
                    () => hosts.All(h => h.HasExited) || hosts.All(h => h.Listening),
                    () => "waiting for both Demo.Api hosts to listen (lock held). "
                          + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                          + Environment.NewLine + Snapshot(output));

                hosts.Should().OnlyContain(
                    h => !h.HasExited,
                    "a host that exited is not a pass; the named lock is supposed to make them "
                    + "wait for each other for milliseconds, so a crash after the barrier is the "
                    + "defect this holds — unless "
                    + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                    + Environment.NewLine + Snapshot(output));

                hosts.Should().OnlyContain(
                    h => h.Listening,
                    "both hosts must have come through EnsureCreated and reached Kestrel. One "
                    + "silent stall would look like 'they never met' dressed as a hang. THIS IS ALSO "
                    + "WHERE THE HARNESS'S OWN PORT DEFECT SURFACED, which is worth saying here "
                    + "because the place it is EXPLAINED (LetKestrelChooseThePort) is not the place a "
                    + "reader meets it: a host handed a port some other process already holds does not "
                    + "hang and is not obviously gone — Kestrel refuses the bind, the host never prints "
                    + "'Now listening on', and it arrives at this line as a host that is simply not "
                    + "Listening. Before the reason was split out, that produced the barrier sentence "
                    + "over a port collision — unless "
                    + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                    + Environment.NewLine + Snapshot(output));

                TableExists(databasePath, DiagramSnapshotsTable).Should().BeTrue(
                    "the hosts are required to have CREATED the schema, not merely to have "
                    + "survived. A seam that skipped creation would satisfy 'both still running' "
                    + "and move the failure to every later reader");
            });
    }

    [Fact]
    public void TwoDemoApiProcesses_BypassingTheLock_NameTheSqliteRace()
    {
        RunTwoHosts(
            skipLock: true,
            onReleased: (hosts, databasePath, output) =>
            {
                WaitUntil(
                    AfterReleaseTimeout,
                    () => hosts.Any(h => h.HasExited)
                          && Snapshot(output).Contains(SqliteRaceMessage, StringComparison.Ordinal),
                    () => "waiting for the lock-bypassing arm to lose the race. If both hosts "
                          + "stay up, this arrangement never met and a green on the locked arm "
                          + "would be indistinguishable from that. Output:"
                          + Environment.NewLine + Snapshot(output));

                Snapshot(output).Should().Contain(
                    SqliteRaceMessage,
                    "the mutation that walks around the named mutex must fail ON THIS MESSAGE, "
                    + "which is the defect the lock exists for, not on a timeout or a bind "
                    + "failure. Looked in the two hosts' combined output");

                TableExists(databasePath, DiagramSnapshotsTable).Should().BeTrue(
                    "the loser is only a loser if a neighbour actually created the table. A pair "
                    + "that crashed before either CREATE would name some other exception and this "
                    + "would not be a measurement of the race");
            });
    }

    /// <summary>
    /// THE OFF-DIAGONAL FOR THE PORT FIX. A green on the two arms above says nothing about ports: they now
    /// ask Kestrel for a port and cannot collide, so the branch that reports a collision would never run and
    /// could rot to anything at all. This arm creates the condition the old harness could produce by
    /// accident — both hosts handed ONE port — and requires the harness to say PORT rather than reuse the
    /// barrier wording. Without that separation the defect would survive under a new name.
    /// </summary>
    [Fact]
    public void TwoDemoApiProcesses_ForcedOntoOnePort_NameThePortCollisionNotTheBarrier()
    {
        var collisionPort = APortToCollideOn();

        RunTwoHosts(
            skipLock: false,
            forceBothHostsOntoThisPort: collisionPort,
            onReleased: (hosts, _, output) =>
            {
                WaitUntil(
                    AfterReleaseTimeout,
                    () => hosts.Any(h => h.HasExited)
                          && hosts.All(h => h.HasExited || h.Listening)
                          && string.Equals(
                              WhyAHostIsMissing(output, BarrierState.Released),
                              DiedOnItsPortReason,
                              StringComparison.Ordinal),
                    () => "waiting for one of two hosts handed THE SAME port to lose its bind. If "
                          + "both stay up there was no collision, this arm is not an off-diagonal, "
                          + "and the distinction it exists to prove was never exercised. Output:"
                          + Environment.NewLine + Snapshot(output));

                var listeners = hosts.Where(h => h.Listening).ToList();

                listeners.Should().ContainSingle(
                    "two processes handed ONE port cannot both bind it, so exactly one of them has to "
                    + "be listening. Two would mean the harness quietly gave them different ports and "
                    + "there was nothing to collide; none would mean a stranger took the port between "
                    + "APortToCollideOn closing its listener and the hosts binding. Either way the "
                    + "PRECONDITION of this arm did not hold, and an arm whose precondition failed has "
                    + "to say so rather than pass quietly. Output:"
                    + Environment.NewLine + Snapshot(output));

                listeners[0].ObservedPort.Should().Be(
                    collisionPort,
                    "the surviving host must have bound THE port this arm forced both of them onto, "
                    + "read back out of its own 'Now listening on' line — that is what makes the "
                    + "collision below a collision. Comparing RequestedPort with collisionPort instead "
                    + "would compare the harness's own parameter with the value it passed in two lines "
                    + "earlier and assert nothing at all, while ObservedPort — the only witness of what "
                    + "the KERNEL actually granted — would never be read by anyone. Output:"
                    + Environment.NewLine + Snapshot(output));

                hosts.Should().Contain(
                    h => h.HasExited,
                    "two processes cannot both bind 127.0.0.1:"
                    + collisionPort.ToString(CultureInfo.InvariantCulture)
                    + "; one of them has to be gone, and if neither is then this port was not "
                    + "shared after all. Output:" + Environment.NewLine + Snapshot(output));

                var reported = WhyAHostIsMissing(output, BarrierState.Released);
                _output.WriteLine("PORT-COLLISION REASON: " + reported);
                _output.WriteLine("BARRIER REASON:        " + DiedBeforeTheBarrierReason);

                reported.Should().Be(
                    DiedOnItsPortReason,
                    "a host killed by a port collision must be reported as a port collision. This "
                    + "is the branch that made the old flake unattributable: the harness had one "
                    + "sentence for every missing host and it pointed at the schema");

                reported.Should().NotBe(
                    DiedBeforeTheBarrierReason,
                    "the two reasons must be TELLABLE APART in the red, which is the whole point. "
                    + "Reusing the barrier sentence here would rename the defect, not remove it");
            });
    }

    /// <summary>
    /// THE TOOTH FOR THE SPLIT CONSTANT. Two numbers, in two projects, that nothing forced into an order
    /// until now: the host-side barrier deadline and the harness's patience for a host to start. While the
    /// barrier borrowed the 30 s mutex wait it was HALF the harness's 120 s, so on a cold runner the first
    /// host killed itself with a <see cref="TimeoutException"/> while the run watching it still considered
    /// it healthy — and that red was indistinguishable from a schema failure.
    /// <para>
    /// Cheap on purpose: it reads two constants and compares them. What makes it worth having is not what it
    /// costs but what it catches — a future edit that retunes either number without noticing the other, which
    /// is exactly how the two came to be one number in the first place.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTestBarrierDeadline_OutlivesTheHarnessWaitForAHostToStart()
    {
        _output.WriteLine(
            "barrier deadline = "
            + DemoDiagramSchema.TestBarrierTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)
            + "s, harness host-startup timeout = "
            + HostStartupTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + "s");

        DemoDiagramSchema.TestBarrierTimeout.Should().BeGreaterThan(
            HostStartupTimeout,
            "a host on the barrier must be able to outwait the harness. If the barrier expires first "
            + "the host throws and dies while the run is still waiting for it, and the red names the "
            + "barrier rather than whatever was actually slow. The barrier is therefore derived from "
            + "this timeout, not from DemoDiagramSchema's mutex wait, which answers a different "
            + "question entirely");
    }

    /// <summary>
    /// THE OFF-DIAGONAL FOR THE BARRIER HALF OF THE SEAM GATE, AND IT TURNS ON ONE VARIABLE.
    /// <see cref="TwoDemoApiProcesses_BypassingTheLock_NameTheSqliteRace"/> hands two hosts the three
    /// TEMPO_TEST_DIAGRAM_SCHEMA_* names and they rendezvous: exactly two ready files appear, and
    /// <see cref="RunTwoHosts"/> refuses to go on without them. This arm hands two hosts the SAME three
    /// names, on the same executable, against the same kind of schema-less file, and changes
    /// <c>ASPNETCORE_ENVIRONMENT</c> to <see cref="OutsideDevelopmentEnvironment"/>. Nothing may appear in
    /// the ready directory, because a host outside Development must not read those two names at all.
    /// <para>
    /// THE GO-FILE IS WRITTEN BEFORE THE HOSTS START, ON PURPOSE. Withholding it would make "both hosts
    /// reached Kestrel" the assertion doing the work, and then any host that hung for an unrelated reason
    /// would be reported as a barrier that stayed armed — an attributable-looking red for an unattributed
    /// death, which is the defect this file already fixed once. With the release lying on disk from the
    /// start, an armed host sails through the barrier and STILL WRITES ITS READY FILE, because that write
    /// is unconditional once the two variables are set. So the empty directory is the whole discriminator,
    /// and it is a positive observation rather than an absence of noise.
    /// </para>
    /// <para>
    /// WHAT THE ABSENT SQLITE MESSAGE HERE DOES NOT PROVE. Both hosts also carry <c>SKIP_LOCK=1</c> and
    /// neither races. That is what the gate is for — but with the barrier closed nothing forces these two
    /// processes to overlap, so a green on that message ALONE would be indistinguishable from "they never
    /// met", which is exactly the confusion the rest of this file exists to avoid. It is asserted because a
    /// red on it would be worth knowing, not because it carries the claim. The lock half of the gate is
    /// measured by <see cref="ADemoApiHostAskedToSkipTheLock_ContendsTheMutexOutsideDevelopment"/>, which
    /// takes the mutex away from the host instead of hoping two processes collide.
    /// </para>
    /// </summary>
    [Fact]
    public void TwoDemoApiProcesses_OutsideDevelopment_DoNotArmTheSchemaTestBarrier()
    {
        var directory = NewThrowawayDirectory();
        var databasePath = Path.Combine(directory, "diagrams.db");
        var readyDir = Path.Combine(directory, "ready");
        Directory.CreateDirectory(readyDir);
        var goFile = Path.Combine(directory, "go");
        var output = new StringBuilder();
        var hosts = new List<DemoApiHost>();

        try
        {
            TableExists(databasePath, DiagramSnapshotsTable).Should().BeFalse(
                "these hosts must find a database that still needs its schema, for the same reason the "
                + "arms above do: a file that arrives with the table in it makes every host a no-op. "
                + "Looked in: " + databasePath);

            File.WriteAllText(
                goFile,
                DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));

            var executable = DemoApiExecutablePath();
            File.Exists(executable).Should().BeTrue(
                "this arm starts the same Demo.Api executable the arms above start; a missing file "
                + "would make every assertion below about nothing. Looked for: " + executable);

            hosts.Add(StartHost(
                executable, databasePath, readyDir, goFile, skipLock: true, LetKestrelChooseThePort,
                output, OutsideDevelopmentEnvironment));
            hosts.Add(StartHost(
                executable, databasePath, readyDir, goFile, skipLock: true, LetKestrelChooseThePort,
                output, OutsideDevelopmentEnvironment));

            WaitUntil(
                HostStartupTimeout,
                () => hosts.All(h => h.Listening) || hosts.Any(h => h.HasExited),
                () => "waiting for two hosts outside Development to reach Kestrel. "
                      + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                      + Environment.NewLine + Snapshot(output));

            Directory.GetFiles(readyDir).Should().BeEmpty(
                "THIS IS THE MEASUREMENT. Both hosts were handed "
                + DemoDiagramSchema.TestReadyDirEnvironmentVariable + " and "
                + DemoDiagramSchema.TestGoFileEnvironmentVariable
                + ", and the only difference from the arm that produces two ready files is "
                + "ASPNETCORE_ENVIRONMENT. A file here means a host outside Development still reads the "
                + "barrier variables, and a host that reads them can be parked on a barrier nobody will "
                + "release and killed by a TimeoutException thrown out of its own startup. Looked in: "
                + readyDir + ". Output:" + Environment.NewLine + Snapshot(output));

            hosts.Should().OnlyContain(
                h => !h.HasExited,
                "a host that exited never got far enough to tell us anything about the seam gate — "
                + "unless " + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                + Environment.NewLine + Snapshot(output));

            Snapshot(output).Should().NotContain(
                SqliteRaceMessage,
                "the bypass variable was set on both hosts and must have been ignored. This is the "
                + "weaker half of this arm and says so: with the barrier closed nothing made these two "
                + "processes overlap, so a green here is consistent with 'they never met'. It is the "
                + "mutex-contention arm that carries the lock half of the claim");

            TableExists(databasePath, DiagramSnapshotsTable).Should().BeTrue(
                "the hosts are required to have CREATED the schema, not merely to have survived a "
                + "closed seam; a gate that also stopped schema creation would move the failure to "
                + "every later reader. Looked in: " + databasePath);
        }
        finally
        {
            _output.WriteLine(Snapshot(output));
            foreach (var host in hosts)
            {
                host.Dispose();
            }

            TryDeleteDirectory(directory);
        }
    }

    /// <summary>
    /// THE OFF-DIAGONAL FOR THE LOCK HALF OF THE SEAM GATE. One host, told to skip the lock, run twice
    /// against the harness holding that very mutex — and the only thing that differs between the two runs
    /// is <c>ASPNETCORE_ENVIRONMENT</c>.
    /// <para>
    /// WHY NOT TWO RACING PROCESSES. The gate closes the barrier as well, so outside Development there is
    /// no rendezvous left to release two hosts together, and the plan's arrangement — both arms on the
    /// barrier — cannot exist once the barrier obeys the same gate. Waiting for two processes to collide
    /// by luck would put the claim on the 100 ms widening reserve, which is the known flake in
    /// <see cref="TwoDemoApiProcesses_BypassingTheLock_NameTheSqliteRace"/>. Holding the mutex instead
    /// makes the discriminator deterministic: a host that honours the bypass reaches Kestrel while this
    /// process owns the lock, and a host that does not, cannot.
    /// </para>
    /// <para>
    /// THE DEVELOPMENT ARM IS ALSO THE REACHABILITY CONTROL. It proves that on THIS machine, in THIS run,
    /// a Demo.Api host handed these settings does reach "Now listening on" while the mutex is held — so
    /// the second arm's silence is a statement about the lock rather than about a host that had not
    /// finished booting. The second arm adds two more controls of its own: it does not start timing until
    /// the host has created the database's directory (<c>Program</c>'s first act, long before schema
    /// creation), and it requires the host to listen once the mutex is RELEASED, which is what a host
    /// blocked on that mutex does and a host that merely died does not.
    /// </para>
    /// <para>
    /// NON-DETERMINISM SOURCE (LIVE): THE LENGTH OF THE HOLD IN THE SECOND ARM. It is derived — three
    /// times what the Development arm needed on this machine moments earlier, floored at 5 s and capped
    /// below the host's own 30 s mutex wait — but derived is not guaranteed. If the second host is still
    /// booting when the hold expires, this arm reports "it did not listen" about a host that never reached
    /// the lock, and that is a green which measured nothing. The directory signal shrinks the window to
    /// the span between "startup began" and "schema creation", it does not remove it. Not fixed here;
    /// recorded as a queue row.
    /// </para>
    /// </summary>
    [Fact]
    public void ADemoApiHostAskedToSkipTheLock_ContendsTheMutexOutsideDevelopment()
    {
        var output = new StringBuilder();
        var executable = DemoApiExecutablePath();
        File.Exists(executable).Should().BeTrue(
            "both arms below start this executable. Looked for: " + executable);

        var developmentDirectory = NewThrowawayDirectory();
        var developmentDatabase = Path.Combine(developmentDirectory, "db", "diagrams.db");
        TimeSpan developmentTimeToListen;
        DemoApiHost? developmentHost = null;

        try
        {
            using var heldByTheHarness = TakeTheSchemaLockAwayFromTheHosts(developmentDatabase);
            var startedAt = DateTime.UtcNow;
            developmentHost = StartHost(
                executable, developmentDatabase, readyDir: null, goFile: null, skipLock: true,
                LetKestrelChooseThePort, output, DevelopmentEnvironment);

            WaitUntil(
                HostStartupTimeout,
                () => developmentHost.Listening || developmentHost.HasExited,
                () => "waiting for a Development host that was told to skip the lock to reach Kestrel "
                      + "while this process holds that lock. If it never arrives, either the bypass seam "
                      + "is not honoured in Development — which would silence the positive control the "
                      + "whole cross-process tooth depends on — or the host could not start at all. "
                      + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                      + Environment.NewLine + Snapshot(output));

            developmentTimeToListen = DateTime.UtcNow - startedAt;

            developmentHost.Listening.Should().BeTrue(
                "the Development arm must walk past a mutex this process owns. Output:"
                + Environment.NewLine + Snapshot(output));

            TableExists(developmentDatabase, DiagramSnapshotsTable).Should().BeTrue(
                "walking past the lock means it created the schema without it, which is the very "
                + "capability the gate has to withhold from every other environment. Looked in: "
                + developmentDatabase);

            heldByTheHarness.ReleaseMutex();
        }
        finally
        {
            developmentHost?.Dispose();
            TryDeleteDirectory(developmentDirectory);
        }

        var hold = TimeSpan.FromMilliseconds(
            Math.Clamp(developmentTimeToListen.TotalMilliseconds * 3, 5000, 20000));
        _output.WriteLine(
            "DEVELOPMENT ARM: reached Kestrel in "
            + developmentTimeToListen.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)
            + " ms with the mutex held (bypass honoured). PRODUCTION ARM will hold the mutex for "
            + hold.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) + " ms.");

        var productionDirectory = NewThrowawayDirectory();
        var productionDatabaseDirectory = Path.Combine(productionDirectory, "db");
        var productionDatabase = Path.Combine(productionDatabaseDirectory, "diagrams.db");
        DemoApiHost? productionHost = null;

        try
        {
            using var heldByTheHarness = TakeTheSchemaLockAwayFromTheHosts(productionDatabase);
            productionHost = StartHost(
                executable, productionDatabase, readyDir: null, goFile: null, skipLock: true,
                LetKestrelChooseThePort, output, OutsideDevelopmentEnvironment);

            WaitUntil(
                HostStartupTimeout,
                () => Directory.Exists(productionDatabaseDirectory) || productionHost.HasExited,
                () => "waiting for the host outside Development to enter startup at all. Program creates "
                      + "the database's directory as one of its first acts, well before schema creation, "
                      + "so this is the cheapest evidence that the process is running rather than still "
                      + "being loaded — without it, a silent host below would be unattributable between "
                      + "'held by the lock' and 'not started yet'. Looked for: "
                      + productionDatabaseDirectory + ". Output:"
                      + Environment.NewLine + Snapshot(output));

            productionHost.HasExited.Should().BeFalse(
                "a host that died on startup measures nothing about the lock. "
                + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                + Environment.NewLine + Snapshot(output));

            var heldUntil = DateTime.UtcNow + hold;
            while (DateTime.UtcNow < heldUntil && !productionHost.Listening && !productionHost.HasExited)
            {
                Thread.Sleep(50);
            }

            productionHost.HasExited.Should().BeFalse(
                "the host must still be alive at the end of the hold; a dead one cannot distinguish "
                + "'waited for the lock' from 'crashed'. "
                + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                + Environment.NewLine + Snapshot(output));

            productionHost.Listening.Should().BeFalse(
                "THIS IS THE MEASUREMENT. The same executable, the same SKIP_LOCK=1, the same held "
                + "mutex — only ASPNETCORE_ENVIRONMENT changed, and it is the one that reached Kestrel "
                + "in "
                + developmentTimeToListen.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)
                + " ms a moment ago. Listening here means a host outside Development still obeys "
                + DemoDiagramSchema.TestSkipLockEnvironmentVariable
                + " and creates the schema with a bare EnsureCreated(), which is the check-then-act the "
                + "named lock exists to remove. Output:" + Environment.NewLine + Snapshot(output));

            TableExists(productionDatabase, DiagramSnapshotsTable).Should().BeFalse(
                "and it must not have created the schema either: reaching CREATE TABLE while this "
                + "process owns the lock is the same bypass seen from the database side. Looked in: "
                + productionDatabase);

            heldByTheHarness.ReleaseMutex();

            WaitUntil(
                AfterReleaseTimeout,
                () => productionHost.Listening || productionHost.HasExited,
                () => "waiting for the host to come through now that the lock is free. THIS IS THE "
                      + "CONTROL FOR THE SILENCE ABOVE: a host that was blocked on the mutex proceeds "
                      + "within milliseconds of its release, while a host that was merely slow, or gone, "
                      + "does not care that anything was released. "
                      + WhyAHostIsMissing(output, BarrierState.Released) + ". Output:"
                      + Environment.NewLine + Snapshot(output));

            productionHost.Listening.Should().BeTrue(
                "the host has to finish once it gets the lock — the gate closes the bypass, it does not "
                + "stop a host from starting. Output:" + Environment.NewLine + Snapshot(output));

            TableExists(productionDatabase, DiagramSnapshotsTable).Should().BeTrue(
                "and it has to have created the schema THROUGH the lock. Looked in: "
                + productionDatabase);
        }
        finally
        {
            _output.WriteLine(Snapshot(output));
            productionHost?.Dispose();
            TryDeleteDirectory(productionDirectory);
        }
    }

    /// <summary>
    /// A port for the collision arm, taken the OLD way — bind 0, read, close — because that is the only way
    /// to name a port the machine is likely to leave alone, and this arm WANTS the two hosts to fight over
    /// it.
    /// <para>
    /// THE CHECK-THEN-ACT THE PRODUCTION ARMS SHED IS THE ONE STANDING HERE. It was not eliminated from
    /// this file, it was relocated into the single method whose purpose is a collision — so the honest
    /// statement is "moved to where it is wanted", never "gone". Between the close above and a host's bind
    /// the port belongs to nobody, and a stranger may take it.
    /// </para>
    /// <para>
    /// AND THAT NO LONGER LEAVES EVERY ASSERTION STANDING, WHICH IS THE POINT. While the arm asserted only
    /// HOW a lost bind is reported, a stranger merely made both hosts lose instead of one and nothing
    /// noticed. The arm now also asserts that the SURVIVOR bound this very number, so a stranger leaves no
    /// survivor and the arm goes red on <c>listeners.Should().ContainSingle</c>. That red is correct: it
    /// says the precondition of the off-diagonal was not met on this run, which is a thing a reader has to
    /// be told, not a thing to pass over in silence.
    /// </para>
    /// </summary>
    private static int APortToCollideOn()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void RunTwoHosts(
        bool skipLock,
        Action<IReadOnlyList<DemoApiHost>, string, StringBuilder> onReleased,
        int forceBothHostsOntoThisPort = LetKestrelChooseThePort)
    {
        var directory = NewThrowawayDirectory();
        var databasePath = Path.Combine(directory, "diagrams.db");
        var readyDir = Path.Combine(directory, "ready");
        Directory.CreateDirectory(readyDir);
        var goFile = Path.Combine(directory, "go");
        var output = new StringBuilder();
        var hosts = new List<DemoApiHost>();

        try
        {
            TableExists(databasePath, DiagramSnapshotsTable).Should().BeFalse(
                "every host below is meant to find a database that still needs its schema; if the "
                + "file arrived with the table already in it, each of them would be a no-op and "
                + "this test would pass without one of them ever racing another — which is "
                + "precisely how the committed database hid this defect. Looked in: "
                + databasePath);

            var executable = DemoApiExecutablePath();
            File.Exists(executable).Should().BeTrue(
                "this tooth starts the Demo.Api executable this project copies into its output; "
                + "a missing file would mean we never started a host and every assertion below "
                + "would be about nothing. Looked for: " + executable);

            hosts.Add(StartHost(
                executable, databasePath, readyDir, goFile, skipLock, forceBothHostsOntoThisPort, output));
            hosts.Add(StartHost(
                executable, databasePath, readyDir, goFile, skipLock, forceBothHostsOntoThisPort, output));

            WaitUntil(
                HostStartupTimeout,
                () => Directory.GetFiles(readyDir).Length >= 2 || hosts.Any(h => h.HasExited),
                () => "waiting for both hosts to arrive at EnsureCreated and write a ready file. "
                      + "Without that rendezvous the first host can finish creating before the "
                      + "second opens the file. "
                      + WhyAHostIsMissing(output, BarrierState.NotYetReleased) + ". Output:"
                      + Environment.NewLine + Snapshot(output));

            hosts.Should().OnlyContain(
                h => !h.HasExited,
                WhyAHostIsMissing(output, BarrierState.NotYetReleased) + ". Output:"
                + Environment.NewLine + Snapshot(output));

            Directory.GetFiles(readyDir).Length.Should().Be(
                2,
                "both processes must have reached the test barrier inside "
                + "DemoDiagramSchema.EnsureCreated. Fewer ready files means they did not meet. "
                + "Output:" + Environment.NewLine + Snapshot(output));

            TableExists(databasePath, DiagramSnapshotsTable).Should().BeFalse(
                "the hosts have signalled ready and must NOT have created the schema yet — that "
                + "is the window. A table that exists before the barrier is released means the "
                + "rendezvous is a no-op and a later green is 'they never met'. Looked in: "
                + databasePath);

            // NON-DETERMINISM SOURCE (LIVE): THE 500 ms GO-LEAD AGAINST TWO POLLS. The go-file names a
            // release instant 500 ms out so both hosts spin to the same moment; the hosts notice the file
            // on a 10 ms poll (DemoDiagramSchema.WaitForTestBarrierIfArmed) and this harness notices its
            // consequences on a 50 ms poll (WaitUntil). 500 > 10 + 50 is the reason it works, and nobody
            // has measured what margin the lead actually has on a loaded runner — the number is chosen,
            // not derived. Not fixed here; recorded as a queue row.
            var releaseAt = DateTime.UtcNow.AddMilliseconds(500);
            File.WriteAllText(
                goFile,
                releaseAt.Ticks.ToString(CultureInfo.InvariantCulture));
            onReleased(hosts, databasePath, output);
        }
        finally
        {
            _output.WriteLine(Snapshot(output));
            foreach (var host in hosts)
            {
                host.Dispose();
            }

            TryDeleteDirectory(directory);
        }
    }

    private static string NewThrowawayDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "tempo-blazor-demo-api-schema-cross-process",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// Takes the schema lock for <paramref name="databasePath"/> away from any host that wants it, using
    /// <see cref="DemoDiagramSchema.LockNameFor"/> so the harness and the host cannot be holding two
    /// differently-named mutexes and calling it contention.
    /// </summary>
    private static Mutex TakeTheSchemaLockAwayFromTheHosts(string databasePath)
    {
        var mutex = new Mutex(initiallyOwned: false, DemoDiagramSchema.LockNameFor(databasePath));
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
            {
                throw new InvalidOperationException(
                    "the harness could not take the schema lock for " + databasePath
                    + ", so nothing below would be a measurement of a host waiting for it. A leftover "
                    + "holder from an earlier run is the usual reason.");
            }
        }
        catch (AbandonedMutexException)
        {
            // A previous run died holding it. The wait succeeded; this process owns it now.
        }

        return mutex;
    }

    private static string DemoApiExecutablePath()
        => Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "Tempo.Blazor.Demo.Api.exe" : "Tempo.Blazor.Demo.Api");

    /// <param name="environment">
    /// The value handed to both <c>ASPNETCORE_ENVIRONMENT</c> and <c>DOTNET_ENVIRONMENT</c>. It is a
    /// PARAMETER because it is the single variable the seam-gate off-diagonals turn: everything else about
    /// the two arms — executable, database, ports, the three TEMPO_TEST_DIAGRAM_SCHEMA_* names — is
    /// identical, so a difference in outcome has exactly one candidate cause.
    /// </param>
    private static DemoApiHost StartHost(
        string executable,
        string databasePath,
        string? readyDir,
        string? goFile,
        bool skipLock,
        int requestedPort,
        StringBuilder output,
        string environment = DevelopmentEnvironment)
    {
        var port = requestedPort;
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
        start.Environment[DemoDatabaseRedirect.EnvironmentVariable] = databasePath;
        if (readyDir is not null)
        {
            start.Environment[DemoDiagramSchema.TestReadyDirEnvironmentVariable] = readyDir;
        }

        if (goFile is not null)
        {
            start.Environment[DemoDiagramSchema.TestGoFileEnvironmentVariable] = goFile;
        }

        if (skipLock)
        {
            start.Environment[DemoDiagramSchema.TestSkipLockEnvironmentVariable] = "1";
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var host = new DemoApiHost(process, port);
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(args.Data);
                }

                if (args.Data.Contains("Now listening on", StringComparison.Ordinal))
                {
                    host.MarkListening(PortFromNowListeningOn(args.Data));
                }
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (output)
                {
                    output.AppendLine(args.Data);
                }
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start Tempo.Blazor.Demo.Api.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return host;
    }

    /// <summary>
    /// Reads the port back out of Kestrel's own "Now listening on: http://127.0.0.1:39473" line. Returns 0
    /// when the line does not carry a parsable port, because the harness must not invent one: this number is
    /// diagnostic, and a made-up value would be worse than none.
    /// </summary>
    private static int PortFromNowListeningOn(string line)
    {
        var lastColon = line.LastIndexOf(':');
        if (lastColon < 0 || lastColon == line.Length - 1)
        {
            return 0;
        }

        return int.TryParse(
            line.AsSpan(lastColon + 1).TrimEnd('/'),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var port)
            ? port
            : 0;
    }

    /// <summary>
    /// Reads the shared output buffer under the SAME lock its two writers take.
    /// <para>
    /// NON-DETERMINISM SOURCE (FIXED): AN UNSYNCHRONISED READ OF THE SHARED BUFFER. Both redirected
    /// streams append from thread-pool threads under <c>lock (output)</c>, but every READER — the assertion
    /// messages, the bypass arm's <c>Contains</c>, the dump in the finally block — took the string while
    /// those appends were still arriving. <see cref="StringBuilder"/> is not safe for a concurrent read, and
    /// it does not fail by returning stale text: it walks a chunk list that is being rewritten and throws
    /// <c>ArgumentOutOfRangeException (Parameter 'chunkLength')</c>. That red names neither the schema nor
    /// the port — it names the harness reading its own log — and it was observed in a full-suite run of this
    /// very tooth. The writers were already correct; only the reads were missing.
    /// </para>
    /// </summary>
    private static string Snapshot(StringBuilder output)
    {
        lock (output)
        {
            return output.ToString();
        }
    }

    /// <summary>
    /// Names why a host is not where the harness expected it, distinguishing a port collision from a death
    /// it cannot attribute. It is a CLASSIFIER, not a verdict: it is consulted only when an assertion has
    /// already decided a host is missing, and it answers "what should this red say", not "is this red".
    /// <para>
    /// ITS THIRD ANSWER IS "I DO NOT KNOW", AND IT EXISTS BECAUSE THE OTHER TWO WERE A GUESS. It used to
    /// have two, and the second — <see cref="DiedBeforeTheBarrierReason"/> — came back for every death that
    /// could not be blamed on a port. But of the call sites in this file only the pair inside
    /// <see cref="RunTwoHosts"/> runs while the go-file is still unwritten; every other one runs AFTER the
    /// release, where "died before the barrier" is not a cautious default but a false sentence printed
    /// inside a red — and a false sentence in a red sends the next reader to the schema. A classifier that
    /// is obliged to answer necessarily guesses, so this one is allowed to decline:
    /// <paramref name="barrier"/> makes the barrier sentence reachable only while the barrier really has
    /// not been released, and everything it cannot name comes back as
    /// <see cref="CouldNotTellWhyReason"/>.
    /// </para>
    /// <para>
    /// NON-DETERMINISM SOURCE (LIVE): WHAT THE BUFFER HOLDS AT THE MOMENT THIS READS IT.
    /// <see cref="Snapshot"/> made the read safe, not timely — a host's "Address already in use" travels a
    /// redirected pipe and a thread-pool callback, so a caller asking early enough sees a buffer that does
    /// not carry it yet and gets the un-attributed answer for a death that IS a port collision. Callers who
    /// depend on the distinction spin on it (the collision arm does) instead of asking once. Not fixed
    /// here; recorded as a queue row.
    /// </para>
    /// </summary>
    /// <param name="output">The two hosts' combined stdout and stderr, read under its writers' lock.</param>
    /// <param name="barrier">Which side of the go-file the caller is standing on.</param>
    private static string WhyAHostIsMissing(StringBuilder output, BarrierState barrier)
    {
        var text = Snapshot(output);
        foreach (var signature in PortCollisionSignatures)
        {
            if (text.Contains(signature, StringComparison.OrdinalIgnoreCase))
            {
                return DiedOnItsPortReason;
            }
        }

        return barrier == BarrierState.NotYetReleased
            ? DiedBeforeTheBarrierReason
            : CouldNotTellWhyReason;
    }

    private static void WaitUntil(TimeSpan timeout, Func<bool> done, Func<string> failure)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (done())
            {
                return;
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException(failure());
    }

    private static bool TableExists(string databasePath, string table)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        using var connection = new SqliteConnection("Data Source=" + databasePath);
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
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class DemoApiHost : IDisposable
    {
        private readonly Process _process;

        public DemoApiHost(Process process, int requestedPort)
        {
            _process = process;
            RequestedPort = requestedPort;
        }

        /// <summary>
        /// What the harness ASKED for. <see cref="LetKestrelChooseThePort"/> in both production arms; a real
        /// number only in the off-diagonal that forces a collision.
        /// </summary>
        public int RequestedPort { get; }

        /// <summary>
        /// What the host actually bound, read out of its own "Now listening on" line. 0 until it listens —
        /// and 0 for ever if it never does, which is the state a port collision leaves it in.
        /// </summary>
        public int ObservedPort { get; private set; }

        public bool Listening { get; private set; }

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public void MarkListening(int observedPort)
        {
            if (observedPort > 0)
            {
                ObservedPort = observedPort;
            }

            Listening = true;
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5_000);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            _process.Dispose();
        }
    }
}
