using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// CF18-7: the AI menu, the mention menu and the comment-mention dropdown used to ride the
/// <c>--tm-z-popover</c> band (1030) — below <c>--tm-z-modal</c> (1040) — so any of them opened
/// inside a <c>TmModal</c> painted under the modal. They must migrate to
/// <c>TmOverlayPanel</c> (popover="manual", browser top layer), which always paints above.
/// </summary>
public sealed class TmNotionOverlayMigrationTests : LocalizationTestBase
{
    [Fact]
    public void AiMenu_WhenVisible_RendersInsideAnOverlayPanel()
    {
        var cut = Render<TmNotionAiMenu>(parameters => parameters
            .Add(p => p.Visible, true)
            .Add(p => p.Provider, Substitute.For<INotionAIProvider>()));

        var panel = cut.Find(".tm-overlay-panel");
        panel.ClassList.Should().Contain("tm-notion-ai",
            "the AI surface keeps its stylesheet hook on the panel element");
        panel.GetAttribute("role").Should().Be("dialog");
        panel.GetAttribute("aria-label").Should().NotBeNullOrEmpty(
            "a dialog-role panel must expose an accessible name");
    }

    [Fact]
    public void MentionMenu_WhenVisible_RendersInsideAnOverlayPanel()
    {
        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(p => p.Value, new NotionEditorContext())
            .AddChildContent<TmNotionMentionMenu>(menu => menu
                .Add(p => p.Visible, true)
                .Add(p => p.Top, 100)
                .Add(p => p.Left, 200)));

        var panel = host.Find(".tm-overlay-panel");
        panel.ClassList.Should().Contain("tm-nmm",
            "the mention menu keeps its stylesheet hook on the panel element");
        panel.GetAttribute("role").Should().Be("dialog");
        panel.GetAttribute("aria-label").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CommentMentionInput_OpenDropdown_RendersInsideAnOverlayPanel()
    {
        var provider = Substitute.For<ITmPeopleProvider>();
        provider.SearchAsync(Arg.Any<TmPeopleQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<TmUser>
            {
                new() { Id = "u1", DisplayName = "Ada", Email = "ada@x.cz" },
            });

        var cut = Render<TmCommentMentionInput>(parameters => parameters
            .Add(p => p.MentionProvider, provider)
            .Add(p => p.Value, string.Empty));

        cut.Find("textarea").Input("@ad");

        cut.WaitForAssertion(() => cut.Find(".tm-overlay-panel")
            .ClassList.Should().Contain("tm-comment-mention-dropdown"));
    }
}
