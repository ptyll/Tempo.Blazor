using System.Globalization;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Phase 5 review (A12 + U10): the ONE reader of a property issue's <c>Id</c> and the ONE localizer of
/// a shared-validator message. <see cref="ModelingPropertySchemaValidator"/> writes
/// <c>Id = "&lt;code&gt;:&lt;path&gt;"</c> (the MCP-facing identity); the EDITOR stamps the owning
/// item's id onto it (<c>"&lt;code&gt;:&lt;path&gt;@&lt;ownerId&gt;"</c>) so two elements that both
/// miss the same required field carry DISTINCT issue identities, which the dock's list keys and the
/// inspector's matching both need.
/// <para>
/// The English <see cref="ModelingIssueDto.Message"/> stays the wire text (MCP parity, writer host
/// messages); the INSPECTOR localizes a coded issue for the user, and the editor localizes it for the
/// dock row. A host-authored (writer) issue carries no code and keeps its own message verbatim.
/// </para>
/// </summary>
internal static class ModelingPropertyIssueText
{
    /// <summary>Separator between the validator code and the path.</summary>
    internal const string CodeSeparator = ":";

    /// <summary>Separator between the path and the owning element/relationship id.</summary>
    internal const string OwnerSeparator = "@";

    /// <summary>The validator code (<c>required</c>, <c>type</c>, …) or <c>null</c> for a
    /// host-authored issue.</summary>
    internal static string? CodeOf(string? issueId)
    {
        if (string.IsNullOrEmpty(issueId))
        {
            return null;
        }

        var separator = issueId.IndexOf(CodeSeparator, StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var candidate = issueId[..separator];
        return candidate is "required" or "type" or "range" or "length" or "pattern" or "enum" or "reference" or "unknown"
            ? candidate
            : null;
    }

    /// <summary>The property path of a coded issue (<c>key</c> or <c>key[i].itemKey</c>), with the
    /// editor's <c>@owner</c> suffix stripped; empty for a host-authored issue.</summary>
    internal static string PathOf(string? issueId)
    {
        if (string.IsNullOrEmpty(issueId) || CodeOf(issueId) is null)
        {
            return string.Empty;
        }

        var separator = issueId.IndexOf(CodeSeparator, StringComparison.Ordinal);
        if (separator < 0 || separator + 1 >= issueId.Length)
        {
            return string.Empty;
        }

        var path = issueId[(separator + 1)..];
        var owner = path.IndexOf(OwnerSeparator, StringComparison.Ordinal);
        return owner < 0 ? path : path[..owner];
    }

    /// <summary>The top-level property key of a path (<c>key[0].item</c> → <c>key</c>).</summary>
    internal static string TopLevelKey(string path)
    {
        var cut = path.IndexOfAny(['[', '.']);
        return cut < 0 ? path : path[..cut];
    }

    /// <summary>The schema field a path names (the top-level field; item paths resolve to their
    /// list field), or <c>null</c> when the schema does not declare it.</summary>
    internal static ModelingPropertyFieldDto? FindField(ModelingPropertySchemaDto? schema, string path)
    {
        var key = TopLevelKey(path);
        if (schema is null || key.Length == 0)
        {
            return null;
        }

        return schema.Sections
            .SelectMany(section => section.Fields)
            .FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.Ordinal));
    }

    /// <summary>The localized message of a coded issue; a host-authored issue keeps its own text.</summary>
    internal static string Localize(
        ITmLocalizer loc,
        ModelingIssueDto issue,
        ModelingPropertyFieldDto? field)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(issue);

        var code = CodeOf(issue.Id);
        if (code is null || field is null)
        {
            return issue.Message;
        }

        var label = string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label;
        return code switch
        {
            "required" => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldRequired"], label),
            "type" => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldType"], label),
            "range" => Range(loc, field, label),
            "length" => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldLength"], label, field.MaxLength ?? 0),
            "pattern" => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldPattern"], label),
            "enum" => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldEnum"], label),
            "reference" => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldReference"], label),
            _ => issue.Message
        };
    }

    private static string Range(ITmLocalizer loc, ModelingPropertyFieldDto field, string label)
    {
        var min = field.Min?.ToString("0.###", CultureInfo.CurrentUICulture);
        var max = field.Max?.ToString("0.###", CultureInfo.CurrentUICulture);
        return (field.Min, field.Max) switch
        {
            (not null, not null) => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldRangeBoth"], label, min, max),
            (not null, null) => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldRangeMin"], label, min),
            _ => string.Format(CultureInfo.CurrentUICulture, loc["TmModelingInspector_FieldRangeMax"], label, max)
        };
    }
}
