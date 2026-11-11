using System.Text.Json;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Validates that the wireframe JSON format conforms to its documented schema contract.
/// These tests act as a regression guard: if a serialization change breaks an AI/API consumer
/// they will fail here before reaching production.
/// </summary>
public class WireframeSchemaTests
{
    // ── Required top-level fields ─────────────────────────────────────────────

    [Fact]
    public void SerializedDocument_HasVersionField()
    {
        var json = WireframeSerializer.Serialize(new WireframeDocument());
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("version", out _).Should().BeTrue("'version' is required by schema");
    }

    [Fact]
    public void SerializedDocument_HasTitleField()
    {
        var json = WireframeSerializer.Serialize(new WireframeDocument { Title = "Test" });
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("title", out var v).Should().BeTrue();
        v.GetString().Should().Be("Test");
    }

    [Fact]
    public void SerializedDocument_HasPagesArray()
    {
        var json = WireframeSerializer.Serialize(new WireframeDocument());
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("pages", out var pages).Should().BeTrue("'pages' is required by schema");
        pages.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void SerializedDocument_HasActivePageIdField()
    {
        var wf = new WireframeDocument();
        wf.EnsureActivePage();
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("activePageId", out _).Should().BeTrue("'activePageId' is required by schema");
    }

    [Fact]
    public void SerializedPage_HasElementsArray()
    {
        var wf = new WireframeDocument();
        wf.EnsureActivePage();
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);
        var page = doc.RootElement.GetProperty("pages")[0];
        page.TryGetProperty("elements", out var el).Should().BeTrue("'elements' is required by schema");
        el.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void SerializedPage_HasConnectorsArray()
    {
        var wf = new WireframeDocument();
        wf.Connectors.Add(new WireframeConnector { FromId = "a", ToId = "b" });
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);
        var page = doc.RootElement.GetProperty("pages")[0];
        page.TryGetProperty("connectors", out var cv).Should().BeTrue();
        cv.ValueKind.Should().Be(JsonValueKind.Array);
    }

    // ── Required element fields ───────────────────────────────────────────────

    [Fact]
    public void SerializedElement_HasAllRequiredFields()
    {
        var wf = new WireframeDocument();
        wf.Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", 10, 20, 120, 36));
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var el = doc.RootElement.GetProperty("pages")[0].GetProperty("elements")[0];
        el.TryGetProperty("id", out _).Should().BeTrue("'id' required");
        el.TryGetProperty("type", out _).Should().BeTrue("'type' required");
        el.TryGetProperty("x", out _).Should().BeTrue("'x' required");
        el.TryGetProperty("y", out _).Should().BeTrue("'y' required");
        el.TryGetProperty("w", out _).Should().BeTrue("'w' required");
        el.TryGetProperty("h", out _).Should().BeTrue("'h' required");
    }

    [Fact]
    public void SerializedElement_TypeIsString()
    {
        var wf = new WireframeDocument();
        wf.Elements.Add(WireframeDocumentExtensions.NewElement("TmCard", 0, 0));
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var el = doc.RootElement.GetProperty("pages")[0].GetProperty("elements")[0];
        el.GetProperty("type").ValueKind.Should().Be(JsonValueKind.String);
        el.GetProperty("type").GetString().Should().Be("TmCard");
    }

    [Fact]
    public void SerializedElement_RoleRoundTrips()
    {
        var wf = new WireframeDocument();
        // The OTP role belongs to the dedicated component (decision WIREFRAME-ROLE-OTP-INPUT-EXCLUSIVE).
        wf.Elements.Add(new WireframeElement { Type = "TmOtpInput", Role = "otp-input" });

        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var el = doc.RootElement.GetProperty("pages")[0].GetProperty("elements")[0];
        el.GetProperty("role").GetString().Should().Be("otp-input");

        var restored = WireframeSerializer.Deserialize(json);
        restored.Elements[0].Role.Should().Be("otp-input");
    }

    [Fact]
    public void SerializedElement_CoordinatesAreNumbers()
    {
        var wf = new WireframeDocument();
        wf.Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", 50.5, 100.25, 200, 40));
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var el = doc.RootElement.GetProperty("pages")[0].GetProperty("elements")[0];
        el.GetProperty("x").ValueKind.Should().Be(JsonValueKind.Number);
        el.GetProperty("y").ValueKind.Should().Be(JsonValueKind.Number);
        el.GetProperty("x").GetDouble().Should().Be(50.5);
        el.GetProperty("y").GetDouble().Should().Be(100.25);
    }

    [Fact]
    public void SerializedElement_PropsIsObject()
    {
        var wf = new WireframeDocument();
        var el = WireframeDocumentExtensions.NewElement("TmButton", 0, 0);
        el.SetProp("label", "Click");
        wf.Elements.Add(el);
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var props = doc.RootElement.GetProperty("pages")[0].GetProperty("elements")[0].GetProperty("props");
        props.ValueKind.Should().Be(JsonValueKind.Object);
        props.TryGetProperty("label", out var lbl).Should().BeTrue();
        lbl.GetString().Should().Be("Click");
    }

    [Fact]
    public void SerializedElement_OptionalNullFields_Omitted()
    {
        // groupId and lockedBy are null by default → schema says omit nulls
        var wf = new WireframeDocument();
        wf.Elements.Add(WireframeDocumentExtensions.NewElement("TmButton", 0, 0));
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var el = doc.RootElement.GetProperty("pages")[0].GetProperty("elements")[0];
        el.TryGetProperty("groupId", out _).Should().BeFalse("null groupId should be omitted");
        el.TryGetProperty("lockedBy", out _).Should().BeFalse("null lockedBy should be omitted");
    }

    // ── Required connector fields ─────────────────────────────────────────────

    [Fact]
    public void SerializedConnector_HasAllRequiredFields()
    {
        var wf = new WireframeDocument();
        wf.Connectors.Add(new WireframeConnector { Id = "c1", FromId = "el1", ToId = "el2" });
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var c = doc.RootElement.GetProperty("pages")[0].GetProperty("connectors")[0];
        c.TryGetProperty("id", out _).Should().BeTrue("'id' required");
        c.TryGetProperty("fromId", out _).Should().BeTrue("'fromId' required");
        c.TryGetProperty("toId", out _).Should().BeTrue("'toId' required");
    }

    [Fact]
    public void SerializedConnector_NullLabel_Omitted()
    {
        var wf = new WireframeDocument();
        wf.Connectors.Add(new WireframeConnector { FromId = "a", ToId = "b" }); // Label = null
        var json = WireframeSerializer.Serialize(wf);
        using var doc = JsonDocument.Parse(json);

        var c = doc.RootElement.GetProperty("pages")[0].GetProperty("connectors")[0];
        c.TryGetProperty("label", out _).Should().BeFalse("null label should be omitted");
    }

    // ── Numeric prop types round-trip correctly ────────────────────────────────

    [Theory]
    [InlineData("count", 42)]
    [InlineData("opacity", 1)]
    [InlineData("zIndex", 0)]
    public void IntProp_RoundTrips(string key, int value)
    {
        var el = new WireframeElement { Type = "T" };
        el.SetProp(key, value);
        var wf = new WireframeDocument();
        wf.Elements.Add(el);

        var json = WireframeSerializer.Serialize(wf);
        var restored = WireframeSerializer.Deserialize(json);

        restored.Elements[0].Props.GetInt(key).Should().Be(value);
    }

    [Theory]
    [InlineData("opacity", 0.5)]
    [InlineData("scale", 1.25)]
    public void DoubleProp_RoundTrips(string key, double value)
    {
        var el = new WireframeElement { Type = "T" };
        el.SetProp(key, value);
        var wf = new WireframeDocument();
        wf.Elements.Add(el);

        var json = WireframeSerializer.Serialize(wf);
        var restored = WireframeSerializer.Deserialize(json);

        restored.Elements[0].Props.GetDouble(key).Should().BeApproximately(value, 0.0001);
    }

    // ── AI-friendly format checks ─────────────────────────────────────────────

    [Fact]
    public void SerializedDocument_IsCamelCase_AllKeys()
    {
        var wf = new WireframeDocument { Title = "AI test" };
        var el = WireframeDocumentExtensions.NewElement("TmButton", 0, 0);
        el.SetProp("isDisabled", true);
        wf.Elements.Add(el);
        wf.Connectors.Add(new WireframeConnector { FromId = "x", ToId = "y" });

        var json = WireframeSerializer.Serialize(wf);

        // PascalCase keys must NOT appear
        json.Should().NotContain("\"Title\"");
        json.Should().NotContain("\"Elements\"");
        json.Should().NotContain("\"Connectors\"");
        json.Should().NotContain("\"CreatedAt\"");
        json.Should().NotContain("\"ModifiedAt\"");
        json.Should().NotContain("\"FromId\"");
        json.Should().NotContain("\"ToId\"");
    }

    [Fact]
    public void SerializedDocument_IsIndented()
    {
        // Indented output is required for AI readability (schema spec)
        var json = WireframeSerializer.Serialize(new WireframeDocument());
        json.Should().Contain("\n");
        json.Should().Contain("  ");
    }

    [Fact]
    public void Deserialize_MinimalAiGeneratedJson_Works()
    {
        // Simulates the minimal JSON an AI might produce (v2.0 multi-page format)
        var json = """
            {
              "version": "2.0",
              "title": "Contact form",
              "pages": [
                {
                  "name": "Page 1",
                  "width": 1280,
                  "height": 800,
                  "elements": [
                    { "id": "f1a2b3c4", "type": "TmTextInput", "x": 40, "y": 40, "w": 280, "h": 36,
                      "props": { "label": "Name", "placeholder": "Your name" } },
                    { "id": "d5e6f7a8", "type": "TmButton",    "x": 40, "y": 100, "w": 120, "h": 36,
                      "props": { "label": "Submit", "variant": "primary" } }
                  ]
                }
              ]
            }
            """;

        var result = WireframeSerializer.TryDeserialize(json, out var doc);

        result.Should().BeTrue("AI-generated minimal JSON should deserialize successfully");
        doc!.Title.Should().Be("Contact form");
        doc.Elements.Should().HaveCount(2);
        doc.Elements[0].Type.Should().Be("TmTextInput");
        doc.Elements[1].Props.GetString("variant").Should().Be("primary");
    }

    [Fact]
    public void Deserialize_JsonWithExtraUnknownFields_Succeeds()
    {
        // Schema should tolerate forward-compatible additions
        var json = """
            {
              "version": "2.0",
              "title": "Test",
              "pages": [
                { "name": "Page 1", "width": 1280, "height": 800, "elements": [], "connectors": [] }
              ],
              "futureField": "ignored",
              "metadata": { "author": "Claude" }
            }
            """;

        var result = WireframeSerializer.TryDeserialize(json, out _);
        result.Should().BeTrue("extra fields should not break deserialization");
    }

    // ── Canvas dimensions within schema bounds ────────────────────────────────

    [Fact]
    public void SerializedDocument_PageWidthAndHeight_AreNumbers()
    {
        var json = WireframeSerializer.Serialize(new WireframeDocument { Width = 1440, Height = 900 });
        using var doc = JsonDocument.Parse(json);

        var page = doc.RootElement.GetProperty("pages")[0];
        page.GetProperty("width").ValueKind.Should().Be(JsonValueKind.Number);
        page.GetProperty("height").ValueKind.Should().Be(JsonValueKind.Number);
        page.GetProperty("width").GetDouble().Should().Be(1440);
        page.GetProperty("height").GetDouble().Should().Be(900);
    }

    [Fact]
    public void Serialize_ThenDeserialize_DocumentDimensionsPreserved()
    {
        var original = new WireframeDocument { Width = 1920, Height = 1080 };
        var json = WireframeSerializer.Serialize(original);
        var restored = WireframeSerializer.Deserialize(json);

        restored.Width.Should().Be(1920);
        restored.Height.Should().Be(1080);
    }

    [Fact]
    public void ComponentSchema_RolesSerializeAsCamelCaseAndRoundTrip()
    {
        var schema = new WireframeComponentSchema
        {
            Type = "TmSearchInput",
            Category = "Inputs",
            DisplayName = "Search input",
            Roles = ["search-input", "text-input"]
        };

        var json = JsonSerializer.Serialize(schema, WireframeJsonOptions.Default);
        var restored = JsonSerializer.Deserialize<WireframeComponentSchema>(json, WireframeJsonOptions.Default);

        json.Should().Contain("\"roles\"");
        json.Should().NotContain("\"Roles\"");
        restored!.Roles.Should().Equal("search-input", "text-input");
    }

    [Fact]
    public void ComponentSchema_IsContainerSerializeAsCamelCaseAndRoundTrip()
    {
        var schema = new WireframeComponentSchema
        {
            Type = "TmCard",
            Category = "Data Display",
            DisplayName = "Card",
            IsContainer = true
        };

        var json = JsonSerializer.Serialize(schema, WireframeJsonOptions.Default);
        var restored = JsonSerializer.Deserialize<WireframeComponentSchema>(json, WireframeJsonOptions.Default);

        json.Should().Contain("\"isContainer\"");
        json.Should().NotContain("\"IsContainer\"");
        restored!.IsContainer.Should().BeTrue();
    }
}
