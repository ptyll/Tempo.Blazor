using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Part of <see cref="BpmnModelBuilder"/> split out to stay under the 1200-line source ceiling (N270/F13).
/// </summary>
internal sealed partial class BpmnModelBuilder
{

    // ---------------------------------------------------------------- extensions

    private void ApplyExtensionElements(XElement extensionElements, ModelingElementDto? node, ModelingModelDto model)
    {
        // Plugins are invoked exactly once per (extensionElements, namespace) — the payload is the
        // whole extensionElements element and the plugin filters what it owns.
        foreach (var group in extensionElements.Elements().GroupBy(payload => payload.Name.NamespaceName))
        {
            var ns = group.Key;
            var plugin = _options.ToolExtensions.FirstOrDefault(extension =>
                string.Equals(extension.NamespaceUri, ns, StringComparison.Ordinal));
            if (plugin is not null)
            {
                plugin.OnImportElement(new BpmnToolImportContext
                {
                    ExtensionElements = extensionElements,
                    Element = node,
                    Model = model,
                    ReportLoss = loss => _losses.Add(loss),
                });
                continue;
            }

            if (ns == HostExtensionNamespace)
            {
                foreach (var payload in group)
                {
                    ImportHostExtensionProperties(payload, node);
                }

                continue;
            }

            foreach (var payload in group)
            {
                PreserveForeignExtension(payload, node, ns);
            }
        }
    }

    /// <summary>
    /// Definitions-root <c>extensionElements</c>: host-namespace payloads restore model-level
    /// properties (the exporter writes them there in the WithHostExtensions profile); tool plugins
    /// get their namespace routed like anywhere else; unknown namespaces are reported instead of
    /// silently dropped.
    /// </summary>
    private void ApplyRootExtensionElements(XElement root)
    {
        var extensionElements = BpmnImportInternals.Child(root, "extensionElements");
        if (extensionElements is null || _models.Count == 0)
        {
            return;
        }

        var model = _models[0];
        foreach (var group in extensionElements.Elements().GroupBy(payload => payload.Name.NamespaceName))
        {
            var ns = group.Key;
            var plugin = _options.ToolExtensions.FirstOrDefault(extension =>
                string.Equals(extension.NamespaceUri, ns, StringComparison.Ordinal));
            if (plugin is not null)
            {
                plugin.OnImportElement(new BpmnToolImportContext
                {
                    ExtensionElements = extensionElements,
                    Element = null,
                    Model = model,
                    ReportLoss = loss => _losses.Add(loss),
                });
                continue;
            }

            if (ns == HostExtensionNamespace)
            {
                foreach (var payload in group)
                {
                    ImportHostExtensionProperties(payload, model);
                }

                continue;
            }

            foreach (var payload in group)
            {
                _losses.Add(new BpmnImportLoss
                {
                    ElementId = null,
                    XmlPath = BpmnImportInternals.BuildPath(payload),
                    Construct = "extensionElements",
                    Reason = $"Definitions-root extension element in namespace '{ns}' has no model element to attach to; content: {payload.ToString(SaveOptions.DisableFormatting)}",
                    Severity = BpmnImportLossSeverity.Loss,
                });
            }
        }
    }

    /// <summary>Model variant of the host property restore (definitions-root payloads).</summary>
    private static void ImportHostExtensionProperties(XElement payload, ModelingModelDto model)
    {
        foreach (var property in BpmnImportInternals.Children(payload, "property"))
        {
            var name = property.Attribute("name")?.Value;
            var value = property.Attribute("value")?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            try
            {
                model.Properties[name!] = JsonNode.Parse(value)?.Deserialize<JsonElement>()
                    ?? BpmnImportInternals.StringElement(value);
            }
            catch (JsonException)
            {
                model.Properties[name!] = BpmnImportInternals.StringElement(value);
            }
        }
    }

    /// <summary>Element variant of the host property restore: <c>tags</c>/<c>governance</c>
    /// payloads restore the typed DTO fields, everything else lands in Properties (finding 9).</summary>
    private void ImportHostExtensionProperties(XElement payload, ModelingElementDto? node)
    {
        if (node is null)
        {
            return;
        }

        foreach (var property in BpmnImportInternals.Children(payload, "property"))
        {
            var name = property.Attribute("name")?.Value;
            var value = property.Attribute("value")?.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.Equals(name, "tags", StringComparison.Ordinal))
            {
                if (TryParseJson(value, out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    node.Tags.Clear();
                    foreach (var tag in tags.EnumerateArray())
                    {
                        if (tag.ValueKind == JsonValueKind.String)
                        {
                            node.Tags.Add(tag.GetString() ?? string.Empty);
                        }
                    }
                }

                continue;
            }

            if (string.Equals(name, "governance", StringComparison.Ordinal))
            {
                if (TryParseJson(value, out var governance) && governance.ValueKind == JsonValueKind.Object)
                {
                    node.Governance = JsonSerializer.Deserialize<ModelingGovernanceDto>(governance.GetRawText()) ?? new ModelingGovernanceDto();
                }

                continue;
            }

            try
            {
                node.Properties[name!] = JsonNode.Parse(value)?.Deserialize<JsonElement>()
                    ?? BpmnImportInternals.StringElement(value);
            }
            catch (JsonException)
            {
                node.Properties[name!] = BpmnImportInternals.StringElement(value);
            }
        }
    }

    private static bool TryParseJson(string value, out JsonElement element)
    {
        element = default;
        if (value.Length == 0)
        {
            return false;
        }

        try
        {
            element = JsonNode.Parse(value)?.Deserialize<JsonElement>() ?? default;
            return element.ValueKind != JsonValueKind.Undefined;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void PreserveForeignExtension(XElement payload, ModelingElementDto? node, string ns)
    {
        if (node is not null)
        {
            if (!_foreignExtensions.TryGetValue(node, out var byNamespace))
            {
                byNamespace = [];
                _foreignExtensions[node] = byNamespace;
            }

            if (!byNamespace.TryGetValue(ns, out var accumulated))
            {
                accumulated = new StringBuilder();
                byNamespace[ns] = accumulated;
            }

            accumulated.Append(payload.ToString(SaveOptions.DisableFormatting));
        }

        _losses.Add(new BpmnImportLoss
        {
            ElementId = node?.Id,
            XmlPath = BpmnImportInternals.BuildPath(payload),
            Construct = "extensionElements",
            Reason = $"Unrecognized extension namespace '{ns}'; the content is preserved verbatim under 'foreignExtensions'.",
            Severity = BpmnImportLossSeverity.Loss,
        });
    }

    /// <summary>
    /// Relationship <c>extensionElements</c> (finding 9): the host payload restores typed fields
    /// (today: <c>tags</c>); every other namespace is reported — relationships have no element to
    /// hang a verbatim foreign payload on.
    /// </summary>
    private void ApplyRelationshipExtensionElements(XElement source, ModelingRelationshipDto relationship, ModelingModelDto model)
    {
        var extensionElements = BpmnImportInternals.Child(source, "extensionElements");
        if (extensionElements is null)
        {
            return;
        }

        foreach (var group in extensionElements.Elements().GroupBy(payload => payload.Name.NamespaceName))
        {
            var ns = group.Key;
            if (string.Equals(ns, HostExtensionNamespace, StringComparison.Ordinal))
            {
                foreach (var payload in group)
                {
                    foreach (var property in BpmnImportInternals.Children(payload, "property"))
                    {
                        var name = property.Attribute("name")?.Value;
                        var value = property.Attribute("value")?.Value ?? string.Empty;
                        if (!string.Equals(name, "tags", StringComparison.Ordinal)
                            || !TryParseJson(value, out var tags)
                            || tags.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        relationship.Tags.Clear();
                        foreach (var tag in tags.EnumerateArray())
                        {
                            if (tag.ValueKind == JsonValueKind.String)
                            {
                                relationship.Tags.Add(tag.GetString() ?? string.Empty);
                            }
                        }
                    }
                }

                continue;
            }

            foreach (var payload in group)
            {
                _losses.Add(new BpmnImportLoss
                {
                    ElementId = relationship.Id,
                    XmlPath = BpmnImportInternals.BuildPath(payload),
                    Construct = "extensionElements",
                    Reason = $"Relationship-level extension element in namespace '{ns}' is not preserved; content: {payload.ToString(SaveOptions.DisableFormatting)}",
                    Severity = BpmnImportLossSeverity.Loss,
                });
            }
        }
    }

    /// <summary>Writes the accumulated foreign extension payloads into each element once, at the end.</summary>
    private void FlushForeignExtensions()
    {
        foreach (var (node, byNamespace) in _foreignExtensions)
        {
            var foreign = new JsonObject();
            foreach (var (ns, accumulated) in byNamespace.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                foreign[ns] = accumulated.ToString();
            }

            node.Properties["foreignExtensions"] = BpmnImportInternals.JsonElementFrom(foreign);
        }
    }
}
