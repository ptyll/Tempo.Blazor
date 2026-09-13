using System.Net.Http;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Shared base class for Notion editor E2E tests and UX baseline screenshot capture.
/// </summary>
public abstract class NotionE2ETestBase : WasmTestBase
{
    private IPage? _page;
    private int _viewportWidth = 1280;
    private int _viewportHeight = 720;

    protected IPage Page => _page ?? throw new InvalidOperationException("OpenNotionEditorAsync must be called before using the page.");

    /// <summary>Restores the demo page, its blocks and its history to their seeded state.</summary>
    protected static async Task ResetNotionDemoDataAsync()
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler);

        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* The API may not be up yet; the test's own waits will report that. */ }
    }

    /// <summary>
    /// Opens the demo editor on a freshly seeded page. Without the reset every test inherits the
    /// blocks the previous ones converted or deleted, so a class that expects a paragraph to be
    /// there fails purely because of the order it happened to run in.
    /// </summary>
    protected async Task<IPage> OpenNotionEditorAsync(string query = "")
    {
        await ResetNotionDemoDataAsync();

        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");

        _page = await context.NewPageAsync();
        await _page.SetViewportSizeAsync(_viewportWidth, _viewportHeight);
        await _page.GotoAsync($"{BaseUrl}/notion-editor{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });

        await WaitForAppReadyAsync(_page);
        await _page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await _page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });

        return _page;
    }

    protected async Task<IPage> OpenNotionEditorAsync(int viewportWidth, int viewportHeight, string query = "")
    {
        await SetViewportAsync(viewportWidth, viewportHeight);
        return await OpenNotionEditorAsync(query);
    }

    protected async Task SeedEmptyPageAsync()
    {
        await InvokeSeedAsync("seedEmptyPage");
        await Page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedRichPageAsync()
    {
        await InvokeSeedAsync("seedRichPage");
        await Page.WaitForSelectorAsync(".tm-notion-callout, .tm-notion-code-block, .tm-notion-todo", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedTextFormattingPageAsync()
    {
        await InvokeSeedAsync("seedTextFormattingPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb100000-0000-0000-0000-000000000011'] .tm-notion-code-block", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedMentionTokenPageAsync()
    {
        await InvokeSeedAsync("seedMentionTokenPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb500000-0000-0000-0000-000000000003'] .tm-notion-token[data-key='unknown.invoice_deadline']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedListTodoPageAsync()
    {
        await InvokeSeedAsync("seedListTodoPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb200000-0000-0000-0000-000000000010'] .tm-notion-toggle", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedActionItemsPageAsync()
    {
        await InvokeSeedAsync("seedActionItemsPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf300000-0000-0000-0000-000000000003'] .tm-notion-todo__due--overdue", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedTasksPageAsync()
    {
        await InvokeSeedAsync("seedTasksPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf400000-0000-0000-0000-000000000101'] .tm-notion-todo__due--overdue", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedEmptyTasksPageAsync()
    {
        await InvokeSeedAsync("seedEmptyTasksPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf401000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedManyTasksPageAsync()
    {
        await InvokeSeedAsync("seedManyTasksPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf402000-0000-0000-0000-000000000100'] .tm-notion-todo", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedMediaPageAsync()
    {
        await InvokeSeedAsync("seedMediaPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb600000-0000-0000-0000-000000000021'] .tm-notion-image-block__img", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedTablePageAsync()
    {
        await InvokeSeedAsync("seedTablePage");
        await Page.WaitForSelectorAsync("[data-block-id='eb700000-0000-0000-0000-000000000010'] .tm-notion-table", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedAtomicTablePageAsync()
    {
        await InvokeSeedAsync("seedAtomicTablePage");
        await Page.WaitForSelectorAsync(
            "[data-block-id='f6000000-0000-0000-0000-000000000010'] .tm-notion-table",
            new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 60000
            });
    }

    protected async Task SeedLayoutPageAsync()
    {
        await InvokeSeedAsync("seedLayoutPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb800000-0000-0000-0000-000000000010'] .tm-notion-column-list", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedKrFidelityPageAsync()
    {
        await InvokeSeedAsync("seedKrFidelityPage");
        await Page.WaitForSelectorAsync(
            "[data-block-id='f7000000-0000-0000-0000-000000000020'] .tm-notion-table",
            new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 60000
            });
    }

    protected async Task SeedEmptyTocPageAsync()
    {
        await InvokeSeedAsync("seedEmptyTocPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb810000-0000-0000-0000-000000000002'] .tm-toc__empty", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedCommentsPageAsync()
    {
        await InvokeSeedAsync("seedCommentsPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb100010-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedCommentlessPageAsync()
    {
        await InvokeSeedAsync("seedCommentlessPage");
        await Page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedSidebarDeepPageAsync()
    {
        await InvokeSeedAsync("seedSidebarDeepPage");
        await Page.WaitForSelectorAsync(".tm-notion-sidebar", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedSidebarEmptyPageAsync()
    {
        await InvokeSeedAsync("seedSidebarEmptyPage");
        await Page.WaitForSelectorAsync(".tm-npt-empty", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedSidebarTrashPageAsync()
    {
        await InvokeSeedAsync("seedSidebarTrashPage");
        await Page.WaitForSelectorAsync(".tm-ns-trash__count", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedPageSettingsPageAsync()
    {
        await InvokeSeedAsync("seedPageSettingsPage");
        await Page.WaitForSelectorAsync(".tm-notion-header-cover", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedHistoryEmptyPageAsync()
    {
        await InvokeSeedAsync("seedHistoryEmptyPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb130100-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedHistoryManyPageAsync()
    {
        await InvokeSeedAsync("seedHistoryManyPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb130100-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedHistoryDiffPageAsync()
    {
        await InvokeSeedAsync("seedHistoryDiffPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb130100-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedCollaborationPageAsync()
    {
        await InvokeSeedAsync("seedCollaborationPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb140000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedSpecialBlocksPageAsync()
    {
        await InvokeSeedAsync("seedSpecialBlocksPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb150000-0000-0000-0000-000000000010'] .tm-notion-equation-block", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedWorkItemsPageAsync()
    {
        await InvokeSeedAsync("seedWorkItemsPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf500000-0000-0000-0000-000000000010'] .tm-work-item--card", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedLabelsPageAsync()
    {
        await InvokeSeedAsync("seedLabelsPage");
        await Page.WaitForSelectorAsync(".tm-notion-labels .tm-notion-labels__chip", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedContentByLabelPageAsync()
    {
        await InvokeSeedAsync("seedContentByLabelPage");
        await Page.WaitForSelectorAsync(".tm-cbl", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedIncludePagePageAsync()
    {
        await InvokeSeedAsync("seedIncludePagePage");
        await Page.WaitForSelectorAsync(".tm-include-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedChildrenDisplayPageAsync()
    {
        await InvokeSeedAsync("seedChildrenDisplayPage");
        await Page.WaitForSelectorAsync(".tm-children", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedExcerptPageAsync()
    {
        await InvokeSeedAsync("seedExcerptPage");
        await Page.WaitForSelectorAsync(".tm-excerpt-include", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedPagePropertiesPageAsync()
    {
        await InvokeSeedAsync("seedPagePropertiesPage");
        await Page.WaitForSelectorAsync(".tm-props-report", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedPageInfoPageAsync()
    {
        await InvokeSeedAsync("seedPageInfoPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedEmptyPageInfoPageAsync()
    {
        await InvokeSeedAsync("seedEmptyPageInfoPage");
        await Page.WaitForSelectorAsync(".tm-notion-page__empty-hint", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedAnalyticsPageAsync()
    {
        await InvokeSeedAsync("seedAnalyticsPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedEmptyAnalyticsPageAsync()
    {
        await InvokeSeedAsync("seedEmptyAnalyticsPage");
        await Page.WaitForSelectorAsync(".tm-notion-page__empty-hint", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedAuditPageAsync()
    {
        await InvokeSeedAsync("seedAuditPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedEmptyAuditPageAsync()
    {
        await InvokeSeedAsync("seedEmptyAuditPage");
        await Page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedManyAuditEntriesPageAsync()
    {
        await InvokeSeedAsync("seedManyAuditEntriesPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedPublicSharePageAsync()
    {
        await InvokeSeedAsync("seedPublicSharePage");
        await Page.WaitForSelectorAsync("[data-block-id='cf330000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedExpiredPublicSharePageAsync()
    {
        await InvokeSeedAsync("seedExpiredPublicSharePage");
        await Page.WaitForSelectorAsync("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedWatchPageAsync()
    {
        await InvokeSeedAsync("seedWatchPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf160000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedPageReactionsEmptyPageAsync()
    {
        await InvokeSeedAsync("seedPageReactionsEmptyPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf170000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedPageReactionsManyPageAsync()
    {
        await InvokeSeedAsync("seedPageReactionsManyPage");
        await Page.WaitForSelectorAsync(".tm-page-reactions__pill", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedSmartLinksPageAsync()
    {
        await InvokeSeedAsync("seedSmartLinksPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf800000-0000-0000-0000-000000000010'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedDragDropPageAsync()
    {
        await InvokeSeedAsync("seedDragDropPage");
        await Page.WaitForSelectorAsync("[data-block-id='eb160000-0000-0000-0000-000000000010'] .tm-notion-column-list", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedRestrictionsPageAsync()
    {
        await InvokeSeedAsync("seedRestrictionsPage");
        await Page.WaitForSelectorAsync(".tm-notion-restricted-badge", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedSearchPageAsync()
    {
        await InvokeSeedAsync("seedSearchPage");
        await Page.WaitForSelectorAsync("[data-block-id='cf220000-0000-0000-0000-000000000002'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedSpacesPageAsync()
    {
        await InvokeSeedAsync("seedSpacesPage");
        await Page.Locator("[data-testid='notion-space-switcher']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await Page.Locator(".tm-npt-title").Filter(new LocatorFilterOptions { HasText = "CF29 Team Launch Plan" }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedManySpacesPageAsync()
    {
        await InvokeSeedAsync("seedManySpacesPage");
        await Page.Locator("[data-testid='notion-space-switcher']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await Page.Locator("[data-testid='notion-space-current']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedBlogPageAsync()
    {
        await InvokeSeedAsync("seedBlogPage");
        await Page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedEmptyBlogPageAsync()
    {
        await InvokeSeedAsync("seedEmptyBlogPage");
        await Page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedManyBlogPostsPageAsync()
    {
        await InvokeSeedAsync("seedManyBlogPostsPage");
        await Page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedBulkPagesAsync()
    {
        await InvokeSeedAsync("seedBulkPages");
        await Page.WaitForSelectorAsync("[data-block-id='cf240001-0000-0000-0000-000000000001'] .tm-notion-editable", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await Page.Locator(".tm-npt-title").Filter(new LocatorFilterOptions { HasText = "CF24 Source Root" }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task SeedExportPageAsync()
    {
        await InvokeSeedAsync("seedExportPage");
        await Page.Locator(".tm-notion-header-title").Filter(new LocatorFilterOptions { HasText = "CF25 Export Bridge" }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await Page.Locator(".tm-npt-title").Filter(new LocatorFilterOptions { HasText = "CF25 Export Bridge" }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
    }

    protected async Task ShowCollaborationNoUsersAsync() =>
        await InvokeSeedAsync("showCollaborationNoUsers");

    protected async Task ShowCollaborationOneCursorAsync() =>
        await InvokeSeedAsync("showCollaborationOneCursor");

    protected async Task ShowCollaborationManyCursorsAsync() =>
        await InvokeSeedAsync("showCollaborationManyCursors");

    protected async Task ShowCollaborationLongNamesAsync() =>
        await InvokeSeedAsync("showCollaborationLongNames");

    protected async Task ShowCollaborationOverlappingCursorsAsync() =>
        await InvokeSeedAsync("showCollaborationOverlappingCursors");

    protected static async Task SeedDatabaseAsync(string seed)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost:5100")
        };

        using var response = await http.PostAsync($"/api/notion/databases/e2e/seed/{Uri.EscapeDataString(seed)}", null);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Assert.Fail("Database E2E seed endpoint was not found on the HTTPS Demo API.");
        }

        response.EnsureSuccessStatusCode();
    }

    protected static async Task SelectLocatorContentsAsync(IPage page, ILocator locator)
    {
        await page.WaitForFunctionAsync(
            "() => window.tmNotionEditor?.hasSelectionWatcher?.(document.querySelector('.tm-notion-page')) === true",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await locator.EvaluateAsync("""
            el => {
                const host = el.closest('[contenteditable="true"]');
                host?.focus?.({ preventScroll: true });
                const range = document.createRange();
                range.selectNodeContents(el);
                const selection = window.getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
                document.dispatchEvent(new Event('selectionchange'));
                document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
            }
            """);
        await page.EvaluateAsync("() => window.tmNotionEditor.forceInlineToolbarForSelection(document.querySelector('.tm-notion-page'))");
        await page.WaitForTimeoutAsync(300);
    }

    protected static async Task OpenNotionInlineToolbarForBlockAsync(IPage page, string blockId, string editableSelector)
    {
        var editable = page.Locator($"[data-block-id='{blockId}'] {editableSelector}").First;
        await editable.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await editable.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "selector => (document.querySelector(selector)?.textContent || '').trim().length > 0",
            $"[data-block-id='{blockId}'] {editableSelector}",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        await editable.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.WaitForTimeoutAsync(300);

        if (await page.Locator(".tm-notion-inline-toolbar").First.CountAsync() == 0 ||
            !await page.Locator(".tm-notion-inline-toolbar").First.IsVisibleAsync())
        {
            await SelectLocatorContentsAsync(page, editable);
        }

        await page.Locator(".tm-notion-inline-toolbar").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    protected static async Task ClickNotionToolbarButtonAsync(IPage page, string title)
    {
        var button = page.Locator($"button[title='{title}']").First;
        await button.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5000 });
        await button.EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(200);
    }

    protected async Task<NotionBaselineCapture> CaptureBaselineAsync(string area, string state)
    {
        var outputDir = GetBaselineDirectory(area);
        var safeState = SanitizePathPart(state);
        var fullPath = Path.Combine(outputDir, $"{safeState}.png");
        var regionPath = Path.Combine(outputDir, $"{safeState}.region.png");

        await Page.WaitForTimeoutAsync(250);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        var region = Page.Locator(".tm-notion-page").First;
        await region.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            OmitBackground = false
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    protected async Task<NotionBaselineCapture> CaptureBaselineAsync(string area, string state, ILocator region)
    {
        var outputDir = GetBaselineDirectory(area);
        var safeState = SanitizePathPart(state);
        var fullPath = Path.Combine(outputDir, $"{safeState}.png");
        var regionPath = Path.Combine(outputDir, $"{safeState}.region.png");

        await Page.WaitForTimeoutAsync(250);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        await region.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            OmitBackground = false
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    protected async Task SetViewportAsync(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        if (_page is not null)
        {
            await _page.SetViewportSizeAsync(width, height);
        }
    }

    protected async Task InvokeSeedAsync(string methodName)
    {
        _ = Page;
        await Page.WaitForFunctionAsync(
            "methodName => window.tmNotionDemo && typeof window.tmNotionDemo[methodName] === 'function'",
            methodName,
            new PageWaitForFunctionOptions { Timeout = 60000 });
        await Page.EvaluateAsync("methodName => window.tmNotionDemo[methodName]()", methodName);
    }

    /// <summary>
    /// Routed through <see cref="BaselineOutput"/>: without TM_WRITE_BASELINES the capture lands in
    /// TestResults, not under the baseline root. The redirect is deliberately NOT a skip — the
    /// tests around these captures assert behaviour.
    /// </summary>
    private string GetBaselineDirectory(string area) =>
        BaselineOutput.DirectoryFor(TestContext, "notion", SanitizePathPart(area));

    private static string SanitizePathPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '-' : char.ToLowerInvariant(ch)).ToArray();
        return new string(chars);
    }
}

public sealed record NotionBaselineCapture(string FullPagePath, string RegionPath);
