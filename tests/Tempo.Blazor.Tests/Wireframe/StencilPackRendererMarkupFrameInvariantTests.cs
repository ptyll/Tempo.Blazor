using System.Text.Json;
using System.Xml;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// FZE-1 render-tree invariant: every <see cref="RenderTreeFrameType.Markup"/> frame a stencil
/// definition emits MUST be a complete, standalone well-formed XML fragment.
///
/// Blazor parses each <c>AddMarkupContent</c> chunk into its own DOM template, so an unclosed
/// <c>&lt;g transform=…&gt;</c> frame is auto-closed by the browser and its children become
/// SIBLINGS — every repeat/group/part then paints collapsed in the editor, the toolbox preview
/// and the client-side export, while the server-side string renderer (which only concatenates
/// frames) and every string-asserting bUnit suite stay green. The server renderer and bUnit
/// markup strings can therefore never catch this defect; only a per-frame parse can.
///
/// This spec renders EVERY definition the <see cref="BuiltInStencilPackProvider"/> ships
/// (declarative pack components, native renderers and part references) plus a
/// <see cref="JsonWireframeComponentProvider"/> sample, walks <c>GetFrames()</c> and parses each
/// markup frame as <c>&lt;root&gt;{markup}&lt;/root&gt;</c> with DTD processing prohibited. Any
/// orphan, unclosed or mismatched tag fails with the component type and the offending frame
/// content. Runs twice per definition: with schema default props and with sample
/// <c>options</c>/<c>items</c>/<c>steps</c> props so the <c>repeat</c> branches are exercised.
/// </summary>
public class StencilPackRendererMarkupFrameInvariantTests
{
    [Fact]
    public void BuiltInDefinitions_WithDefaultProps_EmitOnlyWellFormedMarkupFrames()
        => AssertWellFormedFrames(BuildElements(includeSampleProps: false));

    [Fact]
    public void BuiltInDefinitions_WithSampleProps_EmitOnlyWellFormedMarkupFrames()
        => AssertWellFormedFrames(BuildElements(includeSampleProps: true));

    [Fact]
    public void JsonProviderDefinitions_EmitOnlyWellFormedMarkupFrames()
    {
        var provider = new JsonWireframeComponentProvider();
        provider.LoadFromJson(
            """
            [
              {
                "type": "CustomCard",
                "displayName": "Custom Card",
                "category": "Custom",
                "defaultWidth": 200,
                "defaultHeight": 120,
                "svgTemplate": "<g transform='translate(4,4)'><rect width='{{w}}' height='{{h}}' rx='8' fill='#ffffff'/><text x='10' y='24'>{{props.title}}</text></g>"
              }
            ]
            """);

        var elements = provider.GetDefinitions()
            .Select(def => (def.Type, Def: def, Element: BuildElement(def, includeSampleProps: false)))
            .ToArray();

        AssertWellFormedFrames(elements);
    }

    // ── Enumeration ──────────────────────────────────────────────────────────

    private static IReadOnlyList<(string Type, WireframeComponentDef Def, WireframeElement Element)> BuildElements(bool includeSampleProps)
        => new BuiltInStencilPackProvider().GetDefinitions()
            .Select(def => (def.Type, Def: def, Element: BuildElement(def, includeSampleProps)))
            .ToArray();

    private static WireframeElement BuildElement(WireframeComponentDef def, bool includeSampleProps)
    {
        var element = new WireframeElement
        {
            Type = def.Type,
            W = def.DefaultWidth,
            H = def.DefaultHeight
        };

        foreach (var prop in def.Props)
        {
            var sample = includeSampleProps ? SampleValueFor(prop.Name) : null;
            if (sample is not null)
            {
                element.Props[prop.Name] = sample.Value;
                continue;
            }

            if (prop.Default is not null)
                element.Props[prop.Name] = JsonSerializer.SerializeToElement(prop.Default);
        }

        return element;
    }

    /// <summary>
    /// Sample values for the collection props that drive <c>repeat</c> branches; everything else
    /// keeps its schema default so the render pass exercises the same paths a designer sees.
    /// </summary>
    private static JsonElement? SampleValueFor(string propName)
        => propName.Trim() switch
        {
            "options" => JsonSerializer.SerializeToElement(new[] { "Day", "Week", "Month", "Year" }),
            "items" => JsonSerializer.SerializeToElement(new[] { "First", "Second", "Third" }),
            "steps" => JsonSerializer.SerializeToElement(new[] { "Upload", "Map", "Preview", "Done" }),
            _ => null
        };

    // ── Frame walk ───────────────────────────────────────────────────────────

    private static void AssertWellFormedFrames(IReadOnlyList<(string Type, WireframeComponentDef Def, WireframeElement Element)> elements)
    {
        var violations = new List<string>();

        foreach (var (type, def, element) in elements)
            violations.AddRange(FindViolations(type, def, element));

        violations.Should().BeEmpty(
            "every markup frame must be a standalone well-formed XML fragment " +
            "(a split <g> open/close pair auto-closes in the browser and collapses the sketch). " +
            $"Affected components: {string.Join(", ", violations.Select(v => v[..v.IndexOf(':')]).Distinct().Order())}. " +
            $"Violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IReadOnlyList<string> FindViolations(
        string type,
        WireframeComponentDef def,
        WireframeElement element)
    {
        var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "svg");
        def.RenderSvg(element, builder);
        builder.CloseElement();

        var frames = builder.GetFrames();
        var violations = new List<string>();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType != RenderTreeFrameType.Markup)
                continue;

            var error = ParseError(frame.MarkupContent);
            if (error is not null)
            {
                violations.Add(
                    $"{type}: markup frame {i} is not a well-formed fragment: {error}. " +
                    $"Frame content: {Truncate(frame.MarkupContent)}");
            }
        }

        return violations;
    }

    private static string? ParseError(string markup)
    {
        try
        {
            using var reader = XmlReader.Create(
                new StringReader("<root>" + markup + "</root>"),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            while (reader.Read())
            {
            }

            return null;
        }
        catch (XmlException ex)
        {
            return ex.Message;
        }
    }

    private static string Truncate(string value)
        => value.Length <= 160 ? value : value[..157] + "…";
}
