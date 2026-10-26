using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Chat;

/// <summary>
/// E2E for TmChat K6 on the Chat demo (WASM @ 7106): a single shared conversation shown to two users
/// (Alice + Bob panes). Exercises threaded replies, emoji reactions, per-user read receipts and edit,
/// asserting each interaction crosses to the other user's pane. Screenshots land in
/// <c>__screenshots__/chat/</c> for UX review.
/// </summary>
[TestClass]
public class ChatCollaborationE2ETests : WasmTestBase
{
    private const string ChatPage = "/chat";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        page.PageError += (_, e) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + e); };
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && m.Text.Contains("Unhandled exception"))
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + m.Text);
        };
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{ChatPage}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator("[data-testid='chat-collab-section']");
        await section.ScrollIntoViewIfNeededAsync();
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private static ILocator Pane(IPage page, string who) => page.Locator($"[data-testid='chat-pane-{who}']");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Chat_Thread_Reactions_Receipts_CrossBetweenTwoUsers()
    {
        var page = await OpenAsync();
        var alice = Pane(page, "alice");
        var bob = Pane(page, "bob");

        // ── Receipts: Bob's pane auto-reads Alice's message c2 → Alice sees a receipt on it ──
        await Assertions.Expect(alice.Locator("[data-testid='chat-receipts-c2']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        // ── Thread: Alice replies to Bob's message c1 in a thread ──
        await alice.Locator("[data-message-id='c1'] .tm-chat__bubble").First.HoverAsync();
        await alice.Locator("[data-testid='chat-reply-c1']").ClickAsync();
        await alice.Locator("[data-testid='chat-thread-input']").FillAsync("Let's do it after lunch.");
        await alice.Locator("[data-testid='chat-thread-send']").ClickAsync();

        // Bob's pane shows the reply-count badge on the same root message.
        await Assertions.Expect(bob.Locator("[data-testid='chat-thread-open-c1']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        await Assertions.Expect(bob.Locator("[data-testid='chat-thread-open-c1']"))
            .ToContainTextAsync("1", new LocatorAssertionsToContainTextOptions { Timeout = 5000 });

        // ── Reaction: Bob reacts to Alice's message c2 → Alice sees the chip ──
        await bob.Locator("[data-message-id='c2'] .tm-chat__bubble").First.HoverAsync();
        await bob.Locator("[data-testid='chat-react-c2']").ClickAsync();
        await bob.Locator("[data-testid='chat-emoji-c2-0']").ClickAsync();

        await Assertions.Expect(alice.Locator("[data-testid='chat-reaction-c2']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        await SaveScreenshotAsync(page, "chat-collab");
        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Chat_EditOwnMessage_ShowsEditedMarkerForBothUsers()
    {
        var page = await OpenAsync();
        var alice = Pane(page, "alice");
        var bob = Pane(page, "bob");

        // Alice edits her own message c2.
        await alice.Locator("[data-message-id='c2'] .tm-chat__bubble").First.HoverAsync();
        await alice.Locator("[data-testid='chat-edit-c2']").ClickAsync();
        await alice.Locator("[data-testid='chat-edit-input']").FillAsync("Yes — threads, reactions and receipts all shipped.");
        await alice.Locator("[data-testid='chat-edit-save']").ClickAsync();

        // The "(edited)" marker appears in Bob's pane too (shared store).
        await Assertions.Expect(bob.Locator("[data-testid='chat-edited-c2']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        await SaveScreenshotAsync(page, "chat-edited");
        AssertNoClientErrors();
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "chat");
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
