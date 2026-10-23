using System.Text.Json;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Tests.Wireframe;

public class WireframeSerializerTests
{
    // ── Serialize ─────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_EmptyDocument_ProducesValidJson()
    {
        var doc = new WireframeDocument();

        var json = WireframeSerializer.Serialize(doc);

        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("\"version\"");
        json.Should().Contain("\"title\"");
        json.Should().Contain("\"pages\"");
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var doc = new WireframeDocument { Title = "Test" };

        var json = WireframeSerializer.Serialize(doc);

        // camelCase keys
        json.Should().Contain("\"title\"");
        json.Should().Contain("\"createdAt\"");
        json.Should().NotContain("\"Title\"");
        json.Should().NotContain("\"CreatedAt\"");
    }

    [Fact]
    public void Serialize_UpdatesModifiedAt()
    {
        var doc = new WireframeDocument();
        var before = DateTime.UtcNow.AddSeconds(-1);

        WireframeSerializer.Serialize(doc);

        doc.ModifiedAt.Should().BeAfter(before);
    }

    [Fact]
    public void Serialize_NullValues_AreOmitted()
    {
        var doc = new WireframeDocument();
        doc.Connectors.Add(new WireframeConnector { FromId = "a", ToId = "b" /* Label = null */ });

        var json = WireframeSerializer.Serialize(doc);

        json.Should().NotContain("\"label\"");
    }

    [Fact]
    public void Name_RoundTrips_AndIsOptional()
    {
        var doc = new WireframeDocument { Title = "T" };
        var named = WireframeDocumentExtensions.NewElement("TmButton", 0, 0, 100, 30);
        named.Name = "Login button";
        var unnamed = WireframeDocumentExtensions.NewElement("TmButton", 0, 40, 100, 30);
        doc.Elements.Add(named);
        doc.Elements.Add(unnamed);

        var json = WireframeSerializer.Serialize(doc);

        using var parsed = JsonDocument.Parse(json);
        var elements = parsed.RootElement.GetProperty("pages")[0].GetProperty("elements");
        elements[0].TryGetProperty("name", out var nameProp).Should().BeTrue();
        nameProp.GetString().Should().Be("Login button");
        elements[1].TryGetProperty("name", out _).Should().BeFalse("unnamed elements omit the name key");

        var restored = WireframeSerializer.Deserialize(json);
        restored.Elements[0].Name.Should().Be("Login button");
        restored.Elements[1].Name.Should().BeNull();

        // Old JSON without "name" still deserializes.
        const string legacy = """
            { "version": "2.1", "title": "Old", "pages": [ { "id": "p1", "name": "Page 1", "width": 800, "height": 600,
              "elements": [ { "id": "e1", "type": "TmButton", "x": 1, "y": 2, "w": 10, "h": 10 } ] } ] }
            """;
        WireframeSerializer.Deserialize(legacy).Elements[0].Name.Should().BeNull();
    }

    // ── Deserialize ───────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_ValidJson_ReturnsDocument()
    {
        var json = """
            {
              "version": "2.0",
              "title": "Login page",
              "pages": [
                {
                  "name": "Page 1",
                  "width": 1280,
                  "height": 800,
                  "elements": [],
                  "connectors": []
                }
              ]
            }
            """;

        var doc = WireframeSerializer.Deserialize(json);

        doc.Should().NotBeNull();
        doc.Title.Should().Be("Login page");
        doc.Version.Should().Be("2.0");
        doc.Width.Should().Be(1280);
    }

    [Fact]
    public void Deserialize_WithElements_MapsCorrectly()
    {
        var json = """
            {
              "version": "2.0",
              "title": "Test",
              "pages": [
                {
                  "name": "Page 1",
                  "width": 1280,
                  "height": 800,
                  "elements": [
                    {
                      "id": "abc123",
                      "type": "TmButton",
                      "x": 100,
                      "y": 200,
                      "w": 120,
                      "h": 36,
                      "props": {
                        "label": "Submit",
                        "variant": "primary",
                        "disabled": false
                      }
                    }
                  ]
                }
              ]
            }
            """;

        var doc = WireframeSerializer.Deserialize(json);

        doc.Elements.Should().HaveCount(1);
        var el = doc.Elements[0];
        el.Id.Should().Be("abc123");
        el.Type.Should().Be("TmButton");
        el.X.Should().Be(100);
        el.Y.Should().Be(200);
        el.W.Should().Be(120);
        el.H.Should().Be(36);
        el.Props.Should().ContainKey("label");
        el.Props["label"].GetString().Should().Be("Submit");
    }

    [Fact]
    public void Deserialize_PropsWithVariousTypes_MapsCorrectly()
    {
        var json = """
            {
              "version": "2.0",
              "title": "Test",
              "pages": [
                {
                  "name": "Page 1",
                  "width": 1280,
                  "height": 800,
                  "elements": [
                    {
                      "id": "el1",
                      "type": "TmDataTable",
                      "x": 0, "y": 0, "w": 800, "h": 400,
                      "props": {
                        "title": "Users",
                        "rowCount": 10,
                        "showPagination": true,
                        "columns": ["Name", "Email", "Role"]
                      }
                    }
                  ]
                }
              ]
            }
            """;

        var doc = WireframeSerializer.Deserialize(json);
        var props = doc.Elements[0].Props;

        props.GetString("title").Should().Be("Users");
        props.GetInt("rowCount").Should().Be(10);
        props.GetBool("showPagination").Should().BeTrue();
        props.GetStringList("columns").Should().BeEquivalentTo("Name", "Email", "Role");
    }

    [Fact]
    public void Deserialize_WithConnectors_MapsCorrectly()
    {
        var json = """
            {
              "version": "2.0",
              "title": "Test",
              "pages": [
                {
                  "name": "Page 1",
                  "width": 1280,
                  "height": 800,
                  "elements": [],
                  "connectors": [
                    { "id": "c1", "fromId": "el1", "toId": "el2", "label": "navigates" }
                  ]
                }
              ]
            }
            """;

        var doc = WireframeSerializer.Deserialize(json);

        doc.Connectors.Should().HaveCount(1);
        doc.Connectors[0].FromId.Should().Be("el1");
        doc.Connectors[0].ToId.Should().Be("el2");
        doc.Connectors[0].Label.Should().Be("navigates");
    }

    [Fact]
    public void RoundTrip_PreservesTargetPacksAndTheme()
    {
        var doc = new WireframeDocument
        {
            Title = "Targeted",
            TargetPackIds = ["tempo", "app:11111111-1111-1111-1111-111111111111"],
            TargetTheme = "dark"
        };
        doc.EnsureActivePage();
        doc.ActivePage!.TargetPackIds = ["tempo"];
        doc.ActivePage.TargetTheme = "light";

        var json = WireframeSerializer.Serialize(doc);
        var restored = WireframeSerializer.Deserialize(json);

        json.Should().Contain("\"targetPacks\"");
        json.Should().Contain("\"targetTheme\"");
        restored.TargetPackIds.Should().Equal("tempo", "app:11111111-1111-1111-1111-111111111111");
        restored.TargetTheme.Should().Be("dark");
        restored.ActivePage!.TargetPackIds.Should().Equal("tempo");
        restored.ActivePage.TargetTheme.Should().Be("light");
    }

    [Fact]
    public void Deserialize_MalformedJson_ThrowsWireframeDeserializationException()
    {
        var act = () => WireframeSerializer.Deserialize("{ this is not valid json }");

        act.Should().Throw<WireframeDeserializationException>()
            .WithMessage("Invalid wireframe JSON.");
    }

    [Fact]
    public void Deserialize_NullString_ThrowsArgumentNullException()
    {
        var act = () => WireframeSerializer.Deserialize(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── TryDeserialize ────────────────────────────────────────────────────────

    [Fact]
    public void TryDeserialize_ValidJson_ReturnsTrueAndDocument()
    {
        var json = """{ "version": "2.0", "title": "Test", "pages": [{ "name": "P1", "width": 1280, "height": 800, "elements": [] }] }""";

        var result = WireframeSerializer.TryDeserialize(json, out var doc);

        result.Should().BeTrue();
        doc.Should().NotBeNull();
        doc!.Title.Should().Be("Test");
    }

    [Fact]
    public void TryDeserialize_InvalidJson_ReturnsFalseAndNull()
    {
        var result = WireframeSerializer.TryDeserialize("not json", out var doc);

        result.Should().BeFalse();
        doc.Should().BeNull();
    }

    // ── Roundtrip ─────────────────────────────────────────────────────────────

    [Fact]
    public void Roundtrip_DocumentWithElements_PreservesAllData()
    {
        var original = new WireframeDocument
        {
            Title = "Dashboard",
            Width = 1440,
            Height = 900
        };
        original.Elements.Add(WireframeDocumentExtensions.NewElement("TmCard", 50, 100, 300, 200));
        original.Elements[0].SetProp("title", "Revenue");
        original.Elements[0].SetProp("showHeader", true);
        original.Connectors.Add(new WireframeConnector { FromId = original.Elements[0].Id, ToId = "other" });

        var json = WireframeSerializer.Serialize(original);
        var restored = WireframeSerializer.Deserialize(json);

        restored.Title.Should().Be("Dashboard");
        restored.Width.Should().Be(1440);
        restored.Height.Should().Be(900);
        restored.Elements.Should().HaveCount(1);
        restored.Elements[0].Type.Should().Be("TmCard");
        restored.Elements[0].X.Should().Be(50);
        restored.Elements[0].Props.GetString("title").Should().Be("Revenue");
        restored.Elements[0].Props.GetBool("showHeader").Should().BeTrue();
        restored.Connectors.Should().HaveCount(1);
    }

    [Fact]
    public void Clone_ProducesDeepCopy()
    {
        var original = new WireframeDocument { Title = "Original" };
        original.Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", 0, 0));

        var clone = original.Clone();

        // Mutate clone – original must not change
        clone.Title = "Changed";
        clone.Elements[0].X = 999;

        original.Title.Should().Be("Original");
        original.Elements[0].X.Should().Be(0);
    }

    // ── Extension helpers ─────────────────────────────────────────────────────

    [Fact]
    public void GetString_MissingKey_ReturnsFallback()
    {
        var props = new Dictionary<string, JsonElement>();

        props.GetString("missing", "default").Should().Be("default");
    }

    [Fact]
    public void GetBool_WrongType_ReturnsFallback()
    {
        var el = WireframeDocumentExtensions.NewElement("TmButton", 0, 0);
        el.SetProp("count", 5); // number, not bool

        el.Props.GetBool("count", true).Should().BeTrue(); // returns fallback
    }

    [Fact]
    public void SetProp_NullValue_RemovesKey()
    {
        var el = WireframeDocumentExtensions.NewElement("TmButton", 0, 0);
        el.SetProp("label", "Test");
        el.SetProp("label", null);

        el.Props.Should().NotContainKey("label");
    }

    [Fact]
    public void NewElement_AppliesDefaultDimensions()
    {
        var el = WireframeDocumentExtensions.NewElement("TmButton", 10, 20, 160, 40);

        el.Type.Should().Be("TmButton");
        el.X.Should().Be(10);
        el.Y.Should().Be(20);
        el.W.Should().Be(160);
        el.H.Should().Be(40);
    }

    // ── New-component round-trip (Phases 1–8) ─────────────────────────────────

    [Theory]
    // Phase 1 — Inputs
    [InlineData("TmSlider")]
    [InlineData("TmRating")]
    [InlineData("TmMaskedTextBox")]
    // Phase 2 — Color
    [InlineData("TmColorPicker")]
    [InlineData("TmColorGradient")]
    // Phase 3 — Signature / Pickers
    [InlineData("TmSignature")]
    [InlineData("TmRecurrenceEditor")]
    // Phase 4 — Charts / Data Display
    [InlineData("TmSankeyChart")]
    [InlineData("TmSparkline")]
    [InlineData("TmGauge")]
    [InlineData("TmQRCode")]
    [InlineData("TmPdfViewer")]
    // Phase 5 — Buttons / Navigation
    [InlineData("TmFloatingActionButton")]
    [InlineData("TmBottomNavigation")]
    [InlineData("TmMenu")]
    // Phase 6 — Layout
    [InlineData("TmSplitter")]
    [InlineData("TmDockManager")]
    // Phase 7 — Builders & collaboration
    [InlineData("TmFormulaBuilder")]
    [InlineData("TmAIPrompt")]
    [InlineData("TmShareLinkPanel")]
    // Phase 8 — Editors & Apps
    [InlineData("TmChat")]
    [InlineData("TmSpreadsheet")]
    [InlineData("TmGantt")]
    [InlineData("TmDiagramEditor")]
    [InlineData("TmDocumentEditor")]
    [InlineData("TmNotionEditor")]
    [InlineData("TmFileManager")]
    public void Roundtrip_NewComponent_PreservesTypeAndProps(string type)
    {
        var doc = new WireframeDocument { Title = "Test" };
        var el  = WireframeDocumentExtensions.NewElement(type, 10, 20, 200, 120);
        el.SetProp("label", "test-value");
        doc.Elements.Add(el);

        var json     = WireframeSerializer.Serialize(doc);
        var restored = WireframeSerializer.Deserialize(json);

        restored.Elements.Should().HaveCount(1);
        restored.Elements[0].Type.Should().Be(type);
        restored.Elements[0].Props.GetString("label").Should().Be("test-value");
    }
}
