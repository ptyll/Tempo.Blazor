using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionAuditLogE2ETests : NotionE2ETestBase
{
    private const string ApiBase = "https://localhost:5100/api/notion";
    private const string Page1Id = "11111111-1111-1111-1111-111111111111";
    private const string Page2Id = "22222222-2222-2222-2222-222222222222";

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF32: audit log records real API actions, filters entries, exports CSV, and captures UX baseline.")]
    public async Task CF32_AuditLog_HappyPathAndBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedAuditPageAsync();
        await PerformAuditedApiActionsAsync(page);
        await OpenAuditAsync(page);

        var panel = page.GetByTestId("notion-audit-panel");
        await Assertions.Expect(panel.GetByTestId("notion-audit-list")).ToContainTextAsync("Created");
        await Assertions.Expect(panel.GetByTestId("notion-audit-list")).ToContainTextAsync("Edited");
        await Assertions.Expect(panel.GetByTestId("notion-audit-list")).ToContainTextAsync("Moved");
        await Assertions.Expect(panel.GetByTestId("notion-audit-list")).ToContainTextAsync("Restricted");

        var capture = await CaptureBaselineAsync("audit", "cf32-audit-panel", page.Locator(".tm-notion-audit-panel").First);

        await panel.GetByTestId("notion-audit-user-filter").FillAsync("demo");
        await panel.GetByTestId("notion-audit-action-filter").SelectOptionAsync("edit");
        await panel.GetByTestId("notion-audit-apply").ClickAsync();
        // Phase 5 keeps metadata behind the row toggle (table and cards).
        var toggles = panel.Locator(".tm-notion-audit__row-toggle:visible");
        var toggleCount = await toggles.CountAsync();
        for (var i = 0; i < toggleCount; i++)
            await toggles.Nth(i).ClickAsync();
        await Assertions.Expect(panel.Locator(".tm-notion-audit__row-details:visible").Filter(new() { HasText = "CF32 Audit Workspace Updated" })).ToHaveCountAsync(1);

        await panel.GetByTestId("notion-audit-export").ClickAsync();
        await Assertions.Expect(panel.GetByTestId("notion-audit-export-link")).ToHaveAttributeAsync("href", new Regex("^data:text/csv"));
        await CaptureBaselineAsync("audit", "cf32-csv-export-ready", page.Locator(".tm-notion-audit__toolbar").First);

        TestContext.WriteLine($"UX CF32 audit baseline captured: {capture.FullPagePath} / {capture.RegionPath}");
        TestContext.WriteLine("UX CF32 review: filters stay close to the log, action badges make high-risk events scannable, and paging/export remain reachable without covering entries.");
    }

    [TestMethod]
    [Description("CF32: providerless, empty filters, and many-entry paging states work.")]
    public async Task CF32_AuditLog_EdgeCases_Work()
    {
        var providerless = await OpenNotionEditorAsync("?disableAuditProvider=true");
        Assert.AreEqual(0, await providerless.GetByTestId("notion-audit-open").CountAsync(), "Audit entry point should be hidden when no provider is configured.");
        await CaptureBaselineAsync("audit", "cf32-providerless-hidden-state", providerless.Locator(".tm-notion-topbar").First);

        var empty = await OpenNotionEditorAsync();
        await SeedEmptyAuditPageAsync();
        await OpenAuditAsync(empty);
        await Assertions.Expect(empty.GetByTestId("notion-audit-empty")).ToContainTextAsync("No audit entries match the current filters.");

        await empty.GetByTestId("notion-audit-user-filter").FillAsync("nobody");
        await empty.GetByTestId("notion-audit-apply").ClickAsync();
        await Assertions.Expect(empty.GetByTestId("notion-audit-empty")).ToBeVisibleAsync();
        await CaptureBaselineAsync("audit", "cf32-filter-no-results", empty.Locator(".tm-notion-audit-panel").First);

        var many = await OpenNotionEditorAsync();
        await SeedManyAuditEntriesPageAsync();
        await OpenAuditAsync(many);
        await Assertions.Expect(many.GetByTestId("notion-audit-page")).ToContainTextAsync("Page 1 of 3");
        await many.GetByTestId("notion-audit-next").ClickAsync();
        await Assertions.Expect(many.GetByTestId("notion-audit-page")).ToContainTextAsync("Page 2 of 3");
        await CaptureBaselineAsync("audit", "cf32-many-entries-page2", many.Locator(".tm-notion-audit-panel").First);
    }

    private static async Task OpenAuditAsync(IPage page)
    {
        await ClickNotionCommandAsync(page, "notion-audit-open");
        await page.GetByTestId("notion-audit-panel").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    private static async Task PerformAuditedApiActionsAsync(IPage page)
    {
        await page.EvaluateAsync(
            @"async ({ apiBase, page1Id, page2Id }) => {
                const headers = {
                    'content-type': 'application/json',
                    'x-tempo-userid': 'demo',
                    'x-tempo-userdisplayname': 'Demo User'
                };

                const createdResponse = await fetch(`${apiBase}/pages`, {
                    method: 'POST',
                    headers,
                    body: JSON.stringify({ title: 'CF32 API Created Page', parentId: null })
                });
                if (!createdResponse.ok) throw new Error(`Create failed: ${createdResponse.status}`);
                const created = await createdResponse.json();

                const pageResponse = await fetch(`${apiBase}/pages/${page1Id}`);
                if (!pageResponse.ok) throw new Error(`Read failed: ${pageResponse.status}`);
                const page = await pageResponse.json();
                page.title = 'CF32 Audit Workspace Updated';
                const updateResponse = await fetch(`${apiBase}/pages/${page1Id}`, {
                    method: 'PUT',
                    headers,
                    body: JSON.stringify(page)
                });
                if (!updateResponse.ok) throw new Error(`Update failed: ${updateResponse.status}`);

                const moveResponse = await fetch(`${apiBase}/pages/${page2Id}/move`, {
                    method: 'POST',
                    headers,
                    body: JSON.stringify({ newParentId: page1Id })
                });
                if (!moveResponse.ok) throw new Error(`Move failed: ${moveResponse.status}`);

                const restrictResponse = await fetch(`${apiBase}/permissions/pages/${page1Id}`, {
                    method: 'PUT',
                    headers,
                    body: JSON.stringify({
                        pageId: page1Id,
                        mode: 1,
                        users: [],
                        groups: [],
                        inheritedFromPageId: null
                    })
                });
                if (!restrictResponse.ok) throw new Error(`Restrict failed: ${restrictResponse.status}`);

                const deleteResponse = await fetch(`${apiBase}/pages/${created.id}`, {
                    method: 'DELETE',
                    headers
                });
                if (!deleteResponse.ok) throw new Error(`Delete failed: ${deleteResponse.status}`);
            }",
            new { apiBase = ApiBase, page1Id = Page1Id, page2Id = Page2Id });
    }
}
