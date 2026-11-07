using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan <c>eadbf967</c> phase 5 — the schema-driven inspector (decision
/// <c>MODELING-PROPERTY-SCHEMA</c>): the demo BPMN step-attribute schema with a HOST lookup, the
/// required-field error and its live announcement, the save gate, the save → reload round trip and
/// the compact band where the inspector lives in a tab.
/// <para>
/// The demo scene is <c>?scenario=step-attributes</c>: four order steps, "Validate order" carrying
/// every attribute and "Approve discount" deliberately missing the two REQUIRED fields
/// (instructions, role). Only the phase-5 specs use it — every other modeling scenario keeps the
/// 2.10.0 inspector, which is exactly the backward-compatibility contract of the decision.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingPropertySchemaPhase5E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    /// <summary>The phase-5 scene with editing on (the writer persists into the demo writer).</summary>
    private const string StepSchemaUrl = "?editing=1&scenario=step-attributes";

    [TestMethod]
    [Description("Selecting a task renders the schema sections; the lookup fills the role reference and the typed values survive a save + reload")]
    public async Task Schema_FieldsRender_AndTheLookupValueSurvivesASaveAndReload()
    {
        var page = await OpenEditorAsync(StepSchemaUrl, 1440, 900);
        try
        {
            // "Approve discount" ships WITHOUT its two required fields, and the save gate refuses any
            // save until the model has no property error — so the flow starts by completing it. That
            // is the contract: a schema error blocks the save and the user fixes the model first.
            // Review U7: the error was already on the value when the inspector seeded, so it renders
            // as a described message (the editor's aria-describedby) and does NOT shout on load.
            await SelectTreeRowAsync(page, "step-approve");
            await ExpectSeededErrorAsync(page, "instructions");
            await FillMarkdownAsync(page, "#modeling-inspector-field-instructions", "Approve the discount.");
            await PickReferenceAsync(page, "#modeling-inspector-field-role", "fin", "Finance");
            await WaitForNoIssueAsync(page);

            await SelectTreeRowAsync(page, "step-validate");

            // The schema surface replaced the generic table for the keys it covers; the pre-existing
            // demo properties (none here) and any host key would stay in "Other properties".
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-schema-section-instructions']");
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-schema-section-ownership']");
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-schema-section-service-level']");
            await ExpectVisibleAsync(page, "[data-testid='modeling-inspector-schema-section-exceptions']");

            // Typed controls: markdown instructions, the role combobox (a reference snapshot), the
            // systems chips, the duration field and the enums.
            await ExpectVisibleAsync(page, "#modeling-inspector-field-instructions .tm-markdown-editor");
            await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-field-role']", "Sales");
            await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-field-systems']", "ERP");
            // The structured list renders one item form with the stored values in its INPUTS (the
            // text content of a form does not carry them) and the enum's stored option value.
            Assert.AreEqual("E-STOCK", await page.Locator("#modeling-inspector-field-exceptions-0-code").InputValueAsync(),
                "the structured list item kept its code");
            Assert.AreEqual("warning", await page.Locator("#modeling-inspector-field-exceptions-0-severity").InputValueAsync(),
                "the item's enum kept its option value");
            Assert.AreEqual("high", await page.Locator("#modeling-inspector-field-priority").InputValueAsync(),
                "the enum stores the option value");
            Assert.AreEqual("240", await page.Locator("#modeling-inspector-field-sla").InputValueAsync(),
                "a duration is stored in MINUTES and the field starts in the minutes unit");

            // The duration commits in MINUTES whatever unit the user reads.
            await page.Locator("[data-testid='modeling-inspector-field-sla'] .tm-modeling-inspector__duration-unit")
                .SelectOptionAsync("hours");
            await page.Locator("#modeling-inspector-field-sla").FillAsync("2");
            await page.Locator("#modeling-inspector-field-sla").BlurAsync();

            // The priority enum commits on change (the demo writer is in Manual mode, so it stays pending).
            await page.Locator("#modeling-inspector-field-priority").SelectOptionAsync("critical");

            // The host LOOKUP: open the role combobox, type, pick the hit.
            await PickReferenceAsync(page, "#modeling-inspector-field-role", "ris", "Risk owner");
            await ExpectPendingAsync(page);

            // Save: the inspector header hosts the Manual-mode save while something is pending.
            await page.Locator("[data-testid='modeling-inspector-save']").ClickAsync();
            await WaitForPendingAsync(page, 0);

            // Reload = a full editor remount; the loader serves the DEMO WRITER's persisted copy.
            await page.Locator("[data-testid='modeling-demo-reload']").ClickAsync();
            await WaitForLoadedAsync(page);
            await SelectTreeRowAsync(page, "step-validate");

            await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-field-role']", "Risk owner");
            Assert.AreEqual("critical", await page.Locator("#modeling-inspector-field-priority").InputValueAsync(),
                "the enum value was persisted");
            Assert.AreEqual("120", await page.Locator("#modeling-inspector-field-sla").InputValueAsync(),
                "the two hours entered through the hours unit persisted as 120 minutes");

            // The typed reference is stored as {type, id, display, snapshotAt} — the demo page shows
            // the last change set, so the contract is visible end to end.
            var changeSet = await page.Locator("[data-testid='modeling-demo-last-changeset']").TextContentAsync();
            Assert.IsNotNull(changeSet);
            Assert.IsTrue(changeSet.Contains("\"role\"", StringComparison.Ordinal), "the change set carries the role property");
            Assert.IsTrue(changeSet.Contains("\"snapshotAt\"", StringComparison.Ordinal), "the stored reference keeps its display snapshot");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A missing required field renders inline quietly (no role=alert at seed), lands in the issues dock and blocks the save")]
    public async Task Schema_SeededRequiredField_IsQuiet_AndBlocksTheSave()
    {
        var page = await OpenEditorAsync(StepSchemaUrl, 1440, 900);
        try
        {
            await SelectTreeRowAsync(page, "step-approve");

            var field = page.Locator("[data-testid='modeling-inspector-field-instructions']");
            await field.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

            // Review U7: an error that was already there when the inspector seeded is described by the
            // control (aria-describedby) instead of announced assertively — a panel that shouts on
            // load is noise. The message is still rendered, and the field is still marked invalid.
            var message = field.Locator("#modeling-inspector-field-instructions-error");
            await message.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            StringAssert.Contains(await message.TextContentAsync(), "Instructions");
            StringAssert.Contains(await message.TextContentAsync(), "required");
            Assert.IsNull(await message.GetAttributeAsync("role"), "a seeded error stays quiet until the user commits the field");
            Assert.AreEqual(0, await field.Locator("[role='alert']").CountAsync(), "no assertive announcement on load");

            Assert.AreEqual("true", await field.GetAttributeAsync("data-schema-error"));
            StringAssert.Contains(
                await field.Locator(".tm-mde-content").GetAttributeAsync("aria-describedby") ?? string.Empty,
                "modeling-inspector-field-instructions-error",
                "the seeded error is reachable through the textbox's described-by");

            // The other required field (the role reference) carries aria-invalid on its combobox: the
            // core input owns that state, the shared validator's Error produced it.
            Assert.AreEqual(
                "true",
                await page.Locator("[data-testid='modeling-inspector-field-role'] [aria-invalid='true']")
                    .First.GetAttributeAsync("aria-invalid"));

            // The shared validator's issues are part of the dock, next to the generated ones.
            var dockCount = int.Parse(await page.Locator("[data-testid='modeling-issue-panel']").GetAttributeAsync("data-issue-count") ?? "0");
            Assert.IsTrue(dockCount >= 2, $"the dock lists the missing required fields (got {dockCount})");

            // Review A9: an untouched error never traps the user. A change on a key that carries no
            // error is allowed through, so the save runs and the pending badge clears.
            await page.Locator("#modeling-inspector-field-priority").SelectOptionAsync("high");
            await ExpectPendingAsync(page);
            var save = page.Locator("[data-testid='modeling-inspector-save']");
            await save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await save.ClickAsync();
            await WaitForPendingAsync(page, 0);

            // ...but an error on the key the change set TOUCHES still blocks it: adding a structured
            // item without its required code reports the error on the ITEM (review U6) and the gate
            // refuses the save, keeping the pending badge.
            await SelectTreeRowAsync(page, "step-validate");
            await page.Locator("[data-testid='modeling-inspector-field-exceptions-add']").ClickAsync();
            await ExpectPendingAsync(page);

            var itemCode = page.Locator("#modeling-inspector-field-exceptions-1-code");
            await itemCode.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            var itemError = page.Locator("#modeling-inspector-field-exceptions-1-code-error");
            await itemError.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            StringAssert.Contains(await itemError.TextContentAsync(), "Code");

            await page.Locator("[data-testid='modeling-inspector-save']").ClickAsync();
            await page.WaitForTimeoutAsync(500);
            await ExpectPendingAsync(page);
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Mobile 390x844: the inspector lives in a tab and renders the schema sections there")]
    public async Task Schema_OnMobile_RendersInsideTheInspectorTab()
    {
        var page = await OpenEditorAsync(StepSchemaUrl, 390, 844, touch: true);
        try
        {
            await page.Locator("[data-testid='modeling-panel-tab-tree']")
                .TapAsync(new LocatorTapOptions { Timeout = 20_000 });
            await SelectTreeRowAsync(page, "step-validate");
            await page.Locator("[data-testid='modeling-panel-tab-inspector']")
                .TapAsync(new LocatorTapOptions { Timeout = 20_000 });

            var section = page.Locator("[data-testid='modeling-inspector-schema-section-ownership']");
            await section.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await ExpectTextContainsAsync(page, "[data-testid='modeling-inspector-field-role']", "Sales");

            // The compact band keeps the schema fields editable (viewing + light edits is the G2 band).
            await ExpectVisibleAsync(page, "#modeling-inspector-field-priority");
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Waits for a schema field that carries a validator error (review U7: seeded errors are
    /// inline messages, not alerts).</summary>
    private static async Task ExpectSeededErrorAsync(IPage page, string fieldKey)
    {
        var field = page.Locator($"[data-testid='modeling-inspector-field-{fieldKey}']");
        await field.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.WaitForFunctionAsync(
            "(key) => document.querySelector(`[data-testid='modeling-inspector-field-${key}'][data-schema-error='true']`) !== null",
            fieldKey,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private async Task<IPage> OpenEditorAsync(string query, int width, int height, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            Locale = "en-US",
            IgnoreHTTPSErrors = true
        });
        RegisterContext(context);
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await WaitForLoadedAsync(page);
        return page;
    }

    private static async Task WaitForLoadedAsync(IPage page)
    {
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.tm-diagram-node').length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 60000 });
    }

    private static async Task SelectTreeRowAsync(IPage page, string elementId)
    {
        var row = page.Locator($"[data-testid='modeling-tree-node-{elementId}']");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        if (await page.Locator("[data-testid='modeling-panel-tab-tree']").CountAsync() > 0
            && !await row.IsVisibleAsync())
        {
            await page.Locator("[data-testid='modeling-panel-tab-tree']")
                .ClickAsync(new LocatorClickOptions { Timeout = 20_000 });
        }

        await row.ClickAsync(new LocatorClickOptions { Timeout = 20_000 });
        await page.Locator($"[data-testid='modeling-editor'][data-selected-element-id='{elementId}']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });
    }

    private static async Task ExpectVisibleAsync(IPage page, string selector)
        => await page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

    private static async Task ExpectTextContainsAsync(IPage page, string selector, string expected)
    {
        var locator = page.Locator(selector).First;
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var text = await locator.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(text, expected);
    }

    private static async Task ExpectPendingAsync(IPage page)
    {
        var pending = await page.Locator("[data-testid='modeling-demo-pending']").TextContentAsync() ?? string.Empty;
        Assert.AreNotEqual(0, ExtractCount(pending), $"a pending change should be reported ('{pending}')");
    }

    /// <summary>Types into the Tempo markdown editor (a contenteditable); leaving it commits.</summary>
    private static async Task FillMarkdownAsync(IPage page, string fieldSelector, string markdown)
    {
        var editor = page.Locator($"{fieldSelector} .tm-mde-content");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await editor.ClickAsync();
        await page.Keyboard.TypeAsync(markdown);
        // focusout commits (the markdown editor has no blur callback of its own); the name input is
        // always mounted in the editing surface, so it is the natural "click away" target.
        await page.Locator("#modeling-inspector-name").ClickAsync();
    }

    /// <summary>Picks one entity reference through the host lookup (the APG combobox).</summary>
    private static async Task PickReferenceAsync(IPage page, string comboboxSelector, string search, string expected)
    {
        await page.Locator(comboboxSelector).ClickAsync();
        await page.Locator(".tm-multiselect__filter-input").First.FillAsync(search);
        var option = page.Locator(".tm-multiselect__option").Filter(new LocatorFilterOptions { HasTextString = expected }).First;
        await option.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await option.ClickAsync();
        await ExpectTextContainsAsync(page, comboboxSelector, expected);
    }

    /// <summary>Waits until the schema validation findings are gone (the save gate is open again).</summary>
    private static async Task WaitForNoIssueAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => { const el = document.querySelector(\"[data-testid='modeling-issue-panel']\"); return el && el.getAttribute('data-issue-count') === '0'; }",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private static async Task WaitForPendingAsync(IPage page, int expected)
    {
        await page.WaitForFunctionAsync(
            "(count) => { const el = document.querySelector(\"[data-testid='modeling-demo-pending']\"); return el && (el.textContent.match(/\\d+/) || ['0'])[0] === String(count); }",
            expected,
            new PageWaitForFunctionOptions { Timeout = 15000 });
    }

    private static int ExtractCount(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = System.Text.RegularExpressions.Regex.Match(text, "\\d+");
        return match.Success && int.TryParse(match.Value, out var value) ? value : 0;
    }
}
