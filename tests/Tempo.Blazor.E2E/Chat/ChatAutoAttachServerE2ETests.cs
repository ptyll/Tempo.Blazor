using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Chat;

/// <summary>
/// FZE-12 on the SERVER lane (https://localhost:7107): the same auto-attach contract over a
/// Blazor Server circuit — Enter sends and clears, the focus returns to the auto-attached
/// textarea and Shift+Enter stays a new line. The IME/composition guard is identical JS in both
/// lanes (the <c>chat.test.mjs</c> node lane owns the unit contract and the WASM spec above
/// runs the synthetic-keyboard emulation), so the Server mirror asserts the submit paths only.
/// </summary>
[TestClass]
public class ChatAutoAttachServerE2ETests : ServerTestBase
{
    private const string AutoAttachSection = "[data-testid='chat-autoattach-section']";
    private const string AutoAttachInput = "[data-testid='chat-autoattach-section'] .chat-demo-gate-composer__input";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1100 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        page.PageError += (_, e) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + e); };
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && m.Text.Contains("Unhandled exception"))
                lock (_clientErrors) _clientErrors.Add("CONSOLE: " + m.Text);
        };
        await page.GotoAsync($"{BaseUrl}/chat", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var section = page.Locator(AutoAttachSection);
        await section.ScrollIntoViewIfNeededAsync();
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.WaitForFunctionAsync(
            @"sel => { const s = document.querySelector(sel);
                return !!s && !!s.querySelector('.tm-chat__messages .tm-chat__item') &&
                    s.querySelector('.tm-chat__messages').scrollHeight -
                    s.querySelector('.tm-chat__messages').scrollTop -
                    s.querySelector('.tm-chat__messages').clientHeight < 40; }",
            AutoAttachSection,
            new PageWaitForFunctionOptions { Timeout = 30000, PollingInterval = 200 });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    [TestMethod]
    [TestCategory("Server")]
    public async Task AutoAttachComposer_Server_EnterSends_ClearsAndRefocuses()
    {
        var page = await OpenAsync();
        var input = page.Locator(AutoAttachInput);

        await input.ClickAsync();
        await input.FillAsync("Server-circuit auto-attach send");
        await input.PressAsync("Enter");

        await Assertions.Expect(page.Locator($"{AutoAttachSection} .tm-chat__item",
                new PageLocatorOptions { HasText = "Server-circuit auto-attach send" }))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(input)
            .ToHaveValueAsync(string.Empty, new LocatorAssertionsToHaveValueOptions { Timeout = 10000 });
        await Assertions.Expect(input)
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10000 });

        AssertNoClientErrors();
    }

    [TestMethod]
    [TestCategory("Server")]
    public async Task AutoAttachComposer_Server_ShiftEnterStaysNewLine()
    {
        var page = await OpenAsync();
        var input = page.Locator(AutoAttachInput);

        await input.ClickAsync();
        await input.PressSequentiallyAsync("server line one");
        await input.PressAsync("Shift+Enter");
        await input.PressSequentiallyAsync("server line two");

        Assert.IsTrue((await input.InputValueAsync()).Contains('\n'),
            "Shift+Enter must insert a new line into the auto-attached composer on the Server lane");
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(page.Locator($"{AutoAttachSection} .tm-chat__item",
                new PageLocatorOptions { HasText = "server line one" }))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 3000 });

        AssertNoClientErrors();
    }
}
