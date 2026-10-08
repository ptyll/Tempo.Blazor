using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Review round 6, against real hosts: opening a Prompt dialog with Enter must not submit it.
/// The opener activates on its Enter keydown, the per-type initial focus lands on the input while
/// the key is still down, and the trailing keyup then landed on the input — the old keyup submit
/// fired the DefaultValue at once, so a keyboard user never saw the prompt (M-r6-1). After the
/// fix the submit happens on the input's Enter keydown (guarded against auto-repeat), and the
/// keyup of the opening press is spent.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayRound6E2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "overlay-round6");

    private async Task<IPage> OpenAsync(int width, int height = 1000)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = width < 1024,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        return page;
    }

    private async Task GotoAsync(IPage page, string path)
    {
        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        // FullPage resets touch emulation; a viewport shot keeps (pointer: coarse) honest.
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 60_000,
        });
        return path;
    }

    private static async Task<string> ActiveElementAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(
            """
            () => {
                const a = document.activeElement;
                if (!a) return '(null)';
                return a.tagName.toLowerCase() + '.' + (a.className || '').toString().trim().split(/\s+/).join('.');
            }
            """);
    }

    [TestMethod]
    [DataRow(390)]
    [DataRow(1440)]
    public async Task PromptDialog_EnterOnTheOpener_StaysOpenWithTheDefaultValue_ThenTypedEnterSubmits(int width)
    {
        var page = await OpenAsync(width);
        await GotoAsync(page, "/modal-dialog");

        // Open the way a keyboard user does: focus the opener and press Enter. One press must
        // activate exactly once — opening the dialog, not opening AND submitting "John Doe".
        var opener = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Enter Name" });
        await opener.ScrollIntoViewIfNeededAsync();
        await opener.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.tagName === 'INPUT' && !!document.activeElement.closest('.tm-dialog-content')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        // The trailing keyup needs time to (wrongly) submit if the defect were still there.
        await page.WaitForTimeoutAsync(400);

        var open = await page.Locator(".tm-modal-overlay").IsVisibleAsync();
        var active = await ActiveElementAsync(page);
        var value = await page.Locator("input.tm-dialog-input").InputValueAsync();
        var banners = await page.GetByText("You entered:").CountAsync();
        TestContext.WriteLine($"prompt {width}: open={open} focus={active} value='{value}' banners={banners}");

        Assert.IsTrue(open, $"the dialog must stay open after the opener's Enter at {width}px");
        Assert.IsTrue(active.Contains("tm-dialog-input"), $"focus must rest on the prompt input, got '{active}'");
        Assert.AreEqual("John Doe", value, "the untouched DefaultValue must still sit in the input");
        Assert.AreEqual(0, banners, "the keyup of the opening press must not submit the DefaultValue");

        await ShootAsync(page, $"{width}-prompt-kbd-open-holds-value");

        // The user edits the value and confirms with Enter — exactly one submit of the typed text.
        await page.Keyboard.PressAsync("Control+a");
        await page.Keyboard.TypeAsync("Pavel Novák");
        await page.Keyboard.PressAsync("Enter");
        await page.Locator(".tm-modal-overlay").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

        var banner = page.GetByText("You entered:").First;
        await banner.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(banner).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(page.GetByText("Pavel Novák").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        await ShootAsync(page, $"{width}-prompt-kbd-typed-enter-submits");
    }

    [TestMethod]
    public async Task PromptInput_FocusRingIsNotClipped_ByTheScroller()
    {
        // m-r6-1: the input opens focused (per-type initial focus) and its 4px focus ring was cut
        // by the scrolling content container — the wrapper now carries one spacing step of padding.
        var page = await OpenAsync(390);
        await GotoAsync(page, "/modal-dialog");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Enter Name" }).ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.classList.contains('tm-dialog-input')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var report = await page.EvaluateAsync<string>(
            """
            () => {
                const wrapper = document.querySelector('.tm-dialog-input-wrapper');
                const input = document.querySelector('.tm-dialog-input');
                const wc = wrapper.getBoundingClientRect(), ic = input.getBoundingClientRect();
                const cs = getComputedStyle(wrapper);
                return JSON.stringify({
                    padding: cs.padding,
                    // the ring (2px surface + 2px primary box-shadow) must fit inside the wrapper
                    ringRoomLeft: +(ic.left - wc.left).toFixed(1),
                    ringRoomRight: +(wc.right - ic.right).toFixed(1),
                });
            }
            """);
        TestContext.WriteLine($"prompt ring {report}");
        using (var doc = JsonDocument.Parse(report))
        {
            Assert.AreEqual("4px", doc.RootElement.GetProperty("padding").GetString(),
                "the wrapper must reserve one --tm-space-1 step so the focus ring is not clipped");
            Assert.IsTrue(doc.RootElement.GetProperty("ringRoomLeft").GetDouble() >= 4,
                $"the ring needs >=4px of room on each side: {report}");
            Assert.IsTrue(doc.RootElement.GetProperty("ringRoomRight").GetDouble() >= 4,
                $"the ring needs >=4px of room on each side: {report}");
        }

        await ShootAsync(page, "390-prompt-input-ring");
        await page.Keyboard.PressAsync("Escape");
    }

    [TestMethod]
    [DataRow(320)]
    [DataRow(390)]
    public async Task DialogSheetFooter_36CharLabelsFit_LikeTheModalSheet(int width)
    {
        // n-r6-1: the dialog sheet footer had 24px side padding where the modal sheet has 16px,
        // which cut 36-char FR/CS labels in the dialog that fit in the modal at the same width.
        // The contract is parity: the dialog's per-label room must equal the modal's, and wherever
        // the modal does not truncate the dialog must not either.
        var page = await OpenAsync(width, 844);
        await GotoAsync(page, "/modal-dialog");

        const string fr36 = "Supprimer définitivement le document";
        const string cs36 = "Odstranit trvale včetně všech příloh";

        await page.GetByTestId("open-long-dialog").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(700);
        await page.EvaluateAsync(
            """
            (labels) => {
                const els = document.querySelectorAll('.tm-modal-overlay .tm-dialog-footer .tm-btn .tm-btn-label');
                els[0].textContent = labels[0];
                els[1].textContent = labels[1];
            }
            """, new[] { fr36, cs36 });
        await page.WaitForTimeoutAsync(300);

        var dialog = await MeasureSheetFooterAsync(page, ".tm-dialog-footer", isDialog: true);
        TestContext.WriteLine($"dialog sheet footer {width}: {dialog}");
        await ShootAsync(page, $"{width}-dialog-sheet-36ch-labels-fit");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal-overlay").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

        await page.GetByTestId("open-sheet").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(700);
        await page.EvaluateAsync(
            """
            (labels) => {
                const els = document.querySelectorAll('.tm-modal-overlay .tm-modal-footer .tm-btn .tm-btn-label');
                els[0].textContent = labels[0];
                els[1].textContent = labels[1];
            }
            """, new[] { fr36, cs36 });
        await page.WaitForTimeoutAsync(300);

        var modal = await MeasureSheetFooterAsync(page, ".tm-modal-footer", isDialog: false);
        TestContext.WriteLine($"modal sheet footer {width}: {modal}");
        await page.Keyboard.PressAsync("Escape");

        using (var d = JsonDocument.Parse(dialog))
        using (var m = JsonDocument.Parse(modal))
        {
            Assert.AreEqual("16px", d.RootElement.GetProperty("dialogInlinePad").GetString(),
                $"the dialog sheet must use the modal sheet's 16px inline padding below 640px: {dialog}");

            var dialogLabels = d.RootElement.GetProperty("labels").EnumerateArray().ToList();
            var modalLabels = m.RootElement.GetProperty("labels").EnumerateArray().ToList();
            Assert.AreEqual(modalLabels.Count, dialogLabels.Count, "both sheets must offer two footer labels");
            for (var i = 0; i < dialogLabels.Count; i++)
            {
                var dOver = dialogLabels[i].GetProperty("scroll").GetDouble() - dialogLabels[i].GetProperty("client").GetDouble();
                var mOver = modalLabels[i].GetProperty("scroll").GetDouble() - modalLabels[i].GetProperty("client").GetDouble();
                Assert.AreEqual(mOver, dOver, 1.0,
                    $"label {i} must have the same room in the dialog sheet as in the modal sheet at {width}px: dialog={dialog} modal={modal}");
                if (mOver <= 1)
                {
                    Assert.IsTrue(dOver <= 1,
                        $"the modal fits label {i} at {width}px, so the dialog must too: {dialog}");
                }
            }
        }
    }

    private static async Task<string> MeasureSheetFooterAsync(IPage page, string footerSelector, bool isDialog)
    {
        return await page.EvaluateAsync<string>(
            """
            (args) => {
                const overlay = document.querySelector('.tm-modal-overlay');
                const footer = overlay.querySelector(args.footerSelector);
                const dialog = overlay.querySelector('.tm-dialog');
                return JSON.stringify({
                    dialogInlinePad: dialog ? getComputedStyle(dialog).paddingLeft : 'none',
                    labels: [...footer.querySelectorAll('.tm-btn-label')].map(l => ({
                        scroll: l.scrollWidth, client: l.clientWidth,
                    })),
                });
            }
            """, new { footerSelector, isDialog });
    }
}
