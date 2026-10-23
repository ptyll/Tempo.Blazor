using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmKycWizard + TmScreeningResultPanel on the /kyc-wizard demo page (WASM demo
/// at 7106). Covers the person (FO) walkthrough to submission, per-step validation
/// blocking (edge), the pre-seeded company (PO) draft with the ownership tree and its
/// share validation (edge), draft saving, the screening confirm/dismiss workflow, and
/// the read-only + empty screening panels (edge). Screenshots land in
/// <c>__screenshots__/kyc-wizard/</c>.
/// </summary>
[TestClass]
public class KycWizardE2ETests : WasmTestBase
{
    private const string DemoPage = "/kyc-wizard";

    private sealed record DemoPageHandle(IPage Page, List<string> Errors);

    private async Task<DemoPageHandle> OpenPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1100);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await GotoAppReadyAsync(page, $"{BaseUrl}{DemoPage}");

        await page.Locator("[data-testid='kyc-demo-person'] [data-testid='kyc-wizard']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        return new DemoPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(DemoPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    private static ILocator Person(IPage page) => page.Locator("[data-testid='kyc-demo-person']");

    private static ILocator Company(IPage page) => page.Locator("[data-testid='kyc-demo-company']");

    private static async Task NextAsync(ILocator wizard)
        => await wizard.Locator("[data-testid='kyc-next']").ClickAsync();

    // ── Person (FO) walkthrough ──────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Kyc_PersonWalkthrough_SubmitsSuccessfully()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var wizard = Person(page);

        // Person path shows five steps in the stepper.
        Assert.AreEqual(5, await wizard.Locator(".tm-stepper-item").CountAsync());

        // Subject.
        await wizard.Locator("[data-testid='kyc-first-name']").FillAsync("Bedřich");
        await wizard.Locator("[data-testid='kyc-last-name']").FillAsync("Novák");
        await wizard.Locator("[data-testid='kyc-birth-date']").FillAsync("1980-05-12");
        await wizard.Locator("[data-testid='kyc-nationality']").FillAsync("CZ");
        await SaveScreenshotAsync(page, "person-subject");
        await NextAsync(wizard);

        // Documents.
        await wizard.Locator("[data-testid='kyc-step-documents']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await wizard.Locator("[data-testid='kyc-add-document']").ClickAsync();
        await wizard.Locator("[data-testid='kyc-doc-number']").FillAsync("123456789");
        await wizard.Locator("[data-testid='kyc-doc-issuer']").FillAsync("Magistrát Praha");
        await wizard.Locator("[data-testid='kyc-doc-valid-until']").FillAsync("2032-01-01");
        await NextAsync(wizard);

        // Addresses.
        await wizard.Locator("[data-testid='kyc-step-addresses']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await wizard.Locator("[data-testid='kyc-add-address']").ClickAsync();
        await wizard.Locator("[data-testid='kyc-addr-street']").FillAsync("Dlouhá 12");
        await wizard.Locator("[data-testid='kyc-addr-city']").FillAsync("Praha");
        await wizard.Locator("[data-testid='kyc-addr-postal']").FillAsync("110 00");
        await wizard.Locator("[data-testid='kyc-addr-country']").FillAsync("CZ");
        await NextAsync(wizard);

        // Declarations.
        await wizard.Locator("[data-testid='kyc-step-declarations']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await wizard.Locator("[data-testid='kyc-pep-no']").CheckAsync();
        await wizard.Locator("[data-testid='kyc-source-of-funds']").FillAsync("Employment income");
        await wizard.Locator("[data-testid='kyc-consent']").CheckAsync();
        await NextAsync(wizard);

        // Review: the entered data is summarized.
        var review = wizard.Locator("[data-testid='kyc-step-review']");
        await review.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var reviewText = await review.InnerTextAsync();
        StringAssert.Contains(reviewText, "Bedřich");
        StringAssert.Contains(reviewText, "Dlouhá 12");
        await SaveScreenshotAsync(page, "person-review");

        await wizard.Locator("[data-testid='kyc-submit']").ClickAsync();
        var submitted = wizard.Locator("[data-testid='kyc-submitted']");
        await submitted.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await submitted.InnerTextAsync(), "KYC-");
        await SaveScreenshotAsync(page, "person-submitted");
        AssertNoBlazorErrors(handle);
    }

    // ── Validation gate (edge) ───────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Kyc_EmptySubject_NextIsBlockedWithLocalizedErrors()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var wizard = Person(page);

        await NextAsync(wizard);

        var alert = wizard.Locator("[data-testid='kyc-errors']");
        await alert.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await alert.InnerTextAsync(), "First name is required");

        // Still on the subject step, with inline field errors.
        await Assertions.Expect(wizard.Locator("[data-testid='kyc-step-subject']")).ToBeVisibleAsync();
        var fieldErrors = await wizard.Locator("[data-testid='kyc-field-error']").CountAsync();
        Assert.IsTrue(fieldErrors >= 3, $"Expected inline field errors, found {fieldErrors}.");
        await SaveScreenshotAsync(page, "edge-validation-errors");
        AssertNoBlazorErrors(handle);
    }

    // ── Company (PO) draft + ownership tree ──────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Kyc_CompanyDraft_OwnershipTreeValidation_AndSubmit()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var wizard = Company(page);
        await wizard.ScrollIntoViewIfNeededAsync();

        // The pre-seeded draft loaded: company identity + six steps.
        await Assertions.Expect(wizard.Locator("[data-testid='kyc-company-name']"))
            .ToHaveValueAsync("Řehoř a syn s.r.o.", new LocatorAssertionsToHaveValueOptions { Timeout = 15000 });
        Assert.AreEqual(6, await wizard.Locator(".tm-stepper-item").CountAsync());

        await NextAsync(wizard);   // → Documents (pre-seeded, valid)
        await NextAsync(wizard);   // → Addresses (pre-seeded, valid)
        await NextAsync(wizard);   // → Ownership
        await wizard.Locator("[data-testid='kyc-step-ownership']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Two direct owners + one nested owner render as three rows.
        Assert.AreEqual(3, await wizard.Locator("[data-testid='kyc-owner-row']").CountAsync());
        await SaveScreenshotAsync(page, "company-ownership");

        // Edge: raising a share to 70 makes the direct owners sum to 110 % → blocked.
        await wizard.Locator("[data-testid='kyc-owner-share']").First.FillAsync("70");
        await NextAsync(wizard);
        var alert = wizard.Locator("[data-testid='kyc-errors']");
        await alert.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await alert.InnerTextAsync(), "exceed 100");
        await SaveScreenshotAsync(page, "edge-ownership-shares");

        // Fix the share, continue to declarations and submit from review.
        await wizard.Locator("[data-testid='kyc-owner-share']").First.FillAsync("60");
        await NextAsync(wizard);
        await wizard.Locator("[data-testid='kyc-step-declarations']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await wizard.Locator("[data-testid='kyc-pep-no']").CheckAsync();
        await wizard.Locator("[data-testid='kyc-source-of-funds']").FillAsync("Business revenue");
        await wizard.Locator("[data-testid='kyc-consent']").CheckAsync();
        await NextAsync(wizard);

        var review = wizard.Locator("[data-testid='kyc-step-review']");
        await review.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var reviewText = await review.InnerTextAsync();
        StringAssert.Contains(reviewText, "Jan Řehoř");
        StringAssert.Contains(reviewText, "Petr Král");

        await wizard.Locator("[data-testid='kyc-submit']").ClickAsync();
        await wizard.Locator("[data-testid='kyc-submitted']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "company-submitted");
        AssertNoBlazorErrors(handle);
    }

    // ── Draft saving ─────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Kyc_SaveDraft_ShowsConfirmation()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var wizard = Person(page);

        await wizard.Locator("[data-testid='kyc-first-name']").FillAsync("Bedřich");
        await wizard.Locator("[data-testid='kyc-save-draft']").ClickAsync();

        await wizard.Locator("[data-testid='kyc-draft-saved']")
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "draft-saved");
        AssertNoBlazorErrors(handle);
    }

    // ── Screening panel workflow ─────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screening_ConfirmWithNote_UpdatesStatusAndPendingCount()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;
        var panel = page.Locator("[data-testid='kyc-demo-screening']");
        await panel.ScrollIntoViewIfNeededAsync();

        // Findings are ordered pending-first by severity: the critical sanctions hit leads.
        var findings = panel.Locator("[data-testid='screening-finding']");
        await Assertions.Expect(findings).ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 30000 });
        Assert.AreEqual("find-sanctions-1", await findings.First.GetAttributeAsync("data-finding-id"));
        StringAssert.Contains(
            await panel.Locator("[data-testid='screening-pending-count']").InnerTextAsync(), "3");
        Assert.AreEqual("0.92",
            await findings.First.Locator("[data-testid='screening-confidence']").GetAttributeAsync("data-confidence"));
        await SaveScreenshotAsync(page, "screening-panel");

        // Cancel first (edge), then confirm with a note.
        await findings.First.Locator("[data-testid='screening-confirm']").ClickAsync();
        await findings.First.Locator("[data-testid='screening-resolve-cancel']").ClickAsync();
        await Assertions.Expect(findings.First.Locator("[data-testid='screening-resolution-form']"))
            .ToHaveCountAsync(0);

        await findings.First.Locator("[data-testid='screening-confirm']").ClickAsync();
        await findings.First.Locator("[data-testid='screening-note']").FillAsync("Verified against the register");
        await findings.First.Locator("[data-testid='screening-resolve-submit']").ClickAsync();

        // The finding is confirmed, records the reviewer, and the pending badge drops to 2.
        var confirmed = panel.Locator("[data-finding-id='find-sanctions-1']");
        await Assertions.Expect(confirmed.Locator("[data-testid='screening-status']"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-screening__status--confirmed"),
                new LocatorAssertionsToHaveClassOptions { Timeout = 15000 });
        StringAssert.Contains(
            await confirmed.Locator("[data-testid='screening-resolution']").InnerTextAsync(), "demo.reviewer");
        StringAssert.Contains(
            await panel.Locator("[data-testid='screening-pending-count']").InnerTextAsync(), "2");
        await SaveScreenshotAsync(page, "screening-confirmed");
        AssertNoBlazorErrors(handle);
    }

    // ── Read-only + empty panels (edge) ──────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Screening_ReadOnlyAndEmptyPanels()
    {
        var handle = await OpenPageAsync();
        var page = handle.Page;

        var readOnly = page.Locator("[data-testid='kyc-demo-screening-readonly']");
        await readOnly.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(readOnly.Locator("[data-testid='screening-finding']"))
            .ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 30000 });
        Assert.AreEqual(0, await readOnly.Locator("[data-testid='screening-confirm']").CountAsync(),
            "Read-only panel must not offer resolution actions.");

        var empty = page.Locator("[data-testid='kyc-demo-screening-empty']");
        await empty.Locator(".tm-empty-state").WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await SaveScreenshotAsync(page, "edge-readonly-empty");
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "kyc-wizard");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
