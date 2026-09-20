using System.Text.Json;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Mcp;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Mcp.Wireframe;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>Tests for the operation engine and the apply_operations / replace_document tools.</summary>
public class WireframeOperationToolsTests
{
    private static WireframeSchemaRegistry Registry() => new([new BuiltInComponentSchemas()]);
    private static string KnownType() => Registry().GetAll().First().Type;
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static bool HasWarning(JsonElement root, string code)
        => root.TryGetProperty("warnings", out var warnings)
           && warnings.ValueKind == JsonValueKind.Array
           && warnings.EnumerateArray().Any(w =>
               w.TryGetProperty("code", out var warningCode)
               && warningCode.GetString() == code);

    // ── Engine ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Engine_AddElement_AddsAndReportsCreatedId()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var ops = $"[{{\"op\":\"addElement\",\"type\":\"{KnownType()}\",\"x\":10,\"y\":10,\"w\":100,\"h\":40}}]";

        var result = WireframeOperationEngine.Apply(doc, ops);

        result.Success.Should().BeTrue();
        result.Applied.Should().Be(1);
        result.CreatedIds.Should().ContainSingle();
        doc.Elements.Should().ContainSingle().Which.Type.Should().Be(KnownType());
    }

    [Fact]
    public void Engine_UnknownOp_FailsWithIndex()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(doc, "[{\"op\":\"frobnicate\"}]");

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("operations[0]").And.Contain("frobnicate");
    }

    [Fact]
    public void Engine_UpdateMissingElement_Fails()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(doc, "[{\"op\":\"updateElement\",\"id\":\"nope\",\"x\":5}]");

        result.Success.Should().BeFalse();
        result.Errors[0].Should().Contain("not found");
    }

    [Fact]
    public void Engine_SetTitleAndCanvasSize_Apply()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(doc,
            "[{\"op\":\"setTitle\",\"title\":\"Orders\"},{\"op\":\"setCanvasSize\",\"width\":1440,\"height\":1024}]");

        result.Success.Should().BeTrue();
        doc.Title.Should().Be("Orders");
        doc.Width.Should().Be(1440);
    }

    [Fact]
    public void Engine_InvalidJson_Fails()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();

        WireframeOperationEngine.Apply(doc, "{not an array}").Success.Should().BeFalse();
    }

    [Fact]
    public void Engine_AddElement_WithoutSize_SeedsSchemaDefaults()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var result = WireframeOperationEngine.Apply(
            doc,
            "[{\"op\":\"addElement\",\"type\":\"TmCard\",\"x\":10,\"y\":10}]",
            Registry());

        result.Success.Should().BeTrue();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.W.Should().Be(280);
        element.H.Should().Be(180);
    }

    [Fact]
    public void Engine_AddElement_TextInputWithoutSize_SeedsPackSchemaDefault()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var result = WireframeOperationEngine.Apply(
            doc,
            "[{\"op\":\"addElement\",\"type\":\"TmTextInput\",\"props\":{\"placeholder\":\"Name\"}}]",
            Registry());

        result.Success.Should().BeTrue();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.W.Should().Be(240);
        element.H.Should().Be(56);
    }

    [Fact]
    public void Engine_AddElement_WithSizeProp_AppliesPreset()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var result = WireframeOperationEngine.Apply(
            doc,
            "[{\"op\":\"addElement\",\"type\":\"TmButton\",\"props\":{\"size\":\"lg\"}}]",
            Registry());

        result.Success.Should().BeTrue();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.W.Should().Be(140);
        element.H.Should().Be(44);
    }

    [Fact]
    public void Engine_AddElement_ExplicitSize_WinsOverPresetAndDefault()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var result = WireframeOperationEngine.Apply(
            doc,
            "[{\"op\":\"addElement\",\"type\":\"TmButton\",\"w\":321,\"h\":65,\"props\":{\"size\":\"lg\"}}]",
            Registry());

        result.Success.Should().BeTrue();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.W.Should().Be(321);
        element.H.Should().Be(65);
    }

    [Fact]
    public void Engine_AddElement_TypeOutsideTargetPacks_IsNotResolvable()
    {
        var appA = Guid.NewGuid().ToString("D");
        var registry = new WireframeSchemaRegistry(
        [
            new BuiltInComponentSchemas(),
            new ScopedSchemaSource("A", 10, appA, Schema("Foo"))
        ]);
        var doc = new WireframeDocument { TargetPackIds = ["tempo"] };
        doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(
            doc,
            $"[{{\"op\":\"addElement\",\"type\":\"app:{appA}:Foo\"}}]",
            registry,
            WireframeComponentScope.ForApp(appA));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("not available in target packs");
        doc.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Engine_AddElement_UnknownType_SuggestsDidYouMean()
    {
        // Same did-you-mean contract as WireframeValidationEngine: a near-miss type names the
        // closest catalog entry so agents self-correct without a schema lookup round-trip.
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var known = KnownType();

        var result = WireframeOperationEngine.Apply(
            doc,
            $"[{{\"op\":\"addElement\",\"type\":\"{known}X\",\"w\":100,\"h\":40}}]",
            Registry());

        result.Success.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle().Which;
        error.Should().Contain("Did you mean").And.Contain(known);
        doc.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Engine_AddElement_WithRole_ResolvesTypeAndStoresRole()
    {
        var doc = new WireframeDocument();
        doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"addElement","role":"otp-input","x":10,"y":12}]""",
            Registry());

        result.Success.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.Type.Should().Be("TmMaskedTextBox");
        element.Role.Should().Be("otp-input");
        element.X.Should().Be(10);
        element.Y.Should().Be(12);
        element.W.Should().Be(180);
        element.H.Should().Be(36);
    }

    [Fact]
    public void Engine_AddElement_WithAmbiguousRole_WarnsAndUsesPrimaryCandidate()
    {
        var appA = Guid.NewGuid().ToString("D");
        var registry = new WireframeSchemaRegistry(
        [
            new BuiltInComponentSchemas(),
            new ScopedSchemaSource("A", 10, appA, Schema("SearchBox", ["search-input"]))
        ]);
        var doc = new WireframeDocument { TargetPackIds = ["tempo", $"app:{appA}"] };
        doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"addElement","role":"search-input"}]""",
            registry,
            WireframeComponentScope.ForApp(appA));

        result.Success.Should().BeTrue();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.Type.Should().Be($"app:{appA}:SearchBox");
        element.Role.Should().Be("search-input");
        result.Warnings.Should().ContainSingle(warning =>
            warning.ElementId == element.Id
            && warning.Code == "role-ambiguous"
            && warning.Hint.Contains("search-input", StringComparison.Ordinal)
            && warning.Hint.Contains(element.Type, StringComparison.Ordinal));
    }

    [Fact]
    public void Engine_AddElement_WithUnmappedRole_ReturnsGapError()
    {
        var doc = new WireframeDocument();
        doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"addElement","role":"future-control"}]""",
            Registry());

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("role 'future-control'").And.Contain("gap");
        doc.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Engine_ScaffoldLanding_SeedsNavbarHeroGridFooter()
    {
        var registry = Registry();
        var doc = new WireframeDocument();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"scaffold","archetype":"landing"}]""",
            registry);

        result.Success.Should().BeTrue();
        result.Applied.Should().Be(1);
        result.RegionMap.Keys.Should().Contain(["navbar", "hero", "featureGrid", "footer"]);

        var desktop = doc.Pages.Should().ContainSingle(page => page.Width == 1440).Subject;
        var mobile = doc.Pages.Should().ContainSingle(page => page.Width == 390).Subject;
        mobile.Name.Should().Contain("Mobile");

        desktop.Elements.Should().Contain(element => element.Type == "TmMenu");
        desktop.Elements.Should().Contain(element => element.Type == "TmText");
        desktop.Elements.Should().Contain(element => element.Type == "TmButton");
        desktop.Elements.Count(element => element.Type == "TmCard").Should().BeGreaterThanOrEqualTo(3);
        desktop.Elements.Should().Contain(element => element.Type == "TmDivider");

        var containerTypes = new[] { "TmCard", "TmStatCard", "TmSection" };
        foreach (var element in doc.Pages.SelectMany(page => page.Elements)
                     .Where(element => containerTypes.Contains(element.Type)))
        {
            var schema = registry.GetSchema(element.Type)!;
            element.W.Should().Be(schema.DefaultWidth);
            element.H.Should().Be(schema.DefaultHeight);
            (element.W, element.H).Should().NotBe((120, 36));
        }
    }

    [Fact]
    public void Engine_ScaffoldUnknownArchetype_Fails()
    {
        var doc = new WireframeDocument();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"scaffold","archetype":"settings"}]""",
            Registry());

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("operations[0]").And.Contain("settings");
        doc.Pages.Should().BeEmpty();
    }

    [Fact]
    public void Engine_Grid_PlacesChildrenInColumnsWithGapAndPadding()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var ops = """
            [{
              "op":"grid",
              "columns":2,
              "gap":10,
              "padding":16,
              "children":[
                {"type":"TmButton","w":100,"h":40},
                {"type":"TmButton","w":100,"h":40},
                {"type":"TmButton","w":100,"h":40},
                {"type":"TmButton","w":100,"h":40}
              ]
            }]
            """;

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue();
        result.CreatedIds.Should().HaveCount(4);
        doc.Elements.Select(e => (e.X, e.Y, e.W, e.H)).Should().Equal(
            (16, 16, 100, 40),
            (126, 16, 100, 40),
            (16, 66, 100, 40),
            (126, 66, 100, 40));
    }

    [Fact]
    public void Engine_Stack_StacksVerticallyByGap()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var ops = """
            [{
              "op":"stack",
              "gap":8,
              "padding":12,
              "children":[
                {"type":"TmButton","w":100,"h":30},
                {"type":"TmButton","w":100,"h":30},
                {"type":"TmButton","w":100,"h":30}
              ]
            }]
            """;

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue();
        doc.Elements.Select(e => (e.X, e.Y)).Should().Equal((12, 12), (12, 50), (12, 88));
    }

    [Fact]
    public void Engine_Row_LaysOutHorizontally()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var ops = """
            [{
              "op":"row",
              "gap":5,
              "padding":20,
              "children":[
                {"type":"TmButton","w":50,"h":30},
                {"type":"TmButton","w":60,"h":30},
                {"type":"TmButton","w":70,"h":30}
              ]
            }]
            """;

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue();
        doc.Elements.Select(e => (e.X, e.Y)).Should().Equal((20, 20), (75, 20), (140, 20));
    }

    [Fact]
    public void Engine_Grid_WithIds_RepositionsExistingElements()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement { Id = "a", Type = "TmButton", W = 80, H = 30 });
        doc.Elements.Add(new WireframeElement { Id = "b", Type = "TmButton", W = 80, H = 30 });
        var ops = """[{"op":"grid","columns":2,"gap":10,"padding":16,"ids":["a","b"]}]""";

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue();
        result.CreatedIds.Should().BeEmpty();
        doc.Elements.Select(e => (e.Id, e.X, e.Y)).Should().Equal(("a", 16, 16), ("b", 106, 16));
    }

    [Fact]
    public void Engine_FillWidth_SpansPageWidthMinusPadding_AndAutoUsesSchemaDefault()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        doc.Width = 500;
        var schema = Registry().GetSchema("TmButton")!;
        var ops = """
            [{
              "op":"grid",
              "columns":1,
              "padding":20,
              "children":[{"type":"TmButton","w":"fill","h":"auto"}]
            }]
            """;

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.X.Should().Be(20);
        element.W.Should().Be(460);
        element.H.Should().Be(schema.DefaultHeight);
    }

    [Fact]
    public void Engine_RelativeAnchor_PositionsBelowReferenceByMargin()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var ops = """
            [
              {"op":"addElement","id":"ref","type":"TmCard","x":30,"y":40,"w":100,"h":50},
              {"op":"addElement","id":"child","type":"TmButton","below":"ref","margin":12,"w":80,"h":30}
            ]
            """;

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue();
        var child = doc.Elements.Single(e => e.Id == "child");
        child.X.Should().Be(30);
        child.Y.Should().Be(102);
    }

    [Fact]
    public void Engine_RelativeAnchor_MissingReferenceFails()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"addElement","type":"TmButton","below":"missing"}]""",
            Registry());

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("reference element 'missing' not found");
        doc.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Engine_UnknownLayoutParam_Fails()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"grid","foo":1,"children":[]}]""",
            Registry());

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("unknown layout param 'foo'");
        doc.Elements.Should().BeEmpty();
    }

    [Fact]
    public void Engine_DocumentedLayoutParam_IsAccepted()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();
        var ops = """
            [{
              "op":"row",
              "pageId":null,
              "direction":"horizontal",
              "align":"start",
              "wrap":true,
              "margin":4,
              "x":5,
              "y":6,
              "w":200,
              "h":100,
              "type":"TmButton",
              "gap":4,
              "padding":8,
              "children":[{"type":"TmButton","w":40,"h":20}]
            }]
            """;

        var result = WireframeOperationEngine.Apply(doc, ops, Registry());

        result.Success.Should().BeTrue();
        doc.Elements.Should().ContainSingle();
    }

    [Fact]
    public void Engine_AddElement_ExplicitXY_Unaffected()
    {
        var doc = new WireframeDocument(); doc.EnsureActivePage();

        var result = WireframeOperationEngine.Apply(
            doc,
            """[{"op":"addElement","type":"TmButton","x":77,"y":88,"w":123,"h":45}]""",
            Registry());

        result.Success.Should().BeTrue();
        var element = doc.Elements.Should().ContainSingle().Which;
        element.X.Should().Be(77);
        element.Y.Should().Be(88);
        element.W.Should().Be(123);
        element.H.Should().Be(45);
    }

    // ── apply_operations tool ─────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyOperations_HappyPath_PersistsAndReportsApplied()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var ops = $"[{{\"op\":\"addElement\",\"type\":\"{KnownType()}\",\"x\":10,\"y\":10,\"w\":100,\"h\":40}}]";

        var root = Parse(await WireframeOperationTools.ApplyOperations(backend, backend, Registry(), id, ops));

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("applied").GetInt32().Should().Be(1);
        (await backend.GetWireframeDocumentAsync(id))!.Elements.Should().ContainSingle();
    }

    [Fact]
    public async Task ApplyOperations_AddElementWithoutSize_SeedsDefault()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");

        var root = Parse(await WireframeOperationTools.ApplyOperationsScoped(
            backend,
            backend,
            Registry(),
            id,
            "[{\"op\":\"addElement\",\"type\":\"TmCard\"}]"));

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var element = (await backend.GetWireframeDocumentAsync(id))!.Elements.Should().ContainSingle().Which;
        element.W.Should().Be(280);
        element.H.Should().Be(180);
    }

    [Fact]
    public async Task ApplyOperations_InvalidResult_SavesNothing()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var ops = "[{\"op\":\"addElement\",\"type\":\"TotallyUnknownType\",\"w\":100,\"h\":40}]";

        var root = Parse(await WireframeOperationTools.ApplyOperations(backend, backend, Registry(), id, ops));

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("validation_failed");
        (await backend.GetWireframeDocumentAsync(id))!.Elements.Should().BeEmpty(); // unchanged
    }

    [Fact]
    public async Task ApplyOperations_StaleExpectedModifiedAt_ReturnsConflict()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var ops = $"[{{\"op\":\"addElement\",\"type\":\"{KnownType()}\",\"w\":100,\"h\":40}}]";

        var root = Parse(await WireframeOperationTools.ApplyOperations(
            backend, backend, Registry(), id, ops, expectedModifiedAt: DateTime.UtcNow.AddMinutes(-10)));

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("conflict");
    }

    [Fact]
    public async Task ApplyOperations_UnknownDocument_ReturnsNotFound()
    {
        var backend = new FakeWireframeBackend();

        var root = Parse(await WireframeOperationTools.ApplyOperations(
            backend, backend, Registry(), Guid.NewGuid(), "[]"));

        root.GetProperty("error").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task ScaffoldTool_PersistsDesktopAndMobilePagesAndReturnsRegionMap()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");

        var root = Parse(await WireframeOperationTools.Scaffold(
            backend,
            backend,
            Registry(),
            id,
            "landing"));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        root.GetProperty("applied").GetInt32().Should().Be(1);
        root.GetProperty("regionMap").GetProperty("navbar").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("createdIds").GetArrayLength().Should().BeGreaterThan(0);

        var document = (await backend.GetWireframeDocumentAsync(id))!;
        document.Pages.Should().Contain(page => page.Width == 1440);
        document.Pages.Should().Contain(page => page.Width == 390);
    }

    [Fact]
    public void AuthoringGuide_ReturnsCanvasConventionsAndPropVocabulary()
    {
        var appId = Guid.NewGuid().ToString("D");
        var registry = new WireframeSchemaRegistry(
        [
            new BuiltInComponentSchemas(),
            new ScopedSchemaSource("App", 10, appId, Schema("InvoiceCard"))
        ]);

        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(
            registry,
            scopeAppId: appId));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        root.GetProperty("canvas").GetProperty("desktopWidth").GetDouble().Should().Be(1440);
        root.GetProperty("canvas").GetProperty("mobileWidth").GetDouble().Should().Be(390);
        root.GetProperty("layoutOps").GetProperty("operations").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(["stack", "row", "grid"]);
        root.GetProperty("propVocabulary").EnumerateArray()
            .Select(item => item.GetProperty("type").GetString())
            .Should().Contain($"app:{appId}:InvoiceCard");
    }

    // ── author_document tool ─────────────────────────────────────────────────────

    [Fact]
    public async Task AuthorDocument_ValidDocument_PersistsAndReturnsAdvisoryWarnings()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Authored" };
        doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement
        {
            Id = "card",
            Type = "TmCard",
            X = 20,
            Y = 20,
            W = 280,
            H = 180
        });

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc)));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        HasWarning(root, "default-size").Should().BeTrue(root.GetRawText());
        (await backend.GetWireframeDocumentAsync(id))!.Title.Should().Be("Authored");
    }

    [Fact]
    public async Task AuthorDocument_NormalizesEnumCasingBeforeSave()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Enum" };
        doc.EnsureActivePage();
        var element = new WireframeElement { Id = "button", Type = "TmButton", W = 120, H = 36 };
        element.Props["label"] = Json("Save");
        element.Props["size"] = Json("LG");
        doc.Elements.Add(element);

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc)));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        HasWarning(root, "enum-normalized").Should().BeTrue(root.GetRawText());
        var saved = (await backend.GetWireframeDocumentAsync(id))!.Elements.Single();
        saved.Props["size"].GetString().Should().Be("lg");
    }

    [Fact]
    public async Task AuthorDocument_ClampsOffCanvasElementsBeforeSave()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Clamp" };
        doc.EnsureActivePage();
        doc.Width = 500;
        doc.Height = 400;
        var element = new WireframeElement
        {
            Id = "button",
            Type = "TmButton",
            X = 490,
            Y = 390,
            W = 120,
            H = 36
        };
        element.Props["label"] = Json("Save");
        doc.Elements.Add(element);

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc)));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        HasWarning(root, "clamped-to-canvas").Should().BeTrue(root.GetRawText());
        var saved = (await backend.GetWireframeDocumentAsync(id))!.Elements.Single();
        saved.X.Should().Be(380);
        saved.Y.Should().Be(364);
    }

    [Fact]
    public async Task AuthorDocument_StructuralError_ReturnsValidationFailedAndSavesNothing()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Bad" };
        doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement { Type = "NopeType", W = 120, H = 40 });

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc)));

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("validation_failed");
        root.GetProperty("validationErrors").GetArrayLength().Should().BeGreaterThan(0);
        (await backend.GetWireframeDocumentAsync(id))!.Title.Should().Be("Design");
    }

    [Fact]
    public async Task AuthorDocument_StrictWarning_ReturnsValidationFailedAndSavesNothing()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Strict" };
        doc.EnsureActivePage();
        var element = new WireframeElement { Type = "TmButton", W = 120, H = 36 };
        element.Props["label"] = Json("Save");
        element.Props["lable"] = Json("Typo");
        doc.Elements.Add(element);

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc),
            strict: true));

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("validation_failed");
        HasWarning(root, "unknown-prop").Should().BeTrue(root.GetRawText());
        (await backend.GetWireframeDocumentAsync(id))!.Title.Should().Be("Design");
    }

    [Fact]
    public async Task AuthorDocument_StaleExpectedModifiedAt_ReturnsConflict()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Conflict" };
        doc.EnsureActivePage();

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc),
            expectedModifiedAt: DateTime.UtcNow.AddMinutes(-10)));

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("conflict");
    }

    [Fact]
    public async Task AuthorDocument_ElementWithRole_ResolvesConcreteTypeBeforeSave()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Role" };
        doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement { Id = "otp", Role = "otp-input", W = 180, H = 36 });

        var root = Parse(await WireframeOperationTools.AuthorDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc)));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        var saved = (await backend.GetWireframeDocumentAsync(id))!.Elements.Single();
        saved.Type.Should().Be("TmMaskedTextBox");
        saved.Role.Should().Be("otp-input");
    }

    // ── replace_document tool ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDocument_ValidDocument_Persists()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");

        var doc = new WireframeDocument { Title = "Replaced" };
        doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement { Type = KnownType(), W = 120, H = 40 });
        var json = WireframeSerializer.Serialize(doc);

        var root = Parse(await WireframeOperationTools.ReplaceDocument(backend, backend, Registry(), id, json));

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        (await backend.GetWireframeDocumentAsync(id))!.Title.Should().Be("Replaced");
    }

    [Fact]
    public async Task ReplaceDocument_InvalidDocument_SavesNothing()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");

        var doc = new WireframeDocument { Title = "Bad" };
        doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement { Type = "NopeType", W = 120, H = 40 });
        var json = WireframeSerializer.Serialize(doc);

        var root = Parse(await WireframeOperationTools.ReplaceDocument(backend, backend, Registry(), id, json));

        root.GetProperty("error").GetString().Should().Be("validation_failed");
        (await backend.GetWireframeDocumentAsync(id))!.Title.Should().Be("Design"); // unchanged
    }

    [Fact]
    public async Task ReplaceDocument_ReturnsAdvisoryWarnings()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Warn" };
        doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement { Type = "TmCard", W = 280, H = 180 });

        var root = Parse(await WireframeOperationTools.ReplaceDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc)));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        HasWarning(root, "default-size").Should().BeTrue(root.GetRawText());
        (await backend.GetWireframeDocumentAsync(id))!.Title.Should().Be("Warn");
    }

    [Fact]
    public async Task ReplaceDocument_StrictWarning_ReturnsValidationFailedAndSavesNothing()
    {
        var backend = new FakeWireframeBackend();
        var id = backend.Add("Design", "/");
        var doc = new WireframeDocument { Title = "Strict replace" };
        doc.EnsureActivePage();
        var element = new WireframeElement { Type = "TmButton", W = 120, H = 36 };
        element.Props["label"] = Json("Save");
        element.Props["lable"] = Json("Typo");
        doc.Elements.Add(element);

        var root = Parse(await WireframeOperationTools.ReplaceDocument(
            backend,
            backend,
            Registry(),
            id,
            WireframeSerializer.Serialize(doc),
            strict: true));

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("validation_failed");
        HasWarning(root, "unknown-prop").Should().BeTrue(root.GetRawText());
        (await backend.GetWireframeDocumentAsync(id))!.Title.Should().Be("Design");
    }

    private static WireframeComponentSchema Schema(string type, IReadOnlyList<string>? roles = null)
        => new()
        {
            Type = type,
            Category = "Custom",
            DisplayName = type,
            Roles = roles,
            Props = []
        };

    private sealed class ScopedSchemaSource(
        string id,
        int priority,
        string scopeAppId,
        params WireframeComponentSchema[] schemas)
        : IWireframeScopedSchemaSource
    {
        public string SourceId => id;
        public int Priority => priority;
        public string ScopeAppId => scopeAppId;
        public IEnumerable<WireframeComponentSchema> GetSchemas() => schemas;
    }
}
