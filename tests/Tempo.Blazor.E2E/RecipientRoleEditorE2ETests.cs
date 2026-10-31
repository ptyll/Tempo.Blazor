using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class RecipientRoleEditorE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Recipient role editor adds a new template role and updates the demo status")]
    public async Task RecipientRoleEditor_AddsTemplateRole()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var editor = page.Locator("[data-testid='recipient-role-editor-template']").First;
        await editor.ScrollIntoViewIfNeededAsync();
        await editor.Locator(".tm-recipient-role-editor__add").ClickAsync();

        await Expect(editor.Locator(".tm-recipient-role-editor__card")).ToHaveCountAsync(3);
        await Expect(page.Locator("[data-testid='recipient-role-editor-template-status']")).ToContainTextAsync("3 roles");
    }

    [TestMethod]
    [Description("Recipient role editor renames a role and keeps the status text in sync")]
    public async Task RecipientRoleEditor_RenamesTemplateRole()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var editor = page.Locator("[data-testid='recipient-role-editor-template']").First;
        await editor.ScrollIntoViewIfNeededAsync();
        var input = editor.Locator(".tm-recipient-role-editor__name").First;

        await input.FillAsync("Primary signer");
        await input.PressAsync("Tab");

        await Expect(page.Locator("[data-testid='recipient-role-editor-template-status']")).ToContainTextAsync("Primary signer");
    }

    [TestMethod]
    [Description("Submission recipient mode validates and accepts required recipient email")]
    public async Task RecipientRoleEditor_ValidatesRecipientEmail()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var editor = page.Locator("[data-testid='recipient-role-editor-submission']").First;
        await editor.ScrollIntoViewIfNeededAsync();
        var email = editor.Locator(".tm-recipient-role-editor__email").First;

        await email.FillAsync(string.Empty);
        await email.PressAsync("Tab");

        await Expect(page.Locator("[data-testid='recipient-role-editor-submission-status']")).ToContainTextAsync("1 recipient email missing");
        await Expect(editor.Locator(".tm-recipient-role-editor__validation").First).ToContainTextAsync("Email is required");

        await email.FillAsync("alex.updated@example.test");
        await email.PressAsync("Tab");

        await Expect(page.Locator("[data-testid='recipient-role-editor-submission-status']")).ToContainTextAsync("2 recipients ready");
    }

    [TestMethod]
    [Description("Recipient role editor reorders template roles and updates order numbers")]
    public async Task RecipientRoleEditor_ReordersTemplateRoles()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var editor = page.Locator("[data-testid='recipient-role-editor-template']").First;
        await editor.ScrollIntoViewIfNeededAsync();

        await editor.Locator(".tm-recipient-role-editor__move-down").First.ClickAsync();

        await Expect(editor.Locator(".tm-recipient-role-editor__card").First).ToHaveAttributeAsync("data-role-uuid", "template-approver");
        await Expect(editor.Locator(".tm-recipient-role-editor__order-badge").First).ToContainTextAsync("1");
        await Expect(page.Locator("[data-testid='recipient-role-editor-template-status']")).ToContainTextAsync("1. Approver");
    }

    [TestMethod]
    [Description("Template role editor keeps order, color, name, and invite controls visually separated")]
    public async Task RecipientRoleEditor_TemplateLayoutDoesNotOverlapControls()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);

        var row = page.Locator("[data-testid='recipient-role-editor-template'] .tm-recipient-role-editor__card").First;
        await row.ScrollIntoViewIfNeededAsync();

        var order = await row.Locator(".tm-recipient-role-editor__order-badge").BoundingBoxAsync();
        var color = await row.Locator(".tm-recipient-role-editor__field--color").BoundingBoxAsync();
        var name = await row.Locator(".tm-recipient-role-editor__field--name").BoundingBoxAsync();
        var invite = await row.Locator(".tm-recipient-role-editor__field--invite-by-role").BoundingBoxAsync();

        Assert.IsNotNull(order);
        Assert.IsNotNull(color);
        Assert.IsNotNull(name);
        Assert.IsNotNull(invite);
        Assert.IsTrue(order.Y + order.Height <= color.Y, "Order badge must sit in the header row above the card body.");
        Assert.IsTrue(name.Y + name.Height <= color.Y, "Role name field must sit above the color swatches (the card body order).");
        Assert.IsTrue(invite.Y - (name.Y + name.Height) >= 0, "Role name field must stay above the invite-by-role control.");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
