using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared smtp4dev harness for every lane that proves an e-mail actually arrived (the
/// Notifications K5 digest spec and the email-template E11.8 send flow). One copy of the
/// cross-platform startup (docker everywhere, the local dotnet project on Windows), one
/// probe and one GUARD.
/// <para>
/// The guard honours <c>TM_E2E_REQUIRE_SMTP4DEV=1</c>: a release-evidence run sets it so a
/// missing smtp4dev FAILS the spec instead of quietly skipping it (Assert.Inconclusive stays
/// the developer-machine default — a Docker Desktop restart leaves the container exited and
/// a red result there says nothing about the product).
/// </para>
/// </summary>
internal static class Smtp4DevHelper
{
    /// <summary>The shared local smtp4dev base (UI + REST API).</summary>
    public const string BaseUrl = "http://localhost:5000";

    /// <summary>One client for the whole harness: smtp4dev answers on localhost, so a hang
    /// means the instance is gone — 5 s keeps the probe from eating the test timeout.</summary>
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>True when the shared smtp4dev answers within its 5 s client timeout.</summary>
    public static async Task<bool> ProbeAsync()
    {
        try
        {
            var resp = await Http.GetAsync($"{BaseUrl}/api/Messages?pageSize=1");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Probes smtp4dev and, when the instance is down, TRIES to start it (docker everywhere,
    /// the local dotnet project on Windows) and waits up to 120 s for the first run to build
    /// and answer. Returns false only when nothing could be started or the instance stayed
    /// silent.
    /// </summary>
    public static async Task<bool> EnsureAsync()
    {
        if (await ProbeAsync()) return true;

        try
        {
            if (HasCommand("docker"))
            {
                Start("docker", "run -d --rm --name tempo-smtp4dev -p 5000:80 -p 2525:25 rnwood/smtp4dev");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && Directory.Exists(@"C:\Work\smtp4dev-master\Rnwood.Smtp4dev"))
            {
                var psi = new ProcessStartInfo("dotnet",
                    "run -c Release --project \"C:\\Work\\smtp4dev-master\\Rnwood.Smtp4dev\" --urls http://localhost:5000")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.Environment["ServerOptions__Port"] = "2525";
                Process.Start(psi);
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (await ProbeAsync()) return true;
            await Task.Delay(1000);
        }
        return false;
    }

    /// <summary>
    /// The lane guard: starts smtp4dev when it is down, then decides the outcome. With
    /// <c>TM_E2E_REQUIRE_SMTP4DEV=1</c> a missing instance FAILS the spec (release-evidence
    /// runs must not silently skip a delivery proof); without it the spec is Inconclusive on
    /// a machine that simply has no smtp4dev.
    /// </summary>
    public static async Task EnsureOrAssertAsync(string lane)
    {
        if (await EnsureAsync()) return;
        if (string.Equals(Environment.GetEnvironmentVariable("TM_E2E_REQUIRE_SMTP4DEV"), "1", StringComparison.Ordinal))
        {
            Assert.Fail(
                $"smtp4dev did not come up on {BaseUrl} but TM_E2E_REQUIRE_SMTP4DEV=1 requires it " +
                $"(lane: {lane}) — start it (docker run -d --rm --name tempo-smtp4dev -p 5000:80 -p 2525:25 rnwood/smtp4dev) and re-run.");
        }

        Assert.Inconclusive("smtp4dev is not available (start it locally on Windows or via docker on Linux).");
    }

    private static bool HasCommand(string command)
    {
        try
        {
            var psi = new ProcessStartInfo(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which", command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void Start(string file, string args)
        => Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true });
}
