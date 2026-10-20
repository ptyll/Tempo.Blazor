using System.Collections;
using System.Text.Json;
using Scriban.Runtime;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Converts an arbitrary model into a Scriban <see cref="ScriptObject"/>. Dictionaries become script
/// objects keyed by their entries (so JSON-derived models work), lists are converted element-wise, and
/// POCO/anonymous objects are imported with the explicit standard snake_case member renamer. The import
/// exposes readable fields and properties only — never methods — and runs the sandbox member policy, so
/// a blocked name cannot even enter the script scope. Nested POCOs stay live CLR values and are read
/// through the same policy.
/// </summary>
public static class ObjectToScriptObjectConverter
{
    private const ScriptMemberImportFlags ImportedMembers =
        ScriptMemberImportFlags.Field | ScriptMemberImportFlags.Property;

    /// <summary>Builds a script object usable as the global scope for rendering.</summary>
    public static ScriptObject ToScriptObject(object? model)
    {
        var scriptObject = new ScriptObject(StringComparer.Ordinal);
        switch (model)
        {
            case null:
                break;
            case JsonElement { ValueKind: JsonValueKind.Object } json:
                foreach (var property in json.EnumerateObject()) scriptObject[property.Name] = Convert(property.Value);
                break;
            case IDictionary<string, object?> typed:
                foreach (var (key, value) in typed) scriptObject[key] = Convert(value);
                break;
            case IDictionary loose:
                foreach (DictionaryEntry entry in loose)
                    scriptObject[System.Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture)!] = Convert(entry.Value);
                break;
            default:
                scriptObject.Import(
                    model,
                    ImportedMembers,
                    SandboxMemberPolicy.IsMemberAllowed,
                    StandardMemberRenamer.Default);
                break;
        }
        return scriptObject;
    }

    private static object? Convert(object? value) => value switch
    {
        null => null,
        JsonElement json => ConvertJson(json),
        string s => s,
        // Scriban would invoke a delegate value as a function; data models must not carry host code.
        Delegate => null,
        IDictionary<string, object?> or IDictionary => ToScriptObject(value),
        IEnumerable enumerable => enumerable.Cast<object?>().Select(Convert).ToList(),
        _ => value,
    };

    // Normalizes System.Text.Json values (from a deserialized JSON model) into plain CLR objects
    // and script objects that Scriban can iterate and access.
    private static object? ConvertJson(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.Object => ToScriptObject(json),
        JsonValueKind.Array => json.EnumerateArray().Select(e => Convert(e)).ToList(),
        JsonValueKind.String => json.GetString(),
        JsonValueKind.Number => json.TryGetInt64(out var l) ? l : json.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
