using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Notifications;

/// <summary>
/// E2E for K5 on the notifications demo (WASM @ 7106, API @ 5100): real-time SignalR push between
/// two windows, Web Push subscribe + server send, and the daily digest e-mail landing in smtp4dev.
/// Screenshots land in <c>__screenshots__/notifications/</c> for UX review.
/// </summary>
[TestClass]
public class NotificationsE2ETests : WasmTestBase
{
    private const string Page = "/notifications";
    private const string Smtp4DevBase = "http://localhost:5000";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private async Task<IPage> OpenAsync(bool grantNotifications = false)
    {
        var context = await CreateContextAsync();
        if (grantNotifications)
        {
            await context.GrantPermissionsAsync(["notifications"], new BrowserContextGrantPermissionsOptions { Origin = BaseUrl });
        }
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 900);
        await page.GotoAsync($"{BaseUrl}{Page}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        // Wait until the page's SignalR service reports connected.
        await Assertions.Expect(page.GetByTestId("notif-status")).ToContainTextAsync("Connected",
            new LocatorAssertionsToContainTextOptions { Timeout = 30000 });
        return page;
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Notifications_Realtime_BellUpdatesInSecondWindow()
    {
        var windowA = await OpenAsync();
        var windowB = await OpenAsync();

        // Zero the shared unread state so we can assert a clean increment.
        await windowB.GetByTestId("notif-mark-read").ClickAsync();
        await Assertions.Expect(windowB.GetByTestId("notif-bell-count")).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        // Publish in window A → window B's bell must light up within ~2s (real-time push).
        await windowA.GetByTestId("notif-send").ClickAsync();
        await Assertions.Expect(windowB.GetByTestId("notif-bell-count")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 2500 });
        await Assertions.Expect(windowB.GetByTestId("notif-bell-count")).ToHaveTextAsync("1",
            new LocatorAssertionsToHaveTextOptions { Timeout = 2500 });

        await SaveScreenshotAsync(windowA, "realtime-window-a");
        await SaveScreenshotAsync(windowB, "realtime-window-b");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Notifications_WebPush_SubscribeAndTestSend()
    {
        // Clear stale subscriptions first: the API's in-memory push store outlives
        // browser contexts, so subscriptions from earlier runs would otherwise keep
        // inflating the attempted counter the assertion below pins to exactly 1.
        using (var http = new HttpClient())
        {
            try { await http.PostAsync("https://localhost:5100/api/notifications/push/reset", null); }
            catch { /* API may not be running; ignore */ }
        }

        var page = await OpenAsync(grantNotifications: true);

        await page.GetByTestId("notif-subscribe").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notif-push-state")).ToContainTextAsync("subscribed",
            new LocatorAssertionsToContainTextOptions { Timeout = 20000 });

        // Server-side push send is invoked for the stored subscription.
        await page.GetByTestId("notif-test-push").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notif-push-attempted")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        var attemptedText = await page.GetByTestId("notif-push-attempted").InnerTextAsync();
        StringAssert.Contains(attemptedText, "1");

        await SaveScreenshotAsync(page, "webpush-subscribed");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Notifications_Digest_EmailDeliveredToSmtp4Dev()
    {
        var available = await EnsureSmtp4DevAsync();
        if (!available)
        {
            Assert.Inconclusive("smtp4dev is not available (start it locally on Windows or via docker on Linux).");
        }

        var page = await OpenAsync();

        // Publish an unread notification, then run the digest immediately.
        await page.GetByTestId("notif-send").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notif-bell-count")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        await page.GetByTestId("notif-digest").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notif-digest-sent")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // The digest e-mail lands in smtp4dev.
        var message = await PollSmtp4DevAsync("notification digest", TimeSpan.FromSeconds(20));
        Assert.IsNotNull(message, "Digest e-mail did not arrive in smtp4dev.");
        StringAssert.Contains(message!.Value.subject, "digest");

        await SaveScreenshotAsync(page, "digest-triggered");
    }

    // ── smtp4dev (cross-platform: local dotnet on Windows, docker elsewhere) ──

    private static async Task<bool> EnsureSmtp4DevAsync()
    {
        if (await ProbeSmtp4DevAsync()) return true;

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

        // Wait for it to come up (first run may build).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (await ProbeSmtp4DevAsync()) return true;
            await Task.Delay(1000);
        }
        return false;
    }

    private static async Task<bool> ProbeSmtp4DevAsync()
    {
        try
        {
            var resp = await Http.GetAsync($"{Smtp4DevBase}/api/Messages?pageSize=1");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(string subject, IReadOnlyList<string> to)?> PollSmtp4DevAsync(string searchTerm, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(
                    $"{Smtp4DevBase}/api/Messages?searchTerms={Uri.EscapeDataString(searchTerm)}&pageSize=50"));
                foreach (var result in doc.RootElement.GetProperty("results").EnumerateArray())
                {
                    var subject = result.TryGetProperty("subject", out var s) ? s.GetString() ?? "" : "";
                    var to = result.TryGetProperty("to", out var toEl) && toEl.ValueKind == JsonValueKind.Array
                        ? toEl.EnumerateArray().Select(t => t.GetString() ?? "").ToList()
                        : [];
                    return (subject, to);
                }
            }
            catch { /* transient */ }
            await Task.Delay(500);
        }
        return null;
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

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "notifications");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx"))) return directory;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
