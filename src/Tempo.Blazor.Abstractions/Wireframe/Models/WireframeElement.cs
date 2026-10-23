using System.Text.Json;

namespace Tempo.Blazor.Components.Wireframe.Models;

/// <summary>
/// A single UI element placed on the wireframe canvas.
/// The <see cref="Type"/> matches a registered <c>WireframeComponentDef.Type</c>
/// (e.g. "TmButton", "TmDataTable" or a custom component type).
/// </summary>
public sealed class WireframeElement
{
    /// <summary>Unique identifier (short Guid, e.g. "a3f8c21b").</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Component type identifier – must match a registered WireframeComponentDef.
    /// Examples: "TmButton", "TmTextInput", "MyCustomCard".
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Optional library-agnostic UI role requested during authoring, such as
    /// <c>search-input</c> or <c>otp-input</c>.
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Optional human-readable name of the element (for example "Login button"). Purely descriptive and
    /// additive: it is shown in the inspector, used as the element's accessible name and tooltip on the
    /// canvas and returned by the MCP tools. <c>null</c> (omitted from JSON) means "unnamed".
    /// </summary>
    public string? Name { get; set; }

    /// <summary>X position of the element's top-left corner in pixels.</summary>
    public double X { get; set; }

    /// <summary>Y position of the element's top-left corner in pixels.</summary>
    public double Y { get; set; }

    /// <summary>Width in pixels.</summary>
    public double W { get; set; } = 120;

    /// <summary>Height in pixels.</summary>
    public double H { get; set; } = 36;

    /// <summary>
    /// Component properties keyed by camelCase prop name.
    /// Values are stored as <see cref="JsonElement"/> to support any JSON type
    /// (string, number, bool, array, object).
    /// </summary>
    public Dictionary<string, JsonElement> Props { get; set; } = [];

    /// <summary>Stacking order. Higher value = rendered on top.</summary>
    public int ZIndex { get; set; }

    /// <summary>Optional group identifier for grouping elements together.</summary>
    public string? GroupId { get; set; }

    /// <summary>
    /// When <c>true</c>, the element cannot be moved, resized, or deleted.
    /// Provides a local editing lock independent of <see cref="LockedBy"/>.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>Rotation angle in degrees. 0 = no rotation.</summary>
    public double Rotation { get; set; }

    /// <summary>Optional layer identifier. When null, the element belongs to the default layer.</summary>
    public string? LayerId { get; set; }

    /// <summary>Reserved for future collaborative editing – who has this element locked.</summary>
    public string? LockedBy { get; set; }
}
