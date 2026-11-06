using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionTemplateGalleryTests : LocalizationTestBase
{
    [Fact]
    public void RendersTemplatesWithCategoriesAndSearch()
    {
        var cut = RenderGallery(new FakeTemplateProvider(SampleTemplates()));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-ntg__card").Should().HaveCount(3);
            cut.Markup.Should().Contain("Meeting notes");
            cut.Markup.Should().Contain("Project plan");
            cut.Markup.Should().Contain("Team");
            cut.Markup.Should().Contain("Planning");
        });

        cut.Find(".tm-ntg__search-input").Input("project");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-ntg__card").Should().ContainSingle();
            cut.Find(".tm-ntg__card-title").TextContent.Should().Contain("Project plan");
        });
    }

    [Fact]
    public async Task SelectsTemplateFromFilteredCategory()
    {
        NotionTemplateDto? selected = null;
        var cut = RenderGallery(
            new FakeTemplateProvider(SampleTemplates()),
            EventCallback.Factory.Create<NotionTemplateDto>(this, value => selected = value));

        cut.WaitForAssertion(() => cut.FindAll(".tm-ntg__category").Should().NotBeEmpty());
        await cut.FindAll(".tm-ntg__category")
            .Single(button => button.TextContent.Trim() == "Planning")
            .ClickAsync(new MouseEventArgs());

        cut.FindAll(".tm-ntg__card").Should().ContainSingle();
        await cut.Find("[data-template-id='project-plan'] .tm-ntg__use").ClickAsync(new MouseEventArgs());

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("project-plan");
    }

    [Fact]
    public void ShowsNoResultsForEmptySearchAndEmptyCategory()
    {
        var cut = RenderGallery(new FakeTemplateProvider(SampleTemplates()));

        cut.WaitForAssertion(() => cut.FindAll(".tm-ntg__category").Should().NotBeEmpty());
        cut.Find(".tm-ntg__search-input").Input("does-not-exist");

        cut.WaitForAssertion(() =>
            cut.Find(".tm-ntg__state").TextContent.Should().Contain("No templates match"));

        cut.Find(".tm-ntg__search-input").Input(string.Empty);
        cut.FindAll(".tm-ntg__category")
            .Single(button => button.TextContent.Trim() == "Knowledge")
            .Click();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-ntg__state").TextContent.Should().Contain("No templates match"));
    }

    [Fact]
    public async Task Escape_RaisesOnClosed_AndApplyingShowsASpinnerOnTheUsedCardOnly()
    {
        var closed = 0;
        var gate = new TaskCompletionSource();
        var cut = Render<TmNotionTemplateGallery>(parameters => parameters
            .Add(component => component.Visible, true)
            .Add(component => component.TemplateProvider, new FakeTemplateProvider(SampleTemplates()))
            .Add(component => component.OnClosed, EventCallback.Factory.Create(this, () => closed++))
            .Add(component => component.OnTemplateSelected, EventCallback.Factory.Create<NotionTemplateDto>(this, async _ => await gate.Task)));

        cut.WaitForAssertion(() => cut.FindAll(".tm-ntg__card").Should().NotBeEmpty());
        await cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());
        closed.Should().Be(1);

        var use = cut.Find("[data-template-id='project-plan'] .tm-ntg__use").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-ntg__card .tm-spinner, .tm-ntg__card [role='status']").Should().ContainSingle());
        gate.SetResult();
        await use;
    }

    [Fact]
    public void Footer_HasACloseAction()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionTemplateGallery_Close"] = "Close"
        });
        var cut = Render<TmNotionTemplateGallery>(parameters => parameters
            .Add(component => component.Visible, true));

        cut.Find(".tm-modal-footer").TextContent.Should().Contain("Close");
    }

    [Fact]
    public void RendersOnlyBlankTemplateWhenProviderIsMissing()
    {
        var cut = Render<TmNotionTemplateGallery>(parameters => parameters
            .Add(component => component.Visible, true));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-ntg__card").Should().ContainSingle();
            cut.Find(".tm-ntg__card-title").TextContent.Should().Contain("Blank page");
        });
    }

    private IRenderedComponent<TmNotionTemplateGallery> RenderGallery(
        INotionTemplateProvider provider,
        EventCallback<NotionTemplateDto> selected = default)
        => Render<TmNotionTemplateGallery>(parameters => parameters
            .Add(component => component.Visible, true)
            .Add(component => component.TemplateProvider, provider)
            .Add(component => component.OnTemplateSelected, selected));

    private static IReadOnlyList<NotionTemplateDto> SampleTemplates() =>
    [
        new()
        {
            Id = "meeting-notes",
            Name = "Meeting notes",
            Description = "Capture agenda and actions.",
            IconEmoji = "M",
            Category = "team",
            Blocks =
            [
                new PageBlock
                {
                    Type = BlockType.Heading1,
                    Content = new HeadingBlockContent { Level = 1, Html = "Meeting notes" }
                }
            ]
        },
        new()
        {
            Id = "project-plan",
            Name = "Project plan",
            Description = "Plan milestones.",
            IconEmoji = "P",
            Category = "planning",
            Blocks =
            [
                new PageBlock
                {
                    Type = BlockType.TodoItem,
                    Content = new TodoBlockContent { Html = "Launch checklist" }
                }
            ]
        }
    ];

    private sealed class FakeTemplateProvider(IReadOnlyList<NotionTemplateDto> templates) : INotionTemplateProvider
    {
        public Task<IReadOnlyList<NotionTemplateDto>> GetTemplatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(templates);

        public Task<NotionTemplateDto?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(templates.FirstOrDefault(template =>
                string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase)));
    }
}
