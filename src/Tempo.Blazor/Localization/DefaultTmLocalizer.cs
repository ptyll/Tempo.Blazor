using Microsoft.Extensions.Localization;
using Tempo.Blazor.Resources;

namespace Tempo.Blazor.Localization;

/// <summary>
/// Default implementation of ITmLocalizer backed by IStringLocalizer&lt;TmResources&gt;.
/// Uses the embedded .resx files in Tempo.Blazor (English default, Czech available).
/// </summary>
internal sealed class DefaultTmLocalizer : ITmLocalizer
{
    private readonly IStringLocalizer<TmResources> _localizer;

    public DefaultTmLocalizer(IStringLocalizer<TmResources> localizer)
    {
        _localizer = localizer;
    }

    public string this[string key] => _localizer[key].Value;

    public string this[string key, params object[] arguments] => _localizer[key, arguments].Value;
}
