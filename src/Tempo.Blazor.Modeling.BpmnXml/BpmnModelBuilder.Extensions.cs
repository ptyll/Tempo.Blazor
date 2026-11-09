using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>Partial of <see cref="BpmnModelBuilder"/> — extension elements (host and foreign namespaces), global definitions and the unused-global report.</summary>
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

    // ---------------------------------------------------------------- global definitions

    private sealed class GlobalDefinition
    {
        public required string Kind { get; init; }
        public required string Name { get; init; }
        public required string Code { get; init; }
    }

    private void MarkGlobalUsed(ModelingModelDto model, string definitionId)
    {
        _usedGlobalDefinitions.Add(definitionId);
        if (!_usedGlobalDefinitionsByModel.TryGetValue(model, out var used))
        {
            used = new HashSet<string>(StringComparer.Ordinal);
            _usedGlobalDefinitionsByModel[model] = used;
        }

        used.Add(definitionId);
    }

    /// <summary>
    /// Attaches the referenced global message/signal/error/escalation definitions to each model as
    /// <c>Properties["bpmnDefinitions"]</c> (canonical JSON, declaration order) so the export phase
    /// can rebuild the <c>&lt;definitions&gt;</c> root after a clean import → export cycle (design §4).
    /// </summary>
    private void AttachGlobalDefinitions()
    {
        foreach (var (model, used) in _usedGlobalDefinitionsByModel)
        {
            var definitions = new List<JsonObject>();
            foreach (var (id, definition) in _globalDefinitions)
            {
                if (!used.Contains(id) || definition.Kind is not ("message" or "signal" or "error" or "escalation"))
                {
                    continue;
                }

                definitions.Add(new JsonObject
                {
                    ["kind"] = definition.Kind,
                    ["id"] = id,
                    ["name"] = definition.Name,
                    ["code"] = definition.Code,
                });
            }

            if (definitions.Count > 0)
            {
                model.Properties["bpmnDefinitions"] = JsonSerializer.SerializeToElement(definitions);
            }
        }
    }

    private void CollectGlobalDefinitions(XElement root)
    {
        foreach (var element in root.Elements())
        {
            var localName = element.Name.LocalName;
            if (!GlobalDefinitionTypes.Contains(localName, StringComparer.Ordinal))
            {
                continue;
            }

            if (localName == "category")
            {
                foreach (var categoryValue in BpmnImportInternals.Children(element, "categoryValue"))
                {
                    if (categoryValue.Attribute("id") is { } id)
                    {
                        _globalDefinitions[id.Value] = new GlobalDefinition
                        {
                            Kind = "categoryValue",
                            Name = categoryValue.Attribute("value")?.Value ?? string.Empty,
                            Code = string.Empty,
                        };
                    }
                }

                continue;
            }

            if (element.Attribute("id") is not { } elementId)
            {
                continue;
            }

            var code = element.Attribute("errorCode")?.Value
                ?? element.Attribute("escalationCode")?.Value
                ?? string.Empty;
            _globalDefinitions[elementId.Value] = new GlobalDefinition
            {
                Kind = localName,
                Name = element.Attribute("name")?.Value ?? string.Empty,
                Code = code,
            };
        }
    }

    private void ReportUnusedGlobalDefinitions()
    {
        foreach (var (id, definition) in _globalDefinitions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (_usedGlobalDefinitions.Contains(id))
            {
                continue;
            }

            if (definition.Kind is "itemDefinition" or "dataStore" or "categoryValue")
            {
                // dataStore/itemDefinition/categoryValue are covered by their referencing elements.
                if (definition.Kind == "itemDefinition")
                {
                    _losses.Add(new BpmnImportLoss
                    {
                        ElementId = id,
                        XmlPath = $"/definitions/{definition.Kind}[@id='{id}']",
                        Construct = definition.Kind,
                        Reason = "Global definition is declared but never used; it is not preserved (nothing references it).",
                        Severity = BpmnImportLossSeverity.Info,
                    });
                }

                continue;
            }

            _losses.Add(new BpmnImportLoss
            {
                ElementId = id,
                XmlPath = $"/definitions/{definition.Kind}[@id='{id}']",
                Construct = definition.Kind,
                Reason = "Global definition is declared but never used; the export phase can only restore definitions referenced from the model.",
                Severity = BpmnImportLossSeverity.Info,
            });
        }
    }
}
