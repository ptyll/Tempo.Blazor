using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Localization;

/// <summary>
/// An <c>IStringLocalizer&lt;TResourceSource&gt;</c> backed by JSON tables embedded in the resource
/// type's assembly: <c>{TResourceSource.FullName}.json</c> for the neutral language and
/// <c>{TResourceSource.FullName}.{culture}.json</c> per culture, each a flat
/// <c>{ "Key": "Value" }</c> map.
/// <para>
/// The tables travel with the assembly and are never resolved through a host-supplied
/// <see cref="IStringLocalizerFactory"/>, so an application that replaces the localization pipeline
/// with its own factory still gets real text instead of raw key names. Embedded JSON also resolves
/// under Blazor WebAssembly, where resx satellite assemblies and the embedded neutral
/// <c>.resources</c> are not loaded.
/// </para>
/// <para>
/// Lookup walks <see cref="CultureInfo.CurrentUICulture"/> → parent cultures → neutral; each table is
/// parsed at most once and a missing table is an empty table rather than a failure. A table entry that is
/// not a string is a packaging error and throws <see cref="InvalidDataException"/> naming the file and the
/// key at the first lookup, rather than rendering a number as a translation.
/// Register it as a CLOSED generic (<c>IStringLocalizer&lt;TMarker&gt;</c>) so it takes precedence over an
/// open-generic registration, and pass a marker type that lives in the assembly holding the JSON files.
/// </para>
/// </summary>
/// <typeparam name="TResourceSource">
/// Marker type whose assembly carries the JSON tables; its full name is the resource base name.
/// </typeparam>
public sealed class JsonStringLocalizer<TResourceSource> : IStringLocalizer<TResourceSource>
{
    private static readonly Assembly ResourceAssembly = typeof(TResourceSource).Assembly;
    private static readonly string BaseName = typeof(TResourceSource).FullName!;
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // culture name ("" = neutral, "cs", "fr", …) → (key → value); each file parsed at most once.
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase);

    // resolved most-specific → neutral lookup chains, keyed by CurrentUICulture.Name.
    private static readonly ConcurrentDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> Chains =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The localized value of <paramref name="name"/>, or the name itself with
    /// <see cref="LocalizedString.ResourceNotFound"/> set when no shipped table defines it.
    /// </summary>
    public LocalizedString this[string name]
    {
        get
        {
            var value = Find(name);
            return new LocalizedString(name, value ?? name, resourceNotFound: value is null, searchedLocation: BaseName);
        }
    }

    /// <summary>
    /// The localized value of <paramref name="name"/> formatted with <paramref name="arguments"/> under
    /// <see cref="CultureInfo.CurrentCulture"/>; an unknown name comes back as the name, unformatted,
    /// with <see cref="LocalizedString.ResourceNotFound"/> set.
    /// </summary>
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var format = Find(name);
            var value = format is null
                ? name
                : string.Format(CultureInfo.CurrentCulture, format, arguments);
            return new LocalizedString(name, value, resourceNotFound: format is null, searchedLocation: BaseName);
        }
    }

    /// <summary>
    /// Every key of the current culture's tables (most specific first, then its parents and the neutral
    /// table when <paramref name="includeParentCultures"/> is <see langword="true"/>), each key once.
    /// </summary>
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var chain = ChainFor(CultureInfo.CurrentUICulture);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var table in includeParentCultures ? chain : chain.Take(1))
        {
            foreach (var pair in table)
            {
                if (seen.Add(pair.Key))
                {
                    yield return new LocalizedString(pair.Key, pair.Value, resourceNotFound: false, searchedLocation: BaseName);
                }
            }
        }
    }

    private static string? Find(string name)
    {
        foreach (var table in ChainFor(CultureInfo.CurrentUICulture))
        {
            if (table.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> ChainFor(CultureInfo culture)
        => Chains.GetOrAdd(culture.Name, _ => BuildChain(culture));

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> BuildChain(CultureInfo culture)
    {
        var chain = new List<IReadOnlyDictionary<string, string>>();

        for (var ci = culture; ci is not null && !string.IsNullOrEmpty(ci.Name); ci = ci.Parent)
        {
            AddTable(chain, ci.Name);
            AddTable(chain, ci.TwoLetterISOLanguageName);
        }

        AddTable(chain, string.Empty); // neutral
        return chain;
    }

    private static void AddTable(List<IReadOnlyDictionary<string, string>> chain, string culture)
    {
        var table = Tables.GetOrAdd(culture, Load);
        if (table.Count > 0 && !chain.Contains(table))
        {
            chain.Add(table);
        }
    }

    private static IReadOnlyDictionary<string, string> Load(string culture)
    {
        var resourceName = string.IsNullOrEmpty(culture) ? $"{BaseName}.json" : $"{BaseName}.{culture}.json";

        using var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return Empty;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        // JsonDocument (not JsonSerializer.Deserialize) so the parse is reflection-free and survives
        // trimming under published Blazor WebAssembly.
        using var document = JsonDocument.Parse(buffer.ToArray());
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            // A table is a flat string map; a number/object/array/null entry is a packaging error. Fail
            // loudly (naming the file and the key) instead of rendering "42" as a translation, or letting
            // JsonElement.GetString() throw an opaque InvalidOperationException at the first lookup.
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"{resourceName}: '{property.Name}' must be a string");
            }

            map[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return map;
    }
}
