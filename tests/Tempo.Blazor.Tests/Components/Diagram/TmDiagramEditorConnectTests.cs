using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramEditorConnectTests : LocalizationTestBase
{
    /// <summary>
    /// Registers the built-in stencil set on first use. Deliberately NOT in the constructor:
    /// resolving a service there locks the BunitContext provider, and the localized-name test
    /// must swap the ITmLocalizer before any resolution happens.
    /// </summary>
    private void EnsureBuiltInStencils()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        if (registry.Count == 0)
            registry.RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public async Task ConnectArrowClick_OpensStencilModal()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        // Select node so arrows appear
        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();

        // Click east arrow
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // Modal should be visible
        cut.Find(".tm-modal").Should().NotBeNull();
        cut.Find(".tm-modal-title").TextContent.Should().Contain("Select shape to connect");
    }

    [Fact]
    public async Task ModalStencilSelection_AddsNodeAndEdge()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();

        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // Pick a stencil from the source node's palette (DI4: the default offer is the
        // source stencil's own set, so pick from the dialog's palette directly).
        var firstStencil = cut.Find(".tm-diagram-connect-modal__item");
        firstStencil.Click();
        cut.Render();

        // Document should now have 2 nodes and 1 edge
        doc.Nodes.Count.Should().Be(2);
        doc.Edges.Count.Should().Be(1);

        var edge = doc.Edges[0];
        edge.SourceNodeId.Should().Be(doc.Nodes[0].Id);
        edge.TargetNodeId.Should().Be(doc.Nodes[1].Id);
    }

    [Fact]
    public async Task EdgeToEdgeCreation_SetsTargetEdgeIdAndT()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        var n1 = new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 40, H = 40 };
        var n2 = new DiagramNode { StencilId = "general.rectangle", X = 200, Y = 100, W = 40, H = 40 };
        doc.Nodes.Add(n1);
        doc.Nodes.Add(n2);

        var targetEdge = new DiagramEdge { SourceNodeId = n1.Id, TargetNodeId = n2.Id };
        doc.Edges.Add(targetEdge);

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await cut.InvokeAsync(async () => await canvas.Instance.JsOnEdgeCreated(n1.Id, null, null, null, "right", 0.5, null, 0.5, targetEdge.Id, 0.75));

        doc.Edges.Count.Should().Be(2);
        var newEdge = doc.Edges[1];
        newEdge.SourceNodeId.Should().Be(n1.Id);
        newEdge.TargetNodeId.Should().BeNull();
        newEdge.TargetEdgeId.Should().Be(targetEdge.Id);
        newEdge.TargetEdgeT.Should().Be(0.75);
    }

    [Fact]
    public async Task ConnectModal_DefaultsToSourceNodePalette()
    {
        EnsureBuiltInStencils();
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // DI4: the default offer is the source node's own set/palette — node stencils sharing
        // the source's set (declared SetId, else the id's family prefix) — never the whole
        // ~158-stencil library and never edge stencils.
        static string Scope(DiagramStencil s) =>
            !string.IsNullOrEmpty(s.SetId)
                ? s.SetId
                : s.Id.IndexOf('.') is > 0 and var dot ? s.Id[..dot] : s.Id;

        var sourceStencil = registry.GetStencil("general.rectangle")!;
        var paletteIds = registry.GetAll()
            .Where(s => s.Kind == DiagramStencilKind.Node && Scope(s) == Scope(sourceStencil))
            .Select(s => s.Id)
            .ToList();
        var allNodeCount = registry.GetAll().Count(s => s.Kind == DiagramStencilKind.Node);
        paletteIds.Count.Should().BeLessThan(allNodeCount,
            "the default palette must be narrower than the whole node library");

        var items = cut.FindAll(".tm-diagram-connect-modal__item").ToList();
        items.Count.Should().Be(paletteIds.Count,
            "by default the modal offers exactly the node stencils of the source stencil's set");
    }

    [Fact]
    public async Task ConnectModal_ShowAll_ListsAllNodeStencils()
    {
        EnsureBuiltInStencils();
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        var showAll = cut.Find(".tm-diagram-connect-modal__show-all");
        showAll.Click();
        cut.Render();

        var allNodeCount = registry.GetAll().Count(s => s.Kind == DiagramStencilKind.Node);
        var items = cut.FindAll(".tm-diagram-connect-modal__item").ToList();
        items.Count.Should().Be(allNodeCount,
            "'Show all' is the explicit opt-in to the whole node library");

        // Edge stencils stay out even in the full list.
        var edgeNames = registry.GetAll()
            .Where(s => s.Kind == DiagramStencilKind.Edge)
            .Select(s => s.Name)
            .ToList();
        foreach (var edgeName in edgeNames)
        {
            items.Should().NotContain(i => i.TextContent.Contains(edgeName),
                $"edge stencil '{edgeName}' must not appear even after 'Show all'");
        }
    }

    [Fact]
    public async Task ConnectModal_SearchFilters()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        cut.Find(".tm-diagram-connect-modal__search").Input("cylinder");
        cut.Render();

        var labels = cut.FindAll(".tm-diagram-connect-modal__label")
            .Select(l => l.TextContent.Trim()).ToList();
        labels.Should().NotBeEmpty("the palette search must find the queried stencil");
        labels.Should().OnlyContain(l => l.Contains("Cylinder", StringComparison.OrdinalIgnoreCase),
            "searching narrows the offer to the matching stencils");
    }

    [Fact]
    public async Task ConnectModal_ArrowKeysMoveFocus()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        var tiles = cut.FindAll(".tm-diagram-connect-modal__item").ToList();
        tiles.Count.Should().BeGreaterThan(1);

        // Roving tabindex: exactly one tile is tabbable and the arrows move the tab stop.
        tiles[0].GetAttribute("tabindex").Should().Be("0", "the first tile starts as the tab stop");
        tiles.Skip(1).Should().OnlyContain(t => t.GetAttribute("tabindex") == "-1");

        tiles[0].KeyDown("ArrowRight");
        cut.Render();

        cut.FindAll(".tm-diagram-connect-modal__item").ToList()[1]
            .GetAttribute("tabindex").Should().Be("0",
                "ArrowRight moves the roving tab stop to the next tile");
        cut.FindAll(".tm-diagram-connect-modal__item").ToList()[0]
            .GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void ConnectModal_MobilePresentationSheet()
    {
        EnsureBuiltInStencils();

        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile));

        // Forced mobile layout presents the modal as a bottom sheet (foundation F2).
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--sheet");

        // The mobile presentation offers list rows and an explicit Close action.
        cut.FindAll(".tm-diagram-connect-modal__item").Should().NotBeEmpty();
        var close = cut.Find(".tm-diagram-connect-modal__close");
        close.TextContent.Should().Contain("Close");
    }

    [Fact]
    public async Task ConnectModal_ExcludesEdgeStencils()
    {
        EnsureBuiltInStencils();
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // Opt in to the whole library, then verify no edge stencil ever appears.
        cut.Find(".tm-diagram-connect-modal__show-all").Click();
        cut.Render();

        var nodeStencilCount = registry.GetAll().Count(s => s.Kind == DiagramStencilKind.Node);
        nodeStencilCount.Should().BeGreaterThan(0);

        var items = cut.FindAll(".tm-diagram-connect-modal__item").ToList();
        items.Count.Should().Be(nodeStencilCount,
            "the connect modal must offer exactly the node stencils — edge stencils are not connectable shapes");

        var edgeStencilNames = registry.GetAll()
            .Where(s => s.Kind == DiagramStencilKind.Edge)
            .Select(s => s.Name)
            .ToList();
        foreach (var edgeName in edgeStencilNames)
        {
            items.Should().NotContain(i => i.TextContent.Contains(edgeName),
                $"edge stencil '{edgeName}' must not appear in the connect modal");
        }
    }

    [Fact]
    public void StencilDisplayName_UsesResourceKeyWithFallback()
    {
        var localizer = new MockTmLocalizer(new Dictionary<string, string>
        {
            ["TestStencil_Localized"] = "Lokalizovaný tvar"
        });
        var keyed = new DiagramStencil { Id = "test.keyed", Name = "Fallback Name", NameResourceKey = "TestStencil_Localized" };
        var fallback = new DiagramStencil { Id = "test.plain", Name = "Fallback Name" };

        DiagramStencilDisplayNames.GetStencilDisplayName(keyed, localizer).Should().Be("Lokalizovaný tvar");
        DiagramStencilDisplayNames.GetStencilDisplayName(fallback, localizer).Should().Be("Fallback Name");
    }

    /// <summary>
    /// Mirrors the production <see cref="Tempo.Blazor.Localization.DefaultTmLocalizer"/>, which
    /// echoes the raw key (no brackets) for missing translations — unlike the bracket-wrapping
    /// <see cref="MockTmLocalizer"/>. The stencil display-name helper must treat that echo as
    /// "missing" too and fall back to the stencil's raw name.
    /// </summary>
    private sealed class RawKeyEchoLocalizer(Dictionary<string, string> data) : Tempo.Blazor.Localization.ITmLocalizer
    {
        public System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.GetCultureInfo("en");

        public string this[string key] => data.TryGetValue(key, out var v) ? v : key;

        public string this[string key, params object[] arguments]
        {
            get
            {
                var template = data.TryGetValue(key, out var v) ? v : key;
                return arguments.Length > 0 ? string.Format(Culture, template, arguments) : template;
            }
        }
    }

    [Fact]
    public void StencilDisplayName_FallsBack_WhenLocalizerEchoesRawKey()
    {
        var localizer = new RawKeyEchoLocalizer(new Dictionary<string, string>
        {
            ["TestStencil_Localized"] = "Lokalizovaný tvar"
        });
        var missing = new DiagramStencil { Id = "test.raw", Name = "Fallback Name", NameResourceKey = "TestStencil_Missing" };

        DiagramStencilDisplayNames.GetStencilDisplayName(missing, localizer).Should().Be("Fallback Name",
            "a localizer echoing the raw key must be treated as untranslated");
        DiagramStencilDisplayNames.GetLocalizedText(null, "Fallback Name", localizer).Should().Be("Fallback Name");
        DiagramStencilDisplayNames.GetLocalizedText("TestStencil_Localized", "Fallback Name", localizer)
            .Should().Be("Lokalizovaný tvar");
    }

    [Fact]
    public async Task ConnectModal_UsesLocalizedStencilNames()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TestStencil_Localized"] = "Lokalizovaný tvar"
        });
        EnsureBuiltInStencils();
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "test.localized",
            Name = "Fallback Name",
            NameResourceKey = "TestStencil_Localized",
            Category = "General",
            Kind = DiagramStencilKind.Node,
            Origin = DiagramStencilOrigin.TempoOriginal
        });

        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // The custom stencil lives in a different set, so opt into the full list to see it.
        cut.Find(".tm-diagram-connect-modal__show-all").Click();
        cut.Render();

        var labels = cut.FindAll(".tm-diagram-connect-modal__label").Select(l => l.TextContent.Trim()).ToList();
        labels.Should().Contain("Lokalizovaný tvar",
            "the connect modal must show localized stencil names through the shared helper");
        labels.Should().NotContain(l => l.StartsWith('['),
            "no raw resource key may leak into the connect modal");
    }
}
