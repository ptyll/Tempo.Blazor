using System.Globalization;

namespace Tempo.Blazor.Demo.SharedUI.Layout;

/// <summary>
/// The culture the host resolved from the persisted preference before rendering. The host sets it;
/// the layout reapplies it inside the render context, because a culture set on the startup thread
/// does not reach Blazor's renderer.
/// </summary>
public static class DemoCulture
{
    public static CultureInfo? Applied { get; set; }
}
