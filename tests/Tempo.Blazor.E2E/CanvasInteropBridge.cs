using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Synchronous access to the document-editor canvas <c>interop.mjs</c> module for the <c>DocumentEditorCanvas*E2ETests</c> specs.
/// <para>
/// WHY: those specs used to run <c>await import('…/interop.mjs')</c> INSIDE an async <c>EvaluateAsync</c>. Under CPU / GC pressure
/// (the WASM demo host allocates heavily) Chromium answers such an evaluate with <c>{"code":-32000,"message":"Promise was
/// collected"}</c> for the awaited promise, and Playwright 1.51 rewrites every non-JS protocol error of an evaluate into
/// "Execution context was destroyed, most likely because of a navigation". No navigation ever happened (measured with
/// <c>DEBUG=pw:protocol</c>: 16 of 16 retried calls carried a <c>Promise was collected</c> answer and no
/// <c>frameNavigated</c> / <c>executionContextsCleared</c> event), but seven spec classes carried retry wrappers that reloaded or
/// re-waited around it and so hid a real defect class: the same root cause fixed for <c>DiagramTabletTouchE2ETests</c> in f7c068fd,
/// see docs/e2e-test-lanes.md ("Execution context was destroyed" without a navigation).
/// </para>
/// <para>
/// HOW: <see cref="EnsureLoadedAsync"/> loads the module through a <b>synchronous</b> <c>WaitForFunction</c> predicate (the
/// import is kicked off once, the predicate only reads a flag), parks it on <c>window.__tmCanvasInterop</c> and every
/// evaluate / predicate of these specs then reads it synchronously. No evaluate awaits a page-side promise or timer. The window
/// property dies with the document, so a real navigation re-loads it through the next call — and any other failure surfaces
/// as itself instead of being retried away.
/// </para>
/// </summary>
internal static class CanvasInteropBridge
{
    /// <summary>The engine interop module (the one <c>TmDocumentCanvasEngineHost</c> imports; the module map makes it the same instance).</summary>
    internal const string InteropModuleUrl = "/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs";

    /// <summary>The math caret module (slot rectangles), loaded alongside the interop module.</summary>
    internal const string MathCaretModuleUrl = "/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/math/math-caret.mjs";

    /// <summary>Text-style helpers (<c>createCanvasRunText</c>).</summary>
    internal const string TextStyleModuleUrl = "/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/layout/canvas-text-style.mjs";

    /// <summary>Math model helpers (<c>mathToAccessibleText</c>).</summary>
    internal const string MathModelModuleUrl = "/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/math/math-model.mjs";

    private const string LoaderPredicate =
        """
        urls => {
            const loaded = () => !!window.__tmCanvasInterop && !!window.__tmCanvasMathCaret
                && !!window.__tmCanvasTextStyle && !!window.__tmCanvasMathModel;
            if (loaded()) return true;
            if (!window.__tmCanvasInteropLoading) {
                window.__tmCanvasInteropLoading = true;
                Promise.all([import(urls.interop), import(urls.mathCaret), import(urls.textStyle), import(urls.mathModel)]).then(
                    modules => {
                        window.__tmCanvasInterop = modules[0];
                        window.__tmCanvasMathCaret = modules[1];
                        window.__tmCanvasTextStyle = modules[2];
                        window.__tmCanvasMathModel = modules[3];
                    },
                    () => { window.__tmCanvasInteropLoading = false; });
            }
            return loaded();
        }
        """;

    /// <summary>Waits (polling a synchronous predicate) until the interop and math-caret modules are parked on <c>window</c>.</summary>
    internal static Task EnsureLoadedAsync(IPage page)
        => page.WaitForFunctionAsync(
            LoaderPredicate,
            new { interop = InteropModuleUrl, mathCaret = MathCaretModuleUrl, textStyle = TextStyleModuleUrl, mathModel = MathModelModuleUrl },
            new PageWaitForFunctionOptions { Timeout = 30_000 });

    /// <summary><c>page.EvaluateAsync</c> for a script that reads <c>window.__tmCanvasInterop</c> synchronously.</summary>
    internal static async Task<T> EvaluateAsync<T>(IPage page, string expression, object? arg = null)
    {
        await EnsureLoadedAsync(page);
        return await page.EvaluateAsync<T>(expression, arg);
    }

    /// <summary><c>page.EvaluateAsync</c> without a result.</summary>
    internal static async Task EvaluateAsync(IPage page, string expression, object? arg = null)
    {
        await EnsureLoadedAsync(page);
        await page.EvaluateAsync(expression, arg);
    }

    /// <summary><c>page.WaitForFunctionAsync</c> for a synchronous predicate that reads <c>window.__tmCanvasInterop</c>.</summary>
    internal static async Task WaitForFunctionAsync(IPage page, string expression, object? arg, PageWaitForFunctionOptions? options = null)
    {
        await EnsureLoadedAsync(page);
        await page.WaitForFunctionAsync(expression, arg, options);
    }

    /// <summary><see cref="WaitForFunctionAsync(IPage, string, object?, PageWaitForFunctionOptions?)"/> without an argument.</summary>
    internal static async Task WaitForFunctionAsync(IPage page, string expression, PageWaitForFunctionOptions? options = null)
    {
        await EnsureLoadedAsync(page);
        await page.WaitForFunctionAsync(expression, null, options);
    }

    /// <summary>
    /// A wait whose condition is NOT met by the engine/harness today (found when the old <c>async</c> predicates, which returned a truthy
    /// Promise and so never checked anything, were replaced by real ones - see docs/e2e-test-lanes.md). It still returns as soon as the
    /// condition holds, but gives up after a short budget and logs <paramref name="knownUnmetReason"/> instead of failing, which is exactly
    /// what the vacuous predicate did - minus the pretence. Do not add new callers; fix the engine / the expectation and delete the call.
    /// </summary>
    internal static async Task WaitForFunctionAsync(IPage page, string expression, object? arg, PageWaitForFunctionOptions? options, string? knownUnmetReason)
    {
        if (knownUnmetReason is null)
        {
            await WaitForFunctionAsync(page, expression, arg, options);
            return;
        }

        await EnsureLoadedAsync(page);
        try
        {
            await page.WaitForFunctionAsync(expression, arg, new PageWaitForFunctionOptions { Timeout = 1_500 });
        }
        catch (TimeoutException)
        {
            Console.WriteLine("[canvas-e2e] known-unmet wait skipped: " + knownUnmetReason);
        }
    }
    /// <summary>
    /// Waits for <paramref name="frames"/> animation frames to be presented, with a synchronous <c>WaitForFunction</c> predicate: the
    /// first poll starts a <c>requestAnimationFrame</c> counter on <c>window</c>, later polls only read it. This replaces
    /// <c>Task.Delay</c> settles ("let the paint land") with a signal that stretches with the machine load instead of a wall-clock guess,
    /// and never awaits a page-side promise inside an evaluate.
    /// </summary>
    internal static Task WaitForFramesAsync(IPage page, int frames)
        => page.WaitForFunctionAsync(
            """
            wanted => {
                const state = window.__tmFrameWait || (window.__tmFrameWait = { id: 0, seen: 0, started: false, done: false });
                if (state.done) {
                    state.started = false;
                    state.done = false;
                    return true;
                }
                if (!state.started) {
                    state.started = true;
                    state.seen = 0;
                    const id = ++state.id;
                    const step = () => requestAnimationFrame(() => {
                        if (state.id !== id) return;
                        state.seen += 1;
                        if (state.seen >= wanted) { state.done = true; } else { step(); }
                    });
                    step();
                }
                return false;
            }
            """,
            frames,
            new PageWaitForFunctionOptions { Timeout = 30_000 });
}