using System.Net.Http.Json;
using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

internal static class DocumentEditorE2EReset
{
    public static async Task ResetAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();

        var response = await http.PostAsync("api/document-editor/reset", JsonContent.Create(new { }));
        response.EnsureSuccessStatusCode();
    }

    public static Task InstallClientStateIsolationAsync(IBrowserContext context)
        => context.AddInitScriptAsync(
            """
            (() => {
                const keys = [
                    'documentEditorMigration',
                    'tmDocumentEditorMigration',
                    'tmDocumentEditorUseGoogleDocsEngine',
                    'tmDocumentEditorImageDebug'
                ];
                for (const storage of [window.localStorage, window.sessionStorage]) {
                    if (!storage) continue;
                    for (const key of keys) {
                        try { storage.removeItem(key); } catch {}
                    }
                }

                window.__tmDocumentEditorUseGoogleDocsEngine = false;
                window.__tmDocumentEditorE2EClientIsolation = {
                    installedAt: Date.now(),
                    clearedKeys: keys
                };
            })();
            """);

    public static async Task ResetTransientClientStateAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.EvaluateAsync(
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                try { window.tmDocumentEditor?.disableBeforeUnloadGuard?.(); } catch {}
                try { window.getSelection?.()?.removeAllRanges?.(); } catch {}

                const active = document.activeElement;
                if (active && active !== document.body && typeof active.blur === 'function') {
                    active.blur();
                }

                document.querySelectorAll('[data-testid="document-editor-live-region"], [data-testid="document-canvas-live-region"], .tm-core-live-region')
                    .forEach(node => { node.textContent = ''; });

                host?.removeAttribute('data-focus-owner');
                return {
                    hostFound: !!host,
                    beforeUnloadActive: !!window.tmDocumentEditor?.getBeforeUnloadGuardState?.().active,
                    activeElement: document.activeElement?.tagName || ''
                };
            }
            """);
        await page.WaitForTimeoutAsync(25);
    }
}
