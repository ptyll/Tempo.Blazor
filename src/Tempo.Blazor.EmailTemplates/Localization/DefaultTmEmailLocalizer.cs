using Microsoft.Extensions.Localization;
using Tempo.Blazor.EmailTemplates.Resources;

namespace Tempo.Blazor.EmailTemplates.Localization;

/// <summary>
/// Default <see cref="ITmEmailLocalizer"/> over the embedded <c>TmEmailResources*</c> JSON tables —
/// never over the host's <see cref="IStringLocalizerFactory"/>, so replacing that factory cannot turn
/// editor text into raw keys.
/// </summary>
internal sealed class DefaultTmEmailLocalizer : ITmEmailLocalizer
{
    private readonly IStringLocalizer<TmEmailResources> _localizer;

    public DefaultTmEmailLocalizer(IStringLocalizer<TmEmailResources> localizer) => _localizer = localizer;

    public string this[string key] => _localizer[key].Value;

    public string this[string key, params object[] arguments] => _localizer[key, arguments].Value;
}
