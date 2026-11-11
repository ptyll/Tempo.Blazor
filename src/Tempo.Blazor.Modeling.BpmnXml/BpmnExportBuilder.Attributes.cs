using System.Text.Json;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Attribute and namespace-declaration half of <see cref="BpmnExportBuilder"/>: the
/// <c>bpmn:*</c> / <c>{prefix}:*</c> property catch-all written as XML attributes, the tool
/// plugin attribute-ownership check and the deterministic extension-namespace declaration on the
/// document root. Split out of <c>BpmnExportBuilder.Elements.cs</c> to stay under the 1200-line
/// source ceiling (N270/F13).
/// </summary>
internal sealed partial class BpmnExportBuilder
{
    /// <summary>
    /// Boolean event attributes: the importer keeps both a typed property and a verbatim
    /// <c>bpmn:*</c> catch-all string — the string wins so the re-imported model keeps both.
    /// </summary>
    private static void WriteBooleanAttribute(XElement node, Dictionary<string, JsonElement> properties,
        string booleanKey, string catchAllKey)
    {
        if (TryGetString(properties, catchAllKey, out var raw))
        {
            node.Add(new XAttribute(catchAllKey["bpmn:".Length..], raw));
        }
        else if (properties.TryGetValue(booleanKey, out var value) && value.ValueKind == JsonValueKind.True)
        {
            node.Add(new XAttribute(booleanKey, "true"));
        }
    }

    private void WriteCatchAllAttributes(XElement node, ModelingElementDto element)
    {
        foreach (var (key, value) in element.Properties)
        {
            if (NativelyHandledProperties.Contains(key)
                || key.StartsWith("bpmndi:", StringComparison.Ordinal)
                || key.StartsWith(_options.HostExtensionPrefix + ":", StringComparison.Ordinal)
                || IsToolOwnedAttributeKey(key))
            {
                continue;
            }

            WritePrefixedPropertyAttribute(node, element.Id, key, value);
        }
    }

    /// <summary>
    /// A <c>{prefix}:*</c> property key whose prefix maps (via
    /// <see cref="BpmnExportOptions.ExtensionNamespaces"/>) to the namespace of a registered tool
    /// plugin that <see cref="IBpmnToolExtension.OwnsAttributeExport">owns its attribute export</see>
    /// — the plugin serializes the value in the dialect-native payload shape instead.
    /// </summary>
    private bool IsToolOwnedAttributeKey(string key)
    {
        var prefix = KeyPrefix(key);
        if (prefix is null
            || !_options.ExtensionNamespaces.TryGetValue(prefix, out var uri))
        {
            return false;
        }

        return _options.ToolExtensions.Any(extension =>
            extension.OwnsAttributeExport
            && string.Equals(extension.NamespaceUri, uri, StringComparison.Ordinal));
    }

    /// <summary>
    /// Writes one <c>bpmn:*</c> / <c>{prefix}:*</c> property as an XML attribute, guarding the
    /// NCName validity (an invalid name is a reported loss instead of an exception — finding 6)
    /// and skipping the names <see cref="WriteMappedAttributes"/> already wrote natively.
    /// </summary>
    private void WritePrefixedPropertyAttribute(XElement node, string? elementId, string key, JsonElement value)
    {
        if (key.StartsWith("bpmn:", StringComparison.Ordinal))
        {
            var localName = key["bpmn:".Length..];
            if (NativelyWrittenAttributeNames.Contains(localName))
            {
                return;
            }

            if (!IsValidNCName(localName))
            {
                AddLoss(elementId, "Property",
                    $"The property '{key}' is not a valid XML attribute name and cannot be written on the element.");
                return;
            }

            // Legacy Camunda dialect attributes (unprefixed in old exports): the strict BPMN
            // schema only knows them in the Camunda namespace — write them there (declared on
            // the root) so schema-validating consumers accept the document (bpmn-moddle interop).
            if (localName is "calledElementBinding" or "calledElementVersion")
            {
                DeclareExtensionNamespace("camunda", ExtensionNamespaceUri("camunda"));
                node.Add(new XAttribute(
                    XNamespace.Get(ExtensionNamespaceUri("camunda")) + localName,
                    SanitizeXmlText(PropertyText(value), elementId, "Property")));
                return;
            }

            node.Add(new XAttribute(localName, SanitizeXmlText(PropertyText(value), elementId, "Property")));
            return;
        }

        var prefix = KeyPrefix(key);
        if (prefix is not null)
        {
            var localName = key[(prefix.Length + 1)..];
            if (!IsValidNCName(prefix) || !IsValidNCName(localName))
            {
                AddLoss(elementId, "Property",
                    $"The property '{key}' is not a valid XML attribute name and cannot be written on the element.");
                return;
            }

            DeclareExtensionNamespace(prefix, ExtensionNamespaceUri(prefix));
            node.Add(new XAttribute(
                XNamespace.Get(ExtensionNamespaceUri(prefix)) + localName,
                SanitizeXmlText(PropertyText(value), elementId, "Property")));
        }

        // A plain (colon-less) property without a native representation: WithHostExtensions
        // carries it in the host payload (see WriteExtensionElements); Standard drops it with
        // a loss recorded there.
    }

    private static string? KeyPrefix(string key)
    {
        var colon = key.IndexOf(':');
        return colon > 0 ? key[..colon] : null;
    }

    private static string PropertyText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private string ExtensionNamespaceUri(string prefix) =>
        _options.ExtensionNamespaces.TryGetValue(prefix, out var uri)
            ? uri
            : "urn:tempo-blazor:bpmn:" + prefix;

    /// <summary>
    /// Declares <paramref name="uri"/> on the document root under <paramref name="prefix"/>,
    /// renaming the prefix deterministically (<c>{prefix}_1</c>, …) when it collides with a
    /// reserved prefix (<c>bpmndi</c>/<c>dc</c>/<c>di</c>/<c>xsi</c>/<c>xml</c>/host) or a
    /// namespace already declared under it (finding 8). Returns the prefix the namespace
    /// ended up with.
    /// </summary>
    private string DeclareExtensionNamespace(string prefix, string uri)
    {
        if (_prefixByUri.TryGetValue(uri, out var existing))
        {
            return existing;
        }

        var resolved = ResolvePrefixCollision(prefix);
        _prefixByUri[uri] = resolved;
        _root.Add(new XAttribute(XNamespace.Xmlns + resolved, uri));

        if (!string.Equals(resolved, prefix, StringComparison.Ordinal))
        {
            AddLoss(null, "Namespace",
                $"The attribute prefix '{prefix}' is reserved or already declared for another namespace; the URI was declared as '{resolved}' instead.",
                BpmnExportLossSeverity.Info);
        }

        if (!_options.ExtensionNamespaces.TryGetValue(prefix, out var knownUri)
            || !string.Equals(knownUri, uri, StringComparison.Ordinal))
        {
            AddLoss(null, "Namespace",
                $"No namespace URI is known for the '{prefix}' attribute prefix; declared deterministically as '{uri}'.",
                BpmnExportLossSeverity.Info);
        }

        return resolved;
    }

    private string ResolvePrefixCollision(string prefix)
    {
        if (!ReservedPrefixes.Contains(prefix) && _root.Attribute(XNamespace.Xmlns + prefix) is null)
        {
            return prefix;
        }

        for (var index = 1; ; index++)
        {
            var candidate = prefix + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ReservedPrefixes.Contains(candidate) && _root.Attribute(XNamespace.Xmlns + candidate) is null)
            {
                return candidate;
            }
        }
    }
}
