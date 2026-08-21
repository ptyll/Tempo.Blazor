using Tempo.ReportServer.Web.Pages;
using Tempo.ReportServer.Web.Tests.Fixtures;

namespace Tempo.ReportServer.Web.Tests;

public sealed class PermissionsPageTests : ReportServerWebTestBase
{
    [Fact]
    public void PermissionsPage_ListsSeededAclForDefaultFolder_FromTypedClient()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<PermissionsPage>();

            cut.Find("[data-testid='f12-permissions-page']").TextContent.Should().Contain("Folder permissions");
            cut.Find("[data-testid='permissions-table']").TextContent.Should().Contain("finance-admins");
        }
    }

    [Fact]
    public void PermissionsPage_GrantAndRevoke_RoundTripsThroughTypedClient()
    {
        SignIn();
        var cut = Render<PermissionsPage>();

        cut.Find("[data-testid='permission-subject']").Input("sales-authors");
        cut.Find("[data-testid='permission-add']").Click();

        cut.Find("[data-testid='permission-row-sales-authors']").Should().NotBeNull();

        cut.Find("[data-testid='permission-revoke-sales-authors']").Click();

        cut.FindAll("[data-testid='permission-row-sales-authors']").Should().BeEmpty();
    }
}
