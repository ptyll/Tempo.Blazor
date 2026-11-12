using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Wireframe.Stencil;

namespace Tempo.Blazor.Components.Wireframe.Models;

/// <summary>
/// Describes a component that can be placed on the wireframe canvas.
/// Each Tempo.Blazor component (TmButton, TmDataTable, …) has exactly one definition.
/// Custom components register their own via <see cref="IWireframeComponentProvider"/>.
/// </summary>
public sealed class WireframeComponentDef
{
    /// <summary>
    /// Unique type identifier – must match <see cref="WireframeElement.Type"/>.
    /// Examples: "TmButton", "TmDataTable", "MyProductCard".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Application id for an app-scoped custom component. When present, registries normalize
    /// <see cref="Type"/> to <c>app:{ScopeAppId}:{LocalType}</c>.
    /// </summary>
    public string? ScopeAppId { get; init; }

    /// <summary>
    /// Component type name without the app scope prefix. Defaults to <see cref="Type"/> for
    /// unscoped components and to the suffix of <c>app:{id}:{name}</c> for scoped components.
    /// </summary>
    public string? LocalType { get; init; }

    /// <summary>Toolbox group (Buttons / Inputs / Layout / Custom / …).</summary>
    public required string Category { get; init; }

    /// <summary>Human-readable name shown in the Toolbox and Properties Panel header.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Library-agnostic UI roles this definition can satisfy.</summary>
    public IReadOnlyList<string>? Roles { get; init; }

    /// <summary>
    /// True when the component is intended to visually contain other placed elements.
    /// </summary>
    public bool IsContainer { get; init; }

    /// <summary>TmIcon name used in the Toolbox list item.</summary>
    public string? Icon { get; init; }

    /// <summary>Default width in pixels applied when dropping from the Toolbox.</summary>
    public double DefaultWidth { get; init; } = 120;

    /// <summary>Default height in pixels applied when dropping from the Toolbox.</summary>
    public double DefaultHeight { get; init; } = 36;

    /// <summary>Property definitions that drive the Properties Panel UI.</summary>
    public IReadOnlyList<PropDef> Props { get; init; } = [];

    /// <summary>
    /// Renders the wireframe SVG shape into <paramref name="builder"/>.
    /// Coordinates inside the render are relative to the element's top-left (0, 0).
    /// The outer <c>&lt;g&gt;</c> transform is applied by the canvas.
    /// <para>
    /// <b>Markup-frame contract:</b> Blazor parses each <c>AddMarkupContent</c> chunk into its OWN
    /// DOM template, so every markup frame MUST be a complete, standalone well-formed XML fragment —
    /// NEVER an opening tag in one frame and its closing tag in another. A split <c>&lt;g&gt;</c>
    /// auto-closes in the browser and its children become siblings, collapsing the sketch. To wrap
    /// children in a container, emit a real element (<c>OpenElement</c> / <c>AddAttribute</c> /
    /// <c>CloseElement</c>), not markup strings.
    /// </para>
    /// </summary>
    public required Action<WireframeElement, RenderTreeBuilder> RenderSvg { get; init; }

    /// <summary>True for built-in Tempo.Blazor components; false for custom/JSON-defined ones.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Identifier of the stencil pack that produced this definition, when applicable.</summary>
    public string? PackId { get; init; }

    /// <summary>Native renderer id for stencil components backed by registered C# renderers.</summary>
    public string? NativeType { get; init; }

    /// <summary>Runtime implementation metadata for stencil components that map to Blazor components.</summary>
    public StencilImpl? Impl { get; init; }

    /// <summary>
    /// Optional map from a <c>size</c> prop value to canonical element dimensions.
    /// When the user changes the <c>size</c> prop and the new value has an entry here,
    /// the canvas element is automatically resized to these dimensions.
    /// Components where <c>size</c> describes an internal sub-element (e.g. TmModal's dialog
    /// inside a full-screen overlay) should leave this null and handle sizing in their render lambda.
    /// </summary>
    public IReadOnlyDictionary<string, (double W, double H)>? SizePresets { get; init; }
}
