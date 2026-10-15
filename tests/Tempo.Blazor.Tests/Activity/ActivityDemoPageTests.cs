using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>
/// bUnit coverage for the /activity demo page's standalone comments section: the draft-state
/// indicator (stable <c>data-testid</c> selectors the E2E lane builds on), the sample data's
/// action spread, and the consumer-side discard control wired through the component @ref.
/// </summary>
public class ActivityDemoPageTests : LocalizationTestBase
{
    public ActivityDemoPageTests()
    {
        Services.AddHttpClient("DemoApi", client =>
                client.BaseAddress = new Uri("https://demo.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new DemoApiHandler());
        Services.AddSingleton<ActivityHttpService>();
        Services.AddSingleton<AttachmentHttpProvider>();
    }

    [Fact]
    public void RendersCommentsSectionWithDraftIndicatorAndDiscardControl()
    {
        var cut = Render<ActivityPage>();

        cut.Find("[data-testid='activity-comments-section']").Should().NotBeNull();

        // The indicator mirrors TmCommentDraftState; the initial state is everything closed.
        cut.Find("[data-testid='activity-comments-draft-state']").Should().NotBeNull();
        cut.Find("[data-testid='activity-comments-draft-open']").TextContent.Trim().Should().Be("no");
        cut.Find("[data-testid='activity-comments-draft-editing']").TextContent.Trim().Should().Be("none");
        cut.Find("[data-testid='activity-comments-draft-unsaved']").TextContent.Trim().Should().Be("no");
        cut.Find("[data-testid='activity-comments-draft-state'] .sr-only").TextContent.Trim().Should().Be("No comment draft.");

        // The discard control (a TmButton) is useless with no editor open, so it renders disabled
        // and points screen readers at the status panel it acts on.
        var discard = cut.Find("[data-testid='activity-comments-discard-draft']");
        discard.HasAttribute("disabled").Should().BeTrue();
        discard.GetAttribute("aria-describedby").Should().Be("activity-comments-draft-state-panel");
    }

    [Fact]
    public void CommentsSection_RendersSampleCommentsWithActionSpread()
    {
        var cut = Render<ActivityPage>();

        var items = cut.FindAll("[data-testid='activity-comments-section'] .tm-comment-item");
        items.Count.Should().Be(3);

        // c1: edit + delete; c2: delete only; c3: read-only viewer.
        items[0].QuerySelector(".tm-comment-edit-btn").Should().NotBeNull();
        items[0].QuerySelector(".tm-comment-delete-btn").Should().NotBeNull();
        items[1].QuerySelector(".tm-comment-edit-btn").Should().BeNull();
        items[1].QuerySelector(".tm-comment-delete-btn").Should().NotBeNull();
        items[2].QuerySelector(".tm-comment-edit-btn").Should().BeNull();
        items[2].QuerySelector(".tm-comment-delete-btn").Should().BeNull();
    }

    [Fact]
    public void DraftIndicator_ReportsOpenEditor_WhenAddCommentClicked()
    {
        var cut = Render<ActivityPage>();

        cut.Find("[data-testid='activity-comments-section'] .tm-comments-add-btn").Click();

        cut.Find("[data-testid='activity-comments-draft-open']").TextContent.Trim().Should().Be("yes");
        cut.Find("[data-testid='activity-comments-draft-editing']").TextContent.Trim().Should().Be("none");
        cut.Find("[data-testid='activity-comments-draft-unsaved']").TextContent.Trim().Should().Be("no");
        cut.Find("[data-testid='activity-comments-draft-state'] .sr-only").TextContent.Trim().Should().Be("Comment editor open, no unsaved changes.");
    }

    [Fact]
    public void DiscardButton_ClosesOpenEditorThroughComponentRef()
    {
        var cut = Render<ActivityPage>();

        cut.Find("[data-testid='activity-comments-section'] .tm-comments-add-btn").Click();
        cut.Find("[data-testid='activity-comments-draft-open']").TextContent.Trim().Should().Be("yes");

        // An open editor enables the control (it renders disabled when nothing is open).
        var discard = cut.Find("[data-testid='activity-comments-discard-draft']");
        discard.HasAttribute("disabled").Should().BeFalse();
        discard.Click();

        cut.Find("[data-testid='activity-comments-draft-open']").TextContent.Trim().Should().Be("no");
        cut.Find("[data-testid='activity-comments-draft-unsaved']").TextContent.Trim().Should().Be("no");
        cut.Find("[data-testid='activity-comments-discard-draft']").HasAttribute("disabled").Should().BeTrue();
    }

    private sealed class DemoApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
    }
}
