using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Diagram.Templates;

/// <summary>
/// Display-name helpers for diagram templates and their categories. Built-in templates are
/// authored with English names (index.json / provider catalogs); the localized display text
/// resolves through a resource key derived from the template <see cref="DiagramTemplate.Id"/>
/// (and from the category name), falling back to the raw value when the key is untranslated —
/// the <see cref="DiagramTemplate"/> model itself stays unchanged.
/// </summary>
public static class DiagramTemplateDisplayNames
{
    /// <summary>Prefix of the per-template name keys, followed by the sanitized template id.</summary>
    public const string NameKeyPrefix = "TmDiagramTemplate_";

    /// <summary>Prefix of the category keys, followed by the sanitized category name.</summary>
    public const string CategoryKeyPrefix = "TmDiagramTemplateCategory_";

    /// <summary>
    /// Returns the localized display name for <paramref name="template"/>: the translation of
    /// the key derived from <see cref="DiagramTemplate.Id"/> when it resolves, otherwise the
    /// template's raw name.
    /// </summary>
    public static string GetTemplateName(DiagramTemplate template, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(localizer);
        return GetLocalizedText(NameKeyPrefix + Sanitize(template.Id), template.Name, localizer);
    }

    /// <summary>
    /// Returns the localized category label: the translation of the key derived from
    /// <paramref name="category"/> when it resolves, otherwise the raw category name.
    /// </summary>
    public static string GetCategoryName(string category, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return GetLocalizedText(CategoryKeyPrefix + Sanitize(category), category, localizer);
    }

    /// <summary>
    /// Maps a template id or category to its resource-key fragment: letters and digits stay,
    /// every other character becomes an underscore ("uml-class" → "uml_class").
    /// </summary>
    public static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Resolves a localized text for <paramref name="resourceKey"/>, falling back to
    /// <paramref name="fallback"/> when the key is empty or untranslated. Untranslated is
    /// decided by <see cref="ITmLocalizer.TryGetString"/>: localizers may echo the missing key
    /// either bare (production default) or bracketed as <c>[key]</c> (test doubles) — both
    /// count as missing.
    /// </summary>
    public static string GetLocalizedText(string? resourceKey, string fallback, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return fallback;
        }

        return localizer.TryGetString(resourceKey, out var localized)
            ? localized
            : fallback;
    }
}
