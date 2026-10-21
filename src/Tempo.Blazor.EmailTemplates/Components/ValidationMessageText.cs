using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Localization;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// Renders one validation/import sentence. Most rules are a plain key, but a rule that MEASURES a threshold
/// carries it as <c>{0}</c> — calling the one-argument localizer indexer on such a key prints a literal
/// „{0}“ in the panel and in the import dialog, so every such key is resolved through this ONE place.
/// </summary>
internal static class ValidationMessageText
{
    /// <summary>Resolves the localized sentence of <paramref name="key"/>, formatting the rule's threshold.</summary>
    /// <param name="loc">The package localizer.</param>
    /// <param name="key">The validation key (e.g. <c>validation.preheader.too_long</c>).</param>
    /// <returns>The localized sentence, with the threshold substituted where the rule names one.</returns>
    internal static string Of(ITmEmailLocalizer loc, string key)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentException.ThrowIfNullOrEmpty(key);

        return string.Equals(key, DocumentValidationKeys.PreheaderTooLong, StringComparison.Ordinal)
            ? loc[key, EmailDocumentValidator.MaxPreheaderLength]
            : loc[key];
    }
}
