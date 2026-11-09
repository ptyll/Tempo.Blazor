using System.Text.Json;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Mcp.Wireframe;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>Tests for the wireframe validation engine and the validate tool.</summary>
public class WireframeValidationEngineTests
{
    private static WireframeSchemaRegistry Registry() => new([new BuiltInComponentSchemas()]);

    private static WireframeDocument DocWith(params WireframeElement[] elements)
    {
        var doc = new WireframeDocument { Title = "T" };
        doc.EnsureActivePage();
        doc.Elements.AddRange(elements);
        return doc;
    }

    private static string KnownType() => new WireframeSchemaRegistry([new BuiltInComponentSchemas()]).GetAll().First().Type;

    private static JsonElement Json(object value)
        => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Valid_Document_HasNoErrors()
    {
        var doc = DocWith(new WireframeElement { Type = KnownType(), X = 0, Y = 0, W = 100, H = 40 });

        var result = WireframeValidationEngine.Validate(doc, Registry());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void UnknownType_IsReported_WithSuggestion()
    {
        var known = KnownType();
        var doc = DocWith(new WireframeElement { Type = known + "X", W = 100, H = 40 });

        var result = WireframeValidationEngine.Validate(doc, Registry());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("unknown component type").And.Contain(known);
    }

    [Fact]
    public void UnknownType_UsesRoleSynonymSuggestion()
    {
        // "verification code" is a registered synonym of the otp-input role; the suggestion must
        // resolve through the role vocabulary to the role's unambiguous owner (finding 10: the
        // TmOtpInput schema now exists, so a bare "TmOtpInput" type would be KNOWN — the synonym
        // path needs an unknown spelling).
        var doc = DocWith(new WireframeElement { Type = "verification code", W = 100, H = 40 });

        var result = WireframeValidationEngine.Validate(doc, Registry());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("TmOtpInput");
    }

    [Fact]
    public void NonPositiveSize_IsReported()
    {
        var doc = DocWith(new WireframeElement { Type = KnownType(), W = 0, H = -5 });

        var result = WireframeValidationEngine.Validate(doc, Registry());

        result.Errors.Should().Contain(e => e.Contains(".w:"));
        result.Errors.Should().Contain(e => e.Contains(".h:"));
    }

    [Fact]
    public void UnknownProperty_IsReported_AsWarning_WithSuggestion()
    {
        var el = new WireframeElement { Type = KnownType(), W = 100, H = 40 };
        el.Props["childContent"] = Json("x");
        var doc = DocWith(el);

        var result = WireframeValidationEngine.Validate(doc, Registry());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w =>
            w.Code == "unknown-prop"
            && w.Hint.Contains("props.childContent")
            && w.Hint.Contains("Did you mean"));
    }

    [Fact]
    public void DuplicateElementId_IsReported()
    {
        var doc = DocWith(
            new WireframeElement { Id = "dup", Type = KnownType(), W = 100, H = 40 },
            new WireframeElement { Id = "dup", Type = KnownType(), W = 100, H = 40 });

        var result = WireframeValidationEngine.Validate(doc, Registry());

        result.Errors.Should().Contain(e => e.Contains("duplicate element id"));
    }

    [Fact]
    public void ConnectorToMissingElement_IsReported()
    {
        var doc = DocWith(new WireframeElement { Id = "a", Type = KnownType(), W = 100, H = 40 });
        doc.Connectors.Add(new WireframeConnector { FromId = "a", ToId = "ghost" });

        var result = WireframeValidationEngine.Validate(doc, Registry());

        result.Errors.Should().Contain(e => e.Contains("toId") && e.Contains("ghost"));
    }

    [Fact]
    public void EnumProperty_OutOfRange_IsReported_AsWarning_WhenSchemaHasEnumProp()
    {
        var registry = Registry();
        var enumSchema = registry.GetAll()
            .FirstOrDefault(s => s.Props.Any(p => p.Type == PropType.Enum && p.Options is { Length: > 0 }));
        if (enumSchema is null)
        {
            return; // no enum-typed component in the built-in set; nothing to assert
        }

        var enumProp = enumSchema.Props.First(p => p.Type == PropType.Enum && p.Options is { Length: > 0 });
        var el = new WireframeElement { Type = enumSchema.Type, W = 100, H = 40 };
        el.Props[enumProp.Name] = Json("___not_a_valid_option___");
        var doc = DocWith(el);

        var result = WireframeValidationEngine.Validate(doc, registry);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Contain(w =>
            w.Code == "enum-out-of-range"
            && w.Hint.Contains($"props.{enumProp.Name}")
            && w.Hint.Contains("not in"));
    }

    [Fact]
    public void ValidateTool_ReturnsValidFalse_NotAnErrorEnvelope()
    {
        var known = KnownType();
        var doc = DocWith(new WireframeElement { Type = known + "X", W = 100, H = 40 });
        var json = WireframeSerializer.Serialize(doc);

        var root = JsonDocument.Parse(WireframeValidationTools.ValidateDocument(Registry(), json)).RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("valid").GetBoolean().Should().BeFalse();
        root.GetProperty("validationErrors").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("warnings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ValidateTool_MalformedJson_ReturnsValidationFailedEnvelope()
    {
        var root = JsonDocument.Parse(WireframeValidationTools.ValidateDocument(Registry(), "{ not json")).RootElement;

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("validation_failed");
    }

    [Fact]
    public void Validate_WithScope_AcceptsMatchingScopedCustomAndRejectsOtherApp()
    {
        var appA = Guid.NewGuid().ToString("D");
        var appB = Guid.NewGuid().ToString("D");
        var registry = new WireframeSchemaRegistry(
        [
            new TestSchemaSource("BuiltIn", 0, Schema("TmButton", "Buttons", "Button", isBuiltIn: true)),
            new ScopedSchemaSource("A", 10, appA, Schema("InvoiceCard", "Custom", "A Invoice")),
            new ScopedSchemaSource("B", 10, appB, Schema("InvoiceCard", "Custom", "B Invoice"))
        ]);
        var doc = DocWith(
            new WireframeElement { Type = $"app:{appA}:InvoiceCard", W = 100, H = 40 },
            new WireframeElement { Type = $"app:{appB}:InvoiceCard", W = 100, H = 40 });

        var result = WireframeValidationEngine.Validate(
            doc,
            registry,
            WireframeComponentScope.ForApp(appA));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain($"app:{appB}:InvoiceCard");
    }

    [Fact]
    public void ValidateTool_WithScope_ResolvesLocalScopedCustomType()
    {
        var appA = Guid.NewGuid().ToString("D");
        var registry = new WireframeSchemaRegistry(
        [
            new ScopedSchemaSource("A", 10, appA, Schema("InvoiceCard", "Custom", "A Invoice"))
        ]);
        var doc = DocWith(new WireframeElement { Type = "InvoiceCard", W = 100, H = 40 });
        var json = WireframeSerializer.Serialize(doc);

        var root = JsonDocument.Parse(
            WireframeValidationTools.ValidateDocumentScoped(registry, json, scopeAppId: appA)).RootElement;

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Validate_RespectsDocumentTargetPackIds()
    {
        var appA = Guid.NewGuid().ToString("D");
        var registry = new WireframeSchemaRegistry(
        [
            new TestSchemaSource("BuiltIn", 0, Schema("TmButton", "Buttons", "Button", isBuiltIn: true)),
            new ScopedSchemaSource("A", 10, appA, Schema("InvoiceCard", "Custom", "A Invoice"))
        ]);
        var doc = new WireframeDocument { TargetPackIds = ["tempo"] };
        doc.EnsureActivePage();
        doc.Elements.Add(new WireframeElement { Type = $"app:{appA}:InvoiceCard", W = 100, H = 40 });

        var result = WireframeValidationEngine.Validate(doc, registry, WireframeComponentScope.ForApp(appA));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("unknown component type");
    }

    private static WireframeComponentSchema Schema(
        string type,
        string category,
        string displayName,
        bool isBuiltIn = false)
        => new()
        {
            Type = type,
            Category = category,
            DisplayName = displayName,
            IsBuiltIn = isBuiltIn,
            Props = []
        };

    private sealed class TestSchemaSource(string id, int priority, params WireframeComponentSchema[] schemas)
        : IWireframeSchemaSource
    {
        public string SourceId => id;
        public int Priority => priority;
        public IEnumerable<WireframeComponentSchema> GetSchemas() => schemas;
    }

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
