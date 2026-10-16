using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Tests.Diagram;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// bUnit tests for the layers panel: active/drag-over row classes, localized
/// new-layer names, radiogroup semantics, keyboard rename and data-testid.
/// </summary>
public class TmDiagramLayersPanelTests : DiagramTestBase
{
    private static DiagramDocument CreateDocument()
    {
        var doc = new DiagramDocument();
        doc.Layers.Add(new DiagramLayer { Name = "Base", Order = 0 });
        doc.Layers.Add(new DiagramLayer { Name = "Annotations", Order = 1 });
        return doc;
    }

    [Fact]
    public void ActiveRow_HasActiveClass()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var activeRow = cut.Find($"[data-layer-id='{doc.Layers[1].Id}']");
        activeRow.ClassList.Should().Contain("tm-diagram-layers__item--active");

        var inactiveRow = cut.Find($"[data-layer-id='{doc.Layers[0].Id}']");
        inactiveRow.ClassList.Should().NotContain("tm-diagram-layers__item--active");
    }

    [Fact]
    public void DragOverRow_HasDragOverClass()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var dragged = cut.Find($"[data-layer-id='{doc.Layers[0].Id}']");
        dragged.TriggerEvent("ondragstart", new Microsoft.AspNetCore.Components.Web.DragEventArgs());

        var target = cut.Find($"[data-layer-id='{doc.Layers[1].Id}']");
        target.TriggerEvent("ondragenter", new Microsoft.AspNetCore.Components.Web.DragEventArgs());
        cut.Render();

        target.ClassList.Should().Contain("tm-diagram-layers__item--drag-over");
    }

    [Fact]
    public void NewLayer_HasLocalizedName()
    {
        UseCzechLocalization();
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();

        var names = cut.FindAll(".tm-diagram-layers__name").Select(n => n.TextContent.Trim()).ToList();
        names.Should().Contain("Nová vrstva 3",
            "the new layer name must come from TmDiagramLayers_NewLayerName with a {0} ordinal");
        names.Should().NotContain("Layer 3");
    }

    [Fact]
    public void ActiveSelector_IsRadioGroup_WithCheckedRadios()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var group = cut.Find(".tm-diagram-layers__list");
        group.GetAttribute("role").Should().Be("radiogroup");

        var radios = cut.FindAll("[role='radio']").ToList();
        radios.Should().HaveCount(2);
        radios.First(r => r.GetAttribute("aria-checked") == "true").Should().NotBeNull();
        radios.Count(r => r.GetAttribute("aria-checked") == "false").Should().Be(1);
    }

    [Fact]
    public async Task EnterKey_And_F2_StartRename()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        nameSpan.GetAttribute("tabindex").Should().Be("0", "the layer name must be keyboard focusable");

        await cut.InvokeAsync(() => nameSpan.KeyDown("F2"));
        cut.Render();
        cut.Find(".tm-diagram-layers__name-input").Should().NotBeNull();
    }

    [Fact]
    public async Task EnterKey_StartsRename_ExactlyOnce()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));

        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();
        cut.Find(".tm-diagram-layers__name-input").Should().NotBeNull();
    }

    [Fact]
    public void DataTestId_RendersOnRoot()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .Add(x => x.DataTestId, "my-layers"));

        cut.Find("[data-testid='my-layers']").Should().NotBeNull();
    }
}
