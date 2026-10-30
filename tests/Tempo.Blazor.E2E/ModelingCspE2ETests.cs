using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Modeling redesign phase 1 — CSP guard. The demo Server host serves the modeling editor under
/// <c>/modeling-editor?csp=1</c> with a Content-Security-Policy that does NOT contain
/// <c>unsafe-eval</c>. On that page the two former eval call paths must still work: the focus
/// return after closing the open-editor overlay and the tree drag bridge (both run through the
/// <c>modeling-editor.js</c> ES module, imported via <c>IJSObjectReference</c>).
/// <para>
/// IMPORTANT: this spec deliberately avoids <c>page.WaitForFunctionAsync</c> (and the shared
/// <c>WaitForAppReadyAsync</c>) — Playwright evaluates its predicate with <c>eval</c>, which a
/// no-<c>unsafe-eval</c> CSP refuses. All waits here go through locator waits and
/// <see cref="ILocator.WaitForAsync"/>, which never evaluate strings as JavaScript.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Server")]
public sealed class ModelingCspE2ETests : ServerTestBase
{
    private const string CspModelingEditorUrl = "/modeling-editor?csp=1";

    [TestMethod]
    [Description("The CSP variant of the modeling editor page ships a strict CSP without unsafe-eval and loads")]
    public async Task CspPage_ShipsStrictHeaderWithoutUnsafeEval()
    {
        var run = await OpenLoadedModelingPageAsync(captureHeader: true);

        var csp = await run.CspTask;
        Assert.IsNotNull(csp, "The ?csp=1 document should carry a Content-Security-Policy header.");
        StringAssert.Contains(csp, "script-src");
        // Token check, not a substring: 'wasm-unsafe-eval' legitimately contains the letters.
        Assert.IsFalse(csp.Contains("'unsafe-eval'", StringComparison.OrdinalIgnoreCase),
            $"The CSP must not allow the unsafe-eval source: {csp}");

        Assert.AreEqual("bpmn-validate-order", await DragTreeNodeAndReadFlagAsync(run.Page),
            "The editor keeps working under a CSP without unsafe-eval.");
        AssertNoEvalViolations(run);
    }

    [TestMethod]
    [Description("Closing the open-editor overlay returns focus to the trigger under a CSP without unsafe-eval")]
    public async Task CspPage_FocusReturnAfterOverlayClose_Works()
    {
        var run = await OpenLoadedModelingPageAsync();
        var page = run.Page;

        await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();
        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        await page.Locator("[data-testid='modeling-open-diagram-close']").ClickAsync();
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });

        // The trigger regains focus (the module's root-scoped focusWithin path, not eval). Polled with
        // EvaluateAsync — an activeElement read cannot be expressed as a locator.
        for (var attempt = 0; ; attempt++)
        {
            var active = await page.EvaluateAsync<string>(
                "() => document.activeElement?.closest?.('[data-testid]')?.getAttribute('data-testid') || ''");
            if (string.Equals(active, "modeling-open-in-editor-button", StringComparison.Ordinal))
            {
                break;
            }

            if (attempt >= 25)
            {
                Assert.Fail($"Focus did not return to the open-editor trigger; active element: '{active}'.");
            }

            await page.WaitForTimeoutAsync(200);
        }

        AssertNoEvalViolations(run);
    }

    [TestMethod]
    [Description("Dragging a model tree node registers the dataTransfer bridge under a CSP without unsafe-eval")]
    public async Task CspPage_TreeDragBridge_Works()
    {
        var run = await OpenLoadedModelingPageAsync();

        Assert.AreEqual("bpmn-validate-order", await DragTreeNodeAndReadFlagAsync(run.Page),
            "The ES-module drag bridge must publish the dragged element id on a strict-CSP page.");

        AssertNoEvalViolations(run);
    }

    /// <summary>
    /// Dispatches a REAL DragEvent (with a DataTransfer) on a tree node and reads the
    /// module-published window flag. A synthetic <c>Event('dragstart')</c> has no dataTransfer
    /// and would also break Blazor's own event-args parsing on the circuit.
    /// </summary>
    private static async Task<string> DragTreeNodeAndReadFlagAsync(IPage page)
    {
        var node = page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']");
        await node.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await node.EvaluateAsync(
            """
            element => element.dispatchEvent(
                new DragEvent('dragstart', { bubbles: true, cancelable: true, dataTransfer: new DataTransfer() }));
            """);
        return await page.EvaluateAsync<string>("() => window.__tmModelingDraggedElementId || ''");
    }

    [TestMethod]
    [Description("Fáze 14: inspector editing (rename → tree + preview) works under a strict CSP without unsafe-eval")]
    public async Task CspPage_InspectorEditing_WorksWithoutEval()
    {
        var run = await OpenLoadedModelingPageAsync(querySuffix: "&editing=1");

        await run.Page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").ClickAsync();
        var name = run.Page.Locator("#modeling-inspector-name");
        await name.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await name.FillAsync("Renamed under CSP");
        await name.PressAsync("Enter");

        await run.Page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']",
                new PageLocatorOptions { HasText = "Renamed under CSP" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        AssertNoEvalViolations(run);
    }

    private async Task<CspRun> OpenLoadedModelingPageAsync(bool captureHeader = false, string querySuffix = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");

        TaskCompletionSource<string?> cspSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var violations = new List<string>();
        var page = await context.NewPageAsync();
        page.Response += (_, response) =>
        {
            if (captureHeader && response.Url.Contains("/modeling-editor", StringComparison.Ordinal))
            {
                var header = response.Headers.FirstOrDefault(pair =>
                    string.Equals(pair.Key, "Content-Security-Policy", StringComparison.OrdinalIgnoreCase));
                cspSource.TrySetResult(header.Value);
            }
        };
        CaptureEvalViolations(page, violations);

        await page.GotoAsync($"{BaseUrl}{CspModelingEditorUrl}{querySuffix}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000
        });

        // Eval-free readiness gates (see the class remark).
        await page.Locator("body[data-blazor-ready]").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        await page.Locator("[data-testid='modeling-diagram-preview']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });

        return new CspRun(page, captureHeader ? cspSource.Task : Task.FromResult<string?>(null), violations);
    }

    private static void CaptureEvalViolations(IPage page, List<string> violations)
    {
        page.Console += (_, message) =>
        {
            if (message.Type == "error"
                && message.Text.Contains("Refused to evaluate a string as JavaScript", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(message.Text);
            }
        };
        page.PageError += (_, error) =>
        {
            if (error.Contains("EvalError", StringComparison.OrdinalIgnoreCase)
                || error.Contains("refused to execute", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(error);
            }
        };
    }

    private static void AssertNoEvalViolations(CspRun run)
        => Assert.IsEmpty(run.Violations, "A CSP eval violation occurred: " + string.Join(" | ", run.Violations));

    private sealed record CspRun(IPage Page, Task<string?> CspTask, List<string> Violations);
}
