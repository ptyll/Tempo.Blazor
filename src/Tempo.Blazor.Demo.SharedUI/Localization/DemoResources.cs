using System.Globalization;

namespace Tempo.Blazor.Demo.SharedUI.Localization;

/// <summary>
/// Marker type whose assembly embeds the demo JSON resource tables
/// (<c>Resources/DemoResources.json</c> neutral English plus <c>.cs</c>/<c>.sk</c>/<c>.fr</c>
/// cultures, each a flat <c>{ "Key": "Value" }</c> map). The tables carry the demo-only keys
/// (<c>TmDemo_*</c> and the <c>*Demo_*</c> page headings) that used to live in the library's
/// <c>TmResources</c> and are served by <see cref="DemoTmLocalizer"/>.
/// </summary>
internal sealed class DemoResources
{
    private DemoResources()
    {
    }
}
