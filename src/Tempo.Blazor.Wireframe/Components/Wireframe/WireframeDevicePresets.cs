namespace Tempo.Blazor.Components.Wireframe;

/// <summary>One device canvas preset offered by the inspector's "Device preset" select.</summary>
/// <param name="Id">Stable option value (<c>desktop</c>, <c>iphone14</c>, <c>custom</c>, …).</param>
/// <param name="Width">Canvas width in pixels; <c>null</c> for the size-less Custom entry.</param>
/// <param name="Height">Canvas height in pixels; <c>null</c> for the size-less Custom entry.</param>
/// <param name="NameKey">Localization key of the preset's display name.</param>
internal sealed record WireframeDevicePreset(string Id, double? Width, double? Height, string NameKey)
{
    /// <summary>True for the size-less "Custom" entry (width and height are typed by the user).</summary>
    public bool IsCustom => Width is null || Height is null;
}

/// <summary>Static list of the built-in device canvas presets (no model, no persistence).</summary>
internal static class WireframeDevicePresets
{
    /// <summary>Id of the "Custom" entry.</summary>
    public const string CustomId = "custom";

    /// <summary>All presets in display order; Custom is last.</summary>
    public static IReadOnlyList<WireframeDevicePreset> All { get; } =
    [
        new("desktop",  1440, 900, "TmWireframeProps_Device_Desktop"),
        new("laptop",   1280, 800, "TmWireframeProps_Device_Laptop"),
        new("tablet",   1024, 768, "TmWireframeProps_Device_Tablet"),
        new("iphone14", 390,  844, "TmWireframeProps_Device_Iphone14"),
        new("android",  360,  800, "TmWireframeProps_Device_Android"),
        new(CustomId,   null, null, "TmWireframeProps_Device_Custom"),
    ];

    /// <summary>Finds a preset by id (case-insensitive); <c>null</c> when unknown.</summary>
    public static WireframeDevicePreset? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The sized preset whose dimensions equal the given size (within half a pixel); <c>null</c> = Custom.</summary>
    public static WireframeDevicePreset? Match(double width, double height) =>
        All.FirstOrDefault(p => !p.IsCustom
            && Math.Abs(p.Width!.Value - width) < 0.5
            && Math.Abs(p.Height!.Value - height) < 0.5);
}
