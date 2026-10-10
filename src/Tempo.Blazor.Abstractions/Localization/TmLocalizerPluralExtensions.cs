namespace Tempo.Blazor.Localization;

/// <summary>Plural-aware lookups on <see cref="ITmLocalizer"/>.</summary>
public static class TmLocalizerPluralExtensions
{
    /// <summary>Resolves the plural form of <paramref name="key"/> for <paramref name="count"/> and formats it.</summary>
    /// <param name="localizer">The localizer.</param>
    /// <param name="key">The base resource key (without a plural suffix).</param>
    /// <param name="count">The counted number; it is argument <c>{0}</c> of the template.</param>
    /// <param name="extraArguments">Further template arguments, <c>{1}</c> onwards.</param>
    public static string Plural(this ITmLocalizer localizer, string key, double count, params object[] extraArguments)
        => throw new NotImplementedException();
}