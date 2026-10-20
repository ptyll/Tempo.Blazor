using Microsoft.Extensions.Localization;
using Tempo.Blazor.Resources;

namespace Tempo.Blazor.Localization;

/// <summary>
/// Default implementation of ITmLocalizer backed by IStringLocalizer&lt;TmResources&gt; — which
/// <see cref="JsonStringLocalizer{TResourceSource}"/> fulfils from the embedded JSON resources
/// (English default, Czech, Slovak and French available), resolving under both Server and WebAssembly.
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

    /// <summary>Precise lookup: the JSON localizer knows whether the key exists, so no placeholder guessing.</summary>
    public bool TryGetString(string key, out string value)
    {
        var localized = _localizer[key];
        value = localized.ResourceNotFound ? string.Empty : localized.Value;
        return !localized.ResourceNotFound;
    }
}
