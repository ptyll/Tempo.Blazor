using System.Globalization;
using Microsoft.Extensions.Localization;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Wraps the built-in localizer and rewords only the keys the application cares about;
/// everything else falls through to the shipped resources (en/cs/sk/fr) untouched.
/// </summary>
public sealed class AppTranslationOverrideLocalizer(IStringLocalizer<TmResources> builtIn) : ITmLocalizer
{
    private readonly Dictionary<string, string> _overrides = new()
    {
        ["TmDataTable_SaveView"] = "Uložit pohled jako…",
        ["Tm_Search"] = "Hledat…",
        ["TmModelingIssuePanel_Count"] = "Celkem nálezů: {0}",
    };

    public CultureInfo Culture => CultureInfo.CurrentUICulture;

    public string this[string key] =>
        _overrides.TryGetValue(key, out var value) ? value : builtIn[key].Value;

    public string this[string key, params object[] arguments] =>
        _overrides.TryGetValue(key, out var value)
            ? string.Format(Culture, value, arguments)
            : builtIn[key, arguments].Value;
}
