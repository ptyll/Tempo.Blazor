using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Resources;

namespace Tempo.Blazor.Demo.SharedUI.Localization;

/// <summary>
/// The demo applications' <see cref="ITmLocalizer"/> decorator: demo-only keys (the
/// <c>TmDemo_*</c> and <c>*Demo_*</c> page strings) resolve from the embedded
/// <c>DemoResources*.json</c> tables of this assembly, every other key falls through to the
/// library's built-in <c>TmResources</c> (en/cs/sk/fr) untouched — a component page only ever
/// injects one <c>ITmLocalizer</c> and cannot tell the two tables apart.
/// </summary>
/// <remarks>
/// Register after <c>AddTempoBlazor()</c> via
/// <see cref="DemoLocalizationServiceCollectionExtensions.AddTempoDemoLocalizer"/> in every demo
/// host (WASM, Server and both InteractiveAuto sides). The lookup mirrors the library's
/// <c>JsonStringLocalizer</c>: <c>CurrentUICulture</c> → parent cultures → neutral English for
/// selection, and <c>CurrentCulture</c> for formatting format-string arguments.
/// </remarks>
public sealed class DemoTmLocalizer(IStringLocalizer<TmResources> builtIn) : ITmLocalizer
{
    private const string ResourceBaseName = "Tempo.Blazor.Demo.SharedUI.Resources.DemoResources";

    private static readonly Assembly ResourceAssembly = typeof(DemoResources).Assembly;

    // culture name ("" = neutral) → (key → value); each file parsed at most once.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Precise lookup over the demo tables, then the library tables.</summary>
    public bool TryGetString(string key, out string value)
    {
        if (Find(key) is { } demoValue)
        {
            value = demoValue;
            return true;
        }

        var localized = builtIn[key];
        value = localized.ResourceNotFound ? string.Empty : localized.Value;
        return !localized.ResourceNotFound;
    }

    public string this[string key] =>
        Find(key) is { } value ? value : builtIn[key].Value;

    public string this[string key, params object[] arguments] =>
        Find(key) is { } value
            ? string.Format(CultureInfo.CurrentCulture, value, arguments)
            : builtIn[key, arguments].Value;

    private static string? Find(string key)
    {
        for (var culture = CultureInfo.CurrentUICulture;
             culture is not null && !string.IsNullOrEmpty(culture.Name);
             culture = culture.Parent)
        {
            if (Table(culture.Name).TryGetValue(key, out var value))
            {
                return value;
            }

            if (Table(culture.TwoLetterISOLanguageName).TryGetValue(key, out value))
            {
                return value;
            }
        }

        return Table(string.Empty).TryGetValue(key, out var neutral) ? neutral : null;
    }

    private static IReadOnlyDictionary<string, string> Table(string culture)
        => Tables.GetOrAdd(culture, Load);

    private static IReadOnlyDictionary<string, string> Load(string culture)
    {
        var resourceName = string.IsNullOrEmpty(culture)
            ? $"{ResourceBaseName}.json"
            : $"{ResourceBaseName}.{culture}.json";

        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return Empty;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        // JsonDocument (not JsonSerializer.Deserialize) so the parse is reflection-free and
        // survives trimming under published Blazor WebAssembly, like the library localizer.
        using var document = JsonDocument.Parse(buffer.ToArray());
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            map[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return map;
    }
}
