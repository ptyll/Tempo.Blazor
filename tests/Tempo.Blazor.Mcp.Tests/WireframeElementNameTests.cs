using System.Text.Json;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Mcp.Wireframe;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>Tests for the optional human-readable element <c>name</c> across the MCP wireframe tools.</summary>
public class WireframeElementNameTests
{
    private static WireframeSchemaRegistry Registry() => new([new BuiltInComponentSchemas()]);
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static WireframeDocument NewDoc()
    {
        var doc = new WireframeDocument();
        doc.EnsureActivePage();
        return doc;
    }

    // ── Operation engine ─────────────────────────────────────────────────────────

    [Fact]
    public void Engine_AddElement_WithName_SetsName()
    {
        var doc = NewDoc();

        var result = WireframeOperationEngine.Apply(
            doc,
            "[{\"op\":\"addElement\",\"type\":\"TmButton\",\"name\":\"Login button\",\"x\":10,\"y\":10,\"w\":100,\"h\":40}]",
            Registry());

        result.Success.Should().BeTrue(string.Join(";", result.Errors));
        doc.Elements.Should().ContainSingle().Which.Name.Should().Be("Login button");
    }

    [Fact]
    public void Engine_AddElement_WithoutName_LeavesNameNull()
    {
        var doc = NewDoc();

        WireframeOperationEngine.Apply(
            doc,
            "[{\"op\":\"addElement\",\"type\":\"TmButton\",\"w\":100,\"h\":40}]",
            Registry());

        doc.Elements.Single().Name.Should().BeNull();
    }

    [Fact]
    public void Engine_AddElement_BlankName_IsTreatedAsUnnamed()
    {
        var doc = NewDoc();

        WireframeOperationEngine.Apply(
            doc,
            "[{\"op\":\"addElement\",\"type\":\"TmButton\",\"name\":\"   \",\"w\":100,\"h\":40}]",
            Registry());

        doc.Elements.Single().Name.Should().BeNull();
    }

    [Theory]
    [InlineData("stack")]
    [InlineData("row")]
    [InlineData("grid")]
    public void Engine_LayoutChildren_AcceptName(string kind)
    {
        var doc = NewDoc();
        var ops = $"[{{\"op\":\"{kind}\",\"children\":[" +
                  "{\"type\":\"TmButton\",\"name\":\"Save\",\"w\":100,\"h\":40}," +
                  "{\"type\":\"TmButton\",\"w\":100,\"h\":40}]}]";

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue(string.Join(";", result.Errors));
        doc.Elements.Select(e => e.Name).Should().Equal("Save", null);
    }

    [Fact]
    public void Engine_UpdateElement_SetsName()
    {
        var doc = NewDoc();
        doc.Elements.Add(new WireframeElement { Id = "b1", Type = "TmButton", W = 100, H = 40 });

        var result = WireframeOperationEngine.Apply(
            doc, "[{\"op\":\"updateElement\",\"id\":\"b1\",\"name\":\"Submit\"}]", Registry());

        result.Success.Should().BeTrue(string.Join(";", result.Errors));
        doc.Elements.Single().Name.Should().Be("Submit");
    }

    [Fact]
    public void Engine_UpdateElement_ExplicitNull_ClearsName()
    {
        var doc = NewDoc();
        doc.Elements.Add(new WireframeElement { Id = "b1", Type = "TmButton", Name = "Old", W = 100, H = 40 });

        var result = WireframeOperationEngine.Apply(
            doc, "[{\"op\":\"updateElement\",\"id\":\"b1\",\"name\":null}]", Registry());

        result.Success.Should().BeTrue(string.Join(";", result.Errors));
        doc.Elements.Single().Name.Should().BeNull();
    }

    [Fact]
    public void Engine_UpdateElement_EmptyString_ClearsName()
    {
        var doc = NewDoc();
        doc.Elements.Add(new WireframeElement { Id = "b1", Type = "TmButton", Name = "Old", W = 100, H = 40 });

        WireframeOperationEngine.Apply(
            doc, "[{\"op\":\"updateElement\",\"id\":\"b1\",\"name\":\"\"}]", Registry());

        doc.Elements.Single().Name.Should().BeNull();
    }

    [Fact]
    public void Engine_UpdateElement_WithoutNameKey_KeepsName()
    {
        var doc = NewDoc();
        doc.Elements.Add(new WireframeElement { Id = "b1", Type = "TmButton", Name = "Keep", W = 100, H = 40 });

        WireframeOperationEngine.Apply(
            doc, "[{\"op\":\"updateElement\",\"id\":\"b1\",\"x\":42}]", Registry());

        doc.Elements.Single().Name.Should().Be("Keep");
    }

    // ── Tools / persistence ──────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyOperations_AddElementWithName_PersistsName()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var ops = "[{\"op\":\"addElement\",\"type\":\"TmButton\",\"name\":\"Login button\",\"w\":100,\"h\":40}]";

        var root = Parse(await WireframeOperationTools.ApplyOperations(backend, backend, Registry(), id, ops));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        (await backend.GetWireframeDocumentAsync(id))!.Elements.Single().Name.Should().Be("Login button");
    }

    [Fact]
    public async Task AuthorDocument_PreservesElementName()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = NewDoc();
        doc.Elements.Add(new WireframeElement { Id = "b1", Type = "TmButton", Name = "Login button", X = 10, Y = 10, W = 100, H = 40 });

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend, backend, Registry(), id, WireframeSerializer.Serialize(doc)));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        (await backend.GetWireframeDocumentAsync(id))!.Elements.Single().Name.Should().Be("Login button");
    }

    [Fact]
    public async Task ReplaceDocument_ThenGetDocument_RoundTripsName()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = NewDoc();
        doc.Elements.Add(new WireframeElement { Id = "b1", Type = "TmButton", Name = "Login button", X = 10, Y = 10, W = 100, H = 40 });
        doc.Elements.Add(new WireframeElement { Id = "b2", Type = "TmButton", X = 10, Y = 80, W = 100, H = 40 });

        var replaced = Parse(await WireframeOperationTools.ReplaceDocument(
            backend, backend, Registry(), id, WireframeSerializer.Serialize(doc)));
        replaced.GetProperty("success").GetBoolean().Should().BeTrue(replaced.GetRawText());

        var root = Parse(await WireframeDocumentTools.GetDocument(backend, backend, id));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        var elements = root.GetProperty("document").GetProperty("pages")[0].GetProperty("elements");
        elements[0].GetProperty("name").GetString().Should().Be("Login button");
        elements[1].TryGetProperty("name", out _).Should().BeFalse("unnamed elements omit the name");
    }

    // ── Brief ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Brief_IncludesElementNamesPerElementAndComponent()
    {
        var doc = NewDoc();
        var page = doc.ActivePage!;
        page.Width = 1000;
        page.Height = 1000;
        page.Elements.Add(new WireframeElement { Id = "a", Type = "TmButton", Name = "Save", X = 200, Y = 300, W = 100, H = 36 });
        page.Elements.Add(new WireframeElement { Id = "b", Type = "TmButton", Name = "Cancel", X = 320, Y = 300, W = 100, H = 36 });
        page.Elements.Add(new WireframeElement { Id = "c", Type = "TmButton", X = 440, Y = 300, W = 100, H = 36 });

        var brief = WireframeImplementationBrief.Build(doc);

        var elements = brief.Pages[0].Regions.Single().Elements;
        elements.Select(e => e.Name).Should().Equal("Save", "Cancel", null);
        var component = brief.Pages[0].Components.Single();
        component.Count.Should().Be(3);
        component.Names.Should().Equal("Cancel", "Save");
        brief.ComponentsUsed.Single().Names.Should().Equal("Cancel", "Save");
    }

    [Fact]
    public void Brief_FlowsCarryElementNames()
    {
        var doc = NewDoc();
        var page = doc.ActivePage!;
        page.Elements.Add(new WireframeElement { Id = "a", Type = "TmButton", Name = "Login button", X = 200, Y = 300, W = 100, H = 36 });
        page.Elements.Add(new WireframeElement { Id = "b", Type = "TmCard", X = 200, Y = 400, W = 100, H = 36 });
        page.Connectors.Add(new WireframeConnector { FromId = "a", ToId = "b" });

        var flow = WireframeImplementationBrief.Build(doc).Pages[0].Flows.Single();

        flow.FromName.Should().Be("Login button");
        flow.ToName.Should().BeNull();
    }

    [Fact]
    public void Brief_WithoutNames_HasNullNames()
    {
        var doc = NewDoc();
        doc.ActivePage!.Elements.Add(new WireframeElement { Id = "a", Type = "TmButton", X = 200, Y = 300, W = 100, H = 36 });

        var brief = WireframeImplementationBrief.Build(doc);

        brief.Pages[0].Components.Single().Names.Should().BeNull();
    }

    [Fact]
    public async Task BriefTool_ReturnsNamesInJson()
    {
        var backend = new FakeWireframeBackend();
        var doc = NewDoc();
        doc.ActivePage!.Elements.Add(new WireframeElement { Id = "a", Type = "TmButton", Name = "Login button", X = 200, Y = 300, W = 100, H = 36 });
        var id = backend.Add("Login", "/", doc);

        var root = Parse(await WireframeBriefTools.GetImplementationBrief(backend, backend, id));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        root.GetProperty("componentsUsed")[0].GetProperty("names")[0].GetString().Should().Be("Login button");
        root.GetProperty("pages")[0].GetProperty("regions")[0].GetProperty("elements")[0]
            .GetProperty("name").GetString().Should().Be("Login button");
    }
}
