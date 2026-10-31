namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One slash command a composer offers after the reader types <c>/</c>. A command is a prompt template:
/// picking it inserts <see cref="Prompt"/> (or <c>/{Title}</c>) into the draft and records the command on
/// the next <see cref="AiComposerSubmit"/>, so the host knows which template the text belongs to.
/// </summary>
public sealed class AiSlashCommand
{
    private string _id = string.Empty;
    private string _title = string.Empty;

    /// <summary>Stable command identifier the host routes on.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Command name shown in the list and used for the <c>/{Title}</c> fallback insertion. Required.</summary>
    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    /// <summary>Optional one-line explanation rendered under the title.</summary>
    public string? Description { get; set; }

    /// <summary>Optional icon name (an <c>IconNames</c> constant or a host-registered icon).</summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Text inserted into the draft when the command is picked. Default <see langword="null"/> inserts
    /// <c>/{Title} </c>; a host whose command needs no visible token (it is fully described by the command
    /// itself) passes an empty string.
    /// </summary>
    public string? Prompt { get; set; }
}
