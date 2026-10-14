using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E smoke for the opt-in error announcement (<c>AnnounceError="false"</c>) on the Forms demo
/// page (<c>/forms</c>, WASM demo at 7106). The interactive card's Submit sets the error on both
/// fields and moves focus to the muted one — the precondition that makes a muted error safe
/// (WCAG 4.1.3): the muted field's message must NOT carry <c>role="alert"</c> (it is read once
/// through <c>aria-describedby</c> on the focused control), while the default field next to it
/// keeps the assertive announcement. <c>aria-describedby</c> and <c>aria-invalid</c> must survive
/// on both. Screenshots land in <c>__screenshots__/input-announce-error/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public class TmInputAnnounceErrorE2ETests : WasmTestBase
{
    private const string FormsPage = "/forms";
    private const string MutedInputId = "announce-demo-amount";
    private const string ActiveElementIsMutedInput = "document.activeElement && document.activeElement.id === 'announce-demo-amount'";

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    /// <summary>The error message div belonging to the wrapper around the given input.</summary>
    private static ILocator MessageOf(ILocator input) =>
        input.Locator("xpath=ancestor::div[contains(@class,'tm-input-wrapper')]/div[contains(@class,'tm-input-error-message')]");

    private async Task<IPage> OpenFormsPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{FormsPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var muted = page.Locator($"input[data-testid='{MutedInputId}']");
        await muted.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await muted.ScrollIntoViewIfNeededAsync();
        return page;
    }

    /// <summary>
    /// Clicks Submit and waits for the focus move that the handler performs right after the error
    /// re-render (OnAfterRenderAsync), so the assertions below cannot race the render.
    /// </summary>
    private static async Task SubmitAndAwaitFocusAsync(IPage page)
    {
        await page.Locator("[data-testid='announce-demo-submit']").ClickAsync();
        await page.WaitForFunctionAsync(ActiveElementIsMutedInput, null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    [TestMethod]
    [Description("Submit focuses the muted input; its error message has no role, aria-describedby points at it, aria-invalid stays")]
    public async Task Announce1_SubmitFocusesMutedInput_MutedErrorHasNoRole_KeepsDescriptionLink()
    {
        var page = await OpenFormsPageAsync();
        var mutedInput = page.Locator($"input[data-testid='{MutedInputId}']");
        var message = MessageOf(mutedInput);

        await SubmitAndAwaitFocusAsync(page);

        // The submit handler moved focus to the muted input — the precondition that makes the
        // muted announcement safe (without it the error would not be announced at all).
        var activeId = await page.EvaluateAsync<string?>(
            "() => document.activeElement ? document.activeElement.id : null");
        Assert.AreEqual(MutedInputId, activeId, "the submit handler must move focus to the muted input");

        await Expect(message).ToBeVisibleAsync();
        await Expect(message).ToContainTextAsync("Amount is required");

        // The whole point: no assertive live region on the muted message.
        Assert.IsNull(await message.GetAttributeAsync("role"),
            "AnnounceError=false must drop role=alert so focus-reading via aria-describedby does not double-announce");

        // The description link and invalid state survive — that is what reads the error on focus.
        var describedBy = await mutedInput.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrEmpty(describedBy), "aria-describedby must stay on the input");
        var messageId = await message.GetAttributeAsync("id");
        Assert.AreEqual(messageId, describedBy, "aria-describedby must point at the rendered error message");
        Assert.AreEqual("true", await mutedInput.GetAttributeAsync("aria-invalid"));
    }

    [TestMethod]
    [Description("Default field next to it keeps role=alert — the opt-out does not leak between instances")]
    public async Task Announce2_DefaultError_KeepsRoleAlert()
    {
        var page = await OpenFormsPageAsync();
        var defaultInput = page.Locator("input[data-testid='announce-demo-default']");
        var defaultMessage = MessageOf(defaultInput);

        await SubmitAndAwaitFocusAsync(page);

        await Expect(defaultMessage).ToBeVisibleAsync();
        await Expect(defaultMessage).ToContainTextAsync("Recipient is required");
        Assert.AreEqual("alert", await defaultMessage.GetAttributeAsync("role"),
            "the default (no AnnounceError) must keep the assertive announcement");

        var describedBy = await defaultInput.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrEmpty(describedBy));
        Assert.AreEqual(await defaultMessage.GetAttributeAsync("id"), describedBy);
        Assert.AreEqual("true", await defaultInput.GetAttributeAsync("aria-invalid"));
    }

    /// <summary>
    /// Visual evidence for UX review: the interactive card AFTER submit — focus sits on the muted
    /// field and both errors are visible. The rendering of the two messages is identical; only the
    /// announcement role differs (not visible — that is what Announce1/Announce2 assert).
    /// </summary>
    [TestMethod]
    [Description("Screenshot of the announce-error card after submit (focus + both errors visible) for UX review")]
    public async Task Announce3_Screenshot_AnnounceErrorCardAfterSubmit()
    {
        var page = await OpenFormsPageAsync();

        await SubmitAndAwaitFocusAsync(page);

        var card = page.Locator(".showcase-card")
            .Filter(new LocatorFilterOptions { Has = page.Locator($"[data-testid='{MutedInputId}']") });
        await card.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);

        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "input-announce-error");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "forms-announce-error-after-submit.png");
        await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });

        Assert.IsTrue(File.Exists(path), "the screenshot evidence must be written");
        TestContext.AddResultFile(path);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent!;
        }

        throw new InvalidOperationException("Repo root with TempoBlazor.slnx not found");
    }
}

/// <summary>
/// The same contract against the Blazor <b>Server</b> demo (7107): the interactive card behaves
/// identically on the circuit — submit moves focus to the muted input, whose message has no role
/// while the describedby link and <c>aria-invalid</c> stay.
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public class TmInputAnnounceErrorServerE2ETests : ServerTestBase
{
    [TestMethod]
    [Description("Server: submit focuses the muted input; its error message has no role and keeps the aria-describedby link")]
    public async Task Announce_Server_SubmitFocusesMutedInput_MutedErrorHasNoRole()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}/forms",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var mutedInput = page.Locator("input[data-testid='announce-demo-amount']");
        await mutedInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await mutedInput.ScrollIntoViewIfNeededAsync();

        await page.Locator("[data-testid='announce-demo-submit']").ClickAsync();
        await page.WaitForFunctionAsync(
            "document.activeElement && document.activeElement.id === 'announce-demo-amount'", null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var message = mutedInput.Locator("xpath=ancestor::div[contains(@class,'tm-input-wrapper')]/div[contains(@class,'tm-input-error-message')]");

        await Assertions.Expect(message).ToBeVisibleAsync();
        Assert.IsNull(await message.GetAttributeAsync("role"),
            "AnnounceError=false must drop role=alert on Server too");

        var describedBy = await mutedInput.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrEmpty(describedBy), "aria-describedby must stay on the input");
        Assert.AreEqual(await message.GetAttributeAsync("id"), describedBy);
        Assert.AreEqual("true", await mutedInput.GetAttributeAsync("aria-invalid"));
    }
}
