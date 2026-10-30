using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for modeling issue panel phase M13.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingIssuePanelM13E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Invalid generated relationship creates at least one visible warning")]
    public async Task IssuePanel_InvalidRelationshipShowsWarning()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=invalid-relationship");

        await WaitForIssueCountAtLeastAsync(page, 1);
        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-panel'] [data-severity='warning']");
    }

    [TestMethod]
    [Description("Info, warning and error severity icons render with distinct visible colors")]
    public async Task IssuePanel_SeverityIconsAreVisible()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        // The issues-mixed scenario carries TWO info issues (a linked one and a sourceless
        // orphan) — the icon selectors must not assume a single match.
        await ExpectVisibleAsync(page, ".tm-modeling-issue-panel__icon--info", selectorMustBeUnique: false);
        await ExpectVisibleAsync(page, ".tm-modeling-issue-panel__icon--warning");
        await ExpectVisibleAsync(page, ".tm-modeling-issue-panel__icon--error");

        Assert.IsTrue(await HasColoredIconAsync(page, ".tm-modeling-issue-panel__icon--info"));
        Assert.IsTrue(await HasColoredIconAsync(page, ".tm-modeling-issue-panel__icon--warning"));
        Assert.IsTrue(await HasColoredIconAsync(page, ".tm-modeling-issue-panel__icon--error"));
    }

    [TestMethod]
    [Description("Empty issue list shows a positive empty state")]
    public async Task IssuePanel_EmptyStateShowsPositiveMessage()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-empty");

        await page.Locator("[data-testid='modeling-issue-panel'][data-issue-count='0']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-empty']");
        StringAssert.Contains(await page.Locator("[data-testid='modeling-issue-empty']").TextContentAsync() ?? string.Empty, "No issues found");
    }

    [TestMethod]
    [Description("Clicking an issue selects and reveals the matching model tree node")]
    public async Task IssuePanel_ClickIssueSelectsTreeElement()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        await page.Locator("[data-testid='modeling-issue-panel'] [data-source-element-id='bpmn-validate-order']").First.ClickAsync();

        await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='bpmn-validate-order']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.AreEqual("true", await page.Locator("[data-testid='modeling-tree-node-bpmn-validate-order']").GetAttributeAsync("aria-selected"));
        Assert.IsTrue(await IsTreeNodeInsideScrollViewportAsync(page, "bpmn-validate-order"));
    }

    [TestMethod]
    [Description("Large issue lists stay scrollable without covering neighboring panels")]
    public async Task IssuePanel_ManyIssuesRemainScrollable()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-many");

        await WaitForIssueCountAtLeastAsync(page, 50);
        var list = page.Locator("[data-testid='modeling-issue-list']");
        Assert.IsTrue(await list.EvaluateAsync<bool>("element => element.scrollHeight > element.clientHeight"));

        await list.EvaluateAsync("element => element.scrollTop = element.scrollHeight");
        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-63']");
        Assert.IsFalse(await IssuePanelOverlapsPreviewAsync(page));
    }

    [TestMethod]
    [Description("Suggested fixes render under the main issue message")]
    public async Task IssuePanel_SuggestedFixIsVisible()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        await ExpectVisibleAsync(page, "[data-testid='modeling-issue-fix-1']");
        StringAssert.Contains(await page.Locator("[data-testid='modeling-issue-fix-1']").TextContentAsync() ?? string.Empty, "Assign an owner lane");
    }

    [TestMethod]
    [Description("Captures required M13 populated, empty and many-issue screenshots")]
    public async Task IssuePanel_CapturesM13Screenshots()
    {
        var withIssues = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");
        await WaitForIssueCountAtLeastAsync(withIssues, 3);
        await TakeScreenshotAsync(withIssues, "issue-panel-with-issues");
        await SaveStableScreenshotAsync(withIssues, "issue-panel-with-issues.png");

        var empty = await OpenLoadedModelingPageAsync("?scenario=issues-empty");
        await ExpectVisibleAsync(empty, "[data-testid='modeling-issue-empty']");
        await TakeScreenshotAsync(empty, "issue-panel-empty");
        await SaveStableScreenshotAsync(empty, "issue-panel-empty.png");

        var many = await OpenLoadedModelingPageAsync("?scenario=issues-many");
        await WaitForIssueCountAtLeastAsync(many, 50);
        await many.Locator("[data-testid='modeling-issue-list']").EvaluateAsync("element => element.scrollTop = element.scrollHeight / 2");
        await TakeScreenshotAsync(many, "issue-panel-many");
        await SaveStableScreenshotAsync(many, "issue-panel-many.png");
    }

    [TestMethod]
    [Description("A sourceless issue renders as a non-interactive list item without a pointer cursor")]
    public async Task IssuePanel_SourcelessIssueIsNotInteractive()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        // Index 3 is the demo's orphan issue (no SourceElementId / SourceRelationshipId).
        var staticItem = page.Locator("[data-testid='modeling-issue-list'] > li[role='listitem'] > div[data-testid='modeling-issue-3']");
        await staticItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var state = await staticItem.EvaluateAsync<string>(
            """
            element => {
                const style = getComputedStyle(element);
                return [
                    element.tagName,
                    style.cursor,
                    (element.textContent || '').replace(/\s+/g, ' ').trim()
                ].join('|');
            }
            """);
        var parts = state.Split('|');
        Assert.AreNotEqual("BUTTON", parts[0], "A sourceless issue must not be a button.");
        Assert.AreNotEqual("pointer", parts[1], "A sourceless issue must not promise navigation with a pointer cursor.");
        StringAssert.Contains(parts[2], "does not map to any element", "The accessible text is preserved.");
        Assert.AreEqual(0, await page.Locator("[data-testid='modeling-issue-3'][blazor\\:onclick]").CountAsync(),
            "The static item carries no click handler.");
    }

    [TestMethod]
    [Description("Fáze 11: chip + text + sort work together and navigation from a FILTERED issue still selects its element")]
    public async Task IssuePanel_FilterChipTextSortAndFilteredNavigation()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        var panel = page.Locator("[data-testid='modeling-issue-panel']");

        // Chip filter: the Error chip narrows the four-issue scenario to its single error row.
        await panel.Locator("[data-testid='modeling-issue-severity-chip-error']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '1'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        Assert.AreEqual("true", await panel.Locator("[data-testid='modeling-issue-severity-chip-error']")
            .GetAttributeAsync("aria-pressed"));

        // Navigation from the FILTERED issue selects the element (the filter never blocks it).
        await panel.Locator("[data-source-element-id='bpmn-ship-order']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await page.Locator("[data-testid='modeling-editor'][data-selected-element-id='bpmn-ship-order']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        // Text filter over the source NAME, then the Source sort — the row order must follow
        // the resolved names with the sourceless orphan last (original input indexes kept).
        await panel.Locator("[data-testid='modeling-issue-severity-chip-error']")
            .ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        await panel.Locator("[data-testid='modeling-issue-filter-input']").FillAsync("validate order");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='modeling-issue-panel']\")?.getAttribute('data-visible-issue-count') === '2'",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await panel.Locator("[data-testid='modeling-issue-filter-input']").FillAsync(string.Empty);
        await panel.Locator("[data-testid='modeling-issue-sort'] select")
            .SelectOptionAsync(new SelectOptionValue { Label = "Source" });
        await page.WaitForFunctionAsync(
            """
            () => {
                const rows = [...document.querySelectorAll("[data-testid='modeling-issue-list'] > li > *")]
                    .map(row => row.getAttribute('data-testid'));
                return rows.join(',') === 'modeling-issue-2,modeling-issue-0,modeling-issue-1,modeling-issue-3';
            }
            """,
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "")
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-issue-panel']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return page;
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector, int timeout = 5000, bool selectorMustBeUnique = true)
    {
        var locator = page.Locator(selector);
        if (!selectorMustBeUnique)
        {
            locator = locator.First;
        }

        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = timeout });
    }

    private static Task WaitForIssueCountAtLeastAsync(IPage page, int minimumCount) =>
        page.WaitForFunctionAsync(
            """
            minimumCount => Number(document.querySelector("[data-testid='modeling-issue-panel']")?.getAttribute('data-issue-count') ?? '0') >= minimumCount
            """,
            minimumCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static Task<bool> HasColoredIconAsync(IPage page, string selector) =>
        page.Locator(selector).First.EvaluateAsync<bool>(
            """
            element => {
                const style = getComputedStyle(element);
                return style.color !== 'rgba(0, 0, 0, 0)' && style.backgroundColor !== 'rgba(0, 0, 0, 0)';
            }
            """);

    private static Task<bool> IsTreeNodeInsideScrollViewportAsync(IPage page, string elementId) =>
        page.EvaluateAsync<bool>(
            """
            elementId => {
                const node = document.querySelector(`[data-testid='modeling-tree-node-${elementId}']`);
                const viewport = document.querySelector('.tm-modeling-model-tree__groups');
                if (!node || !viewport) {
                    return false;
                }

                const nodeRect = node.getBoundingClientRect();
                const viewportRect = viewport.getBoundingClientRect();
                return nodeRect.top >= viewportRect.top && nodeRect.bottom <= viewportRect.bottom;
            }
            """,
            elementId);

    private static Task<bool> IssuePanelOverlapsPreviewAsync(IPage page) =>
        page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector("[data-testid='modeling-issue-panel']");
                const preview = document.querySelector("[data-testid='modeling-preview-panel']");
                if (!panel || !preview) {
                    return true;
                }

                const a = panel.getBoundingClientRect();
                const b = preview.getBoundingClientRect();
                return !(a.right <= b.left || a.left >= b.right || a.bottom <= b.top || a.top >= b.bottom);
            }
            """);

    private static async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingIssuePanelM13E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m13");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        Assert.IsTrue(bytes.Length > 20_000, $"{fileName} screenshot should contain the rendered modeling issue panel.");
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }
}
