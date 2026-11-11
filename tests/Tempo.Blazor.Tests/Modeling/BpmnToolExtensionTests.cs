using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5: the built-in tool dialect plugins (Camunda/Signavio/Bizagi) map their
/// <c>extensionElements</c> payloads into typed properties and restore them on export — a full
/// import → export → import cycle must keep every tool property. The plain parameterless
/// importer stays plugin-free (the corpus goldens prove it), so plugin behaviour is covered here
/// with explicitly registered plugins and via the DI fallback.
/// </summary>
public sealed class BpmnToolExtensionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static BpmnImportOptions OptionsWithBuiltInPlugins() =>
        new() { ToolExtensions = { BpmnBuiltInToolExtensions.All[0], BpmnBuiltInToolExtensions.All[1], BpmnBuiltInToolExtensions.All[2] } };

    [Fact]
    public void Built_in_plugins_cover_the_three_dialect_namespaces_in_fixed_order()
    {
        BpmnBuiltInToolExtensions.All.Select(extension => extension.NamespaceUri).Should().Equal(
            "http://camunda.org/schema/1.0/bpmn",
            "http://www.signavio.com",
            "http://www.bizagi.com/schemas/2010/Bizagi");
    }

    // ---------------------------------------------------------------- Camunda

    [Fact]
    public void Camunda_import_maps_properties_documentation_and_preserves_other_payloads()
    {
        var result = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("camunda-export.bpmn"), OptionsWithBuiltInPlugins());
        result.HasErrors.Should().BeFalse(result.ErrorMessage);

        var process = result.Models.Should().ContainSingle().Subject;
        // camunda:properties at process level ⇒ model properties.
        process.Properties.Should().ContainKeys("camunda:department", "camunda:criticality");

        var approve = process.Elements.Should().Contain(element => element.Id == "Task_Approve").Subject;
        // Attributes stay the attribute catch-all's job.
        approve.Properties.Should().ContainKeys("camunda:assignee", "camunda:dueDate");
        // taskListener + inputOutput payloads are preserved verbatim for a loss-free re-export.
        approve.Properties["camundaExtensions"].GetString().Should().Contain("taskListener").And.Contain("inputOutput");

        var book = process.Elements.Should().Contain(element => element.Id == "Task_Book").Subject;
        book.Properties["camundaExtensions"].GetString().Should().Contain("connector");

        var reviewPool = process.Elements.Should().Contain(element => element.Id == "Task_ReviewPool").Subject;
        reviewPool.Properties.Should().ContainKeys("camunda:candidateGroups", "camunda:candidateUsers");
    }

    [Fact]
    public void Camunda_documentation_payload_extends_the_element_description()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:camunda="http://camunda.org/schema/1.0/bpmn"
                         id="Defs" targetNamespace="http://tempo.example/t">
              <process id="P" isExecutable="false">
                <task id="T" name="T">
                  <documentation>Native docs</documentation>
                  <extensionElements>
                    <camunda:documentation>Camunda docs</camunda:documentation>
                  </extensionElements>
                </task>
              </process>
            </definitions>
            """;
        var result = new BpmnXmlImporter().Import(xml, OptionsWithBuiltInPlugins());
        result.HasErrors.Should().BeFalse();
        result.Models[0].Elements.Should().ContainSingle()
            .Which.Description.Should().Be("Native docs\nCamunda docs");
    }

    // ---------------------------------------------------------------- Signavio

    [Fact]
    public void Signavio_import_maps_element_metadata_and_diagram_metadata()
    {
        var result = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("signavio-export.bpmn"), OptionsWithBuiltInPlugins());
        result.HasErrors.Should().BeFalse(result.ErrorMessage);

        var model = result.Models.Should().ContainSingle().Subject;
        // Diagram-level meta data rides on the model (the process IS the model).
        model.Properties.Should().ContainKeys("signavio:revision", "signavio:author");

        var validate = model.Elements.Should().Contain(element => element.Id == "Task_Validate").Subject;
        validate.Properties.Should().ContainKey("signavio:meta-role").WhoseValue.GetString().Should().Be("Compliance");

        var decide = model.Elements.Should().Contain(element => element.Id == "Task_Decide").Subject;
        decide.Properties.Should().ContainKey("signavio:meta-sla").WhoseValue.GetString().Should().Be("P1D");
        // The assignmentRule ATTRIBUTE is normalized to the canonical lowercase prefix.
        decide.Properties.Should().ContainKey("signavio:assignmentRule").WhoseValue.GetString().Should().Be("round-robin");
    }

    // ---------------------------------------------------------------- Bizagi

    [Fact]
    public void Bizagi_import_maps_property_payloads_and_normalizes_the_attribute_prefix()
    {
        var result = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("bizagi-export.bpmn"), OptionsWithBuiltInPlugins());
        result.HasErrors.Should().BeFalse(result.ErrorMessage);

        var model = result.Models.Should().ContainSingle().Subject;
        model.Properties.Should().ContainKeys("bizagi:BizagiVersion", "bizagi:DiagramId");

        var collect = model.Elements.Should().Contain(element => element.Id == "Task_CollectDocs").Subject;
        collect.Properties.Should().ContainKey("bizagi:FormId").WhoseValue.GetString().Should().Be("frm-collect");
        collect.Properties.Should().ContainKey("bizagi:ApplicationId").WhoseValue.GetString().Should().Be("onboarding-app");

        // The Bizagi:property attribute becomes the canonical 'bizagi:property' key (design §6).
        var start = model.Elements.Should().Contain(element => element.Id == "StartEvent_NewCustomer").Subject;
        start.Properties.Should().ContainKey("bizagi:property").WhoseValue.GetString().Should().Be("displayName:Start;category:Events");
    }

    // ---------------------------------------------------------------- round-trips

    [Theory]
    [InlineData("camunda-export.bpmn")]
    [InlineData("signavio-export.bpmn")]
    [InlineData("bizagi-export.bpmn")]
    [InlineData("collaboration-3-pools.bpmn")]
    public void Tool_properties_survive_import_export_reimport(string fixture)
    {
        var options = OptionsWithBuiltInPlugins();
        var first = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture), options);
        first.HasErrors.Should().BeFalse(first.ErrorMessage);

        var export = new BpmnXmlExporter().Export(first.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions, ToolExtensions = { options.ToolExtensions[0], options.ToolExtensions[1], options.ToolExtensions[2] } });
        export.Success.Should().BeTrue(export.ErrorMessage);

        // The dialect namespaces are declared on the root with their canonical prefixes.
        var prefixes = new[]
        {
            "xmlns:camunda=\"http://camunda.org/schema/1.0/bpmn\"",
            "xmlns:signavio=\"http://www.signavio.com\"",
            "xmlns:bizagi=\"http://www.bizagi.com/schemas/2010/Bizagi\"",
        };
        foreach (var prefix in prefixes)
        {
            if (BpmnCorpusPaths.ReadFixture(fixture).Contains(prefix.Split(':')[1].Split('"')[0] + ':', StringComparison.Ordinal))
            {
                export.Xml.Should().Contain(prefix);
            }
        }

        var second = new BpmnXmlImporter().Import(export.Xml!, options);
        second.HasErrors.Should().BeFalse(second.ErrorMessage);

        var firstKeys = ToolKeys(first.Models).ToHashSet(StringComparer.Ordinal);
        var secondKeys = ToolKeys(second.Models).ToHashSet(StringComparer.Ordinal);
        secondKeys.Should().BeEquivalentTo(firstKeys);
    }

    private static IEnumerable<string> ToolKeys(IEnumerable<ModelingModelDto> models)
    {
        foreach (var model in models)
        {
            foreach (var (key, value) in model.Properties)
            {
                if (key.StartsWith("camunda:", StringComparison.Ordinal) || key.StartsWith("signavio:", StringComparison.Ordinal) || key.StartsWith("bizagi:", StringComparison.Ordinal))
                {
                    yield return $"model:{key}={PropertyText(value)}";
                }
            }

            foreach (var element in model.Elements)
            {
                foreach (var (key, value) in element.Properties)
                {
                    if (key.StartsWith("camunda:", StringComparison.Ordinal) || key.StartsWith("camundaExtensions", StringComparison.Ordinal)
                        || key.StartsWith("signavio:", StringComparison.Ordinal)
                        || key.StartsWith("bizagi:", StringComparison.Ordinal))
                    {
                        yield return $"{element.Id}:{key}={PropertyText(value)}";
                    }
                }
            }
        }
    }

    private static string PropertyText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    // ---------------------------------------------------------------- export de-duplication (finding 3)

    [Fact]
    public void Export_deduplicates_tool_plugins_by_namespace_first_wins()
    {
        var model = new ModelingModelDto { Id = "dedup", Title = "Dedup", Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto { Id = "task-1", SemanticType = "UserTask", Name = "Approve", Notation = "bpmn2" });
        model.Elements[0].Properties["signavio:meta-role"] = JsonSerializer.SerializeToElement("Compliance");

        // The same built-in plugins supplied BOTH explicitly and through the DI fallback must run
        // exactly once per namespace — the merged list is de-duplicated by NamespaceUri.
        var doubled = new BpmnExportOptions();
        foreach (var plugin in BpmnBuiltInToolExtensions.All)
        {
            doubled.ToolExtensions.Add(plugin);
        }

        var doubledResult = new BpmnXmlExporter(BpmnBuiltInToolExtensions.All).Export(model, doubled);
        doubledResult.Success.Should().BeTrue(doubledResult.ErrorMessage);
        CountOccurrences(doubledResult.Xml!, "signavioMetaData").Should().Be(1);

        // An explicit plugin for a namespace the DI fallback also supplies wins (explicit options
        // first, then DI fallback): the built-in Signavio payload is not written at all.
        var overridden = new BpmnExportOptions();
        overridden.ToolExtensions.Add(new MarkerSignavioPlugin());
        var overriddenResult = new BpmnXmlExporter(BpmnBuiltInToolExtensions.All).Export(model, overridden);
        overriddenResult.Success.Should().BeTrue(overriddenResult.ErrorMessage);
        overriddenResult.Xml.Should().Contain("<signavio:marker", "the explicit plugin owns the namespace");
        overriddenResult.Xml.Should().NotContain("signavioMetaData", "the DI fallback plugin lost the first-wins race");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    /// <summary>A test-only Signavio-namespace plugin proving an explicit plugin overrides the DI
    /// fallback for the same NamespaceUri (export merge is first-wins by namespace).</summary>
    private sealed class MarkerSignavioPlugin : IBpmnToolExtension
    {
        public string NamespaceUri => BpmnSignavioToolExtension.NamespaceUriConstant;

        public bool OwnsAttributeExport => true;

        public void OnImportElement(BpmnToolImportContext context)
        {
        }

        public void OnExportElement(BpmnToolExportContext context) =>
            context.ExtensionElements.Add(new System.Xml.Linq.XElement(
                System.Xml.Linq.XNamespace.Get(NamespaceUri) + "marker",
                new System.Xml.Linq.XAttribute("metaKey", "explicit-wins")));
    }

    // ---------------------------------------------------------------- defaults stay plain

    [Fact]
    public void Parameterless_importer_keeps_reporting_unrecognized_tool_namespaces()
    {
        var result = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("camunda-export.bpmn"));
        result.HasErrors.Should().BeFalse();
        result.Losses.Should().Contain(loss =>
            loss.Severity == BpmnImportLossSeverity.Loss
            && loss.Reason.Contains("Unrecognized extension namespace 'http://camunda.org/schema/1.0/bpmn'", StringComparison.Ordinal));
    }

    [Fact]
    public void Explicitly_configured_tool_extensions_win_over_the_di_fallback()
    {
        var services = new ServiceCollection().AddTempoBpmnXml();
        var provider = services.BuildServiceProvider();
        var importer = provider.GetRequiredService<IBpmnXmlImporter>();

        // DI fallback: the resolved importer applies the built-in plugins by itself.
        var viaDi = importer.Import(BpmnCorpusPaths.ReadFixture("camunda-export.bpmn"));
        viaDi.Losses.Should().NotContain(loss => loss.Reason.Contains("Unrecognized extension namespace", StringComparison.Ordinal));
        viaDi.Models[0].Elements.Should().Contain(element => element.Id == "Task_Approve")
            .Which.Properties.Should().ContainKey("camundaExtensions");

        // An explicitly populated options list wins over the fallback.
        var explicitPlugins = new BpmnImportOptions();
        explicitPlugins.ToolExtensions.Add(new BpmnSignavioToolExtension());
        var explicitResult = importer.Import(BpmnCorpusPaths.ReadFixture("camunda-export.bpmn"), explicitPlugins);
        explicitResult.Losses.Should().Contain(loss =>
            loss.Reason.Contains("Unrecognized extension namespace 'http://camunda.org/schema/1.0/bpmn'", StringComparison.Ordinal));

        provider.GetServices<IBpmnToolExtension>().Select(extension => extension.GetType()).Should().Equal(
            typeof(BpmnCamundaToolExtension), typeof(BpmnSignavioToolExtension), typeof(BpmnBizagiToolExtension));
    }
}
