using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E smoke for <c>TmSelect</c> with a nullable TValue on the Forms demo page
/// (<c>/forms</c>, WASM demo at 7106): the null-valued option must be visible with its text,
/// <c>selectedIndex</c> must never fall to -1, and switching to a real option and back to the
/// empty option must round-trip <c>null</c> through the bound value.
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public class TmSelectNullableE2ETests : WasmTestBase
{
    private const string FormsPage = "/forms";

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private async Task<IPage> OpenFormsPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{FormsPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var card = page.Locator("[data-testid='select-nullable-card']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await card.ScrollIntoViewIfNeededAsync();
        return page;
    }

    private static ILocator Select(ILocator card) => card.Locator("select");

    private static async Task<int> SelectedIndexAsync(ILocator select) =>
        await select.EvaluateAsync<int>("el => el.selectedIndex");

    [TestMethod]
    [Description("Nullable select shows the null option selected (value=\"\") with selectedIndex >= 0 on load")]
    public async Task Select1_NullOptionVisible_SelectedWithEmptyValue()
    {
        var page = await OpenFormsPageAsync();
        var select = Select(page.Locator("[data-testid='select-nullable-card']"));

        // The empty (null) option is visible with its text and is the current selection.
        await Expect(select).ToHaveValueAsync(string.Empty);
        (await SelectedIndexAsync(select)).Should().BeGreaterThanOrEqualTo(0, "the select must show a real option, never a blank (selectedIndex = -1)");

        var selectedText = await select.EvaluateAsync<string>("el => el.options[el.selectedIndex]?.text ?? ''");
        Assert.AreEqual("Root (no parent)", selectedText,
            "the null-valued root option must be the displayed selection on load");
    }

    [TestMethod]
    [Description("Selecting a real option and back to the empty option round-trips null through the bound value")]
    public async Task Select2_RoundTrip_ToValueAndBackToNull()
    {
        var page = await OpenFormsPageAsync();
        var card = page.Locator("[data-testid='select-nullable-card']");
        var select = Select(card);
        var bound = page.Locator("[data-testid='select-nullable-value']");

        await Expect(bound).ToHaveTextAsync("null");

        await select.SelectOptionAsync(new SelectOptionValue { Label = "Books" });
        await Expect(bound).ToHaveTextAsync("2");
        await Expect(select).ToHaveValueAsync("2");

        // Back to the empty (null) option — twice, to prove the round trip repeats.
        for (var i = 0; i < 2; i++)
        {
            await select.SelectOptionAsync(new SelectOptionValue { Label = "Root (no parent)" });
            await Expect(bound).ToHaveTextAsync("null");
            await Expect(select).ToHaveValueAsync(string.Empty);
            (await SelectedIndexAsync(select)).Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [TestMethod]
    [Description("Selectable placeholder picks back to null and to a real option repeatedly")]
    public async Task Select3_SelectablePlaceholder_ClearsToNull()
    {
        var page = await OpenFormsPageAsync();
        var card = page.Locator("[data-testid='select-clearable-card']");
        var select = Select(card);
        var bound = page.Locator("[data-testid='select-clearable-value']");

        await Expect(bound).ToHaveTextAsync("2");

        // The placeholder is a real (not disabled) option with value "".
        var placeholder = card.Locator("option").First;
        Assert.AreEqual("No priority", await placeholder.TextContentAsync());
        Assert.IsNull(await placeholder.GetAttributeAsync("disabled"), "PlaceholderSelectable must render the placeholder enabled");

        await select.SelectOptionAsync(new SelectOptionValue { Label = "No priority" });
        await Expect(bound).ToHaveTextAsync("null");
        (await SelectedIndexAsync(select)).Should().BeGreaterThanOrEqualTo(0);

        await select.SelectOptionAsync(new SelectOptionValue { Label = "High" });
        await Expect(bound).ToHaveTextAsync("3");

        await select.SelectOptionAsync(new SelectOptionValue { Label = "No priority" });
        await Expect(bound).ToHaveTextAsync("null");
    }
}

/// <summary>
/// The same nullable-<c>TmSelect</c> smoke against the Blazor <b>Server</b> demo (7107) — the
/// change event takes the SignalR round trip there, which is where an empty-value select most
/// often loses its preselection.
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public class TmSelectNullableServerE2ETests : ServerTestBase
{
    private const string FormsPage = "/forms";

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    [TestMethod]
    [Description("Server: nullable select shows the null option selected and round-trips null through the bound value")]
    public async Task Select_Nullable_Server_NullOptionVisible_AndRoundTripsNull()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{FormsPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var card = page.Locator("[data-testid='select-nullable-card']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        var select = card.Locator("select");
        var bound = page.Locator("[data-testid='select-nullable-value']");

        await Expect(select).ToHaveValueAsync(string.Empty);
        var selectedIndex = await select.EvaluateAsync<int>("el => el.selectedIndex");
        Assert.IsTrue(selectedIndex >= 0, $"selectedIndex must not be -1, was {selectedIndex}");
        var selectedText = await select.EvaluateAsync<string>("el => el.options[el.selectedIndex]?.text ?? ''");
        Assert.AreEqual("Root (no parent)", selectedText);

        await select.SelectOptionAsync(new SelectOptionValue { Label = "Books" });
        await Expect(bound).ToHaveTextAsync("2");

        await select.SelectOptionAsync(new SelectOptionValue { Label = "Root (no parent)" });
        await Expect(bound).ToHaveTextAsync("null");
        await Expect(select).ToHaveValueAsync(string.Empty);
    }

    [TestMethod]
    [Description("Server: the selectable placeholder clears to null and a real option re-selects, selectedIndex stays >= 0")]
    public async Task Select_SelectablePlaceholder_Server_ClearsToNull_AndBackToValue()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{FormsPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var card = page.Locator("[data-testid='select-clearable-card']");
        await card.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        var select = card.Locator("select");
        var bound = page.Locator("[data-testid='select-clearable-value']");

        await Expect(bound).ToHaveTextAsync("2");

        // Pick the selectable placeholder → null (empty wire value, a real selected option).
        await select.SelectOptionAsync(new SelectOptionValue { Label = "No priority" });
        await Expect(bound).ToHaveTextAsync("null");
        await Expect(select).ToHaveValueAsync(string.Empty);
        var selectedIndex = await select.EvaluateAsync<int>("el => el.selectedIndex");
        Assert.IsTrue(selectedIndex >= 0, $"selectedIndex must not be -1 after clearing, was {selectedIndex}");

        // Back to a real option → 3.
        await select.SelectOptionAsync(new SelectOptionValue { Label = "High" });
        await Expect(bound).ToHaveTextAsync("3");
        await Expect(select).ToHaveValueAsync("3");
        selectedIndex = await select.EvaluateAsync<int>("el => el.selectedIndex");
        Assert.IsTrue(selectedIndex >= 0, $"selectedIndex must not be -1 after re-selecting, was {selectedIndex}");
    }
}
