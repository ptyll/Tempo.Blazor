using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.NotionEditor.Enums;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

/// <summary>
/// The one icon map for a database field type (DB-2). The six inline SVG copies it replaces all
/// rendered through <c>TmIcon</c> now, so a new field type fails the icon test instead of falling
/// through to a text glyph in one view and nothing in another.
/// </summary>
public static class DatabaseFieldIcons
{
    /// <summary>The <see cref="IconNames"/> glyph for <paramref name="type"/>.</summary>
    public static string Get(DatabaseFieldType type) => type switch
    {
        DatabaseFieldType.Text           => IconNames.Type,
        DatabaseFieldType.Number         => IconNames.Hash,
        DatabaseFieldType.Select         => IconNames.ChevronDown,
        DatabaseFieldType.MultiSelect    => IconNames.List,
        DatabaseFieldType.Status         => IconNames.Circle,
        DatabaseFieldType.Date           => IconNames.Calendar,
        DatabaseFieldType.DateRange      => IconNames.Calendar,
        DatabaseFieldType.Person         => IconNames.User,
        DatabaseFieldType.Files          => IconNames.Paperclip,
        DatabaseFieldType.Checkbox       => IconNames.CheckSquare,
        DatabaseFieldType.Url            => IconNames.Link,
        DatabaseFieldType.Email          => IconNames.Mail,
        DatabaseFieldType.Phone          => IconNames.Phone,
        DatabaseFieldType.Formula        => IconNames.Sigma,
        DatabaseFieldType.Relation       => IconNames.ArrowRight,
        DatabaseFieldType.Rollup         => IconNames.Layers,
        DatabaseFieldType.CreatedTime    => IconNames.Clock,
        DatabaseFieldType.CreatedBy      => IconNames.UserPlus,
        DatabaseFieldType.LastEditedTime => IconNames.History,
        DatabaseFieldType.LastEditedBy   => IconNames.UserCheck,
        _                                => IconNames.Type
    };
}
