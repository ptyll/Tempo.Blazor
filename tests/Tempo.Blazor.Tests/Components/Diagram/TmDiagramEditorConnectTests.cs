using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
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

        // Modal should be visible; a node without its own text names the source by its stencil.
        cut.Find(".tm-modal").Should().NotBeNull();
        cut.Find(".tm-modal-title").TextContent.Should().Contain("Connect from");
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

    // ── D1 / A2: recents are per editor, not a shared static list ────────────

    [Fact]
    public async Task TwoEditors_DoNotShareRecentStencils()
    {
        EnsureBuiltInStencils();
        DiagramDocument NewDoc()
        {
            var doc = new DiagramDocument();
            doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });
            return doc;
        }

        var first = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, NewDoc())
            .Add(e => e.ReadOnly, false));
        var second = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, NewDoc())
            .Add(e => e.ReadOnly, false));

        // Open the connect dialog in the first editor and remember the offer order.
        var firstCanvas = first.FindComponent<TmDiagramCanvas>();
        await firstCanvas.Instance.SetSelection(first.Instance.Document.Nodes[0].Id);
        first.Render();
        first.Find(".tm-diagram-connect-arrow--e").Click();
        first.Render();
        var before = first.FindAll(".tm-diagram-connect-modal__item")
            .Select(i => i.GetAttribute("data-stencil-id"))
            .ToList();

        // Pick the LAST tile so the recents reorder is observable.
        var lastId = before[^1];
        first.FindAll(".tm-diagram-connect-modal__item")[^1].Click();
        first.Render();

        // Reopen: the picked stencil leads the offer in the first editor.
        first.Find(".tm-diagram-connect-arrow--e").Click();
        first.Render();
        first.Find(".tm-diagram-connect-modal__item")
            .GetAttribute("data-stencil-id").Should().Be(lastId,
                "the first editor's recents lead its own connect offer");

        // The second editor keeps the untouched order — nothing leaked across instances.
        var secondCanvas = second.FindComponent<TmDiagramCanvas>();
        await secondCanvas.Instance.SetSelection(second.Instance.Document.Nodes[0].Id);
        second.Render();
        second.Find(".tm-diagram-connect-arrow--e").Click();
        second.Render();
        second.FindAll(".tm-diagram-connect-modal__item")
            .Select(i => i.GetAttribute("data-stencil-id"))
            .Should().Equal(before, "a pick in one editor must never reorder another editor's offer");
    }

    [Fact]
    public async Task RecentStencilIdsChanged_IsRaisedAfterConnect()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        IReadOnlyList<string>? captured = null;
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false)
            .Add(e => e.RecentStencilIdsChanged, ids => captured = ids));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        var pickedId = cut.Find(".tm-diagram-connect-modal__item").GetAttribute("data-stencil-id");
        cut.Find(".tm-diagram-connect-modal__item").Click();
        cut.Render();

        captured.Should().NotBeNull();
        captured!.Should().Equal(new[] { pickedId }, "the recents callback carries the picked stencil first");
    }

    [Fact]
    public async Task HostRecentStencilIds_OrderTheConnectOffer()
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

        var lastId = cut.FindAll(".tm-diagram-connect-modal__item")[^1].GetAttribute("data-stencil-id");

        // A host-bound recents list leads the offer when the dialog opens.
        cut.Render(p => p.Add(e => e.RecentStencilIds, new[] { lastId }));
        cut.Find(".tm-diagram-connect-modal__item")
            .GetAttribute("data-stencil-id").Should().Be(lastId,
                "host-provided recents order the connect offer");
    }

    [Fact]
    public async Task OneWayRecents_SurviveAnUnrelatedRerender_BeforeTheBindingFeedsBack()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        // One-way usage: the host seeds the recents once and does NOT feed the changed callback
        // back (no @bind, no RecentStencilIdsChanged handler).
        var hostRecents = new[] { "general.ellipse" };
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false)
            .Add(e => e.RecentStencilIds, hostRecents));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        var pickedId = cut.Find(".tm-diagram-connect-modal__item").GetAttribute("data-stencil-id");
        cut.Find(".tm-diagram-connect-modal__item").Click();
        cut.Render();

        // An unrelated parameter pass with the SAME host instance arrives before any binding
        // feedback: the just-added recent must not be clobbered by re-adopting the seed.
        cut.Render(p => p.Add(e => e.RecentStencilIds, hostRecents));

        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();
        cut.Find(".tm-diagram-connect-modal__item")
            .GetAttribute("data-stencil-id").Should().Be(pickedId,
                "a stale re-render must not wipe the recent added after the host seeded the list");
    }

    [Fact]
    public async Task HostPinnedRecentStencilIds_AreAdoptedOnce_AndTheHostArrayIsNeverMutated()
    {
        // The reference guard in OnParametersSet: a host that pins RecentStencilIds to a fixed
        // instance (ignoring the callback) owns the SEED — the editor adopts it once and never
        // mutates the pinned array. Local picks still evolve the editor's own offer order, a
        // re-render with the SAME instance never re-seeds (no clobbering oscillation), and a
        // NEW host reference is adopted.
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 100, Y = 100, W = 120, H = 60 });

        var pinned = new[] { "general.cylinder" };
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false)
            .Add(e => e.RecentStencilIds, pinned)
            .Add(e => e.RecentStencilIdsChanged, _ => { }));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        // The pinned seed leads the offer, so pick the SECOND tile.
        var tiles = cut.FindAll(".tm-diagram-connect-modal__item");
        tiles[0].GetAttribute("data-stencil-id").Should().Be("general.cylinder",
            "the pinned host list seeds the offer");
        var pickedId = tiles[1].GetAttribute("data-stencil-id");
        tiles[1].Click();
        cut.Render();

        pinned.Should().Equal(new[] { "general.cylinder" }, "the editor must never mutate the host's array");

        // A re-render with the SAME pinned instance: no re-seed, the local pick leads the offer.
        cut.Render(p => p.Add(e => e.RecentStencilIds, pinned));
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();
        cut.Find(".tm-diagram-connect-modal__item")
            .GetAttribute("data-stencil-id").Should().Be(pickedId,
                "the same host instance is adopted once; the local recents keep their order");

        // The host replaces the reference: the new seed wins.
        cut.Render(p => p.Add(e => e.RecentStencilIds, new[] { "general.hexagon" }));
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();
        cut.Find(".tm-diagram-connect-modal__item")
            .GetAttribute("data-stencil-id").Should().Be("general.hexagon",
                "a new host reference is adopted as the new seed");
    }

    [Fact]
    public void ConnectDialog_HasNoStaticCollectionState()
    {
        typeof(TmDiagramConnectDialog)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Should().NotContain(f =>
                    typeof(System.Collections.IEnumerable).IsAssignableFrom(f.FieldType)
                    && f.FieldType != typeof(string),
                "the connect dialog must not keep shared static collection state (D1/A2)");
    }

    // ── D3 / A7: reopening the dialog resets it ───────────────────────────────

    [Fact]
    public void ConnectModal_Reopen_ResetsSearchShowAllAndFocus()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__search").Input("cyl");
        cut.Find(".tm-diagram-connect-modal__show-all").Click();
        cut.Find(".tm-diagram-connect-modal__item").KeyDown("ArrowRight");
        cut.Render();

        cut.Render(p => p.Add(x => x.Show, false));
        cut.Render(p => p.Add(x => x.Show, true));

        ((AngleSharp.Html.Dom.IHtmlInputElement)cut.Find(".tm-diagram-connect-modal__search")).Value
            .Should().BeEmpty("a hidden dialog forgets its search");
        cut.Find(".tm-diagram-connect-modal__show-all").GetAttribute("aria-pressed")
            .Should().Be("false", "a hidden dialog forgets its show-all scope");
        cut.Find(".tm-diagram-connect-modal__item").GetAttribute("tabindex")
            .Should().Be("0", "the roving tab stop returns to the first tile");
    }

    // ── D2: real focus movement, row-wise arrows, layout-keyed columns ────────

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static string? TileRefId(TmDiagramConnectDialog dialog, string stencilId) =>
        ((Dictionary<string, Microsoft.AspNetCore.Components.ElementReference>)typeof(TmDiagramConnectDialog)
            .GetField("_tileRefs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(dialog)!)[stencilId].Id;

    [Fact]
    public void ConnectModal_ArrowDown_MovesOneRow_OnDesktop()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        var tiles = cut.FindAll(".tm-diagram-connect-modal__item").ToList();
        tiles.Count.Should().BeGreaterThan(3);

        tiles[0].KeyDown("ArrowDown");
        cut.Render();

        cut.FindAll(".tm-diagram-connect-modal__item").ToList()[3]
            .GetAttribute("tabindex").Should().Be("0",
                "ArrowDown moves one ROW (3 columns), not one tile");

        // DOM focus follows the roving tab stop.
        var movedId = cut.FindAll(".tm-diagram-connect-modal__item").ToList()[3].GetAttribute("data-stencil-id");
        var refId = TileRefId(cut.Instance, movedId!);
        refId.Should().NotBeNullOrWhiteSpace();
        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations
                .Where(i => i.Identifier == FocusInvocation)
                .SelectMany(i => i.Arguments.OfType<Microsoft.AspNetCore.Components.ElementReference>())
                .Select(er => er.Id)
                .Should().Contain(refId, "OnAfterRenderAsync must move DOM focus to the new tile");
        });
    }

    [Fact]
    public void ConnectModal_ArrowDown_MovesOneTile_InMobileList()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile));

        cut.Find(".tm-diagram-connect-modal__grid").ClassList
            .Should().Contain("tm-diagram-connect-modal__grid--list",
                "the mobile sheet keys the single-column layout off the resolved layout modifier");

        var tiles = cut.FindAll(".tm-diagram-connect-modal__item").ToList();
        tiles[0].KeyDown("ArrowDown");
        cut.Render();

        cut.FindAll(".tm-diagram-connect-modal__item").ToList()[1]
            .GetAttribute("tabindex").Should().Be("0",
                "in the single-column list ArrowDown moves exactly one tile");
    }

    // ── Review fixes: own placeholder, empty-state escape hatch, ArrowDown from search ──

    [Fact]
    public void ConnectModal_SearchUsesItsOwnPlaceholderKey()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__search").GetAttribute("placeholder")
            .Should().Be("Search shapes…", "the connect dialog owns its placeholder (U+2026), it does not borrow the toolbox key");
    }

    [Fact]
    public void ConnectModal_NoResults_OffersSearchAllShapes_OutsideTheLiveRegion()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__search").Input("zzz-no-match");
        cut.Render();

        var button = cut.Find(".tm-diagram-connect-modal__search-all");
        button.TextContent.Trim().Should().Be("Search all shapes");
        button.Closest(".tm-diagram-connect-modal__status").Should().BeNull(
            "an actionable button must not ride inside the role=status live region");

        // Clicking widens the scope to the whole library — the same state the "Show all" toggle
        // drives (the query stays; the offer is no longer narrowed to the source stencil set).
        button.Click();
        cut.Render();
        cut.Find(".tm-diagram-connect-modal__show-all").GetAttribute("aria-pressed").Should().Be("true");
        cut.FindAll(".tm-diagram-connect-modal__search-all").Should().BeEmpty();
    }

    [Fact]
    public void ConnectModal_ArrowDown_FromSearch_MovesFocusIntoTheGrid()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__search").KeyDown("ArrowDown");

        // DOM focus lands on the roving tile (the first one when nothing was focused yet).
        var firstId = cut.Find(".tm-diagram-connect-modal__item").GetAttribute("data-stencil-id");
        var refId = TileRefId(cut.Instance, firstId!);
        cut.WaitForAssertion(() =>
        {
            JSInterop.Invocations
                .Where(i => i.Identifier == FocusInvocation)
                .SelectMany(i => i.Arguments.OfType<Microsoft.AspNetCore.Components.ElementReference>())
                .Select(er => er.Id)
                .Should().Contain(refId, "ArrowDown from the search field must move focus into the grid");
        });
    }

    [Fact]
    public async Task ConnectModal_Hidden_ClearsTheTileReferences()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        var firstId = cut.Find(".tm-diagram-connect-modal__item").GetAttribute("data-stencil-id");
        TileRefId(cut.Instance, firstId!).Should().NotBeNullOrWhiteSpace();

        cut.Render(p => p.Add(x => x.Show, false));
        await cut.InvokeAsync(() => { });

        ((Dictionary<string, Microsoft.AspNetCore.Components.ElementReference>)typeof(TmDiagramConnectDialog)
                .GetField("_tileRefs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(cut.Instance)!)
            .Should().BeEmpty("a hidden dialog forgets its tile element references (the reopened dialog renders fresh tiles)");
    }

    [Fact]
    public void ConnectModal_RovingStop_FollowsTheStencil_NotTheIndex()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__item").KeyDown("ArrowRight");
        cut.Render();

        var followedId = cut.FindAll(".tm-diagram-connect-modal__item").ToList()[1].GetAttribute("data-stencil-id");

        // Reordering the offer (Show all) must not move the tab stop to a different stencil.
        cut.Find(".tm-diagram-connect-modal__show-all").Click();
        cut.Render();

        cut.Find($".tm-diagram-connect-modal__item[data-stencil-id='{followedId}']")
            .GetAttribute("tabindex").Should().Be("0",
                "the roving stop tracks the stencil, not a positional index");
    }

    // ── D16 / X8: legible thumbnails, "Connect from {0}" title ────────────────

    [Fact]
    public void ConnectModal_Icons_HaveNoWordLabels()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__show-all").Click();
        cut.Render();

        cut.FindAll(".tm-diagram-connect-modal__icon text")
            .Should().OnlyContain(t => t.TextContent.Trim().Length <= 2,
                "multi-character labels (stereotypes, captions) are illegible in a 40px tile and must be stripped");
    }

    [Fact]
    public async Task ConnectModal_Title_NamesTheSourceNode()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100, Y = 100, W = 120, H = 60,
            Data = new Dictionary<string, object> { ["name"] = "Customer" }
        });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();
        cut.Find(".tm-diagram-connect-arrow--e").Click();
        cut.Render();

        cut.Find(".tm-modal-title").TextContent.Should().Be("Connect from Customer");
    }

    // ── D11 / D12 / D7: accessible search, live region, diacritic search ──────

    [Fact]
    public void ConnectModal_SearchHasAccessibleName()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__search").GetAttribute("aria-label")
            .Should().Be("Search shapes to connect");
    }

    [Fact]
    public void ConnectModal_Search_MatchesLocalizedDisplayName()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TestStencil_Localized"] = "Šablonový tvar"
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

        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-diagram-connect-modal__show-all").Click();
        cut.Render();
        cut.Find(".tm-diagram-connect-modal__search").Input("sablonovy");
        cut.Render();

        var labels = cut.FindAll(".tm-diagram-connect-modal__label").Select(l => l.TextContent.Trim()).ToList();
        labels.Should().Contain("Šablonový tvar",
            "the visible localized name is searchable without diacritics (D7)");
    }

    [Fact]
    public void EmptyState_LiveRegionIsAlwaysPresent_AndUsesStencilText()
    {
        EnsureBuiltInStencils();
        var cut = Render<TmDiagramConnectDialog>(p => p
            .Add(x => x.Show, true)
            .Add(x => x.SourceStencilId, "general.rectangle")
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        var status = cut.Find(".tm-diagram-connect-modal__status");
        status.GetAttribute("role").Should().Be("status");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.TextContent.Should().BeEmpty("the live region exists before the empty state appears");

        cut.Find(".tm-diagram-connect-modal__search").Input("zzzz-no-match");
        cut.Render();

        cut.Find(".tm-diagram-connect-modal__status").TextContent.Trim()
            .Should().Be("No shapes match your search.",
                "the connect dialog uses its own stencil empty-state wording, not the gallery's");
    }

    // ── Review fix (connect arrows unreachable by finger): decorator pane order ──

    [Fact]
    public async Task DecoratorPane_ConnectArrowGroup_RendersAfterPortHits()
    {
        EnsureBuiltInStencils();
        var doc = new DiagramDocument();
        doc.Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100, Y = 100, W = 120, H = 60
        });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.ReadOnly, false));

        var canvas = cut.FindComponent<TmDiagramCanvas>();
        await canvas.Instance.SetSelection(doc.Nodes[0].Id);
        cut.Render();

        // Fixed paint order (buttons > gesture hit areas > visual handles): the visible
        // resize/rotate handles render first, the finger-sized port hit discs after them,
        // and the quick-connect arrow BUTTONS last — so a press lands on the arrows, never
        // on a port hit disc painting over them (the pre-fix defect on a coarse pointer).
        var order = cut.FindAll(".tm-diagram-decorator-pane > g").Select(g => g.ClassName).ToList();
        var handlesIndex = order.IndexOf("tm-diagram-node-handles");
        var portHitsIndex = order.IndexOf("tm-diagram-port-hits");
        var arrowsIndex = order.IndexOf("tm-diagram-connect-arrows");
        handlesIndex.Should().BeGreaterThanOrEqualTo(0);
        portHitsIndex.Should().BeGreaterThan(handlesIndex, "the port hits paint after the handles");
        arrowsIndex.Should().BeGreaterThan(portHitsIndex, "the connect arrows paint after the port hits");

        // The arrows live in their own per-node group mirroring the node transform
        // (JS keeps it in sync during drags through the generic [data-node-id] selector).
        var arrows = cut.Find("g.tm-diagram-connect-arrows");
        arrows.GetAttribute("data-node-id").Should().Be(doc.Nodes[0].Id);
        arrows.GetAttribute("transform").Should().StartWith("translate(")
            .And.Contain("rotate(0 60 32)", "the group mirrors the node's translate+rotate transform");
    }
}
