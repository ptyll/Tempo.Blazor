using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Tests.Diagram;
using Tempo.Blazor.Tests.Theme;

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
    public async Task EnterKey_InInput_CommitsRename()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        var input = cut.Find(".tm-diagram-layers__name-input");
        input.Input("Nový název");
        await cut.InvokeAsync(() => input.KeyDown("Enter"));
        cut.Render();

        doc.Layers.First(l => l.Name == "Nový název").Should().NotBeNull(
            "Enter in the rename input must commit the new name");
        cut.FindAll(".tm-diagram-layers__name-input").Should().BeEmpty(
            "Enter in the rename input must end the edit");
        cut.FindAll(".tm-diagram-layers__name")
            .Should().Contain(n => n.TextContent == "Nový název");
    }

    [Fact]
    public async Task EscapeKey_InInput_CancelsRename()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        var input = cut.Find(".tm-diagram-layers__name-input");
        input.Input("Jiný název");
        await cut.InvokeAsync(() => input.KeyDown("Escape"));
        cut.Render();

        doc.Layers[1].Name.Should().Be("Annotations",
            "Escape in the rename input must discard the edit");
        cut.FindAll(".tm-diagram-layers__name-input").Should().BeEmpty(
            "Escape in the rename input must end the edit");
    }

    [Fact]
    public async Task SpaceKeyUp_InInput_DoesNotStealSpace()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        var input = cut.Find(".tm-diagram-layers__name-input");
        input.Input("My Layer");

        input.Invoking(i => i.KeyUp(" "))
            .Should().Throw<Bunit.MissingEventHandlerException>(
                "the rename input must not have a Space keyup handler — spaces in the name belong to the user");
        cut.FindAll(".tm-diagram-layers__name-input").Should().HaveCount(1,
            "Space inside the input must neither end nor restart the edit");
        doc.Layers[1].Name.Should().Be("Annotations");
    }

    [Fact]
    public void ActiveRadio_AriaLabel_ContainsLayerName()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var radios = cut.FindAll("[role='radio']").ToList();
        radios.Should().HaveCount(2);
        radios[0].GetAttribute("aria-label").Should().Contain("Base",
            "each radio's aria-label must identify its layer");
        radios[1].GetAttribute("aria-label").Should().Contain("Annotations");
        radios[0].GetAttribute("aria-label").Should().NotBe(radios[1].GetAttribute("aria-label"));
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

    [Fact]
    public void SingleStyleSource()
    {
        // The panel's styles lived twice: in the component's scoped stylesheet AND as a legacy
        // copy inside the package's global _diagram-editor.css. Two sources drift silently —
        // the scoped copy wins on specificity, so edits to the global one never showed up.
        // Freeze: every .tm-diagram-layers* rule must live in exactly ONE source, the component's
        // own TmDiagramLayersPanel.razor.css (P21 ownership).
        var src = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src");
        var excluded = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}" };
        var sources = Directory.EnumerateFiles(src, "*.css", SearchOption.AllDirectories)
            .Where(path => !excluded.Any(path.Contains))
            .Where(path => File.ReadAllText(path).Contains(".tm-diagram-layers"))
            .Select(Path.GetFileName)
            .ToList();

        sources.Should().ContainSingle("the layers panel styles must have exactly one CSS source")
            .Which.Should().Be("TmDiagramLayersPanel.razor.css");
    }
}
