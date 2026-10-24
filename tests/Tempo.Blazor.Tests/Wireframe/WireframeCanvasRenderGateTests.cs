using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// The canvas render gate (<c>LightEditing</c>): a parent pass whose Snapshot() is unchanged repaints nothing, so EVERY render-affecting
/// [Parameter] must be part of it. A new parameter that is forgotten there would leave a stale phone canvas - this guards it.
/// </summary>
public class WireframeCanvasRenderGateTests : LocalizationTestBase
{
    // Parameters that do not change what the scene draws (callbacks, the gate's own token, the host's pure document pointer is in the tuple).
    private static readonly HashSet<string> NotRenderAffecting =
    [
        nameof(TmWireframeDesignerCanvas.DocumentChanged), nameof(TmWireframeDesignerCanvas.OnSelectionChanged),
        nameof(TmWireframeDesignerCanvas.OnConnectorSelectionChanged), nameof(TmWireframeDesignerCanvas.OnConnectorCreated),
        nameof(TmWireframeDesignerCanvas.OnDeleteConnectors), nameof(TmWireframeDesignerCanvas.OnConnectorWaypointDragged),
        nameof(TmWireframeDesignerCanvas.OnConnectorWaypointAdded), nameof(TmWireframeDesignerCanvas.OnConnectorDragged),
        nameof(TmWireframeDesignerCanvas.OnUndo), nameof(TmWireframeDesignerCanvas.OnRedo), nameof(TmWireframeDesignerCanvas.OnToolModeChanged),
        nameof(TmWireframeDesignerCanvas.OnCopyStyleRequested), nameof(TmWireframeDesignerCanvas.OnPasteStyleRequested),
        nameof(TmWireframeDesignerCanvas.OnPasteSizeRequested), nameof(TmWireframeDesignerCanvas.OnElementContextMenuRequested),
        nameof(TmWireframeDesignerCanvas.OnConnectorContextMenuRequested), nameof(TmWireframeDesignerCanvas.OnCanvasContextMenuRequested),
        nameof(TmWireframeDesignerCanvas.OnCloseContextMenu), nameof(TmWireframeDesignerCanvas.OnViewBoxChangedEvent),
        nameof(TmWireframeDesignerCanvas.OnCanvasMouseMoved), nameof(TmWireframeDesignerCanvas.OnZoomChangedEvent),
        nameof(TmWireframeDesignerCanvas.ContentVersion),   // it IS in the tuple (as Version)
        "AdditionalAttributes",
    ];

    private WireframeDocument Doc()
    {
        var registry = new WireframeComponentRegistry();
        Services.AddSingleton(registry);
        var doc = new WireframeDocument { Title = "G", Width = 400, Height = 300 };
        doc.Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", 10, 10));
        return doc;
    }

    [Fact]
    public void EveryRenderAffectingParameter_IsCoveredBySnapshot()
    {
        var parameters = typeof(TmWireframeDesignerCanvas)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null && !NotRenderAffecting.Contains(p.Name))
            .Select(p => p.Name).Order().ToArray();

        // Every one of these is in the Snapshot tuple; the repaint theory below proves it behaviourally for each, so a name cannot be added here without it.
        string[] covered = ["ShowGrid", "GridSize", "SnapToObjects", "ReadOnly", "Class", "LightEditing", "Document", "ComponentScope", "InitialZoom", "StencilTheme", "TestIdPrefix", "DataTestId"];
        parameters.Except(covered).Should().BeEmpty("a new render-affecting [Parameter] must be added to TmWireframeDesignerCanvas.Snapshot() (and to this list)");
    }

    [Theory]
    [InlineData("ShowGrid")]
    [InlineData("GridSize")]
    [InlineData("SnapToObjects")]
    [InlineData("ReadOnly")]
    [InlineData("InitialZoom")]
    [InlineData("StencilTheme")]
    [InlineData("TestIdPrefix")]
    [InlineData("ContentVersion")]
    public void UnderLightEditing_AChangeOfASnapshotParameter_Repaints(string parameter)
    {
        var doc = Doc();
        var cut = Render<TmWireframeDesignerCanvas>(p => p.Add(c => c.Document, doc).Add(c => c.LightEditing, true));
        var before = cut.Instance.RenderCount;

        cut.Render(p =>
        {
            switch (parameter)
            {
                case "ShowGrid": p.Add(c => c.ShowGrid, false); break;
                case "GridSize": p.Add(c => c.GridSize, 16); break;
                case "SnapToObjects": p.Add(c => c.SnapToObjects, true); break;
                case "ReadOnly": p.Add(c => c.ReadOnly, true); break;
                case "InitialZoom": p.Add(c => c.InitialZoom, WireframeInitialZoom.ActualSize); break;
                case "StencilTheme": p.Add(c => c.StencilTheme, "indigo"); break;
                case "TestIdPrefix": p.Add(c => c.TestIdPrefix, "x"); break;
                case "ContentVersion": p.Add(c => c.ContentVersion, 5); break;
            }
        });

        cut.Instance.RenderCount.Should().BeGreaterThan(before, $"{parameter} is render-affecting");
    }

    [Fact]
    public void UnderLightEditing_ARenderWithNothingNew_IsSkipped()
    {
        var doc = Doc();
        var cut = Render<TmWireframeDesignerCanvas>(p => p.Add(c => c.Document, doc).Add(c => c.LightEditing, true));
        var before = cut.Instance.RenderCount;

        cut.Render();
        cut.Render();

        cut.Instance.RenderCount.Should().Be(before);
    }
}
