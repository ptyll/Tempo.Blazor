using Bunit;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class TmModelingModelTreeTests : LocalizationTestBase
{
    [Fact]
    public void Renders_node_for_each_model_element()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(3);
    }

    [Fact]
    public void Filter_narrows_visible_nodes_to_matches()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-search']").Input("approve");

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(1);
        cut.Find("[data-testid='modeling-tree-node-task-approve']").TextContent.Should().Contain("Approve request");
    }

    [Fact]
    public void Empty_filter_restores_all_nodes()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-search']").Input("approve");
        cut.Find("[data-testid='modeling-tree-search']").Input(string.Empty);

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().HaveCount(3);
    }

    [Fact]
    public void Non_matching_filter_shows_empty_filter_state()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements()));

        cut.Find("[data-testid='modeling-tree-search']").Input("xxxx");

        cut.FindAll("[data-testid^='modeling-tree-node-']").Should().BeEmpty();
        cut.Find("[data-testid='modeling-tree-empty-filter']").TextContent.Should().Contain("xxxx");
    }

    [Fact]
    public void Clicking_node_emits_selected_element()
    {
        ModelingElementDto? selected = null;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnElementSelected, EventCallback.Factory.Create<ModelingElementDto>(this, element => selected = element)));

        cut.Find("[data-testid='modeling-tree-node-task-review']").Click();

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("task-review");
    }

    [Fact]
    public void Enter_on_node_selects_exactly_once()
    {
        // Native <button role="treeitem"> sequence: Enter produces the click on
        // keydown — keydown -> click. A keydown handler that also selected the
        // element fired OnElementSelected twice for one key press.
        var selections = new List<ModelingElementDto>();
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnElementSelected, EventCallback.Factory.Create<ModelingElementDto>(this, element => selections.Add(element))));

        var node = cut.Find("[data-testid='modeling-tree-node-task-review']");
        node.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.Find("[data-testid='modeling-tree-node-task-review']").Click();

        selections.Select(e => e.Id).Should().Equal("task-review");
    }

    [Fact]
    public void Dragstart_exposes_element_id_for_data_transfer_bridge()
    {
        ModelingElementDto? dragged = null;
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, CreateElements())
            .Add(p => p.OnElementDragStarted, EventCallback.Factory.Create<ModelingElementDto>(this, element => dragged = element)));

        var node = cut.Find("[data-testid='modeling-tree-node-task-review']");
        node.GetAttribute("draggable").Should().Be("true");
        node.GetAttribute("data-modeling-drag-element-id").Should().Be("task-review");
        JSInterop.Invocations.Any(invocation =>
            invocation.Identifier == "eval"
            && invocation.Arguments.Any(argument => argument?.ToString()?.Contains("dataTransfer.setData") == true))
            .Should().BeTrue();

        node.TriggerEvent("ondragstart", new Microsoft.AspNetCore.Components.Web.DragEventArgs());

        dragged.Should().NotBeNull();
        dragged!.Id.Should().Be("task-review");
    }

    [Fact]
    public void Empty_model_shows_message_instead_of_blank_container()
    {
        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, Array.Empty<ModelingElementDto>()));

        cut.Find("[data-testid='modeling-model-tree']").Should().NotBeNull();
        cut.Find("[data-testid='modeling-tree-empty']").TextContent.Should().Contain("No elements");
    }

    [Fact]
    public void Empty_name_renders_unnamed_fallback()
    {
        var elements = CreateElements().Append(new ModelingElementDto
        {
            Id = "empty-name",
            SemanticType = "task",
            Name = string.Empty
        }).ToArray();

        using var cut = Render<TmModelingModelTree>(parameters => parameters
            .Add(p => p.Elements, elements));

        cut.Find("[data-testid='modeling-tree-node-empty-name']").TextContent.Should().Contain("(Unnamed)");
    }

    private static ModelingElementDto[] CreateElements() =>
    [
        new()
        {
            Id = "task-approve",
            SourceId = "source/task-approve",
            SourceType = "task",
            SourcePath = "/Requests/Approve",
            SemanticType = "userTask",
            Name = "Approve request",
            Description = "Approve the request.",
            Tags = ["workflow", "approval"]
        },
        new()
        {
            Id = "task-review",
            SourceId = "source/task-review",
            SourceType = "task",
            SourcePath = "/Requests/Review",
            SemanticType = "userTask",
            Name = "Review request",
            Description = "Review the request.",
            Tags = ["workflow", "review"]
        },
        new()
        {
            Id = "system-customer",
            SourceId = "source/system-customer",
            SourceType = "system",
            SourcePath = "/Systems/Customer",
            SemanticType = "applicationComponent",
            Name = "Customer portal",
            Description = "Customer application.",
            Tags = ["portal"]
        }
    ];
}
