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
/// </summary>
public sealed class DemoDiagramSchemaCrossProcessRaceTests
{
    private const string DiagramSnapshotsTable = "DiagramSnapshots";
    private const string SqliteRaceMessage = """SQLite Error 1: 'table "DiagramSnapshots" already exists'""";
    private static readonly TimeSpan HostStartupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan AfterReleaseTimeout = TimeSpan.FromSeconds(60);

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
                    () => "waiting for both Demo.Api hosts to listen (lock held). Output:"
                          + Environment.NewLine + output);

                hosts.Should().OnlyContain(
                    h => !h.HasExited,
                    "a host that exited is not a pass; the named lock is supposed to make them "
                    + "wait for each other for milliseconds, so a crash after the barrier is the "
                    + "defect this holds. Output:" + Environment.NewLine + output);

                hosts.Should().OnlyContain(
                    h => h.Listening,
                    "both hosts must have come through EnsureCreated and reached Kestrel. One "
                    + "silent stall would look like 'they never met' dressed as a hang. Output:"
                    + Environment.NewLine + output);

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
                          && output.ToString().Contains(SqliteRaceMessage, StringComparison.Ordinal),
                    () => "waiting for the lock-bypassing arm to lose the race. If both hosts "
                          + "stay up, this arrangement never met and a green on the locked arm "
                          + "would be indistinguishable from that. Output:"
                          + Environment.NewLine + output);

                output.ToString().Should().Contain(
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

    private void RunTwoHosts(
        bool skipLock,
        Action<IReadOnlyList<DemoApiHost>, string, StringBuilder> onReleased)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "tempo-blazor-demo-api-schema-cross-process",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
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

            hosts.Add(StartHost(executable, databasePath, readyDir, goFile, skipLock, FreePort(), output));
            hosts.Add(StartHost(executable, databasePath, readyDir, goFile, skipLock, FreePort(), output));

            WaitUntil(
                HostStartupTimeout,
                () => Directory.GetFiles(readyDir).Length >= 2 || hosts.Any(h => h.HasExited),
                () => "waiting for both hosts to arrive at EnsureCreated and write a ready file. "
                      + "Without that rendezvous the first host can finish creating before the "
                      + "second opens the file. Output:" + Environment.NewLine + output);

            hosts.Should().OnlyContain(
                h => !h.HasExited,
                "a host that died before the barrier was released never entered the window this "
                + "test exists for. Output:" + Environment.NewLine + output);

            Directory.GetFiles(readyDir).Length.Should().Be(
                2,
                "both processes must have reached the test barrier inside "
                + "DemoDiagramSchema.EnsureCreated. Fewer ready files means they did not meet. "
                + "Output:" + Environment.NewLine + output);

            TableExists(databasePath, DiagramSnapshotsTable).Should().BeFalse(
                "the hosts have signalled ready and must NOT have created the schema yet — that "
                + "is the window. A table that exists before the barrier is released means the "
                + "rendezvous is a no-op and a later green is 'they never met'. Looked in: "
                + databasePath);

            var releaseAt = DateTime.UtcNow.AddMilliseconds(500);
            File.WriteAllText(
                goFile,
                releaseAt.Ticks.ToString(CultureInfo.InvariantCulture));
            onReleased(hosts, databasePath, output);
        }
        finally
        {
            _output.WriteLine(output.ToString());
            foreach (var host in hosts)
            {
                host.Dispose();
            }

            TryDeleteDirectory(directory);
        }
    }

    private static string DemoApiExecutablePath()
        => Path.Combine(
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "Tempo.Blazor.Demo.Api.exe" : "Tempo.Blazor.Demo.Api");

    private static DemoApiHost StartHost(
        string executable,
        string databasePath,
        string readyDir,
        string goFile,
        bool skipLock,
        int port,
        StringBuilder output)
    {
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
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["DOTNET_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture);
        start.Environment[DemoDatabaseRedirect.EnvironmentVariable] = databasePath;
        start.Environment[DemoDiagramSchema.TestReadyDirEnvironmentVariable] = readyDir;
        start.Environment[DemoDiagramSchema.TestGoFileEnvironmentVariable] = goFile;
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
                    host.MarkListening();
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

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
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

        public DemoApiHost(Process process, int port)
        {
            _process = process;
            Port = port;
        }

        public int Port { get; }

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

        public void MarkListening() => Listening = true;

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
