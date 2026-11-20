using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Self-hosted demo host processes (start, probe, resurrect, restart records) of <see cref="PlaywrightTestBase"/>.
/// </summary>
public abstract partial class PlaywrightTestBase
{
    private static readonly SemaphoreSlim HostLock = new(1, 1);
    private static readonly List<DemoHostProcess> DemoHostProcesses = [];
    private static bool _demoHostsInitialized;

    /// <summary>
    /// Scratch directory the demo hosts write their mutable state into, so a test run leaves the
    /// working tree alone. Created once per run and deliberately NOT deleted — a failed run's SQLite
    /// file is worth keeping, and the OS temp directory is where it belongs either way.
    /// </summary>
    private static readonly Lazy<string> DemoDataDirectory = new(() =>
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "tempo-blazor-e2e",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        return directory;
    });

    /// <summary>
    /// Re-probes (and, when needed, resurrects) the self-hosted demo hosts from an IN-FLIGHT
    /// failure. ClassInitialize already resurrects a dead host at every class boundary, but a test
    /// whose host died MID-TEST used to fail with <c>net::ERR_CONNECTION_REFUSED</c> and the
    /// resurrection waited for the next class — the casualty was avoidable. The cold-boot recovery
    /// in <see cref="GotoAppReadyAsync"/> calls this before its one reload, so a host that died
    /// while the app booted comes back for the SAME test. H1: the INSTANCE <see cref="TestContext"/>
    /// is forwarded, so the restart record names the test that triggered the recovery and its
    /// diagnostics land in that test's log instead of nowhere.
    /// </summary>
    internal Task RecoverDemoHostsAsync() => EnsureDemoHostsAsync(TestContext);

    private static async Task EnsureDemoHostsAsync(TestContext? context)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TM_E2E_SELF_HOST"), "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await HostLock.WaitAsync();
        try
        {
            // No "already initialized" fast path here on purpose: every class init re-probes the
            // URLs (cheap when healthy — a refused/wedged host is answered in milliseconds) so a
            // demo host that dies mid-run is resurrected instead of failing every later test with
            // ERR_CONNECTION_REFUSED.
            var repoRoot = FindRepositoryRoot();
            await EnsureHostAsync(
                context,
                "Demo API",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo.Api", "Tempo.Blazor.Demo.Api.csproj"),
                "Tempo.Blazor.Demo.Api",
                ["https://localhost:5100"],
                TimeSpan.FromSeconds(120));

            await EnsureHostAsync(
                context,
                "Demo WASM",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo", "Tempo.Blazor.Demo.csproj"),
                "https",
                ["https://localhost:7106", "http://localhost:5010"],
                TimeSpan.FromSeconds(180));

            // Server and InteractiveAuto demo hosts: the ServerTestBase (:7107) and
            // InteractiveAutoTestBase (:7108) lanes previously assumed a developer had these running
            // by hand, so a bare `dotnet test` run failed every one of their tests in CreatePageAsync.
            // Self-host them the same way as the two lanes above.
            await EnsureHostAsync(
                context,
                "Demo Server",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo.Server", "Tempo.Blazor.Demo.Server.csproj"),
                "Tempo.Blazor.Demo.Server",
                ["https://localhost:7107"],
                TimeSpan.FromSeconds(120));

            await EnsureHostAsync(
                context,
                "Demo InteractiveAuto",
                Path.Combine(repoRoot, "src", "Tempo.Blazor.Demo.InteractiveAuto", "Tempo.Blazor.Demo.InteractiveAuto", "Tempo.Blazor.Demo.InteractiveAuto.csproj"),
                "Tempo.Blazor.Demo.InteractiveAuto",
                ["https://localhost:7108"],
                TimeSpan.FromSeconds(120));

            _demoHostsInitialized = true;
        }
        finally
        {
            HostLock.Release();
        }
    }

    private static async Task EnsureHostAsync(
        TestContext? context,
        string name,
        string projectPath,
        string launchProfile,
        IReadOnlyList<string> urls,
        TimeSpan timeout)
    {
        if (await AllUrlsReachableAsync(urls))
        {
            return;
        }

        var existing = DemoHostProcesses.FirstOrDefault(host => host.Name == name);
        if (existing is { HasExited: false })
        {
            if (DateTimeOffset.UtcNow - existing.StartedAtUtc <= timeout)
            {
                // Still inside its readiness window (slow boot under load) — wait on the same
                // process rather than stack a second `dotnet run` on the same port.
                await WaitForHostReadyAsync(context, existing, urls, timeout);
                return;
            }

            // Alive past its readiness window and not serving — PROBABLY wedged. H3: one failed
            // probe is also the signature of a host that answers just after the probe timed out
            // under full-lane load, so the kill needs THREE consecutive failed probes spread over
            // ~6 s. Any healthy probe leaves the host in place.
            if (!await IsHostStuckAcrossConsecutiveProbesAsync(urls))
            {
                return;
            }

            // Wedged: alive past its readiness window, three probes in a row saw nothing. Replace it.
            DemoHostProcesses.Remove(existing);
            var wedgedOutput = existing.RecentOutput;
            existing.Dispose();
            RecordHostRestart(context, name, "unreachable-past-window", wedgedOutput);
            context?.WriteLine($"{name} was unreachable past its readiness window (3 consecutive failed probes) — restarting.");
        }
        else if (existing is not null)
        {
            // The process we started died mid-run — resurrect it. Throttle back-to-back restarts
            // so a permanently-broken host fails fast instead of paying the full startup per class.
            var sinceLastStart = DateTimeOffset.UtcNow - existing.StartedAtUtc;
            DemoHostProcesses.Remove(existing);
            var recentOutput = existing.RecentOutput;
            // B2: the exit code is captured INSIDE Dispose() before the Process handle goes away,
            // so reading it after Dispose is safe (the disposed Process object throws).
            existing.Dispose();
            if (sinceLastStart < TimeSpan.FromSeconds(30))
            {
                throw new InvalidOperationException($"{name} exited before it became ready. Recent output:{Environment.NewLine}{recentOutput}");
            }

            RecordHostRestart(context, name, "died-mid-run", recentOutput, existing.ExitCode);
            context?.WriteLine($"{name} died mid-run — restarting it.");
        }
        else if (_demoHostsInitialized)
        {
            // The hosts were healthy at first init but this one is unreachable now, and no tracked
            // process exists — an externally-started host died. Adopt it: self-host a replacement
            // instead of letting every remaining test fail with connection refused.
            RecordHostRestart(context, name, "externally-died", "");
            context?.WriteLine($"{name} became unreachable mid-run — self-hosting a replacement.");
        }

        var process = StartDemoHostProcess(name, projectPath, launchProfile);
        DemoHostProcesses.Add(process);
        await WaitForHostReadyAsync(context, process, urls, timeout);
    }

    private static async Task WaitForHostReadyAsync(
        TestContext? context,
        DemoHostProcess process,
        IReadOnlyList<string> urls,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"{process} exited before it became ready. Recent output:{Environment.NewLine}{process.RecentOutput}");
            }

            if (await AllUrlsReachableAsync(urls))
            {
                context?.WriteLine($"{process} ready at {string.Join(", ", urls)}.");
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"{process} did not become ready at {string.Join(", ", urls)} within {timeout.TotalSeconds:n0}s. Recent output:{Environment.NewLine}{process.RecentOutput}");
    }

    private static DemoHostProcess StartDemoHostProcess(string name, string projectPath, string launchProfile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--launch-profile");
        startInfo.ArgumentList.Add(launchProfile);
        // The run build must not fail on the repo-wide NuGet audit warning-as-error (NU1902, AngleSharp
        // via bUnit/AngleSharp) or other warnings-as-errors — we only need the host to launch. Without
        // these the host dies during build and every dependent lane fails in ClassInitialize with
        // "…exited before it became ready". Mirrors ReportServerE2ETestBase.StartHostProcess.
        startInfo.ArgumentList.Add("--property:NuGetAudit=false");
        startInfo.ArgumentList.Add("--property:TreatWarningsAsErrors=false");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";

        // The demo API keeps its diagrams in a SQLite file that is COMMITTED next to the project, so
        // booting it from a test run left `diagrams.db` modified and a -wal/-shm pair untracked in the
        // working tree. Point it at a per-run temp file instead; the demo API is the only host that
        // reads this key, and it is harmless on the others.
        startInfo.Environment["Demo__DiagramsDbPath"] =
            Path.Combine(DemoDataDirectory.Value, "diagrams.db");

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var host = new DemoHostProcess(name, process);
        process.OutputDataReceived += (_, args) => host.AddOutput(args.Data);
        process.ErrorDataReceived += (_, args) => host.AddOutput(args.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start {name}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return host;
    }

    private static async Task<bool> AllUrlsReachableAsync(IReadOnlyList<string> urls)
    {
        foreach (var url in urls)
        {
            if (!await IsUrlReachableAsync(url))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// H3: whether a host is REALLY wedged — THREE consecutive failed probes spread over ~6 s.
    /// The old single-probe verdict killed hosts that were alive and answered a moment after the
    /// one probe timed out (a full-lane load spikes every probe at once), discarding a process
    /// that was a second away from serving. Any healthy probe in the window answers "not stuck".
    /// </summary>
    internal static async Task<bool> IsHostStuckAcrossConsecutiveProbesAsync(IReadOnlyList<string> urls)
    {
        const int requiredFailures = 3;
        for (var probe = 1; probe <= requiredFailures; probe++)
        {
            if (await AllUrlsReachableAsync(urls))
            {
                return false;
            }

            if (probe < requiredFailures)
            {
                // Probes at ~0 s / ~3 s / ~6 s: the window rides out one slow probe round without
                // stretching the wedged-host replacement beyond a few seconds.
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        return true;
    }

    /// <summary>
    /// One pooled probe client for the whole class (N193): the previous per-call
    /// <c>new HttpClient(handler)</c> churned a socket per 500 ms readiness poll across eight
    /// shards. The handler accepts untrusted certificates on loopback addresses only — every demo
    /// host is a self-signed localhost dev cert (see <see cref="DemoApiTestHttp"/>).
    /// </summary>
    private static readonly HttpClient ProbeClient = new(DemoApiTestHttp.CreateHandler())
    { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>
    /// Records a host resurrection to <c>TestResults/host-restarts.jsonl</c> — the machine-readable
    /// half of N209. The restart itself stays (the suite should still try to finish); what ends is
    /// its invisibility: the release-evidence run reads <see cref="HostRestartLog.TotalHostRestarts"/>
    /// and refuses to report a run with hidden server crashes as a clean green.
    /// </summary>
    private static void RecordHostRestart(TestContext? context, string name, string reason, string recentOutput, int? exitCode = null)
    {
        try
        {
            HostRestartLog.AppendRestartRecord(
                Path.Combine(FindRepositoryRoot(), "TestResults", "host-restarts.jsonl"),
                name,
                reason,
                recentOutput,
                exitCode,
                testName: context?.TestName);
        }
        catch
        {
            // The record is evidence, not the mechanism: a disk problem must not stop the restart.
        }
    }

    /// <summary>
    /// H4: records a shared-browser relaunch to <c>TestResults/browser-relaunches.jsonl</c> and
    /// <see cref="HostRestartLog.TotalBrowserRelaunches"/> — the browser twin of
    /// <see cref="RecordHostRestart"/>, so the lane summary can show how often the run had to
    /// resurrect the wedge-prone shared Chromium. Evidence, not the mechanism: a failed append
    /// never fails the relaunch.
    /// </summary>
    private static void RecordBrowserRelaunch(string reason, string? testName)
    {
        try
        {
            HostRestartLog.AppendBrowserRelaunchRecord(
                Path.Combine(FindRepositoryRoot(), "TestResults", "browser-relaunches.jsonl"),
                reason,
                testName);
        }
        catch
        {
            // The record is evidence, not the mechanism.
        }
    }

    /// <summary>
    /// The readiness verdict over a /health response (N193): 2xx exactly. The previous predicate
    /// — <c>status != ServiceUnavailable</c> — counted a crashed app answering 500 (or a
    /// misrouted 404) as reachable, so a dead host passed its own readiness gate.
    /// </summary>
    internal static bool IsReadyStatusCode(HttpStatusCode statusCode) =>
        (int)statusCode is >= 200 and <= 299;

    private static async Task<bool> IsUrlReachableAsync(string url)
    {
        try
        {
            // N193: probe the dedicated readiness endpoint, not the root URL — a crashed app
            // answering 500 on "/" used to read as "reachable" because only 503 counted as down.
            // /health must answer 2xx exactly. For the standalone WASM dev server there is no
            // endpoint to add: its SPA fallback serves index.html for extensionless paths, so
            // /health answers 200 as soon as the host serves at all — precisely the signal this
            // probe asks for.
            using var response = await ProbeClient.GetAsync($"{url.TrimEnd('/')}/health");
            return IsReadyStatusCode(response.StatusCode);
        }
        catch
        {
            return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    /// <summary>
    /// The tracked demo host process. Internal (nested) so the harness unit tests can exercise the
    /// exit-code capture (B2) without standing a demo host up.
    /// </summary>
    internal sealed class DemoHostProcess : IDisposable
    {
        private readonly string _name;
        private readonly Process _process;
        private readonly ConcurrentQueue<string> _output = new();
        // B2: the exit code is captured ONCE — inside Dispose(), BEFORE _process.Dispose(). A
        // disposed Process throws InvalidOperationException from HasExited/ExitCode, so the
        // died-mid-run restart record that reads ExitCode after Dispose used to crash instead of
        // recording the evidence.
        private int? _exitCode;
        private bool _disposed;

        public DemoHostProcess(string name, Process process)
        {
            _name = name;
            _process = process;
            StartedAtUtc = DateTimeOffset.UtcNow;
        }

        public string Name => _name;

        /// <summary>When this process was launched — used to throttle resurrection attempts.</summary>
        public DateTimeOffset StartedAtUtc { get; }

        public bool HasExited => _process.HasExited;

        /// <summary>
        /// The dead process's exit code, or null while it is alive / not yet reaped. Recorded with a
        /// died-mid-run restart so the JSONL line can tell a crash (a Win32 fault code) from an
        /// external hard kill (taskkill leaves exit code 1 and an empty output tail). Safe to read
        /// after <see cref="Dispose"/>: the value rides the field captured before the handle died,
        /// and a disposed process answers null instead of throwing.
        /// </summary>
        public int? ExitCode => _exitCode ?? (!_disposed && _process.HasExited ? _process.ExitCode : null);

        public string RecentOutput => string.Join(Environment.NewLine, _output.ToArray());

        public void AddOutput(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            _output.Enqueue(line);
            while (_output.Count > 80 && _output.TryDequeue(out _))
            {
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(5000);
                }

                // B2: capture BEFORE _process.Dispose() — after it the Process object is dead and
                // every member read throws InvalidOperationException.
                _exitCode ??= _process.HasExited ? _process.ExitCode : null;
            }
            catch
            {
                // Best-effort cleanup after the test run.
            }
            finally
            {
                _disposed = true;
                _process.Dispose();
            }
        }

        public override string ToString() => _name;
    }
}
