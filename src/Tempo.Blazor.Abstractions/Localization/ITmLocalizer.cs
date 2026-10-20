using System.Globalization;

namespace Tempo.Blazor.Localization;

/// <summary>
/// Localization abstraction for Tempo.Blazor components.
/// All built-in component strings go through this interface.
///
/// Registration: AddTempoBlazor() registers DefaultTmLocalizer automatically.
///
/// Override: Implement this interface and register it AFTER AddTempoBlazor() to
/// replace all component strings with your own translations:
/// <code>
/// builder.Services.AddTempoBlazor();
/// builder.Services.AddSingleton&lt;ITmLocalizer, MyCustomTmLocalizer&gt;();
/// </code>
///
/// Counts ("1 item", "2 items", "5 items") are localized with
/// <see cref="TmLocalizerPluralExtensions.Plural(ITmLocalizer, string, double, object[])"/>, which
/// needs nothing from an implementation: it reads the <c>Key_One</c>/<c>Key_Few</c>/<c>Key_Many</c>/
/// <c>Key_Other</c> entries through the indexer. An implementation may override
/// <see cref="Culture"/> and <see cref="TryGetString"/> to make that precise.
/// </summary>
public interface ITmLocalizer
{
    /// <summary>Gets a localized string by resource key.</summary>
    string this[string key] { get; }

    /// <summary>Gets a formatted localized string with arguments.</summary>
    string this[string key, params object[] arguments] { get; }

    /// <summary>
    /// The culture whose string table this localizer serves, and therefore whose plural rules apply.
    /// Defaults to <see cref="CultureInfo.CurrentUICulture"/> — the culture the built-in JSON
    /// localizer resolves strings with. A localizer bound to one fixed language (a test double, a
    /// per-tenant localizer) overrides it.
    /// </summary>
    CultureInfo Culture => CultureInfo.CurrentUICulture;

    /// <summary>
    /// Looks a key up without falling back to the key text. The default implementation reads the
    /// indexer and treats a result equal to the key (or to <c>[key]</c>, the placeholder some test
    /// doubles return) as "not found"; an implementation that knows precisely overrides it.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="value">The unformatted resource text when found.</param>
    /// <returns><c>true</c> when the key has a translation.</returns>
    bool TryGetString(string key, out string value)
    {
        var text = this[key];
        if (string.IsNullOrEmpty(text) || text == key || text == "[" + key + "]")
        {
            value = string.Empty;
            return false;
        }

        value = text;
        return true;
    }
}