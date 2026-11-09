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
