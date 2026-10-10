namespace Tempo.Blazor.Localization;

/// <summary>
/// Plural-aware lookups on <see cref="ITmLocalizer"/>: <c>Loc.Plural("TmFoo_Count", n)</c>.
/// </summary>
/// <remarks>
/// The resource families are written as <c>Key_One</c>, <c>Key_Few</c>, <c>Key_Many</c> and
/// <c>Key_Other</c> and each language provides the forms its grammar needs (see
/// <see cref="TmPluralRules"/>). The lookup order is: the form for the count, then <c>Key_Other</c>,
/// then the plain <c>Key</c> — so a key that has no plural family keeps working (and a host that only
/// translates <c>Key</c> is not broken), and a language that omits a rarely used form falls back to
/// <c>Other</c>. The count is always template argument <c>{0}</c>.
/// </remarks>
public static class TmLocalizerPluralExtensions
{
    /// <summary>Resolves the plural form of <paramref name="key"/> for <paramref name="count"/> and formats it.</summary>
    /// <param name="localizer">The localizer; its <see cref="ITmLocalizer.Culture"/> selects the plural rules.</param>
    /// <param name="key">The base resource key (without a plural suffix).</param>
    /// <param name="count">The counted number; it is argument <c>{0}</c> of the template.</param>
    /// <param name="extraArguments">Further template arguments, <c>{1}</c> onwards.</param>
    /// <returns>The formatted text; what the indexer returns for <paramref name="key"/> when no form exists.</returns>
    public static string Plural(this ITmLocalizer localizer, string key, double count, params object[] extraArguments)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(key);

        var arguments = new object[1 + (extraArguments?.Length ?? 0)];
        arguments[0] = count;
        extraArguments?.CopyTo(arguments, 1);

        var category = TmPluralRules.Select(localizer.Culture, count);
        var resolved = category != TmPluralCategory.Other && localizer.TryGetString(key + Suffix(category), out _)
            ? key + Suffix(category)
            : localizer.TryGetString(key + "_Other", out _)
                ? key + "_Other"
                : key;

        return localizer[resolved, arguments];
    }

    private static string Suffix(TmPluralCategory category) => category switch
    {
        TmPluralCategory.One => "_One",
        TmPluralCategory.Few => "_Few",
        TmPluralCategory.Many => "_Many",
        _ => "_Other",
    };
}