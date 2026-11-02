using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SigningProductPanelsE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Signing completion panel renders completed and waiting states with interactive actions")]
    public async Task SigningCompletionPanel_RendersStatesAndActions()
    {
        var page = await OpenSigningComponentsAsync();
        var panel = await GetByTestIdAsync(page, "signing-completion-panel");

        await Assertions.Expect(panel).ToContainTextAsync("Document completed");
        await Assertions.Expect(panel.Locator("a.tm-signing-completion-panel__download")).ToHaveAttributeAsync("href", "/sample-signed.pdf");

        await panel.Locator(".tm-signing-completion-panel__send-copy").ClickAsync();
        await Assertions.Expect(await GetByTestIdAsync(page, "signing-completion-status")).ToContainTextAsync("Copy requested");

        await panel.Locator(".tm-signing-completion-panel__custom-action").ClickAsync();
        await Assertions.Expect(await GetByTestIdAsync(page, "signing-completion-status")).ToContainTextAsync("Custom action requested");

        var waitingPanel = await GetByTestIdAsync(page, "signing-completion-waiting");
        await Assertions.Expect(waitingPanel).ToContainTextAsync("Waiting for others");
        await Assertions.Expect(waitingPanel.Locator(".tm-signing-completion-panel__send-copy")).ToBeDisabledAsync();
    }

    [TestMethod]
    [Description("Submission status timeline renders signer lifecycle, delivery, verification, and KBA events")]
    public async Task SubmissionStatusTimeline_RendersLifecycleEvents()
    {
        var page = await OpenSigningComponentsAsync();
        var timeline = await GetByTestIdAsync(page, "submission-status-timeline");

        await Assertions.Expect(timeline).ToContainTextAsync("Sent");
        await Assertions.Expect(timeline).ToContainTextAsync("Opened");
        await Assertions.Expect(timeline).ToContainTextAsync("Completed");
        await Assertions.Expect(timeline).ToContainTextAsync("Declined");
        await Assertions.Expect(timeline).ToContainTextAsync("Email bounced");
        await Assertions.Expect(timeline).ToContainTextAsync("Email complaint");
        await Assertions.Expect(timeline).ToContainTextAsync("Verification completed");
        await Assertions.Expect(timeline).ToContainTextAsync("KBA completed");
        await Assertions.Expect(timeline).ToContainTextAsync("550 mailbox unavailable");
        await Assertions.Expect(timeline).ToContainTextAsync("Needs legal review");
    }

    [TestMethod]
    [Description("Share link panel renders copyable link, QR code, embed code, expiration, and enable toggle")]
    public async Task ShareLinkPanel_RendersLinkQrEmbedAndToggle()
    {
        var page = await OpenSigningComponentsAsync();
        var panel = await GetByTestIdAsync(page, "share-link-panel");

        await Assertions.Expect(panel.Locator(".tm-share-link-panel__input")).ToHaveValueAsync("https://sign.example.test/s/demo-token");
        await Assertions.Expect(panel.Locator(".tm-qr-code")).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".tm-share-link-panel__embed-code")).ToHaveValueAsync("<iframe src=\"https://sign.example.test/s/demo-token\"></iframe>");
        await Assertions.Expect(panel).ToContainTextAsync("Expires");

        // The enable control is a core TmToggle now (input[role=switch], visually hidden): the
        // click goes through the wrapping label (the phase-4 convention for hidden native inputs).
        await panel.Locator(".tm-toggle-label").ClickAsync();
        await Assertions.Expect(await GetByTestIdAsync(page, "share-link-status")).ToContainTextAsync("Share link disabled");
        await Assertions.Expect(panel.Locator(".tm-share-link-panel__input")).ToBeDisabledAsync();
    }

    [TestMethod]
    [Description("PDF signature verification moves from empty state to verified state and renders missing checksum state")]
    public async Task PdfSignatureVerification_RendersVerificationStates()
    {
        var page = await OpenSigningComponentsAsync();
        var panel = await GetByTestIdAsync(page, "pdf-signature-verification");

        await Assertions.Expect(panel).ToContainTextAsync("Verify a signed PDF");
        await panel.Locator(".tm-pdf-signature-verification__verify").ClickAsync();

        await Assertions.Expect(panel).ToContainTextAsync("PDF verified");
        await Assertions.Expect(panel).ToContainTextAsync("mutual-nda-signed.pdf");
        await Assertions.Expect(panel).ToContainTextAsync("sha256-7f2c-demo");
        await Assertions.Expect(panel).ToContainTextAsync("Alex Johnson");
        await Assertions.Expect(panel).ToContainTextAsync("SMS");

        var missingPanel = await GetByTestIdAsync(page, "pdf-signature-verification-missing");
        await Assertions.Expect(missingPanel).ToContainTextAsync("Checksum not found");
        await Assertions.Expect(missingPanel).ToContainTextAsync("sha256-missing");
    }

    [TestMethod]
    [Description("Audit trail viewer renders document checksum, signer identity, network evidence, verification method, and audit PDF")]
    public async Task AuditTrailViewer_RendersEvidence()
    {
        var page = await OpenSigningComponentsAsync();
        var viewer = await GetByTestIdAsync(page, "audit-trail-viewer");

        await Assertions.Expect(viewer).ToContainTextAsync("mutual-nda.pdf");
        await Assertions.Expect(viewer).ToContainTextAsync("sha256-7f2c-demo");
        await Assertions.Expect(viewer).ToContainTextAsync("Alex Johnson");
        await Assertions.Expect(viewer).ToContainTextAsync("alex@example.test");
        await Assertions.Expect(viewer).ToContainTextAsync("203.0.113.10");
        await Assertions.Expect(viewer).ToContainTextAsync("Europe/Prague");
        await Assertions.Expect(viewer).ToContainTextAsync("SMS");
        await Assertions.Expect(viewer.Locator(".tm-audit-trail-viewer__audit-pdf")).ToHaveAttributeAsync("href", "/audit-demo.pdf");
    }

    private async Task<IPage> OpenSigningComponentsAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/signing-components");
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task<ILocator> GetByTestIdAsync(IPage page, string testId)
    {
        var locator = page.Locator($"[data-testid='{testId}']").First;
        await locator.ScrollIntoViewIfNeededAsync();
        return locator;
    }
}
