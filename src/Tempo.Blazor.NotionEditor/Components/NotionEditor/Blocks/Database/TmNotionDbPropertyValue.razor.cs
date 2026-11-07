using System.Collections;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

/// <summary>
/// Read-only renderer for one database field value (DB-2). It sits on top of
/// <see cref="NotionDatabaseValueFormatter"/> and adds the presentation the plain text lost: a
/// colored pill for Select/Status/MultiSelect, initials for a person, a check for a checkbox and
/// nothing for an empty value.
/// </summary>
public partial class TmNotionDbPropertyValue
{
    /// <summary>The field the value belongs to. Drives the presentation.</summary>
    [Parameter, EditorRequired] public IDatabaseField? Field { get; set; }

    /// <summary>The raw stored value. Null or empty renders nothing.</summary>
    [Parameter] public object? Value { get; set; }

    /// <summary>Date format passed to the shared formatter. Defaults to a short date.</summary>
    [Parameter] public string DateFormat { get; set; } = "yyyy-MM-dd";

    private string Text => NotionDatabaseValueFormatter.Format(Value, DateFormat);

    private bool Checked => Value switch
    {
        bool b                                   => b,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        _                                        => false
    };

    private IEnumerable<(string Name, string? Color)> Pills
    {
        get
        {
            foreach (var name in Names())
            {
                if (name.Length == 0) continue;
                yield return (name, ColorOf(name));
            }
        }
    }

    private IEnumerable<string> Names()
    {
        if (Value is null) yield break;
        if (Value is string s)
        {
            if (Field?.Type == DatabaseFieldType.MultiSelect)
            {
                foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    yield return part;
            }
            else
            {
                yield return s;
            }
            yield break;
        }

        if (Value is IEnumerable many and not string)
        {
            foreach (var item in many)
            {
                var formatted = NotionDatabaseValueFormatter.Format(item, DateFormat);
                if (formatted.Length > 0) yield return formatted;
            }
        }
    }

    private string? ColorOf(string name)
    {
        var options = Field?.Config switch
        {
            ISelectFieldConfig select => select.Options,
            IStatusFieldConfig status => status.Groups.SelectMany(g => g.Options),
            _                         => null
        };
        return options?.FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase))?.Color;
    }

    private static string PillBackground(string? color) =>
        string.IsNullOrEmpty(color) ? "var(--tm-bg-muted)" : NotionColorTokens.OptionWash(color);

    private static string PillForeground(string? color) =>
        string.IsNullOrEmpty(color) ? "var(--tm-text-secondary)" : NotionColorTokens.OptionInk(color);

    private static string AvatarColor(string name) => NotionColorTokens.OptionSolid(AvatarName(name));

    /// <summary>A palette name for an avatar, stable across processes (not <see cref="string.GetHashCode"/>).</summary>
    public static string AvatarName(string seed)
    {
        var names = new[] { "blue", "green", "yellow", "red", "purple", "gray", "orange", "brown", "pink" };
        return names[StableHash(seed) % names.Length];
    }

    internal static int StableIndex(string seed, int length) => StableHash(seed) % length;

    private static int StableHash(string seed)
    {
        var hash = 17;
        foreach (var ch in seed)
            hash = unchecked(hash * 31 + ch);
        return hash & int.MaxValue;
    }

    private static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpper(parts[0][0], CultureInfo.InvariantCulture)}{char.ToUpper(parts[^1][0], CultureInfo.InvariantCulture)}"
            : name.Length >= 2 ? name[..2].ToUpper(CultureInfo.InvariantCulture) : name.ToUpper(CultureInfo.InvariantCulture);
    }
}
