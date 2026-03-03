using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Activity;

/// <summary>Accessibility tests for TmActivityLog (tab button IDs, aria-labelledby on panel).</summary>
public class TmActivityLogAccessibilityTests : LocalizationTestBase
{
    [Fact]
    public void ActivityLog_TabButtons_HaveIds()
    {
        var cut = RenderComponent<TmActivityLog>(p => p
            .Add(x => x.ShowTimeline, true)
            .Add(x => x.ShowComments, true)
            .Add(x => x.ShowAttachments, true)
            .Add(x => x.TimelineEntries, Array.Empty<ITimelineEntry>())
            .Add(x => x.Comments, Array.Empty<ICommentEntry>())
            .Add(x => x.Attachments, Array.Empty<IFileAttachment>()));

        var timelineTab = cut.Find("button#tm-activity-tab-timeline");
        timelineTab.Should().NotBeNull();
        timelineTab.GetAttribute("role").Should().Be("tab");

        var commentsTab = cut.Find("button#tm-activity-tab-comments");
        commentsTab.Should().NotBeNull();
        commentsTab.GetAttribute("role").Should().Be("tab");

        var attachmentsTab = cut.Find("button#tm-activity-tab-attachments");
        attachmentsTab.Should().NotBeNull();
        attachmentsTab.GetAttribute("role").Should().Be("tab");
    }

    [Fact]
    public void ActivityLog_TabPanel_HasAriaLabelledBy()
    {
        var cut = RenderComponent<TmActivityLog>(p => p
            .Add(x => x.ShowTimeline, true)
            .Add(x => x.ShowComments, true)
            .Add(x => x.ShowAttachments, true)
            .Add(x => x.TimelineEntries, Array.Empty<ITimelineEntry>())
            .Add(x => x.Comments, Array.Empty<ICommentEntry>())
            .Add(x => x.Attachments, Array.Empty<IFileAttachment>()));

        var panel = cut.Find("div[role='tabpanel']");
        panel.GetAttribute("aria-labelledby").Should().Be("tm-activity-tab-timeline");
    }
}
