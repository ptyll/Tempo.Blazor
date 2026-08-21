using Bunit;
using Tempo.ReportServer.Web.Pages;
using Tempo.ReportServer.Web.Tests.Fixtures;

namespace Tempo.ReportServer.Web.Tests;

public sealed class ApiKeysPageTests : ReportServerWebTestBase
{
    [Fact]
    public void ApiKeysPage_IssueKey_ShowsOneTimeSecretAndListsKeyAndAuditsCreation()
    {
        SignIn();
        var cut = Render<ApiKeysPage>();

        cut.Find("[data-testid='api-key-application']").Input("payments-app");
        cut.Find("[data-testid='api-key-create']").Click();

        var secret = cut.Find("[data-testid='api-key-secret-value']").TextContent;
        secret.Should().StartWith("tmr_");

        cut.Find("[data-testid='api-keys-table']").TextContent.Should().Contain("payments-app");

        // Key creation is recorded in the audit trail (who/when/what).
        cut.FindAll("[data-testid='audit-row']").Should().NotBeEmpty();
        cut.Find("[data-testid='audit-table']").TextContent.Should().Contain("ChangeAcl");
    }

    [Fact]
    public void ApiKeysPage_CopySecret_InvokesClipboardInteropAndMarksCopied()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<ApiKeysPage>();

            cut.Find("[data-testid='api-key-create']").Click();
            cut.Find("[data-testid='api-key-copy']").Click();

            JSInterop.VerifyInvoke("tempoReportServer.copyToClipboard");
            cut.Find("[data-testid='api-key-copy']").TextContent.Should().Contain("Copied");
        }
    }

    [Fact]
    public void ApiKeysPage_RevokeKey_RequiresConfirmation_ThenMarksKeyRevoked()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<ApiKeysPage>();

            // The demo store seeds a deterministic key (rk_demo_embed) for the northwind tenant.
            cut.Find("[data-testid='api-key-status-rk_demo_embed']").TextContent.Trim().Should().Be("Active");

            // First click only arms the confirmation; the key is NOT yet revoked.
            cut.Find("[data-testid='api-key-revoke-rk_demo_embed']").Click();
            cut.Find("[data-testid='api-key-revoke-confirm-panel-rk_demo_embed']").TextContent
                .Should().Contain("permanent");
            cut.Find("[data-testid='api-key-status-rk_demo_embed']").TextContent.Trim().Should().Be("Active");

            // Second click confirms and performs the revoke.
            cut.Find("[data-testid='api-key-revoke-confirm-rk_demo_embed']").Click();
            cut.Find("[data-testid='api-key-status-rk_demo_embed']").TextContent.Trim().Should().Be("Revoked");
        }
    }

    [Fact]
    public void ApiKeysPage_RevokeKey_CanBeCancelled_LeavesKeyActive()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<ApiKeysPage>();

            cut.Find("[data-testid='api-key-revoke-rk_demo_embed']").Click();
            cut.Find("[data-testid='api-key-revoke-cancel-rk_demo_embed']").Click();

            // Confirmation dismissed, the plain trigger is back, and the key stays active.
            cut.Find("[data-testid='api-key-revoke-rk_demo_embed']");
            cut.FindAll("[data-testid='api-key-revoke-confirm-panel-rk_demo_embed']").Should().BeEmpty();
            cut.Find("[data-testid='api-key-status-rk_demo_embed']").TextContent.Trim().Should().Be("Active");
        }
    }

    [Fact]
    public void ApiKeysPage_RotateKey_RequiresConfirmation_ThenRevokesOriginalAndIssuesReplacement()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<ApiKeysPage>();

            cut.Find("[data-testid='api-key-rotate-rk_demo_embed']").Click();

            // Consequence is spelled out before the destructive step is confirmed.
            cut.Find("[data-testid='api-key-rotate-confirm-panel-rk_demo_embed']").TextContent
                .Should().Contain("NEW secret");
            cut.Find("[data-testid='api-key-status-rk_demo_embed']").TextContent.Trim().Should().Be("Active");

            cut.Find("[data-testid='api-key-rotate-confirm-rk_demo_embed']").Click();

            cut.Find("[data-testid='api-key-secret-value']").TextContent.Should().StartWith("tmr_");
            cut.Find("[data-testid='api-key-status-rk_demo_embed']").TextContent.Trim().Should().Be("Revoked");
        }
    }

    [Fact]
    public void ApiKeysPage_AuditFilter_ByOutcomeNarrowsResults()
    {
        SignIn();
        var cut = Render<ApiKeysPage>();

        // Create a key so there is at least one (Allowed) audit event.
        cut.Find("[data-testid='api-key-create']").Click();
        cut.FindAll("[data-testid='audit-row']").Should().NotBeEmpty();

        // Filtering by Denied removes all Allowed events -> empty state.
        cut.Find("[data-testid='audit-filter-outcome']").Change("Denied");

        cut.FindAll("[data-testid='audit-row']").Should().BeEmpty();
        cut.Find("[data-testid='audit-empty']").Should().NotBeNull();
    }
}
